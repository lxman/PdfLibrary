using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Builder for checkbox fields
/// </summary>
public class PdfCheckboxBuilder(string name, PdfRect rect) : PdfFormFieldBuilder(name, rect)
{
    /// <summary>
    /// Whether the checkbox is checked by default
    /// </summary>
    public bool IsChecked { get; private set; }

    /// <summary>
    /// Export value when checked
    /// </summary>
    public string ExportValue { get; private set; } = "Yes";

    /// <summary>
    /// Check mark style
    /// </summary>
    public PdfCheckStyle CheckStyle { get; private set; } = PdfCheckStyle.Check;

    /// <summary>
    /// Set default checked state
    /// </summary>
    public PdfCheckboxBuilder Checked(bool isChecked = true)
    {
        IsChecked = isChecked;
        return this;
    }

    /// <summary>
    /// Set export value
    /// </summary>
    public PdfCheckboxBuilder WithExportValue(string value)
    {
        ExportValue = value;
        return this;
    }

    /// <summary>
    /// Set check mark style
    /// </summary>
    public PdfCheckboxBuilder Style(PdfCheckStyle style)
    {
        CheckStyle = style;
        return this;
    }

    /// <summary>
    /// Make field required
    /// </summary>
    public PdfCheckboxBuilder Required(bool required = true)
    {
        IsRequired = required;
        return this;
    }

    /// <summary>
    /// Make field read-only
    /// </summary>
    public PdfCheckboxBuilder ReadOnly(bool readOnly = true)
    {
        IsReadOnly = readOnly;
        return this;
    }

    /// <summary>
    /// Set tooltip text
    /// </summary>
    public PdfCheckboxBuilder WithTooltip(string tooltip)
    {
        Tooltip = tooltip;
        return this;
    }

    /// <summary>
    /// Set border appearance
    /// </summary>
    public PdfCheckboxBuilder Border(PdfColor color, double width = 1)
    {
        BorderColor = color;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set border style
    /// </summary>
    public PdfCheckboxBuilder BorderStyled(PdfBorderStyle style, double width = 1)
    {
        BorderStyle = style;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set background color
    /// </summary>
    public PdfCheckboxBuilder Background(PdfColor color)
    {
        BackgroundColor = color;
        return this;
    }
}