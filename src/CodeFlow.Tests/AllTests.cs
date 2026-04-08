using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using CodeFlow.Core;
using CodeFlow.Core.Models;
using CodeFlow.Crypto;
using CodeFlow.Storage;
using NSec.Cryptography;

namespace CodeFlow.Tests
{
    // ─── Crypto Tests ─────────────────────────────────────────────────────────

    public class CryptoTests
    {
        [Fact]
        public void KeyGen_ProducesValidKeyPair()
        {
            var creation = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
            using var key = new Key(SignatureAlgorithm.Ed25519, creation);
            var priv = key.Export(KeyBlobFormat.RawPrivateKey);
            var pub = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            Assert.Equal(32, priv.Length);
            Assert.Equal(32, pub.Length);
        }

        [Fact]
        public void SignAndVerify_RoundTrips()
        {
            var creation = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
            using var key = new Key(SignatureAlgorithm.Ed25519, creation);
            var data = Encoding.UTF8.GetBytes("hello codeflow");
            var sig = CryptoUtils.Sign(key, data);
            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            Assert.True(CryptoUtils.Verify(pubRaw, data, sig));
        }

        [Fact]
        public void Verify_FailsWithWrongData()
        {
            var creation = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
            using var key = new Key(SignatureAlgorithm.Ed25519, creation);
            var sig = CryptoUtils.Sign(key, Encoding.UTF8.GetBytes("original"));
            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            Assert.False(CryptoUtils.Verify(pubRaw, Encoding.UTF8.GetBytes("tampered"), sig));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"treeHash\":\"abc\",\"parentHash\":\"def\"}")]
        public void HashUtil_GetObjectType_Commit(string json)
        {
            var result = HashUtil.GetObjectType(json);
            if (json.Contains("treeHash") && json.Contains("parentHash"))
                Assert.Equal("commit", result);
        }

        [Fact]
        public void HashUtil_Sha256_IsConsistent()
        {
            var h1 = HashUtil.Sha256(Encoding.UTF8.GetBytes("test"));
            var h2 = HashUtil.Sha256(Encoding.UTF8.GetBytes("test"));
            Assert.Equal(h1, h2);
            Assert.Equal(64, h1.Length); // SHA256 = 32 bytes = 64 hex chars
        }

        [Fact]
        public void HashUtil_IsBinaryData_DetectsText()
        {
            var text = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
            Assert.False(HashUtil.IsBinaryData(text));
        }

