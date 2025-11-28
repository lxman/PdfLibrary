using System.Security.Cryptography;
using PdfLibrary.Security;

namespace PdfLibrary.Tests.Security;

public class AesCipherTests
{
    [Fact]
    public void Decrypt_WithValidAes128Data_ShouldDecryptCorrectly()
    {
        // Arrange - create known plaintext and encrypt it
        byte[] plaintext = "Hello, PDF World!"u8.ToArray();
        byte[] key = new byte[16]; // 128-bit key (all zeros for test)
        byte[] iv = new byte[16];  // IV (all zeros for test)

        // Encrypt the plaintext
        byte[] encrypted = EncryptAes(plaintext, key, iv);

        // Prepend IV to encrypted data (as PDF does)
        byte[] encryptedWithIv = new byte[iv.Length + encrypted.Length];
        Array.Copy(iv, 0, encryptedWithIv, 0, iv.Length);
        Array.Copy(encrypted, 0, encryptedWithIv, iv.Length, encrypted.Length);

        // Act
        byte[] decrypted = AesCipher.Decrypt(key, encryptedWithIv);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_WithValidAes256Data_ShouldDecryptCorrectly()
    {
        // Arrange - create known plaintext and encrypt it
        byte[] plaintext = "Testing AES-256 encryption for PDF!"u8.ToArray();
        byte[] key = new byte[32]; // 256-bit key (all zeros for test)
        byte[] iv = new byte[16];  // IV (all zeros for test)

        // Encrypt the plaintext
        byte[] encrypted = EncryptAes(plaintext, key, iv);

        // Prepend IV to encrypted data
        byte[] encryptedWithIv = new byte[iv.Length + encrypted.Length];
        Array.Copy(iv, 0, encryptedWithIv, 0, iv.Length);
        Array.Copy(encrypted, 0, encryptedWithIv, iv.Length, encrypted.Length);

        // Act
        byte[] decrypted = AesCipher.Decrypt(key, encryptedWithIv);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_WithDataTooShort_ShouldReturnOriginal()
    {
        // Arrange - data shorter than IV (16 bytes)
        byte[] shortData = [1, 2, 3, 4, 5];
        byte[] key = new byte[16];

        // Act
        byte[] result = AesCipher.Decrypt(key, shortData);

        // Assert
        Assert.Equal(shortData, result);
    }

    [Fact]
    public void Decrypt_WithOnlyIV_ShouldReturnEmpty()
    {
        // Arrange - exactly 16 bytes (IV only, no ciphertext)
        byte[] ivOnly = new byte[16];
        byte[] key = new byte[16];

        // Act
        byte[] result = AesCipher.Decrypt(key, ivOnly);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Decrypt_WithEmptyData_ShouldReturnEmpty()
    {
        // Arrange
        byte[] emptyData = [];
        byte[] key = new byte[16];

        // Act
        byte[] result = AesCipher.Decrypt(key, emptyData);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Decrypt_WithRandomIV_ShouldDecryptCorrectly()
    {
        // Arrange - use random IV
        byte[] plaintext = "Random IV test data for PDF encryption"u8.ToArray();
        byte[] key = new byte[16];
        RandomNumberGenerator.Fill(key);
        byte[] iv = new byte[16];
        RandomNumberGenerator.Fill(iv);

        // Encrypt the plaintext
        byte[] encrypted = EncryptAes(plaintext, key, iv);

        // Prepend IV to encrypted data
        byte[] encryptedWithIv = new byte[iv.Length + encrypted.Length];
        Array.Copy(iv, 0, encryptedWithIv, 0, iv.Length);
        Array.Copy(encrypted, 0, encryptedWithIv, iv.Length, encrypted.Length);

        // Act
        byte[] decrypted = AesCipher.Decrypt(key, encryptedWithIv);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Decrypt_WithPkcs7Padding_ShouldRemovePadding()
    {
        // Arrange - plaintext that requires padding (not multiple of 16)
        byte[] plaintext = "Short"u8.ToArray(); // 5 bytes, will be padded to 16
        byte[] key = new byte[16];
        byte[] iv = new byte[16];

        byte[] encrypted = EncryptAes(plaintext, key, iv);
        byte[] encryptedWithIv = new byte[iv.Length + encrypted.Length];
        Array.Copy(iv, 0, encryptedWithIv, 0, iv.Length);
        Array.Copy(encrypted, 0, encryptedWithIv, iv.Length, encrypted.Length);

        // Act
        byte[] decrypted = AesCipher.Decrypt(key, encryptedWithIv);

        // Assert - should have padding removed
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Sha256_ShouldComputeCorrectHash()
    {
        // Arrange
        byte[] data = "test"u8.ToArray();
        // Known SHA-256 hash of "test"
        byte[] expectedHash = Convert.FromHexString(
            "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08");

        // Act
        byte[] hash = AesCipher.Sha256(data);

        // Assert
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void Sha384_ShouldComputeCorrectHash()
    {
        // Arrange
        byte[] data = "test"u8.ToArray();
        // Known SHA-384 hash of "test"
        byte[] expectedHash = Convert.FromHexString(
            "768412320f7b0aa5812fce428dc4706b3cae50e02a64caa16a782249bfe8efc4b7ef1ccb126255d196047dfedf17a0a9");

        // Act
        byte[] hash = AesCipher.Sha384(data);

        // Assert
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void Sha512_ShouldComputeCorrectHash()
    {
        // Arrange
        byte[] data = "test"u8.ToArray();
        // Known SHA-512 hash of "test"
        byte[] expectedHash = Convert.FromHexString(
            "ee26b0dd4af7e749aa1a8ee3c10ae9923f618980772e473f8819a5d4940e0db27ac185f8a0e1d5f84f88bc887fd67b143732c304cc5fa9ad8e6f57f50028a8ff");

        // Act
        byte[] hash = AesCipher.Sha512(data);

        // Assert
        Assert.Equal(expectedHash, hash);
    }

    [Fact]
    public void Sha256_WithEmptyData_ShouldComputeCorrectHash()
    {
        // Arrange
        byte[] data = [];
        // Known SHA-256 hash of empty string
        byte[] expectedHash = Convert.FromHexString(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        // Act
        byte[] hash = AesCipher.Sha256(data);

        // Assert
        Assert.Equal(expectedHash, hash);
    }

    /// <summary>
    /// Helper method to encrypt data using AES-CBC with PKCS7 padding.
    /// </summary>
    private static byte[] EncryptAes(byte[] plaintext, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }
}
