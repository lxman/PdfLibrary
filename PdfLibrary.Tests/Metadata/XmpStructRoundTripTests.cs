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
}
