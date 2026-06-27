using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
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

    // ---- SetClippingPath ----

    [Fact]
    public void SetClippingPath_ClipsDrawing()
    {
        // Clip to the left half (PDF x: 0-50), then fill the whole 100×100 page red.
        // Left-half pixel (25,50) must be red; right-half pixel (75,50) must be background.
        byte[] px = Sta.Run(() =>
        {
            var t = new WpfRenderTarget();
            t.BeginPage(1, 100, 100, 1.0, 0, 0, 0);

            // Clip: left half rectangle in PDF user space.
            var clip = new PathBuilder();
            clip.MoveTo(0, 0); clip.LineTo(50, 0); clip.LineTo(50, 100); clip.LineTo(0, 100); clip.ClosePath();
            t.SetClippingPath(clip, new PdfGraphicsState(), evenOdd: false);

            // Fill: entire page red.
            var fill = new PathBuilder();
            fill.MoveTo(0, 0); fill.LineTo(100, 0); fill.LineTo(100, 100); fill.LineTo(0, 100); fill.ClosePath();
            var state = new PdfGraphicsState
            {
                ResolvedFillColor = new List<double> { 1.0, 0.0, 0.0 },
                ResolvedFillColorSpace = "DeviceRGB"
            };
            t.FillPath(fill, state, evenOdd: false);
            t.EndPage();

            var rtb = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(t.Visual);
            var buf = new byte[100 * 100 * 4];
            rtb.CopyPixels(buf, 100 * 4, 0);
            return buf;
        });

        // Left-half pixel at (25, 50) — Pbgra32: B G R A
        int left = (50 * 100 + 25) * 4;
        Assert.True(px[left + 2] > 200, $"R={px[left+2]}: left-half pixel should be red (inside clip)");

        // Right-half pixel at (75, 50) — must be transparent/background, not red.
        int right = (50 * 100 + 75) * 4;
        Assert.True(px[right + 2] < 60, $"R={px[right+2]}: right-half pixel should be unpainted (outside clip)");
    }

    // ---- SaveState / RestoreState ----

    [Fact]
    public void SaveRestoreState_LiftsSaveStateClip()
    {
        // 1. SetClippingPath to left half (x 0-50).
        // 2. SaveState.
        // 3. SetClippingPath to top half (y 50-100 in PDF space = bitmap y 0-50 after Y-flip).
        //    Combined: only top-left quadrant (x 0-50, PDF y 50-100 → bitmap y 0-50) is painted.
        // 4. RestoreState — second clip is lifted; back to left-half-only clip.
        // 5. FillPath entire page red.
        // After restore we expect left half fully red, right half unpainted,
        // and also bottom-left (PDF y < 50 → bitmap y > 50) to be red again (second clip gone).
        byte[] px = Sta.Run(() =>
        {
            var t = new WpfRenderTarget();
            t.BeginPage(1, 100, 100, 1.0, 0, 0, 0);

            // First clip: left half.
            var clip1 = new PathBuilder();
            clip1.MoveTo(0, 0); clip1.LineTo(50, 0); clip1.LineTo(50, 100); clip1.LineTo(0, 100); clip1.ClosePath();
            t.SetClippingPath(clip1, new PdfGraphicsState(), evenOdd: false);

            t.SaveState();

            // Second clip: top half in PDF space (y 50-100).
            // Combined intersection with first clip → only top-left quadrant.
            var clip2 = new PathBuilder();
            clip2.MoveTo(0, 50); clip2.LineTo(100, 50); clip2.LineTo(100, 100); clip2.LineTo(0, 100); clip2.ClosePath();
            t.SetClippingPath(clip2, new PdfGraphicsState(), evenOdd: false);

            t.RestoreState();   // lifts second clip; left-half clip remains.

            // Fill entire page red (only left half should paint).
            var fill = new PathBuilder();
            fill.MoveTo(0, 0); fill.LineTo(100, 0); fill.LineTo(100, 100); fill.LineTo(0, 100); fill.ClosePath();
            var state = new PdfGraphicsState
            {
                ResolvedFillColor = new List<double> { 1.0, 0.0, 0.0 },
                ResolvedFillColorSpace = "DeviceRGB"
            };
            t.FillPath(fill, state, evenOdd: false);
            t.EndPage();

            var rtb = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(t.Visual);
            var buf = new byte[100 * 100 * 4];
            rtb.CopyPixels(buf, 100 * 4, 0);
            return buf;
        });

        // Bottom-left quadrant (x=25, bitmap y=75 → PDF y=25): should be red if second clip is gone.
        int bottomLeft = (75 * 100 + 25) * 4;
        Assert.True(px[bottomLeft + 2] > 200,
            $"R={px[bottomLeft+2]}: bottom-left quadrant should be red (second clip was lifted)");

        // Right half (x=75, y=50): still outside first clip → background.
        int right = (50 * 100 + 75) * 4;
        Assert.True(px[right + 2] < 60,
            $"R={px[right+2]}: right-half pixel should be unpainted (first clip still active)");
    }

    // ---- DrawImage ----

    [Fact]
    public void DrawImage_RendersKnownColorAtExpectedPixel()
    {
        // Build a 1x1 DeviceRGB image with R=200, G=100, B=50 (fully opaque).
        // CTM scales the unit-square to 50×50 PDF units placed at (25,25).
        // After PageTransform the image occupies screen pixels (25,25)→(75,75);
        // center pixel (50,50) must carry the image colour.
        // RenderTargetBitmap uses Pbgra32 (B G R A); red channel is at index+2.
        byte[] px = Sta.Run(() =>
        {
            var dict = new PdfDictionary
            {
                [new PdfName("Subtype")]         = new PdfName("Image"),
                [new PdfName("Width")]           = new PdfInteger(1),
                [new PdfName("Height")]          = new PdfInteger(1),
                [new PdfName("ColorSpace")]      = new PdfName("DeviceRGB"),
                [new PdfName("BitsPerComponent")] = new PdfInteger(8)
            };
            // Raw DeviceRGB bytes for a single pixel: R=200, G=100, B=50.
            byte[] imgData = [200, 100, 50];
            var stream = new PdfStream(dict, imgData);
            var image  = new PdfImage(stream);   // internal ctor — requires InternalsVisibleTo

            var t = new WpfRenderTarget();
            t.BeginPage(1, 100, 100, 1.0, 0, 0, 0);

            // CTM: scale(50) + translate(25,25) — image covers PDF rect (25,25)→(75,75).
            var state = new PdfGraphicsState
            {
                Ctm = new Matrix3x2(50, 0, 0, 50, 25, 25)
            };
            t.DrawImage(image, state);
            t.EndPage();

            var rtb = new RenderTargetBitmap(100, 100, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(t.Visual);
            var buf = new byte[100 * 100 * 4];
            rtb.CopyPixels(buf, 100 * 4, 0);
            return buf;
        });

        // Center pixel (50,50) in Pbgra32 layout: [+0]=B, [+1]=G, [+2]=R, [+3]=A.
        int center = (50 * 100 + 50) * 4;
        Assert.True(px[center + 2] > 150,
            $"R={px[center+2]}: center pixel should carry the image red channel (~200)");
        Assert.True(px[center + 1] < 150,
            $"G={px[center+1]}: green channel should be lower than red");
        Assert.True(px[center + 0] < 100,
            $"B={px[center+0]}: blue channel should be lowest");
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
