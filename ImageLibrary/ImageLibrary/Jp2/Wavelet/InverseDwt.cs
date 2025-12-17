using System;
using ImageLibrary.Jp2.Pipeline;

namespace ImageLibrary.Jp2.Wavelet;

/// <summary>
/// Inverse Discrete Wavelet Transform.
/// Reconstructs image from DWT subbands using 5/3 or 9/7 synthesis filters.
/// </summary>
internal class InverseDwt : IInverseDwt
{
    private readonly WaveletTransform _waveletType;

    // 5/3 filter coefficients (reversible)
    private const double Alpha_5_3 = -0.5;      // Lifting step 1
    private const double Beta_5_3 = 0.25;       // Lifting step 2

    // 9/7 filter coefficients (irreversible, CDF 9/7)
    private const double Alpha_9_7 = -1.586134342;   // Lifting step 1
    private const double Beta_9_7 = -0.05298011854;  // Lifting step 2
    private const double Gamma_9_7 = 0.8829110762;   // Lifting step 3
    private const double Delta_9_7 = 0.4435068522;   // Lifting step 4
    private const double K_9_7 = 1.230174105;        // Scaling factor (per JPEG2000/OpenJPEG)

    public InverseDwt(WaveletTransform waveletType)
    {
        _waveletType = waveletType;
    }

    public double[,] Process(DwtCoefficients input)
    {
        if (input.DecompositionLevels == 0)
        {
            // No wavelet transform - just return LL subband
            return (double[,])input.Subbands[0].Clone();
        }

        // Start with LL subband
        var result = (double[,])input.Subbands[0].Clone();

        // Reconstruct from lowest resolution to highest
        var subbandIdx = 1; // Skip LL (index 0)

        for (int level = input.DecompositionLevels; level >= 1; level--)
        {
            // Get detail subbands for this level
            double[,] hl = input.Subbands[subbandIdx++];
            double[,] lh = input.Subbands[subbandIdx++];
            double[,] hh = input.Subbands[subbandIdx++];

            // Combine LL + HL, LH, HH to get higher resolution
            result = Synthesize2D(result, hl, lh, hh);
        }

        return result;
    }

    /// <summary>
    /// Synthesizes one level of 2D DWT.
    /// </summary>
    private double[,] Synthesize2D(double[,] ll, double[,] hl, double[,] lh, double[,] hh)
    {
        int lowHeight = ll.GetLength(0);
        int lowWidth = ll.GetLength(1);
        int height = lowHeight + lh.GetLength(0);
        int width = lowWidth + hl.GetLength(1);

        // First, interleave horizontally: combine LL with HL, and LH with HH
        var lowBand = new double[lowHeight, width];
        var highBand = new double[lh.GetLength(0), width];

        // Combine LL and HL row-wise
        for (var y = 0; y < lowHeight; y++)
        {
            double[] row = InterleaveAndSynthesize1D(
                GetRow(ll, y),
                GetRow(hl, y));
            for (var x = 0; x < width; x++)
            {
                lowBand[y, x] = row[x];
            }
        }

        // Combine LH and HH row-wise
        for (var y = 0; y < lh.GetLength(0); y++)
        {
            double[] row = InterleaveAndSynthesize1D(
                GetRow(lh, y),
                GetRow(hh, y));
            for (var x = 0; x < width; x++)
            {
                highBand[y, x] = row[x];
            }
        }

        // Then, interleave vertically: combine lowBand with highBand
        var result = new double[height, width];

        for (var x = 0; x < width; x++)
        {
            double[] col = InterleaveAndSynthesize1D(
                GetColumn(lowBand, x),
                GetColumn(highBand, x));
            for (var y = 0; y < height; y++)
            {
                result[y, x] = col[y];
            }
        }

        return result;
    }

    /// <summary>
    /// Interleaves low and high pass signals and applies inverse filter.
    /// </summary>
    /// <param name="low">Low-pass samples</param>
    /// <param name="high">High-pass samples</param>
    /// <param name="phase">Starting phase: 0 if first sample is even (low-pass), 1 if first sample is odd (high-pass)</param>
    private double[] InterleaveAndSynthesize1D(double[] low, double[] high, int phase = 0)
    {
        int n = low.Length + high.Length;
        var result = new double[n];

        // Determine phase from sample counts if not explicitly specified
        // If high.Length > low.Length, then phase must be 1 (starts with odd/high-pass)
        if (high.Length > low.Length)
        {
            phase = 1;
        }

        if (phase == 0)
        {
            // Phase 0: Even indices from low, odd indices from high
            // Sample 0 is even (low-pass)
            for (var i = 0; i < low.Length; i++)
            {
                result[i * 2] = low[i];
            }
            for (var i = 0; i < high.Length; i++)
            {
                result[i * 2 + 1] = high[i];
            }
        }
        else
        {
            // Phase 1: Odd indices from low, even indices from high
            // Sample 0 is odd (high-pass)
            for (var i = 0; i < high.Length; i++)
            {
                result[i * 2] = high[i];
            }
            for (var i = 0; i < low.Length; i++)
            {
                result[i * 2 + 1] = low[i];
            }
        }

        // Apply inverse lifting
        if (_waveletType == WaveletTransform.Reversible_5_3)
        {
            InverseLifting_5_3(result, phase);
        }
        else
        {
            InverseLifting_9_7(result, phase);
        }

        return result;
    }

