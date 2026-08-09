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
/// with <see cref="ReferencedFontWalker"/> before writing these tests), so the scenario-specific
/// tests use hand-built fixtures, matching the established convention in
/// <c>ReferencedFontWalkerTests.cs</c> and <c>PreflightSlice12Tests.cs</c> — direct
/// <see cref="PdfDocument"/> object construction rather than files on disk.
///
/// <para><see cref="AssertInventoryMatchesRules"/> is deliberately count-aware, not just
/// set-equality. A pure "the distinct object-number sets match" check cannot fail when the
/// descendant-exclusion logic breaks: every number in a duplicated/phantom row is already a member
/// of the same set via some OTHER entry's <c>Id</c> or <c>ProgramHolderId</c>, so <c>Distinct()</c>
/// silently absorbs the duplicate. Proven by mutation: deleting the <c>continue</c> in
/// <c>FontInventory.Read</c> that excludes a descendant CIDFont from the top-level pass left the
/// original set-only assertion green. The additional invariants below (no duplicate <c>Id</c>, no
/// <c>Id</c> that is also some OTHER entry's <c>ProgramHolderId</c>) are what actually catch it —
/// see <c>task-3-report.md</c>'s fix-report section for the before/after mutation run.</para>
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
        using PdfDocument document = BuildType0Document();
        AssertInventoryMatchesRules(document);

        // Decisive against the descendant-exclusion mutation: this fixture has exactly ONE logical
        // font (a Type0 over one descendant CIDFont). A duplicated/phantom descendant row changes
        // this count even though it would NOT change the distinct-object-number set that
        // AssertInventoryMatchesRules's first assertion checks.
        Assert.Single(FontInventory.Read(document));
    }

    [Fact]
    public void Read_ListsTheSameFontsTheRulesConsiderReferenced_OnADirectFontDictionary()
    {
        using PdfDocument document = BuildDirectFontDictDocument();
        AssertInventoryMatchesRules(document);
    }

    private static void AssertInventoryMatchesRules(PdfDocument document)
    {
        var context = new ConformanceContext(document, ConformanceProfile.PdfA2b);
        IEnumerable<int> viaRules = context.ReferencedFonts
            .Where(d => d.IsIndirect).Select(d => d.ObjectNumber).Distinct().OrderBy(n => n);

        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);
        IEnumerable<int> viaInventory = inventory
            .SelectMany(e => new[] { e.Id.ObjectNumber, e.ProgramHolderId?.ObjectNumber })
            .Where(n => n is not null && n != 0).Select(n => n!.Value).Distinct().OrderBy(n => n);

        Assert.Equal(viaRules, viaInventory);

        // A duplicated or phantom row does not change the DISTINCT set above (the number is already
        // present via some other entry), so it needs its own, count-sensitive checks:
        List<int> ids = inventory.Where(e => e.Id.ObjectNumber != 0).Select(e => e.Id.ObjectNumber).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count()); // no font is its own row twice

        HashSet<int> programHolderIds = inventory
            .Where(e => e.ProgramHolderId is not null && e.ProgramHolderId!.Value.ObjectNumber != e.Id.ObjectNumber)
            .Select(e => e.ProgramHolderId!.Value.ObjectNumber)
            .ToHashSet();
        // A descendant CIDFont must never ALSO stand as its own top-level row — that would mean the
        // walk both treated it as a program holder (correct) AND failed to exclude it (the bug).
        Assert.False(ids.Any(programHolderIds.Contains),
            "a descendant CIDFont object number also appears as a standalone top-level Id");
    }

    /// <summary>A Type0 font (object 20) over its descendant CIDFontType2 (object 21), referenced from
    /// a page's content stream — mirrors <c>ReferencedFontWalkerTests.Type0_font_reaches_its_descendant_cidfont</c>.
    /// Object 22 gives the descendant a /FontFile2 (embedded) and object 23 gives the Type0 a
    /// /ToUnicode, so <see cref="FontInventoryEntry.IsEmbedded"/> and
    /// <see cref="FontInventoryEntry.HasToUnicode"/> both have a real true case to assert against.</summary>
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

    // ── Find resolves either half of a Type0 pair to the same entry, correctly assigned ────────────

    [Fact]
    public void Find_ResolvesEitherHalfOfAType0PairToTheSameEntry()
    {
        using PdfDocument document = BuildType0Document();
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);

        // Assert.Single with NO predicate: a predicate filtering on "ProgramHolderId != Id" would
        // silently pass even if the descendant leaked through as its own extra row (that phantom row
        // has ProgramHolderId == Id, so the predicate would just exclude it rather than catching it).
        FontInventoryEntry pair = Assert.Single(inventory);

        // Pin down WHICH half is which — Find succeeding from both sides does not by itself prove Id
        // and ProgramHolderId were not swapped; the object numbers must be checked directly.
        Assert.Equal(20, pair.Id.ObjectNumber);            // the Type0 wrapper
        Assert.Equal(21, pair.ProgramHolderId!.Value.ObjectNumber); // its descendant CIDFont

        Assert.Same(pair, FontInventory.Find(inventory, pair.Id.ObjectNumber));
        Assert.Same(pair, FontInventory.Find(inventory, pair.ProgramHolderId!.Value.ObjectNumber));
    }

    // ── State fields: IsEmbedded, HasToUnicode, Kind, UsedCodes, PagesUsedOn ────────────────────────

    [Fact]
    public void Read_ComputesEmbeddingToUnicodeKindUsageAndPagesForATypeZeroPair()
    {
        using PdfDocument document = BuildType0Document();
        FontInventoryEntry entry = Assert.Single(FontInventory.Read(document));

        Assert.Equal(FontKind.Type0CidType2, entry.Kind);
        Assert.True(entry.IsEmbedded);     // descendant carries /FontFile2
        Assert.True(entry.HasToUnicode);   // Type0 wrapper carries /ToUnicode
        Assert.False(entry.HasWidths);     // neither /Widths nor /W is present on this fixture
        Assert.True(entry.IsAddressable);  // both halves are indirect
        Assert.Equal([1], entry.UsedCodes);      // content draws code 0x0001
        Assert.Equal([0], entry.PagesUsedOn);    // drawn on page 0, the only page
    }

    // ── SubsetTag stripped into FamilyName; HasWidths true case ─────────────────────────────────────

    [Fact]
    public void Read_StripsTheSubsetTagIntoFamilyName()
    {
        using PdfDocument document = BuildSubsetFontDocument();

        FontInventoryEntry entry = Assert.Single(
            FontInventory.Read(document), e => e.SubsetTag is not null);

        Assert.Equal(6, entry.SubsetTag!.Length);
        Assert.StartsWith(entry.SubsetTag + "+", entry.BaseFont);
        Assert.Equal(entry.BaseFont[7..], entry.FamilyName);

        // The other state fields this fixture happens to cover: no descriptor at all (unembedded),
        // no /ToUnicode, but a real /Widths array (the true case HasWidths otherwise never exercises).
        Assert.Equal(FontKind.Type1, entry.Kind);
        Assert.False(entry.IsEmbedded);
        Assert.False(entry.HasToUnicode);
        Assert.True(entry.HasWidths);
        Assert.Equal([65], entry.UsedCodes);   // content draws "A" = 0x41
        Assert.Equal([0], entry.PagesUsedOn);
    }

    /// <summary>A single Type1 font whose /BaseFont carries a genuine 6-uppercase-letter subset tag,
    /// matching <see cref="PdfFont.IsSubsetFont"/>'s rule, with a /Widths array so the true case of
    /// <see cref="FontInventoryEntry.HasWidths"/> is exercised somewhere in the suite.</summary>
    private static PdfDocument BuildSubsetFontDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("BAAAAA+Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(65),
            [N("Widths")] = new PdfArray(new PdfInteger(722)),
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
        Assert.Equal(FontKind.Type1, entry.Kind);
        Assert.False(entry.IsEmbedded);
        Assert.False(entry.HasToUnicode);
        Assert.False(entry.HasWidths);
        Assert.Equal([65], entry.UsedCodes);  // content draws "A" = 0x41
        Assert.Equal([0], entry.PagesUsedOn);
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

    // ── A direct (non-indirect) descendant CIDFont must still be excluded from the top-level pass ──

    [Fact]
    public void Read_ExcludesADirectNonIndirectDescendantCidFontFromTheTopLevelPass()
    {
        using PdfDocument document = BuildDirectDescendantType0Document();

        FontInventoryEntry entry = Assert.Single(FontInventory.Read(document));

        Assert.Equal(20, entry.Id.ObjectNumber);
        Assert.Equal(FontKind.Type0CidType2, entry.Kind); // not Unknown — the direct descendant is still consulted
        Assert.True(entry.IsEmbedded); // the direct descendant's own /FontDescriptor /FontFile2
        // The direct descendant has no object number of its own to report — FontId cannot address a
        // direct dictionary. That is a known, separately-tracked limitation of FontId (see
        // task-3-report.md's deferred-Minor note), not something this test should paper over.
        Assert.Null(entry.ProgramHolderId);

        AssertInventoryMatchesRules(document);
    }

    /// <summary>A Type0 font (object 20) whose /DescendantFonts[0] is a CIDFontType2 dictionary
    /// embedded DIRECTLY in the array (ISO 32000-1 Table 121 requires the array, not that its
    /// element be indirect) rather than referenced by object number.</summary>
    private static PdfDocument BuildDirectDescendantType0Document()
    {
        var directDescendant = new PdfDictionary
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
        };
        Assert.False(directDescendant.IsIndirect); // guards the fixture's own premise

        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfStream(new PdfDictionary { [N("Length1")] = new PdfInteger(0) }, []));
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(directDescendant),
        });
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

    // ── PagesUsedOn: under-report (Form XObject) and over-report (present but never drawn) ─────────

    [Fact]
    public void Read_PagesUsedOnReflectsActualDrawingNotResourcePresence()
    {
        using PdfDocument document = BuildFormXObjectAndUnusedFontDocument();
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);

        // Reachable ONLY through a Form XObject's own /Resources — a page-level-only resource scan
        // (the earlier, buggy implementation) would report this font as unused on every page.
        FontInventoryEntry drawnViaFormXObject = inventory.Single(e => e.Id.ObjectNumber == 30);
        Assert.Equal([65], drawnViaFormXObject.UsedCodes);
        Assert.Equal([0], drawnViaFormXObject.PagesUsedOn);

        // Present in the page's OWN /Font resource dictionary but never selected by a Tf/Tj — a
        // resource-presence scan would report this font as used on page 0; it is not.
        FontInventoryEntry presentButUnused = inventory.Single(e => e.Id.ObjectNumber == 40);
        Assert.Empty(presentButUnused.UsedCodes);
        Assert.Empty(presentButUnused.PagesUsedOn);
    }

    /// <summary>One page with two Type1 fonts: object 30 is drawn only via a Form XObject's own
    /// resources (the recursion a page-level-only resource scan misses); object 40 sits in the
    /// page's own /Font resources but the content stream never selects it with /Tf (the "present but
    /// unused" case a resource-presence scan would over-report as used).</summary>
    private static PdfDocument BuildFormXObjectAndUnusedFontDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"), [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("UsedViaFormXObject"), [N("Encoding")] = N("WinAnsiEncoding"),
        });
        doc.AddObject(40, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"), [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("PresentButNeverDrawn"), [N("Encoding")] = N("WinAnsiEncoding"),
        });
        doc.AddObject(50, 0, new PdfStream(
            new PdfDictionary
            {
                [N("Type")] = N("XObject"),
                [N("Subtype")] = N("Form"),
                [N("BBox")] = Rect(0, 0, 100, 100),
                [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
            },
            Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("/Fm0 Do")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F1")] = Ref(40) },
                [N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(50) },
            },
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
