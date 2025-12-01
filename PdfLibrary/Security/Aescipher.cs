using System.Security.Cryptography;

namespace PdfLibrary.Security;

/// <summary>
/// AES cipher implementation for PDF decryption (V=4 and V=5).
/// Uses .NET's built-in AES implementation in CBC mode.
/// </summary>
public static class AesCipher
{
    /// <summary>
    /// Decrypts data using AES-CBC.
    /// For PDF encryption, the IV is the first 16 bytes of the encrypted data.
    /// </summary>
    /// <param name="key">AES key (16 bytes for AES-128, 32 bytes for AES-256)</param>
    /// <param name="data">Encrypted data (IV + ciphertext)</param>
    /// <returns>Decrypted data</returns>
    public static byte[] Decrypt(byte[] key, byte[] data)
    {
        if (data.Length < 16)
            return data; // Too short to contain IV, return as-is

        // Extract IV (first 16 bytes) and ciphertext
        var iv = new byte[16];
        Array.Copy(data, 0, iv, 0, 16);

        var ciphertextLength = data.Length - 16;
        if (ciphertextLength == 0)
            return []; // No actual data after IV

        var ciphertext = new byte[ciphertextLength];
        Array.Copy(data, 16, ciphertext, 0, ciphertextLength);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // PDF handles padding manually

        using var decryptor = aes.CreateDecryptor();

        // AES requires input to be multiple of block size
        // If not, we need to pad it (this shouldn't happen with valid PDF data)
        var paddedLength = (ciphertext.Length + 15) / 16 * 16;
        if (paddedLength != ciphertext.Length)
        {
            var padded = new byte[paddedLength];
            Array.Copy(ciphertext, padded, ciphertext.Length);
            ciphertext = padded;
        }

        var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        // Remove PKCS#7 padding if present
        return RemovePkcs7Padding(decrypted);
    }

    /// <summary>
    /// Removes PKCS#7 padding from decrypted data.
    /// </summary>
    private static byte[] RemovePkcs7Padding(byte[] data)
    {
        if (data.Length == 0)
            return data;

        int paddingLength = data[data.Length - 1];

        // Validate padding
        if (paddingLength < 1 || paddingLength > 16 || paddingLength > data.Length)
            return data; // Invalid padding, return as-is

        // Verify all padding bytes are correct
        for (var i = data.Length - paddingLength; i < data.Length; i++)
        {
            if (data[i] != paddingLength)
                return data; // Invalid padding
        }

        var result = new byte[data.Length - paddingLength];
        Array.Copy(data, result, result.Length);
        return result;
    }

    /// <summary>
    /// Encrypts data using AES-CBC with PKCS#7 padding.
    /// Returns IV + ciphertext (IV is first 16 bytes).
    /// </summary>
    /// <param name="key">AES key (16 bytes for AES-128, 32 bytes for AES-256)</param>
    /// <param name="data">Plaintext data to encrypt</param>
    /// <param name="iv">Optional IV (16 bytes). If null, a random IV is generated.</param>
    /// <returns>IV + ciphertext</returns>
    public static byte[] Encrypt(byte[] key, byte[] data, byte[]? iv = null)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Use provided IV or generate random one
        if (iv != null)
        {
            if (iv.Length != 16)
                throw new ArgumentException("IV must be 16 bytes", nameof(iv));
            aes.IV = iv;
        }
        else
        {
            aes.GenerateIV();
        }

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(data, 0, data.Length);

        // Prepend IV to ciphertext
        var result = new byte[16 + ciphertext.Length];
        Array.Copy(aes.IV, 0, result, 0, 16);
        Array.Copy(ciphertext, 0, result, 16, ciphertext.Length);

        return result;
    }

    /// <summary>
    /// Encrypts data using AES-CBC without prepending IV.
    /// Used for encrypting file encryption keys where IV is all zeros.
    /// </summary>
    public static byte[] EncryptNoPrependIV(byte[] key, byte[] data, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        // Pad data to block size if needed
        var paddedLength = (data.Length + 15) / 16 * 16;
        var paddedData = data;
        if (paddedLength != data.Length)
        {
            paddedData = new byte[paddedLength];
            Array.Copy(data, paddedData, data.Length);
        }

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(paddedData, 0, paddedData.Length);
    }

    /// <summary>
    /// Computes SHA-256 hash (used in V=5 encryption).
    /// </summary>
    public static byte[] Sha256(byte[] data)
    {
        return SHA256.HashData(data);
    }

    /// <summary>
    /// Computes SHA-384 hash (used in V=5 encryption).
    /// </summary>
    public static byte[] Sha384(byte[] data)
    {
        return SHA384.HashData(data);
    }

    /// <summary>
    /// Computes SHA-512 hash (used in V=5 encryption).
    /// </summary>
    public static byte[] Sha512(byte[] data)
    {
        return SHA512.HashData(data);
    }
}
