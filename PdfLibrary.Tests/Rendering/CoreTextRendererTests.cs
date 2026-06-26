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

    // Minimal recording target — captures FillPath; everything else is a no-op/stub.
    private sealed class RecordingRenderTarget : IRenderTarget
    {
        public List<(IPathBuilder Path, bool EvenOdd)> FillPaths { get; } = [];
        public int CurrentPageNumber { get; private set; }

        public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
            double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0) => CurrentPageNumber = pageNumber;
        public void EndPage() { }
        public void Clear() { }
        public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
            => FillPaths.Add((path.Clone(), evenOdd));
        public void StrokePath(IPathBuilder path, PdfGraphicsState state) { }
        public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
        public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
            PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent) { }
        public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
        public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state,
            PdfFont? font, List<int>? charCodes = null) { }
        public float MeasureTextWidth(string text, PdfGraphicsState state, PdfFont font) => 0;
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
