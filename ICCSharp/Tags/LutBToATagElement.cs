using System;
using System.Collections.Generic;

namespace ICCSharp.Tags;

/// <summary>
/// ICC.1:2010 §10.9 lutBToAType ('mBA '). Pipeline goes:
///   input → B curves → matrix → M curves → CLUT → A curves → output
/// Any of A curves, CLUT, M curves, matrix may be omitted; B curves are mandatory.
///
/// For mBA the B-curve count equals input channels (i); A-curve count equals output channels (o).
/// </summary>
public sealed class LutBToATagElement : TagElement
{
    public int InputChannels { get; }
    public int OutputChannels { get; }
    public IReadOnlyList<TagElement> BCurves { get; }
    /// <summary>Length-12 row-major: m11..m33 followed by offsets.</summary>
    public double[]? Matrix { get; }
    public IReadOnlyList<TagElement>? MCurves { get; }
    public LutClutData? Clut { get; }
    public IReadOnlyList<TagElement>? ACurves { get; }

    public LutBToATagElement(
        int inputChannels, int outputChannels,
        IReadOnlyList<TagElement> bCurves,
        double[]? matrix,
        IReadOnlyList<TagElement>? mCurves,
        LutClutData? clut,
        IReadOnlyList<TagElement>? aCurves)
        : base(TagTypeSignatures.LutBToA)
    {
        InputChannels = inputChannels;
        OutputChannels = outputChannels;
        BCurves = bCurves;
        Matrix = matrix;
        MCurves = mCurves;
        Clut = clut;
        ACurves = aCurves;
    }

    internal static LutBToATagElement Parse(ReadOnlyMemory<byte> tagData)
    {
        int i = tagData.Span[8];
        int o = tagData.Span[9];
        // mBA: B count = i, A count = o.
        ModernLutSections.Parsed p = ModernLutSections.Parse(tagData, aCurveCount: o, bCurveCount: i);
        return new LutBToATagElement(p.InputChannels, p.OutputChannels,
            p.BCurves, p.Matrix, p.MCurves, p.Clut, p.ACurves);
    }
}
