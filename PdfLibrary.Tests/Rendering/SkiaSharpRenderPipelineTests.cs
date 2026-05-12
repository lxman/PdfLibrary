using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Document;
using PdfLibrary.Integration;
using PdfLibrary.Integration.Documents;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// End-to-end render pipeline coverage for PdfLibrary.Rendering.SkiaSharp.
///
/// Tests go through the high-level `page.RenderTo()` fluent API and assert structural
/// guarantees on the returned `SKImage` (non-null, expected dimensions, non-trivial
/// pixel content). They deliberately do NOT do byte-exact image comparison — that would
/// break when SkiaSharp 4 changes rasterization defaults. The match style here is
/// tolerant to AA/rasterization differences, in line with the 95%-threshold pattern
/// the Integration framework already uses.
///
/// These tests exercise the previously-untested rendering pipeline: SkiaSharpRenderTarget,
/// PathRenderer, TextRenderer, ImageRenderer, CanvasStateManager, SoftMaskManager,
/// the conversion helpers in Conversion/, and the state managers in State/.
/// </summary>
public class SkiaSharpRenderPipelineTests : IDisposable
{
    private readonly string _scratchDir;

    public SkiaSharpRenderPipelineTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), "PdfLibrary.Tests.Render", Guid.NewGuid().ToString("N"));
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
            // Best-effort cleanup.
        }
        GC.SuppressFinalize(this);
    }

    // ----- Helpers -----

    /// <summary>
    /// Counts pixels in the image whose RGB channels are NOT pure white. Used to confirm
    /// that the renderer actually drew content rather than producing a blank canvas.
    /// </summary>
    private static int CountNonWhitePixels(SKImage image)
    {
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                if (c.Red != 255 || c.Green != 255 || c.Blue != 255)
                    count++;
            }
        }
        return count;
    }

    private static (PdfDocument doc, PdfPage page) GenerateAndLoadPage(ITestDocument generator, string scratchDir)
    {
        string path = Path.Combine(scratchDir, $"{generator.Name}.pdf");
        generator.Generate(path);

        PdfDocument doc = PdfDocument.Load(path);
        PdfPage page = doc.GetPage(0) ?? throw new InvalidOperationException($"{generator.Name}: page 0 missing");
        return (doc, page);
    }

    // ----- Smoke: simplest possible builder produces a renderable page -----

    [Fact]
    public void Render_SinglePage_ProducesNonEmptyImage()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700, "Helvetica", 24))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();

        Assert.NotNull(image);
        Assert.Equal((int)Math.Ceiling(page.Width), image.Width);
        Assert.Equal((int)Math.Ceiling(page.Height), image.Height);
        Assert.True(CountNonWhitePixels(image) > 0, "Render produced an all-white page");
    }

    [Fact]
    public void Render_WithScale_ResultMatchesScaledDimensions()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(PdfPageSize.A4, p => p.AddText("Scale test", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithScale(2.0).ToImage();

        // A4 = 595 x 842; at 2x scale = 1190 x 1684.
        Assert.Equal(1190, image.Width);
        Assert.Equal(1684, image.Height);
    }

    [Fact]
    public void Render_WithDpi_ConvertsToCorrectScale()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("DPI test", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithDpi(144).ToImage();

        // Letter = 612 x 792; at 144 DPI = 2x = 1224 x 1584.
        Assert.Equal(1224, image.Width);
        Assert.Equal(1584, image.Height);
    }

    [Fact]
    public void Render_TransparentBackground_AlphaChannelHasTransparentPixels()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Tiny", 100, 700, "Helvetica", 8))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage opaque = page.RenderTo().WithTransparentBackground().ToImage();

        using SKBitmap bitmap = SKBitmap.FromImage(opaque);
        // The page is mostly empty so most pixels should be alpha=0 with transparent background.
        var transparentCount = 0;
        for (var y = 0; y < bitmap.Height; y += 20)
        {
            for (var x = 0; x < bitmap.Width; x += 20)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    transparentCount++;
            }
        }
        Assert.True(transparentCount > 0, "Transparent-background render produced no transparent pixels");
    }

    // ----- Output format coverage -----

    [Fact]
    public void Render_ToBytes_Png_ProducesValidPngHeader()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("body", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);

        byte[] png = doc.GetPage(0)!.RenderTo().WithScale(1.0).ToBytes();

        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.True(png.Length > 8);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]); // P
        Assert.Equal(0x4E, png[2]); // N
        Assert.Equal(0x47, png[3]); // G
    }

    [Fact]
    public void Render_ToBytes_Jpeg_ProducesValidJpegHeader()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("body", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);

        byte[] jpg = doc.GetPage(0)!.RenderTo().WithScale(1.0).ToBytes(SKEncodedImageFormat.Jpeg, quality: 80);

        Assert.True(jpg.Length > 4);
        Assert.Equal(0xFF, jpg[0]);
        Assert.Equal(0xD8, jpg[1]); // SOI marker
    }

    [Fact]
    public void Render_ToFile_WritesPng()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("file", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);

        string outPath = Path.Combine(_scratchDir, "out.png");
        doc.GetPage(0)!.RenderTo().WithScale(1.0).ToFile(outPath);

        Assert.True(File.Exists(outPath));
        Assert.True(new FileInfo(outPath).Length > 100, "PNG file is implausibly small");
    }

    [Fact]
    public void Render_SavePageAs_Extension_WorksThroughDocumentApi()
    {
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("doc-api", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);

        string outPath = Path.Combine(_scratchDir, "from-doc.png");
        doc.SavePageAs(0, outPath, scale: 1.0);

        Assert.True(File.Exists(outPath));
    }

    // ----- Render the Integration document fixtures -----
    //
    // These exercise the rendering pipeline against complex builder output: blend modes,
    // clipping paths, transparency groups, line styles, separation colors, etc.

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
    public void IntegrationDocument_AllPages_RenderWithoutError(ITestDocument generator)
    {
        (PdfDocument doc, PdfPage _) = GenerateAndLoadPage(generator, _scratchDir);
        using (doc)
        {
            // Render every page in the document, asserting dimensions and non-trivial content.
            for (var i = 0; i < doc.PageCount; i++)
            {
                PdfPage page = doc.GetPage(i)!;
                using SKImage image = page.RenderTo(pageNumber: i + 1).WithScale(1.0).ToImage();

                Assert.NotNull(image);
                Assert.Equal((int)Math.Ceiling(page.Width), image.Width);
                Assert.Equal((int)Math.Ceiling(page.Height), image.Height);
                Assert.True(
                    CountNonWhitePixels(image) > 0,
                    $"{generator.Name} page {i + 1}: render produced an all-white image");
            }
        }
    }

    [Fact]
    public void BlendModes_MultiPage_AllRenderWithContent()
    {
        // BlendModeTestDocument is 7 pages; specifically validate the 16-blend-mode pipeline
        // has been exercised end-to-end and every page produced non-empty output.
        var generator = new BlendModeTestDocument();
        (PdfDocument doc, _) = GenerateAndLoadPage(generator, _scratchDir);
        using (doc)
        {
            Assert.True(doc.PageCount >= 1, "BlendModeTestDocument should produce at least one page");

            for (var i = 0; i < doc.PageCount; i++)
            {
                PdfPage page = doc.GetPage(i)!;
                using SKImage image = page.RenderTo(pageNumber: i + 1).WithScale(1.0).ToImage();
                Assert.True(CountNonWhitePixels(image) > 100, $"BlendModes page {i + 1} has < 100 non-white pixels");
            }
        }
    }

    // ----- Encrypted documents render after decryption -----

    [Theory]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Rc4_128, "")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Rc4_128, "test123")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Aes128, "")]
    [InlineData(EncryptedPdfTestDocument.EncryptionType.Aes128, "test123")]
    public void EncryptedDocument_DecryptsAndRenders(EncryptedPdfTestDocument.EncryptionType type, string userPassword)
    {
        var generator = new EncryptedPdfTestDocument(type, userPassword);
        string path = Path.Combine(_scratchDir, $"{generator.Name}.pdf");
        generator.Generate(path);

        using PdfDocument doc = PdfDocument.Load(path, userPassword);
        Assert.True(doc.IsEncrypted);

        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();

        Assert.NotNull(image);
        Assert.True(CountNonWhitePixels(image) > 0);
    }

    [Fact]
    public void NewAes256Encryption_DecryptsAndRenders()
    {
        // AES-256 round-trip got fixed in this change; ensure rendering also works end-to-end.
        const string password = "render-aes256";
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .WithPassword(password)
            .AddPage(p => p.AddText("AES-256 rendered", 100, 700, "Helvetica", 16))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms, password);

        PdfPage page = doc.GetPage(0)!;
        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();

        Assert.True(CountNonWhitePixels(image) > 0);
    }

    // ----- Path and color rendering spot checks -----

    [Fact]
    public void Render_FilledRectangle_ProducesColoredPixels()
    {
        // Draws a red rectangle and confirms the rendered image actually contains red pixels.
        // Pinning behavior at the pixel level catches PathRenderer / ColorConverter regressions.
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddRectangle(100, 600, 200, 100, fillColor: PdfColor.Red))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        // Sample inside the rectangle (100,600 page coords → PDF bottom-origin).
        // PDF origin is bottom-left; the renderer flips to top-left. Use the page-height-based
        // conversion to find a point that's safely inside the rectangle.
        int sampleX = 150;                            // 100 < x < 300
        int sampleY = (int)page.Height - 650;         // 600 < y < 700 in PDF coords → flipped
        SKColor px = bitmap.GetPixel(sampleX, sampleY);

        Assert.True(
            px.Red > 200 && px.Green < 80 && px.Blue < 80,
            $"Expected red pixel inside rectangle at ({sampleX},{sampleY}), got ({px.Red},{px.Green},{px.Blue})");
    }

    [Fact]
    public void Render_Text_ProducesDarkPixelsAgainstWhiteBackground()
    {
        // Draw black text on a white page; confirm dark pixels exist where text was placed.
        // Exercises the TextRenderer and font metrics path.
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("XXXX", 100, 700, "Helvetica", 72))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithScale(1.0).ToImage();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        // Sample a band where the text glyphs should lie. We don't require pixels to be
        // pure black (AA produces shades of gray) — just at least one clearly-dark pixel
        // in the row.
        int sampleY = (int)page.Height - 720;  // ~just below baseline of text at y=700
        var darkFound = false;
        for (var x = 100; x < 300; x++)
        {
            SKColor px = bitmap.GetPixel(x, sampleY);
            if (px.Red < 100 && px.Green < 100 && px.Blue < 100)
            {
                darkFound = true;
                break;
            }
        }
        Assert.True(darkFound, $"No dark pixels found in text band at y={sampleY}");
    }

    // ----- SkiaSharpRenderTarget direct usage (the lower-level entry point) -----

    [Fact]
    public void DirectRenderTarget_RendersAndProducesImage()
    {
        // Exercise the lower-level API that PageRenderBuilder wraps. Confirms the target
        // can be driven directly (the path PdfImageRenderer in Integration uses).
        byte[] pdfBytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Direct", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream(pdfBytes);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        var width = (int)page.Width;
        var height = (int)page.Height;

        using var target = new SkiaSharpRenderTarget(width, height, doc);
        page.Render(target, pageNumber: 1, scale: 1.0);

        using SKImage image = target.GetImage();
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.True(CountNonWhitePixels(image) > 0);
    }
}
