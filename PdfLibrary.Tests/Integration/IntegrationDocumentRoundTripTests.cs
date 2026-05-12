using PdfLibrary.Integration;
using PdfLibrary.Integration.Documents;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Integration;

/// <summary>
/// Drives the rich test documents from PdfLibrary.Integration through a build → load round-trip
/// inside xUnit. These documents exercise much more of the builder surface than the unit tests
/// (blend modes, clipping, transparency, separation colors, layered text, etc.). Wiring them
/// here means a writer regression that breaks any complex document will fail `dotnet test`,
/// not just a manual rendering run.
///
/// Tests that depend on external fonts (EmbeddedFontsTestDocument requires C:\Users\jorda\source\TestFonts)
/// are skipped when the resources aren't present — same pattern as PdfDocumentSecurityTests.
/// </summary>
public class IntegrationDocumentRoundTripTests : IDisposable
{
    private readonly string _scratchDir;

    public IntegrationDocumentRoundTripTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "PdfLibrary.Tests.Integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_scratchDir))
                Directory.Delete(_scratchDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; don't fail the test on temp-file leftovers.
        }
        GC.SuppressFinalize(this);
    }

    public static IEnumerable<object[]> StandardDocuments()
    {
        // Documents that don't need external resources (fonts, images on a specific path).
        yield return [new ColorSpaceTestDocument()];
        yield return [new PathDrawingTestDocument()];
        yield return [new TransparencyTestDocument()];
        yield return [new ClippingPathTestDocument()];
        yield return [new LineStyleTestDocument()];
        yield return [new TextBasicsTestDocument()];
        yield return [new TextLayoutTestDocument()];
        yield return [new TextRenderingTestDocument()];
        yield return [new SeparationColorTestDocument()];
        yield return [new AdvancedGraphicsStateTestDocument()];
        yield return [new BlendModeTestDocument()];
    }

    [Theory]
    [MemberData(nameof(StandardDocuments))]
    public void Generate_Then_Load_Succeeds(ITestDocument document)
    {
        string outputPath = Path.Combine(_scratchDir, $"{document.Name}.pdf");

        document.Generate(outputPath);

        Assert.True(File.Exists(outputPath), $"{document.Name}: PDF was not generated");
        long size = new FileInfo(outputPath).Length;
        Assert.True(size > 0, $"{document.Name}: PDF is empty");

        using PdfDocument doc = PdfDocument.Load(outputPath);
        Assert.True(doc.PageCount > 0, $"{document.Name}: re-parsed document has zero pages");
    }

    [Fact]
    public void EmbeddedFonts_GenerateAndLoad_WhenFontsAvailable()
    {
        // EmbeddedFontsTestDocument hard-codes a font path; skip cleanly when the path isn't present
        // so this remains a useful regression test on the dev machine without breaking CI/other devs.
        const string fontDir = @"C:\Users\jorda\source\TestFonts";
        if (!Directory.Exists(fontDir))
            return;

        var document = new EmbeddedFontsTestDocument();
        string outputPath = Path.Combine(_scratchDir, $"{document.Name}.pdf");

        document.Generate(outputPath);

        Assert.True(File.Exists(outputPath));
        using PdfDocument doc = PdfDocument.Load(outputPath);
        Assert.True(doc.PageCount > 0);
    }

    [Theory]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Rc4_128, "")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Rc4_128, "test123")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Aes128, "")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Aes128, "test123")]
    public void EncryptedDocument_GenerateAndLoad(EncryptedPdfTestDocument.EncryptionType type, string userPassword)
    {
        var document = new EncryptedPdfTestDocument(type, userPassword);
        string outputPath = Path.Combine(_scratchDir, $"{document.Name}.pdf");

        document.Generate(outputPath);

        Assert.True(File.Exists(outputPath));
        using PdfDocument doc = PdfDocument.Load(outputPath, userPassword);
        Assert.True(doc.IsEncrypted);
        Assert.True(doc.PageCount > 0);
    }

    /// <summary>
    /// Verifies that the goldens committed to TestPDFs/targeted_custom_generated/golden/ still
    /// parse cleanly under the current PdfDocument.Load. Catches parser regressions where input
    /// formats we used to accept stop working.
    /// </summary>
    [Theory]
    [InlineData("ColorSpaces.pdf")]
    [InlineData("PathDrawing.pdf")]
    [InlineData("Transparency.pdf")]
    [InlineData("ClippingPath.pdf")]
    [InlineData("LineStyles.pdf")]
    [InlineData("TextBasics.pdf")]
    [InlineData("TextLayout.pdf")]
    [InlineData("TextRendering.pdf")]
    [InlineData("SeparationColors.pdf")]
    [InlineData("AdvancedGraphicsState.pdf")]
    [InlineData("BlendModes.pdf")]
    public void GoldenPdf_Parses(string fileName)
    {
        string path = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "TestPDFs", "targeted_custom_generated", "golden", fileName);
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
            return;  // Goldens may not exist in clean CI checkout — skip cleanly.

        using PdfDocument doc = PdfDocument.Load(path);
        Assert.True(doc.PageCount > 0, $"{fileName} re-parsed with zero pages");
    }
}
