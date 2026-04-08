using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodeFlow.Storage
{
    /// <summary>
    /// Content-Addressable Store with Git-style 2-char prefix sharding.
    /// Objects stored as: objects/ab/cdef1234... (avoids filesystem inode limits with huge repos)
    /// HEAD and refs stored in .codeflow/refs/
    /// </summary>
    public class ContentAddressableStore
    {
        private readonly string _storePath;
        private readonly string _repoRoot;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public ContentAddressableStore(string objectsPath)
        {
            _storePath = Path.GetFullPath(objectsPath);
            _repoRoot = Path.GetFullPath(Path.Combine(_storePath, ".."));
            Directory.CreateDirectory(_storePath);
            Directory.CreateDirectory(Path.Combine(_repoRoot, "refs", "heads"));
            Directory.CreateDirectory(Path.Combine(_repoRoot, "refs", "tags"));
        }

        // ─── Object storage ──────────────────────────────────────────────────

        public string SaveObject(string content)
        {
            var bytes = Utf8NoBom.GetBytes(content);
            return SaveObjectBytes(bytes);
        }

        public string SaveObjectBytes(byte[] data)
        {
            var hash = ComputeHash(data);
            var path = ObjectPath(hash);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
            return hash;
        }

        public void SaveObject(byte[] data, string hash)
        {
            var path = ObjectPath(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
                File.WriteAllBytes(path, data);
        }

        public string? GetObject(string hash)
        {
            var path = ObjectPath(hash);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        public byte[]? GetRawObject(string hash)
        {
            var path = ObjectPath(hash);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        public bool HasObject(string hash)
        {
            return File.Exists(ObjectPath(hash));
        }

        public IEnumerable<string> GetAllObjectHashes()
        {
            if (!Directory.Exists(_storePath)) yield break;
            foreach (var dir in Directory.EnumerateDirectories(_storePath))
            {
                var prefix = Path.GetFileName(dir);
                if (prefix.Length != 2) continue;
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var suffix = Path.GetFileName(file);
                    yield return prefix + suffix;
                }
            }
        }

        // ─── HEAD / Branch refs ───────────────────────────────────────────────

        /// <summary>Reads the current HEAD. Returns a commit hash or "ref: refs/heads/main".</summary>
        public string? ReadHead()
        {
            var headFile = Path.Combine(_repoRoot, "HEAD");
            if (!File.Exists(headFile)) return null;
            var content = File.ReadAllText(headFile).Trim();
            if (content.StartsWith("ref: "))
            {
                // Symbolic ref — resolve it
                var refPath = content.Substring(5).Trim();
                return ReadRef(refPath);
            }
            return string.IsNullOrEmpty(content) ? null : content;
        }

        /// <summary>Returns "ref: refs/heads/main" or a bare hash.</summary>
        public string? ReadHeadRaw()
        {
            var headFile = Path.Combine(_repoRoot, "HEAD");
            return File.Exists(headFile) ? File.ReadAllText(headFile).Trim() : null;
        }

        public string? GetCurrentBranch()
        {
            var raw = ReadHeadRaw();
            if (raw == null) return null;
            if (raw.StartsWith("ref: refs/heads/"))
                return raw.Substring("ref: refs/heads/".Length);
            return null; // detached HEAD
        }

        public bool IsDetachedHead()
        {
            var raw = ReadHeadRaw();
            return raw != null && !raw.StartsWith("ref: ");
        }

        public void UpdateHead(string hashOrRef)
        {
            var headFile = Path.Combine(_repoRoot, "HEAD");
            File.WriteAllText(headFile, hashOrRef, Utf8NoBom);
        }

        /// <summary>Points HEAD at a branch (symbolic ref).</summary>
        public void SetHeadToBranch(string branchName)
        {
            UpdateHead($"ref: refs/heads/{branchName}");
        }

        /// <summary>Sets HEAD to a bare commit hash (detached HEAD).</summary>
        public void DetachHead(string commitHash)
        {
            UpdateHead(commitHash);
        }

        // ─── Ref management (branches & tags) ────────────────────────────────

        public string? ReadRef(string refPath)
        {
            var file = Path.Combine(_repoRoot, refPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) return null;
            var content = File.ReadAllText(file).Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }

        public void WriteRef(string refPath, string hash)
        {
            var file = Path.Combine(_repoRoot, refPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, hash, Utf8NoBom);
        }

        public string? GetBranchTip(string branchName) =>
            ReadRef($"refs/heads/{branchName}");

        public void SetBranchTip(string branchName, string commitHash) =>
            WriteRef($"refs/heads/{branchName}", commitHash);

        public void DeleteBranch(string branchName)
        {
            var file = Path.Combine(_repoRoot, "refs", "heads", branchName);
            if (File.Exists(file)) File.Delete(file);
        }

        public IEnumerable<string> GetAllBranches()
        {
            var headsDir = Path.Combine(_repoRoot, "refs", "heads");
            if (!Directory.Exists(headsDir)) yield break;
            foreach (var f in Directory.EnumerateFiles(headsDir, "*", SearchOption.AllDirectories))
                yield return Path.GetRelativePath(headsDir, f).Replace(Path.DirectorySeparatorChar, '/');
        }

        public void SetTagTip(string tagName, string commitHash) =>
            WriteRef($"refs/tags/{tagName}", commitHash);

        public string? GetTagTip(string tagName) =>
            ReadRef($"refs/tags/{tagName}");

        public IEnumerable<string> GetAllTags()
        {
            var tagsDir = Path.Combine(_repoRoot, "refs", "tags");
            if (!Directory.Exists(tagsDir)) yield break;
            foreach (var f in Directory.EnumerateFiles(tagsDir))
                yield return Path.GetFileName(f);
        }

        // ─── MERGE_HEAD (for tracking merges in progress) ────────────────────

        public void WriteMergeHead(string hash)
        {
            File.WriteAllText(Path.Combine(_repoRoot, "MERGE_HEAD"), hash, Utf8NoBom);
        }

        public string? ReadMergeHead()
        {
            var f = Path.Combine(_repoRoot, "MERGE_HEAD");
            return File.Exists(f) ? File.ReadAllText(f).Trim() : null;
        }

        public void ClearMergeHead()
        {
            var f = Path.Combine(_repoRoot, "MERGE_HEAD");
            if (File.Exists(f)) File.Delete(f);
        }

        // ─── Internal helpers ─────────────────────────────────────────────────

        private string ObjectPath(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                throw new ArgumentException("Object hash cannot be null or empty.", nameof(hash));
            if (hash.Length < 4) return Path.Combine(_storePath, hash);
            var prefix = hash.Substring(0, 2);
            var suffix = hash.Substring(2);
            return Path.Combine(_storePath, prefix, suffix);
        }

        private static string ComputeHash(byte[] data)
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(data);
            return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
        }
    }
}