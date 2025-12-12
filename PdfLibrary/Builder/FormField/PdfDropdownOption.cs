namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Dropdown option
/// </summary>
public class PdfDropdownOption
{
    /// <summary>
    /// The value that will be stored/submitted when this option is selected.
    /// This is the internal value used by the form field, which may differ from the displayed text.
    /// </summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// The text displayed to the user in the dropdown list.
    /// This is what the user sees when browsing options, which may be more descriptive than the internal value.
    /// </summary>
    public string DisplayText { get; set; } = "";
}