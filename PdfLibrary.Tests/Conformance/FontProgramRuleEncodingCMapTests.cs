using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Remediation;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Issue 59: Type0 font-program checks must resolve an indirect encoding name and must map an
/// embedded CMap's character codes to CIDs before consulting /CIDToGIDMap, /W, and /DW.
/// </summary>
public sealed class FontProgramRuleEncodingCMapTests
{
    private static readonly PdfName EncodingName = new("Encoding");

    [Fact]
    public void Indirect_identity_encoding_reaches_font_program_checks()
    {
        using PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc();
        doc.AddObject(12, 0, new PdfName("Identity-H"));
        ((PdfDictionary)doc.GetObject(1)!)[EncodingName] = new PdfIndirectReference(12, 0);

        Finding[] findings = Check(doc);

        Assert.Single(findings, f => f.Clause.EndsWith("6.2.11.8"));
        Assert.Single(findings, f => f.Clause.EndsWith("6.2.11.5"));
    }

    [Fact]
    public void Embedded_encoding_maps_character_codes_to_cids_before_checks()
    {
        using PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc(
            contentHex: "0020 0021",
            toUnicodeEntries: [(0x0020, "0042"), (0x0021, "0041")]);
        byte[] cmap = Encoding.ASCII.GetBytes("""
            begincmap
            2 begincidchar
            <0020> 66
            <0021> 65
            endcidchar
            endcmap
            """);
        doc.AddObject(12, 0, new PdfStream(new PdfDictionary(), cmap));
        ((PdfDictionary)doc.GetObject(1)!)[EncodingName] = new PdfIndirectReference(12, 0);

        Finding[] findings = Check(doc);

        Assert.Single(findings, f => f.Clause.EndsWith("6.2.11.8"));
        Assert.Single(findings, f => f.Clause.EndsWith("6.2.11.5"));
    }

    private static Finding[] Check(PdfDocument doc) =>
        new FontProgramRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => f.RuleId == "font-program")
            .ToArray();
}
