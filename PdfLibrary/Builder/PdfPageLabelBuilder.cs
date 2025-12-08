namespace PdfLibrary.Builder;

/// <summary>
/// Fluent builder for configuring page label ranges
/// </summary>
public class PdfPageLabelBuilder
{
    private readonly PdfPageLabelRange _range;

    internal PdfPageLabelBuilder(PdfPageLabelRange range)
    {
        _range = range;
    }

    /// <summary>
    /// Use decimal Arabic numerals (1, 2, 3, ...)
    /// </summary>
    public PdfPageLabelBuilder Decimal()
    {
        _range.Style = PdfPageLabelStyle.Decimal;
        return this;
    }

    /// <summary>
    /// Use uppercase Roman numerals (I, II, III, ...)
    /// </summary>
    public PdfPageLabelBuilder UppercaseRoman()
    {
        _range.Style = PdfPageLabelStyle.UppercaseRoman;
        return this;
    }

    /// <summary>
    /// Use lowercase Roman numerals (i, ii, iii, ...)
    /// </summary>
    public PdfPageLabelBuilder LowercaseRoman()
    {
        _range.Style = PdfPageLabelStyle.LowercaseRoman;
        return this;
    }

    /// <summary>
    /// Use uppercase letters (A, B, C, ...)
    /// </summary>
    public PdfPageLabelBuilder UppercaseLetters()
    {
        _range.Style = PdfPageLabelStyle.UppercaseLetters;
        return this;
    }

    /// <summary>
    /// Use lowercase letters (a, b, c, ...)
    /// </summary>
    public PdfPageLabelBuilder LowercaseLetters()
    {
        _range.Style = PdfPageLabelStyle.LowercaseLetters;
        return this;
    }

    /// <summary>
    /// Use no numbering - only prefix will be shown
    /// </summary>
    public PdfPageLabelBuilder NoNumbering()
    {
        _range.Style = PdfPageLabelStyle.None;
        return this;
    }

    /// <summary>
    /// Add a prefix before the page number
    /// </summary>
    /// <param name="prefix">The prefix string (e.g., "A-" or "Chapter ")</param>
    public PdfPageLabelBuilder WithPrefix(string prefix)
    {
        _range.Prefix = prefix;
        return this;
    }

    /// <summary>
    /// Set the starting number for this range (default is 1)
    /// </summary>
    /// <param name="number">The starting number</param>
    public PdfPageLabelBuilder StartingAt(int number)
    {
        _range.StartNumber = number;
        return this;
    }

    /// <summary>
    /// Gets the underlying range
    /// </summary>
    public PdfPageLabelRange Range => _range;
}
