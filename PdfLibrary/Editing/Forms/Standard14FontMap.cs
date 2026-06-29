namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Maps a standard-14 <c>/DA</c> resource name (Helv, HeBo, TiRo, Cour, …) to its <c>/BaseFont</c>.
/// Shared by the editing-path appearance resolver and the builder writer so both honour the same
/// cross-repo contract. Unknown names fall back to Helvetica.
/// </summary>
internal static class Standard14FontMap
{
    public static string BaseFont(string? daName) => daName switch
    {
        "Helv" => "Helvetica",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "HeBO" => "Helvetica-BoldOblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "TiBI" => "Times-BoldItalic",
        "Cour" => "Courier",
        "CoBo" => "Courier-Bold",
        "CoOb" => "Courier-Oblique",
        "CoBO" => "Courier-BoldOblique",
        "Symb" => "Symbol",
        "Symbol" => "Symbol",
        "ZaDb" => "ZapfDingbats",
        _ => "Helvetica",
    };
}
