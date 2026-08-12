using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Xmp;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>Coverage for <see cref="XmpTreeSerializer"/> shapes the golden Illustrator packet in
/// <see cref="XmpStructRoundTripTests"/> never exercises: lang-alt arrays, unordered bags,
/// struct-in-struct nesting, an array nested inside an array item (the finding-1 regression —
/// silently dropped by the first cut of the serializer, which only checked <c>IsStruct</c> before
/// falling through to plain text), namespace-prefix collisions and the empty prefix a default-namespace
/// property parses to, and values carrying XML metacharacters or an embedded newline. Every assertion
/// is against the re-parsed tree, never against raw bytes, for the same reason as the golden test:
/// attribute-form and element-form are equivalent RDF.</summary>
public class XmpTreeSerializerCoverageTests
{
    private static XmpNode Find(IReadOnlyList<XmpNode> nodes, string localName)
        => Assert.Single(nodes, n => n.LocalName == localName);

    private static IReadOnlyList<XmpNode> RoundTrip(string packet)
        => XmpTreeParser.Parse(XmpTreeSerializer.Serialize(XmpTreeParser.Parse(Encoding.UTF8.GetBytes(packet))));

    // ── Alt-text (lang-alt) array ────────────────────────────────────────────────────────────────

    private const string AltTextPacket = """
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
 <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
  <dc:title>
   <rdf:Alt>
    <rdf:li xml:lang="x-default">Default Title</rdf:li>
    <rdf:li xml:lang="en-US">English Title</rdf:li>
   </rdf:Alt>
  </dc:title>
 </rdf:Description>
</rdf:RDF>
""";

    [Fact]
    public void An_alt_text_array_with_x_default_and_a_locale_survives_round_trip()
    {
        XmpNode title = Find(RoundTrip(AltTextPacket), "title");

        Assert.True(title.IsArray);
        Assert.True(title.IsArrayOrdered);
        Assert.True(title.IsArrayAlternate);
        Assert.True(title.IsArrayAltText);
        Assert.Equal(2, title.Children.Count);

        XmpNode defaultItem = Assert.Single(title.Children, c => c.XmlLang == "x-default");
        XmpNode localeItem = Assert.Single(title.Children, c => c.XmlLang == "en-US");
        Assert.True(defaultItem.HasXmlLang);
        Assert.True(localeItem.HasXmlLang);
        Assert.Equal("Default Title", defaultItem.Value);
        Assert.Equal("English Title", localeItem.Value);
    }

    // ── Unordered Bag ────────────────────────────────────────────────────────────────────────────

    private const string BagPacket = """
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
 <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">
  <dc:subject>
   <rdf:Bag>
    <rdf:li>keyword-one</rdf:li>
    <rdf:li>keyword-two</rdf:li>
   </rdf:Bag>
  </dc:subject>
 </rdf:Description>
</rdf:RDF>
""";

    [Fact]
    public void An_unordered_bag_survives_round_trip()
    {
        XmpNode subject = Find(RoundTrip(BagPacket), "subject");

        Assert.True(subject.IsArray);
        Assert.False(subject.IsArrayOrdered);
        Assert.False(subject.IsArrayAlternate);
        Assert.Equal(
            new[] { "keyword-one", "keyword-two" },
            subject.Children.Select(c => c.Value).OrderBy(v => v));
    }

    // ── Struct nested inside struct ─────────────────────────────────────────────────────────────

    private const string NestedStructPacket = """
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
 <rdf:Description rdf:about=""
   xmlns:to="urn:test:outer#"
   xmlns:ti="urn:test:inner#">
  <to:Manager rdf:parseType="Resource">
   <to:name>Manager Name</to:name>
   <to:detail rdf:parseType="Resource">
    <ti:role>Lead</ti:role>
    <ti:since>2020</ti:since>
   </to:detail>
  </to:Manager>
 </rdf:Description>
</rdf:RDF>
""";

    [Fact]
    public void A_struct_nested_inside_a_struct_survives_round_trip()
    {
        XmpNode manager = Find(RoundTrip(NestedStructPacket), "Manager");

        Assert.True(manager.IsStruct);
        XmpNode name = Assert.Single(manager.Children, c => c.LocalName == "name");
        Assert.Equal("Manager Name", name.Value);

        XmpNode detail = Assert.Single(manager.Children, c => c.LocalName == "detail");
        Assert.True(detail.IsStruct);
        Assert.Equal(
            new (string, string?)[] { ("role", "Lead"), ("since", "2020") },
            detail.Children.Select(c => (c.LocalName, c.Value)).OrderBy(x => x.Item1));
    }

    // ── CRITICAL 1 regression: an array nested inside an array item ────────────────────────────────

    private const string NestedArrayPacket = """
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
 <rdf:Description rdf:about="" xmlns:t="urn:test:groups#">
  <t:Groups>
   <rdf:Bag>
    <rdf:li>
     <rdf:Seq>
      <rdf:li>one</rdf:li>
      <rdf:li>two</rdf:li>
     </rdf:Seq>
    </rdf:li>
   </rdf:Bag>
  </t:Groups>
 </rdf:Description>
</rdf:RDF>
""";

