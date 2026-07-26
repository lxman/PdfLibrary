using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Direct coverage for the ColorSpaceResolver query members that had none before Pass 1 —
/// BuildTintToCmyk, OriginForColorSpaceObject and the resource-name PaintsNothing overload were
/// exercised only through their callers. These pin current behaviour so the Pass 1 migration onto
/// SpotColorSpace has a net under it: a behaviour change that a caller happens not to exercise
/// would otherwise pass unnoticed.
/// </summary>
public class ColorSpaceResolverCharacterizationTests
{
    private const string Tint2 = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>";
    private const string TintGray = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    private static (PdfDictionary Spaces, PdfDocument Doc) ParseWithResources(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = doc.GetPage(0)!;
        return (page.GetResources()!.GetColorSpaces()!, doc);
    }

    // --- BuildTintToCmyk ---

    [Fact]
    public void BuildTintToCmyk_SeparationWithCmykAlternate_EvaluatesTheTransform()
    {
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");

        Func<double[], (double C, double M, double Y, double K)>? f =
            ColorSpaceResolver.BuildTintToCmyk(cs, null, out int inputs);

        Assert.NotNull(f);
        Assert.Equal(1, inputs);
        (double c, double m, double y, double k) = f!([1.0]);
        Assert.Equal(0.5, c, 3);
        Assert.Equal(0.0, m, 3);
        Assert.Equal(1.0, y, 3);
        Assert.Equal(0.0, k, 3);
    }

    [Fact]
    public void BuildTintToCmyk_GrayAlternate_MapsToKOnly()
    {
        // §10.3.3: DeviceGray separates onto the black plate alone, k = 1 - gray. At tint 1 the
        // transform yields gray 0, so k must be 1 — full black, not white.
        PdfArray cs = Parse("[/Separation /Spot1 /DeviceGray " + TintGray + "]");

        Func<double[], (double C, double M, double Y, double K)>? f =
            ColorSpaceResolver.BuildTintToCmyk(cs, null, out int _);

        Assert.NotNull(f);
        (double c, double m, double y, double k) = f!([1.0]);
        Assert.Equal(0.0, c, 3);
        Assert.Equal(0.0, m, 3);
        Assert.Equal(0.0, y, 3);
        Assert.Equal(1.0, k, 3);
    }

    [Fact]
    public void BuildTintToCmyk_RgbAlternate_ReturnsNull()
    {
        // Not convertible to native ink: the caller falls back to the RGB path.
        PdfArray cs = Parse("[/Separation /Spot1 /DeviceRGB "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");

        Assert.Null(ColorSpaceResolver.BuildTintToCmyk(cs, null, out int _));
    }

    [Fact]
    public void BuildTintToCmyk_SeparationAll_PaintsEveryPlateUncomplemented()
    {
        // §8.6.6.4 row 4-10: alternate and tint transform are ignored for /All, and on a subtractive
        // device the tint applies DIRECTLY. Tint 0.25 must be 0.25 on all four plates, not 0.75.
        PdfArray cs = Parse("[/Separation /All /DeviceRGB "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");

        Func<double[], (double C, double M, double Y, double K)>? f =
            ColorSpaceResolver.BuildTintToCmyk(cs, null, out int inputs);

        Assert.NotNull(f);
        Assert.Equal(1, inputs);
        (double c, double m, double y, double k) = f!([0.25]);
        Assert.Equal(0.25, c, 3);
        Assert.Equal(0.25, m, 3);
        Assert.Equal(0.25, y, 3);
        Assert.Equal(0.25, k, 3);
    }

    [Fact]
    public void BuildTintToCmyk_SeparationNone_ReturnsNull()
    {
        PdfArray cs = Parse("[/Separation /None /DeviceCMYK " + Tint2 + "]");
        Assert.Null(ColorSpaceResolver.BuildTintToCmyk(cs, null, out int _));
    }

    [Fact]
    public void BuildTintToCmyk_DeviceN_ReportsOneInputPerColorantName()
    {
        PdfArray cs = Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>]");

        ColorSpaceResolver.BuildTintToCmyk(cs, null, out int inputs);
        Assert.Equal(2, inputs);
    }

    // --- OriginForColorSpaceObject ---

    [Fact]
    public void OriginForColorSpaceObject_Separation_CarriesNameTintAndAlternate()
    {
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, [0.75], null);

        Assert.NotNull(origin);
        Assert.Equal(["GWGGreen"], origin!.Names);
        Assert.Equal([0.75], origin.Tints);
        Assert.Equal("DeviceCMYK", origin.AlternateSpace);
    }

    [Fact]
    public void OriginForColorSpaceObject_DeviceN_CarriesEveryNameInOrder()
    {
        PdfArray cs = Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, [0.25, 0.5], null);

        Assert.NotNull(origin);
        Assert.Equal(["GWGGreen", "Cyan"], origin!.Names);
        Assert.Equal([0.25, 0.5], origin.Tints);
    }

    [Fact]
    public void OriginForColorSpaceObject_NullRawColor_YieldsEmptyTints()
    {
        // Shadings resolve their origin with rawColor null — a gradient has no single per-op tint.
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, null, null);

        Assert.NotNull(origin);
        Assert.Empty(origin!.Tints);
        Assert.Equal(["GWGGreen"], origin.Names);
    }

    [Fact]
    public void OriginForColorSpaceObject_IccBased_ReturnsNull()
    {
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");
        // A non-Separation/DeviceN family must yield no origin. Reuse an Indexed array as the negative.
        PdfArray indexed = Parse("[/Indexed /DeviceRGB 1 <FF0000 00FF00>]");

        Assert.NotNull(ColorSpaceResolver.OriginForColorSpaceObject(cs, [1.0], null));
        Assert.Null(ColorSpaceResolver.OriginForColorSpaceObject(indexed, [1.0], null));
    }

    // --- PaintsNothing (resource-name overload) ---

    [Fact]
    public void PaintsNothing_ByResourceName_ResolvesThroughTheColorSpaceDictionary()
    {
        (PdfDictionary spaces, PdfDocument doc) =
            ParseWithResources("[/Separation /None /DeviceRGB "
                               + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");
        using (doc)
        {
            Assert.True(ColorSpaceResolver.PaintsNothing("Cs0", spaces, doc));
        }
    }

    [Theory]
    [InlineData("DeviceGray")]
    [InlineData("DeviceRGB")]
    [InlineData("DeviceCMYK")]
    [InlineData("Pattern")]
    public void PaintsNothing_ByResourceName_DeviceAndPatternSpacesAreNeverSuppressed(string csName)
    {
        (PdfDictionary spaces, PdfDocument doc) =
            ParseWithResources("[/Separation /None /DeviceRGB "
                               + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");
        using (doc)
        {
            Assert.False(ColorSpaceResolver.PaintsNothing(csName, spaces, doc));
        }
    }

    [Fact]
    public void PaintsNothing_ByResourceName_UnknownNameIsFalse()
    {
        (PdfDictionary spaces, PdfDocument doc) =
            ParseWithResources("[/Separation /None /DeviceRGB "
                               + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");
        using (doc)
        {
            Assert.False(ColorSpaceResolver.PaintsNothing("NoSuchSpace", spaces, doc));
            Assert.False(ColorSpaceResolver.PaintsNothing("Cs0", null, doc));
            Assert.False(ColorSpaceResolver.PaintsNothing(null, spaces, doc));
        }
    }
}
