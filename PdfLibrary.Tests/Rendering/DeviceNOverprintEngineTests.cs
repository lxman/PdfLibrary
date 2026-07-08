using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Functions;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Engine fixes behind GWG190/191/192 DeviceN vector overprint: (1) a type-0 sampled function must index
/// its sample table with the FIRST input dimension varying fastest (ISO 32000 §7.10.2) — the reversed
/// order read the wrong samples and a DeviceN duotone resolved to zero ink; (2) a <c>/None</c> colorant
/// marks no plate and must be skipped when building the overprint plate mask, not treated as an
/// unmappable spot (which nulled the mask, dropping the DeviceN colorant-set overprint to OPM behaviour).
/// </summary>
public class DeviceNOverprintEngineTests
{
    private static PdfArray Ints(params int[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfInteger(v[i]);
        return new PdfArray(items);
    }

    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // A 2-in, 1-out type-0 function on a 2×2 grid. Samples in first-dimension-fastest order:
    // (i0,i1) = (0,0),(1,0),(0,1),(1,1) → 0, 85, 170, 255.
    private static PdfFunction TwoInSampled()
    {
        var dict = new PdfDictionary();
        dict.Add(new PdfName("FunctionType"), new PdfInteger(0));
        dict.Add(new PdfName("Domain"), Reals(0, 1, 0, 1));
        dict.Add(new PdfName("Range"), Reals(0, 1));
        dict.Add(new PdfName("Size"), Ints(2, 2));
        dict.Add(new PdfName("BitsPerSample"), new PdfInteger(8));
        var stream = new PdfStream(dict, [0, 85, 170, 255]);
        return PdfFunction.Create(stream, null)!;
    }

    [Fact]
    public void SampledFunction_multi_input_indexes_first_dimension_fastest()
    {
        PdfFunction fn = TwoInSampled();

        // (i0=1,i1=0) → sample[1] = 85/255 ≈ 0.333; (i0=0,i1=1) → sample[2] = 170/255 ≈ 0.667.
        // The old reversed index gave [1,0] → sample[2] (0.667), so this pins the ordering.
        Assert.Equal(85.0 / 255.0, fn.Evaluate([1, 0])[0], precision: 2);
        Assert.Equal(170.0 / 255.0, fn.Evaluate([0, 1])[0], precision: 2);
        Assert.Equal(0.0, fn.Evaluate([0, 0])[0], precision: 2);
        Assert.Equal(1.0, fn.Evaluate([1, 1])[0], precision: 2);
    }

    // [/DeviceN [names] /DeviceCMYK <dummy tint>] — PlatesForColorSpaceObject only reads the names.
    private static PdfArray DeviceN(params string[] names)
    {
        var nameObjs = new PdfObject[names.Length];
        for (var i = 0; i < names.Length; i++) nameObjs[i] = new PdfName(names[i]);
        return new PdfArray(new PdfName("DeviceN"), new PdfArray(nameObjs), new PdfName("DeviceCMYK"), new PdfInteger(0));
    }

    [Fact]
    public void PlateMask_skips_None_colorants()
    {
        // DeviceN[Cyan, None] marks only C (None marks nothing).
        Assert.Equal((true, false, false, false), ColorSpaceResolver.PlatesForColorSpaceObject(DeviceN("Cyan", "None"), null));
        // DeviceN[Cyan, Yellow, Black, None, None] marks C, Y, K.
        Assert.Equal((true, false, true, true),
            ColorSpaceResolver.PlatesForColorSpaceObject(DeviceN("Cyan", "Yellow", "Black", "None", "None"), null));
    }

    [Fact]
    public void PlateMask_still_null_for_a_real_spot_colorant()
    {
        // A genuine spot colorant can't map to a process plate → null (OPM fallback), unchanged.
        Assert.Null(ColorSpaceResolver.PlatesForColorSpaceObject(DeviceN("Cyan", "PANTONE 265 C"), null));
    }
}
