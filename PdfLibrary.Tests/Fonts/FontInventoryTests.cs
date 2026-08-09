using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Tests for <see cref="FontInventory"/> — the public font-inventory read model built on top of
/// <see cref="ReferencedFontWalker"/> (Task 2). No <c>TestFixtures.AllCorpusPaths()</c> helper exists
/// in this project (confirmed by Task 2 — only <c>CorpusHarness</c>, which walks an external,
/// unvendored veraPDF corpus). The corpus-wide consistency theory below instead runs over the
/// vendored <c>TestPDFs/</c> directory. None of those files contain a Type0 font, a document with
/// exactly one subset-tagged font, or a direct (non-indirect) font dictionary (confirmed by scanning
/// with <see cref="ReferencedFontWalker"/> before writing these tests), so the three scenario-specific
/// tests use hand-built fixtures, matching the established convention in
/// <c>ReferencedFontWalkerTests.cs</c> and <c>PreflightSlice12Tests.cs</c> — direct
/// <see cref="PdfDocument"/> object construction rather than files on disk.
/// </summary>
public class FontInventoryTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    // ── §4 consistency guarantee: the inventory and the conformance rules must agree ───────────────

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void Read_ListsTheSameFontsTheRulesConsiderReferenced(string fixture)
    {
        using PdfDocument document = PdfDocument.Load(fixture, "");
        AssertInventoryMatchesRules(document);
    }

    public static TheoryData<string> CorpusDocuments()
    {
        string dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "TestPDFs"));
        var data = new TheoryData<string>();
        foreach (string file in Directory.GetFiles(dir, "*.pdf"))
            data.Add(file);
        return data;
    }

    [Fact]
    public void Read_ListsTheSameFontsTheRulesConsiderReferenced_OnAType0Pair()
    {
        AssertInventoryMatchesRules(BuildType0Document());
    }

    [Fact]
    public void Read_ListsTheSameFontsTheRulesConsiderReferenced_OnADirectFontDictionary()
    {
        AssertInventoryMatchesRules(BuildDirectFontDictDocument());
    }

    private static void AssertInventoryMatchesRules(PdfDocument document)
    {
        var context = new ConformanceContext(document, ConformanceProfile.PdfA2b);
        IEnumerable<int> viaRules = context.ReferencedFonts
            .Where(d => d.IsIndirect).Select(d => d.ObjectNumber).Distinct().OrderBy(n => n);

        IEnumerable<int> viaInventory = FontInventory.Read(document)
            .SelectMany(e => new[] { e.Id.ObjectNumber, e.ProgramHolderId?.ObjectNumber })
            .Where(n => n is not null && n != 0).Select(n => n!.Value).Distinct().OrderBy(n => n);

        Assert.Equal(viaRules, viaInventory);
    }

    // ── Find resolves either half of a Type0 pair to the same entry ────────────────────────────────

    [Fact]
    public void Find_ResolvesEitherHalfOfAType0PairToTheSameEntry()
    {
        using PdfDocument document = BuildType0Document();
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);

        FontInventoryEntry pair = Assert.Single(inventory, e => e.ProgramHolderId is not null
                                                             && e.ProgramHolderId != e.Id);

        Assert.Same(pair, FontInventory.Find(inventory, pair.Id.ObjectNumber));
        Assert.Same(pair, FontInventory.Find(inventory, pair.ProgramHolderId!.Value.ObjectNumber));
    }

    /// <summary>A Type0 font (object 20) over its descendant CIDFontType2 (object 21), referenced from
    /// a page's content stream — mirrors <c>ReferencedFontWalkerTests.Type0_font_reaches_its_descendant_cidfont</c>.</summary>
    private static PdfDocument BuildType0Document()
    {
        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfStream(new PdfDictionary { [N("Length1")] = new PdfInteger(0) }, []));
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"),
                [N("FontName")] = N("CIDFontX"),
                [N("FontFile2")] = Ref(22),
            },
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(21)),
            [N("ToUnicode")] = Ref(23),
        });
        doc.AddObject(23, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(
            "/CIDInit /ProcSet findresource begin\n"
            + "1 begincidrange <0001> <0001> 65\nendcidrange\n"
            + "end")));
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(20) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    // ── SubsetTag stripped into FamilyName ──────────────────────────────────────────────────────────

    [Fact]
    public void Read_StripsTheSubsetTagIntoFamilyName()
    {
        using PdfDocument document = BuildSubsetFontDocument();

        FontInventoryEntry entry = Assert.Single(
            FontInventory.Read(document), e => e.SubsetTag is not null);

        Assert.Equal(6, entry.SubsetTag!.Length);
        Assert.StartsWith(entry.SubsetTag + "+", entry.BaseFont);
        Assert.Equal(entry.BaseFont[7..], entry.FamilyName);
    }

    /// <summary>A single Type1 font whose /BaseFont carries a genuine 6-uppercase-letter subset tag,
    /// matching <see cref="PdfFont.IsSubsetFont"/>'s rule.</summary>
    private static PdfDocument BuildSubsetFontDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("BAAAAA+Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    // ── Direct (non-indirect) font dictionary is unaddressable ─────────────────────────────────────

    [Fact]
    public void Read_MarksADirectFontDictionaryUnaddressable()
    {
        using PdfDocument document = BuildDirectFontDictDocument();

        FontInventoryEntry entry = Assert.Single(FontInventory.Read(document));

        Assert.False(entry.IsAddressable);
    }

    /// <summary>A page whose /Font resource entry is a font dictionary embedded DIRECTLY (never
    /// registered via <c>AddObject</c>, so <c>IsIndirect</c> is false) rather than an indirect
    /// reference — validated by asserting <c>IsIndirect == false</c> before relying on it.</summary>
    private static PdfDocument BuildDirectFontDictDocument()
    {
        var directFont = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
        };
        Assert.False(directFont.IsIndirect); // guards the fixture's own premise

        var doc = new PdfDocument();
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = directFont } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }
}
