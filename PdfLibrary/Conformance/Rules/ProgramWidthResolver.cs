using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Conformance.Rules;

/// <summary>One comparable code: its resolved program glyph, the PDF-declared width, and the
/// program's advance, both in 1000-per-em glyph space.</summary>
internal readonly record struct WidthComparison(
    int Code, // character code for simple fonts; already-mapped CID for composite fonts
    ushort Gid,
    double Declared,
    double Program);

/// <summary>
/// The width enumeration shared by <see cref="FontProgramRule"/> (6.2.11.5) and the F-4a width-repair
/// path. Extracted so the repair patches the SAME gid the rule compared — two enumerations that
/// disagreed anywhere would produce a fix that reports success while the finding stands (the F-3
/// SubsetProgramGlyphs lesson). Every skip below is verbatim rule behavior: unresolvable,
/// out-of-bounds, gid-0, and zero-advance codes yield nothing rather than a guess (FP-safe).
/// </summary>
internal static class ProgramWidthResolver
{
    /// <summary>Font units → PDF 1000-per-em glyph space (the rule's own Scale, moved here).</summary>
    internal static double Scale(EmbeddedFontMetrics metrics, int advanceInFontUnits)
    {
        int upm = metrics.UnitsPerEm <= 0 ? 1000 : metrics.UnitsPerEm;
        return advanceInFontUnits * 1000.0 / upm;
    }

    /// <summary>Simple TrueType / simple CFF: declared from /Widths via FirstChar indexing, or from
    /// the descriptor's /MissingWidth (default 0) when a used code lies outside that array.</summary>
    public static IEnumerable<WidthComparison> Simple(
        PdfFont font, EmbeddedFontMetrics metrics, PdfArray widths, IEnumerable<int> codes,
        bool isTrueType)
    {
        double missingWidth = font.GetDescriptor()?.MissingWidth ?? 0;
        foreach (int code in codes)
        {
            int index = code - font.FirstChar;
            // ISO 32000-2 §9.8.3: /MissingWidth supplies the width for a character code whose
            // /Widths entry is absent, and defaults to 0. Skipping an out-of-range used code hid
            // issue 29: CC-MAIN 4000_4000080.pdf shows code 13 below /FirstChar 30; its descriptor
            // omits /MissingWidth (declared 0) while the embedded Arial program advances 569 font
            // units (278 units in PDF glyph space). veraPDF correctly reports that 6.2.11.5 mismatch.
            double declared = index >= 0 && index < widths.Count
                ? widths[index].ToDouble()
                : missingWidth;

            (ushort Gid, double Program)? resolved = isTrueType
                ? TrueTypeAdvance(font, metrics, code)
                : SimpleCffAdvance(font, metrics, code);
            if (resolved is null)
                continue; // glyph could not be resolved — skip rather than guess (FP-safe)

            yield return new WidthComparison(
                code, resolved.Value.Gid, declared, resolved.Value.Program);
        }
    }

    /// <summary>Composite: the caller supplies CIDs after applying the Type0 encoding CMap; declared
    /// from /W else /DW via <see cref="CidFont.GetCharacterWidth"/>.</summary>
    public static IEnumerable<WidthComparison> Composite(
        CidFont cid, EmbeddedFontMetrics metrics, bool cidKeyedCff, IEnumerable<int> cids)
    {
        foreach (int cidValue in cids)
        {
            // Strict (issue 42): must agree with FontProgramRule's own .notdef resolution, or a CID
            // beyond the map's coverage would be skipped as .notdef by one and width-compared by
            // the other. The renderer's lenient answer is deliberately NOT used here.
            int gid = cidKeyedCff
                ? metrics.GetGlyphIdByCid((ushort)cidValue)
                : cid.MapCidToGidStrict(cidValue);
            if (gid == 0 || gid >= metrics.NumGlyphs)
                continue; // .notdef or an absent glyph has no meaningful width to compare

            yield return new WidthComparison(
                cidValue, (ushort)gid, cid.GetCharacterWidth(cidValue),
                Scale(metrics, metrics.GetAdvanceWidth((ushort)gid)));
        }
    }

    // Uses the renderer's shared code->GID decision so the conformance comparison cannot inspect a
    // different glyph (or skip one) when the PDF encoding and the embedded cmap use different byte
    // spaces. Zero-advance glyphs remain deliberately unmeasurable and are skipped.
    private static (ushort Gid, double Program)? TrueTypeAdvance(
        PdfFont font, EmbeddedFontMetrics metrics, int code)
    {
        ushort gid = TrueTypeGlyphResolver.Resolve(font, metrics, code);
        if (gid == 0)
            return null;
        ushort advance = metrics.GetAdvanceWidth(gid);
        return advance == 0 ? null : (gid, Scale(metrics, advance));
    }

    // Moved verbatim from FontProgramRule.SimpleCffAdvance (doc comment travels with it).
    private static (ushort Gid, double Program)? SimpleCffAdvance(
        PdfFont font, EmbeddedFontMetrics metrics, int code)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName))
            return null;

        ushort gid = metrics.GetGlyphIdByName(glyphName);
        return gid == 0 ? null : (gid, Scale(metrics, metrics.GetAdvanceWidth(gid)));
    }
}
