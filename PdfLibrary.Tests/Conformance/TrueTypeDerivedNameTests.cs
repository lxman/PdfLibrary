using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Task 10 fix round 2 (issues 27-28 follow-up review, 2026-08-16): round 1 gated BOTH
/// <c>ResolveSimpleGlyph</c> branches (CFF and TrueType) on <see cref="PdfFontEncoding.IsDerivedName"/>.
/// Round 2 removed the TrueType gate: analytically, the TrueType branch only ever uses the encoding
/// name as a courier for the encoding's OWN Unicode value (<c>GlyphList.GetUnicode(glyphName)</c>)
/// before keying the font's cmap directly by that Unicode value — a derived name's Unicode IS the
/// encoding's own Annex-D/WinAnsi value (<c>SetUnicode</c> wrote it; the reverse lookup that derived
/// the name is a lossless inversion of the same map), so whether the name arrived via
/// <c>SetCharacterName</c> or <c>SetUnicode</c> carries no information for this branch. Empirically:
/// the TrueType provenance gate cleared ZERO false positives across both corpora and deleted the
/// corpora's only TrueType glyph-present detection (local-708's <c>StudentLoan1098E.pdf</c>,
/// veraPDF-confirmed genuine).
///
/// <para>This file is the direct regression gate the TrueType arm never had (round 1 added no test for
/// it at all — the existing PreflightSlice19 TrueType tests either use an EXPLICIT
/// <c>/Differences</c> name, or a code that IS present in the program, so none of them would have
/// caught a wrongly-added derived-name gate here). Uses a hand-built minimal-but-valid TrueType
/// program (mirroring <c>FontProgramZeroAdvanceTests</c>' and <c>CmapSubtablePreferenceTests</c>'
/// builders — a real (3,1) Windows-UnicodeBMP format-4 cmap subtable covering exactly ONE codepoint,
/// U+0041 'A' → gid 1) with a WinAnsi-base, no-<c>/Differences</c> encoding, so every shown code's
/// name is SetUnicode-derived.</para>
/// </summary>
public class TrueTypeDerivedNameTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void U32(List<byte> b, uint v)
    { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    private static byte[] Head()
    {
        var b = new List<byte>();
        U32(b, 0x00010000); U32(b, 0); U32(b, 0); U32(b, 0x5F0F3CF5);
        U16(b, 0); U16(b, 1000);
        for (var i = 0; i < 16; i++) b.Add(0);
        U16(b, 0); U16(b, 0); U16(b, 0); U16(b, 0);
        U16(b, 0); U16(b, 8); U16(b, 2); U16(b, 0); U16(b, 0);
        return b.ToArray();
    }

    private static byte[] Maxp(ushort numGlyphs)
    {
        var b = new List<byte>();
        U32(b, 0x00010000);
        U16(b, numGlyphs);
        for (var i = 0; i < 13; i++) U16(b, 0);
        return b.ToArray();
    }

    private static byte[] Hhea(ushort numberOfHMetrics)
    {
        var b = new List<byte>();
        U32(b, 0x00010000);
        U16(b, 800); U16(b, unchecked((ushort)-200)); U16(b, 0); U16(b, 500);
        for (var i = 0; i < 3; i++) U16(b, 0);
        U16(b, 1); U16(b, 0); U16(b, 0);
        for (var i = 0; i < 4; i++) U16(b, 0);
        U16(b, 0); U16(b, numberOfHMetrics);
        return b.ToArray();
    }

    /// <summary>gid 0 and gid 1 both advance 600 units — irrelevant to these tests (glyph-present
    /// only, no width comparison), just needs to be present and non-zero so it never triggers the
    /// unrelated zero-advance skip.</summary>
    private static byte[] Hmtx()
    {
        var b = new List<byte>();
        U16(b, 600); U16(b, 0);
        U16(b, 600); U16(b, 0);
        return b.ToArray();
    }

    /// <summary>Format 4 with a single one-character segment plus the mandatory 0xFFFF terminator —
    /// covers ONLY <paramref name="codePoint"/>; any other Unicode value queried against this
    /// subtable is genuinely absent (gid 0), not a lookup gap. Copied from
    /// <c>CmapSubtablePreferenceTests.Format4</c>.</summary>
    private static byte[] Format4(ushort codePoint, ushort glyphId)
    {
        var t = new List<byte>();
        const int segCount = 2;
        U16(t, 4);
        U16(t, 16 + segCount * 8);
        U16(t, 0);
        U16(t, segCount * 2);
        U16(t, 2); U16(t, 0); U16(t, 0);
        U16(t, codePoint); U16(t, 0xFFFF);
        U16(t, 0);
        U16(t, codePoint); U16(t, 0xFFFF);
        U16(t, (ushort)(glyphId - codePoint)); U16(t, 1);
        U16(t, 0); U16(t, 0);
        return t.ToArray();
    }

    /// <summary>A lone (3,1) Windows-UnicodeBMP subtable mapping U+0041 'A' -> gid 1. No other
    /// codepoint (including 'B', U+0042) resolves through it.</summary>
    private static byte[] CmapWindowsUnicodeOnly()
    {
        byte[] win = Format4(0x0041, 1);
        const int headerSize = 4 + 8;
        var f = new List<byte>();
        U16(f, 0); U16(f, 1);
        U16(f, 3); U16(f, 1); U32(f, headerSize);
        f.AddRange(win);
        return f.ToArray();
    }

    /// <summary>A lone (1,0) Macintosh-Roman format-0 subtable mapping code 0x41 -> gid 1. No (3,x)
    /// or (0,x) Unicode-capable record at all, so <c>HasUnicodeCmapEncoding</c> is false regardless
    /// of what any encoding derives.</summary>
    private static byte[] CmapMacOnly()
    {
        var t = new List<byte>();
        U16(t, 0); U16(t, 262); U16(t, 0);
        for (var i = 0; i < 256; i++) t.Add(i == 0x41 ? (byte)1 : (byte)0);
        const int headerSize = 4 + 8;
        var f = new List<byte>();
        U16(f, 0); U16(f, 1);
        U16(f, 1); U16(f, 0); U32(f, headerSize);
        f.AddRange(t);
        return f.ToArray();
    }

    private static byte[] FontBytes(byte[] cmap) => MinimalSfnt.Build(
        ("head", Head()),
        ("maxp", Maxp(2)),
        ("hhea", Hhea(2)),
        ("hmtx", Hmtx()),
        ("cmap", cmap),
        ("glyf", new byte[4]));

    /// <summary>One-page document: a simple TrueType font (no <c>/Differences</c>, so /Encoding
    /// alone — <c>/WinAnsiEncoding</c> unless overridden — governs every code's name), showing
    /// <paramref name="code"/>.</summary>
    private static PdfDocument BuildDoc(byte[] cmap, byte code, int flags = 32, PdfObject? encoding = null)
    {
        byte[] font = FontBytes(cmap);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+DerivedNameTrueType"),
            [N("Flags")] = new PdfInteger(flags),
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+DerivedNameTrueType"),
            [N("FirstChar")] = new PdfInteger(code),
            [N("LastChar")] = new PdfInteger(code),
            [N("Widths")] = new PdfArray(new PdfInteger(600)),
            [N("Encoding")] = encoding ?? N("WinAnsiEncoding"),
            [N("FontDescriptor")] = Ref(2),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"BT /F0 12 Tf <{code:X2}> Tj ET")));
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(21), [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) } },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(22)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(21) });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    private static Finding[] Run(PdfDocument doc) =>
        new FontProgramRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();

    private static string? Clause(Finding f) => ParitySnapshot.ClauseKey(f.Clause);

    // ── fixture honesty ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fixture_cmap_maps_only_A_not_B()
    {
        var metrics = new PdfLibrary.Fonts.Embedded.EmbeddedFontMetrics(FontBytes(CmapWindowsUnicodeOnly()));
        Assert.True(metrics.IsValid);
        Assert.True(metrics.HasUnicodeCmapEncoding());
        Assert.Equal(1, metrics.GetGlyphId(0x0041));
        Assert.Equal(0, metrics.GetGlyphId(0x0042));
    }

    // ── direction 1: a derived-name code absent from the cmap must still yield the finding ──────

    [Fact]
    public void Derived_name_code_absent_from_cmap_yields_glyph_present_finding()
    {
        // Code 'B' (0x42): /Encoding /WinAnsiEncoding derives the name "B" for it (no /Differences),
        // and "B" is genuinely absent from this font's cmap. Round 1 wrongly suppressed this with
        // the TrueType derived-name gate; round 2 removes that gate, so the finding must return.
        Finding f = Assert.Single(Run(BuildDoc(CmapWindowsUnicodeOnly(), code: (byte)'B')),
            x => Clause(x) == "6.2.11.4.1");
        Assert.Contains("renders a glyph that is not present", f.Message);
    }

    // ── direction 2: a derived-name code PRESENT in the cmap must not falsely fire ───────────────

    [Fact]
    public void Derived_name_code_present_in_cmap_yields_no_finding()
    {
        // Code 'A' (0x41): also WinAnsi-derived, but genuinely present in the cmap (gid 1).
        Assert.Empty(Run(BuildDoc(CmapWindowsUnicodeOnly(), code: (byte)'A')));
    }

    // ── the existing FP-safe guards must still yield Unknown regardless of the derived-name fix ──

    [Fact]
    public void Symbolic_font_with_derived_name_absent_from_cmap_still_yields_unknown()
    {
        // Same absent-glyph shape as the direction-1 gate, but /Flags declares symbolic (bit 3, value
        // 4): the pre-existing symbolic guard must still suppress the finding, independent of
        // provenance — a derived name was never the reason this guard exists.
        Assert.Empty(Run(BuildDoc(CmapWindowsUnicodeOnly(), code: (byte)'B', flags: 4)));
    }

    [Fact]
    public void No_unicode_cmap_font_with_derived_name_absent_still_yields_unknown()
    {
        // Mac-only (1,0) cmap: HasUnicodeCmapEncoding() is false, so the pre-existing "no trustworthy
        // Unicode-capable subtable" guard must still suppress the finding for 'B' (0x42, which this
        // cmap doesn't map either — format-0 array entry 0x42 is 0), independent of provenance.
        Assert.Empty(Run(BuildDoc(CmapMacOnly(), code: (byte)'B')));
    }
}
