using System.Windows;
using System.Windows.Media;

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
}
