using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Functions;

/// <summary>
/// Type 2 (Exponential interpolation) function.
/// Computes: y_j = C0_j + x^N * (C1_j - C0_j)
/// </summary>
internal class ExponentialFunction : PdfFunction
{
    private readonly double[] _c0;
    private readonly double[] _c1;
    private readonly double _n;

    private ExponentialFunction(double[] domain, double[]? range, double[] c0, double[] c1, double n)
    {
        Domain = domain;
        Range = range;
        _c0 = c0;
        _c1 = c1;
        _n = n;
    }

    public static ExponentialFunction? Create(PdfDictionary dict, double[] domain, double[]? range)
    {
        // N (required) - interpolation exponent
        if (!dict.TryGetValue(new PdfName("N"), out PdfObject nObj))
            return null;

        double n = nObj switch
        {
            PdfInteger intVal => intVal.Value,
            PdfReal realVal => realVal.Value,
            _ => 1.0
        };

        // Determine output count from Range or C0/C1
        int outputCount = range?.Length / 2 ?? 0;

        // C0 (optional) - output values at x=0, default [0.0]
        double[] c0 = ParseNumberArray(dict, "C0") ?? (outputCount > 0 ? new double[outputCount] : [0.0]);

        if (outputCount == 0)
            outputCount = c0.Length;

        // C1 (optional) - output values at x=1, default [1.0]
        double[]? c1 = ParseNumberArray(dict, "C1");
        if (c1 is not null) return new ExponentialFunction(domain, range, c0, c1, n);
        c1 = new double[outputCount];
        for (var i = 0; i < outputCount; i++)
            c1[i] = 1.0;

        return new ExponentialFunction(domain, range, c0, c1, n);
    }

    public override double[] Evaluate(double[] input)
    {
        if (input.Length == 0)
            return [];

        // Clamp input to domain (Type 2 functions are single-input)
        double x = Clamp(input[0], Domain[0], Domain[1]);

        // Compute x^N
        double xn = Math.Pow(x, _n);

        // Compute outputs: y_j = C0_j + x^N * (C1_j - C0_j)
        int outputCount = _c0.Length;
        var result = new double[outputCount];

        for (var j = 0; j < outputCount; j++)
        {
            double value = _c0[j] + xn * (_c1[j] - _c0[j]);

            // Clamp to range if specified
            if (Range is not null && j * 2 + 1 < Range.Length)
            {
                value = Clamp(value, Range[j * 2], Range[j * 2 + 1]);
            }

            result[j] = value;
        }

        return result;
    }
}
