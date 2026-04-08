using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace CodeFlow.Storage.Providers
{
    public class MinioStorageProvider
    {
        private readonly IMinioClient _client;
        private readonly string _bucket;
        private readonly string _prefix; // repo-scoped prefix e.g. "org/repo/"

        public MinioStorageProvider(string endpointUrl, string bucket, string accessKey, string secretKey, string prefix = "")
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
                throw new ArgumentException("Endpoint URL is required", nameof(endpointUrl));

            var uri = new Uri(endpointUrl);
            var builder = new MinioClient()
                .WithEndpoint(uri.Authority)
                .WithCredentials(accessKey, secretKey);

            if (uri.Scheme == Uri.UriSchemeHttps)
                builder.WithSSL();

            _client = builder.Build();
            _bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
            _prefix = string.IsNullOrEmpty(prefix) ? "" : prefix.TrimEnd('/') + "/";

            EnsureBucketExistsAsync().GetAwaiter().GetResult();
        }

        private string Key(string objectName) => _prefix + objectName;

        // ─── Bucket lifecycle ─────────────────────────────────────────────────

        private async Task EnsureBucketExistsAsync()
        {
            var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket));
            if (!exists)
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket));
        }

        // ─── Core operations ──────────────────────────────────────────────────

        public async Task UploadAsync(string objectName, byte[] data, CancellationToken ct = default)
        {
            using var ms = new MemoryStream(data);
            var args = new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(Key(objectName))
                .WithStreamData(ms)
                .WithObjectSize(data.Length)
                .WithContentType("application/octet-stream");
            await _client.PutObjectAsync(args, ct);
        }

        public async Task UploadAsync(string objectName, Stream data, CancellationToken ct = default)
        {
            if (data.CanSeek) data.Position = 0;
            var args = new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(Key(objectName))
                .WithStreamData(data)
                .WithObjectSize(data.Length)
                .WithContentType("application/octet-stream");
            await _client.PutObjectAsync(args, ct);
        }

        public async Task<byte[]?> DownloadAsync(string objectName, CancellationToken ct = default)
        {
            try
            {
                using var ms = new MemoryStream();
                var args = new GetObjectArgs()
                    .WithBucket(_bucket)
                    .WithObject(Key(objectName))
                    .WithCallbackStream(s => s.CopyTo(ms));
                await _client.GetObjectAsync(args, ct);
                return ms.ToArray();
            }
            catch (ObjectNotFoundException) { return null; }
        }

        public async Task<bool> ExistsAsync(string objectName, CancellationToken ct = default)
        {
            try
            {
                var args = new StatObjectArgs().WithBucket(_bucket).WithObject(Key(objectName));
                await _client.StatObjectAsync(args, ct);
                return true;
            }
            catch { return false; }
        }

        public async Task DeleteAsync(string objectName, CancellationToken ct = default)
        {
            var args = new RemoveObjectArgs().WithBucket(_bucket).WithObject(Key(objectName));
            await _client.RemoveObjectAsync(args, ct);
        }

        // ─── Listing (for differential push) ─────────────────────────────────

        public async Task<HashSet<string>> ListObjectKeysAsync(CancellationToken ct = default)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var args = new ListObjectsArgs()
                    .WithBucket(_bucket)
                    .WithPrefix(_prefix)
                    .WithRecursive(true);
                await foreach (var item in _client.ListObjectsEnumAsync(args, ct))
                {
                    if (!string.IsNullOrEmpty(item.Key))
                    {
                        // Strip prefix to get bare object name
                        var bare = item.Key.StartsWith(_prefix)
                            ? item.Key.Substring(_prefix.Length)
                            : item.Key;
                        keys.Add(bare);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MinIO] List error: {ex.Message}");
            }
            return keys;
        }

        // ─── Parallel differential push ───────────────────────────────────────

        public async Task<(int uploaded, int skipped, int failed)> DifferentialPushAsync(
            IEnumerable<(string hash, byte[] data)> objects,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var remoteKeys = await ListObjectKeysAsync(ct);

            int uploaded = 0, skipped = 0, failed = 0;

            await Parallel.ForEachAsync(objects, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
                async (obj, token) =>
                {
                    if (remoteKeys.Contains(obj.hash))
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }
                    try
                    {
                        await UploadAsync(obj.hash, obj.data, token);
                        Interlocked.Increment(ref uploaded);
                        progress?.Report($"  ↑ {obj.hash[..12]}  ({obj.data.Length:N0} bytes)");
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        progress?.Report($"  ✗ {obj.hash[..12]}  {ex.Message}");
                    }
                });

            return (uploaded, skipped, failed);
        }
    }
}
