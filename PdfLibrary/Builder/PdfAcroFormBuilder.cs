namespace PdfLibrary.Builder;

/// <summary>
/// Builder for document-level AcroForm settings
/// </summary>
public class PdfAcroFormBuilder
{
    internal string DefaultFont { get; private set; } = "Helvetica";
    internal double DefaultFontSize { get; private set; } = 10;
    internal bool NeedAppearances { get; private set; } = true;

    public PdfAcroFormBuilder SetDefaultFont(string fontName, double fontSize = 10)
    {
        DefaultFont = fontName;
        DefaultFontSize = fontSize;
        return this;
    }

    public PdfAcroFormBuilder SetNeedAppearances(bool value)
    {
        NeedAppearances = value;
        return this;
    }
}