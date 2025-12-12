namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Radio button option
/// </summary>
public class PdfRadioOption
{
    /// <summary>
    /// The value associated with this radio button when selected.
    /// In a radio button group, each option should have a unique value to identify which option was chosen.
    /// </summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// The position and size of the radio button on the page.
    /// Defines where the clickable radio button circle will be rendered.
    /// </summary>
    public PdfRect Rect { get; set; }
}