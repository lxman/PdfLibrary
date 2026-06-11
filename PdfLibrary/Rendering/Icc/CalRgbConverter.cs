using ICCSharp.Eval;
using ICCSharp.IO;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Converts PDF CalRGB (calibrated RGB, ISO 32000-1 §8.6.5.3) to device sRGB. CalRGB applies a
/// per-channel decoding gamma and a 3×3 matrix mapping the decoded components to CIE XYZ relative
/// to the space's WhitePoint; the result is chromatically adapted to D50 (the sRGB PCS white) and
/// converted to sRGB through the same XYZ→sRGB path as <see cref="LabToSrgb"/>.
///
/// Wide-gamut CalRGB spaces (the typical use) make this visibly different from treating the
/// components as raw DeviceRGB — saturated primaries land on the correct, more-saturated colours
/// rather than the muted DeviceRGB interpretation.
/// </summary>
internal sealed class CalRgbConverter
{
    private readonly double _gammaR, _gammaG, _gammaB;
    private readonly Matrix3x3 _toXyz;          // (A^g, B^g, C^g) → XYZ (relative to WhitePoint)
    private readonly Matrix3x3? _adaptToD50;    // null when WhitePoint is already D50

    internal CalRgbConverter(double[] whitePoint, double[] gamma, double[] matrix)
    {
        _gammaR = gamma[0];
        _gammaG = gamma[1];
        _gammaB = gamma[2];

        // PDF /Matrix is [XA YA ZA  XB YB ZB  XC YC ZC]: each column is a primary's XYZ.
        _toXyz = new Matrix3x3(
            matrix[0], matrix[3], matrix[6],
            matrix[1], matrix[4], matrix[7],
            matrix[2], matrix[5], matrix[8]);

        var wp = new XyzNumber(whitePoint[0], whitePoint[1], whitePoint[2]);
        _adaptToD50 = LabToSrgb.AreClose(wp, StandardIlluminants.D50)
            ? null
            : ChromaticAdaptation.ComputeMatrix(wp, StandardIlluminants.D50);
    }

    /// <summary>Converts a CalRGB triple (each in [0, 1]) to sRGB in [0, 1].</summary>
    public double[] ToSrgb(double a, double b, double c)
    {
        double ag = SafePow(a, _gammaR);
        double bg = SafePow(b, _gammaG);
        double cg = SafePow(c, _gammaB);

        (double x, double y, double z) = _toXyz.Transform(ag, bg, cg);
        if (_adaptToD50 is { } adapt)
            (x, y, z) = adapt.Transform(x, y, z);

        return LabToSrgb.D50XyzToSrgb(new XyzNumber(x, y, z));
    }

    /// <summary>
    /// Builds a converter from a <c>[/CalRGB &lt;&lt; … &gt;&gt;]</c> color-space array. Missing
    /// Gamma defaults to [1,1,1], missing Matrix to identity, missing WhitePoint to D50. Returns
    /// null when the array is not a usable CalRGB definition (caller falls back to DeviceRGB).
    /// </summary>
    public static CalRgbConverter? FromCalRgbArray(PdfArray? csArray, PdfDocument? document)
    {
        if (csArray is not { Count: >= 2 } || csArray[0] is not PdfName { Value: "CalRGB" })
            return null;

        PdfObject? dictObj = csArray[1];
        if (dictObj is PdfIndirectReference r && document is not null)
            dictObj = document.ResolveReference(r);
        if (dictObj is not PdfDictionary dict)
            return null;

        double[] whitePoint = ReadNumbers(dict, "WhitePoint", 3) ?? [0.9642, 1.0, 0.8249];
        double[] gamma = ReadNumbers(dict, "Gamma", 3) ?? [1.0, 1.0, 1.0];
        double[] matrix = ReadNumbers(dict, "Matrix", 9) ?? [1, 0, 0, 0, 1, 0, 0, 0, 1];

        return new CalRgbConverter(whitePoint, gamma, matrix);
    }

