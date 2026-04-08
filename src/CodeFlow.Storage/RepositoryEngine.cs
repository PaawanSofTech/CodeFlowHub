using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CodeFlow.Core;
using CodeFlow.Core.Models;
using CodeFlow.Crypto;
using CodeFlow.Storage;

namespace CodeFlow.Storage
{
    /// <summary>
    /// Central repository engine. All VCS operations go through here.
    /// </summary>
    public class RepositoryEngine
    {
        public string RepoRoot { get; }
        public ContentAddressableStore Store { get; }
        public BlobStore Blobs { get; }
        public TreeStore Trees { get; }
        public Index StagingIndex { get; }
        public RepoConfig Config { get; private set; }

        public RepositoryEngine(string repoRoot)
        {
            RepoRoot = Path.GetFullPath(repoRoot);
            var objectsPath = Path.Combine(RepoRoot, ".codeflow", "objects");
            Store = new ContentAddressableStore(objectsPath);
            Blobs = new BlobStore(objectsPath);
            Trees = new TreeStore(Blobs);
            StagingIndex = new Index(RepoRoot);
            Config = RepoConfig.Load(RepoRoot);

        }

        public static void InitRepo(string path)
        {
            var root = Path.GetFullPath(path);
            var codeflowDir = Path.Combine(root, ".codeflow");
            Directory.CreateDirectory(Path.Combine(codeflowDir, "objects"));
            Directory.CreateDirectory(Path.Combine(codeflowDir, "refs", "heads"));
            Directory.CreateDirectory(Path.Combine(codeflowDir, "refs", "tags"));
            Directory.CreateDirectory(Path.Combine(codeflowDir, "keys"));

            // Symbolic HEAD pointing to main
            File.WriteAllText(Path.Combine(codeflowDir, "HEAD"), "ref: refs/heads/main");

            // Default config
            if (!File.Exists(RepoConfig.ConfigPath(root)))
                new RepoConfig().Save(root);

            // Default .flowignore
            var ignoreFile = Path.Combine(root, ".flowignore");
            if (!File.Exists(ignoreFile))
            {
                File.WriteAllText(ignoreFile,
                    "# CodeFlow ignore file\n.codeflow/\n.git/\n*.tmp\n*.log\nbin/\nobj/\nnode_modules/\n");
            }
        }

        public static bool IsRepo(string path)
        {
            return Directory.Exists(Path.Combine(path, ".codeflow", "objects"));
        }

        // ─── Staging ──────────────────────────────────────────────────────────

        public void StageFiles(IEnumerable<string> absolutePaths, Action<string>? onFile = null)
        {
            var ignore = FlowIgnore.Load(RepoRoot);
            var cfDir = Path.Combine(RepoRoot, ".codeflow");
            var chunker = new Chunker(Blobs);
            var threshold = Config.LargeFileThresholdBytes;

            foreach (var abs in absolutePaths)
            {
                var rel = Path.GetRelativePath(RepoRoot, abs).Replace('\\', '/');
                if (ignore.IsIgnored(rel)) { onFile?.Invoke($"  ignored  {rel}"); continue; }
                if (abs.StartsWith(cfDir)) continue;

                try
                {
                    string hash;
                    var fi = new FileInfo(abs);
                    if (fi.Length > threshold)
                    {
                        var ptr = chunker.SaveLargeFile(abs);
                        var ptrBytes = Encoding.UTF8.GetBytes(ptr.ToJson());
                        hash = Blobs.SaveBlob(ptrBytes);
                        onFile?.Invoke($"  large    {rel} ({fi.Length:N0} bytes, {ptr.ChunkHashes.Count} chunks)");
                    }
                    else
                    {
                        hash = Blobs.SaveBlob(File.ReadAllBytes(abs));
                        onFile?.Invoke($"  staged   {rel}");
                    }
                    StagingIndex.AddOrUpdate(rel, hash);
                }
                catch (Exception ex) { onFile?.Invoke($"  error    {rel}: {ex.Message}"); }
            }
        }

