using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Xmp;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using PdfLibrary.Xmp;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>The eight pre-2005 (namespace, local name) pairs <see cref="XmpLegacyCrosswalk"/> maps,
/// duplicated here as test data rather than exposing the whole table publicly.</summary>
internal static class XmpLegacyCrosswalkTestData
{
    public static IEnumerable<(string ns, string name)> AllLegacy { get; } =
    [
        ("http://ns.adobe.com/pdf/1.3/", "Title"),
        ("http://ns.adobe.com/pdf/1.3/", "Author"),
        ("http://ns.adobe.com/pdf/1.3/", "Subject"),
        ("http://ns.adobe.com/pdf/1.3/", "Creator"),
        ("http://ns.adobe.com/pdf/1.3/", "CreationDate"),
        ("http://ns.adobe.com/pdf/1.3/", "ModDate"),
        ("http://ns.adobe.com/xap/1.0/", "Title"),
        ("http://ns.adobe.com/xap/1.0/", "Author"),
    ];
}

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
    /// Proves the classifier's documented multi-island contract (see the class doc comment on
    /// <see cref="XmpConformance"/>): a packet may legally carry two sibling <c>rdf:RDF</c> islands
    /// under one <c>x:xmpmeta</c> root (the "DWC FX Generator" / ZUGFeRD shape — see
    /// <c>PdfLibrary.Tests.Metadata.XmpPacketParseTests.Parse_TwoSiblingRdfRdfIslands_SurfacesPropertiesFromBoth</c>
    /// for the pin that <see cref="XmpPacket.Parse"/> merges both). This is the test the corpus agreement test
    /// below CANNOT be: the veraPDF corpus contains no multi-island fixture, so it can only prove
    /// agreement on the (identical, for single-island packets) trees — it could not have caught the
    /// classifier reading a genuinely different tree from the rules.
    ///
    /// <para>Island 1 carries an offending property visible to both trees (an undeclared custom
    /// property, <c>ex:Widget</c>). Island 2 carries a second offending property
    /// (<c>ex:Gadget</c>) that exists ONLY in island 2. <see cref="XmpConformance.ClassifyProperties"/>
    /// reads <see cref="XmpPacket.Nodes"/> — the packet's one merged model — so it must report BOTH.
    /// <see cref="XmpTreeParser.Parse(byte[])"/> (the one-argument, first-island-only overload
    /// <c>ConformanceContext.XmpTree</c> actually uses to drive the real rules) must NOT surface
    /// <c>ex:Gadget</c> at all — demonstrating that a document in this shape really would produce
    /// fewer findings today than the classifier reports, which is exactly the superset the class doc
    /// comment promises.</para>
    /// </summary>
    [Fact]
    public void Multi_island_packet_is_classified_as_a_superset_of_what_the_rules_currently_see()
    {
        const string bytes = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:ex="http://example.com/ns/island-one/">
   <ex:Widget>island one offender</ex:Widget>
  </rdf:Description>
 </rdf:RDF>
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:ex="http://example.com/ns/island-two/">
   <ex:Gadget>island two offender</ex:Gadget>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        byte[] xmpBytes = Encoding.UTF8.GetBytes(bytes);

        // What ConformanceContext.XmpTree — and so the real rules — actually reads: first island only.
        IReadOnlyList<XmpNode> firstIslandOnly = XmpTreeParser.Parse(xmpBytes);
        Assert.Contains(firstIslandOnly, n => n.LocalName == "Widget");
        Assert.DoesNotContain(firstIslandOnly, n => n.LocalName == "Gadget");

        // What the classifier reads: XmpPacket's merged model — both islands.
        IReadOnlyList<XmpPropertyVerdict> verdicts =
            XmpConformance.ClassifyProperties(XmpPacket.Parse(xmpBytes));

        XmpPropertyVerdict widget = Find(verdicts, "Widget");
        Assert.False(widget.IsPredefined);
        Assert.False(widget.IsDeclaredByExtension);

        XmpPropertyVerdict gadget = Find(verdicts, "Gadget");
        Assert.False(gadget.IsPredefined);
        Assert.False(gadget.IsDeclaredByExtension);
    }

    /// <summary>
    /// Corpus-level proof that the classifier never drifts from the two rules it mirrors: for every
    /// PDF/A fixture in the external veraPDF corpus, the count of properties the classifier marks
    /// "neither predefined nor extension-declared" equals the count of
    /// <c>pdfa-xmp-property-predefined</c> findings the real rule produces for that same document, and
    /// the count marked "type does not conform" equals the count of <c>pdfa-xmp-property-type</c>
    /// findings. The corpus is a sibling checkout absent on CI and fresh clones, so this is
    /// <c>[Trait("Category","LocalOnly")]</c> and skips when it is not present.
    ///
    /// <para><b>Scope: this is honestly an equality check only because every corpus fixture is
    /// single-island.</b> The classifier reads <see cref="XmpPacket.Nodes"/> (every <c>rdf:RDF</c>
    /// island merged); the rules read <c>ConformanceContext.XmpTree</c> (first island only). For a
    /// single-island document — every fixture the veraPDF corpus contains — those two trees are
    /// textually identical, so the equality asserted here really is a like-with-like comparison, not
    /// an accidental pass. It CANNOT exercise the documented multi-island superset relationship
    /// (classifier verdicts covering a property the rules do not yet see) because the corpus carries
    /// no multi-island fixture — see
    /// <see cref="Multi_island_packet_is_classified_as_a_superset_of_what_the_rules_currently_see"/>
    /// for the synthetic test that proves that relationship instead.</para>
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

    [Theory]
    // The measured crosswalk head: pre-2005 spellings whose modern equivalents are predefined.
    // 'xap' is the former NAME of the xmp namespace — the URI is identical, so xap:Title and
    // xmp:Title are the same property and both map to dc:title.
    [InlineData("http://ns.adobe.com/pdf/1.3/", "Title",        "http://purl.org/dc/elements/1.1/", "title")]
    [InlineData("http://ns.adobe.com/pdf/1.3/", "Author",       "http://purl.org/dc/elements/1.1/", "creator")]
    [InlineData("http://ns.adobe.com/pdf/1.3/", "Subject",      "http://purl.org/dc/elements/1.1/", "description")]
    [InlineData("http://ns.adobe.com/pdf/1.3/", "Creator",      "http://ns.adobe.com/xap/1.0/",     "CreatorTool")]
    [InlineData("http://ns.adobe.com/pdf/1.3/", "CreationDate", "http://ns.adobe.com/xap/1.0/",     "CreateDate")]
    [InlineData("http://ns.adobe.com/pdf/1.3/", "ModDate",      "http://ns.adobe.com/xap/1.0/",     "ModifyDate")]
    [InlineData("http://ns.adobe.com/xap/1.0/", "Title",        "http://purl.org/dc/elements/1.1/", "title")]
    [InlineData("http://ns.adobe.com/xap/1.0/", "Author",       "http://purl.org/dc/elements/1.1/", "creator")]
    public void Legacy_properties_map_to_their_modern_equivalent(
        string legacyNs, string legacyName, string modernNs, string modernName)
    {
        XmpModernEquivalent modern =
            Assert.NotNull(XmpConformance.ModernEquivalentOf(legacyNs, legacyName));

        Assert.Equal(modernNs, modern.NamespaceUri);
        Assert.Equal(modernName, modern.LocalName);
    }

    [Fact]
    public void Every_crosswalk_target_is_itself_predefined()
    {
        // A migration that lands on a property the standard does not predefine would trade one
        // finding for another.
        foreach ((string ns, string name) in XmpLegacyCrosswalkTestData.AllLegacy)
        {
            XmpModernEquivalent m = Assert.NotNull(XmpConformance.ModernEquivalentOf(ns, name));
            Assert.True(XmpPredefinedSchemas.IsPredefined(m.NamespaceUri, m.LocalName),
                $"{m.Prefix}:{m.LocalName} is a migration target but is not predefined.");
        }
    }

    [Fact]
    public void A_property_with_no_modern_equivalent_returns_null()
    {
        Assert.Null(XmpConformance.ModernEquivalentOf("http://ns.adobe.com/pdfx/1.3/", "Company"));
    }

    [Theory]
    // Pins the predefined type of every crosswalk target, not just its existence. Three targets are
    // non-scalar (dc:title, dc:description are "lang alt"; dc:creator is "seq propername") while every
    // legacy source is a plain string, so a migration must wrap the value rather than copy it verbatim.
    // If XmpPredefinedSchemas' table ever changes under us, this fails loudly instead of a migration
    // silently writing a bare literal into a lang-alt or seq property.
    [InlineData("http://purl.org/dc/elements/1.1/", "title",       "lang alt")]
    [InlineData("http://purl.org/dc/elements/1.1/", "creator",     "seq propername")]
    [InlineData("http://purl.org/dc/elements/1.1/", "description", "lang alt")]
    [InlineData("http://ns.adobe.com/xap/1.0/",     "CreatorTool", "agentname")]
    [InlineData("http://ns.adobe.com/xap/1.0/",     "CreateDate",  "date")]
    [InlineData("http://ns.adobe.com/xap/1.0/",     "ModifyDate",  "date")]
    public void Crosswalk_target_predefined_types_match_what_this_task_assumed(
        string namespaceUri, string localName, string expectedType)
    {
        Assert.Equal(expectedType, XmpPredefinedSchemas.TypeOf(namespaceUri, localName));
    }

    // ── shape facets (Task 7b, 2026-08-12) ────────────────────────────────────────────────────────
    //
    // A verdict used to carry no shape at all, so a remediation fixer had to infer one from the flat
    // XmpProperty projection — "does it carry text anywhere". That proxy failed three times (see
    // task-7b-brief.md), most destructively on the anchor packet below, whose xmpidq:Scheme qualifier
    // a retype silently deleted. These four facets are the shape the engine already knew and simply
    // did not expose.

    private const string ShapesPacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:ex="http://example.com/ns/shapes/"
    xmlns:dc="http://purl.org/dc/elements/1.1/"
    xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
    xmlns:stEvt="http://ns.adobe.com/xap/1.0/sType/ResourceEvent#">
   <pdf:Producer>Acme Writer</pdf:Producer>
   <ex:Blank></ex:Blank>
   <ex:Structured rdf:parseType="Resource">
    <stEvt:action>converted</stEvt:action>
    <stEvt:softwareAgent>Acme Writer</stEvt:softwareAgent>
   </ex:Structured>
   <ex:Bagged>
    <rdf:Bag><rdf:li>one</rdf:li><rdf:li>two</rdf:li></rdf:Bag>
   </ex:Bagged>
   <dc:title>
    <rdf:Alt><rdf:li xml:lang="x-default">A title</rdf:li></rdf:Alt>
   </dc:title>
   <ex:PlainAlt>
    <rdf:Alt><rdf:li>no language here</rdf:li></rdf:Alt>
   </ex:PlainAlt>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    /// <summary>The `xmp:Identifier` shape carried by veraPDF corpus fixture
    /// <c>6-6-2-3-1-t07-fail-j.pdf</c>, copied verbatim from its packet: a <c>rdf:Seq</c> whose single
    /// item is an <c>rdf:value</c> qualified by <c>xmpidq:Scheme</c>. The qualifier has nowhere to live
    /// on <see cref="XmpNode"/>, so the parser additionally snapshots the item into
    /// <see cref="XmpNode.RawXml"/> and the serializer emits that verbatim — which is exactly why a
    /// fixer must not rewrite the property: rebuilding the container drops the snapshot and the
    /// qualifier with it.</summary>
    private const string QualifiedValuePacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmlns:xmpidq="http://ns.adobe.com/xmp/Identifier/qual/1.0/" rdf:about="">
   <xmp:Identifier>
    <rdf:Seq>
     <rdf:li>
      <rdf:Description>
       <rdf:value>Some value</rdf:value>
       <xmpidq:Scheme>Some name</xmpidq:Scheme>
      </rdf:Description>
     </rdf:li>
    </rdf:Seq>
   </xmp:Identifier>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    private static IReadOnlyList<XmpPropertyVerdict> Shapes() =>
        XmpConformance.ClassifyProperties(XmpPacket.Parse(Encoding.UTF8.GetBytes(ShapesPacket)));

    [Fact]
    public void A_struct_property_reports_IsStruct()
    {
        XmpPropertyVerdict v = Find(Shapes(), "Structured");
        Assert.True(v.IsStruct);
        Assert.False(v.IsArray);
        Assert.False(v.IsLangAlt);
        Assert.False(v.CarriesRawXml);
    }

    [Fact]
    public void An_array_property_reports_IsArray_but_not_IsLangAlt()
    {
        XmpPropertyVerdict v = Find(Shapes(), "Bagged");
        Assert.True(v.IsArray);
        Assert.False(v.IsLangAlt);
        Assert.False(v.IsStruct);
    }

    [Fact]
    public void A_lang_alt_property_reports_both_IsArray_and_IsLangAlt()
    {
        // IsLangAlt is a REFINEMENT of IsArray (XmpNode.IsArrayAltText is only ever set on an array),
        // never an alternative to it — a consumer testing IsArray must still see a lang alt.
        XmpPropertyVerdict v = Find(Shapes(), "title");
        Assert.True(v.IsArray);
        Assert.True(v.IsLangAlt);
    }

    [Fact]
    public void An_alt_array_without_xml_lang_is_not_a_lang_alt()
    {
        XmpPropertyVerdict v = Find(Shapes(), "PlainAlt");
        Assert.True(v.IsArray);
        Assert.False(v.IsLangAlt);
    }

    [Fact]
    public void A_simple_property_reports_no_shape_facet_at_all()
    {
        // Including a genuinely BLANK one: the whole point of the facets is that "carries no text"
        // and "is a struct" stop being the same observation.
        foreach (string name in new[] { "Producer", "Blank" })
        {
            XmpPropertyVerdict v = Find(Shapes(), name);
            Assert.False(v.IsStruct, name);
            Assert.False(v.IsArray, name);
            Assert.False(v.IsLangAlt, name);
            Assert.False(v.CarriesRawXml, name);
        }
    }

    /// <summary>The anchor case. <c>xmp:Identifier</c>'s RawXml snapshot lives on the ARRAY ITEM, not
    /// on the top-level property node (<c>XmpTreeParser.SetArray</c> passes the <c>rdf:li</c> as the
    /// capture root), so <see cref="XmpPropertyVerdict.CarriesRawXml"/> has to be a SUBTREE question.
    /// A facet that only looked at the top-level node would report false here and the fixer would
    /// destroy the qualifier exactly as it did before this task.</summary>
    [Fact]
    public void A_qualified_value_nested_in_an_array_item_reports_CarriesRawXml()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(QualifiedValuePacket));
        XmpPropertyVerdict v = Find(XmpConformance.ClassifyProperties(packet), "Identifier");

        Assert.True(v.CarriesRawXml);
        Assert.True(v.IsArray);

        // And the qualifier really is only reachable through that snapshot — the flat projection has
        // lost it entirely (it reads as a one-item array holding just the rdf:value text), which is
        // what made the text-based proxy blind.
        XmpProperty? flat = packet.Get("http://ns.adobe.com/xap/1.0/", "Identifier");
        Assert.NotNull(flat);
        Assert.Equal(["Some value"], flat!.Items);
    }

    [Fact]
    public void A_qualified_value_at_the_top_level_reports_CarriesRawXml()
    {
        const string packet = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.com/ns/shapes/" xmlns:q="http://example.com/ns/qual/">
   <ex:Qualified rdf:parseType="Resource">
    <rdf:value>the value</rdf:value>
    <q:Scheme>the qualifier</q:Scheme>
   </ex:Qualified>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        XmpPropertyVerdict v = Find(
            XmpConformance.ClassifyProperties(XmpPacket.Parse(Encoding.UTF8.GetBytes(packet))), "Qualified");

        Assert.True(v.CarriesRawXml);
    }

    /// <summary>The facets must describe the node the classifier ACTUALLY classified — the merged
    /// superset of every <c>rdf:RDF</c> island (see the class doc comment). A property living only in
    /// island 2 gets a verdict, so it must get that verdict's shape too, not a default.</summary>
    [Fact]
    public void Facets_describe_the_merged_node_the_classifier_classified()
    {
        const string packet = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.com/ns/island-one/">
   <ex:Widget>island one offender</ex:Widget>
  </rdf:Description>
 </rdf:RDF>
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.com/ns/island-two/" xmlns:stEvt="http://ns.adobe.com/xap/1.0/sType/ResourceEvent#">
   <ex:Gadget rdf:parseType="Resource"><stEvt:action>converted</stEvt:action></ex:Gadget>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        IReadOnlyList<XmpPropertyVerdict> verdicts =
            XmpConformance.ClassifyProperties(XmpPacket.Parse(Encoding.UTF8.GetBytes(packet)));

        Assert.False(Find(verdicts, "Widget").IsStruct);
        Assert.True(Find(verdicts, "Gadget").IsStruct);
    }
}
