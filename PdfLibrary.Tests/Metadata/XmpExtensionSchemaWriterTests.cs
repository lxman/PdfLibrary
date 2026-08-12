using System;
using System.Collections.Generic;
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
