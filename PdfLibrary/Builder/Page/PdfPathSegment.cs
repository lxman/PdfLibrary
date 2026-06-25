namespace PdfLibrary.Builder.Page;

/// <summary>
/// A single segment in a <see cref="PdfPathContent"/> path.
/// </summary>
public class PdfPathSegment
{
    /// <summary>The segment kind (move, line, cubic/quadratic curve, close).</summary>
    public PdfPathSegmentType Type { get; init; }
    /// <summary>The segment coordinates in points; how many depends on <see cref="Type"/>.</summary>
    public double[] Points { get; init; } = [];
}
