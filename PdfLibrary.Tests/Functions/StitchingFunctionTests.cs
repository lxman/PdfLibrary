using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Functions;

namespace PdfLibrary.Tests.Functions;

/// <summary>
/// Tests for the Type 3 (stitching) PDF function — the canonical shape of a gradient colour ramp,
/// a stitch of Type 2 (exponential) segments mapped onto subintervals of the domain.
/// </summary>
public class StitchingFunctionTests
{
    private static PdfArray Reals(params double[] values)
    {
        var items = new PdfObject[values.Length];
        for (var i = 0; i < values.Length; i++) items[i] = new PdfReal(values[i]);
        return new PdfArray(items);
    }

    private static PdfDictionary Type2(double[] c0, double[] c1, double n)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(c0));
        d.Add(new PdfName("C1"), Reals(c1));
        d.Add(new PdfName("N"), new PdfReal(n));
        return d;
    }

    private static PdfDictionary Stitch(PdfObject functions, double[] bounds, double[] encode)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(3));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("Functions"), functions);
        d.Add(new PdfName("Bounds"), Reals(bounds));
        d.Add(new PdfName("Encode"), Reals(encode));
        return d;
    }

    [Fact]
    public void TwoLinearSegments_FormTriangleRamp()
    {
        // f0: 0 -> 1 over [0, 0.5]; f1: 1 -> 0 over [0.5, 1]; the stitched ramp peaks at x = 0.5.
        PdfDictionary dict = Stitch(
            new PdfArray(Type2([0], [1], 1), Type2([1], [0], 1)),
            bounds: [0.5],
            encode: [0, 1, 0, 1]);

        var fn = PdfFunction.Create(dict, null);

        Assert.NotNull(fn);
        Assert.Equal(0.0, fn!.Evaluate([0.0])[0], 3);
        Assert.Equal(0.5, fn.Evaluate([0.25])[0], 3);
        Assert.Equal(1.0, fn.Evaluate([0.5])[0], 3);
        Assert.Equal(0.5, fn.Evaluate([0.75])[0], 3);
        Assert.Equal(0.0, fn.Evaluate([1.0])[0], 3);
    }

    [Fact]
    public void ThreeComponentRamp_StitchesRgbSegments()
    {
        // red (1,0,0) -> green (0,1,0) over [0,0.5], then green -> blue (0,0,1) over [0.5,1].
        PdfDictionary dict = Stitch(
            new PdfArray(
                Type2([1, 0, 0], [0, 1, 0], 1),
                Type2([0, 1, 0], [0, 0, 1], 1)),
            bounds: [0.5],
            encode: [0, 1, 0, 1]);

        var fn = PdfFunction.Create(dict, null)!;

        double[] mid0 = fn.Evaluate([0.25]); // halfway red -> green
        Assert.Equal(0.5, mid0[0], 3);
        Assert.Equal(0.5, mid0[1], 3);
        Assert.Equal(0.0, mid0[2], 3);

        double[] boundary = fn.Evaluate([0.5]); // pure green at the stitch boundary
        Assert.Equal(0.0, boundary[0], 3);
        Assert.Equal(1.0, boundary[1], 3);
        Assert.Equal(0.0, boundary[2], 3);
    }

    [Fact]
    public void InputOutsideDomain_IsClamped()
    {
        var fn = PdfFunction.Create(
            Stitch(new PdfArray(Type2([0], [1], 1), Type2([1], [0], 1)), [0.5], [0, 1, 0, 1]), null)!;

        Assert.Equal(0.0, fn.Evaluate([-5.0])[0], 3); // clamps to x = 0
        Assert.Equal(0.0, fn.Evaluate([5.0])[0], 3);  // clamps to x = 1
    }

    [Fact]
    public void EncodeReversesSubfunctionInput()
    {
        // Encode [1 0] on the (single) segment flips the input, so a 0->1 ramp evaluates reversed.
        var fn = PdfFunction.Create(
            Stitch(new PdfArray(Type2([0], [1], 1)), bounds: [], encode: [1, 0]), null)!;

        Assert.Equal(1.0, fn.Evaluate([0.0])[0], 3); // x=0 -> encoded 1 -> ramp = 1
        Assert.Equal(0.0, fn.Evaluate([1.0])[0], 3); // x=1 -> encoded 0 -> ramp = 0
    }

    [Fact]
    public void UnsupportedSubfunction_FailsToBuild()
    {
        // A Type 4 (PostScript) subfunction is not implemented; the whole stitch must fail to build
        // so the caller falls back cleanly instead of mis-evaluating part of the ramp.
        var type4 = new PdfDictionary();
        type4.Add(new PdfName("FunctionType"), new PdfInteger(4));
        type4.Add(new PdfName("Domain"), Reals(0, 1));

        PdfDictionary dict = Stitch(new PdfArray(Type2([0], [1], 1), type4), [0.5], [0, 1, 0, 1]);

        Assert.Null(PdfFunction.Create(dict, null));
    }
}
