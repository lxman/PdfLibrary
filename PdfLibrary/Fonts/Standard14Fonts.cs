namespace PdfLibrary.Fonts;

/// <summary>
/// Maps a PDF <c>/BaseFont</c> name to an ordered list of metric-compatible substitute font
/// file base-names (no extension) to look for among installed system fonts. Ordering is by
/// preference: the OS-native metric clone first (Arial/Times New Roman/Courier New), then the
/// common libre families (Liberation, URW/Nimbus, Arimo/Tinos/Cousine, DejaVu).
/// </summary>
internal static class Standard14Fonts
{
    private enum Family { Sans, Serif, Mono, Symbol, Dingbats }

    private static readonly IReadOnlyList<string> Empty = Array.Empty<string>();

    public static IReadOnlyList<string> SubstituteFileBaseNames(string baseFontName)
    {
        if (string.IsNullOrWhiteSpace(baseFontName))
            return Empty;

        // Strip subset tag "ABCDEF+Name".
        string name = baseFontName;
        int plus = name.IndexOf('+');
        if (plus == 6) name = name[(plus + 1)..];

        // Split "Family-Style" or "Family,Style".
        string core = name;
        string style = string.Empty;
        int sep = name.IndexOfAny(['-', ',']);
        if (sep >= 0)
        {
            core = name[..sep];
            style = name[(sep + 1)..];
        }

        string c = core.Replace(" ", string.Empty).ToLowerInvariant();
        Family? family = c switch
        {
            "helvetica" or "arial" or "arialmt" or "helv" => Family.Sans,
            "times" or "timesroman" or "timesnewroman" or "timesnewromanpsmt" => Family.Serif,
            "courier" or "couriernew" or "couriernewpsmt" => Family.Mono,
            "symbol" => Family.Symbol,
            "zapfdingbats" or "dingbats" => Family.Dingbats,
            _ => null
        };
        if (family is null) return Empty;

        string s = style.Replace(" ", string.Empty).ToLowerInvariant();
        bool bold = s.Contains("bold");
        bool italic = s.Contains("italic") || s.Contains("oblique");

        return family switch
        {
            Family.Sans => Pick(bold, italic,
                ["arial", "LiberationSans-Regular", "NimbusSans-Regular", "Arimo-Regular", "DejaVuSans"],
                ["arialbd", "LiberationSans-Bold", "NimbusSans-Bold", "Arimo-Bold", "DejaVuSans-Bold"],
                ["ariali", "LiberationSans-Italic", "NimbusSans-Italic", "Arimo-Italic", "DejaVuSans-Oblique"],
                ["arialbi", "LiberationSans-BoldItalic", "NimbusSans-BoldItalic", "Arimo-BoldItalic", "DejaVuSans-BoldOblique"]),
            Family.Serif => Pick(bold, italic,
                ["times", "LiberationSerif-Regular", "NimbusRoman-Regular", "Tinos-Regular", "DejaVuSerif"],
                ["timesbd", "LiberationSerif-Bold", "NimbusRoman-Bold", "Tinos-Bold", "DejaVuSerif-Bold"],
                ["timesi", "LiberationSerif-Italic", "NimbusRoman-Italic", "Tinos-Italic", "DejaVuSerif-Italic"],
                ["timesbi", "LiberationSerif-BoldItalic", "NimbusRoman-BoldItalic", "Tinos-BoldItalic", "DejaVuSerif-BoldItalic"]),
            Family.Mono => Pick(bold, italic,
                ["cour", "LiberationMono-Regular", "NimbusMonoPS-Regular", "Cousine-Regular", "DejaVuSansMono"],
                ["courbd", "LiberationMono-Bold", "NimbusMonoPS-Bold", "Cousine-Bold", "DejaVuSansMono-Bold"],
                ["couri", "LiberationMono-Italic", "NimbusMonoPS-Italic", "Cousine-Italic", "DejaVuSansMono-Oblique"],
                ["courbi", "LiberationMono-BoldItalic", "NimbusMonoPS-BoldItalic", "Cousine-BoldItalic", "DejaVuSansMono-BoldOblique"]),
            Family.Symbol => ["Symbol", "StandardSymbolsPS", "StandardSymbolsL"],
            Family.Dingbats => ["ZapfDingbats", "D050000L", "Dingbats"],
            _ => Empty
        };
    }

    private static IReadOnlyList<string> Pick(bool bold, bool italic,
        string[] regular, string[] boldArr, string[] italicArr, string[] boldItalic)
        => (bold, italic) switch
        {
            (true, true) => boldItalic,
            (true, false) => boldArr,
            (false, true) => italicArr,
            _ => regular
        };
}
