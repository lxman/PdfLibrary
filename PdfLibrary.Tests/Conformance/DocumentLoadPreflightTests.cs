using PdfLibrary.Conformance;
using PdfLibrary.Integration.Documents;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
///     Pins the synthetic <c>document-load</c> boundary. It is emitted only by the preflight overloads
///     that own document loading; an already-loaded document can never produce it, and filesystem I/O
///     before the loader is deliberately still an exception for the path overload's caller to handle.
/// </summary>
public sealed class DocumentLoadPreflightTests
{
    [Theory]
    [InlineData(ConformanceProfile.PdfA2b)]
    [InlineData(ConformanceProfile.PdfA2u)]
    [InlineData(ConformanceProfile.PdfA3b)]
    [InlineData(ConformanceProfile.PdfX4)]
    [InlineData(ConformanceProfile.PdfUA1)]
    public void Malformed_bytes_return_one_document_load_error_with_the_loader_diagnostic(
        ConformanceProfile profile)
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        Exception loadError = Assert.ThrowsAny<Exception>(() =>
            PdfDocument.Load(new MemoryStream(bytes, writable: false), password: string.Empty));

        PreflightResult result = Preflighter.Check(bytes, profile);

        Finding finding = Assert.Single(result.Findings);
        Assert.False(result.Conforms);
        Assert.Equal("document-load", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Contains(loadError.GetType().Name, finding.Message, StringComparison.Ordinal);
        Assert.Contains(loadError.Message, finding.Message, StringComparison.Ordinal);
        Assert.Contains("encrypted or structurally invalid", finding.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(finding.PageIndex);
        Assert.Null(finding.ObjectNumber);
    }

    [Fact]
    public void Malformed_path_returns_the_same_document_load_diagnostic_as_the_byte_overload()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        string path = TempPath();
        File.WriteAllBytes(path, bytes);
        try
        {
            Finding fromBytes = Assert.Single(Preflighter.Check(bytes, ConformanceProfile.PdfA2b).Findings);
            Finding fromPath = Assert.Single(Preflighter.Check(path, ConformanceProfile.PdfA2b).Findings);

            Assert.Equal(fromBytes.RuleId, fromPath.RuleId);
            Assert.Equal(fromBytes.Severity, fromPath.Severity);
            Assert.Equal(fromBytes.Clause, fromPath.Clause);
            Assert.Equal(fromBytes.Message, fromPath.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public void Missing_or_wrong_password_returns_document_load_without_leaking_the_password(string? password)
    {
        string path = TempPath();
        new EncryptedPdfTestDocument(
            EncryptedPdfTestDocument.EncryptionType.Rc4_128,
            userPassword: "correct-document-load-password").Generate(path);
        try
        {
            PreflightResult result = Preflighter.Check(path, ConformanceProfile.PdfA2b, password);

            Finding finding = Assert.Single(result.Findings);
            Assert.Equal("document-load", finding.RuleId);
            Assert.Equal(FindingSeverity.Error, finding.Severity);
            Assert.Contains("PdfSecurityException", finding.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("correct-document-load-password", finding.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("wrong", finding.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Correct_password_loads_and_never_emits_document_load()
    {
        string path = TempPath();
        new EncryptedPdfTestDocument(
            EncryptedPdfTestDocument.EncryptionType.Rc4_128,
            userPassword: "correct-document-load-password").Generate(path);
        try
        {
            PreflightResult result = Preflighter.Check(
                path, ConformanceProfile.PdfA2b, "correct-document-load-password");

            Assert.DoesNotContain(result.Findings, finding => finding.RuleId == "document-load");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_path_keeps_the_file_read_exception_boundary()
    {
        string path = TempPath();

        Assert.Throws<FileNotFoundException>(() =>
            Preflighter.Check(path, ConformanceProfile.PdfA2b));
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"pdflibrary-document-load-{Guid.NewGuid():N}.pdf");
}
