using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Builder;

/// <summary>
/// Information about a custom embedded font
/// </summary>
internal class CustomFontInfo
{
    /// <summary>
    /// Alias name used to reference this font in the document
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Path to the original font file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Raw font file data
    /// </summary>
    public required byte[] FontData { get; init; }

    /// <summary>
    /// Parsed font metrics
    /// </summary>
    public required EmbeddedFontMetrics Metrics { get; init; }

    /// <summary>
    /// PostScript name from font
    /// </summary>
    public required string PostScriptName { get; init; }

    /// <summary>
    /// Font family name
    /// </summary>
    public required string FamilyName { get; init; }
}