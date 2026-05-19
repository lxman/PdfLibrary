using System;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Executes the lutAToBType pipeline (ICC.1:2010 §10.8):
///   input → A curves → CLUT → M curves → matrix → B curves → output
/// Each block is optional except B curves. M curves and matrix appear together (matrix is 3×3 +
/// offset, so the M/matrix stage operates on exactly 3 channels).
/// </summary>
public sealed class MabPipeline : IColorTransform
{
    public int InputChannels { get; }
    public int OutputChannels { get; }

    private readonly PerChannelCurves? _aCurves;
    private readonly IClut? _clut;
    private readonly PerChannelCurves? _mCurves;
    private readonly MatrixTransform? _matrix;
    private readonly PerChannelCurves _bCurves;

    public MabPipeline(LutAToBTagElement tag)
    {
        if (tag is null) throw new ArgumentNullException(nameof(tag));
        InputChannels = tag.InputChannels;
        OutputChannels = tag.OutputChannels;

        _aCurves = tag.ACurves is null ? null : new PerChannelCurves(tag.ACurves);
        _clut = tag.Clut is null ? null : Clut.FromTag(tag.Clut);
        _mCurves = tag.MCurves is null ? null : new PerChannelCurves(tag.MCurves);
        _matrix = tag.Matrix is null ? null : MatrixTransform.FromMabArray(tag.Matrix);
        _bCurves = new PerChannelCurves(tag.BCurves);

        ValidateShape();
    }

    private void ValidateShape()
    {
        if (_aCurves != null && _aCurves.ChannelCount != InputChannels)
            throw new ArgumentException(
                $"A curves count {_aCurves.ChannelCount} != input channels {InputChannels}.");
        if (_clut != null && _clut.InputChannels != InputChannels)
            throw new ArgumentException(
                $"CLUT input channels {_clut.InputChannels} != tag input channels {InputChannels}.");
        if (_clut != null && _clut.OutputChannels != OutputChannels)
            throw new ArgumentException(
                $"CLUT output channels {_clut.OutputChannels} != tag output channels {OutputChannels}.");
        if (_mCurves != null && _matrix == null)
            throw new ArgumentException("mAB tag has M curves but no matrix; spec allows both together only.");
        if (_matrix != null && _mCurves == null)
            throw new ArgumentException("mAB tag has matrix but no M curves; spec allows both together only.");
        if (_matrix != null && _mCurves != null && _mCurves.ChannelCount != 3)
            throw new ArgumentException(
                $"mAB M curves count {_mCurves.ChannelCount} != 3 (matrix dimension).");
        if (_bCurves.ChannelCount != OutputChannels)
            throw new ArgumentException(
                $"B curves count {_bCurves.ChannelCount} != output channels {OutputChannels}.");
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length != InputChannels)
            throw new ArgumentException($"Expected {InputChannels} inputs; got {input.Length}.", nameof(input));
        if (output.Length < OutputChannels)
            throw new ArgumentException(
                $"Output buffer too short: need {OutputChannels}, got {output.Length}.", nameof(output));

        // Working buffer sized to the wider of input/output (CLUT changes width).
        int max = Math.Max(InputChannels, OutputChannels);
        Span<double> buf = stackalloc double[Math.Max(max, 3)];
        input.CopyTo(buf.Slice(0, InputChannels));

        // A curves operate on i channels.
        if (_aCurves != null) _aCurves.Apply(buf.Slice(0, InputChannels));

        // CLUT maps i → o.
        if (_clut != null)
        {
            Span<double> outSlice = stackalloc double[OutputChannels];
            _clut.Apply(buf.Slice(0, InputChannels), outSlice);
            outSlice.CopyTo(buf.Slice(0, OutputChannels));
        }

        int width = _clut != null ? OutputChannels : InputChannels;

        if (_mCurves != null) _mCurves.Apply(buf.Slice(0, 3));
        if (_matrix != null)
        {
            (double x, double y, double z) = _matrix.Transform(buf[0], buf[1], buf[2]);
            buf[0] = x; buf[1] = y; buf[2] = z;
            width = 3;
        }

        if (width != OutputChannels)
            throw new InvalidOperationException(
                $"Pipeline width mismatch: ended at {width} channels but tag declares {OutputChannels} outputs.");
        _bCurves.Apply(buf.Slice(0, OutputChannels));

        buf.Slice(0, OutputChannels).CopyTo(output);
    }
}