        public void UnstageFile(string relPath) => StagingIndex.Remove(relPath);

        // ─── Commit ───────────────────────────────────────────────────────────

        public Commit Commit(string message, string authorName, string authorEmail,
            NSec.Cryptography.Key signingKey, byte[] pubKeyBytes)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException("Commit message cannot be empty.");

            var staged = StagingIndex.Load();
            if (staged.Count == 0)
                throw new InvalidOperationException("Nothing staged. Use 'add' to stage files.");

            // Build new tree from parent tree + staged changes
            var headHash = Store.ReadHead();
            var branch = Store.GetCurrentBranch() ?? Config.DefaultBranch;
            var parentTree = new Dictionary<string, Tree.TreeEntry>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(headHash))
            {
                var parentCommitJson = Store.GetObject(headHash);
                if (parentCommitJson != null)
                {
                    var pc = Core.Models.Commit.FromJson(parentCommitJson);
                    if (pc != null)
                    {
                        var pt = Trees.LoadTree(pc.TreeHash);
                        if (pt != null)
                            foreach (var e in pt.Entries)
                                parentTree[e.Path] = e;
                    }
                }
            }

            foreach (var (path, hash) in staged)
            {
                parentTree[path] = new Tree.TreeEntry { Path = path, Hash = hash, Type = "blob" };
            }

            var tree = new Tree();
            tree.Entries.AddRange(parentTree.Values);
            var treeHash = Trees.SaveTree(tree);

            var commit = new Core.Models.Commit
            {
                Author = Convert.ToBase64String(pubKeyBytes),
                AuthorName = authorName,
                AuthorEmail = authorEmail,
                Message = message,
                Timestamp = DateTime.UtcNow,
                ParentHash = headHash,
                ParentHashes = headHash != null ? new List<string> { headHash } : new List<string>(),
                Changes = staged.Keys.ToArray(),
                TreeHash = treeHash,
                Branch = branch
            };
            commit.SignWithKey(signingKey);

            var commitJson = commit.ToJson();
            var commitHash = Store.SaveObject(commitJson);
            commit.Hash = commitHash;

            // Update branch tip and HEAD
            Store.SetBranchTip(branch, commitHash);
            Store.SetHeadToBranch(branch);  // ensure HEAD stays symbolic

