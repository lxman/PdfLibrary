using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Builder for signature fields
/// </summary>
public class PdfSignatureFieldBuilder(string name, PdfRect rect) : PdfFormFieldBuilder(name, rect)
{
    /// <summary>
    /// Make field required
    /// </summary>
    public PdfSignatureFieldBuilder Required(bool required = true)
    {
        IsRequired = required;
        return this;
    }

    /// <summary>
    /// Make field read-only
    /// </summary>
    public PdfSignatureFieldBuilder ReadOnly(bool readOnly = true)
    {
        IsReadOnly = readOnly;
        return this;
    }

    /// <summary>
    /// Set tooltip text
    /// </summary>
    public PdfSignatureFieldBuilder WithTooltip(string tooltip)
    {
        Tooltip = tooltip;
        return this;
    }

    /// <summary>
    /// Set border appearance
    /// </summary>
    public PdfSignatureFieldBuilder Border(PdfColor color, double width = 1)
    {
        BorderColor = color;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set border style
    /// </summary>
    public PdfSignatureFieldBuilder BorderStyled(PdfBorderStyle style, double width = 1)
    {
        BorderStyle = style;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set a dashed border with a custom pattern
    /// </summary>
    public PdfSignatureFieldBuilder BorderDashed(double dashLength = 3, double gapLength = 3, double width = 1)
    {
        BorderStyle = PdfBorderStyle.Dashed;
        DashPattern = [dashLength, gapLength];
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set background color
    /// </summary>
    public PdfSignatureFieldBuilder Background(PdfColor color)
    {
        BackgroundColor = color;
        return this;
    }
}
