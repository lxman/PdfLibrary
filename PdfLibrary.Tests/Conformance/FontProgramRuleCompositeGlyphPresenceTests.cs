using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Remediation;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Issue 60: an Identity CIDToGIDMap can produce a non-zero GID beyond the embedded TrueType
/// program's glyph count. That is an absent glyph, not a valid width-comparison target.
/// </summary>
public sealed class FontProgramRuleCompositeGlyphPresenceTests
{
    [Fact]
    public void Visible_identity_mapped_gid_past_num_glyphs_is_reported_as_absent()
    {
        // ZeroAdvanceSfntFixture has exactly two glyphs (valid GIDs 0 and 1). Identity maps the
        // shown CID 2 to GID 2, which is therefore outside the embedded program.
        using PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc(
            contentHex: "0002",
            toUnicodeEntries: [(0x0002, "0041")]);
        ((PdfDictionary)doc.GetObject(4)!)[new PdfName("CIDToGIDMap")] = new PdfName("Identity");

        Finding[] findings = new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => f.RuleId == "font-program")
            .ToArray();

        Finding finding = Assert.Single(findings);
        Assert.EndsWith("6.2.11.4.1", finding.Clause);
        Assert.Equal(1, finding.ObjectNumber);
    }
}
