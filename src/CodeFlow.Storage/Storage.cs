using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CodeFlow.Storage
{
    // ─── BlobStore ────────────────────────────────────────────────────────────

    public class BlobStore
    {
        private readonly ContentAddressableStore _cas;

        public BlobStore(string objectsPath)
        {
            _cas = new ContentAddressableStore(objectsPath);
        }

        public string SaveBlob(byte[] content) => _cas.SaveObjectBytes(content);

        public byte[]? GetBlob(string hash) => _cas.GetRawObject(hash);

        public bool HasBlob(string hash) => _cas.HasObject(hash);

        internal ContentAddressableStore CAS => _cas;
    }

    // ─── Tree ─────────────────────────────────────────────────────────────────

    public class Tree
    {
        public List<TreeEntry> Entries { get; set; } = new();

        private static readonly JsonSerializerOptions _opts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public string ToJson() => JsonSerializer.Serialize(this, _opts);

        public static Tree? FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<Tree>(json, _opts); }
            catch { return null; }
        }

        public class TreeEntry
        {
            public string Path { get; set; } = "";
            public string Hash { get; set; } = "";
            public string Type { get; set; } = "blob";   // "blob" | "tree"
            public long Size { get; set; }
        }
    }

    // ─── TreeStore ────────────────────────────────────────────────────────────

    public class TreeStore
    {
        private readonly BlobStore _blobStore;

        public TreeStore(BlobStore blobStore) { _blobStore = blobStore; }

        public string SaveTree(Tree tree)
        {
            // Sort entries for canonical representation
            tree.Entries = tree.Entries.OrderBy(e => e.Path).ToList();
            var bytes = Encoding.UTF8.GetBytes(tree.ToJson());
            return _blobStore.SaveBlob(bytes);
        }

        public Tree? LoadTree(string? treeHash)
        {
            if (string.IsNullOrEmpty(treeHash)) return null;
            var bytes = _blobStore.GetBlob(treeHash);
            if (bytes == null) return null;
            return Tree.FromJson(Encoding.UTF8.GetString(bytes));
        }

        /// <summary>Recursively collect all file paths in a tree.</summary>
        public Dictionary<string, string> FlattenTree(string treeHash)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var tree = LoadTree(treeHash);
            if (tree == null) return result;
            foreach (var entry in tree.Entries)
                result[entry.Path] = entry.Hash;
            return result;
        }
    }

    // ─── Index (Staging area) ─────────────────────────────────────────────────

    public class Index
    {
        private readonly string _indexPath;
        private static readonly JsonSerializerOptions _opts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public Index(string repoRoot)
        {
            var dir = Path.Combine(repoRoot, ".codeflow");
            Directory.CreateDirectory(dir);
            _indexPath = Path.Combine(dir, "index");
        }

        public Dictionary<string, string> Load()
        {
            if (!File.Exists(_indexPath)) return new();
            try
            {
                var json = File.ReadAllText(_indexPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json, _opts) ?? new();
            }
            catch { return new(); }
        }

        public void Save(Dictionary<string, string> map)
        {
            File.WriteAllText(_indexPath, JsonSerializer.Serialize(map, _opts));
        }

        public void AddOrUpdate(string path, string blobHash)
        {
            var m = Load();
            m[path] = blobHash;
            Save(m);
        }

        public void Remove(string path)
        {
            var m = Load();
            m.Remove(path);
            Save(m);
        }

        public void Clear() => Save(new());
    }

    // ─── Chunker (Large File Support) ────────────────────────────────────────

    public class Chunker
    {
        private readonly BlobStore _blobStore;
        private readonly int _chunkSize;

        public Chunker(BlobStore blobStore, int chunkSizeBytes = 4 * 1024 * 1024)
        {
            _blobStore = blobStore;
            _chunkSize = chunkSizeBytes;
        }

        public LargeFilePointer SaveLargeFile(string filePath)
        {
            var fi = new FileInfo(filePath);
            var pointer = new LargeFilePointer { TotalSize = fi.Length };
            using var fs = File.OpenRead(filePath);
            var buffer = new byte[_chunkSize];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = read < _chunkSize ? buffer[..read] : buffer;
                pointer.ChunkHashes.Add(_blobStore.SaveBlob(chunk));
            }
            return pointer;
        }

        public void RestoreLargeFile(LargeFilePointer ptr, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var fs = File.Create(targetPath);
            foreach (var hash in ptr.ChunkHashes)
            {
                var bytes = _blobStore.GetBlob(hash)
                    ?? throw new InvalidOperationException($"Missing chunk {hash} for {targetPath}");
                fs.Write(bytes, 0, bytes.Length);
            }
        }
    }

    // ─── LargeFilePointer ────────────────────────────────────────────────────

    public class LargeFilePointer
    {
        public string Type { get; set; } = "lfs-pointer";
        public long TotalSize { get; set; }
        public List<string> ChunkHashes { get; set; } = new();

        private static readonly JsonSerializerOptions _opts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public string ToJson() => JsonSerializer.Serialize(this, _opts);

        public static LargeFilePointer? FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<LargeFilePointer>(json, _opts); }
            catch { return null; }
        }
    }
}
