using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Builder for text input fields
/// </summary>
public class PdfTextFieldBuilder(string name, PdfRect rect) : PdfFormFieldBuilder(name, rect)
{
    /// <summary>
    /// Default value
    /// </summary>
    public string? DefaultValue { get; private set; }

    /// <summary>
    /// Maximum length of text (0 = unlimited)
    /// </summary>
    public int MaxLength { get; private set; }

    /// <summary>
    /// Whether this is a multiline field
    /// </summary>
    public bool IsMultiline { get; private set; }

    /// <summary>
    /// Whether this is a password field
    /// </summary>
    public bool IsPassword { get; private set; }

    /// <summary>
    /// Whether to use a comb layout (fixed character positions)
    /// </summary>
    public bool IsComb { get; private set; }

    /// <summary>
    /// Font name for the field text
    /// </summary>
    public string FontName { get; private set; } = "Helvetica";

    /// <summary>
    /// Font size in points (0 = auto-size)
    /// </summary>
    public double FontSize { get; private set; }

    /// <summary>
    /// Text color
    /// </summary>
    public PdfColor TextColor { get; private set; } = PdfColor.Black;

    /// <summary>
    /// Text alignment
    /// </summary>
    public PdfTextAlignment Alignment { get; private set; } = PdfTextAlignment.Left;

    /// <summary>
    /// Set the default value
    /// </summary>
    public PdfTextFieldBuilder Value(string value)
    {
        DefaultValue = value;
        return this;
    }

    /// <summary>
    /// Set maximum text length
    /// </summary>
    public PdfTextFieldBuilder WithMaxLength(int length)
    {
        MaxLength = length;
        return this;
    }

    /// <summary>
    /// Make this a multiline text field
    /// </summary>
    public PdfTextFieldBuilder Multiline(bool multiline = true)
    {
        IsMultiline = multiline;
        return this;
    }

    /// <summary>
    /// Make this a password field
    /// </summary>
    public PdfTextFieldBuilder Password(bool password = true)
    {
        IsPassword = password;
        return this;
    }

    /// <summary>
    /// Use a comb layout with fixed character positions
    /// </summary>
    public PdfTextFieldBuilder Comb(bool comb = true)
    {
        IsComb = comb;
        return this;
    }

    /// <summary>
    /// Set the font
    /// </summary>
    public PdfTextFieldBuilder Font(string fontName, double fontSize = 0)
    {
        FontName = fontName;
        FontSize = fontSize;
        return this;
    }

    /// <summary>
    /// Set text color
    /// </summary>
    public PdfTextFieldBuilder Color(PdfColor color)
    {
        TextColor = color;
        return this;
    }

    /// <summary>
    /// Set text alignment
    /// </summary>
    public PdfTextFieldBuilder Align(PdfTextAlignment alignment)
    {
        Alignment = alignment;
        return this;
    }

    /// <summary>
    /// Make field required
    /// </summary>
    public PdfTextFieldBuilder Required(bool required = true)
    {
        IsRequired = required;
        return this;
    }

    /// <summary>
    /// Make the field read-only
    /// </summary>
    public PdfTextFieldBuilder ReadOnly(bool readOnly = true)
    {
        IsReadOnly = readOnly;
        return this;
    }

    /// <summary>
    /// Set tooltip text
    /// </summary>
    public PdfTextFieldBuilder WithTooltip(string tooltip)
    {
        Tooltip = tooltip;
        return this;
    }

    /// <summary>
    /// Set border appearance
    /// </summary>
    public PdfTextFieldBuilder Border(PdfColor color, double width = 1)
    {
        BorderColor = color;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set border style
    /// </summary>
    public PdfTextFieldBuilder BorderStyled(PdfBorderStyle style, double width = 1)
    {
        BorderStyle = style;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set a dashed border with a custom pattern
    /// </summary>
    public PdfTextFieldBuilder BorderDashed(double dashLength = 3, double gapLength = 3, double width = 1)
    {
        BorderStyle = PdfBorderStyle.Dashed;
        DashPattern = [dashLength, gapLength];
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set background color
    /// </summary>
    public PdfTextFieldBuilder Background(PdfColor color)
    {
        BackgroundColor = color;
        return this;
    }

    /// <summary>
    /// Remove border (no border)
    /// </summary>
    public PdfTextFieldBuilder NoBorder()
    {
        BorderWidth = 0;
        BorderColor = null;
        return this;
    }
}