    [Fact]
    public void An_array_nested_inside_an_array_item_is_not_dropped()
    {
        XmpNode groups = Find(RoundTrip(NestedArrayPacket), "Groups");

        Assert.True(groups.IsArray);
        Assert.False(groups.IsArrayOrdered); // outer is a Bag
        XmpNode item = Assert.Single(groups.Children);

        // Before the fix, EmitArrayItem only checked item.IsStruct: a nested array fell through to
        // the simple-text branch and li.Value = item.Value ?? "" emitted an empty <rdf:li/> — the
        // whole nested Seq vanished with no exception.
        Assert.True(item.IsArray);
        Assert.True(item.IsArrayOrdered); // inner is a Seq
        Assert.Equal(new[] { "one", "two" }, item.Children.Select(c => c.Value));
    }

    // ── IMPORTANT 2: prefix collision between two namespaces ───────────────────────────────────────

    private const string PrefixCollisionPacket = """
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
 <rdf:Description rdf:about="" xmlns:common="urn:test:outer#">
  <common:title>Outer Value</common:title>
 </rdf:Description>
 <rdf:Description rdf:about="" xmlns:common="urn:test:inner#">
  <common:name>Inner Value</common:name>
 </rdf:Description>
</rdf:RDF>
""";

    [Fact]
    public void Two_namespaces_that_resolved_to_the_same_prefix_do_not_collide()
    {
        // Sanity: the source packet really does resolve both properties to prefix "common" but
        // different namespace URIs — the exact shape CollectNamespaces choked on.
        IReadOnlyList<XmpNode> before = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(PrefixCollisionPacket));
        XmpNode titleBefore = Find(before, "title");
        XmpNode nameBefore = Find(before, "name");
        Assert.Equal("common", titleBefore.Prefix);
        Assert.Equal("common", nameBefore.Prefix);
        Assert.NotEqual(titleBefore.NamespaceUri, nameBefore.NamespaceUri);

        // Serialize must not throw (old code: XElement.Add threw ArgumentException on the duplicate
        // xmlns:common attribute) and both properties, with their distinct namespaces, must survive.
        IReadOnlyList<XmpNode> after = XmpTreeParser.Parse(XmpTreeSerializer.Serialize(before));
        XmpNode titleAfter = Find(after, "title");
        XmpNode nameAfter = Find(after, "name");

        Assert.Equal("Outer Value", titleAfter.Value);
        Assert.Equal("Inner Value", nameAfter.Value);
        Assert.Equal(titleBefore.NamespaceUri, titleAfter.NamespaceUri);
        Assert.Equal(nameBefore.NamespaceUri, nameAfter.NamespaceUri);
        Assert.NotEqual(titleAfter.NamespaceUri, nameAfter.NamespaceUri);
    }

    // ── IMPORTANT 2: default namespace (empty prefix) ───────────────────────────────────────────────

    private const string DefaultNamespacePacket = """
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
 <rdf:Description rdf:about="" xmlns="urn:test:default#">
  <title>Default Namespace Value</title>
 </rdf:Description>
</rdf:RDF>
""";

    [Fact]
    public void A_default_namespace_property_with_an_empty_prefix_survives_round_trip()
    {
        IReadOnlyList<XmpNode> before = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(DefaultNamespacePacket));
        XmpNode titleBefore = Find(before, "title");
        Assert.Equal("urn:test:default#", titleBefore.NamespaceUri);
        Assert.Equal(string.Empty, titleBefore.Prefix); // the exact shape XNamespace.Xmlns + "" chokes on

        // Serialize must not throw (old code: an empty local name is not a legal XName) and the
        // property must survive with the same namespace.
        IReadOnlyList<XmpNode> after = XmpTreeParser.Parse(XmpTreeSerializer.Serialize(before));
        XmpNode titleAfter = Find(after, "title");

        Assert.Equal("Default Namespace Value", titleAfter.Value);
        Assert.Equal("urn:test:default#", titleAfter.NamespaceUri);
    }

    // ── Values with XML metacharacters and an embedded newline ─────────────────────────────────────

    private static readonly string SpecialCharsPacket =
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
        " <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n" +
        "  <dc:description>Value with &lt;tag&gt; &amp; \"quotes\" and 'apostrophes'</dc:description>\n" +
        "  <dc:rights>Line one\nLine two</dc:rights>\n" +
        " </rdf:Description>\n" +
        "</rdf:RDF>";

    [Fact]
    public void A_value_with_xml_metacharacters_survives_round_trip()
    {
        XmpNode description = Find(RoundTrip(SpecialCharsPacket), "description");
        Assert.Equal("Value with <tag> & \"quotes\" and 'apostrophes'", description.Value);
    }

    [Fact]
    public void A_value_with_an_embedded_newline_survives_round_trip()
    {
        XmpNode rights = Find(RoundTrip(SpecialCharsPacket), "rights");
        Assert.Equal("Line one\nLine two", rights.Value);
    }
}
