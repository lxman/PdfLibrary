using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Document;
using PdfLibrary.Integration;
using PdfLibrary.Integration.Documents;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// End-to-end coverage for the renderer-neutral recording contract. Backend-specific encoding,
/// background, and rasterization tests live with the supported renderer that owns those policies.
/// </summary>
public sealed class RecordingRenderPipelineTests : IDisposable
{
    private readonly string _scratchDir = Path.Combine(
        Path.GetTempPath(), "PdfLibrary.Tests.Record", Guid.NewGuid().ToString("N"));

    public RecordingRenderPipelineTests() => Directory.CreateDirectory(_scratchDir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_scratchDir)) Directory.Delete(_scratchDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void Record_SinglePage_ProducesCommandsAndDimensions()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700, "Helvetica", 24))
            .ToByteArray();

        using PdfDocument document = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = document.GetPage(0)!;
        PageDrawList list = RecordedPageProbe.Record(page);

        Assert.Equal(page.GetCropBox().Width, list.Begin.Width, 3);
        Assert.Equal(page.GetCropBox().Height, list.Begin.Height, 3);
        Assert.NotEmpty(RecordedPageProbe.PaintCommands(list));
    }

    [Fact]
    public void Record_WithScale_RetainsScaleAndPageDimensions()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, p => p.AddText("Scale", 100, 700))
            .ToByteArray();

        using PdfDocument document = PdfDocument.Load(new MemoryStream(pdf));
        PageDrawList list = RecordedPageProbe.Record(document.GetPage(0)!, 2.0);

        Assert.Equal(595, list.Begin.Width, 3);
        Assert.Equal(842, list.Begin.Height, 3);
        Assert.Equal(2.0, list.Begin.Scale);
    }

    public static IEnumerable<object[]> RenderableDocuments()
    {
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
    [MemberData(nameof(RenderableDocuments))]
    public void IntegrationDocument_AllPages_RecordWithoutError(ITestDocument generator)
    {
        string path = Path.Combine(_scratchDir, $"{generator.Name}.pdf");
        generator.Generate(path);
        using PdfDocument document = PdfDocument.Load(path);

        for (var i = 0; i < document.PageCount; i++)
        {
            PageDrawList list = RecordedPageProbe.Record(document.GetPage(i)!);
            Assert.NotEmpty(RecordedPageProbe.PaintCommands(list));
        }
    }

    [Theory]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Rc4_128, "")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Rc4_128, "test123")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Aes128, "")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Aes128, "test123")]
    public void EncryptedDocument_DecryptsAndRecords(
        EncryptedPdfTestDocument.EncryptionType type, string userPassword)
    {
        var generator = new EncryptedPdfTestDocument(type, userPassword);
        string path = Path.Combine(_scratchDir, $"{generator.Name}.pdf");
        generator.Generate(path);

        using PdfDocument document = PdfDocument.Load(path, userPassword);
        Assert.True(document.IsEncrypted);
        Assert.NotEmpty(RecordedPageProbe.PaintCommands(RecordedPageProbe.Record(document.GetPage(0)!)));
    }

    [Fact]
    public void NewAes256Encryption_DecryptsAndRecords()
    {
        const string password = "record-aes256";
        byte[] pdf = PdfDocumentBuilder.Create()
            .WithPassword(password)
            .AddPage(p => p.AddText("AES-256", 100, 700, "Helvetica", 16))
            .ToByteArray();

        using PdfDocument document = PdfDocument.Load(new MemoryStream(pdf), password);
        Assert.NotEmpty(RecordedPageProbe.PaintCommands(RecordedPageProbe.Record(document.GetPage(0)!)));
    }

    [Fact]
    public void FilledRectangle_RecordsResolvedRed()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddRectangle(100, 600, 200, 100, fillColor: PdfColor.Red))
            .ToByteArray();

        RecordedColor color = RecordedPageProbe.ColorAt(pdf, 150, 650);
        Assert.True(color is { Red: > 200, Green: < 80, Blue: < 80 });
    }

    [Fact]
    public void MalformedNumericOperand_IsToleratedAndFollowingPaintIsRecorded()
    {
        byte[] pdf = BuildBoxedPdf("[0 0 100 100]", "[0 0 100 100]",
            "- q 0 0 1 rg 20 20 60 60 re f Q\n");

        RecordedColor color = RecordedPageProbe.ColorAt(pdf, 50, 50);
        Assert.True(color is { Blue: > 200, Red: < 80, Green: < 80 });
    }

    private static byte[] BuildBoxedPdf(string mediaBox, string cropBox, string content)
    {
        byte[] body = System.Text.Encoding.Latin1.GetBytes(content);
        var bytes = new List<byte>();
        var offsets = new int[5];
        void Add(string value) => bytes.AddRange(System.Text.Encoding.Latin1.GetBytes(value));
        void StartObject(int number) { offsets[number] = bytes.Count; Add($"{number} 0 obj\n"); }
        void EndObject() => Add("\nendobj\n");

        Add("%PDF-1.7\n");
        StartObject(1); Add("<< /Type /Catalog /Pages 2 0 R >>"); EndObject();
        StartObject(2); Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"); EndObject();
        StartObject(3);
        Add($"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /CropBox {cropBox} /Contents 4 0 R >>");
        EndObject();
        StartObject(4); Add($"<< /Length {body.Length} >>\nstream\n"); bytes.AddRange(body);
        Add("endstream"); EndObject();

        int xref = bytes.Count;
        Add("xref\n0 5\n0000000000 65535 f \n");
        for (var i = 1; i <= 4; i++) Add($"{offsets[i]:D10} 00000 n \n");
        Add($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return bytes.ToArray();
    }
}
