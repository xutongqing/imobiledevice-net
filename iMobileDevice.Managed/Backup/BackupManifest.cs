namespace iMobileDevice.Managed.Backup;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using iMobileDevice.Managed.Plist;

/// <summary>Prepares a readable manifest for the device's List operation using the caller's backup password.</summary>
internal static class BackupManifest
{
    public static async Task PrepareListAsync(BackupStorage store, string? password, CancellationToken token)
    {
        var manifest = BackupStorage.ValidateSnapshot(store.Snapshot);
        Directory.CreateDirectory(store.WorkingSnapshot);
        foreach (string name in new[] { "Info.plist", "Status.plist" })
            File.Copy(BackupStorage.Resolve(store.Snapshot, name), BackupStorage.Resolve(store.WorkingSnapshot, name));
        bool encrypted = manifest.GetBoolean("IsEncrypted") == true;
        string database = File.Exists(BackupStorage.Resolve(store.Snapshot, "Manifest.db")) ? "Manifest.db" : "Manifest.mbdb";
        string source = BackupStorage.Resolve(store.Snapshot, database);
        string dest = BackupStorage.Resolve(store.WorkingSnapshot, database);
        if (encrypted)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("The encrypted backup requires its password.", nameof(password));
            if (database != "Manifest.db") throw new NotSupportedException("Listing encrypted legacy MBDB backups is unsupported.");
            byte[] key = UnlockManifestKey(manifest, password);
            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = new byte[16];
                aes.Padding = PaddingMode.None;
                using var decrypt = aes.CreateDecryptor();
                await using var input = File.OpenRead(source);
                using var plaintext = new CryptoStream(input, decrypt, CryptoStreamMode.Read);
                await using var output = File.Create(dest);
                await plaintext.CopyToAsync(output, token).ConfigureAwait(false);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            manifest["IsEncrypted"] = false;
            manifest.Remove("ManifestKey");
        }
        else await BackupStorage.CopyAsync(source, dest, token).ConfigureAwait(false);
        await File.WriteAllBytesAsync(BackupStorage.Resolve(store.WorkingSnapshot, "Manifest.plist"),
            PlistSerializer.WriteBinary(manifest), token).ConfigureAwait(false);
    }

    internal static byte[] UnlockManifestKey(PlistDictionary manifest, string password)
    {
        byte[] bag = manifest.GetData("BackupKeyBag") ?? throw new InvalidDataException("Missing backup keybag.");
        byte[] wrapped = manifest.GetData("ManifestKey") ?? throw new InvalidDataException("Missing manifest key.");
        if (wrapped.Length != 44) throw new InvalidDataException("Invalid manifest key length.");
        uint requiredClass = BinaryPrimitives.ReadUInt32LittleEndian(wrapped);
        byte[]? salt = null, doubleSalt = null, classKey = null;
        int iterations = 0, doubleIterations = 0;
        uint? currentClass = null;
        bool passwordWrapped = false;
        for (int pos = 0; pos < bag.Length;)
        {
            if (bag.Length - pos < 8) throw new InvalidDataException("Truncated keybag tag.");
            string tag = Encoding.ASCII.GetString(bag, pos, 4);
            int length = BinaryPrimitives.ReadInt32BigEndian(bag.AsSpan(pos + 4));
            pos += 8;
            if (length < 0 || length > bag.Length - pos) throw new InvalidDataException("Invalid keybag tag length.");
            var data = bag.AsSpan(pos, length);
            switch (tag)
            {
                case "SALT": salt = data.ToArray(); break;
                case "DPSL": doubleSalt = data.ToArray(); break;
                case "ITER": iterations = BinaryPrimitives.ReadInt32BigEndian(data); break;
                case "DPIC": doubleIterations = BinaryPrimitives.ReadInt32BigEndian(data); break;
                case "UUID": currentClass = null; passwordWrapped = false; break;
                case "CLAS": currentClass = BinaryPrimitives.ReadUInt32BigEndian(data); break;
                case "WRAP": passwordWrapped = (BinaryPrimitives.ReadUInt32BigEndian(data) & 2) != 0; break;
                case "WPKY" when currentClass == requiredClass && passwordWrapped: classKey = data.ToArray(); break;
            }
            pos += length;
        }
        if (salt == null || iterations <= 0 || classKey == null || (doubleSalt != null && doubleIterations <= 0))
            throw new InvalidDataException("Incomplete keybag parameters.");
        byte[] secret = Encoding.UTF8.GetBytes(password);
        try
        {
            if (doubleSalt != null)
            {
                byte[] stronger = Rfc2898DeriveBytes.Pbkdf2(secret, doubleSalt, doubleIterations, HashAlgorithmName.SHA256, 32);
                CryptographicOperations.ZeroMemory(secret);
                secret = stronger;
            }
            byte[] wrapping = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA1, 32);
            try
            {
                byte[] key = Unwrap(wrapping, classKey);
                try { return Unwrap(key, wrapped.AsSpan(4)); }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            finally { CryptographicOperations.ZeroMemory(wrapping); }
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    // RFC 3394 AES key unwrap, including the integrity check for an incorrect password.
    private static byte[] Unwrap(byte[] kek, ReadOnlySpan<byte> wrapped)
    {
        if (wrapped.Length != 40) throw new InvalidDataException("Expected a wrapped 256-bit key.");
        using var aes = Aes.Create();
        aes.Key = kek;
        ulong a = BinaryPrimitives.ReadUInt64BigEndian(wrapped);
        byte[] key = wrapped[8..].ToArray();
        Span<byte> block = stackalloc byte[16];
        Span<byte> plain = stackalloc byte[16];
        for (int j = 5; j >= 0; j--)
        {
            for (int i = 4; i > 0; i--)
            {
                BinaryPrimitives.WriteUInt64BigEndian(block, a ^ (uint)(4 * j + i));
                key.AsSpan((i - 1) * 8, 8).CopyTo(block[8..]);
                aes.DecryptEcb(block, plain, PaddingMode.None);
                a = BinaryPrimitives.ReadUInt64BigEndian(plain);
                plain[8..].CopyTo(key.AsSpan((i - 1) * 8));
            }
        }
        CryptographicOperations.ZeroMemory(block);
        CryptographicOperations.ZeroMemory(plain);
        if (a == 0xa6a6a6a6a6a6a6a6UL) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new CryptographicException("The supplied password did not unlock this backup.");
    }
}
