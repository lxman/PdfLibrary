namespace PdfLibrary.Builder.Page;

/// <summary>
/// Fluent builder for text content
/// </summary>
public class PdfTextBuilder
{
    private readonly PdfPageBuilder _pageBuilder;
    private readonly PdfTextContent _content;

    internal PdfTextBuilder(PdfPageBuilder pageBuilder, PdfTextContent content)
    {
        _pageBuilder = pageBuilder;
        _content = content;
    }

    /// <summary>
    /// Set the font
    /// </summary>
    public PdfTextBuilder Font(string fontName, double fontSize = 12)
    {
        _content.FontName = fontName;
        _content.FontSize = fontSize;
        return this;
    }

    /// <summary>
    /// Set font size only
    /// </summary>
    public PdfTextBuilder Size(double fontSize)
    {
        _content.FontSize = fontSize;
        return this;
    }

    /// <summary>
    /// Set fill color
    /// </summary>
    public PdfTextBuilder Color(PdfColor color)
    {
        _content.FillColor = color;
        return this;
    }

    /// <summary>
    /// Set fill color (alias for Color method, following fluent API naming conventions)
    /// </summary>
    public PdfTextBuilder WithColor(PdfColor color) => Color(color);

    /// <summary>
    /// Set stroke color (for outline text)
    /// </summary>
    public PdfTextBuilder StrokeColor(PdfColor color, double width = 1)
    {
        _content.StrokeColor = color;
        _content.StrokeWidth = width;
        return this;
    }

    /// <summary>
    /// Rotate text by specified degrees
    /// </summary>
    public PdfTextBuilder Rotate(double degrees)
    {
        _content.Rotation = degrees;
        return this;
    }

    /// <summary>
    /// Set character spacing (extra space between characters in points)
    /// </summary>
    public PdfTextBuilder CharacterSpacing(double spacing)
    {
        _content.CharacterSpacing = spacing;
        return this;
    }

    /// <summary>
    /// Set word spacing (extra space between words in points)
    /// </summary>
    public PdfTextBuilder WordSpacing(double spacing)
    {
        _content.WordSpacing = spacing;
        return this;
    }

    /// <summary>
    /// Set horizontal scaling (percentage, 100 = normal)
    /// </summary>
    public PdfTextBuilder HorizontalScale(double percentage)
    {
        _content.HorizontalScale = percentage;
        return this;
    }

    /// <summary>
    /// Condense text horizontally
    /// </summary>
    public PdfTextBuilder Condensed(double percentage = 80)
    {
        _content.HorizontalScale = percentage;
        return this;
    }

    /// <summary>
    /// Expand text horizontally
    /// </summary>
    public PdfTextBuilder Expanded(double percentage = 120)
    {
        _content.HorizontalScale = percentage;
        return this;
    }

    /// <summary>
    /// Set text rise (positive = superscript, negative = subscript)
    /// </summary>
    public PdfTextBuilder Rise(double points)
    {
        _content.TextRise = points;
        return this;
    }

    /// <summary>
    /// Format as superscript
    /// </summary>
    public PdfTextBuilder Superscript()
    {
        _content.TextRise = _content.FontSize * 0.4;
        _content.FontSize *= 0.7;
        return this;
    }

    /// <summary>
    /// Format as subscript
    /// </summary>
    public PdfTextBuilder Subscript()
    {
        _content.TextRise = -_content.FontSize * 0.2;
        _content.FontSize *= 0.7;
        return this;
    }

    /// <summary>
    /// Set line spacing for multiline text
    /// </summary>
    public PdfTextBuilder LineSpacing(double points)
    {
        _content.LineSpacing = points;
        return this;
    }

    /// <summary>
    /// Set text rendering mode
    /// </summary>
    public PdfTextBuilder RenderMode(PdfTextRenderMode mode)
    {
        _content.RenderMode = mode;
        return this;
    }

    /// <summary>
    /// Render as outline only (no fill)
    /// </summary>
    public PdfTextBuilder Outline(double strokeWidth = 1)
    {
        _content.RenderMode = PdfTextRenderMode.Stroke;
        _content.StrokeWidth = strokeWidth;
        _content.StrokeColor ??= _content.FillColor;
        return this;
    }

    /// <summary>
    /// Render as filled with outline
    /// </summary>
    public PdfTextBuilder FillAndOutline(PdfColor strokeColor, double strokeWidth = 1)
    {
        _content.RenderMode = PdfTextRenderMode.FillStroke;
        _content.StrokeColor = strokeColor;
        _content.StrokeWidth = strokeWidth;
        return this;
    }

    /// <summary>
    /// Make text invisible (useful for searchable OCR layers)
    /// </summary>
    public PdfTextBuilder Invisible()
    {
        _content.RenderMode = PdfTextRenderMode.Invisible;
        return this;
    }

    /// <summary>
    /// Set text alignment (for use with MaxWidth)
    /// </summary>
    public PdfTextBuilder Align(PdfTextAlignment alignment)
    {
        _content.Alignment = alignment;
        return this;
    }

    /// <summary>
    /// Set maximum width (text will wrap or be truncated)
    /// </summary>
    public PdfTextBuilder MaxWidth(double points)
    {
        _content.MaxWidth = points;
        return this;
    }

    /// <summary>
    /// Bold simulation using stroke
    /// </summary>
    public PdfTextBuilder Bold()
    {
        _content.RenderMode = PdfTextRenderMode.FillStroke;
        _content.StrokeColor = _content.FillColor;
        _content.StrokeWidth = _content.FontSize * 0.03;
        return this;
    }

    /// <summary>
    /// Return to page builder for adding more content
    /// </summary>
    public PdfPageBuilder Done()
    {
        return _pageBuilder;
    }

    /// <summary>
    /// Implicit conversion back to page builder
    /// </summary>
    public static implicit operator PdfPageBuilder(PdfTextBuilder builder)
    {
        return builder._pageBuilder;
    }
}