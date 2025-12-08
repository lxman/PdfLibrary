namespace PdfLibrary.Builder.Page;

/// <summary>
/// Represents a segment in a path
/// </summary>
public class PdfPathSegment
{
    public PdfPathSegmentType Type { get; init; }
    public double[] Points { get; init; } = [];
}