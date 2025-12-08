namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Builder for radio button groups
/// </summary>
public class PdfRadioGroupBuilder(string name, double pageHeight) : PdfFormFieldBuilder(name, default)
{
    private readonly List<PdfRadioOption> _options = [];

    /// <summary>
    /// Radio button options
    /// </summary>
    public IReadOnlyList<PdfRadioOption> Options => _options;

    /// <summary>
    /// Selected option value
    /// </summary>
    public string? SelectedValue { get; private set; }

    /// <summary>
    /// Whether to force selection (no deselect)
    /// </summary>
    public bool NoToggleToOff { get; private set; }

    /// <summary>
    /// Check mark style for radio buttons
    /// </summary>
    public PdfCheckStyle RadioStyle { get; private set; } = PdfCheckStyle.Circle;

    /// <summary>
    /// Add a radio button option
    /// </summary>
    public PdfRadioGroupBuilder AddOption(string value, PdfRect rect)
    {
        _options.Add(new PdfRadioOption { Value = value, Rect = rect });
        return this;
    }

    /// <summary>
    /// Add a radio button option with position in inches
    /// </summary>
    public PdfRadioGroupBuilder AddOptionInches(string value, double left, double top, double size = 0.15)
    {
        PdfRect rect = PdfRect.FromInches(left, top, size, size, pageHeight);
        return AddOption(value, rect);
    }

    /// <summary>
    /// Set the default selected value
    /// </summary>
    public PdfRadioGroupBuilder Select(string value)
    {
        SelectedValue = value;
        return this;
    }

    /// <summary>
    /// Prevent deselecting all options
    /// </summary>
    public PdfRadioGroupBuilder NoToggleOff(bool noToggle = true)
    {
        NoToggleToOff = noToggle;
        return this;
    }

    /// <summary>
    /// Set radio button style
    /// </summary>
    public PdfRadioGroupBuilder Style(PdfCheckStyle style)
    {
        RadioStyle = style;
        return this;
    }

    /// <summary>
    /// Make field required
    /// </summary>
    public PdfRadioGroupBuilder Required(bool required = true)
    {
        IsRequired = required;
        return this;
    }

    /// <summary>
    /// Make field read-only
    /// </summary>
    public PdfRadioGroupBuilder ReadOnly(bool readOnly = true)
    {
        IsReadOnly = readOnly;
        return this;
    }

    /// <summary>
    /// Set tooltip text
    /// </summary>
    public PdfRadioGroupBuilder WithTooltip(string tooltip)
    {
        Tooltip = tooltip;
        return this;
    }
}