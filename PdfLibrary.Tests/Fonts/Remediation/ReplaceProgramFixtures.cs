using System.Collections.Generic;
using System.IO;
using System.Text;
using CffTestFixtures;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using PdfLibrary.Tests.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4b Task 5/6 shared fixture: the dead-CID Type0 documents (CIDFontType2 and CIDFontType0
/// descendants) used by both <see cref="ReplaceProgramProposalTests"/> (Task 5, planner) and
/// <c>ReplaceProgramApplyTests</c> (Task 6, editor write + close-by-construction gate) so the two
/// suites cannot silently diverge on the document shape the gate relies on — the same reason
/// <see cref="WidthPatchFixtures"/> exists for F-4a.
/// </summary>
internal static class ReplaceProgramFixtures
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    public static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        new(provider ?? new StubFontProvider(null));

    public static byte[] LiberationSansBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "Liberation", "LiberationSans-Regular.ttf"));

    /// <summary>Same bfchar-block builder as <c>CidReplacementMapTests.BfChar</c>, but returning raw
    /// stream bytes (rather than a parsed <see cref="ToUnicodeCMap"/>) so a fixture can attach it as a
    /// <c>/ToUnicode</c> stream object.</summary>
    public static byte[] BfCharBytes(IReadOnlyList<(int Code, string Hex)> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine($"{entries.Count} beginbfchar");
        foreach ((int code, string hex) in entries)
            sb.AppendLine($"<{code:X4}> <{hex}>");
        sb.AppendLine("endbfchar");
        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void AddCidSystemInfo(PdfDictionary descendant)
    {
        descendant[N("CIDSystemInfo")] = new PdfDictionary
        {
            [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
            [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
            [N("Supplement")] = new PdfInteger(0),
        };
    }

    /// <summary>
    /// The shared dead-CID Type0/CIDFontType2 fixture (spec brief): program =
    /// <see cref="ZeroAdvanceSfntFixture.FontBytes"/> (2 glyphs), descendant /CIDToGIDMap /Identity,
    /// /DW 1000, /W [65 [500]]; wrapper /Encoding /Identity-H, /BaseFont /ABCDEF+DeadFace. Content
    /// shows <paramref name="contentHex"/> (default <c>0000 0041</c>: CID 0 → .notdef → the 6.2.11.8
    /// finding; CID 0x41 → a live-by-the-rule glyph). /ToUnicode carries
    /// <paramref name="toUnicodeEntries"/> (default CID 0 → 'A', CID 0x41 → 'B') unless
    /// <paramref name="includeToUnicode"/> is false.
    /// </summary>
    public static PdfDocument DeadCid2Doc(
        IReadOnlyList<(int Code, string Hex)>? toUnicodeEntries = null,
        bool includeToUnicode = true,
        string contentHex = "0000 0041")
    {
        IReadOnlyList<(int Code, string Hex)> entries = toUnicodeEntries ?? [(0x0000, "0041"), (0x0041, "0042")];

        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+DeadFace"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile2")] = Ref(3),
        });
        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+DeadFace"),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = N("Identity"),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x41), new PdfArray(new PdfInteger(500))),
        };
        AddCidSystemInfo(descendant);
        doc.AddObject(4, 0, descendant);

        var type0Dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+DeadFace"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        if (includeToUnicode)
        {
            doc.AddObject(5, 0, new PdfStream(new PdfDictionary(), BfCharBytes(entries)));
            type0Dict[N("ToUnicode")] = Ref(5);
        }
        doc.AddObject(1, 0, type0Dict);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"BT /F0 12 Tf <{contentHex}> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    /// <summary>A dead-CID Type0/CIDFontType0 (CID-keyed CFF descendant) fixture, with a LIVE CID
    /// drawn alongside the dead one (as the CID2 fixture does), so <c>RestoredCodeCount</c> can
    /// discriminate 1 restored from 2. CID 0 is ALWAYS .notdef
    /// (<see cref="EmbeddedFontMetrics.GetGlyphIdByCid"/>'s own hardcoded rule) regardless of the
    /// charset, but proving the OTHER drawn CID (0x41) is genuinely NOT dead in the OLD program needs
    /// a real charset entry for it — built the same way
    /// <c>ResolveGlyphIdCid2OttoTests.DivergentCharsetCff</c> does (a non-CID
    /// <see cref="MinimalCff.Build"/> whose charset entries <see cref="EmbeddedFontMetrics.GetGlyphIdByCid"/>
    /// reads as CIDs regardless of the CFF's own CID-ness): gid 1 ↔ CID 0x41. /ToUnicode maps CID 0 →
    /// 'A', CID 0x41 → 'B'.</summary>
    public static PdfDocument DeadCid0Doc()
    {
        byte[] font = MinimalCff.Build(charsetOperand: null, numGlyphs: 2, customCharsetSids: [0x41]);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+DeadCid0"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile3")] = Ref(3),
        });
        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType0"),
            [N("BaseFont")] = N("ABCDEF+DeadCid0"),
            [N("FontDescriptor")] = Ref(2),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x41), new PdfArray(new PdfInteger(500))),
        };
        AddCidSystemInfo(descendant);
        doc.AddObject(4, 0, descendant);

        var type0Dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+DeadCid0"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        doc.AddObject(5, 0, new PdfStream(new PdfDictionary(),
            BfCharBytes([(0x0000, "0041"), (0x0041, "0042")])));
        type0Dict[N("ToUnicode")] = Ref(5);
        doc.AddObject(1, 0, type0Dict);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0000 0041> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font: 1);
        return doc;
    }
}
