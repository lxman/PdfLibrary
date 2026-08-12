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

    /// <summary>A model that loses data on meeting the unfamiliar is exactly what caused this bug.
    /// Anything the node model cannot express must survive verbatim.</summary>
    [Fact]
    public void An_unmodelled_shape_survives_verbatim()
    {
        const string exotic = """
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
        IReadOnlyList<XmpNode> parsed = XmpTreeParser.Parse(Encoding.UTF8.GetBytes(exotic));
        string text = Encoding.UTF8.GetString(XmpTreeSerializer.Serialize(parsed));

        Assert.Contains("ex:qualifier", text);
        Assert.Contains("the qualifier", text);
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
}
