using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class ShadingSpotInkTests
{
    // Minimal /Function: a Type-2 exponential 0→1 on one output (tint ramp). Reuse the shading-test idiom.
    private static PdfDictionary Type2Fn(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d[new PdfName("FunctionType")] = new PdfInteger(2);
        d[new PdfName("Domain")] = new PdfArray(new PdfReal(0), new PdfReal(1));
        d[new PdfName("C0")] = new PdfArray(System.Array.ConvertAll(c0, v => (PdfObject)new PdfReal(v)));
        d[new PdfName("C1")] = new PdfArray(System.Array.ConvertAll(c1, v => (PdfObject)new PdfReal(v)));
        d[new PdfName("N")] = new PdfReal(1);
        return d;
    }

    private static PdfArray SeparationCmyk(string name) => new(
        new PdfName("Separation"), new PdfName(name), new PdfName("DeviceCMYK"),
        Type2Fn([0, 0, 0, 0], [0, 1, 0, 0]));   // tint 0→(0,0,0,0), tint 1→(0,1,0,0) — a magenta-ish spot

    // Tint transform is a single-input, 4-output Type-2 (eval-valid → BuildCmykMapper returns non-null so
    // SpotInk is emitted). The SP-7 split reads the raw shading /Function components, NOT this transform,
    // so its exact CMYK output is irrelevant to the assertions.
    private static PdfArray DeviceNCmyk(string[] names) => new(
        new PdfName("DeviceN"),
        new PdfArray(System.Array.ConvertAll(names, n => (PdfObject)new PdfName(n))),
        new PdfName("DeviceCMYK"),
        Type2Fn([0, 0, 0, 0], [0, 1, 0, 0]));

    private static PdfDictionary AxialShading(PdfObject colorSpace, PdfObject function)
    {
        var d = new PdfDictionary();
        d[new PdfName("ShadingType")] = new PdfInteger(2);
        d[new PdfName("Coords")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(1), new PdfReal(0));
        d[new PdfName("ColorSpace")] = colorSpace;
        d[new PdfName("Function")] = function;
        return d;
    }

    [Fact]
    public void PureSeparationSpot_PopulatesSpotInk_ProcessZero()
    {
        // Separation shading whose /Function maps t → the single GWG Green tint (t itself).
        PdfDictionary dict = AxialShading(SeparationCmyk("GWG Green"), Type2Fn([0], [1]));
        ShadingDescriptor? sh = ShadingBuilder.Build(dict, null);

        Assert.NotNull(sh);
        Assert.NotNull(sh!.SpotInk);
        Assert.Equal(new[] { "GWG Green" }, sh.SpotInk!.Names);
        Assert.Equal(sh.Stops.Length, sh.SpotInk.StopProcessCmyk.Length);
        Assert.All(sh.SpotInk.StopProcessCmyk, v => Assert.Equal(0u, v));   // pure spot ⇒ process all zero
        // Last stop's tint (t≈1) ≈ full; first stop (t≈0) ≈ 0.
        int n = sh.SpotInk.Names.Count;
        Assert.True(sh.SpotInk.StopTints[(sh.Stops.Length - 1) * n] > 200);
        Assert.True(sh.SpotInk.StopTints[0] < 40);
        // Mirror: CmykColors still the full flatten (unchanged).
        Assert.Equal(sh.Stops.Length, sh.CmykColors.Length);
    }

    [Fact]
    public void DeviceNSpotPlusProcess_SplitsCyanToProcess_SpotToTints()
    {
        // DeviceN [GWG Green, Cyan]; /Function outputs both at full at t=1.
        PdfDictionary dict = AxialShading(DeviceNCmyk(["GWG Green", "Cyan"]),
            new PdfArray(Type2Fn([0], [1]), Type2Fn([0], [1])));
        ShadingDescriptor? sh = ShadingBuilder.Build(dict, null);

        Assert.NotNull(sh!.SpotInk);
        Assert.Equal(new[] { "GWG Green" }, sh.SpotInk!.Names);   // only the spot name
        // At the last stop (t≈1): Cyan → C plate ≈ full; GWG Green → tint ≈ full.
        int last = sh.Stops.Length - 1, n = sh.SpotInk.Names.Count;
        Assert.True((sh.SpotInk.StopProcessCmyk[last] >> 24 & 0xFF) > 200);   // C plate
        Assert.Equal(0u, sh.SpotInk.StopProcessCmyk[last] & 0x00FFFFFFu);      // M/Y/K zero
        Assert.True(sh.SpotInk.StopTints[last * n] > 200);                     // GWG Green tint
    }

    [Fact]
    public void DeviceCmykShading_NoSpotInk()
    {
        PdfDictionary dict = AxialShading(new PdfName("DeviceCMYK"),
            new PdfArray(Type2Fn([0], [1]), Type2Fn([0], [1]), Type2Fn([0], [1]), Type2Fn([0], [1])));
        ShadingDescriptor? sh = ShadingBuilder.Build(dict, null);
        Assert.NotNull(sh);
        Assert.Null(sh!.SpotInk);
    }

    [Fact]
    public void DeviceRgbShading_NoSpotInk()
    {
        PdfDictionary dict = AxialShading(new PdfName("DeviceRGB"),
            new PdfArray(Type2Fn([0], [1]), Type2Fn([0], [1]), Type2Fn([0], [1])));
        ShadingDescriptor? sh = ShadingBuilder.Build(dict, null);
        Assert.NotNull(sh);
        Assert.Null(sh!.SpotInk);   // non-CMYK ⇒ SampleRgbAt path, no spot plane
    }
}
