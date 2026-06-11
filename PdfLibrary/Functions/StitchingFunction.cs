using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Functions;

/// <summary>
/// Type 3 (Stitching) function. Defines a single-input function by stitching together k subfunctions,
/// each mapped onto a subinterval of the domain delimited by /Bounds. /Encode re-maps each subinterval
/// onto its subfunction's own domain before evaluation. This is the usual shape of a gradient colour
/// ramp — a stitch of type 2 (exponential) segments. ISO 32000-1 §7.10.4.
/// </summary>
internal class StitchingFunction : PdfFunction
{
    private readonly PdfFunction[] _functions;
    private readonly double[] _bounds;   // k-1 increasing interior boundaries
    private readonly double[] _encode;   // 2k values: [lo0, hi0, lo1, hi1, ...]

    private StitchingFunction(double[] domain, double[]? range, PdfFunction[] functions, double[] bounds, double[] encode)
    {
        Domain = domain;
        Range = range;
        _functions = functions;
        _bounds = bounds;
        _encode = encode;
    }

    public static StitchingFunction? Create(PdfDictionary dict, double[] domain, double[]? range, PdfDocument? document)
    {
        // A stitching function takes exactly one input.
        if (domain.Length < 2) return null;

        if (!dict.TryGetValue(new PdfName("Functions"), out PdfObject? functionsObj)) return null;
        if (functionsObj is PdfIndirectReference fref && document is not null)
            functionsObj = document.ResolveReference(fref);
        if (functionsObj is not PdfArray functionsArray || functionsArray.Count == 0) return null;

        int k = functionsArray.Count;
        var functions = new PdfFunction[k];
        for (var i = 0; i < k; i++)
        {
            // Subfunctions may be any type; if one is unsupported (e.g. type 4), skip the whole
            // stitch cleanly so the caller falls back rather than mis-evaluating part of the ramp.
            PdfFunction? f = Create(functionsArray[i], document);
            if (f is null) return null;
            functions[i] = f;
        }

        double[] bounds = ParseNumberArray(dict, "Bounds") ?? [];
        double[] encode = ParseNumberArray(dict, "Encode") ?? [];

        // Spec: Bounds has k-1 entries; Encode has 2k entries.
        if (bounds.Length != k - 1 || encode.Length != 2 * k) return null;

        return new StitchingFunction(domain, range, functions, bounds, encode);
    }

    public override double[] Evaluate(double[] input)
    {
        if (input.Length == 0) return [];

        double x = Clamp(input[0], Domain[0], Domain[1]);

        // Locate the subinterval: the first i with x < Bounds[i], else the last subinterval.
        var i = 0;
        while (i < _functions.Length - 1 && x >= _bounds[i])
            i++;

        // Subinterval domain [lo, hi).
        double lo = i == 0 ? Domain[0] : _bounds[i - 1];
        double hi = i == _functions.Length - 1 ? Domain[1] : _bounds[i];

        // Re-map x onto the subfunction's own input domain via Encode.
        double encoded = Interpolate(x, lo, hi, _encode[2 * i], _encode[2 * i + 1]);

        double[] result = _functions[i].Evaluate([encoded]);

        // Clamp outputs to Range if specified.
        if (Range is not null)
        {
            for (var j = 0; j < result.Length && j * 2 + 1 < Range.Length; j++)
                result[j] = Clamp(result[j], Range[j * 2], Range[j * 2 + 1]);
        }

        return result;
    }
}
