using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Pins <see cref="ColorSpaceResolver.PaintsNothing(PdfObject?, PdfDocument?)"/> directly, independent of
/// any render path. <see cref="SeparationNoneImageShadingTests"/> asserts the same rule through rendered
/// pixels, but two of those cases (image XObject and inline image) are ALSO covered by the tint-transform
/// builder returning null for /None (G-5b) — a second, independent suppression that would keep those
/// tests green even if this predicate regressed. Testing the predicate here removes that ambiguity: these
/// tests fail if and only if <c>PaintsNothing</c> itself is wrong.
/// </summary>
public class ColorSpaceResolverPaintsNothingTests
{
    private const string Tint = "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        // Reuses the same hand-authored-PDF convention as ColourConformancePage: parse a literal colour
        // space array through a throwaway one-page document rather than hand-building PdfArray/PdfName
        // object graphs, so the fixture text matches exactly what SeparationNoneImageShadingTests writes.
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    [Fact]
    public void SeparationNone_PaintsNothing()
    {
        PdfArray cs = Parse("[/Separation /None /DeviceRGB " + Tint + "]");
        Assert.True(ColorSpaceResolver.PaintsNothing(cs, null));
    }

    [Fact]
    public void DeviceNAllNone_PaintsNothing()
    {
        PdfArray cs = Parse("[/DeviceN [/None /None] /DeviceRGB " + Tint + "]");
        Assert.True(ColorSpaceResolver.PaintsNothing(cs, null));
    }

    [Fact]
    public void IndexedOverSeparationNone_PaintsNothing()
    {
        // The case that discriminates I-1: the tint-builder null (G-5b) does NOT cover Indexed, so this
        // is the one case among these five that is exercised ONLY through PaintsNothing itself, never
        // through the second suppression mechanism.
        PdfArray cs = Parse("[/Indexed [/Separation /None /DeviceRGB " + Tint + "] 1 <0055>]");
        Assert.True(ColorSpaceResolver.PaintsNothing(cs, null));
    }

    [Fact]
    public void OrdinarySpotSeparation_DoesNotPaintNothing()
    {
        PdfArray cs = Parse("[/Separation /Spot1 /DeviceRGB " + Tint + "]");
        Assert.False(ColorSpaceResolver.PaintsNothing(cs, null));
    }

    [Fact]
    public void SeparationAll_DoesNotPaintNothing()
    {
        // /All paints every process colourant (§8.6.6.4) — it is the opposite of /None and must not be
        // suppressed.
        PdfArray cs = Parse("[/Separation /All /DeviceCMYK " + Tint + "]");
        Assert.False(ColorSpaceResolver.PaintsNothing(cs, null));
    }
}
