using System;
using ICCSharp.Eval;
using ICCSharp.IO;
using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Transform;

/// <summary>
/// Destination-side pipeline for v2 matrix/TRC profiles: inverse 3×3 colorant matrix followed by
/// per-channel inverse TRCs. Input is PCS-XYZ (D50), output is device RGB.
/// </summary>
internal sealed class MatrixTrcFromPcs : IColorTransform
{
    private readonly IToneCurve _rTrc;
    private readonly IToneCurve _gTrc;
    private readonly IToneCurve _bTrc;
    private readonly Matrix3x3 _inverseMatrix;

    public int InputChannels => 3;
    public int OutputChannels => 3;

    public MatrixTrcFromPcs(IccProfile p)
    {
        TagElement rt = p.RedTrc ?? throw new IccTransformException("Destination profile missing rTRC.");
        TagElement gt = p.GreenTrc ?? throw new IccTransformException("Destination profile missing gTRC.");
        TagElement bt = p.BlueTrc ?? throw new IccTransformException("Destination profile missing bTRC.");
        XyzNumber rc = p.RedColorant ?? throw new IccTransformException("Destination profile missing rXYZ.");
        XyzNumber gc = p.GreenColorant ?? throw new IccTransformException("Destination profile missing gXYZ.");
        XyzNumber bc = p.BlueColorant ?? throw new IccTransformException("Destination profile missing bXYZ.");

        _rTrc = ToneCurve.FromTag(rt);
        _gTrc = ToneCurve.FromTag(gt);
        _bTrc = ToneCurve.FromTag(bt);
        _inverseMatrix = Matrix3x3.FromColorants(rc, gc, bc).Inverse();
    }

    public void Apply(ReadOnlySpan<double> input, Span<double> output)
    {
        (double r, double g, double b) = _inverseMatrix.Transform(input[0], input[1], input[2]);
        output[0] = _rTrc.EvaluateInverse(r);
        output[1] = _gTrc.EvaluateInverse(g);
        output[2] = _bTrc.EvaluateInverse(b);
    }
}
