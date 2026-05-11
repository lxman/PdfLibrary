using System.Security.Cryptography;

namespace PdfLibrary.Security;

/// <summary>
/// ISO 32000-2:2020 Algorithm 2.B — the hash function used by AES-256 (V=5, R=6)
/// password verification and key derivation. Lives in a shared helper because the
/// encrypt and decrypt sides must agree byte-for-byte; previously each side carried
/// its own implementation and they drifted (the decryptor's was a SHA-256-only stub),
/// breaking every AES-256 round-trip.
/// </summary>
internal static class AesV5Hash
{
    /// <summary>
    /// Computes the 32-byte hash used for password validation (against /U or /O salt+hash)
    /// and key derivation (against /UE or /OE key salt).
    /// </summary>
    /// <param name="password">Password bytes (UTF-8, truncated to 127 bytes by the caller).</param>
    /// <param name="salt">8-byte salt (validation or key salt).</param>
    /// <param name="userKey">For owner password operations, the 48-byte /U value; otherwise null.</param>
    public static byte[] Compute(byte[] password, byte[] salt, byte[]? userKey)
    {
        // Initial K = SHA-256(password || salt || userKey?)
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(password);
        sha256.AppendData(salt);
        if (userKey != null)
            sha256.AppendData(userKey);

        byte[] k = sha256.GetHashAndReset();

        // Round loop: at least 64 rounds, terminating when round >= k[^1] + 32.
        // Each round AES-CBC encrypts K1 (= password+K+userKey repeated 64x) with
        // K[0..15] as the key and K[16..31] as the IV, then re-hashes the result
        // with SHA-256/384/512 depending on (sum of first 16 output bytes) mod 3.
        var round = 0;
        while (round < 64 || round < k[^1] + 32)
        {
            int k1Size = password.Length + k.Length + (userKey?.Length ?? 0);
            var k1 = new byte[k1Size * 64];
            for (var i = 0; i < 64; i++)
            {
                int offset = i * k1Size;
                Array.Copy(password, 0, k1, offset, password.Length);
                Array.Copy(k, 0, k1, offset + password.Length, k.Length);
                if (userKey != null)
                    Array.Copy(userKey, 0, k1, offset + password.Length + k.Length, userKey.Length);
            }

            var aesKey = new byte[16];
            var aesIv = new byte[16];
            Array.Copy(k, 0, aesKey, 0, 16);
            Array.Copy(k, 16, aesIv, 0, 16);

            byte[] e = AesCipher.EncryptNoPrependIV(aesKey, k1, aesIv);

            var sum = 0;
            for (var i = 0; i < 16; i++)
                sum += e[i];

            k = (sum % 3) switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e)
            };

            round++;
        }

        var result = new byte[32];
        Array.Copy(k, result, 32);
        return result;
    }
}
