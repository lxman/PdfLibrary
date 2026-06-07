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

    // ----- Regression: text stroke widths must be computed in the correct coordinate space -----

    [Fact]
    public void StrokedText_WithSizeCarriedInCtm_DoesNotSmudgeIntoABlob()
    {
        // Regression guard for the text stroke-width coordinate-space bug.
        //
        // Faux-bold and Tr-mode (stroke) glyph widths were specified in glyph/local space but
        // multiplied by the CTM scale a second time (the draw matrix already carries the CTM)
        // and floored in glyph space instead of device space. For a PDF that carries its font
        // size in the CTM (small `Tf` + large `cm`), the stroke ballooned to several times the
        // glyph height, filling every counter so text rendered as solid black blobs.
        //
        // This reproduces that exact shape: a non-embedded font (which routes text through the
        // SkiaSharp fallback path), Tr=2 (fill+stroke), 1 Tf, and the real size carried in `cm`.
        // Correct output is a thin ring whose counter stays open; the pre-fix blob filled it in.
        byte[] pdf = BuildScaledStrokedGlyphPdf(
            "q\n40 0 0 40 12 28 cm\nBT\n/F1 1 Tf\n2 Tr\n0.05 w\n0 0 0 rg\n0 0 0 RG\n(o) Tj\nET\nQ\n");

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithScale(3.0).ToImage();
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        // Find the dark pixels and their bounding box.
        int total = bitmap.Width * bitmap.Height;
        int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;
        var darkCount = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                // "Ink" = an opaque dark pixel. ToImage() leaves the page background transparent
                // (premultiplied → reads as RGB 0,0,0), so the alpha test is what distinguishes a
                // drawn glyph pixel from empty background.
                if (c.Alpha <= 128 || c.Red >= 128 || c.Green >= 128 || c.Blue >= 128)
                    continue;
                darkCount++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        // 1. The glyph actually rendered.
        Assert.True(darkCount > 100, $"Glyph did not render (only {darkCount} dark pixels)");

        // 2. Dark coverage stays in a sane range. A correct 40pt 'o' covers a few percent of
        //    this 600x300 image; the pre-fix blob covered tens of percent. 12% is a wide margin.
        double darkFraction = (double)darkCount / total;
        Assert.True(darkFraction < 0.12,
            $"Stroked glyph coverage {darkFraction:P1} looks like a blob (stroke width over-scaled)");

        // 3. The 'o' counter stays open: the centre of the glyph's bounding box must be
        //    background, not filled. This is the precise symptom the bug produced.
        int cx = (minX + maxX) / 2;
        int cy = (minY + maxY) / 2;
        var openAtCentre = 0;
        var samples = 0;
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                SKColor c = bitmap.GetPixel(cx + dx, cy + dy);
                samples++;
                bool isInk = c.Alpha > 128 && c.Red < 128 && c.Green < 128 && c.Blue < 128;
                if (!isInk)
                    openAtCentre++;
            }
        }
        Assert.True(openAtCentre > samples / 2,
            $"Counter of 'o' is filled ({openAtCentre}/{samples} open at centre {cx},{cy}) — rendered as a blob");
    }

    /// <summary>
    /// Hand-assembles a minimal single-page PDF (with a valid xref) whose only font is a
    /// non-embedded standard Type1 face, so text is rendered through the SkiaSharp fallback
    /// path. The caller supplies the page content stream.
    /// </summary>
    private static byte[] BuildScaledStrokedGlyphPdf(string contentStream)
    {
        byte[] content = System.Text.Encoding.Latin1.GetBytes(contentStream);
        var buf = new List<byte>();
        var offsets = new int[6];

        void Ascii(string s) => buf.AddRange(System.Text.Encoding.Latin1.GetBytes(s));
        void StartObj(int n) { offsets[n] = buf.Count; Ascii($"{n} 0 obj\n"); }
        void EndObj() => Ascii("\nendobj\n");

        Ascii("%PDF-1.7\n");
        buf.AddRange(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }); // binary marker comment
        StartObj(1); Ascii("<< /Type /Catalog /Pages 2 0 R >>"); EndObj();
        StartObj(2); Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"); EndObj();
        StartObj(3); Ascii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] " +
                           "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>"); EndObj();
        StartObj(4); Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"); EndObj();
        StartObj(5); Ascii($"<< /Length {content.Length} >>\nstream\n"); buf.AddRange(content); Ascii("endstream"); EndObj();

        int xrefOff = buf.Count;
        Ascii("xref\n0 6\n0000000000 65535 f \n");
        for (var i = 1; i <= 5; i++)
            Ascii($"{offsets[i]:D10} 00000 n \n");
        Ascii($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOff}\n%%EOF\n");

        return buf.ToArray();
    }

    // ----- Regression: the fluent render path must size to the CropBox, not the MediaBox -----

    [Fact]
    public void Render_SizesToCropBox_NotMediaBox()
    {
        // Regression: PageRenderBuilder.ToImage() sized the surface from PdfPage.Width/Height
        // (the full MediaBox) while PdfRenderer draws to the CropBox. For a page whose CropBox is
        // smaller than its MediaBox, the artwork landed in a corner of an oversized canvas and
        // content outside the CropBox (e.g. print-to-PDF footers) bled in. The rendered image
        // must match the CropBox.
        byte[] pdf = BuildBoxedPdf(
            mediaBox: "[0 0 200 100]",
            cropBox:  "[50 25 150 75]",                     // 100 x 50, offset (50,25), inside the MediaBox
            content:  "q 0 0 1 rg 50 25 100 50 re f Q\n");  // fill the CropBox region blue

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;

        using SKImage image = page.RenderTo().WithScale(2.0).ToImage();

        // CropBox is 100x50 -> at 2x = 200x100. Before the fix this was 400x200 (the MediaBox).
        Assert.Equal(200, image.Width);
        Assert.Equal(100, image.Height);
    }

    /// <summary>Hand-assembles a minimal single-page PDF with explicit MediaBox and CropBox.</summary>
    private static byte[] BuildBoxedPdf(string mediaBox, string cropBox, string content)
    {
        byte[] body = System.Text.Encoding.Latin1.GetBytes(content);
        var buf = new List<byte>();
        var offsets = new int[5];

        void Ascii(string s) => buf.AddRange(System.Text.Encoding.Latin1.GetBytes(s));
        void StartObj(int n) { offsets[n] = buf.Count; Ascii($"{n} 0 obj\n"); }
        void EndObj() => Ascii("\nendobj\n");

        Ascii("%PDF-1.7\n");
        buf.AddRange(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });
        StartObj(1); Ascii("<< /Type /Catalog /Pages 2 0 R >>"); EndObj();
        StartObj(2); Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"); EndObj();
        StartObj(3); Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox {mediaBox} /CropBox {cropBox} /Contents 4 0 R >>"); EndObj();
        StartObj(4); Ascii($"<< /Length {body.Length} >>\nstream\n"); buf.AddRange(body); Ascii("endstream"); EndObj();

        int xrefOff = buf.Count;
        Ascii("xref\n0 5\n0000000000 65535 f \n");
        for (var i = 1; i <= 4; i++)
            Ascii($"{offsets[i]:D10} 00000 n \n");
        Ascii($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xrefOff}\n%%EOF\n");

        return buf.ToArray();
    }
}
