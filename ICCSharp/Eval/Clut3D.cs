using System;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Three-input-channel CLUT with tetrahedral interpolation. Tetrahedral partitions the unit
/// cube into six congruent tetrahedra (one per ordering of the three fractional coordinates)
/// and linearly interpolates within whichever one contains the query point. Versus trilinear
/// it preserves the diagonal of the cube exactly (important for neutral RGB → neutral PCS)
/// and is the lcms2 default for 3-D LUTs.
///
/// Construction reads from a parsed <see cref="LutClutData"/> with exactly three grid dimensions.
/// </summary>
public sealed class Clut3D : IClut
{
    private readonly int _gR, _gG, _gB;       // grid points per input channel
    private readonly int _output;             // output channels per grid point
    private readonly double[] _values;        // flat raster, normalized to [0,1]

    public int InputChannels => 3;
    /// <summary>Output channel count.</summary>
    public int OutputChannels => _output;

    /// <summary>Grid point counts (r, g, b).</summary>
    public (int R, int G, int B) GridPoints => (_gR, _gG, _gB);

    void IClut.Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length != 3)
            throw new ArgumentException($"Clut3D expects 3 inputs; got {input.Length}.", nameof(input));
        Apply(input[0], input[1], input[2], output);
    }

    public Clut3D(LutClutData clut)
    {
        if (clut is null) throw new ArgumentNullException(nameof(clut));
        if (clut.GridPoints.Count != 3)
            throw new ArgumentException(
                $"Clut3D requires 3 input dimensions; CLUT has {clut.GridPoints.Count}.", nameof(clut));

        _gR = clut.GridPoints[0];
        _gG = clut.GridPoints[1];
        _gB = clut.GridPoints[2];
        _output = clut.OutputChannels;

        // Each axis needs ≥ 2 grid points: interpolation reads floor index + 1, and a single-sample
        // axis would produce a negative neighbour offset. (ClutND enforces the same lower bound.)
        if (_gR < 2 || _gG < 2 || _gB < 2)
            throw new ArgumentException(
                $"Clut3D requires ≥ 2 grid points per axis; got {_gR}×{_gG}×{_gB}.", nameof(clut));

        int expected = _gR * _gG * _gB * _output;
        if (clut.Values.Count != expected)
            throw new ArgumentException(
                $"CLUT value count {clut.Values.Count} does not match grid {_gR}×{_gG}×{_gB}×{_output} = {expected}.",
                nameof(clut));

        _values = new double[expected];
        for (int i = 0; i < expected; i++) _values[i] = clut.Values[i];
    }

    /// <summary>
    /// Tetrahedral interpolation at (r, g, b) where each input is in [0, 1]. Allocates a fresh
    /// output array of length <see cref="OutputChannels"/>.
    /// </summary>
    public double[] Apply(double r, double g, double b)
    {
        double[] result = new double[_output];
        Apply(r, g, b, result);
        return result;
    }

    /// <summary>Zero-allocation overload — caller-supplied output buffer.</summary>
    public void Apply(double r, double g, double b, Span<double> output)
    {
        if (output.Length < _output)
            throw new ArgumentException(
                $"Output buffer too short: need {_output} elements, got {output.Length}.", nameof(output));

        (int i0, double dr) = LocateIndex(r, _gR);
        (int j0, double dg) = LocateIndex(g, _gG);
        (int k0, double db) = LocateIndex(b, _gB);

        // Stride between adjacent grid points along each axis (in flat array units of `output` size).
        int stride2 = _output;
        int stride1 = _gB * _output;
        int stride0 = _gG * _gB * _output;

        int v000 = ((i0 * _gG + j0) * _gB + k0) * _output;
        int v100 = v000 + stride0;
        int v010 = v000 + stride1;
        int v001 = v000 + stride2;
        int v110 = v100 + stride1;
        int v101 = v100 + stride2;
        int v011 = v010 + stride2;
        int v111 = v110 + stride2;

        // Pick the tetrahedron based on ordering of (dr, dg, db). The six branches each blend
        // four vertices; weights sum to 1 in every branch.
        for (int c = 0; c < _output; c++)
        {
            double V000 = _values[v000 + c];
            double V100 = _values[v100 + c];
            double V010 = _values[v010 + c];
            double V001 = _values[v001 + c];
            double V110 = _values[v110 + c];
            double V101 = _values[v101 + c];
            double V011 = _values[v011 + c];
            double V111 = _values[v111 + c];

            double y;
            if (dr >= dg)
            {
                if (dg >= db)
                    y = V000 * (1 - dr) + V100 * (dr - dg) + V110 * (dg - db) + V111 * db;
                else if (dr >= db)
                    y = V000 * (1 - dr) + V100 * (dr - db) + V101 * (db - dg) + V111 * dg;
                else
                    y = V000 * (1 - db) + V001 * (db - dr) + V101 * (dr - dg) + V111 * dg;
            }
            else
            {
                if (dr >= db)
                    y = V000 * (1 - dg) + V010 * (dg - dr) + V110 * (dr - db) + V111 * db;
                else if (dg >= db)
                    y = V000 * (1 - dg) + V010 * (dg - db) + V011 * (db - dr) + V111 * dr;
                else
                    y = V000 * (1 - db) + V001 * (db - dg) + V011 * (dg - dr) + V111 * dr;
            }
            output[c] = y;
        }
    }

    /// <summary>
    /// Maps x ∈ [0, 1] to a floor grid index and fractional offset, clamping at grid boundaries
    /// so that floor index + 1 is always a valid array offset.
    /// </summary>
    private static (int Index, double Frac) LocateIndex(double x, int gridPoints)
    {
        if (x <= 0.0) return (0, 0.0);
        if (x >= 1.0) return (gridPoints - 2, 1.0);
        double scaled = x * (gridPoints - 1);
        int idx = (int)Math.Floor(scaled);
        if (idx >= gridPoints - 1) idx = gridPoints - 2;
        return (idx, scaled - idx);
    }
}
