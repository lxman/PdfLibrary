using System;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Executes a legacy lut8 ('mft1') or lut16 ('mft2') tag (ICC.1:2010 §10.10/§10.11). The pipeline
/// is identical for both tag types:
///   input → matrix(3×3) → input curves (per channel) → CLUT (i → o) → output curves → output
/// The matrix is applied only when input is 3-channel (per spec it is identity otherwise).
///
/// Input and output values are CLUT-normalized [0, 1]. The legacy XYZ encoding (max stored value
/// maps to <see cref="IccConstants.IccMaxXyz"/>) matches the modern mAB convention, so the same
/// <see cref="Transform.PcsCodec"/> decode applies after the pipeline.
/// </summary>
public sealed class LegacyLutPipeline : IColorTransform
{
    private readonly Matrix3x3 _matrix;
    private readonly SampledToneCurve[] _inputCurves;
    private readonly IClut _clut;
    private readonly SampledToneCurve[] _outputCurves;

    public int InputChannels { get; }
    public int OutputChannels { get; }

    public LegacyLutPipeline(Lut8TagElement tag)
    {
        if (tag is null) throw new ArgumentNullException(nameof(tag));
        InputChannels = tag.InputChannels;
        OutputChannels = tag.OutputChannels;
        _matrix = Matrix3x3.FromRowMajor(tag.Matrix);

        // Promote byte tables to ushort via replication (0xAB → 0xABAB) so the existing
        // SampledToneCurve evaluator can read them directly.
        _inputCurves = new SampledToneCurve[InputChannels];
        for (int c = 0; c < InputChannels; c++)
            _inputCurves[c] = BuildCurveFromBytes(tag.InputTables[c]);

        // CLUT body — promote 8-bit values to normalized doubles.
        double[] values = new double[tag.Clut.Length];
        for (int i = 0; i < values.Length; i++) values[i] = tag.Clut[i] / 255.0;
        byte[] gridPoints = new byte[InputChannels];
        for (int i = 0; i < InputChannels; i++) gridPoints[i] = (byte)tag.ClutGridPoints;
        _clut = Clut.FromTag(new LutClutData(gridPoints, 1, values, OutputChannels));

        _outputCurves = new SampledToneCurve[OutputChannels];
        for (int c = 0; c < OutputChannels; c++)
            _outputCurves[c] = BuildCurveFromBytes(tag.OutputTables[c]);
    }

    public LegacyLutPipeline(Lut16TagElement tag)
    {
        if (tag is null) throw new ArgumentNullException(nameof(tag));
        InputChannels = tag.InputChannels;
        OutputChannels = tag.OutputChannels;
        _matrix = Matrix3x3.FromRowMajor(tag.Matrix);

        _inputCurves = new SampledToneCurve[InputChannels];
        for (int c = 0; c < InputChannels; c++)
            _inputCurves[c] = new SampledToneCurve(tag.InputTables[c]);

        double[] values = new double[tag.Clut.Length];
        for (int i = 0; i < values.Length; i++) values[i] = tag.Clut[i] / 65535.0;
        byte[] gridPoints = new byte[InputChannels];
        for (int i = 0; i < InputChannels; i++) gridPoints[i] = (byte)tag.ClutGridPoints;
        _clut = Clut.FromTag(new LutClutData(gridPoints, 2, values, OutputChannels));

        _outputCurves = new SampledToneCurve[OutputChannels];
        for (int c = 0; c < OutputChannels; c++)
            _outputCurves[c] = new SampledToneCurve(tag.OutputTables[c]);
    }

    private static SampledToneCurve BuildCurveFromBytes(byte[] bytes)
    {
        ushort[] samples = new ushort[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            samples[i] = (ushort)(bytes[i] * 257); // 0xAB → 0xABAB; preserves full [0, 0xFFFF] range.
        return new SampledToneCurve(samples);
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length != InputChannels)
            throw new ArgumentException(
                $"Expected {InputChannels} inputs; got {input.Length}.", nameof(input));
        if (output.Length < OutputChannels)
            throw new ArgumentException(
                $"Output buffer too short: need {OutputChannels}, got {output.Length}.", nameof(output));

        int max = Math.Max(InputChannels, OutputChannels);
        Span<double> buf = stackalloc double[Math.Max(max, 3)];
        input.CopyTo(buf.Slice(0, InputChannels));

        // 1. Matrix (3×3 only meaningful for 3-channel input; identity otherwise per spec).
        if (InputChannels == 3)
        {
            (double X, double Y, double Z) = _matrix.Transform(buf[0], buf[1], buf[2]);
            buf[0] = X; buf[1] = Y; buf[2] = Z;
        }

        // 2. Input curves.
        for (int c = 0; c < InputChannels; c++)
            buf[c] = _inputCurves[c].Evaluate(buf[c]);

        // 3. CLUT (i → o).
        Span<double> clutOut = stackalloc double[OutputChannels];
        _clut.Apply(buf.Slice(0, InputChannels), clutOut);

        // 4. Output curves.
        for (int c = 0; c < OutputChannels; c++)
            output[c] = _outputCurves[c].Evaluate(clutOut[c]);
    }
}
