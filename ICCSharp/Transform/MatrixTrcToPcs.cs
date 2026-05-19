using System;
using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Transform;

/// <summary>
/// Source-side pipeline for v2 matrix/TRC profiles: per-channel TRC followed by the 3×3 matrix
/// whose columns are the rXYZ / gXYZ / bXYZ colorant XYZ tags. Output is PCS-XYZ (D50).
/// </summary>
internal sealed class MatrixTrcToPcs : IColorTransform
{
    private readonly IToneCurve _rTrc;
    private readonly IToneCurve _gTrc;
    private readonly IToneCurve _bTrc;
    private readonly Matrix3x3 _matrix;

    public int InputChannels => 3;
    public int OutputChannels => 3;

    public MatrixTrcToPcs(IccProfile p)
    {
        TagElement rt = p.RedTrc ?? throw new IccTransformException("Source profile missing rTRC.");
        TagElement gt = p.GreenTrc ?? throw new IccTransformException("Source profile missing gTRC.");
        TagElement bt = p.BlueTrc ?? throw new IccTransformException("Source profile missing bTRC.");
        XyzNumber rc = p.RedColorant ?? throw new IccTransformException("Source profile missing rXYZ.");
        XyzNumber gc = p.GreenColorant ?? throw new IccTransformException("Source profile missing gXYZ.");
        XyzNumber bc = p.BlueColorant ?? throw new IccTransformException("Source profile missing bXYZ.");

        _rTrc = ToneCurve.FromTag(rt);
        _gTrc = ToneCurve.FromTag(gt);
        _bTrc = ToneCurve.FromTag(bt);
        _matrix = Matrix3x3.FromColorants(rc, gc, bc);
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        double r = _rTrc.Evaluate(input[0]);
        double g = _gTrc.Evaluate(input[1]);
        double b = _bTrc.Evaluate(input[2]);
        (double X, double Y, double Z) = _matrix.Transform(r, g, b);
        output[0] = X; output[1] = Y; output[2] = Z;
    }
}
