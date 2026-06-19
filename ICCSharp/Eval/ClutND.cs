using System;
using System.Collections.Generic;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Arbitrary-dimensional CLUT (1..15 input channels) with multilinear interpolation. Used when
/// the input space isn't 3-D (CMYK → PCS, 6CLR HiFi, hexachrome, etc.) — for 3-D inputs prefer
/// <see cref="Clut3D"/>, which uses tetrahedral interpolation that preserves the neutral diagonal.
///
/// Multilinear blends 2^N corners with weights = product over each axis of (frac or 1-frac).
/// </summary>
public sealed class ClutND : IClut
{
    /// <summary>Maximum supported input-channel count (ICC NCLR space tops out at 'FCLR' = 15).</summary>
    public const int MaxInputDims = 15;

    private readonly int _inputDims;
    private readonly int _outputChannels;
    private readonly int[] _gridPoints;
    private readonly int[] _strides;     // flat-array stride per input dim
    private readonly double[] _values;

    public int InputChannels => _inputDims;
    public int OutputChannels => _outputChannels;
    public IReadOnlyList<int> GridPoints => _gridPoints;

    public ClutND(LutClutData clut)
    {
        if (clut is null) throw new ArgumentNullException(nameof(clut));
        _inputDims = clut.GridPoints.Count;
        if (_inputDims < 1 || _inputDims > MaxInputDims)
            throw new ArgumentException(
                $"ClutND supports 1..{MaxInputDims} input dimensions; got {_inputDims}.", nameof(clut));

        _gridPoints = new int[_inputDims];
        long total = 1;
        for (var i = 0; i < _inputDims; i++)
        {
            _gridPoints[i] = clut.GridPoints[i];
            if (_gridPoints[i] < 2)
                throw new ArgumentException(
                    $"Grid dimension {i} must be ≥ 2; got {_gridPoints[i]}.", nameof(clut));
            total = checked(total * _gridPoints[i]);
        }
        _outputChannels = clut.OutputChannels;

        long expected = checked(total * _outputChannels);
        if (clut.Values.Count != expected)
            throw new ArgumentException(
                $"CLUT value count {clut.Values.Count} does not match grid product × outputs = {expected}.",
                nameof(clut));

        _values = new double[(int)expected];
        for (var i = 0; i < _values.Length; i++) _values[i] = clut.Values[i];

        // Row-major: first dim slowest. stride[N-1] = output; stride[i] = stride[i+1] * grid[i+1].
        _strides = new int[_inputDims];
        _strides[_inputDims - 1] = _outputChannels;
        for (int i = _inputDims - 2; i >= 0; i--)
            _strides[i] = _strides[i + 1] * _gridPoints[i + 1];
    }

    public double[] Apply(ReadOnlySpan<double> inputs)
    {
        var result = new double[_outputChannels];
        Apply(inputs, result);
        return result;
    }

    /// <summary>Zero-allocation overload — caller-supplied output buffer.</summary>
    public void Apply(ReadOnlySpan<double> inputs, Span<double> outputs)
    {
        if (inputs.Length != _inputDims)
            throw new ArgumentException(
                $"Expected {_inputDims} inputs, got {inputs.Length}.", nameof(inputs));
        if (outputs.Length < _outputChannels)
            throw new ArgumentException(
                $"Output buffer too short: need {_outputChannels} elements, got {outputs.Length}.", nameof(outputs));

        // Floor indices and fractions per dim.
        Span<int> idx = stackalloc int[MaxInputDims];
        Span<double> frac = stackalloc double[MaxInputDims];

        var baseOffset = 0;
        for (var i = 0; i < _inputDims; i++)
        {
            (int ii, double f) = LocateIndex(inputs[i], _gridPoints[i]);
            idx[i] = ii;
            frac[i] = f;
            baseOffset += ii * _strides[i];
        }

        outputs.Slice(0, _outputChannels).Clear();

        int cornerCount = 1 << _inputDims;
        for (var corner = 0; corner < cornerCount; corner++)
        {
            int offset = baseOffset;
            var weight = 1.0;
            for (var i = 0; i < _inputDims; i++)
            {
                if (((corner >> i) & 1) != 0)
                {
                    offset += _strides[i];
                    weight *= frac[i];
                }
                else
                {
                    weight *= 1.0 - frac[i];
                }
            }
            if (weight == 0.0) continue;
            for (var c = 0; c < _outputChannels; c++)
                outputs[c] += weight * _values[offset + c];
        }
    }

    private static (int Index, double Frac) LocateIndex(double x, int gridPoints)
    {
        if (x <= 0.0) return (0, 0.0);
        if (x >= 1.0) return (gridPoints - 2, 1.0);
        double scaled = x * (gridPoints - 1);
        var idx = (int)Math.Floor(scaled);
        if (idx >= gridPoints - 1) idx = gridPoints - 2;
        return (idx, scaled - idx);
    }
}
