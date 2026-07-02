namespace PdfLibrary.Content;

/// <summary>
/// Represents a text fragment with position and formatting information
/// </summary>
public class TextFragment
{
    public string Text { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public string? FontName { get; init; }
    public double FontSize { get; init; }

    /// <summary>Total horizontal advance of this fragment in PDF text-space units (the same space
    /// as <see cref="X"/>/<see cref="Y"/>): the sum of per-glyph advances from font metrics, or a
    /// rough <c>bytes × size × 0.5</c> estimate when the font cannot be resolved. Lets consumers
    /// build highlight/selection rectangles without re-walking font metrics.</summary>
    public double Width { get; init; }

    /// <summary>Offset of <see cref="Text"/> within the assembled page text returned alongside
    /// the fragments by <c>ExtractTextWithFragments()</c> (the assembled text interleaves
    /// heuristic ' '/'\n' separators that belong to no fragment). Lets consumers search the
    /// correctly word-joined assembled text and map match ranges back to fragments.</summary>
    public int TextOffset { get; init; }

    public override string ToString() => $"{Text} at ({X:F2}, {Y:F2}) {FontName} {FontSize}pt";
}