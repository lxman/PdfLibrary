using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Conformance.Rules;

/// <summary>One comparable code: its resolved program glyph, the PDF-declared width, and the
/// program's advance, both in 1000-per-em glyph space.</summary>
internal readonly record struct WidthComparison(int Code, ushort Gid, double Declared, double Program);

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

    /// <summary>Simple TrueType / simple CFF: declared from /Widths via FirstChar indexing.</summary>
    public static IEnumerable<WidthComparison> Simple(
        PdfFont font, EmbeddedFontMetrics metrics, PdfArray widths, IEnumerable<int> codes,
        bool isTrueType)
    {
        foreach (int code in codes)
        {
            int index = code - font.FirstChar;
            if (index < 0 || index >= widths.Count)
                continue; // no declared width for this code — cannot compare

            (ushort Gid, double Program)? resolved = isTrueType
                ? TrueTypeAdvance(font, metrics, code)
                : SimpleCffAdvance(font, metrics, code);
            if (resolved is null)
                continue; // glyph could not be resolved — skip rather than guess (FP-safe)

            yield return new WidthComparison(
                code, resolved.Value.Gid, widths[index].ToDouble(), resolved.Value.Program);
        }
    }

    /// <summary>Composite: code IS the CID (Identity CMap, enforced by the caller); declared from
    /// /W else /DW via <see cref="CidFont.GetCharacterWidth"/>.</summary>
    public static IEnumerable<WidthComparison> Composite(
        CidFont cid, EmbeddedFontMetrics metrics, bool cidKeyedCff, IEnumerable<int> codes)
    {
        foreach (int code in codes)
        {
            int gid = cidKeyedCff ? metrics.GetGlyphIdByCid((ushort)code) : cid.MapCidToGid(code);
            if (gid == 0)
                continue; // .notdef has no meaningful width to compare

            yield return new WidthComparison(
                code, (ushort)gid, cid.GetCharacterWidth(code),
                Scale(metrics, metrics.GetAdvanceWidth((ushort)gid)));
        }
    }

    // Moved verbatim from FontProgramRule.TrueTypeAdvance, reshaped only to surface the gid the
    // advance came from. The doc comments there (WinAnsi remap band; the issue-26 zero-advance
    // recall-for-precision trade) travel with the code.
    private static (ushort Gid, double Program)? TrueTypeAdvance(
        PdfFont font, EmbeddedFontMetrics metrics, int code)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);
        string? unicode = glyphName is null ? null : GlyphList.GetUnicode(glyphName);
        if (!string.IsNullOrEmpty(unicode))
        {
            int cp = char.ConvertToUtf32(unicode, 0);
            ushort gidByUnicode = metrics.GetGlyphIdByUnicode(cp);
            if (gidByUnicode != 0)
            {
                ushort widthViaUnicode = metrics.GetAdvanceWidth(gidByUnicode);
                if (widthViaUnicode > 0)
                    return (gidByUnicode, Scale(metrics, widthViaUnicode));
            }
        }

        ushort gid = metrics.GetGlyphId((ushort)code);
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
