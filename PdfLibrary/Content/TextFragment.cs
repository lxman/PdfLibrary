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

    public override string ToString() => $"{Text} at ({X:F2}, {Y:F2}) {FontName} {FontSize}pt";
}