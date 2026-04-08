using System;
using System.IO;
using NSec.Cryptography;

namespace CodeFlow.Crypto
{
    public static class KeyManager
    {
        private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;
        private const string KeyDir = ".codeflow/keys";
        private const string PrivFile = ".codeflow/keys/private.key";
        private const string PubFile = ".codeflow/keys/public.key";

        public static void EnsureKeysDirectory()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrivFile)!);
        }

        public static (byte[] privateRaw, byte[] publicRaw) GenerateAndSaveKeyPair()
        {
            EnsureKeysDirectory();
            var creation = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
            using var key = new Key(Algorithm, creation);
            var priv = key.Export(KeyBlobFormat.RawPrivateKey);
            var pub = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            // Store private key with restricted permissions
            File.WriteAllBytes(PrivFile, priv);
            File.WriteAllBytes(PubFile, pub);
            return (priv, pub);
        }

        public static bool KeysExist() => File.Exists(PrivFile) && File.Exists(PubFile);

        public static (byte[] privateRaw, byte[] publicRaw) LoadKeyPair()
        {
            if (!KeysExist())
                throw new InvalidOperationException("Keys not found. Run 'codeflow keygen' first.");
            return (File.ReadAllBytes(PrivFile), File.ReadAllBytes(PubFile));
        }

        public static byte[] GetPublicKeyBytes()
        {
            if (!File.Exists(PubFile))
                throw new InvalidOperationException("Public key not found. Run 'codeflow keygen' first.");
            return File.ReadAllBytes(PubFile);
        }

        public static string GetPublicKeyBase64() => Convert.ToBase64String(GetPublicKeyBytes());

        public static Key ImportPrivateKey(byte[] raw) =>
            Key.Import(Algorithm, raw, KeyBlobFormat.RawPrivateKey,
                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        public static PublicKey ImportPublicKey(byte[] raw) =>
            PublicKey.Import(Algorithm, raw, KeyBlobFormat.RawPublicKey);
    }
}
