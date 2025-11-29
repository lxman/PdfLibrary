using PdfLibrary.Security;

namespace PdfLibrary.Tests.Security;

public class PdfEncryptorTests
{
    private static readonly byte[] TestDocumentId =
    [
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10
    ];

    [Fact]
    public void Constructor_WithRc4_40_SetsCorrectVersionAndRevision()
    {
        // Arrange & Act
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Rc4_40,
            TestDocumentId);

        // Assert
        Assert.Equal(1, encryptor.Version);
        Assert.Equal(2, encryptor.Revision);
        Assert.Equal(40, encryptor.KeyLengthBits);
    }

    [Fact]
    public void Constructor_WithRc4_128_SetsCorrectVersionAndRevision()
    {
        // Arrange & Act
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Rc4_128,
            TestDocumentId);

        // Assert
        Assert.Equal(2, encryptor.Version);
        Assert.Equal(3, encryptor.Revision);
        Assert.Equal(128, encryptor.KeyLengthBits);
    }

    [Fact]
    public void Constructor_WithAes128_SetsCorrectVersionAndRevision()
    {
        // Arrange & Act
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes128,
            TestDocumentId);

        // Assert
        Assert.Equal(4, encryptor.Version);
        Assert.Equal(4, encryptor.Revision);
        Assert.Equal(128, encryptor.KeyLengthBits);
    }

    [Fact]
    public void Constructor_WithAes256_SetsCorrectVersionAndRevision()
    {
        // Arrange & Act
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        // Assert
        Assert.Equal(5, encryptor.Version);
        Assert.Equal(6, encryptor.Revision);
        Assert.Equal(256, encryptor.KeyLengthBits);
    }

    [Fact]
    public void Constructor_GeneratesOAndUValues()
    {
        // Arrange & Act
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        // Assert
        Assert.NotNull(encryptor.OValue);
        Assert.NotNull(encryptor.UValue);
        Assert.Equal(48, encryptor.OValue.Length); // V5 uses 48 bytes
        Assert.Equal(48, encryptor.UValue.Length);
    }

    [Fact]
    public void Constructor_WithAes256_GeneratesOEUEPerms()
    {
        // Arrange & Act
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        // Assert
        Assert.NotNull(encryptor.OEValue);
        Assert.NotNull(encryptor.UEValue);
        Assert.NotNull(encryptor.PermsValue);
        Assert.Equal(32, encryptor.OEValue.Length);
        Assert.Equal(32, encryptor.UEValue.Length);
        Assert.Equal(16, encryptor.PermsValue.Length);
    }

    [Fact]
    public void Constructor_WithEmptyOwnerPassword_UsesUserPassword()
    {
        // Should not throw - empty owner password is valid
        var encryptor = new PdfEncryptor(
            "user",
            "",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        Assert.NotNull(encryptor.OValue);
    }

    [Fact]
    public void Encrypt_WithEmptyData_ReturnsEmptyData()
    {
        // Arrange
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        // Act
        byte[] result = encryptor.Encrypt([], 1, 0);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Encrypt_WithAes256_ProducesEncryptedData()
    {
        // Arrange
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        byte[] plaintext = "Hello, World!"u8.ToArray();

        // Act
        byte[] encrypted = encryptor.Encrypt(plaintext, 1, 0);

        // Assert
        Assert.NotEqual(plaintext, encrypted);
        Assert.True(encrypted.Length >= plaintext.Length); // AES adds padding + IV
    }

    [Fact]
    public void Encrypt_SameDataDifferentObjects_ProducesDifferentCiphertext_ForAes128()
    {
        // For AES-128, each object gets a different key derived from object number
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes128,
            TestDocumentId);

        byte[] plaintext = "Hello, World!"u8.ToArray();

        // Act
        byte[] encrypted1 = encryptor.Encrypt(plaintext, 1, 0);
        byte[] encrypted2 = encryptor.Encrypt(plaintext, 2, 0);

        // Assert - different objects should produce different ciphertext
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void Encrypt_SameDataSameObject_ProducesDifferentCiphertext_DueToRandomIV()
    {
        // AES uses random IV, so even same plaintext should produce different ciphertext
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        byte[] plaintext = "Hello, World!"u8.ToArray();

        // Act
        byte[] encrypted1 = encryptor.Encrypt(plaintext, 1, 0);
        byte[] encrypted2 = encryptor.Encrypt(plaintext, 1, 0);

        // Assert - different IVs should produce different ciphertext
        Assert.NotEqual(encrypted1, encrypted2);
    }

    [Fact]
    public void EncryptString_EncryptsData()
    {
        // Arrange
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        byte[] plaintext = "Test string"u8.ToArray();

        // Act
        byte[] encrypted = encryptor.EncryptString(plaintext, 1, 0);

        // Assert
        Assert.NotEqual(plaintext, encrypted);
    }

    [Fact]
    public void EncryptStream_EncryptsData()
    {
        // Arrange
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            PdfPermissionFlags.All,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        byte[] plaintext = "Stream content"u8.ToArray();

        // Act
        byte[] encrypted = encryptor.EncryptStream(plaintext, 1, 0);

        // Assert
        Assert.NotEqual(plaintext, encrypted);
    }

    [Fact]
    public void Permissions_SetsCorrectFlags()
    {
        // Arrange
        var flags = PdfPermissionFlags.Print | PdfPermissionFlags.CopyContent;

        // Act
        var encryptor = new PdfEncryptor(
            "user",
            "owner",
            flags,
            PdfEncryptionMethod.Aes256,
            TestDocumentId);

        // Assert
        Assert.True(encryptor.Permissions.CanPrint);
        Assert.True(encryptor.Permissions.CanCopyContent);
        Assert.False(encryptor.Permissions.CanModifyContents);
    }
}
