using System.Security.Cryptography;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Security;

namespace PdfLibrary.Tests.Security;

public class PdfDecryptorTests
{
    /// <summary>
    /// Creates a minimal encryption dictionary for testing.
    /// </summary>
    private static PdfDictionary CreateEncryptionDict(
        int version,
        int revision,
        int keyLength,
        byte[] oValue,
        byte[] uValue,
        int permissions)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Filter")] = new PdfName("Standard"),
            [new PdfName("V")] = new PdfInteger(version),
            [new PdfName("R")] = new PdfInteger(revision),
            [new PdfName("Length")] = new PdfInteger(keyLength),
            [new PdfName("O")] = new PdfString(oValue),
            [new PdfName("U")] = new PdfString(uValue),
            [new PdfName("P")] = new PdfInteger(permissions)
        };

        return dict;
    }

    /// <summary>
    /// Creates a V=4 encryption dictionary with crypt filters for AES-128.
    /// </summary>
    private static PdfDictionary CreateAes128EncryptionDict(
        byte[] oValue,
        byte[] uValue,
        int permissions)
    {
        var stdCf = new PdfDictionary
        {
            [new PdfName("CFM")] = new PdfName("AESV2"),
            [new PdfName("Length")] = new PdfInteger(16),
            [new PdfName("AuthEvent")] = new PdfName("DocOpen")
        };

        var cf = new PdfDictionary
        {
            [new PdfName("StdCF")] = stdCf
        };

        var dict = new PdfDictionary
        {
            [new PdfName("Filter")] = new PdfName("Standard"),
            [new PdfName("V")] = new PdfInteger(4),
            [new PdfName("R")] = new PdfInteger(4),
            [new PdfName("Length")] = new PdfInteger(128),
            [new PdfName("O")] = new PdfString(oValue),
            [new PdfName("U")] = new PdfString(uValue),
            [new PdfName("P")] = new PdfInteger(permissions),
            [new PdfName("CF")] = cf,
            [new PdfName("StmF")] = new PdfName("StdCF"),
            [new PdfName("StrF")] = new PdfName("StdCF")
        };

        return dict;
    }

    [Fact]
    public void Constructor_WithInvalidPassword_ShouldThrowSecurityException()
    {
        // Arrange - create an encryption dict that won't validate
        byte[] oValue = new byte[32];
        byte[] uValue = new byte[32];
        byte[] documentId = new byte[16];

        // Fill with random values that won't match any password
        Random.Shared.NextBytes(oValue);
        Random.Shared.NextBytes(uValue);
        Random.Shared.NextBytes(documentId);

        PdfDictionary encryptDict = CreateEncryptionDict(
            version: 2,
            revision: 3,
            keyLength: 128,
            oValue: oValue,
            uValue: uValue,
            permissions: -4);

        // Act & Assert
        Assert.Throws<PdfSecurityException>(() =>
            new PdfDecryptor(encryptDict, documentId, "wrongpassword"));
    }

    [Fact]
    public void Constructor_WithNullEncryptDict_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PdfDecryptor(null!, [], ""));
    }

    [Fact]
    public void Constructor_WithNullDocumentId_ShouldThrow()
    {
        // Arrange
        var encryptDict = new PdfDictionary();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PdfDecryptor(encryptDict, null!, ""));
    }

    [Fact]
    public void Constructor_WithMissingOValue_ShouldThrow()
    {
        // Arrange - missing /O value
        var encryptDict = new PdfDictionary
        {
            [new PdfName("V")] = new PdfInteger(2),
            [new PdfName("R")] = new PdfInteger(3),
            [new PdfName("U")] = new PdfString(new byte[32]),
            [new PdfName("P")] = new PdfInteger(-4)
        };

        // Act & Assert
        var ex = Assert.Throws<PdfSecurityException>(() =>
            new PdfDecryptor(encryptDict, new byte[16], ""));
        Assert.Contains("/O", ex.Message);
    }

    [Fact]
    public void Constructor_WithMissingUValue_ShouldThrow()
    {
        // Arrange - missing /U value
        var encryptDict = new PdfDictionary
        {
            [new PdfName("V")] = new PdfInteger(2),
            [new PdfName("R")] = new PdfInteger(3),
            [new PdfName("O")] = new PdfString(new byte[32]),
            [new PdfName("P")] = new PdfInteger(-4)
        };

        // Act & Assert
        var ex = Assert.Throws<PdfSecurityException>(() =>
            new PdfDecryptor(encryptDict, new byte[16], ""));
        Assert.Contains("/U", ex.Message);
    }

    [Fact]
    public void Constructor_WithUnsupportedVersion_ShouldThrow()
    {
        // Arrange - V=99 is not a valid version
        var encryptDict = new PdfDictionary
        {
            [new PdfName("V")] = new PdfInteger(99),
            [new PdfName("R")] = new PdfInteger(2),
            [new PdfName("O")] = new PdfString(new byte[32]),
            [new PdfName("U")] = new PdfString(new byte[32]),
            [new PdfName("P")] = new PdfInteger(-4)
        };

        // Act & Assert
        Assert.Throws<PdfSecurityException>(() =>
            new PdfDecryptor(encryptDict, new byte[16], ""));
    }

}

public class PdfSecurityExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_ShouldSetMessage()
    {
        // Arrange & Act
        var ex = new PdfSecurityException("Test message");

        // Assert
        Assert.Equal("Test message", ex.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndInner_ShouldSetBoth()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner");

        // Act
        var ex = new PdfSecurityException("Outer message", inner);

        // Assert
        Assert.Equal("Outer message", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}
