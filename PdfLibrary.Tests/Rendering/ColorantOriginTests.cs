using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class ColorantOriginTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

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

    private static PdfDictionary ColorSpaces(string name, PdfArray def)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName(name), def);
        return d;
    }

    [Fact]
    public void Separation_ProducesOrigin_WithNameTintsAndAltSpace()
    {
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE 185 C"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 0, 0]));

        ColorantOrigin? origin = ColorSpaceResolver.OriginFor("CS0", new double[] { 0.5 }, ColorSpaces("CS0", sep), null);

        Assert.NotNull(origin);
        Assert.Equal(new[] { "PANTONE 185 C" }, origin!.Names);
        Assert.Equal(new[] { 0.5 }, origin.Tints);
        Assert.Equal("DeviceCMYK", origin.AlternateSpace);
    }

    [Fact]
    public void DeviceN_ProducesOrigin_WithAllNames()
    {
        var deviceN = new PdfArray(
            new PdfName("DeviceN"),
            new PdfArray(new PdfName("Spot1"), new PdfName("Spot2")),
            new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [1, 1, 1, 0]));

        ColorantOrigin? origin = ColorSpaceResolver.OriginFor("CS0", new double[] { 0.3, 0.7 }, ColorSpaces("CS0", deviceN), null);

        Assert.NotNull(origin);
        Assert.Equal(new[] { "Spot1", "Spot2" }, origin!.Names);
        Assert.Equal(new[] { 0.3, 0.7 }, origin.Tints);
    }

    [Fact]
    public void DeviceCmyk_ProducesNull()
    {
        Assert.Null(ColorSpaceResolver.OriginFor("DeviceCMYK", new double[] { 0, 1, 0, 0 }, null, null));
    }

    [Fact]
    public void ProcessNamedDeviceN_StillProducesOrigin()
    {
        // Identity is about the SOURCE space; Process/Spot classification happens later (Task 3).
        var deviceN = new PdfArray(
            new PdfName("DeviceN"),
            new PdfArray(new PdfName("Cyan"), new PdfName("Magenta")),
            new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [1, 1, 0, 0]));

        ColorantOrigin? origin = ColorSpaceResolver.OriginFor("CS0", new double[] { 0.5, 0.5 }, ColorSpaces("CS0", deviceN), null);

        Assert.NotNull(origin);
        Assert.Equal(new[] { "Cyan", "Magenta" }, origin!.Names);
    }
}
