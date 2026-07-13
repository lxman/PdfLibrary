using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class TintRampTests
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

    // Type 2 (Exponential) functions are single-input by spec (ISO 32000-1 §7.10.3) — Evaluate always
    // reads input[0] regardless of the array length passed in, so they cannot model a genuine N-input
    // DeviceN tint transform. Real DeviceN tint transforms use Type 0 (Sampled) or Type 4 (PostScript
    // calculator) functions; a Type 4 program is the simplest to hand-construct for a test.
    private static PdfStream Type4(string program, double[] domain, double[] range)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(4));
        d.Add(new PdfName("Domain"), Reals(domain));
        d.Add(new PdfName("Range"), Reals(range));
        return new PdfStream(d, Encoding.ASCII.GetBytes(program));
    }

    [Fact]
    public void LinearSeparation_ProducesMonotonic256EntryRamp()
    {
        // [/Separation /X /DeviceCMYK {t -> [0 t 0 0]}] — magenta ramps 0→1 linearly.
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("X"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 0, 0]));

        (double[][]? ramp, (byte R, byte G, byte B) solid) = ColorSpaceResolver.BuildTintRamp(sep, null, 0, 1);

        Assert.NotNull(ramp);
        Assert.Equal(256, ramp!.Length);
        Assert.Equal(0.0, ramp[0][1], 3);        // tint 0 → M = 0
        Assert.Equal(1.0, ramp[255][1], 3);       // tint 1 → M = 1
        Assert.True(ramp[128][1] > ramp[0][1] && ramp[255][1] > ramp[128][1]); // monotonic in M
        // Solid (tint=1) is full magenta in DeviceCMYK → red-ish sRGB (R high, G/B low).
        Assert.True(solid.R > 200 && solid.G < 120);
    }

    [Fact]
    public void DeviceN_RampSweepsOnlyTheGivenColorant()
    {
        // [/DeviceN [/A /B] /DeviceCMYK {[a b] -> [a b 0 0]}] — ramp for index 1 sweeps b, holds a=0.
        // A genuine 2-input Type 4 (PostScript calculator) function: inputs a, b are pre-pushed onto
        // the stack, then "0 0" pushes two more zeros, leaving [a b 0 0] for the four outputs.
        var deviceN = new PdfArray(
            new PdfName("DeviceN"),
            new PdfArray(new PdfName("A"), new PdfName("B")),
            new PdfName("DeviceCMYK"),
            Type4("{ 0 0 }", [0, 1, 0, 1], [0, 1, 0, 1, 0, 1, 0, 1]));

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(deviceN, null, 1, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.0, ramp![255][0], 3);      // C (colorant A, held 0) stays 0
        Assert.Equal(1.0, ramp[255][1], 3);       // M (colorant B, swept) reaches 1
    }

    [Fact]
    public void NoTintTransform_ProducesNullRamp()
    {
        // A malformed array with a non-function in the tint-transform slot.
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("X"), new PdfName("DeviceCMYK"), new PdfName("NotAFunction"));

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(sep, null, 0, 1);

        Assert.Null(ramp);
    }

    // A Type 0 (Sampled) function whose /Domain declares 2 inputs, wired up as the tint transform of a
    // Separation (which by ISO 32000-1 §8.6.6.4 is always single-input). PdfFunction.Create succeeds —
    // nothing at creation time cross-checks Domain against the colour space's actual input count — but
    // SampledFunction.Evaluate reads Domain.Length/2 = 2 input slots from the caller-supplied array, which
    // BuildTintRamp sizes to the Separation's real input count (1). Reading the second slot throws
    // IndexOutOfRangeException. This models a malformed/authoring-mismatched tint-transform body: the
    // "createable but throws on Evaluate" case Fix 1 guards against.
    private static PdfStream MismatchedDomainSampledFunction()
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(0));
        d.Add(new PdfName("Domain"), Reals(0, 1, 0, 1)); // declares 2 inputs
        d.Add(new PdfName("Range"), Reals(0, 1));
        d.Add(new PdfName("Size"), new PdfArray(new PdfInteger(2), new PdfInteger(2)));
        d.Add(new PdfName("BitsPerSample"), new PdfInteger(8));
        return new PdfStream(d, new byte[4]); // 2*2 samples * 1 output * 1 byte/sample
    }

    [Fact]
    public void ThrowingTintTransform_ProducesNullRampInsteadOfThrowing()
    {
        // [/Separation /X /DeviceCMYK <2-input sampled fn>] — single-tint Separation, but the tint
        // transform's own Domain mismatches (declares 2 inputs). Evaluate throws when BuildTintRamp
        // (correctly, per the 1-input Separation) calls it with a 1-element array.
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("X"), new PdfName("DeviceCMYK"),
            MismatchedDomainSampledFunction());

        (double[][]? ramp, (byte R, byte G, byte B) solid) = ColorSpaceResolver.BuildTintRamp(sep, null, 0, 1);

        Assert.Null(ramp);
        Assert.Equal((0, 0, 0), solid);
    }
}