            StagingIndex.Clear();
            return commit;
        }

        // ─── Branch management ────────────────────────────────────────────────

        public void CreateBranch(string name, string? fromHash = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Branch name cannot be empty.");
            if (Store.GetBranchTip(name) != null)
                throw new InvalidOperationException($"Branch '{name}' already exists.");

            var tip = fromHash ?? Store.ReadHead()
                ?? throw new InvalidOperationException("No commits yet — cannot create branch.");
            Store.SetBranchTip(name, tip);
        }

        public void DeleteBranch(string name, bool force = false)
        {
            var current = Store.GetCurrentBranch();
            if (string.Equals(current, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cannot delete the currently checked-out branch '{name}'.");
            Store.DeleteBranch(name);
        }

        public void CheckoutBranch(string branch, bool createNew = false)
        {
            if (createNew)
            {
                // Point the new branch at the current commit (may be null on an empty repo).
                // SetHeadToBranch must happen first so GetCurrentBranch() returns the new
                // branch name immediately after this call returns.
                var currentHead = Store.ReadHead();
                if (currentHead != null)
                    Store.SetBranchTip(branch, currentHead);
                // Switch HEAD to the new branch (symbolic ref) before we try to read its tip.
                Store.SetHeadToBranch(branch);
                // Nothing to restore if the repo has no commits yet.
                if (currentHead != null)
                    RestoreWorkspaceFromCommit(currentHead);
                return;
            }

            var tip = Store.GetBranchTip(branch);

            if (tip == null)
                throw new InvalidOperationException($"Branch '{branch}' does not exist.");

            // ✅ Correct behavior
            Store.SetHeadToBranch(branch);   // symbolic HEAD
            RestoreWorkspaceFromCommit(tip); // update working tree

            // ❌ DO NOT detach HEAD
        }

        public void CheckoutCommit(string commitHash)
        {
            // Detached HEAD
            RestoreWorkspaceFromCommit(commitHash);
            Store.DetachHead(commitHash);
            Store.UpdateHead(commitHash);

        }

        // ─── Merge ────────────────────────────────────────────────────────────

        public MergeResult Merge(string sourceBranch, string targetBranch,
            string message, NSec.Cryptography.Key signingKey, byte[] pubKeyBytes)
        {
            var sourceHash = Store.GetBranchTip(sourceBranch)
                ?? throw new InvalidOperationException($"Branch '{sourceBranch}' not found.");
            var targetHash = Store.GetBranchTip(targetBranch)
                ?? throw new InvalidOperationException($"Branch '{targetBranch}' not found.");

            // Fast-forward check
            if (IsAncestor(sourceHash, targetHash))
                return new MergeResult { Success = true, Message = $"Already up to date." };

            if (IsAncestor(targetHash, sourceHash))
            {
                // Fast-forward: just move the target branch tip
                Store.SetBranchTip(targetBranch, sourceHash);
                if (Store.GetCurrentBranch() == targetBranch)
                    RestoreWorkspaceFromCommit(sourceHash);
                return new MergeResult { Success = true, MergeCommitHash = sourceHash, Message = "Fast-forward merge." };
            }

            // Find common ancestor (LCA)
            var baseHash = FindCommonAncestor(sourceHash, targetHash);
            var baseTree = baseHash != null ? Trees.FlattenTree(GetCommit(baseHash)?.TreeHash ?? "") : new();
            var sourceTree = Trees.FlattenTree(GetCommit(sourceHash)!.TreeHash);
            var targetTree = Trees.FlattenTree(GetCommit(targetHash)!.TreeHash);

            // Three-way merge
            var conflicts = new List<ConflictFile>();
            var mergedTree = new Dictionary<string, string>(StringComparer.Ordinal);

            var allPaths = new HashSet<string>(StringComparer.Ordinal);
            allPaths.UnionWith(sourceTree.Keys);
            allPaths.UnionWith(targetTree.Keys);
            allPaths.UnionWith(baseTree.Keys);

            foreach (var path in allPaths)
            {
                baseTree.TryGetValue(path, out var baseHash2);
                sourceTree.TryGetValue(path, out var srcHash);
                targetTree.TryGetValue(path, out var tgtHash);

                if (srcHash == tgtHash) { if (srcHash != null) mergedTree[path] = srcHash; continue; }
                if (srcHash == baseHash2) { if (tgtHash != null) mergedTree[path] = tgtHash; continue; }
                if (tgtHash == baseHash2) { if (srcHash != null) mergedTree[path] = srcHash; continue; }

                // Both changed → conflict
                conflicts.Add(new ConflictFile
                {
                    Path = path,
                    OursHash = tgtHash ?? "",
                    TheirsHash = srcHash ?? "",
                    BaseHash = baseHash2 ?? ""
                });
            }

            if (conflicts.Count > 0)
                return new MergeResult { Success = false, Conflicts = conflicts, Message = $"{conflicts.Count} conflict(s) detected." };

            // Build merge tree
            var finalTree = new Tree();
            finalTree.Entries.AddRange(mergedTree.Select(kv => new Tree.TreeEntry { Path = kv.Key, Hash = kv.Value, Type = "blob" }));
            var mergeTreeHash = Trees.SaveTree(finalTree);

            // Create merge commit with two parents
            var mergeCommit = new Core.Models.Commit
            {
                Author = Convert.ToBase64String(pubKeyBytes),
                Message = message,
                Timestamp = DateTime.UtcNow,
                ParentHash = targetHash,
                ParentHashes = new List<string> { targetHash, sourceHash },
                TreeHash = mergeTreeHash,
                Branch = targetBranch,
                Changes = Array.Empty<string>()
            };
            mergeCommit.SignWithKey(signingKey);

            var mergeCommitJson = mergeCommit.ToJson();
            var mergeCommitHash = Store.SaveObject(mergeCommitJson);
            mergeCommit.Hash = mergeCommitHash;

            Store.SetBranchTip(targetBranch, mergeCommitHash);
            if (Store.GetCurrentBranch() == targetBranch)
                RestoreWorkspaceFromCommit(mergeCommitHash);

            return new MergeResult { Success = true, MergeCommitHash = mergeCommitHash, Message = "Merge successful." };
        }

        // ─── Diff ─────────────────────────────────────────────────────────────

        public List<DiffResult> DiffCommits(string? fromHash, string? toHash)
        {
            var fromTree = fromHash != null ? Trees.FlattenTree(GetCommit(fromHash)?.TreeHash ?? "") : new();
            var toTree = toHash != null ? Trees.FlattenTree(GetCommit(toHash)?.TreeHash ?? "") : new();
            return ComputeDiff(fromTree, toTree);
        }

        public List<DiffResult> DiffWorkingTree()
        {
            var headHash = Store.ReadHead();
            var headTree = headHash != null ? Trees.FlattenTree(GetCommit(headHash)?.TreeHash ?? "") : new();
            var staged = StagingIndex.Load();

            // merge head + staged for "current" state
            var currentTree = new Dictionary<string, string>(headTree, StringComparer.Ordinal);
            foreach (var (k, v) in staged) currentTree[k] = v;

            // working directory
            var workTree = new Dictionary<string, string>(StringComparer.Ordinal);
            var ignore = FlowIgnore.Load(RepoRoot);
            foreach (var f in Directory.EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(RepoRoot, f).Replace('\\', '/');
                if (ignore.IsIgnored(rel)) continue;
                try { workTree[rel] = HashUtil.Sha256(File.ReadAllBytes(f)); } catch { }
            }

            return ComputeDiff(currentTree, workTree);
        }

        private List<DiffResult> ComputeDiff(Dictionary<string, string> from, Dictionary<string, string> to)
        {
            var results = new List<DiffResult>();
            var allPaths = new HashSet<string>(from.Keys.Concat(to.Keys), StringComparer.Ordinal);

            foreach (var path in allPaths.OrderBy(p => p))
            {
                from.TryGetValue(path, out var oldHash);
                to.TryGetValue(path, out var newHash);

                if (oldHash == newHash) continue;

                var status = (oldHash, newHash) switch
                {
                    (null, _) => DiffStatus.Added,
                    (_, null) => DiffStatus.Deleted,
                    _ => DiffStatus.Modified
                };

                var result = new DiffResult { FilePath = path, Status = status, OldHash = oldHash, NewHash = newHash };

                // Compute line-level diff for text files
                var oldBytes = oldHash != null ? Blobs.GetBlob(oldHash) : null;
                var newBytes = newHash != null ? Blobs.GetBlob(newHash) : null;

                if ((oldBytes == null || !HashUtil.IsBinaryData(oldBytes)) &&
                    (newBytes == null || !HashUtil.IsBinaryData(newBytes)))
                {
                    var oldLines = oldBytes != null ? Encoding.UTF8.GetString(oldBytes).Split('\n') : Array.Empty<string>();
                    var newLines = newBytes != null ? Encoding.UTF8.GetString(newBytes).Split('\n') : Array.Empty<string>();
                    result.Hunks = ComputeHunks(oldLines, newLines);
                    result.Additions = result.Hunks.Sum(h => h.Lines.Count(l => l.Type == DiffLineType.Addition));
                    result.Deletions = result.Hunks.Sum(h => h.Lines.Count(l => l.Type == DiffLineType.Deletion));
                }
                else
                {
                    result.IsBinary = true;
                }

                results.Add(result);
            }
            return results;
        }

        private static List<DiffHunk> ComputeHunks(string[] oldLines, string[] newLines)
        {
            // Simple Myers-like diff using LCS
            var lcs = LCS(oldLines, newLines);
            var hunks = new List<DiffHunk>();
            var lines = new List<DiffLine>();

            int oi = 0, ni = 0, li = 0;
            while (oi < oldLines.Length || ni < newLines.Length)
            {
                if (li < lcs.Count && oi < oldLines.Length && ni < newLines.Length
                    && oldLines[oi] == lcs[li] && newLines[ni] == lcs[li])
                {
                    lines.Add(new DiffLine { Type = DiffLineType.Context, Content = oldLines[oi], OldLineNumber = oi + 1, NewLineNumber = ni + 1 });
                    oi++; ni++; li++;
                }
                else if (oi < oldLines.Length && (li >= lcs.Count || oldLines[oi] != lcs[li]))
                {
                    lines.Add(new DiffLine { Type = DiffLineType.Deletion, Content = oldLines[oi], OldLineNumber = oi + 1 });
                    oi++;
                }
                else
                {
                    lines.Add(new DiffLine { Type = DiffLineType.Addition, Content = newLines[ni], NewLineNumber = ni + 1 });
                    ni++;
                }
            }

            if (lines.Count > 0)
                hunks.Add(new DiffHunk { OldStart = 1, NewStart = 1, OldCount = oldLines.Length, NewCount = newLines.Length, Lines = lines });

            return hunks;
        }

        private static List<string> LCS(string[] a, string[] b)
        {
            if (a.Length > 500 || b.Length > 500) return new(); // skip for huge files
            int m = a.Length, n = b.Length;
            int[,] dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            var result = new List<string>();
            int r = m, c = n;
            while (r > 0 && c > 0)
            {
                if (a[r - 1] == b[c - 1]) { result.Add(a[r - 1]); r--; c--; }
                else if (dp[r - 1, c] > dp[r, c - 1]) r--;
                else c--;
            }
            result.Reverse();
            return result;
        }

        // ─── Graph / History traversal ────────────────────────────────────────

        public List<Core.Models.Commit> GetLog(string? startHash = null, int limit = 50, string? authorFilter = null, string? branchFilter = null)
        {
            var head = startHash ?? Store.ReadHead();
            if (head == null) return new();

            var result = new List<Core.Models.Commit>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(head);

            while (queue.Count > 0 && result.Count < limit)
            {
                var hash = queue.Dequeue();
                if (!visited.Add(hash)) continue;

                var c = GetCommit(hash);
                if (c == null) continue;
                c.Hash = hash;

                bool include = true;
                if (authorFilter != null && !c.AuthorName.Contains(authorFilter, StringComparison.OrdinalIgnoreCase)
                    && !c.AuthorEmail.Contains(authorFilter, StringComparison.OrdinalIgnoreCase))
                    include = false;
                if (branchFilter != null && !string.Equals(c.Branch, branchFilter, StringComparison.OrdinalIgnoreCase))
                    include = false;

                if (include) result.Add(c);

                foreach (var parent in c.ParentHashes)
                    if (!visited.Contains(parent))
                        queue.Enqueue(parent);
            }

            return result;
        }

        public Core.Models.Commit? GetCommit(string hash)
        {
            var json = Store.GetObject(hash);
            if (json == null) return null;
            var c = Core.Models.Commit.FromJson(json);
            if (c != null) c.Hash = hash;
            return c;
        }

        public bool IsAncestor(string ancestorHash, string descendantHash)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(descendantHash);
            while (queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (!visited.Add(h)) continue;
                if (h == ancestorHash) return true;
                var c = GetCommit(h);
                if (c == null) continue;
                foreach (var p in c.ParentHashes) queue.Enqueue(p);
            }
            return false;
        }

        public string? FindCommonAncestor(string hashA, string hashB)
        {
            var ancestorsA = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(hashA);
            while (queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (!ancestorsA.Add(h)) continue;
                var c = GetCommit(h);
                if (c == null) continue;
                foreach (var p in c.ParentHashes) queue.Enqueue(p);
            }

            queue.Clear();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            queue.Enqueue(hashB);
            while (queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (!visited.Add(h)) continue;
                if (ancestorsA.Contains(h)) return h;
                var c = GetCommit(h);
                if (c == null) continue;
                foreach (var p in c.ParentHashes) queue.Enqueue(p);
            }
            return null;
        }

        // ─── Workspace restore ────────────────────────────────────────────────

        public void RestoreWorkspaceFromCommit(string commitHash)
        {
            var commit = GetCommit(commitHash)
                ?? throw new InvalidOperationException($"Commit {commitHash[..8]} not found.");
            var tree = Trees.LoadTree(commit.TreeHash)
                ?? throw new InvalidOperationException("Commit tree missing.");

            var cfDir = Path.GetFullPath(Path.Combine(RepoRoot, ".codeflow"));
            var expected = new HashSet<string>(tree.Entries.Select(e => e.Path), StringComparer.Ordinal);

            // Write to temp area then swap to avoid partial-write corruption
            var tmpDir = Path.Combine(RepoRoot, ".codeflow", "tmp_checkout");
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);

            var chunker = new Chunker(Blobs);
            foreach (var entry in tree.Entries)
            {
                var dest = Path.Combine(tmpDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var bytes = Blobs.GetBlob(entry.Hash)
                    ?? throw new InvalidOperationException($"Missing blob {entry.Hash} for {entry.Path}");
                var text = Encoding.UTF8.GetString(bytes);
                if (text.TrimStart().StartsWith("{") && text.Contains("\"type\":\"lfs-pointer\""))
                {
                    var ptr = LargeFilePointer.FromJson(text);
                    if (ptr != null) chunker.RestoreLargeFile(ptr, dest);
                }
                else File.WriteAllBytes(dest, bytes);
            }

            // Atomic: move tmp files to workspace, delete removed files
            foreach (var entry in tree.Entries)
            {
                var src = Path.Combine(tmpDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                var dst = Path.Combine(RepoRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }

            // Remove files not in tree
            foreach (var f in Directory.EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(f);
                if (full.StartsWith(cfDir)) continue;
                var rel = Path.GetRelativePath(RepoRoot, f).Replace('\\', '/');
                if (!expected.Contains(rel)) File.Delete(f);
            }

            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }

        // ─── Object graph enumeration ─────────────────────────────────────────

        public HashSet<string> GetReachableObjects(string startHash)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(startHash);

            while (queue.Count > 0)
            {
                var hash = queue.Dequeue();
                if (!result.Add(hash)) continue;

                var raw = Store.GetRawObject(hash);
                if (raw == null) continue;
                if (HashUtil.IsBinaryData(raw)) continue;

                try
                {
                    var text = Encoding.UTF8.GetString(raw);
                    var type = HashUtil.GetObjectType(text);
                    if (type == "commit")
                    {
                        var c = Core.Models.Commit.FromJson(text);
                        if (c == null) continue;
                        foreach (var p in c.ParentHashes) if (result.Add(p)) queue.Enqueue(p);
                        if (!string.IsNullOrEmpty(c.TreeHash) && result.Add(c.TreeHash)) queue.Enqueue(c.TreeHash);
                    }
                    else if (type == "tree")
                    {
                        var t = Tree.FromJson(text);
                        if (t == null) continue;
                        foreach (var e in t.Entries) if (!string.IsNullOrEmpty(e.Hash) && result.Add(e.Hash)) queue.Enqueue(e.Hash);
                    }
                    else if (type == "lfs-pointer")
                    {
                        var ptr = LargeFilePointer.FromJson(text);
                        if (ptr == null) continue;
                        foreach (var ch in ptr.ChunkHashes) if (result.Add(ch)) queue.Enqueue(ch);
                    }
                }
                catch { /* binary blob */ }
            }
            return result;
        }

        private static class HashUtil
        {
            public static string Sha256(byte[] data)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
            }
            public static bool IsBinaryData(byte[] d) => CodeFlow.Crypto.HashUtil.IsBinaryData(d);
            public static string GetObjectType(string s) => CodeFlow.Crypto.HashUtil.GetObjectType(s);
        }
    }
}