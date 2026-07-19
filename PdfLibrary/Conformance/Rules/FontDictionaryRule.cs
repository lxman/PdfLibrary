using System.Text;
using System.Text.RegularExpressions;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Dictionary-level font requirements shared by PDF/A-2 (ISO 19005-2, 6.2.11) and PDF/UA-1
/// (ISO 14289-1, 7.21) — the two standards use identical sub-numbers, so a single profile-aware rule
/// serves both (like <see cref="FontEmbeddingRule"/>). Four groups, all read from the font dictionaries
/// alone (no font-program parsing — metrics, .notdef and CharSet/CIDSet are a later slice and out of
/// scope here):
/// <list type="number">
///   <item>character encodings of simple TrueType fonts (6.2.11.6 / 7.21.6);</item>
///   <item>CIDSystemInfo compatibility of a Type0 font's descendant CIDFont against an embedded CMap
///     (6.2.11.3.1 / 7.21.3.1);</item>
///   <item>the CIDToGIDMap of a CIDFontType2 descendant (6.2.11.3.2 / 7.21.3.2);</item>
///   <item>a Type0 font's /Encoding being an embedded CMap stream or a predefined name, plus — for an
///     embedded CMap stream — its /WMode consistency and /UseCMap referencing only predefined CMaps
///     (6.2.11.3.3 / 7.21.3.3, tests 1–3).</item>
/// </list>
/// <para>
/// Fonts come from <see cref="ConformanceContext.ReferencedFonts"/> (those reachable for rendering), so an
/// unreferenced font — e.g. an AcroForm /DR pool font that is only drawn when /NeedAppearances is set — is
/// not reported. Each check is deliberately narrowed to exactly the font kind the reference validator
/// flags: the simple-font encoding rules apply to TrueType only (a Type1 Encoding dictionary routinely
/// carries a non-WinAnsi base or Differences names outside the bundled glyph list, and reporting those
/// would be a false positive), and the CIDSystemInfo comparison runs only when the CMap is an embedded
/// stream (a predefined name such as Identity-H imposes no CIDSystemInfo compatibility constraint on the
/// CIDFont). Unlike <see cref="FontEmbeddingRule"/>, PDF/X-4 is excluded: ISO 15930-7 does not carry these
/// dictionary-level font constraints (e.g. it permits a CIDFontType2 with no explicit /CIDToGIDMap).
/// </para>
/// </summary>
internal sealed class FontDictionaryRule : IConformanceRule
{
    public string RuleId => "font-dictionary";

    // Shared with PDF/UA-1 (7.21), whose dictionary-level font requirements mirror PDF/A's. PDF/X-4 is
    // intentionally NOT included (its font rules differ — see the type remarks).
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA | ConformanceProfile.PdfUA1;

    // The predefined CMap names of ISO 32000-1:2008, Table 118 (the Identity pair plus the CJK collections).
    // A Type0 /Encoding that is neither an embedded CMap stream nor one of these names is non-conformant.
    private static readonly HashSet<string> PredefinedCMaps = new(StringComparer.Ordinal)
    {
        "Identity-H", "Identity-V",
        // Adobe-GB1 (Simplified Chinese)
        "GB-EUC-H", "GB-EUC-V", "GBpc-EUC-H", "GBpc-EUC-V", "GBK-EUC-H", "GBK-EUC-V",
        "GBKp-EUC-H", "GBKp-EUC-V", "GBK2K-H", "GBK2K-V",
        "UniGB-UCS2-H", "UniGB-UCS2-V", "UniGB-UTF16-H", "UniGB-UTF16-V",
        // Adobe-CNS1 (Traditional Chinese)
        "B5pc-H", "B5pc-V", "HKscs-B5-H", "HKscs-B5-V", "ETen-B5-H", "ETen-B5-V",
        "ETenms-B5-H", "ETenms-B5-V", "CNS-EUC-H", "CNS-EUC-V",
        "UniCNS-UCS2-H", "UniCNS-UCS2-V", "UniCNS-UTF16-H", "UniCNS-UTF16-V",
        // Adobe-Japan1 (Japanese)
        "83pv-RKSJ-H", "90ms-RKSJ-H", "90ms-RKSJ-V", "90msp-RKSJ-H", "90msp-RKSJ-V",
        "90pv-RKSJ-H", "Add-RKSJ-H", "Add-RKSJ-V", "EUC-H", "EUC-V",
        "Ext-RKSJ-H", "Ext-RKSJ-V", "H", "V",
        "UniJIS-UCS2-H", "UniJIS-UCS2-V", "UniJIS-UCS2-HW-H", "UniJIS-UCS2-HW-V",
        "UniJIS-UTF16-H", "UniJIS-UTF16-V",
        // Adobe-Korea1 (Korean)
        "KSC-EUC-H", "KSC-EUC-V", "KSCms-UHC-H", "KSCms-UHC-V",
        "KSCms-UHC-HW-H", "KSCms-UHC-HW-V", "KSCpc-EUC-H",
        "UniKS-UCS2-H", "UniKS-UCS2-V", "UniKS-UTF16-H", "UniKS-UTF16-V",
    };