    private static double SafePow(double v, double g)
        => v <= 0.0 ? 0.0 : (g == 1.0 ? v : Math.Pow(v, g));

    private static double[]? ReadNumbers(PdfDictionary dict, string key, int count)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject? obj) || obj is not PdfArray arr || arr.Count < count)
            return null;
        var nums = new double[count];
        for (var i = 0; i < count; i++)
        {
            nums[i] = arr[i] switch
            {
                PdfInteger n => n.Value,
                PdfReal x => x.Value,
                _ => double.NaN
            };
            if (double.IsNaN(nums[i])) return null;
        }
        return nums;
    }
}

/// <summary>
/// Converts PDF CalGray (calibrated grayscale, ISO 32000-1 §8.6.5.2) to device sRGB: a single
/// gray component, decoded by a gamma, scaled along the WhitePoint's neutral axis to XYZ, then
/// adapted to D50 and converted to sRGB. The result is always neutral.
/// </summary>
internal sealed class CalGrayConverter
{
    private readonly double _gamma;
    private readonly XyzNumber _whitePoint;
    private readonly Matrix3x3? _adaptToD50;

    internal CalGrayConverter(double[] whitePoint, double gamma)
    {
        _gamma = gamma;
        _whitePoint = new XyzNumber(whitePoint[0], whitePoint[1], whitePoint[2]);
        _adaptToD50 = LabToSrgb.AreClose(_whitePoint, StandardIlluminants.D50)
            ? null
            : ChromaticAdaptation.ComputeMatrix(_whitePoint, StandardIlluminants.D50);
    }

    /// <summary>Converts a CalGray value (in [0, 1]) to sRGB in [0, 1].</summary>
    public double[] ToSrgb(double gray)
    {
        double a = gray <= 0.0 ? 0.0 : (_gamma == 1.0 ? gray : Math.Pow(gray, _gamma));
        double x = _whitePoint.X * a;
        double y = _whitePoint.Y * a;
        double z = _whitePoint.Z * a;
        if (_adaptToD50 is { } adapt)
            (x, y, z) = adapt.Transform(x, y, z);
        return LabToSrgb.D50XyzToSrgb(new XyzNumber(x, y, z));
    }

    /// <summary>
    /// Builds a converter from a <c>[/CalGray &lt;&lt; … &gt;&gt;]</c> color-space array. Missing
    /// Gamma defaults to 1, missing WhitePoint to D50. Returns null when the array is not a usable
    /// CalGray definition (caller falls back to DeviceGray).
    /// </summary>
    public static CalGrayConverter? FromCalGrayArray(PdfArray? csArray, PdfDocument? document)
    {
        if (csArray is not { Count: >= 2 } || csArray[0] is not PdfName { Value: "CalGray" })
            return null;

        PdfObject? dictObj = csArray[1];
        if (dictObj is PdfIndirectReference r && document is not null)
            dictObj = document.ResolveReference(r);
        if (dictObj is not PdfDictionary dict)
            return null;

        double[] whitePoint = ReadNumbers(dict, "WhitePoint", 3) ?? [0.9642, 1.0, 0.8249];
        double gamma = ReadNumber(dict, "Gamma") ?? 1.0;

        return new CalGrayConverter(whitePoint, gamma);
    }

    private static double? ReadNumber(PdfDictionary dict, string key)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject? obj)) return null;
        return obj switch
        {
            PdfInteger n => n.Value,
            PdfReal x => x.Value,
            _ => null
        };
    }

    private static double[]? ReadNumbers(PdfDictionary dict, string key, int count)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject? obj) || obj is not PdfArray arr || arr.Count < count)
            return null;
        var nums = new double[count];
        for (var i = 0; i < count; i++)
        {
            nums[i] = arr[i] switch
            {
                PdfInteger n => n.Value,
                PdfReal x => x.Value,
                _ => double.NaN
            };
            if (double.IsNaN(nums[i])) return null;
        }
        return nums;
    }
}
