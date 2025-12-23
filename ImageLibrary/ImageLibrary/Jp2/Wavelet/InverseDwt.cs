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
            double[,] hl = input.Subbands[subbandIdx];
            double[,] lh = input.Subbands[subbandIdx + 1];
            double[,] hh = input.Subbands[subbandIdx + 2];

            // Get coordinates for phase detection
            int hlX0 = input.SubbandX0[subbandIdx];
            int hlY0 = input.SubbandY0[subbandIdx];
            int lhX0 = input.SubbandX0[subbandIdx + 1];
            int lhY0 = input.SubbandY0[subbandIdx + 1];
            subbandIdx += 3;

            // Combine LL + HL, LH, HH to get higher resolution
            result = Synthesize2D(result, hl, lh, hh, hlX0, hlY0, lhX0, lhY0);
        }

        return result;
    }

    /// <summary>
    /// Synthesizes one level of 2D DWT.
    /// </summary>
    /// <param name="ll">LL subband (low-low approximation)</param>
    /// <param name="hl">HL subband (horizontal detail)</param>
    /// <param name="lh">LH subband (vertical detail)</param>
    /// <param name="hh">HH subband (diagonal detail)</param>
    /// <param name="hlX0">X0 coordinate of HL subband in tile component reference grid</param>
    /// <param name="hlY0">Y0 coordinate of HL subband in tile component reference grid</param>
    /// <param name="lhX0">X0 coordinate of LH subband in tile component reference grid</param>
    /// <param name="lhY0">Y0 coordinate of LH subband in tile component reference grid</param>
    private double[,] Synthesize2D(double[,] ll, double[,] hl, double[,] lh, double[,] hh,
        int hlX0, int hlY0, int lhX0, int lhY0)
    {
        Console.WriteLine($"[IDWT-START] Synthesize2D called with HL({hlX0},{hlY0}) LH({lhX0},{lhY0})");
        int lowHeight = ll.GetLength(0);
        int lowWidth = ll.GetLength(1);
        int height = lowHeight + lh.GetLength(0);
        int width = lowWidth + hl.GetLength(1);

        // CRITICAL: Per ITU-T T.800 and OpenJPEG reference implementation:
        // Forward DWT: Vertical → Horizontal
        // Inverse DWT: Horizontal → Vertical (reversed order)
        //
        // First, interleave horizontally: combine LL with HL (both low-pass vertical),
        // and LH with HH (both high-pass vertical)
        var lowBand = new double[lowHeight, width];
        var highBand = new double[lh.GetLength(0), width];

        // Per OpenJPEG (dwt.c:2117,2168): phase is determined by subband starting coordinate modulo 2
        // h.cas = tr->x0 % 2 (horizontal interleaving uses X coordinate)
        // v.cas = tr->y0 % 2 (vertical interleaving uses Y coordinate)
        //
        // For horizontal synthesis (producing rows):
        //   - LL+HL: HL starts at x0=hlX0 (should be 0), phase = hlX0 % 2 = 0
        //   - LH+HH: LH starts at x0=lhX0 (should be lowWidth), phase = lhX0 % 2
        // For vertical synthesis (producing columns):
        //   - lowBand+highBand: highBand starts at y0=lhY0 (should be lowHeight), phase = lhY0 % 2
        int horizontalPhaseLeft = hlX0 % 2;
        int horizontalPhaseRight = lhX0 % 2;
        int verticalPhase = lhY0 % 2;

        Console.WriteLine($"[IDWT] Synthesize2D: HL({hlX0},{hlY0}) LH({lhX0},{lhY0}) → phases H-Left={horizontalPhaseLeft}, H-Right={horizontalPhaseRight}, V={verticalPhase}");

        // Combine LL and HL row-wise (both share low-pass vertical filter)
        for (var y = 0; y < lowHeight; y++)
        {
            double[] row = InterleaveAndSynthesize1D(
                GetRow(ll, y),
                GetRow(hl, y),
                horizontalPhaseLeft);
            for (var x = 0; x < width; x++)
            {
                lowBand[y, x] = row[x];
            }
        }

        // Combine LH and HH row-wise (both share high-pass vertical filter)
        for (var y = 0; y < lh.GetLength(0); y++)
        {
            double[] row = InterleaveAndSynthesize1D(
                GetRow(lh, y),
                GetRow(hh, y),
                horizontalPhaseRight);
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
                GetColumn(highBand, x),
                verticalPhase);
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
    /// <param name="phase">Starting phase: 0 if first sample is even (low-pass), 1 if first sample is odd (high-pass).
    /// This should be determined by subband starting coordinate modulo 2 per OpenJPEG.</param>
    private double[] InterleaveAndSynthesize1D(double[] low, double[] high, int phase)
    {
        int n = low.Length + high.Length;
        var result = new double[n];

        if (phase == 0)
        {
            // Phase 0: Even indices from low, odd indices from high
            // Sample 0 is even (low-pass)
            int lowIdx = 0, highIdx = 0;
            for (int i = 0; i < n; i++)
            {
                if (i % 2 == 0)
                {
                    // Even position: low-pass
                    result[i] = (lowIdx < low.Length) ? low[lowIdx++] : 0;
                }
                else
                {
                    // Odd position: high-pass
                    result[i] = (highIdx < high.Length) ? high[highIdx++] : 0;
                }
            }
        }
        else
        {
            // Phase 1: Even indices from high, odd indices from low
            // Sample 0 is even (high-pass)
            int lowIdx = 0, highIdx = 0;
            for (int i = 0; i < n; i++)
            {
                if (i % 2 == 0)
                {
                    // Even position: high-pass
                    result[i] = (highIdx < high.Length) ? high[highIdx++] : 0;
                }
                else
                {
                    // Odd position: low-pass
                    result[i] = (lowIdx < low.Length) ? low[lowIdx++] : 0;
                }
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
        // Use symmetric boundary extension for consistency
        for (int i = sStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : ((dStart < n) ? x[dStart] : 0);
            double right = (i < n - 1) ? x[i + 1] : ((i > 0) ? x[i - 1] : 0);
            x[i] = x[i] - Delta_9_7 * (left + right);
        }

        // Step 3 (inverse): Update high-pass samples
        // Use symmetric boundary extension for consistency
        for (int i = dStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : ((sStart < n) ? x[sStart] : 0);
            double right = (i < n - 1) ? x[i + 1] : ((i > 0) ? x[i - 1] : 0);
            x[i] = x[i] - Gamma_9_7 * (left + right);
        }

        // Step 2 (inverse): Update low-pass samples
        // Use symmetric boundary extension for consistency
        for (int i = sStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : ((dStart < n) ? x[dStart] : 0);
            double right = (i < n - 1) ? x[i + 1] : ((i > 0) ? x[i - 1] : 0);
            x[i] = x[i] - Beta_9_7 * (left + right);
        }

        // Step 1 (inverse): Update high-pass samples
        // Use symmetric boundary extension for consistency
        for (int i = dStart; i < n; i += 2)
        {
            double left = (i > 0) ? x[i - 1] : ((sStart < n) ? x[sStart] : 0);
            double right = (i < n - 1) ? x[i + 1] : ((i > 0) ? x[i - 1] : 0);
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