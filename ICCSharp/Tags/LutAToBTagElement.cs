using System;
using System.Collections.Generic;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.8 lutAToBType ('mAB '). Pipeline goes:
///   input → A curves → CLUT → M curves → matrix(3×3 + offset) → B curves → output
/// Any of A curves, CLUT, M curves, matrix may be omitted; B curves are mandatory.
///
/// For mAB the A-curve count equals input channels (i); B-curve count equals output channels (o).
/// </summary>
public sealed class LutAToBTagElement : TagElement
{
    public int InputChannels { get; }
    public int OutputChannels { get; }
    public IReadOnlyList<TagElement>? ACurves { get; }
    public LutClutData? Clut { get; }
    public IReadOnlyList<TagElement>? MCurves { get; }
    /// <summary>Length-12 row-major: m11..m33 (rows 0..8) followed by offsets [m41,m42,m43] (9..11).</summary>
    public double[]? Matrix { get; }
    public IReadOnlyList<TagElement> BCurves { get; }

    public LutAToBTagElement(
        int inputChannels, int outputChannels,
        IReadOnlyList<TagElement>? aCurves,
        LutClutData? clut,
        IReadOnlyList<TagElement>? mCurves,
        double[]? matrix,
        IReadOnlyList<TagElement> bCurves)
        : base(TagTypeSignatures.LutAToB)
    {
        InputChannels = inputChannels;
        OutputChannels = outputChannels;
        ACurves = aCurves;
        Clut = clut;
        MCurves = mCurves;
        Matrix = matrix;
        BCurves = bCurves;
    }

    internal static LutAToBTagElement Parse(ReadOnlyMemory<byte> tagData)
    {
        // mAB: input channels feed A curves; B curves count = output channels.
        // We need the i/o values to compute curve counts; ModernLutSections.Parse reads them
        // and uses them directly for curve counts.
        // For mAB:  A count = i,  B count = o.
        // Trick: peek at i and o without re-parsing the whole header.
        int i = tagData.Span[8];
        int o = tagData.Span[9];
        ModernLutSections.Parsed p = ModernLutSections.Parse(tagData, aCurveCount: i, bCurveCount: o);
        return new LutAToBTagElement(p.InputChannels, p.OutputChannels,
            p.ACurves, p.Clut, p.MCurves, p.Matrix, p.BCurves);
    }
}
