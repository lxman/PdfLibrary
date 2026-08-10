namespace PdfLibrary.Fonts;

/// <summary>
/// Adobe AFM advance-width metrics for the Standard-14 fonts (Helvetica/Times/Courier families),
/// plus /BaseFont-name normalisation (ISO 32000-1 §9.6.2.1 / §9.10.1).
///
/// A non-embedded simple font (Type1 or TrueType) may legally omit its /Widths array; the viewer
/// must then lay the text out with the AFM metrics of the Standard-14 font the /BaseFont maps to.
/// The mapping covers the Windows aliases producers emit — TimesNewRoman→Times-Roman,
/// Arial,Bold→Helvetica-Bold, CourierNew→Courier — as well as the canonical PostScript names.
/// Shared by <see cref="TrueTypeFont"/> and <see cref="Type1Font"/>.
/// </summary>
internal static class Standard14Metrics
{
    /// <summary>
    /// Canonical Standard-14 PostScript name for <paramref name="baseFont"/> (e.g. "Times-Bold"), or
    /// null when the name is not a recognised Standard-14 family. Strips a subset tag ("ABCDEF+"),
    /// splits "Family-Style"/"Family,Style", and folds bold/italic(oblique) style flags.
    /// </summary>
    public static string? CanonicalName(string? baseFont)
    {
        if (string.IsNullOrWhiteSpace(baseFont))
            return null;

        string name = baseFont;
        int plus = name.IndexOf('+');
        if (plus == 6) name = name[(plus + 1)..];          // strip "ABCDEF+" subset tag

        string core = name, style = string.Empty;
        int sep = name.IndexOfAny(['-', ',']);
        if (sep >= 0)
        {
            core = name[..sep];
            style = name[(sep + 1)..];
        }

        string c = core.Replace(" ", string.Empty).ToLowerInvariant();
        string s = style.Replace(" ", string.Empty).ToLowerInvariant();
        bool bold = s.Contains("bold");
        bool italic = s.Contains("italic") || s.Contains("oblique");

        return c switch
        {
            "times" or "timesroman" or "timesnewroman" or "timesnewromanps" or "timesnewromanpsmt" =>
                (bold, italic) switch
                {
                    (true, true) => "Times-BoldItalic",
                    (true, false) => "Times-Bold",
                    (false, true) => "Times-Italic",
                    _ => "Times-Roman"
                },
            "helvetica" or "arial" or "arialmt" or "helv" =>
                (bold, italic) switch
                {
                    (true, true) => "Helvetica-BoldOblique",
                    (true, false) => "Helvetica-Bold",
                    (false, true) => "Helvetica-Oblique",
                    _ => "Helvetica"
                },
            "courier" or "couriernew" or "couriernewps" or "couriernewpsmt" =>
                (bold, italic) switch
                {
                    (true, true) => "Courier-BoldOblique",
                    (true, false) => "Courier-Bold",
                    (false, true) => "Courier-Oblique",
                    _ => "Courier"
                },
            "symbol" => "Symbol",
            "zapfdingbats" or "dingbats" => "ZapfDingbats",
            _ => null
        };
    }

    /// <summary>
    /// AFM advance width (1000-unit em) for the glyph named <paramref name="glyphName"/> in the
    /// Standard-14 font <paramref name="baseFont"/> maps to. Null when no glyph name is supplied,
    /// the base font is not a recognised Standard-14 family, or the glyph name is not present in
    /// that face's vendored AFM (see <see cref="AfmMetrics.ForFace"/>).
    /// </summary>
    public static double? WidthByName(string? baseFont, string? glyphName)
    {
        if (string.IsNullOrEmpty(glyphName))
            return null;
        string? canonical = CanonicalName(baseFont);
        if (canonical is null)
            return null;

        // Courier is monospaced — all 315 glyphs are 600 — so it needs no vendored AFM.
        if (canonical.StartsWith("Courier", StringComparison.Ordinal))
            return 600;

        // The Oblique faces are width-identical to their upright counterparts, verified against the
        // AFMs, so they share a table rather than duplicating one.
        string face = canonical switch
        {
            "Helvetica-Oblique" => "Helvetica",
            "Helvetica-BoldOblique" => "Helvetica-Bold",
            _ => canonical,
        };

        return AfmMetrics.ForFace(face) is { } widths && widths.TryGetValue(glyphName, out double w)
            ? w
            : null;
    }

    /// <summary>
    /// AFM advance width by raw character code, used only when no glyph name is available (no
    /// /Encoding). Interprets the code as **WinAnsi** — which is what the previous hand-written
    /// tables did — and resolves the resulting glyph name through <see cref="WidthByName"/>, so the
    /// two paths cannot drift apart the way they did before. A null or not-a-Standard-14
    /// <paramref name="baseFont"/> and an unmapped glyph name are not re-checked here:
    /// <see cref="WidthByName"/> already rejects both, so duplicating those checks would be dead
    /// code no test could ever observe.
    ///
    /// <para>Symbol and ZapfDingbats return null: their built-in encodings are not WinAnsi, so a raw
    /// code carries no information about which glyph is intended. They resolve by NAME only. This
    /// guard IS load-bearing — <see cref="WidthByName"/> would otherwise happily answer a WinAnsi
    /// code that happens to collide with a Symbol/ZapfDingbats glyph name (e.g. "space").</para>
    /// </summary>
    public static double? WidthByCode(string? baseFont, int charCode)
    {
        string? canonical = CanonicalName(baseFont);
        if (canonical is "Symbol" or "ZapfDingbats")
            return null;

        string? glyphName = WinAnsi.GetGlyphName(charCode);
        return glyphName == ".notdef" ? null : WidthByName(baseFont, glyphName);
    }

    private static readonly PdfFontEncoding WinAnsi =
        PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
}
