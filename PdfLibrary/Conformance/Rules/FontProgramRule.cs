using System;
using System.Collections.Generic;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Font-program requirements shared by PDF/A-2 (ISO 19005-2, 6.2.11) and PDF/UA-1 (ISO 14289-1, 7.21) —
/// the sub-numbers are identical, so one profile-aware rule serves both (like <see cref="FontDictionaryRule"/>
/// and <see cref="FontEmbeddingRule"/>). Unlike the dictionary-level slice, these checks read the *embedded
/// font program* (via <see cref="EmbeddedFontMetrics"/>) for the glyphs actually shown
/// (<see cref="ConformanceContext.UsedTextGlyphs"/>), matching veraPDF's "used for rendering" scope:
/// <list type="number">
///   <item><b>.notdef glyph (6.2.11.8 / 7.21.8):</b> faithful to veraPDF's own predicate — a shown code
///     whose glyph NAME is literally ".notdef" (encoding lookup only, no program parsing). NOT render-mode
///     exempt ("regardless of text rendering mode"), so this walks every shown code, including RM3
///     (invisible) text. Implemented for Type0 composite fonts with an Identity CMap, where the code equals
///     the CID and the CID→GID map (CIDToGIDMap for CIDFontType2, the CFF charset for CIDFontType0) resolves
///     to glyph/CID 0; and for simple TrueType / simple CFF fonts via the PDF <c>/Encoding</c>'s
///     <c>GetGlyphName</c>.</item>
///   <item><b>glyph-present (6.2.11.4.1 t2 / 7.21.4.1 t2):</b> a shown code whose (non-".notdef") glyph is
///     confidently absent from the embedded font program — near-mutually-exclusive with the .notdef check
///     above (a ".notdef"-named code is skipped here; it is already covered by 6.2.11.8). RM3-exempt per
///     veraPDF, so this walks only visible codes. Implemented for simple TrueType and simple CFF fonts
///     (with an embedded charset) via the tri-state <see cref="ResolveSimpleGlyph"/> resolver, which only
///     ever reports a confident absence and returns <c>Unknown</c> (skip, no finding) whenever the
///     code→glyph path is not reproducible here — symbolic TrueType (declared, or carrying only a (3,0)
///     Windows-Symbol cmap, or lacking a trustworthy Unicode-capable cmap subtable) with no usable Unicode
///     cmap path, a supplementary-plane AGL Unicode value (would truncate through a 16-bit cmap lookup), an
///     encoding name with no AGL Unicode, or a predefined-charset CFF. Not yet implemented for Type0 (a
///     later slice covers CIDToGIDMap→out-of-range).</item>
///   <item><b>font metrics (6.2.11.5 / 7.21.5):</b> the PDF-declared width of each used glyph
///     (<c>/Widths</c> for simple, <c>/W</c>÷<c>/DW</c> for CID) must match the embedded program's advance
///     width. Implemented for simple TrueType fonts (advance from glyf/hmtx via the cmap), simple CFF / Type1C
///     fonts with an embedded charset (advance from the CFF CharString via a glyph-name→charset-GID lookup —
///     see <see cref="SimpleCffAdvance"/>; predefined-charset CFF is excluded, see the note in
///     <see cref="CheckSimple"/>) and both Type0 descendant kinds — CIDFontType2 (glyf/hmtx) and
///     CIDFontType0 (CFF CharString: encoded width nominalWidthX+delta, else the FD's defaultWidthX), and
///     Type3 (the CharProc's <c>d0</c>/<c>d1</c> operator gives the glyph-space program width directly — see
///     <see cref="CheckType3"/>). Classic Type1 (FontFile) remains excluded from the width check (its
///     program-advance extraction is not reproduced reliably enough here to avoid a false positive — see the
///     tolerance remark).</item>
/// </list>
/// <para>
/// PDF/X-4 is excluded (ISO 15930-7 carries no such font-program constraints — same reasoning as
/// <see cref="FontDictionaryRule"/>). CMap WMode / usecmap (6.2.11.3.3 t02/t03) and subset CharSet/CIDSet
/// (6.2.11.4.2) are out of scope for this rule. Every check is one finding per font (deduplicated by base
/// name) and only ever under-reports: a font whose program will not parse, or whose glyph cannot be resolved,
/// is skipped rather than guessed at.
/// </para>
/// </summary>
internal sealed class FontProgramRule : IConformanceRule
{
    public string RuleId => "font-program";

