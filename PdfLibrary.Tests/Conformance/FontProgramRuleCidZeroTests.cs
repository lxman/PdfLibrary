using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Remediation;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Issue 40: <see cref="FontProgramRule.CheckType0"/>'s .notdef predicate (6.2.11.8) used to key
/// ONLY on the resolved GID (<c>gid == 0</c>). ISO 32000 §9.7.4.2 defines CID 0 as .notdef
/// regardless of what glyph a /CIDToGIDMap assigns it to — an explicit map CAN point CID 0 at a
/// real, non-zero glyph (the OLD predicate saw nothing wrong there), so the predicate now also
/// fires on <c>code == 0</c> directly. Uses <see cref="ReplaceProgramFixtures.DeadCid2Doc"/>'s
/// <c>cidToGid</c> override (Task 2's fixture-migration addition) rather than a fresh hand-built
/// document, per the controller brief's instruction to reuse Task 1's fixture helpers.
/// </summary>
public sealed class FontProgramRuleCidZeroTests
{
    private static string? Clause(Finding f) => ParitySnapshot.ClauseKey(f.Clause);

    [Fact]
    public void A_used_cid_zero_is_notdef_even_when_mapped_to_a_real_glyph()
    {
        // Explicit /CIDToGIDMap: CID 0 -> gid 1 (a REAL glyph) and CID 0x41 -> gid 1 too (also real,
        // and not itself under test). The OLD predicate (gid == 0) sees nothing wrong with CID 0
        // here; ISO 32000 §9.7.4.2 and veraPDF's 6.2.11.8 both call a USED CID 0 .notdef regardless.
        var gidByCid = new ushort[0x42];
        gidByCid[0x00] = 1;
        gidByCid[0x41] = 1;
        using PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc(
            contentHex: "0000 0041",
            toUnicodeEntries: [(0x0000, "0041"), (0x0041, "0042")],
            cidToGid: gidByCid);

        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => Clause(f) == "6.2.11.8").ToArray();

        Assert.Single(findings);
    }

    [Fact]
    public void An_unused_cid_zero_is_not_a_finding()
    {
        // CID 0 exists in the descendant's coverage (implicitly, via the default explicit
        // /CIDToGIDMap's covered range) but is never drawn — DeadCid2Doc's default content is
        // "0042 0041" only. The predicate is about USED codes, not merely mapped ones.
        using PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc();

        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => Clause(f) == "6.2.11.8").ToArray();

        Assert.Single(findings); // the 0x42 dead code — NOT a second finding for CID 0
    }
}