    /// <summary>
    /// Applies inverse 5/3 lifting transform.
    /// </summary>
    /// <param name="x">The interleaved signal</param>
    /// <param name="phase">0 if even indices are low-pass, 1 if even indices are high-pass</param>
    private void InverseLifting_5_3(double[] x, int phase = 0)
    {
        int n = x.Length;
        if (n < 2) return;

        // Determine which indices are low-pass (s) and high-pass (d)
        // Phase 0: s at even indices (0,2,4...), d at odd indices (1,3,5...)
        // Phase 1: d at even indices (0,2,4...), s at odd indices (1,3,5...)
        int sStart = phase;       // Low-pass start index
        int dStart = 1 - phase;   // High-pass start index

        // Step 2 (inverse): Update low-pass samples (s)
        // s[i] = s[i] - floor((d[i-1] + d[i] + 2) / 4)
        for (int i = sStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : x[dStart];  // Mirror at boundary
            double right = (i < n - 1) ? x[i + 1] : x[n - 2 + (n % 2 == 0 ? dStart : sStart)];
            x[i] = x[i] - Math.Floor((left + right + 2) / 4.0);
        }

        // Step 1 (inverse): Update high-pass samples (d)
        // d[i] = d[i] + floor((s[i] + s[i+1]) / 2)
        for (int i = dStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : x[sStart];
            double right = (i < n - 1) ? x[i + 1] : x[i - 1];
            x[i] = x[i] + Math.Floor((left + right) / 2.0);
        }
    }

    /// <summary>
    /// Applies inverse 9/7 lifting transform.
    /// </summary>
    /// <param name="x">The interleaved signal</param>
    /// <param name="phase">0 if even indices are low-pass, 1 if even indices are high-pass</param>
    private void InverseLifting_9_7(double[] x, int phase = 0)
    {
        int n = x.Length;
        if (n < 2) return;

        int sStart = phase;       // Low-pass start index
        int dStart = 1 - phase;   // High-pass start index

        // Undo scaling: low-pass * K, high-pass / K
        // Forward transform scales: low *= 1/K, high *= K
        // Inverse must undo: low *= K, high *= 1/K
        for (int i = sStart; i < n; i += 2)
        {
            x[i] = x[i] * K_9_7;
        }
        for (int i = dStart; i < n; i += 2)
        {
            x[i] = x[i] / K_9_7;
        }

        // Step 4 (inverse): Update low-pass samples
        for (int i = sStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : x[dStart];
            double right = (i < n - 1) ? x[i + 1] : x[n - 2 + (n % 2 == 0 ? dStart : sStart)];
            x[i] = x[i] - Delta_9_7 * (left + right);
        }

        // Step 3 (inverse): Update high-pass samples
        for (int i = dStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : x[sStart];
            double right = (i < n - 1) ? x[i + 1] : x[i - 1];
            x[i] = x[i] - Gamma_9_7 * (left + right);
        }

        // Step 2 (inverse): Update low-pass samples
        for (int i = sStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : x[dStart];
            double right = (i < n - 1) ? x[i + 1] : x[n - 2 + (n % 2 == 0 ? dStart : sStart)];
            x[i] = x[i] - Beta_9_7 * (left + right);
        }

        // Step 1 (inverse): Update high-pass samples
        for (int i = dStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : x[sStart];
            double right = (i < n - 1) ? x[i + 1] : x[i - 1];
            x[i] = x[i] - Alpha_9_7 * (left + right);
        }
    }

    private static double[] GetRow(double[,] array, int row)
    {
        int width = array.GetLength(1);
        var result = new double[width];
        for (var x = 0; x < width; x++)
        {
            result[x] = array[row, x];
        }
        return result;
    }

    private static double[] GetColumn(double[,] array, int col)
    {
        int height = array.GetLength(0);
        var result = new double[height];
        for (var y = 0; y < height; y++)
        {
            result[y] = array[y, col];
        }
        return result;
    }
}