    // Shared with PDF/UA-1 (7.21); PDF/X-4 is intentionally excluded.
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA | ConformanceProfile.PdfUA1;

    /// <summary>
    /// Width-consistency tolerance, in PDF glyph-space units (1000 per em). Tuned empirically against the
    /// veraPDF corpus: conformant files round-trip declared-vs-program width within ~1 unit (rounding of the
    /// units-per-em scaling), while the genuine 6.2.11.5 fail files diverge by 41 units or more. 10 sits in
    /// that gap with wide margin on both sides, keeping false positives at zero while still catching every
    /// fail file the rule's supported font types reach.
    /// </summary>
    private const double WidthTolerance = 10.0;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var notdefReported = new HashSet<string>(StringComparer.Ordinal);
        var metricsReported = new HashSet<string>(StringComparer.Ordinal);
        var presentReported = new HashSet<string>(StringComparer.Ordinal);

        foreach (UsedFontCodes usage in context.UsedTextGlyphs)
        {
            PdfFont font = usage.Font;

            // Type3 fonts have no embedded program (glyphs are content streams), so they never reach the
            // metrics-based checks below. Their 6.2.11.5 width comes from the CharProc's d0/d1 operator.
            if (font is Type3Font type3)
            {
                foreach (Finding f in CheckType3(context, type3, usage.VisibleCodes, metricsReported))
                    yield return f;
                continue;
            }

            EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
            if (metrics is null || !metrics.IsValid)
                continue; // not embedded, or the program will not parse — nothing to compare (FP-safe)

            foreach (Finding f in font is Type0Font type0
                         ? CheckType0(context, type0, metrics, usage.Codes, usage.VisibleCodes, notdefReported,
                             metricsReported)
                         : CheckSimple(context, font, metrics, usage.Codes, usage.VisibleCodes, metricsReported,
                             notdefReported, presentReported))
            {
                yield return f;
            }
        }
    }

    // ── Type0 composite fonts (.notdef + metrics) ─────────────────────────────────────────────────────
    private IEnumerable<Finding> CheckType0(
        ConformanceContext context, Type0Font font, EmbeddedFontMetrics metrics,
        IReadOnlyCollection<int> codes, IReadOnlyCollection<int> visibleCodes, HashSet<string> notdefReported,
        HashSet<string> metricsReported)
    {
        // Only an Identity CMap lets us treat the shown two-byte code as the CID directly; any other CMap
        // (predefined name or embedded stream) needs a CMap parser the engine lacks, so the font is skipped.
        if (!IsIdentity(font.EncodingName) || font.DescendantFont is not CidFont cid)
            yield break;

        bool cidKeyedCff = context.ResolveName(cid.FontDictionary.Get("Subtype")) == "CIDFontType0";
        if (cidKeyedCff && !metrics.IsCffFont)
            yield break; // CIDFontType0 maps CID→GID through the CFF charset — need the CFF program

        // .notdef (6.2.11.8) is NOT render-mode-exempt — walks ALL codes.
        bool notdefHit = false;
        foreach (int code in codes)
        {
            int gid = cidKeyedCff ? metrics.GetGlyphIdByCid((ushort)code) : cid.MapCidToGid(code);
            if (gid == 0)
                notdefHit = true; // a shown code with no glyph in the subset renders as .notdef
        }

        // metrics (6.2.11.5) IS render-mode-exempt — walks only visibleCodes (was: codes).
        double worstDiff = 0;
        foreach (int code in visibleCodes)
        {
            int gid = cidKeyedCff ? metrics.GetGlyphIdByCid((ushort)code) : cid.MapCidToGid(code);
            if (gid == 0)
                continue; // .notdef has no meaningful width to compare

            // Both descendant kinds are width-checked: CIDFontType2's advance comes from glyf/hmtx, and
            // CIDFontType0's from the CFF CharString (encoded width = nominalWidthX + delta, else the FD's
            // defaultWidthX — resolved per-FD in the CFF parser). CIDFontType0 was formerly excluded because
            // a nominalWidthX/defaultWidthX confusion made omitted-width glyphs diverge by hundreds of units;
            // with that fixed, a conformant CFF-keyed font round-trips well inside the tolerance.
            double declared = cid.GetCharacterWidth(code);
            double program = Scale(metrics, metrics.GetAdvanceWidth((ushort)gid));
            worstDiff = Math.Max(worstDiff, Math.Abs(declared - program));
        }

        if (notdefHit && notdefReported.Add(font.BaseFont))
            yield return Make(context, font, "8",
                $"The composite font {Name(font)} renders a character code that maps to the .notdef glyph "
                + "(glyph 0), which is not present in the embedded font program.");

        if (worstDiff > WidthTolerance && metricsReported.Add(font.BaseFont))
            yield return Make(context, font, "5",
                $"The composite font {Name(font)} declares a glyph width that differs from the embedded font "
                + $"program's advance width by {worstDiff:F0} units (tolerance {WidthTolerance:F0}).");
    }

    // ── Simple fonts — .notdef + glyph-present + metrics (TrueType + simple CFF) ──────────────────────
    private IEnumerable<Finding> CheckSimple(
        ConformanceContext context, PdfFont font, EmbeddedFontMetrics metrics,
        IReadOnlyCollection<int> codes, IReadOnlyCollection<int> visibleCodes, HashSet<string> metricsReported,
        HashSet<string> notdefReported, HashSet<string> presentReported)
    {
        // Two simple embeddings are covered, each with a reliable program-advance path: TrueType (glyf/hmtx via
        // the cmap) and simple CFF / Type1C (CharString advance via the CFF charset). Classic Type1 (FontFile)
        // stays excluded — its advance extraction is not reproduced precisely enough here to compare without
        // risking a false positive. Type0 fonts never reach this method (routed to CheckType0), and Type3 fonts
        // are routed to CheckType3 (their width comes from the CharProc d0/d1 operator) before this point.
        //
        // Simple CFF is gated on an embedded (custom) charset. A CFF using a predefined charset (ISOAdobe /
        // Expert / ExpertSubset) is skipped: the engine does not yet materialise predefined charsets, so
        // glyph-name→GID resolution misfires on such fonts (e.g. a full 'Helvetica' resolves "space" to the
        // wrong glyph), which would be a false positive. Subsetted fonts — the conformant-producer norm, and
        // the form of every corpus fail fixture this rule targets — always carry a custom charset, so they are
        // covered. Widening to predefined-charset CFF is deferred to the CFF charset (Tier-2) engine work.
        bool isTrueType = font.FontType == PdfFontType.TrueType;
        bool isSimpleCff = metrics.IsCffFont && metrics.CffHasEmbeddedCharset;
        if (!isTrueType && !isSimpleCff)
            yield break; // classic Type1 (FontFile) / Type3 / predefined-charset CFF → out of scope (FP-safe)

        string kind = isTrueType ? "TrueType" : "CFF";
        bool symbolic = IsSymbolic(context, font);

        // .notdef (6.2.11.8 / 7.21.8): a shown code whose encoding glyph name is literally ".notdef" — the
        // veraPDF predicate is name == ".notdef". NOT render-mode-exempt (spec: "regardless of text rendering
        // mode"), so this walks ALL codes. Pure encoding lookup, no program parsing → FP-safe.
        if (codes.Any(code => font.Encoding?.GetGlyphName(code) == ".notdef") && notdefReported.Add(font.BaseFont))
            yield return Make(context, font, "8",
                $"The {kind} font {Name(font)} shows a character code mapped to the .notdef glyph.");

        // glyph-present (6.2.11.4.1 t2 / 7.21.4.1 t2): a VISIBLE code whose (non-.notdef) glyph is confidently
        // absent from the embedded program. RM3-exempt → visibleCodes. Skip ".notdef" names (that is the
        // clause above; glyph 0 is present).
        bool absentHit = false;
        foreach (int code in visibleCodes)
        {
            if (font.Encoding?.GetGlyphName(code) == ".notdef")
                continue;
            if (ResolveSimpleGlyph(font, metrics, code, isTrueType, symbolic) == SimpleGlyphResolution.NotDef)
            {
                absentHit = true;
                break;
            }
        }
        if (absentHit && presentReported.Add(font.BaseFont))
            yield return Make(context, font, "4.1",
                $"The {kind} font {Name(font)} renders a glyph that is not present in the embedded font "
                + "program.");

        // metrics (6.2.11.5 / 7.21.5): RM3-exempt → iterate visibleCodes (was: codes). Only runs when
        // /Widths is present.
        if (context.Resolve(font.FontDictionary.Get("Widths")) is not PdfArray widths)
            yield break;

        double worstDiff = 0;
        foreach (int code in visibleCodes)
        {
            int index = code - font.FirstChar;
            if (index < 0 || index >= widths.Count)
                continue; // no declared width for this code — cannot compare

            double? program = isTrueType
                ? TrueTypeAdvance(font, metrics, code)
                : SimpleCffAdvance(font, metrics, code);
            if (program is null)
                continue; // glyph could not be resolved — skip rather than guess (FP-safe)

            double declared = widths[index].ToDouble();
            worstDiff = Math.Max(worstDiff, Math.Abs(declared - program.Value));
        }

        if (worstDiff > WidthTolerance && metricsReported.Add(font.BaseFont))
            yield return Make(context, font, "5",
                $"The {kind} font {Name(font)} declares a glyph width that differs "
                + $"from the embedded font program's advance width by {worstDiff:F0} units "
                + $"(tolerance {WidthTolerance:F0}).");
    }

    // ── Type3 fonts — metrics only (glyphs are content streams; width from the d0/d1 operator) ─────────
    private IEnumerable<Finding> CheckType3(
        ConformanceContext context, Type3Font font, IReadOnlyCollection<int> visibleCodes,
        HashSet<string> metricsReported)
    {
        // 6.2.11.5 / 7.21.5 for Type3: the /Widths value and the CharProc's d0/d1 width (both raw glyph
        // space) must be consistent. RM3-exempt → visibleCodes. FP-safe: any code whose glyph name,
        // CharProc, or d0/d1 width can't be resolved is skipped.
        if (context.Resolve(font.FontDictionary.Get("Widths")) is not PdfArray widths)
            yield break;

        double worstDiff = 0;
        foreach (int code in visibleCodes)
        {
            int index = code - font.FirstChar;
            if (index < 0 || index >= widths.Count)
                continue; // no declared width for this code

            double? program = Type3ProgramWidth(context, font, code);
            if (program is null)
                continue; // glyph/CharProc/d0-d1 not resolvable — skip rather than guess

            double declared = widths[index].ToDouble();
            worstDiff = Math.Max(worstDiff, Math.Abs(declared - program.Value));
        }

        if (worstDiff > WidthTolerance && metricsReported.Add(font.BaseFont))
            yield return Make(context, font, "5",
                $"The Type3 font {Name(font)} declares a glyph width that differs from the CharProc's "
                + $"d0/d1 width by {worstDiff:F0} units (tolerance {WidthTolerance:F0}).");
    }

    /// <summary>
    /// The program (glyph-space) advance width of a Type3 glyph: the <c>wx</c> operand of the first
    /// <c>d0</c>/<c>d1</c> operator in the glyph's CharProc (ISO 32000-1 9.6.5.1 requires one of these as the
    /// procedure's first operator). Returns null when the code has no glyph name, no CharProc, an unparseable
    /// CharProc, or no d0/d1 — the caller then skips it (FP-safe).
    /// </summary>
    private static double? Type3ProgramWidth(ConformanceContext context, Type3Font font, int code)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName))
            return null;

        PdfStream? charProc = font.GetCharProc(glyphName);
        if (charProc is null)
            return null;

        List<PdfOperator> ops;
        try { ops = PdfContentParser.Parse(charProc.GetDecodedData(context.Document.Decryptor)); }
        catch { return null; }

        foreach (PdfOperator op in ops)
            if (op is GenericOperator { Name: "d0" or "d1" } g && g.Operands.Count >= 1)
                return g.Operands[0].ToDouble();
        return null;
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a simple TrueType code to its program advance width, preferring the encoding's Unicode
    /// value (via the Adobe Glyph List) so the WinAnsi 0x80–0x9F remap band — where the raw code is not the
    /// Unicode code point (euro, smart quotes, dashes …) — is looked up correctly, falling back to the raw
    /// code. Returns null when neither path finds a glyph.
    /// </summary>
    private static double? TrueTypeAdvance(PdfFont font, EmbeddedFontMetrics metrics, int code)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);
        string? unicode = glyphName is null ? null : GlyphList.GetUnicode(glyphName);
        if (!string.IsNullOrEmpty(unicode))
        {
            // GetUnicodeAdvanceWidth resolves the glyph through the cmap and returns its advance width
            // (in font units) directly — scale it, do NOT feed it back through GetAdvanceWidth.
            ushort widthViaUnicode = metrics.GetUnicodeAdvanceWidth(char.ConvertToUtf32(unicode, 0));
            if (widthViaUnicode > 0)
                return Scale(metrics, widthViaUnicode);
        }

        ushort gid = metrics.GetGlyphId((ushort)code);
        return gid == 0 ? null : Scale(metrics, metrics.GetAdvanceWidth(gid));
    }

    /// <summary>
    /// Resolves a simple CFF (Type1C) code to its program advance width, the CFF way: code → glyph name (via the
    /// PDF <c>/Encoding</c>, so a custom <c>/Differences</c> name is honoured) → GID (via the CFF charset, using
    /// <see cref="EmbeddedFontMetrics.GetGlyphIdByName"/>) → CharString advance. Returns null when the code has no
    /// glyph name, or the name is not in the charset (GID 0 / .notdef) — skipped rather than guessed, keeping the
    /// check false-positive-safe. Deliberately avoids <see cref="EmbeddedFontMetrics.GetAdvanceWidthByName"/>,
    /// which resolves only real Type1 programs and returns a hard-coded 500 for CFF — feeding that into the width
    /// comparison would itself be the false positive.
    /// </summary>
    private static double? SimpleCffAdvance(PdfFont font, EmbeddedFontMetrics metrics, int code)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName))
            return null;

        ushort gid = metrics.GetGlyphIdByName(glyphName);
        return gid == 0 ? null : Scale(metrics, metrics.GetAdvanceWidth(gid));
    }

    /// <summary>Confidence-tagged resolution of a simple-font code to its program glyph.</summary>
    private enum SimpleGlyphResolution { Present, NotDef, Unknown }

    /// <summary>
    /// Resolves a simple-font code to a program glyph with a confidence flag, the FP-safe way. Returns
    /// <see cref="SimpleGlyphResolution.Unknown"/> whenever the standard code→glyph path is not reproducible
    /// here (symbolic TrueType — declared or carrying only a (3,0) Windows-Symbol cmap — with no trustworthy
    /// Unicode-capable cmap subtable, an encoding name with no Unicode, a supplementary-plane Unicode value
    /// that a 16-bit cmap lookup would truncate, a predefined-charset CFF) so the caller emits nothing. Only
    /// a confident glyph-0 result is <see cref="SimpleGlyphResolution.NotDef"/>.
    /// </summary>
    private static SimpleGlyphResolution ResolveSimpleGlyph(
        PdfFont font, EmbeddedFontMetrics metrics, int code, bool isTrueType, bool symbolic)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);

        if (isTrueType)
        {
            // A symbolic font (PDF-declared /Flags bit 3, or a program carrying only a (3,0) Windows-Symbol
            // cmap keyed at 0xF000+code) has no reliable AGL-Unicode → cmap path: there is no 0xF000-offset
            // retry against a Symbol cmap anywhere in the lookup chain, so an AGL-derived Unicode value
            // missing there is indistinguishable from a genuine .notdef. Skip rather than guess — Unknown is
            // always FP-safe, even when the font happens to also carry a usable Unicode cmap.
            if (symbolic || metrics.HasSymbolCmapEncoding())
                return SimpleGlyphResolution.Unknown;

            // Trustworthy only through a real cmap keyed by the encoding name's Unicode value. Without an
            // AGL Unicode for the name (symbolic / custom name) we cannot tell absence from a lookup gap.
            string? unicode = glyphName is null ? null : GlyphList.GetUnicode(glyphName);
            if (string.IsNullOrEmpty(unicode))
                return SimpleGlyphResolution.Unknown;
            int cp = char.ConvertToUtf32(unicode, 0);
            // A supplementary-plane code point (cp > 0xFFFF) would truncate through GetGlyphId's ushort
            // parameter — and HasUnicodeCmapEncoding requires an actual Unicode-capable subtable (a plain
            // cmap presence, e.g. only a (1,0) Macintosh-Roman record, is keyed by neither Unicode nor a
            // reliable direct code, so GetGlyphId would fall back to "code is the GID", a rendering
            // heuristic, not a trustworthy absence signal).
            if (cp > 0xFFFF || !metrics.HasUnicodeCmapEncoding())
                return SimpleGlyphResolution.Unknown;
            ushort gid = metrics.GetGlyphId((ushort)cp);
            return gid == 0 ? SimpleGlyphResolution.NotDef : SimpleGlyphResolution.Present;
        }

        // Simple CFF / Type1: code → name → charset GID. Gated (by the caller) on an embedded charset, so a
        // name absent from the charset is a genuine miss, not the predefined-charset parser bug.
        if (string.IsNullOrEmpty(glyphName))
            return SimpleGlyphResolution.Unknown;
        if (metrics.GetGlyphIdByName(glyphName) != 0)
            return SimpleGlyphResolution.Present;

        // A name miss is not yet confident .notdef: a producer can point the PDF /Encoding at a standard
        // name (e.g. code 0xA0 → "nonbreakingspace" under WinAnsiEncoding) while the CFF subset carries no
        // charset entry of that name, reusing another glyph (typically "space") via the font program's own
        // built-in Encoding instead. CoreTextRenderer.ResolveGlyphId takes exactly this fallback when
        // rendering, so a resolver that skipped it would flag glyphs the renderer draws just fine — see
        // EmbeddedFontMetrics.GetGlyphIdByCffEncoding (same PDFUA-Ref-2-03 precedent it documents).
        return metrics.GetGlyphIdByCffEncoding((ushort)code) == 0
            ? SimpleGlyphResolution.NotDef
            : SimpleGlyphResolution.Present;
    }

    /// <summary>Scales a program advance width from font units to PDF 1000-per-em glyph space.</summary>
    private static double Scale(EmbeddedFontMetrics metrics, int advanceInFontUnits)
    {
        int upm = metrics.UnitsPerEm <= 0 ? 1000 : metrics.UnitsPerEm;
        return advanceInFontUnits * 1000.0 / upm;
    }

    /// <summary>True when the font's /FontDescriptor /Flags bit 3 (symbolic) is set. Mirrors
    /// <see cref="FontDictionaryRule.SymbolicFlags"/>'s descriptor resolution (read through
    /// <see cref="ConformanceContext.Resolve"/>, the same indirection the rest of this rule uses) rather
    /// than <see cref="PdfFont.GetDescriptor()"/>.</summary>
    private static bool IsSymbolic(ConformanceContext context, PdfFont font) =>
        context.Resolve(font.FontDictionary.Get("FontDescriptor")) is PdfDictionary descriptor
        && context.Resolve(descriptor.Get("Flags")) is PdfInteger flags
        && (flags.LongValue & 4) != 0;

    private static bool IsIdentity(string? encoding) => encoding is "Identity-H" or "Identity-V";

    private static string Name(PdfFont font) =>
        string.IsNullOrEmpty(font.BaseFont) || font.BaseFont == "Unknown" ? "(unnamed)" : $"'{font.BaseFont}'";

    private Finding Make(ConformanceContext context, PdfFont font, string sub, string message) =>
        new()
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target,
                context.Target == ConformanceProfile.PdfUA1 ? $"7.21.{sub}" : $"6.2.11.{sub}"),
            Message = message,
            ObjectNumber = font.FontDictionary.IsIndirect ? font.FontDictionary.ObjectNumber : null,
        };
}
