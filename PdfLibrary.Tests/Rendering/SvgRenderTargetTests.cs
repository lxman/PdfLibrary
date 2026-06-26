using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Rendering.Svg;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

public class SvgRenderTargetTests
{
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
}
