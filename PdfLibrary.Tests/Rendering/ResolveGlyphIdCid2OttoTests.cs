using System.Collections.Generic;
using CffTestFixtures;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Issue 36: <c>CoreTextRenderer.ResolveGlyphId</c>'s Type0 arm re-mapped an ALREADY-MAPPED gid
/// (<c>CidFont.MapCidToGid</c>'s result) through the CFF charset whenever the embedded program
/// happened to carry CFF outlines (<c>metrics.IsCffFont</c>), instead of keying on the DESCENDANT's
/// own <c>/Subtype</c>. That discriminator is right for a CIDFontType0 descendant (the CFF charset
/// IS the CID→GID authority there — <c>MapCidToGid</c> passes CIDs through untouched). It is WRONG
/// for a CIDFontType2 descendant whose <c>/FontFile2</c> happens to be an OTTO/CFF-outline sfnt:
/// there <c>/CIDToGIDMap</c> alone is the mapping authority, and the OTTO's own charset (not
/// CID-keyed) scrambles the already-correct gid (PowerBASIC Compiler for Windows v10.0.pdf,
/// reproduced on body pages).
/// </summary>
public class ResolveGlyphIdCid2OttoTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>
    /// A CFF charset (format 0) whose entries are DELIBERATELY reversed relative to gid, so a
    /// CID→GID lookup THROUGH the charset diverges from the correct CIDFontType2 answer: gid 1 →
    /// CID 5, gid 2 → CID 4, ..., gid 5 → CID 1. <c>GetGlyphIdByCid(5)</c> therefore returns gid 1
    /// (the charset detour) where the identity <c>/CIDToGIDMap</c> answer is gid 5.
    /// </summary>
    private static byte[] DivergentCharsetCff() =>
        MinimalCff.Build(charsetOperand: null, numGlyphs: 6, customCharsetSids: [5, 4, 3, 2, 1]);

    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }

    /// <summary>hmtx for 6 glyphs, each advancing 200 units — content is irrelevant to gid
    /// resolution, only needed so <see cref="EmbeddedFontMetrics"/>.IsValid is true.</summary>
    private static byte[] Hmtx6()
    {
        var b = new List<byte>();
        for (var gid = 0; gid < 6; gid++) { U16(b, 200); U16(b, 0); }
        return b.ToArray();
    }

    /// <summary>
    /// OTTO sfnt wrapping <see cref="DivergentCharsetCff"/> as a <c>'CFF '</c> table alongside the
    /// tables <see cref="EmbeddedFontMetrics"/>'s sfnt constructor needs for IsValid — the shape a
    /// CIDFontType2 descendant's <c>/FontFile2</c> takes when CFF outlines are embedded in an
    /// OpenType container (PowerBASIC's body fonts; poppler's own diagnostic names this exact
    /// mismatch: "font type and embedded font file").
    /// </summary>
    private static byte[] Cid2OttoFontBytes()
    {
        byte[] sfnt = MinimalSfnt.Build(
            ("head", ZeroAdvanceSfntFixture.Head()),
            ("hhea", ZeroAdvanceSfntFixture.Hhea(6)),
            ("hmtx", Hmtx6()),
            ("maxp", ZeroAdvanceSfntFixture.Maxp(6)),
            ("CFF ", DivergentCharsetCff()));
        // Flip the sfnt version to 'OTTO' — the real-world marker for CFF-outline OpenType, and the
        // flavour ResolveGlyphId's PRE-FIX code keyed its double-mapping decision on. Cosmetic for
        // THIS fixture (EmbeddedFontMetrics detects CFF via the 'CFF ' table's presence, not the
        // version bytes; SfntFont's version check merely also accepts 'OTTO'), but keeps the byte
        // shape honest against a real OTTO-flavoured /FontFile2.
        sfnt[0] = 0x4F; sfnt[1] = 0x54; sfnt[2] = 0x54; sfnt[3] = 0x4F; // 'OTTO'
        return sfnt;
    }

    /// <summary>Type0 wrapper + descendant <c>/Subtype /CIDFontType2</c>, <c>/CIDToGIDMap
    /// /Identity</c>, <c>/FontFile2</c> = <see cref="Cid2OttoFontBytes"/>.</summary>
    private static Type0Font BuildCid2OttoFont()
    {
        byte[] font = Cid2OttoFontBytes();
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+Cid2Otto"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(4, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEE+Cid2Otto"),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = N("Identity"),
            [N("DW")] = new PdfInteger(1000),
        });
        var type0Dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEE+Cid2Otto"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        doc.AddObject(1, 0, type0Dict);
        return new Type0Font(type0Dict, doc);
    }

    /// <summary>Same charset-bearing CFF, but the descendant declares <c>/Subtype
    /// /CIDFontType0</c> (CID-keyed CFF) and the program is embedded raw via <c>/FontFile3</c> — the
    /// shape where the charset detour IS the correct CID→GID authority (pins that the fix does not
    /// overcorrect).</summary>
    private static Type0Font BuildCid0Font()
    {
        byte[] font = DivergentCharsetCff();
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+Cid0Cff"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile3")] = Ref(3),
        });
        doc.AddObject(4, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType0"),
            [N("BaseFont")] = N("ABCDEE+Cid0Cff"),
            [N("FontDescriptor")] = Ref(2),
            [N("DW")] = new PdfInteger(1000),
        });
        var type0Dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEE+Cid0Cff"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        doc.AddObject(1, 0, type0Dict);
        return new Type0Font(type0Dict, doc);
    }

    // ── fixture honesty ──────────────────────────────────────────────────────────────────────
    [Fact]
    public void Cid2Otto_fixture_program_is_cff_flavoured_and_charset_diverges_from_identity()
    {
        Type0Font font = BuildCid2OttoFont();
        EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
        Assert.NotNull(metrics);
        Assert.True(metrics!.IsValid);
        Assert.True(metrics.IsCffFont);
        // The charset detour a pre-fix ResolveGlyphId would take: CID 5 -> gid 1, NOT identity gid 5.
        Assert.Equal(1, metrics.GetGlyphIdByCid(5));
    }

    // ── the defect: CIDFontType2 descendant must use CIDToGIDMap alone, never the charset ─────
    [Fact]
    public void CidFontType2_with_OTTO_program_resolves_via_CIDToGIDMap_not_the_charset()
    {
        Type0Font font = BuildCid2OttoFont();
        EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
        Assert.NotNull(metrics);

        ushort glyphId = CoreTextRenderer.ResolveGlyphId(metrics!, font, charCode: 5, out _);

        // Identity /CIDToGIDMap: CID 5 -> gid 5. The pre-fix charset detour instead returns gid 1
        // (see Cid2Otto_fixture_program_is_cff_flavoured_and_charset_diverges_from_identity).
        Assert.Equal(5, glyphId);
    }

    // ── the counter-pin: a genuine CID-keyed CFF must still route through the charset ─────────
    [Fact]
    public void CidFontType0_still_resolves_via_the_charset()
    {
        Type0Font font = BuildCid0Font();
        EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
        Assert.NotNull(metrics);

        ushort glyphId = CoreTextRenderer.ResolveGlyphId(metrics!, font, charCode: 5, out _);

        // CIDFontType0: the CFF charset IS the CID->GID authority. CID 5 -> gid 1 via the charset.
        Assert.Equal(1, glyphId);
    }
}
