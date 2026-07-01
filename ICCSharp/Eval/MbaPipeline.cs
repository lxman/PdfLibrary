using System;
using ICCSharp.Tags;

namespace ICCSharp.Eval;

/// <summary>
/// Executes the lutBToAType pipeline (ICC.1:2010 §10.9):
///   input → B curves → matrix → M curves → CLUT → A curves → output
/// Same blocks as <see cref="MabPipeline"/> but in reverse order.
/// </summary>
public sealed class MbaPipeline : IColorTransform
{
    public int InputChannels { get; }
    public int OutputChannels { get; }

    private readonly PerChannelCurves _bCurves;
    private readonly MatrixTransform? _matrix;
    private readonly PerChannelCurves? _mCurves;
    private readonly IClut? _clut;
    private readonly PerChannelCurves? _aCurves;

    public MbaPipeline(LutBToATagElement tag)
    {
        if (tag is null) throw new ArgumentNullException(nameof(tag));
        InputChannels = tag.InputChannels;
        OutputChannels = tag.OutputChannels;

        _bCurves = new PerChannelCurves(tag.BCurves);
        _matrix = tag.Matrix is null ? null : MatrixTransform.FromMabArray(tag.Matrix);
        _mCurves = tag.MCurves is null ? null : new PerChannelCurves(tag.MCurves);
        _clut = tag.Clut is null ? null : Clut.FromTag(tag.Clut);
        _aCurves = tag.ACurves is null ? null : new PerChannelCurves(tag.ACurves);

        ValidateShape();
    }

    private void ValidateShape()
    {
        if (_bCurves.ChannelCount != InputChannels)
            throw new ArgumentException(
                $"B curves count {_bCurves.ChannelCount} != input channels {InputChannels}.");
        if (_mCurves is not null && _matrix is null)
            throw new ArgumentException("mBA tag has M curves but no matrix; spec allows both together only.");
        if (_matrix is not null && _mCurves is null)
            throw new ArgumentException("mBA tag has matrix but no M curves; spec allows both together only.");
        if (_matrix is not null && _mCurves is not null && _mCurves.ChannelCount != 3)
            throw new ArgumentException(
                $"mBA M curves count {_mCurves.ChannelCount} != 3 (matrix dimension).");
        if (_clut is not null && _clut.InputChannels != InputChannels)
            throw new ArgumentException(
                $"CLUT input channels {_clut.InputChannels} != tag input channels {InputChannels}.");
        if (_clut is not null && _clut.OutputChannels != OutputChannels)
            throw new ArgumentException(
                $"CLUT output channels {_clut.OutputChannels} != tag output channels {OutputChannels}.");
        if (_aCurves is not null && _aCurves.ChannelCount != OutputChannels)
            throw new ArgumentException(
                $"A curves count {_aCurves.ChannelCount} != output channels {OutputChannels}.");
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length != InputChannels)
            throw new ArgumentException($"Expected {InputChannels} inputs; got {input.Length}.", nameof(input));
        if (output.Length < OutputChannels)
            throw new ArgumentException(
                $"Output buffer too short: need {OutputChannels}, got {output.Length}.", nameof(output));

        int max = Math.Max(InputChannels, OutputChannels);
        Span<double> buf = stackalloc double[Math.Max(max, 3)];
        input.CopyTo(buf.Slice(0, InputChannels));

        _bCurves.Apply(buf.Slice(0, InputChannels));

        if (_matrix is not null)
        {
            (double x, double y, double z) = _matrix.Transform(buf[0], buf[1], buf[2]);
            buf[0] = x; buf[1] = y; buf[2] = z;
        }

        if (_mCurves is not null) _mCurves.Apply(buf.Slice(0, 3));

        // CLUT maps i → o.
        if (_clut is not null)
        {
            Span<double> outSlice = stackalloc double[OutputChannels];
            _clut.Apply(buf.Slice(0, InputChannels), outSlice);
            outSlice.CopyTo(buf.Slice(0, OutputChannels));
        }

        if (_aCurves is not null) _aCurves.Apply(buf.Slice(0, OutputChannels));

        buf.Slice(0, OutputChannels).CopyTo(output);
    }
}
