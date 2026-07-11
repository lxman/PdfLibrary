using System;
using System.Collections.Generic;
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
///   <item><b>.notdef glyph (6.2.11.8 / 7.21.8):</b> a shown code that resolves to glyph 0 (.notdef) is
///     reported. Implemented for Type0 composite fonts with an Identity CMap only, where the code equals the
///     CID and the CID→GID map (CIDToGIDMap for CIDFontType2, the CFF charset for CIDFontType0) is
///     authoritative. Simple Type1/TrueType are deliberately left unreported: their code→glyph selection
///     (encoding differences, symbolic cmaps, the WinAnsi remap band) is not reproduced here precisely
///     enough to distinguish a genuine .notdef from a resolution gap without risking a false positive.</item>
///   <item><b>font metrics (6.2.11.5 / 7.21.5):</b> the PDF-declared width of each used glyph
///     (<c>/Widths</c> for simple, <c>/W</c>÷<c>/DW</c> for CID) must match the embedded program's advance
///     width. Implemented for TrueType simple fonts and both Type0 descendant kinds — CIDFontType2 (advance
///     from glyf/hmtx) and CIDFontType0 (advance from the CFF CharString: encoded width nominalWidthX+delta,
///     else the FD's defaultWidthX). Simple Type1/CFF fonts and Type3 fonts remain excluded from the width
///     check (their program-advance extraction is not reproduced reliably enough here to avoid a false
///     positive — see the tolerance remark).</item>
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

        foreach (UsedFontCodes usage in context.UsedTextGlyphs)
        {
            PdfFont font = usage.Font;
            EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
            if (metrics is null || !metrics.IsValid)
                continue; // not embedded, or the program will not parse — nothing to compare (FP-safe)

            foreach (Finding f in font is Type0Font type0
                         ? CheckType0(context, type0, metrics, usage.Codes, notdefReported, metricsReported)
                         : CheckSimple(context, font, metrics, usage.Codes, metricsReported))
            {
                yield return f;
            }
        }
    }

    // ── Type0 composite fonts (.notdef + metrics) ─────────────────────────────────────────────────────
    private IEnumerable<Finding> CheckType0(
        ConformanceContext context, Type0Font font, EmbeddedFontMetrics metrics,
        IReadOnlyCollection<int> codes, HashSet<string> notdefReported, HashSet<string> metricsReported)
    {
        // Only an Identity CMap lets us treat the shown two-byte code as the CID directly; any other CMap
        // (predefined name or embedded stream) needs a CMap parser the engine lacks, so the font is skipped.
        if (!IsIdentity(font.EncodingName) || font.DescendantFont is not CidFont cid)
            yield break;

        bool cidKeyedCff = context.ResolveName(cid.FontDictionary.Get("Subtype")) == "CIDFontType0";
        if (cidKeyedCff && !metrics.IsCffFont)
            yield break; // CIDFontType0 maps CID→GID through the CFF charset — need the CFF program

        bool notdefHit = false;
        double worstDiff = 0;
        foreach (int code in codes)
        {
            int gid = cidKeyedCff ? metrics.GetGlyphIdByCid((ushort)code) : cid.MapCidToGid(code);
            if (gid == 0)
            {
                notdefHit = true; // a shown code with no glyph in the subset renders as .notdef
                continue;         // .notdef has no meaningful width to compare
            }

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

    // ── Simple fonts — metrics only, TrueType only ────────────────────────────────────────────────────
    private IEnumerable<Finding> CheckSimple(
        ConformanceContext context, PdfFont font, EmbeddedFontMetrics metrics,
        IReadOnlyCollection<int> codes, HashSet<string> metricsReported)
    {
        // Only TrueType simple fonts are covered. Type1/CFF advance-width extraction and Type3 glyph
        // metrics are not reproduced reliably enough here to compare without risking a false positive.
        if (font.FontType != PdfFontType.TrueType)
            yield break;
        if (context.Resolve(font.FontDictionary.Get("Widths")) is not PdfArray widths)
            yield break;

        double worstDiff = 0;
        foreach (int code in codes)
        {
            int index = code - font.FirstChar;
            if (index < 0 || index >= widths.Count)
                continue; // no declared width for this code — cannot compare

            double? program = TrueTypeAdvance(font, metrics, code);
            if (program is null)
                continue; // glyph could not be resolved — skip rather than guess (FP-safe)

            double declared = widths[index].ToDouble();
            worstDiff = Math.Max(worstDiff, Math.Abs(declared - program.Value));
        }

        if (worstDiff > WidthTolerance && metricsReported.Add(font.BaseFont))
            yield return Make(context, font, "5",
                $"The TrueType font {Name(font)} declares a glyph width that differs from the embedded font "
                + $"program's advance width by {worstDiff:F0} units (tolerance {WidthTolerance:F0}).");
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

    /// <summary>Scales a program advance width from font units to PDF 1000-per-em glyph space.</summary>
    private static double Scale(EmbeddedFontMetrics metrics, int advanceInFontUnits)
    {
        int upm = metrics.UnitsPerEm <= 0 ? 1000 : metrics.UnitsPerEm;
        return advanceInFontUnits * 1000.0 / upm;
    }

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
