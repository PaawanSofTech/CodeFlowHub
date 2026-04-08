using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeFlow.Crypto;

namespace CodeFlow.Core.Models
{
    /// <summary>
    /// Represents a commit object in the DAG. Supports multiple parents (for merge commits).
    /// </summary>
    public class Commit
    {
        // === Payload fields (included in signature) ===
        public string Message { get; set; } = "";
        public string Author { get; set; } = "";   // base64 public key
        public string AuthorName { get; set; } = "";
        public string AuthorEmail { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string? ParentHash { get; set; }           // primary parent (null = root commit)
        public List<string> ParentHashes { get; set; } = new(); // all parents (>1 = merge commit)
        public string[] Changes { get; set; } = Array.Empty<string>();
        public string TreeHash { get; set; } = "";
        public string Branch { get; set; } = "main";

        // === Signature metadata (filled after signing) ===
        public string? Signature { get; set; }    // Base64 Ed25519 signature
        public string? PublicKey { get; set; }    // Base64 raw public key

        // === Computed / stored hash ===
        public string? Hash { get; set; }

        [JsonIgnore]
        private static readonly JsonSerializerOptions _opts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [JsonIgnore]
        private static readonly JsonSerializerOptions _prettyOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        /// <summary>Canonical JSON used for signing (excludes Signature, PublicKey, Hash).</summary>
        public string PayloadJson()
        {
            var payload = new
            {
                message = Message,
                author = Author,
                authorName = AuthorName,
                authorEmail = AuthorEmail,
                timestamp = Timestamp.ToUniversalTime(),
                parentHash = ParentHash,
                parentHashes = ParentHashes,
                treeHash = TreeHash,
                changes = Changes,
                branch = Branch
            };
            return JsonSerializer.Serialize(payload, _opts);
        }

        public string ToJson() => JsonSerializer.Serialize(this, _opts);

        public static Commit? FromJson(string json)
        {
            var c = JsonSerializer.Deserialize<Commit>(json, _opts);
            if (c != null && c.ParentHash != null && c.ParentHashes.Count == 0)
                c.ParentHashes.Add(c.ParentHash); // backcompat: single parent -> list
            return c;
        }

        public void SignWithKey(NSec.Cryptography.Key privateKey)
        {
            var payload = PayloadJson();
            var bytes = Encoding.UTF8.GetBytes(payload);
            var sig = CryptoUtils.Sign(privateKey, bytes);
            var pubRaw = privateKey.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey);
            Signature = Convert.ToBase64String(sig);
            PublicKey = Convert.ToBase64String(pubRaw);
        }

        public bool VerifySignature()
        {
            if (string.IsNullOrEmpty(Signature) || string.IsNullOrEmpty(PublicKey))
                return false;
            var payload = PayloadJson();
            var bytes = Encoding.UTF8.GetBytes(payload);
            var sig = Convert.FromBase64String(Signature);
            var pub = Convert.FromBase64String(PublicKey);
            return CryptoUtils.Verify(pub, bytes, sig);
        }

        public bool IsMergeCommit => ParentHashes.Count > 1;
        public string ShortHash => Hash?.Substring(0, Math.Min(8, Hash.Length)) ?? "????????";
    }
}
