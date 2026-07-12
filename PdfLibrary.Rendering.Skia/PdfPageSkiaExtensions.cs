using PdfLibrary.Document;
using PdfLibrary.Rendering;
using SkiaSharp;

namespace PdfLibrary.Rendering.Skia;

/// <summary>Render a PdfPage by recording it to a display list and walking that list onto a
/// caller-supplied SKCanvas (the primary surface contract), or to a new bitmap (convenience).</summary>
public static class PdfPageSkiaExtensions
{
    public static void RenderTo(this PdfPage page, SKCanvas canvas, double scale = 1.0)
    {
        PageDrawList list = RecordingRenderTarget.Record(page, scale);
        SkiaPageRenderer.Render(canvas, list);
    }

    public static SKImage RenderToImage(this PdfPage page, double scale = 1.0)
    {
        PageDrawList list = RecordingRenderTarget.Record(page, scale);
        BeginPageArgs b = list.Begin;
        double pw = (b.Rotation is 90 or 270 ? b.Height : b.Width) * b.Scale;
        double ph = (b.Rotation is 90 or 270 ? b.Width : b.Height) * b.Scale;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Ceiling(pw)), Math.Max(1, (int)Math.Ceiling(ph)),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        SkiaPageRenderer.Render(surface.Canvas, list);
        return surface.Snapshot();
    }
}