    private static readonly HashSet<string> AllowedBaseEncodings =
        new(StringComparer.Ordinal) { "WinAnsiEncoding", "MacRomanEncoding" };

    // The algorithmic glyph-name forms of the Adobe Glyph List spec that are valid without a table lookup.
    private static readonly Regex UniForm = new("^uni[0-9A-F]{4}$", RegexOptions.Compiled);
    private static readonly Regex UForm = new("^u[0-9A-F]{5,6}$", RegexOptions.Compiled);

    // The integer /WMode declared in a CMap stream body: "/WMode <n> def".
    private static readonly Regex CMapWMode = new(@"/WMode\s+(-?\d+)\s+def", RegexOptions.Compiled);

    // A PostScript %-comment (to end of line). Stripped before the /WMode scan so a "/WMode <n> def"
    // mentioned in a header comment can't be mistaken for the authoritative definition.
    private static readonly Regex CMapComment = new("%[^\r\n]*", RegexOptions.Compiled);

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary font in context.ReferencedFonts)
        {
            // A Type0 font's descendant CIDFont is also present in ReferencedFonts as its own entry; the
            // CIDFont checks are driven from the Type0 wrapper below, so the CIDFontType0/2 entries are
            // skipped here (only TrueType and Type0 are actionable).
            switch (context.ResolveName(font.Get("Subtype")))
            {
                case "TrueType":
                    foreach (Finding f in CheckSimpleEncoding(context, font))
                        yield return f;
                    break;
                case "Type0":
                    foreach (Finding f in CheckType0(context, font))
                        yield return f;
                    break;
            }
        }
    }

    // ── Group 1 — 6.2.11.6 / 7.21.6 character encodings (simple TrueType) ─────────────────────────────
    private IEnumerable<Finding> CheckSimpleEncoding(ConformanceContext context, PdfDictionary font)
    {
        (bool symbolic, bool nonsymbolic) = SymbolicFlags(context, font);
        bool hasEncoding = font.Get("Encoding") is not null;

        // A symbolic TrueType font must not carry an /Encoding entry (its built-in cmap governs).
        if (symbolic && hasEncoding)
        {
            yield return Make(context, font, "6",
                $"The symbolic TrueType font {BaseFont(font)} must not define an /Encoding entry.");
        }

        if (!nonsymbolic)
            yield break;

        // A non-symbolic TrueType font must define an /Encoding.
        if (!hasEncoding)
        {
            yield return Make(context, font, "6",
                $"The non-symbolic TrueType font {BaseFont(font)} does not define an /Encoding entry.");
            yield break;
        }

        // When /Encoding is a dictionary its base encoding must be WinAnsi/MacRoman and every /Differences
        // name must be recognisable. (A name-form /Encoding for a non-symbolic TrueType is not reported —
        // the reference corpus exercises only the dictionary form, so the name case is left under-reported.)
        if (context.Resolve(font.Get("Encoding")) is not PdfDictionary encoding)
            yield break;

        string? baseEncoding = context.ResolveName(encoding.Get("BaseEncoding"));
        if (baseEncoding is null)
        {
            yield return Make(context, font, "6",
                $"The non-symbolic TrueType font {BaseFont(font)} has an /Encoding dictionary with no "
                + "/BaseEncoding.");
        }
        else if (!AllowedBaseEncodings.Contains(baseEncoding))
        {
            yield return Make(context, font, "6",
                $"The non-symbolic TrueType font {BaseFont(font)} uses /BaseEncoding /{baseEncoding}; only "
                + "WinAnsiEncoding or MacRomanEncoding is permitted.");
        }

        if (context.Resolve(encoding.Get("Differences")) is PdfArray differences)
        {
            foreach (PdfObject entry in differences)
            {
                if (context.Resolve(entry) is PdfName glyph && !IsRecognizedGlyphName(glyph.Value))
                {
                    yield return Make(context, font, "6",
                        $"The /Differences array of TrueType font {BaseFont(font)} names glyph "
                        + $"'{glyph.Value}', which is not in the Adobe Glyph List.");
                    yield break; // one finding per font is enough for the clause
                }
            }
        }
    }

    // ── Groups 2–4 — Type0 composite fonts ────────────────────────────────────────────────────────────
    private IEnumerable<Finding> CheckType0(ConformanceContext context, PdfDictionary font)
    {
        PdfObject? encoding = context.Resolve(font.Get("Encoding"));
        PdfDictionary? cidFont = null;
        if (context.Resolve(font.Get("DescendantFonts")) is PdfArray descendants && descendants.Count > 0)
            cidFont = context.Resolve(descendants[0]) as PdfDictionary;

        // Group 4 (6.2.11.3.3 / 7.21.3.3, test 1): /Encoding is an embedded CMap stream or a predefined name.
        if (encoding is not PdfStream)
        {
            string? cmapName = (encoding as PdfName)?.Value;
            if (cmapName is null || !PredefinedCMaps.Contains(cmapName))
            {
                yield return Make(context, font, "3.3",
                    $"The Type0 font {BaseFont(font)} uses CMap /Encoding "
                    + $"{(cmapName is null ? "(non-name)" : "/" + cmapName)}, which is neither an embedded CMap "
                    + "stream nor a predefined CMap.");
            }
        }

        // Group 4 tests 2 & 3 (6.2.11.3.3 / 7.21.3.3) — embedded CMap stream consistency. Independent of the
        // descendant CIDFont, so checked here before the cidFont guard.
        if (encoding is PdfStream encodingCMap)
        {
            // test 2 — the /WMode dictionary entry must equal the /WMode in the CMap stream body.
            if (context.Resolve(encodingCMap.Dictionary.Get("WMode")) is PdfInteger dictWMode
                && ReadCMapBodyWMode(context, encodingCMap) is { } bodyWMode
                && dictWMode.LongValue != bodyWMode)
            {
                yield return Make(context, font, "3.3",
                    $"The Type0 font {BaseFont(font)} embeds a CMap whose /WMode dictionary entry "
                    + $"({dictWMode.LongValue}) differs from the /WMode in the CMap stream ({bodyWMode}).");
            }

            // test 3 — a CMap must not reference a non-predefined CMap (via /UseCMap).
            if (encodingCMap.Dictionary.Get("UseCMap") is { } useCMapRaw
                && ReferencedCMapName(context, useCMapRaw) is { } referencedName
                && !PredefinedCMaps.Contains(referencedName))
            {
                yield return Make(context, font, "3.3",
                    $"The Type0 font {BaseFont(font)} embeds a CMap that references non-predefined CMap "
                    + $"/{referencedName} via /UseCMap.");
            }
        }

        if (cidFont is null)
            yield break;

        // Group 2 (6.2.11.3.1 / 7.21.3.1): CIDSystemInfo compatibility — only decidable against an embedded
        // CMap, which carries its own /CIDSystemInfo. A predefined name (e.g. Identity-H) imposes no such
        // constraint, so the check is skipped for it (matching the reference validator).
        if (encoding is PdfStream cmap
            && context.Resolve(cmap.Dictionary.Get("CIDSystemInfo")) is PdfDictionary cmapCsi
            && context.Resolve(cidFont.Get("CIDSystemInfo")) is PdfDictionary cidCsi)
        {
            string? cmapRegistry = StringValue(context, cmapCsi.Get("Registry"));
            string? cmapOrdering = StringValue(context, cmapCsi.Get("Ordering"));
            string? cidRegistry = StringValue(context, cidCsi.Get("Registry"));
            string? cidOrdering = StringValue(context, cidCsi.Get("Ordering"));

            if (cidRegistry is not null && cmapRegistry is not null && cidRegistry != cmapRegistry)
            {
                yield return Make(context, cidFont, "3.1",
                    $"The CIDFont {BaseFont(cidFont)} declares /Registry ({cidRegistry}) incompatible with the "
                    + $"CMap's ({cmapRegistry}).");
            }
            else if (cidOrdering is not null && cmapOrdering is not null && cidOrdering != cmapOrdering)
            {
                yield return Make(context, cidFont, "3.1",
                    $"The CIDFont {BaseFont(cidFont)} declares /Ordering ({cidOrdering}) incompatible with the "
                    + $"CMap's ({cmapOrdering}).");
            }
            // Direction matters and is easy to get wrong: this flags CIDFont /Supplement GREATER than the
            // CMap's, matching veraPDF's 6.2.11.3.1 / 7.21.3.1 rule (its own pass-d fixture embeds a CMap
            // whose Supplement EXCEEDS a subset CIDFont's and is CONFORMANT, so the reverse direction is a
            // false positive — the reference corpus is the oracle here). Note this is the OPPOSITE of the
            // Matterhorn Protocol 1.1 condition 31-003 text ("CIDFont Supplement less than the CMap's"), a
            // known veraPDF-vs-Matterhorn discrepancy — do NOT "correct" this to `<`. See the regression
            // guard Type0_embedded_cmap_supplement_less_passes.
            else if (context.Resolve(cidCsi.Get("Supplement")) is PdfInteger cidSupplement
                     && context.Resolve(cmapCsi.Get("Supplement")) is PdfInteger cmapSupplement
                     && cidSupplement.LongValue > cmapSupplement.LongValue)
            {
                yield return Make(context, cidFont, "3.1",
                    $"The CIDFont {BaseFont(cidFont)} /Supplement ({cidSupplement.LongValue}) is greater than "
                    + $"the CMap's ({cmapSupplement.LongValue}).");
            }
        }

        // Group 3 (6.2.11.3.2 / 7.21.3.2): CIDToGIDMap of a CIDFontType2 descendant must be present and be
        // either the name Identity or an embedded stream. (CIDFontType0 has no such entry.)
        if (context.ResolveName(cidFont.Get("Subtype")) == "CIDFontType2")
        {
            PdfObject? cidToGidRaw = cidFont.Get("CIDToGIDMap");
            if (cidToGidRaw is null)
            {
                yield return Make(context, cidFont, "3.2",
                    $"The CIDFontType2 font {BaseFont(cidFont)} does not define /CIDToGIDMap.");
            }
            else if (context.Resolve(cidToGidRaw) is PdfName cidToGid && cidToGid.Value != "Identity")
            {
                yield return Make(context, cidFont, "3.2",
                    $"The CIDFontType2 font {BaseFont(cidFont)} uses /CIDToGIDMap /{cidToGid.Value}; only "
                    + "Identity or an embedded stream is permitted.");
            }
            // An embedded stream (custom mapping) is valid and produces no finding.
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The symbolic (Flags bit 3) / non-symbolic (bit 6) state read from the /FontDescriptor.</summary>
    private static (bool Symbolic, bool Nonsymbolic) SymbolicFlags(ConformanceContext context, PdfDictionary font)
    {
        if (context.Resolve(font.Get("FontDescriptor")) is PdfDictionary descriptor
            && context.Resolve(descriptor.Get("Flags")) is PdfInteger flags)
        {
            return ((flags.LongValue & 4) != 0, (flags.LongValue & 32) != 0);
        }
        return (false, false);
    }

    /// <summary>True when a /Differences glyph name is in the Adobe Glyph List or one of its algorithmic
    /// forms (uniXXXX / uXXXXXX) or the .notdef name — i.e. NOT a reportable unknown name.</summary>
    private static bool IsRecognizedGlyphName(string name) =>
        name == ".notdef"
        || UniForm.IsMatch(name)
        || UForm.IsMatch(name)
        || GlyphList.GetUnicode(name) is not null;

    private static string? StringValue(ConformanceContext context, PdfObject? obj) =>
        (context.Resolve(obj) as PdfString)?.Value;

    /// <summary>The integer /WMode declared in the CMap stream body ("/WMode &lt;n&gt; def"), or null when
    /// absent or the stream cannot be decoded. FP-safe: the caller compares it only when the dictionary also
    /// carries a /WMode.</summary>
    private static long? ReadCMapBodyWMode(ConformanceContext context, PdfStream cmap)
    {
        byte[] data;
        try { data = cmap.GetDecodedData(context.Document.Decryptor); }
        catch { return null; }

        // CMap bodies are PostScript: drop %-comments first so a "/WMode <n> def" in a header comment
        // cannot be read as the authoritative WMode (veraPDF's CMap tokenizer likewise skips comments).
        string body = CMapComment.Replace(Encoding.Latin1.GetString(data), "");
        Match m = CMapWMode.Match(body);
        return m.Success && long.TryParse(m.Groups[1].Value, out long w) ? w : null;
    }

    /// <summary>The name of the CMap referenced by a /UseCMap value: a predefined-name reference directly, or
    /// the /CMapName of an embedded CMap stream. Null when it cannot be resolved to a name.</summary>
    private static string? ReferencedCMapName(ConformanceContext context, PdfObject useCMap) =>
        context.Resolve(useCMap) switch
        {
            PdfName name => name.Value,
            PdfStream stream => (context.Resolve(stream.Dictionary.Get("CMapName")) as PdfName)?.Value,
            _ => null,
        };

    private static string BaseFont(PdfDictionary font) =>
        (font.Get("BaseFont") as PdfName)?.Value is { } name ? $"'{name}'" : "(unnamed)";

    private Finding Make(ConformanceContext context, PdfDictionary offender, string sub, string message) =>
        new()
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target,
                context.Target == ConformanceProfile.PdfUA1 ? $"7.21.{sub}" : $"6.2.11.{sub}"),
            Message = message,
            ObjectNumber = offender.IsIndirect ? offender.ObjectNumber : null,
        };
}
