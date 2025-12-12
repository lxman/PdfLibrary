namespace PdfLibrary.Builder;

/// <summary>
/// Builder for document-level AcroForm settings
/// </summary>
public class PdfAcroFormBuilder
{
    internal string DefaultFont { get; private set; } = "Helvetica";
    internal double DefaultFontSize { get; private set; } = 10;
    internal bool NeedAppearances { get; private set; } = true;

    /// <summary>
    /// Set the default font and size for all form fields in the document.
    /// Form fields that don't specify their own font will use this default.
    /// </summary>
    /// <param name="fontName">Font name (e.g., "Helvetica", "Times-Roman", "Courier")</param>
    /// <param name="fontSize">Font size in points (default: 10)</param>
    /// <returns>The AcroForm builder for chaining</returns>
    /// <example>
    /// <code>
    /// .WithAcroForm(form => form
    ///     .SetDefaultFont("Helvetica", 12))
    /// </code>
    /// </example>
    public PdfAcroFormBuilder SetDefaultFont(string fontName, double fontSize = 10)
    {
        DefaultFont = fontName;
        DefaultFontSize = fontSize;
        return this;
    }

    /// <summary>
    /// Set the NeedAppearances flag which tells PDF viewers whether to generate
    /// field appearances on-the-fly. When true (default), viewers dynamically generate
    /// field appearances based on field values. When false, the PDF must contain
    /// pre-generated appearance streams for each field.
    /// </summary>
    /// <param name="value">True to have viewers generate appearances, false to use embedded appearances</param>
    /// <returns>The AcroForm builder for chaining</returns>
    /// <remarks>
    /// Setting this to true (default) allows viewers to render fields with their current values
    /// even if appearance streams are missing. This is the recommended setting for most use cases.
    /// </remarks>
    /// <example>
    /// <code>
    /// .WithAcroForm(form => form
    ///     .SetDefaultFont("Helvetica", 10)
    ///     .SetNeedAppearances(true))
    /// </code>
    /// </example>
    public PdfAcroFormBuilder SetNeedAppearances(bool value)
    {
        NeedAppearances = value;
        return this;
    }
}