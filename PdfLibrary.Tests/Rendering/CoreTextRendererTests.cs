using System.Numerics;
using PdfLibrary.Builder;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

public class CoreTextRendererTests
{
    [Fact]
    public void Render_EmbeddedFontText_EmitsFillPathPerVisibleGlyph()
    {
        // Build a one-page PDF that draws "AB" with the embedded PublicPixel font, load it,
        // and run a page render through a recording target; assert the core text path fired.
        byte[] fontBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

        byte[] pdf = PdfDocumentBuilder.Create()
            .LoadFont(fontBytes, "Pixel")
            .AddPage(p => p.AddText("AB", 100, 700, "Pixel", 24))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        var target = new RecordingRenderTarget();
        doc.GetPage(0)!.Render(target);

        // Two visible glyphs (A, B) → at least two glyph FillPath calls, all even-odd, non-empty.
        Assert.True(target.FillPaths.Count >= 2, $"expected >=2 fills, got {target.FillPaths.Count}");
        Assert.All(target.FillPaths, f =>
        {
            Assert.True(f.EvenOdd);
            Assert.False(f.Path.IsEmpty);
        });
    }

    [Fact]
    public void Render_RotatedEmbeddedText_AdvancesGlyphsInTextMatrixDirection()
    {
        // A 180°-rotated run (Tm.M11 < 0, positive font size) must place successive glyphs in the
        // SAME direction as the between-run pen motion — AdvanceTextMatrix(totalAdvance, 0) with a
        // POSITIVE advance (PdfRenderer.cs:913), which the M11<0 matrix maps to DECREASING user X.
        // CoreTextRenderer.Advance negated the per-glyph advance (flipX), marching glyphs the
        // opposite way (increasing X) — fighting the between-run motion and jumbling upside-down
        // lines (PDFUA-Ref-2-08 page 2 answer key). Embedded-font path.
        byte[] fontBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

        byte[] pdf = PdfDocumentBuilder.Create()
            .LoadFont(fontBytes, "Pixel")
            .AddPage(p => p.AddText("ABCDEF", 300, 400).Font("Pixel", 24).Rotate(180))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        var target = new RecordingRenderTarget();
        doc.GetPage(0)!.Render(target);

        List<double> xs = target.FillPaths.Select(f => MeanPathX(f.Path)).ToList();
        Assert.True(xs.Count >= 4, $"expected >=4 glyph fills, got {xs.Count}");

        for (var i = 1; i < xs.Count; i++)
            Assert.True(xs[i] < xs[i - 1],
                $"glyph {i} X={xs[i]:F1} must advance leftward (decreasing) from glyph {i - 1} " +
                $"X={xs[i - 1]:F1} under a 180° text matrix; rotated-text advance direction is reversed");
    }

    [Fact]
    public void Render_RotatedSubstituteText_AdvancesGlyphsInTextMatrixDirection()
    {
        // Same invariant as the embedded case, for the substitute-font path (non-embedded font
        // resolved via ISystemFontProvider). The substitute loop applied the identical flipX
        // negation to currentX.
        byte[] subst = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("ABCDEF", 300, 400).Font("Helvetica", 24).Rotate(180))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        var target = new RecordingRenderTarget();
        doc.GetPage(0)!.Render(target, new StubProvider(subst));

        List<double> xs = target.FillPaths.Select(f => MeanPathX(f.Path)).ToList();
        Assert.True(xs.Count >= 4, $"expected >=4 substitute glyph fills, got {xs.Count}");

