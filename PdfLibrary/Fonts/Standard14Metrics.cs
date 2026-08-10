namespace PdfLibrary.Fonts;

/// <summary>
/// Adobe AFM advance-width metrics for the Standard-14 fonts (Helvetica/Times/Courier families,
/// plus Symbol and ZapfDingbats resolved by glyph name), plus /BaseFont-name normalisation
/// (ISO 32000-1 §9.6.2.1 / §9.10.1).
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
    /// <para>Symbol and ZapfDingbats return null here (by-code lookups only — the by-NAME arm is not
    /// gated by this check; see the caveat on <see cref="CodeAliases"/>'s call site below and
    /// TrueTypeFont.cs, which defaults /Encoding to WinAnsi and tries WidthByName first regardless of
    /// /BaseFont): their built-in encodings are not WinAnsi, so a raw code carries no information
    /// about which glyph is intended. This guard IS load-bearing for the by-code path —
    /// <see cref="WidthByName"/> would otherwise happily answer a WinAnsi code that happens to
    /// collide with a Symbol/ZapfDingbats glyph name (e.g. "space").</para>
    /// </summary>
    public static double? WidthByCode(string? baseFont, int charCode)
    {
        string? canonical = CanonicalName(baseFont);
        if (canonical is "Symbol" or "ZapfDingbats")
            return null;

        // WinAnsiEncoding is incomplete: a handful of codes either resolve to a name the AFMs don't
        // use (0xA0 "nonbreakingspace", where the AFM glyph is "space") or resolve to nothing at all
        // (0xAD, 0x88, 0x98, 0xB5). CodeAliases overrides those five with the AFM-recognised glyph
        // name; the actual widths still come from WidthByName per face, so a face missing one of
        // these glyphs still returns null rather than a fabricated value.
        string? glyphName = CodeAliases.TryGetValue(charCode, out string? alias)
            ? alias
            : WinAnsi.GetGlyphName(charCode);
        return WidthByName(baseFont, glyphName);
    }

    /// <summary>
    /// WinAnsi code → AFM glyph name overrides for codes where <see cref="PdfFontEncoding"/>'s
    /// WinAnsi table either names a glyph the AFMs don't recognise, or names none at all. Local to
    /// <see cref="WidthByCode"/> deliberately: widening the shared encoding table would change glyph
    /// resolution for the whole renderer, a far larger blast radius than this by-code AFM fallback
    /// owns.
    /// </summary>
    private static readonly Dictionary<int, string> CodeAliases = new()
    {
        [0xA0] = "space",       // no-break space; WinAnsi names it "nonbreakingspace", not an AFM glyph
        [0xAD] = "hyphen",      // soft hyphen; WinAnsi has no name for this code
        [0x88] = "circumflex",  // WinAnsi has no name for this code
        [0x98] = "tilde",       // WinAnsi has no name for this code
        [0xB5] = "mu",          // micro sign; WinAnsi has no name for this code
    };

    private static readonly PdfFontEncoding WinAnsi =
        PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
}