        [Fact]
        public void HashUtil_IsBinaryData_DetectsBinary()
        {
            // PNG magic bytes
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert.True(HashUtil.IsBinaryData(png));
        }
    }

    // ─── Storage Tests ────────────────────────────────────────────────────────

    public class ContentAddressableStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ContentAddressableStore _store;

        public ContentAddressableStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _store = new ContentAddressableStore(Path.Combine(_tempDir, "objects"));
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void SaveAndGet_RoundTrips()
        {
            const string content = "{\"message\":\"test commit\"}";
            var hash = _store.SaveObject(content);
            Assert.NotNull(hash);
            Assert.Equal(64, hash.Length);
            var retrieved = _store.GetObject(hash);
            Assert.Equal(content, retrieved);
        }

        [Fact]
        public void SaveObject_IsIdempotent()
        {
            const string content = "same content twice";
            var h1 = _store.SaveObject(content);
            var h2 = _store.SaveObject(content);
            Assert.Equal(h1, h2);
        }

        [Fact]
        public void GetObject_ReturnsNullForMissingHash()
        {
            var result = _store.GetObject("aaaa" + new string('0', 60));
            Assert.Null(result);
        }

        [Fact]
        public void HasObject_ReturnsTrueAfterSave()
        {
            var hash = _store.SaveObject("exists");
            Assert.True(_store.HasObject(hash));
            Assert.False(_store.HasObject("aaaa" + new string('0', 60)));
        }

        [Fact]
        public void ObjectsAreSharded_TwoCharPrefix()
        {
            var hash = _store.SaveObject("sharding test content");
            var objectsDir = Path.Combine(_tempDir, "objects");
            // In sharded layout, the first 2 chars form a subdirectory
            var subdir = Path.Combine(objectsDir, hash.Substring(0, 2));
            var file = Path.Combine(subdir, hash.Substring(2));
            Assert.True(Directory.Exists(subdir), $"Shard directory should exist: {subdir}");
            Assert.True(File.Exists(file), $"Object file should exist: {file}");
        }

        [Fact]
        public void Head_UpdateAndRead_RoundTrips()
        {
            const string hash = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234";
            _store.UpdateHead(hash);
            Assert.Equal(hash, _store.ReadHead());
        }

        [Fact]
        public void ReadHead_ReturnsNull_WhenNoHead()
        {
            Assert.Null(_store.ReadHead());
        }

        [Fact]
        public void SymbolicHead_ResolvesViaBranch()
        {
            const string commitHash = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234";
            _store.SetBranchTip("main", commitHash);
            _store.SetHeadToBranch("main");
            Assert.Equal(commitHash, _store.ReadHead());
            Assert.Equal("main", _store.GetCurrentBranch());
            Assert.False(_store.IsDetachedHead());
        }

        [Fact]
        public void DetachedHead_IsDetected()
        {
            const string hash = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234";
            _store.DetachHead(hash);
            Assert.True(_store.IsDetachedHead());
            Assert.Null(_store.GetCurrentBranch());
        }

        [Fact]
        public void BranchCRUD_Works()
        {
            const string hash = "aaaa1234aaaa1234aaaa1234aaaa1234aaaa1234aaaa1234aaaa1234aaaa1234";
            _store.SetBranchTip("feature/test", hash);
            Assert.Equal(hash, _store.GetBranchTip("feature/test"));
            var branches = _store.GetAllBranches().ToList();
            Assert.Contains("feature/test", branches);
            _store.DeleteBranch("feature/test");
            Assert.Null(_store.GetBranchTip("feature/test"));
        }

        [Fact]
        public void GetAllObjectHashes_EnumeratesAll()
        {
            _store.SaveObject("object one");
            _store.SaveObject("object two");
            _store.SaveObject("object three");
            var hashes = _store.GetAllObjectHashes().ToList();
            Assert.Equal(3, hashes.Count);
        }
    }

    // ─── BlobStore Tests ──────────────────────────────────────────────────────

    public class BlobStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly BlobStore _store;

        public BlobStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_blob_" + Guid.NewGuid().ToString("N"));
            _store = new BlobStore(Path.Combine(_tempDir, "objects"));
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void SaveBlob_ReturnsConsistentHash()
        {
            var data = Encoding.UTF8.GetBytes("hello world");
            var h1 = _store.SaveBlob(data);
            var h2 = _store.SaveBlob(data);
            Assert.Equal(h1, h2);
        }

        [Fact]
        public void GetBlob_ReturnsNull_WhenMissing()
        {
            Assert.Null(_store.GetBlob("0000000000000000000000000000000000000000000000000000000000000000"));
        }

        [Fact]
        public void GetBlob_ReturnsStoredBytes()
        {
            var data = new byte[] { 1, 2, 3, 4, 5 };
            var hash = _store.SaveBlob(data);
            var retrieved = _store.GetBlob(hash);
            Assert.NotNull(retrieved);
            Assert.Equal(data, retrieved);
        }
    }

    // ─── Tree Tests ───────────────────────────────────────────────────────────

    public class TreeTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly TreeStore _treeStore;

        public TreeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_tree_" + Guid.NewGuid().ToString("N"));
            var blobs = new BlobStore(Path.Combine(_tempDir, "objects"));
            _treeStore = new TreeStore(blobs);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Tree_SerializesAndDeserializes()
        {
            var tree = new Tree();
            tree.Entries.Add(new Tree.TreeEntry { Path = "src/main.cs", Hash = "abc123", Type = "blob" });
            tree.Entries.Add(new Tree.TreeEntry { Path = "README.md", Hash = "def456", Type = "blob" });

            var json = tree.ToJson();
            var parsed = Tree.FromJson(json);

            Assert.NotNull(parsed);
            Assert.Equal(2, parsed!.Entries.Count);
        }

        [Fact]
        public void TreeStore_SaveAndLoad_RoundTrips()
        {
            var tree = new Tree();
            tree.Entries.Add(new Tree.TreeEntry { Path = "file.txt", Hash = "aabbcc", Type = "blob" });

            var hash = _treeStore.SaveTree(tree);
            var loaded = _treeStore.LoadTree(hash);

            Assert.NotNull(loaded);
            Assert.Single(loaded!.Entries);
            Assert.Equal("file.txt", loaded.Entries[0].Path);
        }

        [Fact]
        public void TreeStore_LoadNull_WhenHashMissing()
        {
            Assert.Null(_treeStore.LoadTree("0000000000000000000000000000000000000000000000000000000000000000"));
        }

        [Fact]
        public void Tree_EntriesAreSorted_OnSave()
        {
            var tree = new Tree();
            tree.Entries.Add(new Tree.TreeEntry { Path = "z_last.txt", Hash = "111", Type = "blob" });
            tree.Entries.Add(new Tree.TreeEntry { Path = "a_first.txt", Hash = "222", Type = "blob" });
            tree.Entries.Add(new Tree.TreeEntry { Path = "m_middle.txt", Hash = "333", Type = "blob" });

            var hash = _treeStore.SaveTree(tree);
            var loaded = _treeStore.LoadTree(hash);

            Assert.Equal("a_first.txt", loaded!.Entries[0].Path);
            Assert.Equal("m_middle.txt", loaded.Entries[1].Path);
            Assert.Equal("z_last.txt", loaded.Entries[2].Path);
        }
    }

    // ─── Commit Model Tests ───────────────────────────────────────────────────

    public class CommitModelTests
    {
        private static Key MakeKey()
        {
            var p = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
            return new Key(SignatureAlgorithm.Ed25519, p);
        }

        [Fact]
        public void Commit_SignAndVerify_IsValid()
        {
            using var key = MakeKey();
            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

            var commit = new Commit
            {
                Author = Convert.ToBase64String(pubRaw),
                AuthorName = "Test User",
                AuthorEmail = "test@example.com",
                Message = "Initial commit",
                Timestamp = DateTime.UtcNow,
                ParentHash = null,
                ParentHashes = new List<string>(),
                TreeHash = "abc123",
                Branch = "main",
                Changes = new[] { "README.md" }
            };

            commit.SignWithKey(key);
            Assert.True(commit.VerifySignature());
        }

        [Fact]
        public void Commit_Tampered_FailsVerification()
        {
            using var key = MakeKey();
            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

            var commit = new Commit
            {
                Author = Convert.ToBase64String(pubRaw),
                AuthorName = "Alice",
                Message = "Real message",
                Timestamp = DateTime.UtcNow,
                ParentHashes = new List<string>(),
                TreeHash = "tree1",
                Branch = "main",
                Changes = Array.Empty<string>()
            };

            commit.SignWithKey(key);
            commit.Message = "TAMPERED"; // modify after signing

            Assert.False(commit.VerifySignature());
        }

        [Fact]
        public void Commit_SerializeDeserialize_RoundTrips()
        {
            using var key = MakeKey();
            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

            var original = new Commit
            {
                Author = Convert.ToBase64String(pubRaw),
                AuthorName = "Dev",
                AuthorEmail = "dev@cf.io",
                Message = "Test commit",
                Timestamp = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                ParentHash = "parentHash123",
                ParentHashes = new List<string> { "parentHash123" },
                TreeHash = "treeHash456",
                Branch = "main",
                Changes = new[] { "a.cs", "b.cs" }
            };
            original.SignWithKey(key);

            var json = original.ToJson();
            var restored = Commit.FromJson(json);

            Assert.NotNull(restored);
            Assert.Equal(original.Message, restored!.Message);
            Assert.Equal(original.AuthorName, restored.AuthorName);
            Assert.Equal(original.TreeHash, restored.TreeHash);
            Assert.Equal(original.Branch, restored.Branch);
            Assert.True(restored.VerifySignature());
        }

        [Fact]
        public void Commit_IsMerge_WhenTwoParents()
        {
            var commit = new Commit
            {
                ParentHashes = new List<string> { "parent1", "parent2" },
                Message = "Merge",
                TreeHash = "tree",
                Branch = "main",
                Changes = Array.Empty<string>()
            };
            Assert.True(commit.IsMergeCommit);
        }

        [Fact]
        public void Commit_IsNotMerge_WhenOneParent()
        {
            var commit = new Commit
            {
                ParentHashes = new List<string> { "parent1" },
                Message = "Normal",
                TreeHash = "tree",
                Branch = "main",
                Changes = Array.Empty<string>()
            };
            Assert.False(commit.IsMergeCommit);
        }
    }

    // ─── Index Tests ──────────────────────────────────────────────────────────

    public class IndexTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Index _index;

        public IndexTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_idx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _index = new Index(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void Load_ReturnsEmpty_WhenNoIndex()
        {
            var m = _index.Load();
            Assert.Empty(m);
        }

        [Fact]
        public void AddOrUpdate_PersistsAcrossLoad()
        {
            _index.AddOrUpdate("src/main.cs", "hash1");
            _index.AddOrUpdate("README.md", "hash2");
            var m = _index.Load();
            Assert.Equal("hash1", m["src/main.cs"]);
            Assert.Equal("hash2", m["README.md"]);
        }

        [Fact]
        public void Clear_EmptiesIndex()
        {
            _index.AddOrUpdate("file.txt", "hash");
            _index.Clear();
            Assert.Empty(_index.Load());
        }

        [Fact]
        public void Remove_DeletesEntry()
        {
            _index.AddOrUpdate("a.txt", "hash_a");
            _index.AddOrUpdate("b.txt", "hash_b");
            _index.Remove("a.txt");
            var m = _index.Load();
            Assert.False(m.ContainsKey("a.txt"));
            Assert.True(m.ContainsKey("b.txt"));
        }
    }

    // ─── FlowIgnore Tests ─────────────────────────────────────────────────────

    public class FlowIgnoreTests : IDisposable
    {
        private readonly string _tempDir;

        public FlowIgnoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_ignore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        private FlowIgnore CreateWithRules(params string[] rules)
        {
            File.WriteAllLines(Path.Combine(_tempDir, ".flowignore"), rules);
            return FlowIgnore.Load(_tempDir);
        }

        [Fact]
        public void DotCodeflow_IsAlwaysIgnored()
        {
            var ig = FlowIgnore.Load(_tempDir);
            Assert.True(ig.IsIgnored(".codeflow/HEAD"));
            Assert.True(ig.IsIgnored(".codeflow/objects/ab/cdef"));
        }

        [Fact]
        public void SimplePattern_MatchesFile()
        {
            var ig = CreateWithRules("*.log");
            Assert.True(ig.IsIgnored("app.log"));
            Assert.True(ig.IsIgnored("debug.log"));
            Assert.False(ig.IsIgnored("app.cs"));
        }

        [Fact]
        public void DirectoryPattern_MatchesContents()
        {
            var ig = CreateWithRules("node_modules/");
            Assert.True(ig.IsIgnored("node_modules/lodash/index.js"));
            Assert.False(ig.IsIgnored("src/node_modules_backup/file.txt"));
        }

        [Fact]
        public void NegationPattern_Unignores()
        {
            var ig = CreateWithRules("*.log", "!important.log");
            Assert.True(ig.IsIgnored("debug.log"));
            Assert.False(ig.IsIgnored("important.log"));
        }

        [Fact]
        public void CommentLines_AreSkipped()
        {
            var ig = CreateWithRules("# this is a comment", "*.tmp");
            Assert.True(ig.IsIgnored("scratch.tmp"));
            Assert.False(ig.IsIgnored("# this is a comment"));
        }

        [Fact]
        public void DoubleStar_MatchesNestedPaths()
        {
            var ig = CreateWithRules("**/bin/");
            Assert.True(ig.IsIgnored("src/project/bin/Debug/app.dll"));
        }
    }

    // ─── RepositoryEngine Integration Tests ───────────────────────────────────

    public class RepositoryEngineTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly RepositoryEngine _engine;
        private readonly Key _signingKey;
        private readonly byte[] _pubKeyBytes;

        public RepositoryEngineTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_repo_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            RepositoryEngine.InitRepo(_tempDir);
            _engine = new RepositoryEngine(_tempDir);

            var p = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
            _signingKey = new Key(SignatureAlgorithm.Ed25519, p);
            _pubKeyBytes = _signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        }

        public void Dispose()
        {
            _signingKey.Dispose();
            Directory.Delete(_tempDir, recursive: true);
        }

        private void WriteAndStageFile(string relPath, string content)
        {
            var abs = Path.Combine(_tempDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
            File.WriteAllText(abs, content);
            _engine.StageFiles(new[] { abs });
        }

        private Commit MakeCommit(string message)
            => _engine.Commit(message, "Test User", "test@cf.io", _signingKey, _pubKeyBytes);

        [Fact]
        public void InitRepo_CreatesExpectedStructure()
        {
            Assert.True(Directory.Exists(Path.Combine(_tempDir, ".codeflow")));
            Assert.True(Directory.Exists(Path.Combine(_tempDir, ".codeflow", "objects")));
            Assert.True(Directory.Exists(Path.Combine(_tempDir, ".codeflow", "refs", "heads")));
            Assert.True(File.Exists(Path.Combine(_tempDir, ".codeflow", "HEAD")));
        }

        [Fact]
        public void IsRepo_ReturnsTrueAfterInit()
        {
            Assert.True(RepositoryEngine.IsRepo(_tempDir));
            Assert.False(RepositoryEngine.IsRepo(Path.GetTempPath()));
        }

        [Fact]
        public void StageAndCommit_ProducesCommitWithTree()
        {
            WriteAndStageFile("hello.txt", "Hello, CodeFlow!");
            var commit = MakeCommit("Initial commit");

            Assert.NotNull(commit.Hash);
            Assert.NotEmpty(commit.TreeHash);
            Assert.True(commit.VerifySignature());
            Assert.Equal("main", commit.Branch);
        }

        [Fact]
        public void Commit_EmptyStaging_ThrowsException()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _engine.Commit("empty", "User", "u@cf.io", _signingKey, _pubKeyBytes));
        }

        [Fact]
        public void Commit_EmptyMessage_ThrowsException()
        {
            WriteAndStageFile("a.txt", "content");
            Assert.Throws<InvalidOperationException>(() =>
                _engine.Commit("   ", "User", "u@cf.io", _signingKey, _pubKeyBytes));
        }

        [Fact]
        public void CommitChain_BuildsHistory()
        {
            WriteAndStageFile("a.txt", "version 1");
            var c1 = MakeCommit("First commit");

            WriteAndStageFile("a.txt", "version 2");
            var c2 = MakeCommit("Second commit");

            WriteAndStageFile("b.txt", "new file");
            var c3 = MakeCommit("Third commit");

            var log = _engine.GetLog(_engine.Store.ReadHead()!, 10);
            Assert.Equal(3, log.Count);
            Assert.Equal("Third commit", log[0].Message);
            Assert.Equal("Second commit", log[1].Message);
            Assert.Equal("First commit", log[2].Message);
        }

        [Fact]
        public void HeadUpdates_AfterEachCommit()
        {
            WriteAndStageFile("x.txt", "x");
            var c1 = MakeCommit("C1");
            Assert.Equal(c1.Hash, _engine.Store.ReadHead());

            WriteAndStageFile("y.txt", "y");
            var c2 = MakeCommit("C2");
            Assert.Equal(c2.Hash, _engine.Store.ReadHead());
        }

        [Fact]
        public void BranchCreate_AndListBranches()
        {
            WriteAndStageFile("base.txt", "base");
            MakeCommit("Base commit");

            _engine.CreateBranch("feature");
            var branches = _engine.Store.GetAllBranches().ToList();
            Assert.Contains("main", branches);
            Assert.Contains("feature", branches);
        }

        [Fact]
        public void CreateBranch_Duplicate_ThrowsException()
        {
            WriteAndStageFile("f.txt", "f");
            MakeCommit("First");
            _engine.CreateBranch("dev");
            Assert.Throws<InvalidOperationException>(() => _engine.CreateBranch("dev"));
        }

        [Fact]
        public void DeleteBranch_RemovesBranch()
        {
            WriteAndStageFile("f.txt", "f");
            MakeCommit("First");
            _engine.CreateBranch("temp");
            _engine.DeleteBranch("temp");
            var branches = _engine.Store.GetAllBranches().ToList();
            Assert.DoesNotContain("temp", branches);
        }

        [Fact]
        public void IsAncestor_DetectsLinearChain()
        {
            WriteAndStageFile("a.txt", "a");
            var c1 = MakeCommit("C1");
            WriteAndStageFile("b.txt", "b");
            var c2 = MakeCommit("C2");
            WriteAndStageFile("c.txt", "c");
            var c3 = MakeCommit("C3");

            Assert.True(_engine.IsAncestor(c1.Hash!, c3.Hash!));
            Assert.True(_engine.IsAncestor(c1.Hash!, c2.Hash!));
            Assert.False(_engine.IsAncestor(c3.Hash!, c1.Hash!));
        }

        [Fact]
        public void FindCommonAncestor_LinearChain()
        {
            WriteAndStageFile("a.txt", "a");
            var c1 = MakeCommit("C1");
            WriteAndStageFile("b.txt", "b");
            var c2 = MakeCommit("C2");

            var ancestor = _engine.FindCommonAncestor(c2.Hash!, c2.Hash!);
            Assert.Equal(c2.Hash, ancestor);
        }

        [Fact]
        public void DiffCommits_ShowsAddedFile()
        {
            WriteAndStageFile("a.txt", "hello");
            var c1 = MakeCommit("Add a.txt");

            WriteAndStageFile("b.txt", "world");
            var c2 = MakeCommit("Add b.txt");

            var diffs = _engine.DiffCommits(c1.Hash, c2.Hash);
            Assert.Single(diffs);
            Assert.Equal("b.txt", diffs[0].FilePath);
            Assert.Equal(DiffStatus.Added, diffs[0].Status);
        }

        [Fact]
        public void DiffCommits_ShowsModifiedFile()
        {
            WriteAndStageFile("a.txt", "original content");
            var c1 = MakeCommit("Original");

            WriteAndStageFile("a.txt", "modified content");
            var c2 = MakeCommit("Modified");

            var diffs = _engine.DiffCommits(c1.Hash, c2.Hash);
            Assert.Single(diffs);
            Assert.Equal(DiffStatus.Modified, diffs[0].Status);
        }

        [Fact]
        public void GetReachableObjects_ReturnsAllObjects()
        {
            WriteAndStageFile("a.txt", "a content");
            WriteAndStageFile("b.txt", "b content");
            var commit = MakeCommit("Two files");

            var objects = _engine.GetReachableObjects(commit.Hash!);
            // Should contain at minimum: commit + tree + 2 blobs = 4
            Assert.True(objects.Count >= 4, $"Expected at least 4 objects, got {objects.Count}");
        }

        [Fact]
        public void FastForwardMerge_AdvancesBranchTip()
        {
            // main: C1
            WriteAndStageFile("base.txt", "base");
            var c1 = MakeCommit("Base");

            // Create feature branch from C1
            _engine.CreateBranch("feature");
            _engine.CheckoutBranch("feature");

            // feature: C1 → C2 → C3
            WriteAndStageFile("feat.txt", "feature work");
            MakeCommit("Feature work");

            // Go back to main and fast-forward merge
            _engine.Store.SetHeadToBranch("main");
            var result = _engine.Merge("feature", "main", "Merge feature", _signingKey, _pubKeyBytes);

            Assert.True(result.Success);
            Assert.Contains("Fast-forward", result.Message);
        }
    }

    // ─── LargeFilePointer Tests ───────────────────────────────────────────────

    public class LargeFilePointerTests
    {
        [Fact]
        public void Serialize_Deserialize_RoundTrips()
        {
            var ptr = new LargeFilePointer
            {
                TotalSize = 12_345_678,
                ChunkHashes = new List<string> { "hash1", "hash2", "hash3" }
            };
            var json = ptr.ToJson();
            var restored = LargeFilePointer.FromJson(json);

            Assert.NotNull(restored);
            Assert.Equal(ptr.TotalSize, restored!.TotalSize);
            Assert.Equal(ptr.ChunkHashes, restored.ChunkHashes);
            Assert.Equal("lfs-pointer", restored.Type);
        }

        [Fact]
        public void FromJson_ReturnsNull_OnInvalidJson()
        {
            Assert.Null(LargeFilePointer.FromJson("not json at all"));
        }
    }

    // ─── Chunker Tests ────────────────────────────────────────────────────────

    public class ChunkerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly BlobStore _blobs;
        private readonly Chunker _chunker;

        public ChunkerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_chunk_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _blobs = new BlobStore(Path.Combine(_tempDir, "objects"));
            _chunker = new Chunker(_blobs, chunkSizeBytes: 1024); // 1KB chunks for testing
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void ChunkAndRestore_SmallFile_RoundTrips()
        {
            var srcPath = Path.Combine(_tempDir, "small.bin");
            var original = new byte[512];
            new Random(42).NextBytes(original);
            File.WriteAllBytes(srcPath, original);

            var ptr = _chunker.SaveLargeFile(srcPath);
            Assert.Single(ptr.ChunkHashes); // fits in one chunk

            var dstPath = Path.Combine(_tempDir, "restored.bin");
            _chunker.RestoreLargeFile(ptr, dstPath);

            Assert.Equal(original, File.ReadAllBytes(dstPath));
        }

        [Fact]
        public void ChunkAndRestore_MultiChunk_RoundTrips()
        {
            var srcPath = Path.Combine(_tempDir, "large.bin");
            var original = new byte[4096]; // 4x the chunk size
            new Random(99).NextBytes(original);
            File.WriteAllBytes(srcPath, original);

            var ptr = _chunker.SaveLargeFile(srcPath);
            Assert.Equal(4, ptr.ChunkHashes.Count);
            Assert.Equal(4096L, ptr.TotalSize);

            var dstPath = Path.Combine(_tempDir, "restored_large.bin");
            _chunker.RestoreLargeFile(ptr, dstPath);

            Assert.Equal(original, File.ReadAllBytes(dstPath));
        }
    }

    // ─── RepoConfig Tests ─────────────────────────────────────────────────────

    public class RepoConfigTests : IDisposable
    {
        private readonly string _tempDir;

        public RepoConfigTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "cf_cfg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() => Directory.Delete(_tempDir, recursive: true);

        [Fact]
        public void SaveAndLoad_RoundTrips()
        {
            var cfg = new RepoConfig
            {
                DefaultBranch = "main",
                LargeFileThresholdBytes = 50 * 1024 * 1024
            };
            cfg.Identity.Name = "Alice";
            cfg.Identity.Email = "alice@example.com";
            cfg.AddOrUpdate(new RemoteConfig
            {
                Name = "origin",
                Url = "http://minio:9000",
                Bucket = "myrepo",
                AccessKey = "user",
                SecretKey = "pass"
            });

            cfg.Save(_tempDir);
            var loaded = RepoConfig.Load(_tempDir);

            Assert.Equal("Alice", loaded.Identity.Name);
            Assert.Equal("alice@example.com", loaded.Identity.Email);
            Assert.Single(loaded.Remotes);
            Assert.Equal("origin", loaded.Remotes[0].Name);
            Assert.Equal("http://minio:9000", loaded.Remotes[0].Url);
        }

        [Fact]
        public void Load_ReturnsDefault_WhenNoFile()
        {
            var cfg = RepoConfig.Load(Path.GetTempPath() + "/nonexistent_" + Guid.NewGuid());
            Assert.NotNull(cfg);
            Assert.Empty(cfg.Remotes);
        }

        [Fact]
        public void AddOrUpdate_UpdatesExistingRemote()
        {
            var cfg = new RepoConfig();
            cfg.AddOrUpdate(new RemoteConfig { Name = "origin", Url = "http://old.url", Bucket = "b1" });
            cfg.AddOrUpdate(new RemoteConfig { Name = "origin", Url = "http://new.url", Bucket = "b2" });

            Assert.Single(cfg.Remotes);
            Assert.Equal("http://new.url", cfg.Remotes[0].Url);
            Assert.Equal("b2", cfg.Remotes[0].Bucket);
        }

        [Fact]
        public void RemoveRemote_DeletesEntry()
        {
            var cfg = new RepoConfig();
            cfg.AddOrUpdate(new RemoteConfig { Name = "origin", Url = "u1", Bucket = "b1" });
            cfg.AddOrUpdate(new RemoteConfig { Name = "upstream", Url = "u2", Bucket = "b2" });
            cfg.RemoveRemote("origin");
            Assert.Single(cfg.Remotes);
            Assert.Equal("upstream", cfg.Remotes[0].Name);
        }
    }
}
