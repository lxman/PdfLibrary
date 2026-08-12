using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Metadata;
using PdfLibrary.Xmp;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>The bug this slice exists to fix: a Seq of ResourceEvent structs (Adobe Illustrator
/// 25.2 output, from CC-MAIN corpus file 0000_0000007.pdf) was flattened to one concatenated text
/// blob on serialize, because array items were read with XElement.Value and written back as plain
/// rdf:li text. Field names must survive by name.</summary>
public class XmpStructRoundTripTests
{
    private const string IllustratorPacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/"
    xmlns:stEvt="http://ns.adobe.com/xap/1.0/sType/ResourceEvent#"
    xmlns:dc="http://purl.org/dc/elements/1.1/">
   <dc:format>application/pdf</dc:format>
   <xmpMM:History>
    <rdf:Seq>
     <rdf:li rdf:parseType="Resource">
      <stEvt:action>saved</stEvt:action>
      <stEvt:instanceID>xmp.iid:7acea5a3-d3b5-4e05-a570-0a5cf27dfe45</stEvt:instanceID>
      <stEvt:when>2021-06-04T14:38:59+09:00</stEvt:when>
      <stEvt:softwareAgent>Adobe Illustrator 25.2 (Macintosh)</stEvt:softwareAgent>
     </rdf:li>
    </rdf:Seq>
   </xmpMM:History>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    private static XmpNode Find(IReadOnlyList<XmpNode> nodes, string localName)
        => Assert.Single(nodes, n => n.LocalName == localName);

    [Fact]
    public void A_seq_of_structs_survives_serialize_with_its_field_names()
    {
        IReadOnlyList<XmpNode> before = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        byte[] emitted = XmpTreeSerializer.Serialize(before);
        string text = Encoding.UTF8.GetString(emitted);

        // The exact assertions the diagnostic probe failed against the old writer.
        Assert.Contains("stEvt:action", text);
        Assert.Contains("stEvt:when", text);
        Assert.Contains("stEvt:softwareAgent", text);
        Assert.Contains("parseType", text);
    }

    [Fact]
    public void Parse_serialize_parse_yields_an_equivalent_tree()
    {
        IReadOnlyList<XmpNode> before = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        IReadOnlyList<XmpNode> after = XmpTreeParser.Parse(XmpTreeSerializer.Serialize(before));

        // Compare trees, not bytes: attribute-form and element-form are equivalent RDF, so a byte
        // comparison would fail for correct output.
        XmpNode historyBefore = Find(before, "History");
        XmpNode historyAfter = Find(after, "History");

        Assert.True(historyAfter.IsArray);
        Assert.True(historyAfter.IsArrayOrdered);
        XmpNode eventBefore = Assert.Single(historyBefore.Children);
        XmpNode eventAfter = Assert.Single(historyAfter.Children);
        Assert.True(eventAfter.IsStruct);

        Assert.Equal(
            eventBefore.Children.Select(c => (c.LocalName, c.Value)).OrderBy(x => x.LocalName),
            eventAfter.Children.Select(c => (c.LocalName, c.Value)).OrderBy(x => x.LocalName));
    }

    [Fact]
    public void A_simple_property_still_round_trips()
    {
        IReadOnlyList<XmpNode> after =
            XmpTreeParser.Parse(XmpTreeSerializer.Serialize(
                XmpTreeParser.Parse(Encoding.UTF8.GetBytes(IllustratorPacket))));

        Assert.Equal("application/pdf", Find(after, "format").Value);
    }

    /// <summary>The end-to-end point of the whole slice: the destruction reached real files through
    /// the public editing API, because every PdfMetadata setter mutates the parsed packet and the
    /// save re-serializes it. Against the pre-fix XmpPacket (flat XmpProperty model) this fails on
    /// the first assertion — the History Seq had been read as two rdf:li text blobs, so no
    /// stEvt: field name survived to be written back.</summary>
    [Fact]
    public void Setting_a_metadata_property_does_not_flatten_existing_structs()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        packet.SetSimple("http://ns.adobe.com/pdf/1.3/", "pdf", "Producer", "Pellucid");

        string text = Encoding.UTF8.GetString(packet.Serialize());

        Assert.Contains("stEvt:softwareAgent", text);   // the History struct survived the edit
        Assert.Contains("Pellucid", text);              // and the edit landed
    }

    /// <summary>An rdf:Alt whose items are not all plain strings. SetLangAlt used to merge by reading
    /// the flat lang-to-string projection and rebuilding the array from it, which rewrote every
    /// sibling item as text — a struct item came back empty. Merging against the item NODES touches
    /// only the language being set.</summary>
    private const string AltWithStructItemPacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:dc="http://purl.org/dc/elements/1.1/"
    xmlns:stEvt="http://ns.adobe.com/xap/1.0/sType/ResourceEvent#">
   <dc:title>
    <rdf:Alt>
     <rdf:li xml:lang="x-default">Existing Title</rdf:li>
     <rdf:li rdf:parseType="Resource">
      <stEvt:action>saved</stEvt:action>
      <stEvt:softwareAgent>Adobe Illustrator 25.2 (Macintosh)</stEvt:softwareAgent>
     </rdf:li>
    </rdf:Alt>
   </dc:title>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    [Fact]
    public void Setting_one_language_does_not_flatten_the_other_items_of_an_alt_array()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(AltWithStructItemPacket));
        packet.SetLangAlt("http://purl.org/dc/elements/1.1/", "dc", "title", "Neuer Titel", "de");

        string text = Encoding.UTF8.GetString(packet.Serialize());

        Assert.Contains("stEvt:softwareAgent", text);                    // the struct item survived
        Assert.Contains("Adobe Illustrator 25.2 (Macintosh)", text);     // with its content
        Assert.Contains("Existing Title", text);                         // the untouched language too
        Assert.Contains("Neuer Titel", text);                            // and the edit landed
    }

    [Fact]
    public void Setting_an_existing_language_replaces_only_that_item()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(AltWithStructItemPacket));
        packet.SetLangAlt("http://purl.org/dc/elements/1.1/", "dc", "title", "Replaced Title");

        string text = Encoding.UTF8.GetString(packet.Serialize());

        Assert.DoesNotContain("Existing Title", text);
        Assert.Contains("Replaced Title", text);
        Assert.Contains("stEvt:softwareAgent", text);
    }

    private const string QualifiedValuePacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.invalid/ns/">
   <ex:qualified>
    <rdf:Description>
     <rdf:value>the value</rdf:value>
     <ex:qualifier>the qualifier</ex:qualifier>
    </rdf:Description>
   </ex:qualified>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    /// <summary>A model that loses data on meeting the unfamiliar is exactly what caused this bug.
    /// Anything the node model cannot express must survive verbatim.</summary>
    [Fact]
    public void An_unmodelled_shape_survives_verbatim()
    {
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(QualifiedValuePacket));
        string text = Encoding.UTF8.GetString(XmpTreeSerializer.Serialize(parsed));

        Assert.Contains("ex:qualifier", text);
        Assert.Contains("the qualifier", text);
    }

    /// <summary>Setting RawXml must NOT blank out the node's normal classification: every reader
    /// that isn't the serializer (conformance rules, XmpProperty.FromNode, extension-schema
    /// resolution) has to keep seeing the same best-effort verdict it always saw, so RawXml has to be
    /// an ADDITION to the facets, not a replacement for them.</summary>
    [Fact]
    public void RawXml_is_additional_and_does_not_blank_the_normal_classification()
    {
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(QualifiedValuePacket));

        XmpNode qualified = Assert.Single(parsed);
        Assert.NotNull(qualified.RawXml);
        Assert.True(qualified.IsSimple);
        Assert.Equal("the value", qualified.Value);
    }

    /// <summary>The verbatim fallback is a last resort, not a catch-all: an rdf:value with NO
    /// qualifiers is a shape the model already expresses as a plain simple value, and must keep going
    /// through the normal shape/prefix/sanitizer machinery rather than being dumped as raw XML (which
    /// would, for instance, skip prefix re-assignment and risk a stale or colliding prefix).</summary>
    [Fact]
    public void An_unqualified_rdf_value_still_takes_the_normal_simple_path()
    {
        const string unqualified = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.invalid/ns/">
   <ex:plain>
    <rdf:Description>
     <rdf:value>the value</rdf:value>
    </rdf:Description>
   </ex:plain>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(unqualified));

        XmpNode plain = Assert.Single(parsed);
        Assert.True(plain.IsSimple);
        Assert.Null(plain.RawXml);
        Assert.Equal("the value", plain.Value);

        string text = Encoding.UTF8.GetString(XmpTreeSerializer.Serialize(parsed));
        Assert.Contains("<ex:plain>the value</ex:plain>", text);
    }

    /// <summary>rdf:type is not a qualifier — SetStruct already treats a struct's own rdf:*-namespaced
    /// children as structural, not fields ("rdf:type qualifier and the like are not struct fields"),
    /// so the classic RDF typed-value pattern (rdf:value plus an rdf:type) must classify the same way:
    /// a plain simple value, not an unmodelled shape.</summary>
    [Fact]
    public void An_rdf_type_qualifier_does_not_trigger_the_verbatim_path()
    {
        const string typed = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.invalid/ns/">
   <ex:typed>
    <rdf:Description>
     <rdf:value>v</rdf:value>
     <rdf:type rdf:resource="http://example.invalid/type/Thing"/>
    </rdf:Description>
   </ex:typed>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(typed));

        XmpNode typedNode = Assert.Single(parsed);
        Assert.True(typedNode.IsSimple);
        Assert.Null(typedNode.RawXml);
        Assert.Equal("v", typedNode.Value);
    }

    /// <summary>dc:title read through the flat XmpProperty/PdfMetadata/UaTitleRule projection path
    /// must keep working when the title happens to be written as a qualified rdf:value: the fix has to
    /// preserve the node's normal classification alongside RawXml, not replace it, or every consumer
    /// of that projection (UaTitleRule among them) regresses on a document that used to pass.</summary>
    [Fact]
    public void A_qualified_dc_title_still_projects_to_a_readable_XmpProperty()
    {
        const string qualifiedTitle = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:dc="http://purl.org/dc/elements/1.1/"
    xmlns:ex="http://example.invalid/ns/">
   <dc:title>
    <rdf:Description>
     <rdf:value>My Document</rdf:value>
     <ex:qualifier>the qualifier</ex:qualifier>
    </rdf:Description>
   </dc:title>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(qualifiedTitle));
        XmpProperty? title = packet.Get("http://purl.org/dc/elements/1.1/", "title");

        Assert.NotNull(title);
        Assert.Equal(XmpValueKind.Simple, title!.Kind);
        Assert.Equal("My Document", title.Value);

        // The full round trip still carries the qualifier the flat projection cannot see.
        string text = Encoding.UTF8.GetString(packet.Serialize());
        Assert.Contains("My Document", text);
        Assert.Contains("ex:qualifier", text);
        Assert.Contains("the qualifier", text);
    }

    /// <summary>An unqualified rdf:value that itself wraps a deeper qualified value must capture the
    /// OUTER property element for RawXml, not the inner &lt;rdf:value&gt; element the recursion is
    /// currently looking at — capturing the inner element would drop the property's own name/namespace
    /// on serialize, and lose the property entirely on re-parse (ReadDescription skips rdf:*-namespaced
    /// children as structural, and a bare &lt;rdf:value&gt; re-emitted at the top level is exactly
    /// that).</summary>
    [Fact]
    public void A_nested_qualified_value_captures_the_owning_property_not_the_inner_rdf_value()
    {
        const string nested = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about="" xmlns:ex="http://example.invalid/ns/">
   <ex:outer>
    <rdf:Description>
     <rdf:value>
      <rdf:Description>
       <rdf:value>deep value</rdf:value>
       <ex:qualifier>deep qualifier</ex:qualifier>
      </rdf:Description>
     </rdf:value>
    </rdf:Description>
   </ex:outer>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(nested));

        XmpNode outer = Assert.Single(parsed);
        Assert.NotNull(outer.RawXml);
        Assert.Contains("ex:outer", outer.RawXml); // captured the OWNING property, not a bare rdf:value

        byte[] serialized = XmpTreeSerializer.Serialize(parsed);
        string text = Encoding.UTF8.GetString(serialized);
        Assert.Contains("ex:outer", text);
        Assert.Contains("ex:qualifier", text);
        Assert.Contains("deep qualifier", text);

        // The property must survive re-parsing too — a bare re-emitted <rdf:value> would vanish here
        // (ReadDescription skips rdf:*-namespaced children as structural).
        IReadOnlyList<XmpNode> reparsed = XmpTreeParser.Parse(serialized);
        XmpNode roundTripped = Assert.Single(reparsed);
        Assert.Equal("http://example.invalid/ns/", roundTripped.NamespaceUri);
        Assert.Equal("outer", roundTripped.LocalName);
    }

    /// <summary>Parse → serialize → parse → serialize must converge: the second serialization is
    /// byte-identical to the first. A RawXml subtree that depended on the first pass's specific prefix
    /// assignment (rather than self-declaring) would drift on a second round trip.</summary>
    [Fact]
    public void An_unmodelled_shape_round_trips_idempotently()
    {
        byte[] firstPass = XmpTreeSerializer.Serialize(
            XmpTreeParser.Parse(Encoding.UTF8.GetBytes(QualifiedValuePacket)));
        byte[] secondPass = XmpTreeSerializer.Serialize(XmpTreeParser.Parse(firstPass));

        Assert.Equal(Encoding.UTF8.GetString(firstPass), Encoding.UTF8.GetString(secondPass));
    }

    /// <summary>The preserved fragment must be self-declaring, not reliant on AssignPrefixes: here the
    /// qualifier field's namespace ("other") differs from the property's own ("ex"), and node.Children
    /// stays empty for a RawXml node, so AssignPrefixes (which only walks NamespaceUri/Children) never
    /// even sees the "other" URI. If the fragment depended on an ancestor-declared xmlns:other rather
    /// than declaring its own, this would serialize to a broken, unparseable packet.</summary>
    [Fact]
    public void An_unmodelled_shapes_inner_namespace_is_self_declared_not_borrowed()
    {
        const string differingNamespace = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:ex="http://example.invalid/ns/"
    xmlns:other="http://example.invalid/other/">
   <ex:qualified>
    <rdf:Description>
     <rdf:value>the value</rdf:value>
     <other:qualifier>the qualifier</other:qualifier>
    </rdf:Description>
   </ex:qualified>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(differingNamespace));
        byte[] serialized = XmpTreeSerializer.Serialize(parsed);
        string text = Encoding.UTF8.GetString(serialized);

        Assert.Contains("other:qualifier", text);
        Assert.Contains("the qualifier", text);

        // Well-formed and re-readable: the "other" prefix must have resolved to a real namespace
        // wherever it was declared, not silently produced an unbound-prefix parse failure.
        IReadOnlyList<XmpNode> reparsed = XmpTreeParser.Parse(serialized);
        Assert.NotEmpty(reparsed);
    }

    /// <summary>RawXml's contract mirrors Serialize's documented one: an invalid value is caller error
    /// (ArgumentException), never an undocumented XmlException surfacing later out of Serialize when a
    /// directly-constructed node (not one the parser produced) carries malformed content.</summary>
    [Fact]
    public void Setting_RawXml_to_malformed_xml_throws_ArgumentException()
    {
        var node = new XmpNode("http://example.invalid/ns/", "broken", "ex");
        Assert.Throws<ArgumentException>(() => node.RawXml = "<not-well-formed");
    }

    /// <summary>The authoring side of struct-array support: SetStructArray must produce a shape that
    /// both serializes with field names intact AND re-parses back into the same struct shape — not
    /// merely emit text containing the right substrings.</summary>
    [Fact]
    public void A_struct_array_can_be_authored_and_round_trips()
    {
        var packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        const string mm = "http://ns.adobe.com/xap/1.0/mm/";
        const string evt = "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#";

        packet.SetStructArray(mm, "xmpMM", "History",
            [[ new XmpField(evt, "stEvt", "action", "converted"),
               new XmpField(evt, "stEvt", "softwareAgent", "Pellucid") ]],
            ordered: true);

        byte[] serialized = packet.Serialize();
        string text = Encoding.UTF8.GetString(serialized);
        Assert.Contains("stEvt:action", text);
        Assert.Contains("converted", text);
        Assert.Contains("Pellucid", text);

        // Round trip: re-parse the serialized packet and assert the struct shape, not just substrings.
        IReadOnlyList<XmpNode> reparsed = XmpTreeParser.Parse(serialized);
        XmpNode history = Find(reparsed, "History");
        Assert.True(history.IsArray);
        Assert.True(history.IsArrayOrdered);
        XmpNode item = Assert.Single(history.Children);
        Assert.True(item.IsStruct);
        Assert.Equal(
            new[] { ("action", "converted"), ("softwareAgent", "Pellucid") },
            item.Children.Select(c => (c.LocalName, c.Value!)).OrderBy(x => x.LocalName));
    }

    /// <summary>SetStruct is the non-array counterpart: a single struct-valued property.</summary>
    [Fact]
    public void A_struct_can_be_authored_and_round_trips()
    {
        var packet = XmpPacket.CreateEmpty();
        const string mm = "http://ns.adobe.com/xap/1.0/mm/";
        const string stRef = "http://ns.adobe.com/xap/1.0/sType/ResourceRef#";

        packet.SetStruct(mm, "xmpMM", "DerivedFrom",
            [ new XmpField(stRef, "stRef", "documentID", "xmp.did:1234"),
              new XmpField(stRef, "stRef", "instanceID", "xmp.iid:5678") ]);

        byte[] serialized = packet.Serialize();
        IReadOnlyList<XmpNode> reparsed = XmpTreeParser.Parse(serialized);

        XmpNode derivedFrom = Find(reparsed, "DerivedFrom");
        Assert.True(derivedFrom.IsStruct);
        Assert.Equal(
            new[] { ("documentID", "xmp.did:1234"), ("instanceID", "xmp.iid:5678") },
            derivedFrom.Children.Select(c => (c.LocalName, c.Value!)).OrderBy(x => x.LocalName));
    }

    /// <summary>An empty fields/items collection must not throw — it authors a struct with no fields,
    /// or an array with no items, both legal RDF shapes.</summary>
    [Fact]
    public void An_empty_struct_and_an_empty_struct_array_author_without_throwing()
    {
        var packet = XmpPacket.CreateEmpty();
        const string mm = "http://ns.adobe.com/xap/1.0/mm/";

        packet.SetStruct(mm, "xmpMM", "Empty", []);
        packet.SetStructArray(mm, "xmpMM", "EmptyHistory", [], ordered: true);

        byte[] serialized = packet.Serialize();
        IReadOnlyList<XmpNode> reparsed = XmpTreeParser.Parse(serialized);

        XmpNode empty = Find(reparsed, "Empty");
        Assert.True(empty.IsStruct);
        Assert.Empty(empty.Children);

        XmpNode emptyHistory = Find(reparsed, "EmptyHistory");
        Assert.True(emptyHistory.IsArray);
        Assert.Empty(emptyHistory.Children);
    }

    /// <summary>Authoring over an existing property (of any prior shape) must replace it cleanly, the
    /// same contract every other setter on XmpPacket already carries.</summary>
    [Fact]
    public void Authoring_a_struct_array_over_an_existing_property_replaces_it()
    {
        var packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(IllustratorPacket));
        const string mm = "http://ns.adobe.com/xap/1.0/mm/";
        const string evt = "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#";

        // IllustratorPacket's History already has one "saved" item; replace it entirely.
        packet.SetStructArray(mm, "xmpMM", "History",
            [[ new XmpField(evt, "stEvt", "action", "converted") ]],
            ordered: true);

        string text = Encoding.UTF8.GetString(packet.Serialize());
        Assert.DoesNotContain("saved", text);
        Assert.DoesNotContain("Adobe Illustrator 25.2 (Macintosh)", text);
        Assert.Contains("converted", text);
    }

    /// <summary>A null field Value is caller error, not document data — matches SetSimple's contract
    /// (ArgumentNullException, not a silently-swallowed empty string).</summary>
    [Fact]
    public void A_null_field_value_throws_ArgumentNullException()
    {
        var packet = XmpPacket.CreateEmpty();
        const string mm = "http://ns.adobe.com/xap/1.0/mm/";
        const string evt = "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#";

        Assert.Throws<ArgumentNullException>(() =>
            packet.SetStruct(mm, "xmpMM", "History", [new XmpField(evt, "stEvt", "action", null!)]));
    }
}
