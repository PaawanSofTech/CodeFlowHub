using System;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace CodeFlow.Crypto
{
    public static class CryptoUtils
    {
        private static readonly SignatureAlgorithm _algo = SignatureAlgorithm.Ed25519;

        public static byte[] Sign(Key privateKey, byte[] data)
        {
            return _algo.Sign(privateKey, data);
        }

        public static bool Verify(byte[] rawPublicKey, byte[] data, byte[] signature)
        {
            try
            {
                var pub = PublicKey.Import(_algo, rawPublicKey, KeyBlobFormat.RawPublicKey);
                return _algo.Verify(pub, data, signature);
            }
            catch { return false; }
        }

        public static bool Verify(PublicKey pub, byte[] data, byte[] signature)
        {
            try { return _algo.Verify(pub, data, signature); }
            catch { return false; }
        }
    }

    public static class HashUtil
    {
        public static string Sha256(byte[] data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var h = sha.ComputeHash(data);
            return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
        }

        public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

        /// <summary>Heuristic: infer object type from JSON content.</summary>
        public static string GetObjectType(string content)
        {
            if (content.Contains("\"treeHash\"") && content.Contains("\"parentHash\""))
                return "commit";
            if (content.Contains("\"entries\""))
                return "tree";
            if (content.Contains("\"type\":\"lfs-pointer\"") || content.Contains("\"chunkHashes\""))
                return "lfs-pointer";
            return "blob";
        }

        public static bool IsBinaryData(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            if (data[0] == '{' || data[0] == '[') return false;

            // BOM = text
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return false;

            // Magic bytes
            if (data.Length >= 4)
            {
                if (data[0] == 0x7F && data[1] == 0x45) return true; // ELF
                if (data[0] == 0x50 && data[1] == 0x4B) return true; // ZIP
                if (data[0] == 0x89 && data[1] == 0x50) return true; // PNG
                if (data[0] == 0xFF && data[1] == 0xD8) return true; // JPEG
                if (data[0] == 0x4D && data[1] == 0x5A) return true; // PE
                if (data[0] == 0x47 && data[1] == 0x49) return true; // GIF
            }

            // Heuristic: >15% non-printable in first 512 bytes
            int sample = Math.Min(512, data.Length);
            int nonPrint = 0;
            for (int i = 0; i < sample; i++)
            {
                byte b = data[i];
                if (b < 9 || (b > 13 && b < 32) || b == 127) nonPrint++;
            }
            return (double)nonPrint / sample > 0.15;
        }
    }
}
