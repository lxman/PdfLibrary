using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Builder for dropdown (combo box) fields
/// </summary>
public class PdfDropdownBuilder(string name, PdfRect rect) : PdfFormFieldBuilder(name, rect)
{
    private readonly List<PdfDropdownOption> _options = [];

    /// <summary>
    /// Dropdown options
    /// </summary>
    public IReadOnlyList<PdfDropdownOption> Options => _options;

    /// <summary>
    /// Selected value
    /// </summary>
    public string? SelectedValue { get; private set; }

    /// <summary>
    /// Whether to allow custom text entry
    /// </summary>
    public bool AllowEdit { get; private set; }

    /// <summary>
    /// Whether to sort options alphabetically
    /// </summary>
    public bool Sort { get; private set; }

    /// <summary>
    /// Font name
    /// </summary>
    public string FontName { get; private set; } = "Helvetica";

    /// <summary>
    /// Font size (0 = auto)
    /// </summary>
    public double FontSize { get; private set; }

    /// <summary>
    /// Text color
    /// </summary>
    public PdfColor TextColor { get; private set; } = PdfColor.Black;

    /// <summary>
    /// Add an option
    /// </summary>
    public PdfDropdownBuilder AddOption(string value, string? displayText = null)
    {
        _options.Add(new PdfDropdownOption { Value = value, DisplayText = displayText ?? value });
        return this;
    }

    /// <summary>
    /// Add multiple options
    /// </summary>
    public PdfDropdownBuilder AddOptions(params string[] values)
    {
        foreach (string value in values)
        {
            _options.Add(new PdfDropdownOption { Value = value, DisplayText = value });
        }
        return this;
    }

    /// <summary>
    /// Set the default selected value
    /// </summary>
    public PdfDropdownBuilder Select(string value)
    {
        SelectedValue = value;
        return this;
    }

    /// <summary>
    /// Allow custom text entry
    /// </summary>
    public PdfDropdownBuilder Editable(bool editable = true)
    {
        AllowEdit = editable;
        return this;
    }

    /// <summary>
    /// Sort options alphabetically
    /// </summary>
    public PdfDropdownBuilder Sorted(bool sorted = true)
    {
        Sort = sorted;
        return this;
    }

    /// <summary>
    /// Set font
    /// </summary>
    public PdfDropdownBuilder Font(string fontName, double fontSize = 0)
    {
        FontName = fontName;
        FontSize = fontSize;
        return this;
    }

    /// <summary>
    /// Set text color
    /// </summary>
    public PdfDropdownBuilder Color(PdfColor color)
    {
        TextColor = color;
        return this;
    }

    /// <summary>
    /// Make field required
    /// </summary>
    public PdfDropdownBuilder Required(bool required = true)
    {
        IsRequired = required;
        return this;
    }

    /// <summary>
    /// Make the field read-only
    /// </summary>
    public PdfDropdownBuilder ReadOnly(bool readOnly = true)
    {
        IsReadOnly = readOnly;
        return this;
    }

    /// <summary>
    /// Set tooltip text
    /// </summary>
    public PdfDropdownBuilder WithTooltip(string tooltip)
    {
        Tooltip = tooltip;
        return this;
    }

    /// <summary>
    /// Set border appearance
    /// </summary>
    public PdfDropdownBuilder Border(PdfColor color, double width = 1)
    {
        BorderColor = color;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set border style
    /// </summary>
    public PdfDropdownBuilder BorderStyled(PdfBorderStyle style, double width = 1)
    {
        BorderStyle = style;
        BorderWidth = width;
        return this;
    }

    /// <summary>
    /// Set background color
    /// </summary>
    public PdfDropdownBuilder Background(PdfColor color)
    {
        BackgroundColor = color;
        return this;
    }
}