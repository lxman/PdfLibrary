using PdfLibrary.Security;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Security;

/// <summary>
/// Tests for PdfDocument security-related functionality.
/// </summary>
public class PdfDocumentSecurityTests
{
    /// <summary>
    /// Gets the path to the encrypted PDF test files directory.
    /// </summary>
    private static string GetEncryptedTestFilesPath()
    {
        string path = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "TestPDFs", "targeted_custom_generated", "golden");
        return Path.GetFullPath(path);
    }

    [Fact]
    public void UnencryptedDocument_ShouldHaveFullPermissions()
    {
        // Arrange - load any unencrypted PDF
        string testFilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "pdf20examples", "Simple PDF 2.0 file.pdf");

        testFilePath = Path.GetFullPath(testFilePath);

        // Skip if file doesn't exist
        if (!File.Exists(testFilePath))
        {
            return; // Skip test if no unencrypted PDF available
        }

        // Act
        using PdfDocument document = PdfDocument.Load(testFilePath);

        // Assert
        Assert.False(document.IsEncrypted);
        Assert.Null(document.Decryptor);

        // Unencrypted documents should have all permissions
        PdfPermissions permissions = document.Permissions;
        Assert.True(permissions.CanPrint);
        Assert.True(permissions.CanCopyContent);
        Assert.True(permissions.CanModifyContents);
        Assert.True(permissions.CanModifyAnnotations);
        Assert.True(permissions.CanFillForms);
        Assert.True(permissions.CanExtractForAccessibility);
        Assert.True(permissions.CanAssembleDocument);
        Assert.True(permissions.CanPrintHighQuality);
    }

    [Fact]
    public void Load_WithPassword_ShouldAcceptEmptyStringPassword()
    {
        // Arrange - load any unencrypted PDF with explicit empty password
        string testFilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "pdf20examples", "Simple PDF 2.0 file.pdf");

        testFilePath = Path.GetFullPath(testFilePath);

        // Skip if file doesn't exist
        if (!File.Exists(testFilePath))
        {
            return;
        }

        // Act - should not throw even with password parameter
        using PdfDocument document = PdfDocument.Load(testFilePath, "");

        // Assert
        Assert.NotNull(document);
        Assert.False(document.IsEncrypted);
    }

    [Fact]
    public void Load_FromStream_WithPassword_ShouldWork()
    {
        // Arrange
        string testFilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "pdf20examples", "Simple PDF 2.0 file.pdf");

        testFilePath = Path.GetFullPath(testFilePath);

        if (!File.Exists(testFilePath))
        {
            return;
        }

        // Act
        using FileStream stream = File.OpenRead(testFilePath);
        using PdfDocument document = PdfDocument.Load(stream, "", leaveOpen: false);

        // Assert
        Assert.NotNull(document);
        Assert.False(document.IsEncrypted);
    }

    [Fact]
    public void Rc4_128EncryptedDocument_WithEmptyPassword_ShouldLoad()
    {
        // Arrange
        string testFilePath = Path.Combine(GetEncryptedTestFilesPath(), "EncryptedRc4_128_EmptyPassword.pdf");

        if (!File.Exists(testFilePath))
        {
            return; // Skip if the test file not generated yet
        }

        // Act
        using PdfDocument document = PdfDocument.Load(testFilePath);

        // Assert
        Assert.True(document.IsEncrypted);
        Assert.NotNull(document.Decryptor);
        Assert.True(document.Decryptor.IsDecrypted);
        Assert.Equal(PdfEncryptionMethod.Rc4_128, document.Decryptor.Method);
    }

    [Fact]
    public void Rc4_128EncryptedDocument_WithCorrectPassword_ShouldLoad()
    {
        // Arrange
        string testFilePath = Path.Combine(GetEncryptedTestFilesPath(), "EncryptedRc4_128_WithPassword.pdf");
        string password = "test123";

        if (!File.Exists(testFilePath))
        {
            return; // Skip if the test file not generated yet
        }

        // Act
        using PdfDocument document = PdfDocument.Load(testFilePath, password);

        // Assert
        Assert.True(document.IsEncrypted);
        Assert.NotNull(document.Decryptor);
        Assert.True(document.Decryptor.IsDecrypted);
        Assert.Equal(PdfEncryptionMethod.Rc4_128, document.Decryptor.Method);
    }

    [Fact]
    public void Rc4_128EncryptedDocument_WithWrongPassword_ShouldThrow()
    {
        // Arrange
        string testFilePath = Path.Combine(GetEncryptedTestFilesPath(), "EncryptedRc4_128_WithPassword.pdf");
        string wrongPassword = "wrongpassword";

        if (!File.Exists(testFilePath))
        {
            return; // Skip if the test file not generated yet
        }

        // Act & Assert
        Assert.Throws<PdfSecurityException>(() =>
            PdfDocument.Load(testFilePath, wrongPassword));
    }

    [Fact]
    public void Aes128EncryptedDocument_WithEmptyPassword_ShouldLoad()
    {
        // Arrange
        string testFilePath = Path.Combine(GetEncryptedTestFilesPath(), "EncryptedAes128_EmptyPassword.pdf");

        if (!File.Exists(testFilePath))
        {
            return; // Skip if the test file not generated yet
        }

        // Act
        using PdfDocument document = PdfDocument.Load(testFilePath);

        // Assert
        Assert.True(document.IsEncrypted);
        Assert.NotNull(document.Decryptor);
        Assert.True(document.Decryptor.IsDecrypted);
        Assert.Equal(PdfEncryptionMethod.Aes128, document.Decryptor.Method);
    }

    [Fact]
    public void Aes128EncryptedDocument_WithCorrectPassword_ShouldLoad()
    {
        // Arrange
        string testFilePath = Path.Combine(GetEncryptedTestFilesPath(), "EncryptedAes128_WithPassword.pdf");
        string password = "test123";

        if (!File.Exists(testFilePath))
        {
            return; // Skip if the test file not generated yet
        }

        // Act
        using PdfDocument document = PdfDocument.Load(testFilePath, password);

        // Assert
        Assert.True(document.IsEncrypted);
        Assert.NotNull(document.Decryptor);
        Assert.True(document.Decryptor.IsDecrypted);
        Assert.Equal(PdfEncryptionMethod.Aes128, document.Decryptor.Method);
    }

    [Fact]
    public void Aes128EncryptedDocument_WithWrongPassword_ShouldThrow()
    {
        // Arrange
        string testFilePath = Path.Combine(GetEncryptedTestFilesPath(), "EncryptedAes128_WithPassword.pdf");
        string wrongPassword = "wrongpassword";

        if (!File.Exists(testFilePath))
        {
            return; // Skip if the test file not generated yet
        }

        // Act & Assert
        Assert.Throws<PdfSecurityException>(() =>
            PdfDocument.Load(testFilePath, wrongPassword));
    }

    [Fact]
    public void EncryptedDocument_ShouldHaveAllPermissions_WhenAllFlagsSet()
    {
        // Arrange - our test PDFs are generated with -4 (all permissions)
        string testFilePath = Path.Combine(GetEncryptedTestFilesPath(), "EncryptedRc4_128_EmptyPassword.pdf");

        if (!File.Exists(testFilePath))
        {
            return; // Skip if the test file not generated yet
        }

        // Act
        using PdfDocument document = PdfDocument.Load(testFilePath);

        // Assert
        Assert.True(document.IsEncrypted);
        PdfPermissions permissions = document.Permissions;
        Assert.True(permissions.CanPrint);
        Assert.True(permissions.CanCopyContent);
        Assert.True(permissions.CanModifyContents);
        Assert.True(permissions.CanModifyAnnotations);
    }
}
