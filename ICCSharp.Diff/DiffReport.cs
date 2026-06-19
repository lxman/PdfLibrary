using System.Text;

namespace ICCSharp.Diff;

/// <summary>
/// Captures per-pixel deltas between ICCSharp output and lcms2 (Pillow) reference output.
/// Reports summary statistics: max, mean, and percentile errors per output channel.
/// </summary>
public sealed class DiffReport
{
    public string Label { get; }
    public int PixelCount { get; }
    public int OutputChannels { get; }

    /// <summary>Per-pixel max absolute difference across all output channels.</summary>
    public IReadOnlyList<double> PerPixelMaxDelta { get; }

    /// <summary>Worst pixel's max delta across all channels.</summary>
    public double GlobalMaxDelta { get; }

    /// <summary>Mean of <see cref="PerPixelMaxDelta"/>.</summary>
    public double MeanDelta { get; }

    /// <summary>Per-pixel input that produced the worst delta.</summary>
    public double[] WorstInput { get; }
    public double[] WorstIccSharp { get; }
    public double[] WorstReference { get; }

    public DiffReport(
        string label,
        int outputChannels,
        IReadOnlyList<double[]> inputs,
        IReadOnlyList<double[]> iccSharp,
        IReadOnlyList<double[]> reference)
    {
        if (inputs.Count != iccSharp.Count || inputs.Count != reference.Count)
            throw new ArgumentException("Mismatched pixel counts across ICCSharp / reference / inputs.");

        Label = label;
        PixelCount = inputs.Count;
        OutputChannels = outputChannels;

        var perPixel = new double[PixelCount];
        double globalMax = 0;
        var worstIdx = 0;

        for (var i = 0; i < PixelCount; i++)
        {
            double maxForPixel = 0;
            for (var c = 0; c < outputChannels; c++)
            {
                double diff = Math.Abs(iccSharp[i][c] - reference[i][c]);
                if (diff > maxForPixel) maxForPixel = diff;
            }
            perPixel[i] = maxForPixel;
            if (maxForPixel > globalMax)
            {
                globalMax = maxForPixel;
                worstIdx = i;
            }
        }

        PerPixelMaxDelta = perPixel;
        GlobalMaxDelta = globalMax;

        double sum = 0;
        for (var i = 0; i < PixelCount; i++) sum += perPixel[i];
        MeanDelta = PixelCount == 0 ? 0 : sum / PixelCount;

        WorstInput = inputs[worstIdx];
        WorstIccSharp = iccSharp[worstIdx];
        WorstReference = reference[worstIdx];
    }

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.AppendLine($"=== {Label} ({PixelCount} pixels, {OutputChannels} channels)");
        sb.AppendLine($"  max delta : {GlobalMaxDelta:F6}");
        sb.AppendLine($"  mean delta: {MeanDelta:F6}");
        sb.AppendLine($"  worst pixel:");
        sb.AppendLine($"    input    : [{string.Join(", ", FormatPixel(WorstInput))}]");
        sb.AppendLine($"    ICCSharp : [{string.Join(", ", FormatPixel(WorstIccSharp))}]");
        sb.AppendLine($"    lcms2    : [{string.Join(", ", FormatPixel(WorstReference))}]");
        return sb.ToString();
    }

    private static IEnumerable<string> FormatPixel(double[] p)
    {
        foreach (double v in p) yield return v.ToString("F4");
    }
}
