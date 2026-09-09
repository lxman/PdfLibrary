namespace PdfLibrary.Content;

/// <summary>
/// Text extracted from a PDF page together with cheap signals that help callers decide whether a
/// predominantly Latin text layer is plausible or should be replaced with OCR.
/// </summary>
/// <remarks>
/// PDF fonts can omit or contain an incorrect <c>/ToUnicode</c> map. In that case extraction may
/// return raw glyph indices, control characters, or replacement characters without throwing. The
/// assessment is deliberately heuristic: a low printable-ASCII ratio may be normal for non-Latin
/// text, so callers handling multilingual documents should use the individual metrics instead of
/// relying only on <see cref="IsLikelyReadable"/>.
/// </remarks>
public sealed class TextExtractionResult
{
    private const double MinimumPrintableAsciiRatio = 0.90;

    internal TextExtractionResult(string text)
    {
        Text = text;
        NonWhitespaceCharacterCount = text.Count(c => !char.IsWhiteSpace(c));
        int printableAscii = text.Count(c => c is >= '!' and <= '~');
        UnexpectedControlCharacterCount = text.Count(c => char.IsControl(c) && !char.IsWhiteSpace(c));
        ReplacementCharacterCount = text.Count(c => c == '\uFFFD');
        PrintableAsciiRatio = NonWhitespaceCharacterCount == 0
            ? 0
            : (double)printableAscii / NonWhitespaceCharacterCount;

        IsLikelyReadable = NonWhitespaceCharacterCount > 0
            && PrintableAsciiRatio >= MinimumPrintableAsciiRatio;
    }

    /// <summary>The assembled page text, identical to <c>PdfPage.ExtractText()</c>.</summary>
    public string Text { get; }

    /// <summary>Number of characters considered when calculating the quality ratios.</summary>
    public int NonWhitespaceCharacterCount { get; }

    /// <summary>Fraction of non-whitespace characters in the printable ASCII range U+0021–U+007E.</summary>
    public double PrintableAsciiRatio { get; }

    /// <summary>Control characters other than whitespace, commonly produced by raw glyph-index fallback.</summary>
    public int UnexpectedControlCharacterCount { get; }

    /// <summary>Number of Unicode replacement characters (U+FFFD), commonly produced by a broken map.</summary>
    public int ReplacementCharacterCount { get; }

    /// <summary>
    /// Whether the text passes conservative plausibility checks for predominantly Latin text.
    /// Empty text and text with a printable-ASCII ratio below 90 percent return
    /// <see langword="false"/>. The control and replacement counts are exposed separately because a
    /// small repeated set of unmapped bullets or icons need not invalidate an otherwise readable page.
    /// </summary>
    public bool IsLikelyReadable { get; }
}
