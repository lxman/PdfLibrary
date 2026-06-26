using System.Text.RegularExpressions;
using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.Svg;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

public class SvgRenderTargetTests
{
    [Fact]
    public void StrokePath_ScalesLineWidthByCtm()
    {
        // LineWidth is in PDF user space; paths arrive CTM-baked. A stroke drawn under a cm that
        // scales the space by 0.5 must emit stroke-width = LineWidth * 0.5 — not the raw LineWidth.
        // Without this, figures drawn in scaled coordinate systems get far-too-thick lines.
        var target = new SvgRenderTarget();
        target.BeginPage(1, 200, 200, 1.0);
        var path = new PathBuilder();
        path.MoveTo(0, 0);
        path.LineTo(100, 100);
        var state = new PdfGraphicsState
        {
            LineWidth = 10,
            Ctm = System.Numerics.Matrix3x2.CreateScale(0.5f) // sqrt(det) = 0.5
        };

        target.StrokePath(path, state);
        target.EndPage();

        string svg = target.GetSvg();
        Assert.Contains("stroke-width=\"5\"", svg);          // 10 * 0.5
        Assert.DoesNotContain("stroke-width=\"10\"", svg);   // not the raw, un-scaled width
    }

    [Fact]
    public void RenderToSvg_FilledRectangle_EmitsPathWithRootTransform()
    {
        // A page that fills a red rectangle. Expect an <svg>, a root <g transform="matrix(...)">,
        // and a <path> with the red fill.
        // Note: AddRectangle takes PdfColor?, not a string; PdfColor.FromHex parses "#FF0000".
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddRectangle(100, 100, 200, 150, fillColor: PdfColor.FromHex("#FF0000")))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        string svg = doc.GetPage(0)!.RenderToSvg();

        Assert.Contains("<svg", svg);
        Assert.Contains("transform=\"matrix(", svg);
        Assert.Contains("<path", svg);
        Assert.Contains("fill=\"rgb(255,0,0)\"", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void SetClippingPath_EmitsClipPathGroupInSvg()
    {
        // Unit-level test: construct SvgRenderTarget directly, call SetClippingPath with a
        // rectangle path, and verify the SVG output contains the expected clip-path elements.
        var target = new SvgRenderTarget();
        target.BeginPage(1, 612, 792);

        var path = new PathBuilder();
        path.Rectangle(50, 50, 200, 150);

        var state = new PdfGraphicsState();

        target.SetClippingPath(path, state, evenOdd: false);
        target.EndPage();

        string svg = target.GetSvg();
        Assert.Contains("<clipPath id=", svg);
        Assert.Contains("clip-path=\"url(#", svg);
    }

    [Fact]
    public void DrawImage_JpegData_EmitsJpegDataUri()
    {
        // Unit-level test: construct SvgRenderTarget directly, call DrawImage with a PdfImage
        // whose encoded data starts with the JPEG magic bytes (FF D8 FF), and verify the SVG
        // output contains an <image> element with a data:image/jpeg;base64, href.
        var target = new SvgRenderTarget();
        target.BeginPage(1, 612, 792);

        // Build a minimal image XObject stream with a JPEG SOI + APP0 header
        byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46]; // JFIF start
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("Filter")] = new PdfName("DCTDecode")
        };
        var stream = new PdfStream(dict, jpegBytes);
        var image = new PdfImage(stream);

        var state = new PdfGraphicsState();

        target.DrawImage(image, state);
        target.EndPage();

        string svg = target.GetSvg();
        Assert.Contains("<image", svg);
        Assert.Contains("data:image/jpeg;base64,", svg);
    }

    [Fact]
    public void ClipNesting_NestedSaveRestore_BalancedGGroups()
    {
        // Drives a nested SaveState/SetClippingPath/RestoreState sequence and verifies
        // the emitted SVG has balanced <g> opening and </g> closing tags.
        var target = new SvgRenderTarget();
        target.BeginPage(1, 200, 200, 1.0);

        var clip = new PathBuilder();
        clip.Rectangle(10, 10, 100, 100);
        var state = new PdfGraphicsState();

        target.SaveState();
        target.SetClippingPath(clip, state, evenOdd: false);
        target.SaveState();
        target.SetClippingPath(clip, state, evenOdd: false);
        target.RestoreState();
        target.RestoreState();
        target.EndPage();

        string svg = target.GetSvg();

        int openCount  = Regex.Matches(svg, @"<g\b").Count;
        int closeCount = Regex.Matches(svg, "</g>").Count;

        Assert.Equal(openCount, closeCount);
    }
}
