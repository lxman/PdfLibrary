using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>F-3: regenerating a subset declaration from the embedded program.
///
/// <para>The enumeration under test is SHARED with FontSubsetCoverageRule. That sharing is the
/// correctness guarantee, not a tidiness preference: the rule's comparison is bidirectional, so a
/// repair that enumerated the program even slightly differently would write a declaration the rule
/// still faults — a fix that reports success and changes nothing.</para></summary>
public sealed class SubsetDeclarationTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    // ── document scaffold, copied from PreflightSlice27Tests' CidDoc (the rule's own fixture) ─────────────
    private static PdfDocument DocWith(PdfDictionary font, params (int, PdfObject)[] extra)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, font);
        foreach ((int num, PdfObject obj) in extra)
            doc.AddObject(num, 0, obj);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT ET")));
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) },
            },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(21) });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    private static byte[] FontBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    /// <summary>Builds a CIDFontType2 (PublicPixel) document, matching the shape of PreflightSlice27Tests'
    /// CidDoc, and returns the document, the descendant CIDFont dictionary, and its parsed metrics.
    /// <paramref name="customMap"/> null → an Identity CIDToGIDMap (no entry at all); non-null → a
    /// CIDToGIDMap stream holding that mapping.</summary>
    private static (PdfDocument Doc, PdfDictionary CidDict, EmbeddedFontMetrics Metrics) TrueTypeCidFont(
        byte[]? customMap)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(4),
            [N("FontFile2")] = Ref(3),
        };

        var cidFont = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.Latin1.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.Latin1.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = Ref(2),
        };
        if (customMap is not null)
            cidFont[N("CIDToGIDMap")] = Ref(6);

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };

        var extra = new List<(int, PdfObject)>
        {
            (2, descriptor),
            (3, new PdfStream(new PdfDictionary(), FontBytes())),
            (4, cidFont),
        };
        if (customMap is not null)
            extra.Add((6, new PdfStream(new PdfDictionary(), customMap)));

        PdfDocument doc = DocWith(font, extra.ToArray());

        var type0Font = (Type0Font)PdfFont.Create(font, doc)!;
        EmbeddedFontMetrics metrics = type0Font.GetEmbeddedMetrics()!;

        return (doc, cidFont, metrics);
    }

    /// <summary>An Identity CIDToGIDMap yields CIDs [0, NumberOfHMetrics), matching what the rule
    /// computed before the extraction. PublicPixel.ttf is the same fixture the rule's own tests use.</summary>
    [Fact]
    public void Identity_cid_to_gid_map_enumerates_the_metric_range()
    {
        (PdfDocument doc, PdfDictionary cidDict, EmbeddedFontMetrics metrics) = TrueTypeCidFont(customMap: null);

        (IReadOnlySet<int>? cids, Func<int, bool> contains) =
            SubsetProgramGlyphs.ProgramCids(doc, cidDict, metrics);

        Assert.NotNull(cids);
        Assert.Equal(metrics.NumberOfHMetrics, cids!.Count);
        Assert.Contains(1, cids);
        Assert.True(contains(1));
        Assert.False(contains(0));
    }

    /// <summary>A custom CIDToGIDMap enumerates the mapping, not the metric range — the two differ,
    /// and picking the wrong one writes a declaration for glyphs the program does not have.</summary>
    [Fact]
    public void A_custom_cid_to_gid_map_enumerates_the_mapping()
    {
        // CIDs 1 and 2 map to GIDs 1 and 2; CID 3 maps to GID 0 (absent).
        byte[] map = [0, 0, 0, 1, 0, 2, 0, 0];
        (PdfDocument doc, PdfDictionary cidDict, EmbeddedFontMetrics metrics) = TrueTypeCidFont(map);

        (IReadOnlySet<int>? cids, _) = SubsetProgramGlyphs.ProgramCids(doc, cidDict, metrics);

        Assert.NotNull(cids);
        Assert.Contains(1, cids!);
        Assert.Contains(2, cids!);
    }
}
