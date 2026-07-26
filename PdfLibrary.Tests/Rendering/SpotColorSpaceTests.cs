using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The single parser for [/Separation …] and [/DeviceN …]. Deliberately PERMISSIVE: it accepts
/// arrays too short to carry an alternate or tint transform (reporting those as null) and records
/// unresolvable DeviceN name elements as null rather than rejecting the space. Five ColorSpaceResolver
/// members disagreed about strictness before Pass 1 — see the plan's arity table — so strictness stays
/// with each caller and only the PARSING is shared.
/// </summary>
public class SpotColorSpaceTests
{
    private const string Tint2 = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    [Fact]
    public void Separation_ParsesNameAlternateAndTransform()
    {
        Assert.True(SpotColorSpace.TryParse(Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]"),
            null, out SpotColorSpace? s));

        Assert.Equal("Separation", s!.Family);
        Assert.Equal(["GWGGreen"], s.Names);
        Assert.True(s.AllNamesResolved);
        Assert.Equal("DeviceCMYK", s.AlternateSpaceName);
        Assert.NotNull(s.AlternateObject);
        Assert.NotNull(s.TintTransformObject);
    }

    [Fact]
    public void DeviceN_ParsesEveryNameInOrder()
    {
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal("DeviceN", s!.Family);
        Assert.Equal(["GWGGreen", "Cyan"], s.Names);
        Assert.True(s.AllNamesResolved);
    }

    [Fact]
    public void ArrayShorterThanFour_StillParses_WithNullAlternateAndTransform()
    {
        // PaintsNothing and PlatesForColorSpaceObject accept Count >= 2 today. If TryParse demanded 4,
        // a [/Separation /None] array would silently stop being suppressed.
        Assert.True(SpotColorSpace.TryParse(Parse("[/Separation /None]"), null, out SpotColorSpace? s));

        Assert.Equal(["None"], s!.Names);
        Assert.Null(s.AlternateObject);
        Assert.Null(s.TintTransformObject);
        Assert.Equal(string.Empty, s.AlternateSpaceName);
    }

    [Fact]
    public void DeviceN_NonNameElement_IsNull_ButTheCountIsStillRight()
    {
        // BuildTintToRgb/BuildTintToCmyk use only Names.Count and must keep working; the strict members
        // use AllNamesResolved to reject. Element 1 here is a number, not a name.
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen 42] /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal(2, s!.Names.Count);
        Assert.Equal("GWGGreen", s.Names[0]);
        Assert.Null(s.Names[1]);
        Assert.False(s.AllNamesResolved);
    }

    [Fact]
    public void ArrayAlternate_ReportsItsFamilyName()
    {
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/Separation /Spot1 [/CalRGB << /WhitePoint [0.9505 1 1.089] >>] "
                  + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >>]"),
            null, out SpotColorSpace? s));

        Assert.Equal("CalRGB", s!.AlternateSpaceName);
    }

    [Theory]
    [InlineData("[/Indexed /DeviceRGB 1 <FF0000 00FF00>]")]
    [InlineData("[/ICCBased 5 0 R]")]
    public void NonSpotFamilies_DoNotParse(string literal)
    {
        Assert.False(SpotColorSpace.TryParse(Parse(literal), null, out SpotColorSpace? s));
        Assert.Null(s);
    }

    [Fact]
    public void NullObject_DoesNotParse()
    {
        Assert.False(SpotColorSpace.TryParse(null, null, out SpotColorSpace? s));
        Assert.Null(s);
    }

    // --- /Attributes: parsed here, consumed in Pass 2 ---

    [Fact]
    public void Subtype_DefaultsToDeviceN_WhenNoAttributesDictionary()
    {
        // ISO 32000-2 Table 70: "Values shall be DeviceN or NChannel. Default value: DeviceN."
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal("DeviceN", s!.Subtype);
        Assert.False(s.IsNChannel);
        Assert.Null(s.Colorants);
        Assert.Null(s.Process);
    }

    [Fact]
    public void NChannelAttributes_AreParsed()
    {
        // The shape of GWG081_DeviceN-Support_5c_X1a.pdf, the corpus's only NChannel file.
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK " + Tint2 + " << "
                  + "/Subtype /NChannel "
                  + "/Colorants << /GWGGreen [/Separation /GWGGreen /DeviceCMYK " + Tint2 + "] >> "
                  + "/Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow /Black] >> "
                  + ">>]"),
            null, out SpotColorSpace? s));

        Assert.Equal("NChannel", s!.Subtype);
        Assert.True(s.IsNChannel);
        Assert.NotNull(s.Colorants);
        Assert.True(s.Colorants!.TryGetValue(new PdfName("GWGGreen"), out PdfObject? _));
        Assert.NotNull(s.Process);
        Assert.True(s.Process!.TryGetValue(new PdfName("Components"), out PdfObject? _));
    }

    [Fact]
    public void SeparationNeverCarriesAttributes()
    {
        // /Attributes is a DeviceN-only element; a five-element Separation array is malformed and its
        // fifth element must not be mistaken for an attributes dictionary.
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/Separation /Spot1 /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]"),
            null, out SpotColorSpace? s));

        Assert.Equal("DeviceN", s!.Subtype);
        Assert.False(s.IsNChannel);
    }
}