        for (var i = 1; i < xs.Count; i++)
            Assert.True(xs[i] < xs[i - 1],
                $"substitute glyph {i} X={xs[i]:F1} must advance leftward (decreasing) from glyph {i - 1} " +
                $"X={xs[i - 1]:F1} under a 180° text matrix; rotated-text advance direction is reversed");
    }

    [Fact]
    [Trait("Category", "LocalOnly")]   // loads main.pdf, a local-only fixture not present on CI
    public void Render_TextUnderTranslatingCtm_BakesCtmIntoGlyphPath()
    {
        // Regression guard. FillPath applies ONLY the page initial-transform, never the CTM —
        // it expects the CTM already baked into the path coordinates (as PdfRenderer does for
        // every regular path point, and as Type3 glyph matrices end with "* Ctm"). CoreTextRenderer
        // must do the same, or text inside a cm-transformed context (a Form XObject figure) loses
        // the transform and collapses toward the origin (the main.pdf page-5/8 label bug).
        //
        // main.pdf page 5 draws figure labels under a translating CTM. We assert the invariant
        // directly: any glyph whose CTM carries a large Y-translation must emit a path that sits in
        // that translated region (not near the origin). This is layout-independent.
        string? pdf = TryFindRepoFile("PDFs", "PDF Standards", "Compression", "JPEG", "main.pdf");
        Assert.SkipUnless(pdf is not null, "main.pdf corpus fixture not present on this system");
        using PdfDocument doc = PdfDocument.Load(pdf!);

        var target = new RecordingRenderTarget();
        doc.GetPage(4)!.Render(target); // page 5 (0-based index)

        List<(IPathBuilder Path, bool EvenOdd, Matrix3x2 Ctm)> translated =
            target.FillPaths.Where(f => f.Ctm.M32 > 200).ToList();

        Assert.True(translated.Count > 0,
            "expected CTM-translated text on main.pdf page 5 (the figure labels); none captured");

        foreach ((IPathBuilder Path, bool EvenOdd, Matrix3x2 Ctm) f in translated)
        {
            double pathMaxY = MaxPathY(f.Path);
            Assert.True(pathMaxY > f.Ctm.M32 * 0.5,
                $"glyph path ignores its CTM translation: pathMaxY={pathMaxY:F1}, CTM.transY={f.Ctm.M32:F1}");
        }
    }

    private static double MaxPathY(IPathBuilder p)
    {
        double max = double.NegativeInfinity;
        foreach (PathSegment s in p.Segments)
            max = s switch
            {
                MoveToSegment m => Math.Max(max, m.Y),
                LineToSegment l => Math.Max(max, l.Y),
                CurveToSegment c => Math.Max(max, Math.Max(c.Y1, Math.Max(c.Y2, c.Y3))),
                _ => max
            };
        return max;
    }

    // Mean X of a glyph path's control points — a stable proxy for the glyph's placed origin,
    // used to compare successive glyphs' horizontal advance direction.
    private static double MeanPathX(IPathBuilder p)
    {
        double sum = 0;
        var n = 0;
        foreach (PathSegment s in p.Segments)
            switch (s)
            {
                case MoveToSegment m: sum += m.X; n++; break;
                case LineToSegment l: sum += l.X; n++; break;
                case CurveToSegment c: sum += c.X1 + c.X2 + c.X3; n += 3; break;
            }
        return n == 0 ? 0 : sum / n;
    }

    [Fact]
    public void Render_NonEmbeddedFont_RendersViaSubstituteOutlines()
    {
        // A page drawing non-embedded "Helvetica" text. With a provider that returns a real font
        // (PublicPixel) as the substitute, the core must emit FillPath per glyph via the substitute's
        // outlines — not return false (the geometry SPI has no text fallback).
        byte[] subst = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hi", 100, 700, "Helvetica", 24))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        var target = new RecordingRenderTarget();
        doc.GetPage(0)!.Render(target, new StubProvider(subst)); // internal seam added in this task

        Assert.True(target.FillPaths.Count >= 1, $"expected substitute glyph fills, got {target.FillPaths.Count}");
    }

    private sealed class StubProvider(byte[] bytes) : PdfLibrary.Fonts.ISystemFontProvider
    {
        public byte[]? GetFontData(string baseFontName) => bytes;
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => true;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
    }

    private static string? TryFindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null; // corpus fixture not checked out on this system
    }

    // Minimal recording target — captures FillPath (path + CTM); everything else is a no-op/stub.
    private sealed class RecordingRenderTarget : IRenderTarget
    {
        public List<(IPathBuilder Path, bool EvenOdd, Matrix3x2 Ctm)> FillPaths { get; } = [];
        public int CurrentPageNumber { get; private set; }

        public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
            double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0) => CurrentPageNumber = pageNumber;
        public void EndPage() { }
        public void Clear() { }
        public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
            => FillPaths.Add((path.Clone(), evenOdd, state.Ctm));
        public void StrokePath(IPathBuilder path, PdfGraphicsState state) { }
        public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
        public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
            PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent) { }
        public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
        public void DrawImage(PdfImage image, PdfGraphicsState state) { }
        public void SaveState() { }
        public void RestoreState() { }
        public void ApplyCtm(Matrix3x2 ctm) { }
        public void OnGraphicsStateChanged(PdfGraphicsState state) { }
        public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent) { }
        public void ClearSoftMask() { }
        public (int width, int height, double scale) GetPageDimensions() => (600, 800, 1.0);
    }
}
