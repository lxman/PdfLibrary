using System.Collections.Generic;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// A Lab colour space's <c>/WhitePoint</c> (ISO 32000-1 §8.6.5.4) must be honoured as declared.
///
/// <para>
/// It previously was not. <c>ColorSpaceResolver.LabToRgb</c> "validated" the WhitePoint against a
/// D65-shaped window (<c>zn &gt;= 1.00</c>) and substituted D65 when it failed — which D50, at
/// <c>[0.9642, 1.0, 0.8249]</c>, always does. D50 is the ICC PCS white and the overwhelmingly common
/// choice in print-oriented PDFs, so in practice the declared white was discarded for the very case
/// that matters most.
/// </para>
///
/// <para>
/// The guard was correct when written: the original converter ended in a hard-coded D65 XYZ→sRGB
/// matrix with no chromatic adaptation, so a D50 input genuinely could not be handled. It became
/// stale when <c>LabToSrgb</c> replaced that with Bradford adaptation from an arbitrary reference
/// white, and was never revisited.
/// </para>
///
/// <para>
/// Neutrals are the reason this hid for so long: an error in the reference white cancels almost
/// exactly for a = b = 0, because the same (wrong) white is used to decode Lab and then to adapt back
/// to the PCS. Only chromatic colours diverge, worst on saturated blues.
/// </para>
/// </summary>
public class LabWhitePointTests
{
    private static readonly double[] D50 = [0.9642, 1.0, 0.8249];
    private static readonly double[] D65 = [0.95047, 1.0, 1.08883];

    /// <summary>Resources dictionary holding one named Lab space with the given white point.</summary>
    private static PdfDictionary Resources(double[]? whitePoint, string name = "Cs1")
    {
        var labDict = new PdfDictionary
        {
            [new PdfName("Range")] = new PdfArray(
                new PdfInteger(-128), new PdfInteger(127), new PdfInteger(-128), new PdfInteger(127)),
        };
        if (whitePoint is not null)
        {
            var wp = new PdfArray();
            foreach (double v in whitePoint) wp.Add(new PdfReal(v));
            labDict[new PdfName("WhitePoint")] = wp;
        }

        return new PdfDictionary
        {
            [new PdfName(name)] = new PdfArray(new PdfName("Lab"), labDict),
        };
    }

    /// <summary>Resolves one Lab triple through the public entry point, returning 8-bit sRGB.</summary>
    private static (int R, int G, int B) Resolve(double[]? whitePoint, double l, double a, double b)
    {
        var resolver = new ColorSpaceResolver(null);
        string? name = "Cs1";
        List<double>? color = [l, a, b];
        resolver.ResolveColorSpace(ref name, ref color, Resources(whitePoint));

        Assert.Equal("DeviceRGB", name);
        Assert.NotNull(color);
        Assert.True(color!.Count >= 3, $"expected 3 components, got {color.Count}");
        static int B8(double v) => (int)System.Math.Round(System.Math.Clamp(v, 0, 1) * 255);
        return (B8(color[0]), B8(color[1]), B8(color[2]));
    }

    [Fact]
    public void Declared_D50_white_point_is_honoured_rather_than_replaced_by_D65()
    {
        // A saturated blue is where the two illuminants diverge most. Before the fix both calls
        // returned the D65 answer, because D50 was silently swapped out — so they were identical.
        (int R, int G, int B) asD50 = Resolve(D50, 60, -10, -40);
        (int R, int G, int B) asD65 = Resolve(D65, 60, -10, -40);

        Assert.True(System.Math.Abs(asD50.R - asD65.R) > 15,
            $"D50 and D65 must not resolve alike: D50={asD50}, D65={asD65}");
    }

    [Fact]
    public void Neutral_lab_is_insensitive_to_the_white_point()
    {
        // Why this defect stayed hidden: for a = b = 0 the error cancels, so any grey-based
        // spot check looks perfect under either illuminant. Guard against a "fix" that breaks this.
        (int R, int G, int B) d50 = Resolve(D50, 50, 0, 0);
        (int R, int G, int B) d65 = Resolve(D65, 50, 0, 0);

        Assert.True(System.Math.Abs(d50.R - d65.R) <= 2, $"neutral drifted: D50={d50}, D65={d65}");
        Assert.True(System.Math.Abs(d50.R - d50.G) <= 2 && System.Math.Abs(d50.G - d50.B) <= 2,
            $"neutral Lab must stay neutral in sRGB, got {d50}");
    }

    [Theory]
    [InlineData(new double[] { 0, 0, 0 })]              // degenerate — would divide by zero downstream
    [InlineData(new double[] { -1, -1, -1 })]           // negative — physically meaningless
    [InlineData(new double[] { 0.9642, 0.0, 0.8249 })]  // Y = 0
    public void Unusable_white_points_fall_back_without_producing_garbage(double[] whitePoint)
    {
        (int R, int G, int B) c = Resolve(whitePoint, 50, 20, -30);

        Assert.InRange(c.R, 0, 255);
        Assert.InRange(c.G, 0, 255);
        Assert.InRange(c.B, 0, 255);
    }

    [Fact]
    public void Absent_white_point_still_resolves()
    {
        // /WhitePoint is required by the spec, but a malformed file may omit it; resolving must not
        // throw or emit NaN.
        (int R, int G, int B) c = Resolve(null, 50, 20, -30);

        Assert.InRange(c.R, 0, 255);
        Assert.InRange(c.G, 0, 255);
        Assert.InRange(c.B, 0, 255);
    }
}
