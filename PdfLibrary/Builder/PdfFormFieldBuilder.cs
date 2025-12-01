namespace PdfLibrary.Builder;

/// <summary>
/// Base class for form field builders
/// </summary>
public abstract class PdfFormFieldBuilder(string name, PdfRect rect)
{
    /// <summary>
    /// Field name (used as the field identifier in the PDF)
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Field rectangle in PDF coordinates
    /// </summary>
    public PdfRect Rect { get; protected set; } = rect;

    /// <summary>
    /// Tooltip text shown on hover
    /// </summary>
    public string? Tooltip { get; protected set; }

    /// <summary>
    /// Whether the field is read-only
    /// </summary>
    public bool IsReadOnly { get; protected set; }

    /// <summary>
    /// Whether the field is required
    /// </summary>
    public bool IsRequired { get; protected set; }

    /// <summary>
    /// Whether the field should not be exported
    /// </summary>
    public bool NoExport { get; protected set; }

    /// <summary>
    /// Border color
    /// </summary>
    public PdfColor? BorderColor { get; protected set; }

    /// <summary>
    /// Background color
    /// </summary>
    public PdfColor? BackgroundColor { get; protected set; }

    /// <summary>
    /// Border width in points
    /// </summary>
    public double BorderWidth { get; protected set; } = 1;

    /// <summary>
    /// Border style
    /// </summary>
    public PdfBorderStyle BorderStyle { get; protected set; } = PdfBorderStyle.Solid;

    /// <summary>
    /// Dash pattern for dashed borders (dash length, gap length)
    /// </summary>
    public double[]? DashPattern { get; protected set; }
}

/// <summary>
/// Border styles for form fields
/// </summary>
public enum PdfBorderStyle
{
    /// <summary>
    /// Solid border (default)
    /// </summary>
    Solid,

    /// <summary>
    /// Dashed border
    /// </summary>
    Dashed,

    /// <summary>
    /// 3D beveled border (raised appearance)
    /// </summary>
    Beveled,

    /// <summary>
    /// 3D inset border (sunken appearance)
    /// </summary>
    Inset,

    /// <summary>
    /// Single line at bottom (underline style)
    /// </summary>
    Underline
}

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
        var rect = PdfRect.FromInches(left, top, size, size, pageHeight);
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

/// <summary>
/// Radio button option
/// </summary>
public class PdfRadioOption
{
    public string Value { get; set; } = "";
    public PdfRect Rect { get; set; }
}

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
        foreach (var value in values)
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

/// <summary>
/// Dropdown option
/// </summary>
public class PdfDropdownOption
{
    public string Value { get; set; } = "";
    public string DisplayText { get; set; } = "";
}

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

/// <summary>
/// Text alignment options
/// </summary>
public enum PdfTextAlignment
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// <summary>
/// Check mark styles for checkboxes and radio buttons
/// </summary>
public enum PdfCheckStyle
{
    Check,
    Circle,
    Cross,
    Diamond,
    Square,
    Star
}
