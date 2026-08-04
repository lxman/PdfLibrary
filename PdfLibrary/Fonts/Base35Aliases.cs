namespace PdfLibrary.Fonts;

/// <summary>Maps a PDF /BaseFont family onto the internal family names that could satisfy it.
///
/// <para>The table is the PostScript base-35 alias set, taken from Ghostscript's
/// <c>Resource/Init/Fontmap.GS</c> — the de-facto reference every renderer follows. Without it
/// <c>NewCenturySchlbk-Italic</c> falls through to a Times italic even on machines that have the real
/// New Century Schoolbook clone installed, which was measured on two of the three CI boxes.</para></summary>
internal static class Base35Aliases
{
    private static readonly Dictionary<string, string[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["times"] = ["Nimbus Roman", "Liberation Serif", "Times New Roman", "Times", "Tinos"],
        ["timesroman"] = ["Nimbus Roman", "Liberation Serif", "Times New Roman", "Times", "Tinos"],
        ["timesnewroman"] = ["Times New Roman", "Liberation Serif", "Nimbus Roman", "Tinos"],
        ["helvetica"] = ["Nimbus Sans", "Liberation Sans", "Arial", "Helvetica", "Arimo"],
        ["arial"] = ["Arial", "Liberation Sans", "Nimbus Sans", "Arimo"],
        ["courier"] = ["Nimbus Mono PS", "Liberation Mono", "Courier New", "Courier", "Cousine"],
        ["couriernew"] = ["Courier New", "Liberation Mono", "Nimbus Mono PS", "Cousine"],
        ["newcenturyschlbk"] = ["C059", "Century Schoolbook L", "New Century Schoolbook", "Century Schoolbook"],
        ["centuryschoolbook"] = ["C059", "Century Schoolbook L", "Century Schoolbook"],
        ["palatino"] = ["P052", "URW Palladio L", "Palatino Linotype", "Palatino"],
        ["bookman"] = ["URW Bookman", "Bookman Old Style", "Bookman"],
        ["avantgarde"] = ["URW Gothic", "Century Gothic", "AvantGarde"],
        ["zapfchancery"] = ["Z003", "URW Chancery L", "Zapf Chancery"],
        ["symbol"] = ["Symbol", "Standard Symbols PS", "StandardSymbolsPS"],
        ["zapfdingbats"] = ["D050000L", "Dingbats", "ZapfDingbats"],
    };

    /// <summary>Splits a /BaseFont into family and style. Handles the <c>ABCDEF+</c> subset tag and
    /// both the PostScript (<c>Arial-Bold</c>) and Windows (<c>Arial,Bold</c>) style separators.</summary>
    public static (string Family, bool Bold, bool Italic) Split(string baseFont)
    {
        string n = baseFont ?? "";
        if (n.Length > 7 && n[6] == '+') n = n[7..];

        var style = "";
        int sep = n.IndexOfAny(['-', ',']);
        if (sep > 0) { style = n[(sep + 1)..]; n = n[..sep]; }

        bool bold = style.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        bool italic = style.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                   || style.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        return (n, bold, italic);
    }

    /// <summary>Internal family names that could satisfy <paramref name="family"/>, best first. An
    /// unknown family aliases to itself, so a document asking for an installed font gets it.</summary>
    public static IReadOnlyList<string> FamiliesFor(string family)
    {
        string key = (family ?? "").Replace(" ", string.Empty);
        return Table.TryGetValue(key, out string[]? aliases) ? aliases : [family ?? ""];
    }
}
