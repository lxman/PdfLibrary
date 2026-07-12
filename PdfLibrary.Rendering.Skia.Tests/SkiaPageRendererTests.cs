using PdfLibrary.Content;
using PdfLibrary.Rendering;
using SkiaSharp;
using Xunit;

namespace PdfLibrary.Rendering.Skia.Tests;

public class SkiaPageRendererTests
{
    [Fact]
    public void Render_fills_a_red_rectangle_covering_the_page()
    {
        var state = new PdfGraphicsState
        {
            ResolvedFillColorSpace = "DeviceRGB",
            ResolvedFillColor = [1.0, 0.0, 0.0],
            FillAlpha = 1.0,
        };
        var segs = new PathSegment[]
        {
            new MoveToSegment(0, 0), new LineToSegment(100, 0),
            new LineToSegment(100, 100), new LineToSegment(0, 100), new ClosePathSegment(),
        };
        var list = new PageDrawList(
            new BeginPageArgs(1, 100, 100, 1.0, 0, 0, 0),
            new DrawCommand[] { new FillCommand(segs, EvenOdd: false, state) });

        using var surface = SKSurface.Create(new SKImageInfo(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);

        SkiaPageRenderer.Render(surface.Canvas, list);

        using SKImage img = surface.Snapshot();
        using SKBitmap bmp = SKBitmap.FromImage(img);
        SKColor center = bmp.GetPixel(50, 50);
        Assert.True(center.Red > 200 && center.Green < 60 && center.Blue < 60,
            $"center pixel was {center}; expected red (fill missing → white)");
    }
}
