using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Issue 35: <see cref="FontProgramRule"/> deduplicated findings by <c>/BaseFont</c> name, so two
/// sibling indirect font objects sharing the same (subset-tag-collided or otherwise identical)
/// base name collapsed onto ONE finding — the remediation planner then patched only the named
/// holder, and the sibling's own defect resurfaced as a fresh finding after the "fix". The dedup
/// key now includes object identity (<see cref="FontProgramRule.DedupKey"/>): one finding per
/// font OBJECT per sub-check, falling back to base-font name only when the dictionary has no
/// object identity (a direct, non-indirect font dictionary).
/// </summary>
public class FontProgramRuleDedupTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>Two independent indirect font objects (1 and 4), both /BaseFont /ABCDEE+Test,
    /// both TrueType, both shown, both declaring a /Widths value that mismatches the same
    /// embedded program's real advance (450) by well over <see cref="FontProgramRule.WidthTolerance"/>.
    /// Clones the object-assembly style of WidthPatchProposalTests.NotdefOnlyDoc / the
    /// F-4a ZeroAdvance fixtures. Shown code is 10 (not the brief's illustrative 'A'/65): the
    /// shared <see cref="ZeroAdvanceSfntFixture"/> program's lone Mac-Roman cmap subtable only
    /// maps code 10 → gid 1 (used verbatim per the brief), so code 10 is what actually reaches
    /// the raw-code fallback in ProgramWidthResolver.TrueTypeAdvance — matching every other test
    /// built on this fixture (FontProgramZeroAdvanceTests, WidthPatchProposalTests).</summary>
    private static PdfDocument TwoSiblingWidthMismatchDoc()
    {
        byte[] fontBytes = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();

        // F0: font object 1, descriptor 2, program 3.
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(fontBytes.Length) }, fontBytes));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+Test"),
            [N("Flags")] = new PdfInteger(32),     // non-symbolic
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+Test"),
            [N("FirstChar")] = new PdfInteger(10),
            [N("LastChar")] = new PdfInteger(10),
            [N("Widths")] = new PdfArray(new PdfInteger(1000)),
            [N("FontDescriptor")] = Ref(2),
        });

        // F1: font object 4, descriptor 5, program 6 — same /BaseFont, an entirely separate
        // embedding of the same (defective) program shape.
        doc.AddObject(6, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(fontBytes.Length) }, fontBytes));
        doc.AddObject(5, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+Test"),
            [N("Flags")] = new PdfInteger(32),
            [N("FontFile2")] = Ref(6),
        });
        doc.AddObject(4, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+Test"),
            [N("FirstChar")] = new PdfInteger(10),
            [N("LastChar")] = new PdfInteger(10),
            [N("Widths")] = new PdfArray(new PdfInteger(1000)),
            [N("FontDescriptor")] = Ref(5),
        });

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0A> Tj /F1 12 Tf <0A> Tj ET")));
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1), [N("F1")] = Ref(4) },
            },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(21),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    [Fact]
    public void Two_sibling_objects_sharing_a_base_font_name_each_get_their_own_width_finding()
    {
        PdfDocument doc = TwoSiblingWidthMismatchDoc();
        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => f.Clause?.EndsWith("6.2.11.5") == true).ToArray();

        // Issue 35: the per-base-font-name dedup reported ONE finding for the pair, so the
        // remediation planner only ever patched the named object and the sibling resurfaced
        // its own finding after the fix.
        Assert.Equal(2, findings.Length);
        Assert.Equal(2, findings.Select(f => f.ObjectNumber).Distinct().Count());
    }

    // ── direct-dictionary fallback (no object identity to key on) ────────────────────────────
    //
    // Building a shown direct-dictionary font through the corpus-style fixture helpers above
    // would require the content/resource assembly to reference the SAME PdfDictionary instance
    // by value (not by indirect reference) from two resource names, which the existing fixture
    // helpers have no support for and which would mostly be exercising the resource-walk, not
    // DedupKey itself. DedupKey is `internal` (Step 3) specifically so this half of the key can
    // be pinned directly against a hand-built, never-added-to-a-document PdfFont instead —
    // narrower, and it still proves the exact fallback branch (FontDictionary.IsIndirect false
    // because the dictionary was never registered via PdfDocument.AddObject).
    [Fact]
    public void Direct_dictionary_font_dedups_by_base_font_name()
    {
        var dictA = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("Direct+Test"),
        };
        var dictB = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("Direct+Test"),
        };
        Assert.False(dictA.IsIndirect);
        Assert.False(dictB.IsIndirect);

        PdfFont? fontA = PdfFont.Create(dictA);
        PdfFont? fontB = PdfFont.Create(dictB);
        Assert.NotNull(fontA);
        Assert.NotNull(fontB);

        // Two distinct direct dictionaries with the same /BaseFont collapse onto the SAME key —
        // there is no object identity to distinguish them, so name is the only signal available.
        Assert.Equal(FontProgramRule.DedupKey(fontA!), FontProgramRule.DedupKey(fontB!));
        Assert.Equal("name:Direct+Test", FontProgramRule.DedupKey(fontA!));
    }

    [Fact]
    public void Indirect_dictionary_font_dedups_by_object_number_not_name()
    {
        var doc = new PdfDocument();
        var dictA = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("Same+Name"),
        };
        var dictB = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("Same+Name"),
        };
        doc.AddObject(1, 0, dictA);
        doc.AddObject(4, 0, dictB);

        PdfFont? fontA = PdfFont.Create(dictA, doc);
        PdfFont? fontB = PdfFont.Create(dictB, doc);
        Assert.NotNull(fontA);
        Assert.NotNull(fontB);

        Assert.Equal("obj:1", FontProgramRule.DedupKey(fontA!));
        Assert.Equal("obj:4", FontProgramRule.DedupKey(fontB!));
        Assert.NotEqual(FontProgramRule.DedupKey(fontA!), FontProgramRule.DedupKey(fontB!));
    }
}
