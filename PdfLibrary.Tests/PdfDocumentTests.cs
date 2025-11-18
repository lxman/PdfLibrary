using PdfLibrary.Structure;
using PdfLibrary.Core;

namespace PdfLibrary.Tests;

public class PdfDocumentTests
{
    [Fact]
    public void LoadPdf20File_ShouldParseSuccessfully()
    {
        // Arrange
        string testFilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "pdf20examples", "Simple PDF 2.0 file.pdf");

        testFilePath = Path.GetFullPath(testFilePath);

        // Act & Assert
        PdfDocument document = PdfDocument.Load(testFilePath);

        Assert.NotNull(document);
        Assert.Equal(PdfVersion.Pdf20, document.Version);

        document.Dispose();
    }

    [Fact]
    public void LoadPdf20File_WithOffsetStart_ShouldParseSuccessfully()
    {
        // Arrange
        string testFilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "pdf20examples", "PDF 2.0 with offset start.pdf");

        testFilePath = Path.GetFullPath(testFilePath);

        // Act & Assert
        PdfDocument document = PdfDocument.Load(testFilePath);

        Assert.NotNull(document);
        Assert.Equal(PdfVersion.Pdf20, document.Version);

        document.Dispose();
    }

    [Fact]
    public void LoadPdf20File_Utf8Test_ShouldParseSuccessfully()
    {
        // Arrange
        string testFilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "pdf20examples", "pdf20-utf8-test.pdf");

        testFilePath = Path.GetFullPath(testFilePath);

        // Act & Assert
        PdfDocument document = PdfDocument.Load(testFilePath);

        Assert.NotNull(document);
        Assert.Equal(PdfVersion.Pdf20, document.Version);

        document.Dispose();
    }
}
