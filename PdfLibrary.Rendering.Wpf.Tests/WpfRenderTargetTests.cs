using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfLibrary.Content;
using PdfLibrary.Rendering;

namespace PdfLibrary.Rendering.Wpf.Tests;

public class WpfRenderTargetTests
{
    // ---- lifecycle: BeginPage / EndPage ----

    [Fact]
    public void BeginEndPage_ProducesDrawingGroup_WithRootTransform()
    {
        // width=600, height=800, scale=1, crop=(0,0), rotation=0
        // PageTransform.Build maps PDF (0,0) -> pixel (0, 800)  [Y-flip + translate]
        DrawingGroup dg = Sta.Run(() =>
        {
            var target = new WpfRenderTarget();
            target.BeginPage(1, 600, 800, 1.0, 0, 0, 0);
            target.EndPage();

            DrawingGroup d = target.Drawing;
            d.Freeze();
            return d;
        });

        Assert.NotNull(dg);
        Assert.NotNull(dg.Transform);

        Point mapped = dg.Transform.Value.Transform(new Point(0, 0));
        Assert.Equal(0,   mapped.X, 3);
        Assert.Equal(800, mapped.Y, 3);
    }

    [Fact]
    public void BeginEndPage_SetsCurrentPageNumber()
    {
        int page = Sta.Run(() =>
        {
            var target = new WpfRenderTarget();
            target.BeginPage(7, 200, 300, 1.0);
            int n = target.CurrentPageNumber;
            target.EndPage();
            return n;
        });

        Assert.Equal(7, page);
    }

    // ---- GetPageDimensions ----

    [Fact]
    public void GetPageDimensions_ReturnsScaledCropSize()
    {
        // width=600, height=800, scale=2  →  pixel size 1200×1600
        (int w, int h, double s) = Sta.Run(() =>
        {
            var target = new WpfRenderTarget();
            target.BeginPage(1, 600, 800, 2.0, 0, 0, 0);
            (int w, int h, double s) dims = target.GetPageDimensions();
            target.EndPage();
            return dims;
        });

        Assert.Equal(1200, w);
        Assert.Equal(1600, h);
        Assert.Equal(2.0, s);
    }

    [Fact]
    public void GetPageDimensions_Rotation90_SwapsDimensions()
    {
        // width=600, height=800, rotation=90  →  pixel size 800×600
        (int w, int h, double _) = Sta.Run(() =>
        {
            var target = new WpfRenderTarget();
            target.BeginPage(1, 600, 800, 1.0, 0, 0, 90);
            (int w, int h, double s) dims = target.GetPageDimensions();
            target.EndPage();
            return dims;
        });

        Assert.Equal(800, w);
        Assert.Equal(600, h);
    }

    // ---- Clear ----

    [Fact]
    public void Clear_ResetsPageNumber()
    {
        int page = Sta.Run(() =>
        {
            var target = new WpfRenderTarget();
            target.BeginPage(3, 200, 300);
            target.EndPage();
            target.Clear();
            return target.CurrentPageNumber;
        });

        Assert.Equal(0, page);
    }

    // ---- Visual ----

    [Fact]
    public void Visual_IsNonNullAfterEndPage()
    {
        DrawingVisual? v = Sta.Run(() =>
        {
            var target = new WpfRenderTarget();
            target.BeginPage(1, 400, 600, 1.0);
            target.EndPage();
            return target.Visual;
        });

        Assert.NotNull(v);
    }

    // ---- FillPath ----

    [Fact]
    public void FillPath_RendersFilledRegion()
    {
        // Render a red filled rectangle on a 100×100 page and verify the center pixel is red.
        // Pbgra32 layout is B, G, R, A so index+2 is the Red channel.
        byte[] px = Sta.Run(() =>
        {
            var t = new WpfRenderTarget();
            t.BeginPage(1, 100, 100, 1.0, 0, 0, 0);

            var pb = new PathBuilder();
            pb.MoveTo(10, 10); pb.LineTo(90, 10); pb.LineTo(90, 90); pb.LineTo(10, 90); pb.ClosePath();

            var state = new PdfGraphicsState
            {
                ResolvedFillColor = new List<double> { 1.0, 0.0, 0.0 },
                ResolvedFillColorSpace = "DeviceRGB"
            };

            t.FillPath(pb, state, evenOdd: false);
            t.EndPage();

            var rtb = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(t.Visual);
            var buf = new byte[100 * 100 * 4];
            rtb.CopyPixels(buf, 100 * 4, 0);
            return buf;
        });

        int i = (50 * 100 + 50) * 4;   // center pixel, Pbgra32 = B G R A
        Assert.True(px[i + 2] > 200, $"R={px[i+2]}: center pixel should be red");
        Assert.True(px[i + 1] < 60,  $"G={px[i+1]}: center pixel should not be green");
        Assert.True(px[i + 0] < 60,  $"B={px[i+0]}: center pixel should not be blue");
    }

    // ---- StrokePath ----

    [Fact]
    public void StrokePath_ScalesLineWidthByCtm()
    {
        // LineWidth=100, CTM scale=0.1 → effective pixel width ≈ 10.
        // A bug (no CTM scaling) would paint all rows, making y=30 red.
        // With correct scaling the stroke only covers roughly y=45–55, leaving y=30 white.
        byte[] px = Sta.Run(() =>
        {
            var t = new WpfRenderTarget();
            t.BeginPage(1, 100, 100, 1.0, 0, 0, 0);

            var pb = new PathBuilder();
            pb.MoveTo(0, 50); pb.LineTo(100, 50);   // horizontal line at PDF y=50 → WPF y=50

            var state = new PdfGraphicsState
            {
                LineWidth = 100,
                Ctm = Matrix3x2.CreateScale(0.1f),   // sqrt(det) = 0.1  →  width = 10
                ResolvedStrokeColor = new List<double> { 1.0, 0.0, 0.0 },
                ResolvedStrokeColorSpace = "DeviceRGB"
            };

            t.StrokePath(pb, state);
            t.EndPage();

            var rtb = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(t.Visual);
            var buf = new byte[100 * 100 * 4];
            rtb.CopyPixels(buf, 100 * 4, 0);
            return buf;
        });

        // Center row (WPF y=50) must be red — inside the stroke.
        int center = (50 * 100 + 50) * 4;
        Assert.True(px[center + 2] > 200, $"R={px[center+2]}: center pixel should be red (inside stroke)");

        // Row at WPF y=30 (20px from line) must NOT be red — outside the CTM-scaled width of ~10.
        // If the bug were present (width=100) this pixel would be fully red.
        int outside = (30 * 100 + 50) * 4;
        Assert.True(px[outside + 2] < 60,
            $"R={px[outside+2]}: pixel at y=30 should not be red — CTM stroke-width scaling missing");
    }
}
