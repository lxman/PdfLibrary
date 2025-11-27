using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Functions;

/// <summary>
/// Base class for PDF function objects (Type 0, 2, 3, 4).
/// Functions map input values to output values, used for tint transforms,
/// shading interpolation, and other color transformations.
/// </summary>
public abstract class PdfFunction
{
    /// <summary>
    /// Domain of the function - pairs of (min, max) for each input dimension.
    /// </summary>
    public double[] Domain { get; protected set; } = [];

    /// <summary>
    /// Range of the function - pairs of (min, max) for each output dimension.
    /// May be null if outputs are unbounded.
    /// </summary>
    public double[]? Range { get; protected set; }

    /// <summary>
    /// Number of input values.
    /// </summary>
    public int InputCount => Domain.Length / 2;

    /// <summary>
    /// Number of output values.
    /// </summary>
    public int OutputCount => Range?.Length / 2 ?? 0;

    /// <summary>
    /// Evaluates the function for the given input values.
    /// </summary>
    public abstract double[] Evaluate(double[] input);

    /// <summary>
    /// Creates a PDF function from a PDF object (stream or dictionary).
    /// </summary>
    public static PdfFunction? Create(PdfObject? obj, PdfDocument? document)
    {
        // Resolve indirect references
        if (obj is PdfIndirectReference reference && document is not null)
            obj = document.ResolveReference(reference);

        PdfDictionary? dict = obj switch
        {
            PdfStream stream => stream.Dictionary,
            PdfDictionary d => d,
            _ => null
        };

        if (dict is null)
            return null;

        // Get function type
        if (!dict.TryGetValue(new PdfName("FunctionType"), out PdfObject typeObj) || typeObj is not PdfInteger typeInt)
            return null;

        int functionType = typeInt.Value;

        // Parse Domain (required for all function types)
        double[] domain = ParseNumberArray(dict, "Domain") ?? [];
        double[]? range = ParseNumberArray(dict, "Range");

        return functionType switch
        {
            0 => SampledFunction.Create(obj as PdfStream, domain, range),
            2 => ExponentialFunction.Create(dict, domain, range),
            // Type 3 (stitching) and Type 4 (PostScript calculator) not yet implemented
            _ => null
        };
    }

    protected static double[]? ParseNumberArray(PdfDictionary dict, string key)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject arrayObj) || arrayObj is not PdfArray array)
            return null;

        var result = new double[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            result[i] = array[i] switch
            {
                PdfInteger intVal => intVal.Value,
                PdfReal realVal => realVal.Value,
                _ => 0.0
            };
        }
        return result;
    }

    protected static int[]? ParseIntArray(PdfDictionary dict, string key)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject arrayObj) || arrayObj is not PdfArray array)
            return null;

        var result = new int[array.Count];
        for (var i = 0; i < array.Count; i++)
        {
            result[i] = array[i] switch
            {
                PdfInteger intVal => intVal.Value,
                _ => 0
            };
        }
        return result;
    }

    /// <summary>
    /// Clamps a value to the specified range.
    /// </summary>
    protected static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    /// <summary>
    /// Linearly interpolates between two values.
    /// </summary>
    protected static double Interpolate(double x, double xMin, double xMax, double yMin, double yMax)
    {
        if (Math.Abs(xMax - xMin) < 1e-10)
            return yMin;
        return yMin + (x - xMin) * (yMax - yMin) / (xMax - xMin);
    }
}
