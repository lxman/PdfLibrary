using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4a Task 3: the planner's <c>font-program</c> arm — <see cref="PatchWidthsProposal"/> and
/// <see cref="FontRemediationPlanner.ProposeWidthPatch"/>. Fixtures reuse
/// <see cref="ZeroAdvanceSfntFixture"/>'s byte builders (shared with
/// <c>ProgramWidthResolverTests</c> and <c>FontProgramZeroAdvanceTests</c>), and the same document
/// shape as those files' <c>Doc</c>/<c>ZeroAdvanceDoc</c> — a TrueType font, <c>/FirstChar 10</c>,
/// code(s) shown in a Tj hex string against a lone (1,0) Mac-Roman format-6 cmap subtable.
/// </summary>
public sealed class WidthPatchProposalTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    // Task 4 (WidthPatchApplyTests) reuses this document shape and planner helper for the
    // close-by-construction gate, so both live in the shared WidthPatchFixtures now.
    private static FontRemediationPlanner Planner() => WidthPatchFixtures.Planner();

    private static PdfDocument MismatchDoc() => WidthPatchFixtures.MismatchDoc();

    /// <summary>/Widths [0] against a real (nonzero) program advance on the one drawn code.</summary>
    private static PdfDocument DeclaredZeroDoc()
    {
        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+ZeroAdvance"),
            [N("Flags")] = new PdfInteger(32),
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+ZeroAdvance"),
            [N("FirstChar")] = new PdfInteger(10),
            [N("LastChar")] = new PdfInteger(10),
            [N("Widths")] = new PdfArray(new PdfInteger(0)),
            [N("FontDescriptor")] = Ref(2),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0A> Tj ET")));
        AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    /// <summary>A lone (1,0) Mac-Roman format-6 subtable mapping BOTH code 10 and code 11 to gid 1
    /// — the two-entry sibling of ZeroAdvanceSfntFixture.CmapMacFormat6 (single entry).</summary>
    private static byte[] CmapMacFormat6TwoEntriesSameGid()
    {
        var b = new List<byte>();
        U16(b, 0);                     // table version
        U16(b, 1);                     // numTables
        U16(b, 1); U16(b, 0);          // platform 1 (Macintosh), encoding 0 (Roman)
        U32(b, 12);                    // subtable offset
        U16(b, 6);                     // format 6
        U16(b, 14);                    // length: 10-byte header + 2 × u16 entries
        U16(b, 0);                     // language
        U16(b, 10);                    // firstCode = 10
        U16(b, 2);                     // entryCount
        U16(b, 1);                     // code 10 -> gid 1
        U16(b, 1);                     // code 11 -> gid 1
        return b.ToArray();
    }

    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void U32(List<byte> b, uint v)
    { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    /// <summary>Two codes (10, 11) both resolving to gid 1, declaring conflicting widths (507 vs 300).</summary>
    private static PdfDocument ConflictingWidthsDoc()
    {
        byte[] font = MinimalSfnt.Build(
            ("head", ZeroAdvanceSfntFixture.Head()),
            ("maxp", ZeroAdvanceSfntFixture.Maxp(2)),
            ("hhea", ZeroAdvanceSfntFixture.Hhea(2)),
            ("hmtx", ZeroAdvanceSfntFixture.Hmtx(gid1Advance: 450)),
            ("cmap", CmapMacFormat6TwoEntriesSameGid()),
            ("glyf", new byte[4]));

        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEE+ZeroAdvance"),
            [N("Flags")] = new PdfInteger(32),
            [N("FontFile2")] = Ref(3),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEE+ZeroAdvance"),
            [N("FirstChar")] = new PdfInteger(10),
            [N("LastChar")] = new PdfInteger(11),
            [N("Widths")] = new PdfArray(new PdfInteger(507), new PdfInteger(300)),
            [N("FontDescriptor")] = Ref(2),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0A0B> Tj ET")));
        AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    private static void AddSinglePageCatalog(PdfDocument doc, int font)
    {
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(font) },
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
    }

    [Fact]
    public void A_width_mismatch_yields_a_patch_proposal_targeting_the_program_holder()
    {
        PdfDocument doc = MismatchDoc();
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);
        PatchWidthsProposal patch = Assert.IsType<PatchWidthsProposal>(Assert.Single(result.Fonts));
        Assert.Equal(1, patch.Font.ObjectNumber);      // simple font: holder == logical font
        Assert.Equal(1, patch.GlyphsPatched);
        Assert.Equal(57, patch.WorstDiffBefore, 0);    // |507 - 450|
        Assert.False(patch.LeavesOtherFindings);
        var metrics = new EmbeddedFontMetrics(patch.PatchedProgram);
        Assert.Equal(507, metrics.GetAdvanceWidth(1)); // upm 1000: font units == glyph units
    }

    // The notdef-only simple-font decline fact moved to ReplaceProgramProposalTests (F-4b Task 5):
    // 6.2.11.8 now dispatches to ProposeProgramReplace, not this decline path, and that test file
    // owns A_simple_font_notdef_finding_declines_naming_v1_scope against the shared
    // WidthPatchFixtures.NotdefOnlyDoc() fixture.

    [Fact]
    public void Cff_kinds_decline_before_bytes_are_read()
    {
        var doc = new PdfDocument(); // no objects at all — proves nothing is read before the decline
        var entry = new FontInventoryEntry(
            Id: new FontId(1),
            ProgramHolderId: null,
            BaseFont: "TestCff",
            SubsetTag: null,
            FamilyName: "TestCff",
            Kind: FontKind.Type1,
            IsEmbedded: true,
            HasToUnicode: false,
            HasWidths: true,
            IsAddressable: true,
            UsedCodes: [65],
            PagesUsedOn: [0]);

        var finding = new Finding
        {
            RuleId = "font-program",
            Severity = FindingSeverity.Error,
            Clause = "ISO 19005-2:2011, 6.2.11.5",
            Message = "declared width mismatch",
            ObjectNumber = 1,
        };
        ILookup<int, Finding> ruleFindings = new[] { finding }.ToLookup(f => f.ObjectNumber!.Value);

        FontProposal result = Planner().ProposeWidthPatch(doc, entry, "font-program", ruleFindings);
        DeclineProposal decline = Assert.IsType<DeclineProposal>(result);
        Assert.Contains("charstring", decline.Reason);
    }

    [Fact]
    public void A_declared_zero_width_against_a_nonzero_advance_declines()
    {
        PdfDocument doc = DeclaredZeroDoc();
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);
        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("layout", decline.Reason);
    }

    [Fact]
    public void Conflicting_declared_widths_for_one_gid_decline()
    {
        PdfDocument doc = ConflictingWidthsDoc();
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);
        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("share one glyph", decline.Reason);
    }
}
