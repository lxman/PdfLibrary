using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class ColorSpaceResolveCountTests
{
    // G-12 BASELINE (Docs/colour/rendering-conformance.md): fixing row 4-4 made `cs` run the
    // whole of OnColorChanged (which resolves BOTH fill and stroke), and `sc`/`scn` runs it
    // again — so one cs + one sc costs FOUR ResolveColorSpace passes. ResolveCallCount++ is the
    // FIRST statement of ResolveColorSpace, ahead of both the IsNullOrEmpty return and the
    // device-colour-space skip, so this fixture's /DeviceRGB fill (and default DeviceGray
    // stroke) take the device skip on all four entries and parse zero tint transforms — this is
    // a method-ENTRY counter, not a work counter. It guards ONLY the fill/stroke-split fix shape
    // (4 -> 2); a tint-transform-caching fix (the option the G-12 entry emphasises, since the
    // complaint is redundant re-parsing through the uncached PdfFunction.Create) leaves this
    // count at 4 and would NOT flip this pin. Guarding the caching shape needs a separate
    // counter at the tint-transform parse site, exercised by a /Separation fixture with a real
    // type-2 tint transform — not built here.
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
