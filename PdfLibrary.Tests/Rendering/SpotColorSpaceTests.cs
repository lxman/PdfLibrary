using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The single parser for [/Separation …] and [/DeviceN …]. Deliberately PERMISSIVE: it accepts
/// arrays too short to carry an alternate or tint transform (reporting those as null), records an
/// unresolvable Separation or DeviceN name element as null rather than rejecting the space, and accepts
/// a zero-length DeviceN names array. Five ColorSpaceResolver members disagreed about strictness before
/// Pass 1 — see the plan's arity table — so strictness stays with each caller and only the PARSING is
/// shared.
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

    /// <summary>
    /// Like <see cref="Parse"/> but keeps the document alive and returns it too, for the tests that
    /// need <see cref="ColorSpaceResolver.Deref"/> to actually resolve an indirect reference rather than
    /// short-circuiting on a null document. Follows the same idiom as
    /// <c>ColorSpaceResolverCharacterizationTests.ParseWithResources</c> — the caller disposes the
    /// document via <c>using (doc)</c>.
    /// </summary>
    private static (PdfArray Array, PdfDocument Doc) ParseWithDoc(
        string pdfArrayLiteral, params string[] extraObjects)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f",
            withFont: false, extraResources: "", extraObjects: extraObjects);
        PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return ((PdfArray)colorSpaces[new PdfName("Cs0")]!, doc);
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
    public void Separation_NonNameColorant_ParsesWithNullName_NotRejected()
    {
        // CRITICAL: BuildTintToRgb/BuildTintToCmyk set inputComponents = 1 for a Separation and never
        // require element 1 to be a name (they deref it only to test for /All) — a
        // [/Separation 42 /DeviceCMYK <tint>] array builds a working evaluator today. If TryParse
        // rejected this, a future migration of those builders onto TryParse would get null back with
        // no way to recover "this was a Separation with one input" — unlike every other strictness
        // difference, this one is not re-tightenable at the call site.
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/Separation 42 /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal("Separation", s!.Family);
        Assert.Single(s.Names);
        Assert.Null(s.Names[0]);
        Assert.False(s.AllNamesResolved);
    }

    [Fact]
    public void DeviceN_EmptyNamesArray_ParsesWithZeroCount()
    {
        // IMPORTANT: every current ColorSpaceResolver member rejects a zero-length DeviceN names array
        // outright, so this is unpinned behaviour today. AllNamesResolved is vacuously true for an empty
        // list (see its doc-comment) — a caller migrating "every component is /None" logic must check
        // Names.Count == 0 separately, or a [/DeviceN [] ...] array would flip from "not suppressed" to
        // "suppressed".
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [] /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal("DeviceN", s!.Family);
        Assert.Empty(s.Names);
        Assert.True(s.AllNamesResolved);
    }

    [Fact]
    public void DeviceN_NameElement_IsIndirectReference_Resolves()
    {
        // IMPORTANT: all eleven original tests passed `null` for the document, so every Deref call
        // inside TryParse was a no-op short-circuit — none of the indirect-reference paths this record
        // exists to share were actually exercised. Object 5 here is a bare /GWGGreen name; element 0 of
        // the DeviceN names array references it indirectly.
        (PdfArray arr, PdfDocument doc) =
            ParseWithDoc("[/DeviceN [5 0 R /Cyan] /DeviceCMYK " + Tint2 + "]", "/GWGGreen");
        using (doc)
        {
            Assert.True(SpotColorSpace.TryParse(arr, doc, out SpotColorSpace? s));

            Assert.Equal(["GWGGreen", "Cyan"], s!.Names);
            Assert.True(s.AllNamesResolved);
        }
    }

    [Fact]
    public void Attributes_AsIndirectReference_Resolves()
    {
        // IMPORTANT: the /Attributes dictionary itself (element 4) is also dereferenced by TryParse;
        // object 5 here is the attributes dictionary, referenced indirectly rather than inline.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK " + Tint2 + " 5 0 R]",
            "<< /Subtype /NChannel "
            + "/Colorants << /GWGGreen [/Separation /GWGGreen /DeviceCMYK " + Tint2 + "] >> "
            + "/Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow /Black] >> >>");
        using (doc)
        {
            Assert.True(SpotColorSpace.TryParse(arr, doc, out SpotColorSpace? s));

            Assert.Equal("NChannel", s!.Subtype);
            Assert.True(s.IsNChannel);
            Assert.NotNull(s.Colorants);
            Assert.True(s.Colorants!.TryGetValue(new PdfName("GWGGreen"), out PdfObject? _));
            Assert.NotNull(s.Process);
            Assert.True(s.Process!.TryGetValue(new PdfName("Components"), out PdfObject? _));
        }
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
