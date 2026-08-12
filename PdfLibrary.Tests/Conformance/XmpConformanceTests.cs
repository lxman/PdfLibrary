using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>The classifier exists so a remediation fixer can identify an offending property without
/// regex-parsing a Finding's free-text Message. Its verdicts must therefore agree with the rules
/// that produce those Findings — a drift would have the fixer "correct" something the rule still
/// reports, or skip something it does not.</summary>
public class XmpConformanceTests
{
    private const string Packet = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
    xmlns:pdfx="http://ns.adobe.com/pdfx/1.3/"
    xmlns:dc="http://purl.org/dc/elements/1.1/">
   <pdf:Producer>Acme Writer</pdf:Producer>
   <pdfx:Company>Acme Ltd</pdfx:Company>
   <dc:title>a plain string where a Lang Alt belongs</dc:title>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    private static XmpPropertyVerdict Find(IReadOnlyList<XmpPropertyVerdict> v, string localName)
        => Assert.Single(v, x => x.LocalName == localName);

    [Fact]
    public void A_predefined_property_is_reported_predefined()
    {
        IReadOnlyList<XmpPropertyVerdict> verdicts =
            XmpConformance.ClassifyProperties(XmpPacket.Parse(Encoding.UTF8.GetBytes(Packet)));

        XmpPropertyVerdict producer = Find(verdicts, "Producer");
        Assert.True(producer.IsPredefined);
        Assert.Equal("agentname", producer.ExpectedType);
    }

    [Fact]
    public void An_undeclared_custom_property_is_reported_neither_predefined_nor_declared()
    {
        IReadOnlyList<XmpPropertyVerdict> verdicts =
            XmpConformance.ClassifyProperties(XmpPacket.Parse(Encoding.UTF8.GetBytes(Packet)));

        XmpPropertyVerdict company = Find(verdicts, "Company");
        Assert.False(company.IsPredefined);
        Assert.False(company.IsDeclaredByExtension);
        Assert.Null(company.ExpectedType);
    }

    [Fact]
    public void A_predefined_property_in_the_wrong_container_is_reported_non_conforming()
    {
        IReadOnlyList<XmpPropertyVerdict> verdicts =
            XmpConformance.ClassifyProperties(XmpPacket.Parse(Encoding.UTF8.GetBytes(Packet)));

        XmpPropertyVerdict title = Find(verdicts, "title");
        Assert.True(title.IsPredefined);
        Assert.Equal("lang alt", title.ExpectedType);
        Assert.False(title.TypeConforms);
    }

    [Fact]
    public void Structural_rdf_and_extension_namespaces_are_not_classified()
    {
        IReadOnlyList<XmpPropertyVerdict> verdicts =
            XmpConformance.ClassifyProperties(XmpPacket.Parse(Encoding.UTF8.GetBytes(Packet)));

        Assert.DoesNotContain(verdicts, v => v.NamespaceUri.Contains("aiim.org"));
        Assert.DoesNotContain(verdicts, v => v.NamespaceUri.Contains("22-rdf-syntax-ns"));
    }

    /// <summary>
    /// Corpus-level proof that the classifier never drifts from the two rules it mirrors: for every
    /// PDF/A fixture in the external veraPDF corpus, the count of properties the classifier marks
    /// "neither predefined nor extension-declared" equals the count of
    /// <c>pdfa-xmp-property-predefined</c> findings the real rule produces for that same document, and
    /// the count marked "type does not conform" equals the count of <c>pdfa-xmp-property-type</c>
    /// findings. The corpus is a sibling checkout absent on CI and fresh clones, so this is
    /// <c>[Trait("Category","LocalOnly")]</c> and skips when it is not present.
    /// </summary>
    [Fact]
    [Trait("Category", "LocalOnly")]
    public void Classifier_agrees_with_the_rules_across_the_corpus()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable, "veraPDF corpus not present at ../veraPDF-corpus");

        ConformanceProfile[] profiles =
        [
            ConformanceProfile.PdfA2b, ConformanceProfile.PdfA2u, ConformanceProfile.PdfA3b,
        ];

        var mismatches = new List<string>();
        int checkedFiles = 0;

        foreach (ConformanceProfile profile in profiles)
        {
            foreach (string path in CorpusHarness.AllPdfPaths(profile))
            {
                PdfDocument document;
                try
                {
                    document = PdfDocument.Load(path);
                }
                catch (Exception)
                {
                    continue; // unreadable fixture — not this test's concern
                }

                PreflightResult result;
                try
                {
                    result = Preflighter.Check(document, profile);
                }
                catch (Exception)
                {
                    continue;
                }

                int predefinedFindings = result.Findings.Count(f => f.RuleId == "pdfa-xmp-property-predefined");
                int typeFindings = result.Findings.Count(f => f.RuleId == "pdfa-xmp-property-type");

                XmpPacket? packet;
                try
                {
                    var metadata = document.GetCatalog()?.GetMetadata();
                    if (metadata is null)
                        continue; // no /Metadata: neither rule can produce a finding
                    byte[] bytes = metadata.GetDecodedData(document.Decryptor);
                    packet = XmpPacket.Parse(bytes);
                }
                catch (Exception)
                {
                    continue;
                }

                IReadOnlyList<XmpPropertyVerdict> verdicts = XmpConformance.ClassifyProperties(packet);
                int classifierPredefined = verdicts.Count(v => !v.IsPredefined && !v.IsDeclaredByExtension);
                int classifierType = verdicts.Count(v => !v.TypeConforms);

                checkedFiles++;

                if (classifierPredefined != predefinedFindings)
                {
                    mismatches.Add(
                        $"{System.IO.Path.GetFileName(path)}: classifier predefined-membership mismatches={classifierPredefined} rule findings={predefinedFindings}");
                }

                if (classifierType != typeFindings)
                {
                    mismatches.Add(
                        $"{System.IO.Path.GetFileName(path)}: classifier type mismatches={classifierType} rule findings={typeFindings}");
                }
            }
        }

        Assert.True(checkedFiles > 0, "corpus reported available but no fixtures were checked");
        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} disagreement(s) between classifier and rules: {string.Join("; ", mismatches)}");
    }
}
