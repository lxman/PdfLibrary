using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Conformance.Xmp;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using PdfLibrary.Xmp;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>The writer's two obligations: what it emits must be READABLE by XmpExtensionSchemas
/// (or the declaration is invisible and the property still fires 6.6.2.3.1) and LEGAL by
/// XmpExtensionSchemaStructureRule (or fixing one rule opens another).</summary>
public class XmpExtensionSchemaWriterTests
{
    private const string PdfxNs = "http://ns.adobe.com/pdfx/1.3/";

    private static readonly XmpExtensionProperty[] Company =
    [
        new("Company", "Text", "internal", "The producing organisation."),
    ];

    [Fact]
    public void A_declared_property_is_readable_by_the_extension_schema_parser()
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        packet.SetSimple(PdfxNs, "pdfx", "Company", "Acme Ltd");
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);

        IReadOnlyList<XmpNode> reparsed = XmpTreeParser.Parse(packet.Serialize());
        XmpExtensionSchemas schemas = XmpExtensionSchemas.Parse(reparsed);

        Assert.True(schemas.IsDeclared(PdfxNs, "Company"));
        Assert.True(schemas.TryGetType(PdfxNs, "Company", out string type, out _));
        Assert.Equal("Text", type);
    }

    [Fact]
    public void The_emitted_block_carries_every_field_the_structure_rule_requires()
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);

        string text = Encoding.UTF8.GetString(packet.Serialize());

        Assert.Contains("pdfaSchema:namespaceURI", text);
        Assert.Contains("pdfaSchema:prefix", text);
        Assert.Contains("pdfaSchema:schema", text);
        Assert.Contains("pdfaProperty:name", text);
        Assert.Contains("pdfaProperty:valueType", text);
        Assert.Contains("pdfaProperty:category", text);
        Assert.Contains("pdfaProperty:description", text);
    }

    [Fact]
    public void Declaring_a_second_schema_preserves_the_first()
    {
        // Wholesale replacement would destroy a producer's existing declarations - the same class of
        // failure the XMP round-trip work existed to stop.
        const string AdhocNs = "http://ns.adobe.com/AdobeHocWorkflow/1.0/";
        XmpPacket packet = XmpPacket.CreateEmpty();
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);
        packet.DeclareExtensionSchema(AdhocNs, "adhocwf", "AdHoc Workflow Schema",
            [new("state", "Text", "internal", "Workflow state.")]);

        XmpExtensionSchemas schemas = XmpExtensionSchemas.Parse(XmpTreeParser.Parse(packet.Serialize()));

        Assert.True(schemas.IsDeclared(PdfxNs, "Company"));
        Assert.True(schemas.IsDeclared(AdhocNs, "state"));
    }

    [Fact]
    public void Declaring_into_an_existing_schema_adds_the_property_without_dropping_its_siblings()
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema",
            [new("SourceModified", "Text", "internal", "Source modification stamp.")]);

        XmpExtensionSchemas schemas = XmpExtensionSchemas.Parse(XmpTreeParser.Parse(packet.Serialize()));

        Assert.True(schemas.IsDeclared(PdfxNs, "Company"));
        Assert.True(schemas.IsDeclared(PdfxNs, "SourceModified"));
    }

    /// <summary>A block the writer produced must be more than string-plausible: run the REAL structure
    /// rule (clause 6.6.2.3.3) over a document carrying it and require zero findings, or closing
    /// 6.6.2.3.1 would merely open 6.6.2.3.3.</summary>
    [Fact]
    public void The_emitted_block_produces_no_findings_from_the_real_structure_rule()
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        packet.SetSimple(PdfxNs, "pdfx", "Company", "Acme Ltd");
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);
        packet.DeclareExtensionSchema("http://ns.adobe.com/AdobeHocWorkflow/1.0/", "adhocwf",
            "AdHoc Workflow Schema", [new("state", "Text", "internal", "Workflow state.")]);

        PdfDocument doc = DocWithXmp(packet.Serialize());
        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        // Control: the block really did reach the context, so "no findings" is not the empty verdict a
        // document with no extension schemas at all would also produce.
        Assert.True(context.XmpExtensions.IsDeclared(PdfxNs, "Company"));

        Assert.Empty(new XmpExtensionSchemaStructureRule().Check(context));
    }

    /// <summary>A repeated declaration of the same property is idempotent: the existing entry is left
    /// alone rather than duplicated, because two property items with the same pdfaProperty:name are
    /// what a naive append would produce and the parser would silently keep only the last.</summary>
    [Fact]
    public void Redeclaring_the_same_property_does_not_duplicate_it()
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);

        string text = Encoding.UTF8.GetString(packet.Serialize());

        Assert.Equal(1, CountOccurrences(text, ">Company<"));
    }

    /// <summary>Declaring nothing declares nothing: an empty property list leaves the packet untouched
    /// rather than planting a vacuous schema item in a packet that had no extension block at all.</summary>
    [Fact]
    public void An_empty_property_list_leaves_the_packet_untouched()
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", []);

        string text = Encoding.UTF8.GetString(packet.Serialize());

        Assert.DoesNotContain("pdfaExtension", text);
    }

    // ── Merging into a FOREIGN block (the path the feature actually exists for) ──────────────────
    //
    // Every test above starts from CreateEmpty(), so every "merge" merges into a block this writer
    // authored moments earlier, with its own prefixes and its own field order. EnsureSchemasArray is
    // the only code that reuses a PARSED node, and only these tests ever hand it one. The packets
    // below are hand-written literals — genuinely foreign input, not this writer's output.

    private const string AdhocNs = "http://ns.adobe.com/AdobeHocWorkflow/1.0/";

    /// <summary>A producer-authored packet carrying its own extension block: rdf:Bag of schema items,
    /// its own field order (schema before namespaceURI), rdf:Seq property array.</summary>
    private const string ProducerPacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/"
    xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#"
    xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#">
   <pdfaExtension:schemas>
    <rdf:Bag>
     <rdf:li rdf:parseType="Resource">
      <pdfaSchema:schema>AdHoc Workflow Schema</pdfaSchema:schema>
      <pdfaSchema:namespaceURI>http://ns.adobe.com/AdobeHocWorkflow/1.0/</pdfaSchema:namespaceURI>
      <pdfaSchema:prefix>adhocwf</pdfaSchema:prefix>
      <pdfaSchema:property>
       <rdf:Seq>
        <rdf:li rdf:parseType="Resource">
         <pdfaProperty:name>state</pdfaProperty:name>
         <pdfaProperty:valueType>Text</pdfaProperty:valueType>
         <pdfaProperty:category>internal</pdfaProperty:category>
         <pdfaProperty:description>Workflow state.</pdfaProperty:description>
        </rdf:li>
       </rdf:Seq>
      </pdfaSchema:property>
     </rdf:li>
    </rdf:Bag>
   </pdfaExtension:schemas>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    [Fact]
    public void Declaring_into_a_producer_authored_block_keeps_its_declarations_and_stays_legal()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(ProducerPacket));

        // One declaration into the producer's OWN schema item, one into a namespace it never mentioned.
        packet.DeclareExtensionSchema(AdhocNs, "adhocwf", "AdHoc Workflow Schema",
            [new("priority", "Text", "internal", "Workflow priority.")]);
        packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", Company);

        byte[] bytes = packet.Serialize();
        XmpExtensionSchemas schemas = XmpExtensionSchemas.Parse(XmpTreeParser.Parse(bytes));

        Assert.True(schemas.IsDeclared(AdhocNs, "state"), "the producer's own declaration was lost");
        Assert.True(schemas.IsDeclared(AdhocNs, "priority"));
        Assert.True(schemas.IsDeclared(PdfxNs, "Company"));

        var context = new ConformanceContext(DocWithXmp(bytes), ConformanceProfile.PdfA2b);
        Assert.Empty(new XmpExtensionSchemaStructureRule().Check(context));
    }

    /// <summary>Two schema items for one namespace is malformed but producible, and the parser keeps
    /// the LAST (RegisterSchema assigns _byNamespace[ns] per item in document order). Merging into the
    /// first would append to an item the parser discards: the declaration would silently not take,
    /// and the 6.6.2.3.1 finding it was meant to close would stay open with nothing reported.</summary>
    [Fact]
    public void Declaring_into_a_duplicated_namespace_targets_the_item_the_parser_keeps()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(DuplicateNamespacePacket));
        packet.DeclareExtensionSchema(AdhocNs, "adhocwf", "AdHoc Workflow Schema",
            [new("fresh", "Text", "internal", "A newly declared property.")]);

        XmpExtensionSchemas schemas = XmpExtensionSchemas.Parse(XmpTreeParser.Parse(packet.Serialize()));

        Assert.True(schemas.IsDeclared(AdhocNs, "fresh"));
        // The parser's own verdict on the shadowed first item, pinned to show what "last wins" means:
        // "second" (from the surviving item) resolves, "first" does not.
        Assert.True(schemas.IsDeclared(AdhocNs, "second"));
        Assert.False(schemas.IsDeclared(AdhocNs, "first"));
    }

    private const string DuplicateNamespacePacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/"
    xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#"
    xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#">
   <pdfaExtension:schemas>
    <rdf:Bag>
     <rdf:li rdf:parseType="Resource">
      <pdfaSchema:namespaceURI>http://ns.adobe.com/AdobeHocWorkflow/1.0/</pdfaSchema:namespaceURI>
      <pdfaSchema:prefix>adhocwf</pdfaSchema:prefix>
      <pdfaSchema:schema>AdHoc Workflow Schema</pdfaSchema:schema>
      <pdfaSchema:property>
       <rdf:Seq>
        <rdf:li rdf:parseType="Resource">
         <pdfaProperty:name>first</pdfaProperty:name>
         <pdfaProperty:valueType>Text</pdfaProperty:valueType>
         <pdfaProperty:category>internal</pdfaProperty:category>
         <pdfaProperty:description>In the shadowed item.</pdfaProperty:description>
        </rdf:li>
       </rdf:Seq>
      </pdfaSchema:property>
     </rdf:li>
     <rdf:li rdf:parseType="Resource">
      <pdfaSchema:namespaceURI>http://ns.adobe.com/AdobeHocWorkflow/1.0/</pdfaSchema:namespaceURI>
      <pdfaSchema:prefix>adhocwf</pdfaSchema:prefix>
      <pdfaSchema:schema>AdHoc Workflow Schema</pdfaSchema:schema>
      <pdfaSchema:property>
       <rdf:Seq>
        <rdf:li rdf:parseType="Resource">
         <pdfaProperty:name>second</pdfaProperty:name>
         <pdfaProperty:valueType>Text</pdfaProperty:valueType>
         <pdfaProperty:category>internal</pdfaProperty:category>
         <pdfaProperty:description>In the surviving item.</pdfaProperty:description>
        </rdf:li>
       </rdf:Seq>
      </pdfaSchema:property>
     </rdf:li>
    </rdf:Bag>
   </pdfaExtension:schemas>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    /// <summary>The repair promise has to hold at the property level too: a producer's property item
    /// declaring only name and valueType leaves 6.6.2.3.3 firing, and matching on the name is no reason
    /// to walk away from it.</summary>
    [Fact]
    public void Redeclaring_a_property_that_lacks_category_and_description_repairs_it()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(IncompletePropertyPacket));

        var context = new ConformanceContext(
            DocWithXmp(Encoding.UTF8.GetBytes(IncompletePropertyPacket)), ConformanceProfile.PdfA2b);
        Assert.Equal(2, new XmpExtensionSchemaStructureRule().Check(context).Count()); // control

        packet.DeclareExtensionSchema(AdhocNs, "adhocwf", "AdHoc Workflow Schema",
            [new("state", "Text", "internal", "Workflow state.")]);

        byte[] bytes = packet.Serialize();
        Assert.Empty(new XmpExtensionSchemaStructureRule()
            .Check(new ConformanceContext(DocWithXmp(bytes), ConformanceProfile.PdfA2b)));
        Assert.True(XmpExtensionSchemas.Parse(XmpTreeParser.Parse(bytes)).IsDeclared(AdhocNs, "state"));
    }

    /// <summary>Absence is repaired; presence is respected. A field that is present but EMPTY is not
    /// missing — both consumers treat presence as satisfaction — so the caller's text must not
    /// overwrite the producer's empty one.</summary>
    [Fact]
    public void A_present_but_empty_field_is_left_alone_rather_than_overwritten()
    {
        XmpPacket packet = XmpPacket.Parse(Encoding.UTF8.GetBytes(EmptyDescriptionPacket));
        packet.DeclareExtensionSchema(AdhocNs, "adhocwf", "AdHoc Workflow Schema",
            [new("state", "Text", "internal", "CALLER TEXT")]);

        byte[] bytes = packet.Serialize();

        Assert.DoesNotContain("CALLER TEXT", Encoding.UTF8.GetString(bytes));
        // ...and the empty field it kept still satisfies the rule, which tests presence, not content.
        Assert.Empty(new XmpExtensionSchemaStructureRule()
            .Check(new ConformanceContext(DocWithXmp(bytes), ConformanceProfile.PdfA2b)));
    }

    private const string IncompletePropertyPacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/"
    xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#"
    xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#">
   <pdfaExtension:schemas>
    <rdf:Bag>
     <rdf:li rdf:parseType="Resource">
      <pdfaSchema:namespaceURI>http://ns.adobe.com/AdobeHocWorkflow/1.0/</pdfaSchema:namespaceURI>
      <pdfaSchema:prefix>adhocwf</pdfaSchema:prefix>
      <pdfaSchema:schema>AdHoc Workflow Schema</pdfaSchema:schema>
      <pdfaSchema:property>
       <rdf:Seq>
        <rdf:li rdf:parseType="Resource">
         <pdfaProperty:name>state</pdfaProperty:name>
         <pdfaProperty:valueType>Text</pdfaProperty:valueType>
        </rdf:li>
       </rdf:Seq>
      </pdfaSchema:property>
     </rdf:li>
    </rdf:Bag>
   </pdfaExtension:schemas>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    private const string EmptyDescriptionPacket = """
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
 <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
  <rdf:Description rdf:about=""
    xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/"
    xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#"
    xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#">
   <pdfaExtension:schemas>
    <rdf:Bag>
     <rdf:li rdf:parseType="Resource">
      <pdfaSchema:namespaceURI>http://ns.adobe.com/AdobeHocWorkflow/1.0/</pdfaSchema:namespaceURI>
      <pdfaSchema:prefix>adhocwf</pdfaSchema:prefix>
      <pdfaSchema:schema>AdHoc Workflow Schema</pdfaSchema:schema>
      <pdfaSchema:property>
       <rdf:Seq>
        <rdf:li rdf:parseType="Resource">
         <pdfaProperty:name>state</pdfaProperty:name>
         <pdfaProperty:valueType>Text</pdfaProperty:valueType>
         <pdfaProperty:category>internal</pdfaProperty:category>
         <pdfaProperty:description></pdfaProperty:description>
        </rdf:li>
       </rdf:Seq>
      </pdfaSchema:property>
     </rdf:li>
    </rdf:Bag>
   </pdfaExtension:schemas>
  </rdf:Description>
 </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>
""";

    /// <summary>All-or-nothing: a null member on the THIRD of four properties must not leave the first
    /// two already written into the packet.</summary>
    [Fact]
    public void A_null_member_late_in_the_list_leaves_the_packet_unmutated()
    {
        XmpPacket packet = XmpPacket.CreateEmpty();
        XmpExtensionProperty[] properties =
        [
            new("one", "Text", "internal", "First."),
            new("two", "Text", "internal", "Second."),
            new("three", "Text", "internal", null!),
            new("four", "Text", "internal", "Fourth."),
        ];

        var ex = Assert.Throws<ArgumentNullException>(
            () => packet.DeclareExtensionSchema(PdfxNs, "pdfx", "PDF/X ID Schema", properties));

        Assert.Equal("properties", ex.ParamName); // the member is not a parameter; the list is
        Assert.DoesNotContain("pdfaExtension", Encoding.UTF8.GetString(packet.Serialize()));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // ── Document fixture (the rule reads ConformanceContext.XmpTree, which comes off /Metadata) ──

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>A one-page document whose catalog carries the given XMP bytes as /Metadata — the same
    /// hand-built shape the rule-level preflight tests use.</summary>
    private static PdfDocument DocWithXmp(byte[] xmp)
    {
        var doc = new PdfDocument();
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
        };
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };

        doc.AddObject(10, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("Metadata"), [N("Subtype")] = N("XML") }, xmp));
        catalog[N("Metadata")] = Ref(10);

        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }
}
