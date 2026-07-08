using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// <see cref="ColorSpaceResolver.OverprintPlatesFor"/> derives a per-plate CMYK overprint mask from a
/// Separation/DeviceN source colour space's colorants so an overprinting spot/DeviceN colour preserves
/// the plates it does not paint (ISO 32000 §8.6.6.3), independent of OPM. Device spaces and spot
/// colorants return null (the caller keeps OPM behaviour).
/// </summary>
public class OverprintPlatesTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // Type 2 exponential tint transform; the actual values are irrelevant to the plate mask, which is
    // derived purely from the colorant names, but a well-formed function keeps the array realistic.
    private static PdfDictionary Type2(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(c0));
        d.Add(new PdfName("C1"), Reals(c1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    // Wraps a colour-space definition array under a resource name in a /ColorSpace dictionary.
    private static PdfDictionary ColorSpaces(string name, PdfArray def)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName(name), def);
        return d;
    }

    [Fact]
    public void SeparationMagenta_ReturnsMagentaPlateOnly()
    {
        // [/Separation /Magenta /DeviceCMYK {t -> [0 t 0 0]}]
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("Magenta"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 0, 0]));

        var plates = ColorSpaceResolver.OverprintPlatesFor("CS0", ColorSpaces("CS0", sep), null);

        Assert.NotNull(plates);
        Assert.Equal((false, true, false, false), plates!.Value);
    }

    [Fact]
    public void DeviceN_CyanMagentaYellow_ReturnsThoseThreePlates()
    {
        // [/DeviceN [/Cyan /Magenta /Yellow] /DeviceCMYK <tint>]
        var deviceN = new PdfArray(
            new PdfName("DeviceN"),
            new PdfArray(new PdfName("Cyan"), new PdfName("Magenta"), new PdfName("Yellow")),
            new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [1, 1, 1, 0]));

        var plates = ColorSpaceResolver.OverprintPlatesFor("CS0", ColorSpaces("CS0", deviceN), null);

        Assert.NotNull(plates);
        Assert.Equal((true, true, true, false), plates!.Value);
    }

    [Fact]
    public void DeviceCmyk_ReturnsNull()
    {
        // A device colour space carries no colorant-derived mask (OPM logic applies instead).
        Assert.Null(ColorSpaceResolver.OverprintPlatesFor("DeviceCMYK", null, null));
    }

    [Fact]
    public void SpotSeparation_ReturnsNull()
    {
        // A spot colorant name isn't a CMYK plate → null so the caller falls back to OPM behaviour.
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE 021 C"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 0.6, 1, 0]));

        Assert.Null(ColorSpaceResolver.OverprintPlatesFor("CS0", ColorSpaces("CS0", sep), null));
    }
}
