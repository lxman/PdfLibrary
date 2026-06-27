using System.Text;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>
/// Tests for XmpPacket.Parse: attribute-form, element-form, Seq, Bag, Alt, unknown namespaces,
/// garbage input tolerance, BOM/xpacket-envelope stripping.
/// </summary>
public class XmpPacketParseTests
{
    // ── helper ────────────────────────────────────────────────────────────────

    private static byte[] Wrap(string innerXml)
    {
        string full = $"""
            <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about=""
                    xmlns:dc="http://purl.org/dc/elements/1.1/"
                    xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                    xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
                    xmlns:ex="http://example.com/ns/">
                  {innerXml}
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        return Encoding.UTF8.GetBytes(full);
    }

    // ── (a) attribute-form simple properties ──────────────────────────────────

    [Fact]
    public void Parse_AttributeFormSimple_SurfacesProperty()
    {
        // We need to put attributes on rdf:Description, not as child elements
        string full = $"""
            <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about=""
                    xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                    xmlns:pdf="http://ns.adobe.com/pdf/1.3/"
                    xmp:CreatorTool="MyApp 1.0"
                    pdf:Producer="PdfLib">
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        byte[] bytes = Encoding.UTF8.GetBytes(full);
        XmpPacket pkt = XmpPacket.Parse(bytes);

        XmpProperty? creator = pkt.Get(XmpSchemas.Xmp, "CreatorTool");
        Assert.NotNull(creator);
        Assert.Equal(XmpValueKind.Simple, creator!.Kind);
        Assert.Equal("MyApp 1.0", creator.Value);

        XmpProperty? producer = pkt.Get(XmpSchemas.Pdf, "Producer");
        Assert.NotNull(producer);
        Assert.Equal("PdfLib", producer!.Value);
    }

    // ── (b) element-form simple ───────────────────────────────────────────────

    [Fact]
    public void Parse_ElementFormSimple_SurfacesProperty()
    {
        byte[] bytes = Wrap("<xmp:CreatorTool>ElementApp 2.0</xmp:CreatorTool>");
        XmpPacket pkt = XmpPacket.Parse(bytes);

        XmpProperty? prop = pkt.Get(XmpSchemas.Xmp, "CreatorTool");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.Simple, prop!.Kind);
        Assert.Equal("ElementApp 2.0", prop.Value);
    }

    // ── (c) rdf:Seq (ordered array) ───────────────────────────────────────────

    [Fact]
    public void Parse_RdfSeq_SurfacesOrderedArray()
    {
        byte[] bytes = Wrap("""
            <dc:creator>
              <rdf:Seq>
                <rdf:li>Alice</rdf:li>
                <rdf:li>Bob</rdf:li>
              </rdf:Seq>
            </dc:creator>
            """);
        XmpPacket pkt = XmpPacket.Parse(bytes);

        XmpProperty? prop = pkt.Get(XmpSchemas.Dc, "creator");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.Array, prop!.Kind);
        Assert.True(prop.Ordered, "rdf:Seq should be ordered");
        Assert.Equal(new[] { "Alice", "Bob" }, prop.Items);
    }

    // ── (d) rdf:Bag (unordered array) ────────────────────────────────────────

    [Fact]
    public void Parse_RdfBag_SurfacesUnorderedArray()
    {
        byte[] bytes = Wrap("""
            <dc:subject>
              <rdf:Bag>
                <rdf:li>pdf</rdf:li>
                <rdf:li>library</rdf:li>
                <rdf:li>csharp</rdf:li>
              </rdf:Bag>
            </dc:subject>
            """);
        XmpPacket pkt = XmpPacket.Parse(bytes);

        XmpProperty? prop = pkt.Get(XmpSchemas.Dc, "subject");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.Array, prop!.Kind);
        Assert.False(prop.Ordered, "rdf:Bag should be unordered");
        Assert.Equal(new[] { "pdf", "library", "csharp" }, prop.Items);
    }

    // ── (e) rdf:Alt with two languages ───────────────────────────────────────

    [Fact]
    public void Parse_RdfAlt_SurfacesLangAltWithTwoLangs()
    {
        byte[] bytes = Wrap("""
            <dc:title>
              <rdf:Alt>
                <rdf:li xml:lang="x-default">Hello World</rdf:li>
                <rdf:li xml:lang="de">Hallo Welt</rdf:li>
              </rdf:Alt>
            </dc:title>
            """);
        XmpPacket pkt = XmpPacket.Parse(bytes);

        XmpProperty? prop = pkt.Get(XmpSchemas.Dc, "title");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.LangAlt, prop!.Kind);
        Assert.Equal("Hello World", prop.LangAlt["x-default"]);
        Assert.Equal("Hallo Welt",  prop.LangAlt["de"]);
    }

    // ── (f) unknown namespace preserved ──────────────────────────────────────

    [Fact]
    public void Parse_UnknownNamespace_SurfacesInProperties()
    {
        byte[] bytes = Wrap("<ex:customProp>SomeValue</ex:customProp>");
        XmpPacket pkt = XmpPacket.Parse(bytes);

        XmpProperty? prop = pkt.Get("http://example.com/ns/", "customProp");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.Simple, prop!.Kind);
        Assert.Equal("SomeValue", prop.Value);
    }

    // ── garbage bytes → tolerant empty ───────────────────────────────────────

    [Fact]
    public void Parse_GarbageBytes_ReturnsEmptyPacket()
    {
        byte[] garbage = Encoding.UTF8.GetBytes("this is not xml !!!");
        XmpPacket pkt = XmpPacket.Parse(garbage);
        Assert.Empty(pkt.Properties);
    }

    [Fact]
    public void Parse_EmptyArray_ReturnsEmptyPacket()
    {
        XmpPacket pkt = XmpPacket.Parse(Array.Empty<byte>());
        Assert.Empty(pkt.Properties);
    }

    [Fact]
    public void Parse_RandomBinaryBytes_ReturnsEmptyPacket()
    {
        byte[] bin = new byte[256];
        for (int i = 0; i < 256; i++) bin[i] = (byte)i;
        XmpPacket pkt = XmpPacket.Parse(bin);
        Assert.Empty(pkt.Properties);
    }

    // ── BOM and xpacket envelope stripping ───────────────────────────────────

    [Fact]
    public void Parse_WithUtf8Bom_StripsCorrectly()
    {
        // Prepend a UTF-8 BOM manually in addition to the one in the xpacket PI
        byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
        byte[] inner = Encoding.UTF8.GetBytes("""
            <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about=""
                    xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                    xmp:CreatorTool="BomApp">
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """);
        byte[] withBom = bom.Concat(inner).ToArray();
        XmpPacket pkt = XmpPacket.Parse(withBom);
        XmpProperty? prop = pkt.Get(XmpSchemas.Xmp, "CreatorTool");
        Assert.NotNull(prop);
        Assert.Equal("BomApp", prop!.Value);
    }

    // ── Properties enumeration ────────────────────────────────────────────────

    [Fact]
    public void Properties_ReturnsAllParsedProperties()
    {
        byte[] bytes = Wrap("""
            <xmp:CreatorTool>App</xmp:CreatorTool>
            <pdf:Producer>PdfLib</pdf:Producer>
            """);
        XmpPacket pkt = XmpPacket.Parse(bytes);
        Assert.Equal(2, pkt.Properties.Count());
    }

    // ── Get returns null for missing property ─────────────────────────────────

    [Fact]
    public void Get_MissingProperty_ReturnsNull()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        Assert.Null(pkt.Get(XmpSchemas.Dc, "title"));
    }
}
