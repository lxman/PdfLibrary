using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Issue 34: LoadCidToGidMap stored only non-zero entries, so the resolution could not distinguish
/// "CID beyond the map's range" from "CID the map explicitly declares GID 0" (a legitimate
/// ".notdef, no glyph" answer it must report as 0).
///
/// <para>Issue 42 then split the beyond-coverage case in two, because the render oracles and the
/// conformance oracle genuinely disagree about it and each is right for its own question — see
/// <see cref="CidFont.MapCidToGid"/> and <see cref="CidFont.MapCidToGidStrict"/>. Both answers are
/// pinned here, deliberately adjacent, so neither can be "corrected" into the other.</para>
/// </summary>
public class CidToGidMapExplicitZeroTests
{
    /// <summary>CIDFontType2 descendant with a direct /CIDToGIDMap stream. Entries (big-endian
    /// 2-byte GIDs, indexed by CID): cid0→0, cid1→5, cid2→0. Covered range = 3 CIDs.</summary>
    private static CidFont FontWithMap()
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Font"));
        dict.Set("Subtype", new PdfName("CIDFontType2"));
        dict.Set("BaseFont", new PdfName("Test"));
        dict.Set("CIDToGIDMap", new PdfStream(new PdfDictionary(), [0, 0, 0, 5, 0, 0]));
        return new CidFont(dict);
    }

    /// <summary>A CIDFontType2 with no /CIDToGIDMap at all — identity under BOTH resolutions, since
    /// there is no stream whose coverage a CID could fall outside of.</summary>
    private static CidFont FontWithoutMap()
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Font"));
        dict.Set("Subtype", new PdfName("CIDFontType2"));
        dict.Set("BaseFont", new PdfName("Test"));
        return new CidFont(dict);
    }

    // ── inside the map's coverage: both resolutions agree (issue 34) ──────────────────────────────

    [Fact]
    public void A_cid_the_map_explicitly_sends_to_zero_reports_gid_zero()
        => Assert.Equal(0, FontWithMap().MapCidToGid(2));

    [Fact]
    public void A_nonzero_entry_still_reports_its_gid()
        => Assert.Equal(5, FontWithMap().MapCidToGid(1));

    [Fact]
    public void The_strict_resolution_agrees_inside_the_maps_coverage()
    {
        CidFont font = FontWithMap();
        Assert.Equal(0, font.MapCidToGidStrict(2));
        Assert.Equal(5, font.MapCidToGidStrict(1));
    }

    // ── beyond the map's coverage: the two resolutions deliberately DISAGREE (issue 42) ───────────

    /// <summary>The RENDER answer. Not a divergence-by-oversight: measured 2026-08-19, poppler 24.x,
    /// mutool and Ghostscript ALL draw the identity glyph here (fixture = a corpus document whose
    /// /CIDToGIDMap was rewritten to cover CID 0 only; poppler and mutool render pixel-identically,
    /// Ghostscript draws the same glyphs plus .notdef boxes only where the resulting GID exceeds the
    /// subset font's glyph count — a separate concern). Returning 0 here would blank text that every
    /// other viewer in the field shows, so this stays identity ON PURPOSE.</summary>
    [Fact]
    public void A_cid_beyond_the_maps_covered_range_keeps_the_identity_fallback_for_rendering()
        => Assert.Equal(7, FontWithMap().MapCidToGid(7));

    /// <summary>The CONFORMANCE answer, and the opposite one. ISO 32000-2 §9.7.6.3: "if a (character)
    /// code does not have a corresponding GID in the CIDtoGIDMap stream, the glyph for CID 0 shall be
    /// substituted" — a sentence ABSENT from ISO 32000-1's same subclause, so it is a PDF 2.0 addition.
    /// veraPDF 1.28.1 applies it: on the same fixture it raises 6.2.11.8 (plus 6.2.11.5 / 6.2.11.4.1 /
    /// 6.2.11.4.2) that the untruncated original does not. Conformance rules must therefore see 0 here
    /// or they under-report against the reference.</summary>
    [Fact]
    public void A_cid_beyond_the_maps_covered_range_is_notdef_under_the_strict_resolution()
        => Assert.Equal(0, FontWithMap().MapCidToGidStrict(7));

    // ── no map at all: identity under both, so the strict answer cannot over-correct ──────────────

    [Fact]
    public void An_identity_name_keeps_identity()
    {
        var dict = new PdfDictionary();
        dict.Set("Subtype", new PdfName("CIDFontType2"));
        dict.Set("BaseFont", new PdfName("Test"));
        dict.Set("CIDToGIDMap", new PdfName("Identity"));
        Assert.Equal(42, new CidFont(dict).MapCidToGid(42));
        Assert.Equal(42, new CidFont(dict).MapCidToGidStrict(42));
    }

    /// <summary>Guards the strict resolution against over-correcting: with NO /CIDToGIDMap stream there
    /// is no coverage to fall outside of, so §9.7.6.3's precondition ("in the CIDtoGIDMap stream") is
    /// not met and identity is right for both callers. Without this pin, "return 0 when the lookup
    /// misses" would silently blank every CIDFontType2 that omits the entry entirely.</summary>
    [Fact]
    public void A_font_with_no_map_keeps_identity_under_both_resolutions()
    {
        CidFont font = FontWithoutMap();
        Assert.Equal(42, font.MapCidToGid(42));
        Assert.Equal(42, font.MapCidToGidStrict(42));
    }
}
