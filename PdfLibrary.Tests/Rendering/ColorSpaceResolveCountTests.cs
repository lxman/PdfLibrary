using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class ColorSpaceResolveCountTests
{
    // G-12 BASELINE (Docs/colour/rendering-conformance.md): fixing row 4-4 made `cs` run the
    // whole of OnColorChanged (which resolves BOTH fill and stroke), and `sc`/`scn` runs it
    // again — so one cs + one sc costs FOUR ResolveColorSpace passes, each of which re-parses
    // any tint transform via the uncached PdfFunction.Create. This pin is the throughput hook:
    // the de-duplication design the G-12 entry calls for (caching a parsed tint transform per
    // colour-space resource, or splitting fill/stroke resolution) must LOWER this number and
    // deliberately retire this pin with the new count.
    [Fact]
    public void Cs_then_sc_resolves_four_times_G12Baseline()
    {
        var renderer = new PdfRenderer(new MockRenderTarget());

        renderer.ProcessOperators(new List<PdfOperator>
        {
            new SetFillColorSpaceOperator(new PdfName("DeviceRGB")),
            new SetFillColorOperator([new PdfReal(1), new PdfReal(0), new PdfReal(0)]),
        });

        Assert.Equal(4, renderer.ColorSpaceResolveCount);
    }
}
