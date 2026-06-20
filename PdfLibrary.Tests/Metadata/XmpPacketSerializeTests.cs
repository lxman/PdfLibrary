using System.Globalization;
using System.Text;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>
/// Tests for XmpPacket serializer and setters: SetSimple, SetArray (Seq/Bag),
/// SetLangAlt, Remove, Serialize/Parse round-trip, padding, trailer PI, encoding,
/// unknown-namespace preservation, and culture-invariance.
/// </summary>
public class XmpPacketSerializeTests
{
    // ── SetSimple + round-trip ────────────────────────────────────────────────

    [Fact]
    public void SetSimple_ThenSerializeAndParse_PreservesValue()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "TestApp 3.0");
        pkt.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Producer", "PdfLib 1.0");

        byte[] bytes = pkt.Serialize();
        XmpPacket parsed = XmpPacket.Parse(bytes);

        XmpProperty? creator = parsed.Get(XmpSchemas.Xmp, "CreatorTool");
        Assert.NotNull(creator);
        Assert.Equal(XmpValueKind.Simple, creator!.Kind);
        Assert.Equal("TestApp 3.0", creator.Value);

        XmpProperty? producer = parsed.Get(XmpSchemas.Pdf, "Producer");
        Assert.NotNull(producer);
        Assert.Equal("PdfLib 1.0", producer!.Value);
    }

    // ── SetArray (Seq ordered) + round-trip ───────────────────────────────────

    [Fact]
    public void SetArray_Ordered_ThenSerializeAndParse_PreservesSeq()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetArray(XmpSchemas.Dc, XmpSchemas.DcPrefix, "creator",
                     new[] { "Alice", "Bob" }, ordered: true);

        XmpPacket parsed = XmpPacket.Parse(pkt.Serialize());

        XmpProperty? prop = parsed.Get(XmpSchemas.Dc, "creator");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.Array, prop!.Kind);
        Assert.True(prop.Ordered, "Should be ordered (Seq)");
        Assert.Equal(new[] { "Alice", "Bob" }, prop.Items);
    }

    // ── SetArray (Bag unordered) + round-trip ─────────────────────────────────

    [Fact]
    public void SetArray_Unordered_ThenSerializeAndParse_PreservesBag()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetArray(XmpSchemas.Dc, XmpSchemas.DcPrefix, "subject",
                     new[] { "pdf", "library" }, ordered: false);

        XmpPacket parsed = XmpPacket.Parse(pkt.Serialize());

        XmpProperty? prop = parsed.Get(XmpSchemas.Dc, "subject");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.Array, prop!.Kind);
        Assert.False(prop.Ordered, "Should be unordered (Bag)");
        Assert.Equal(new[] { "pdf", "library" }, prop.Items);
    }

    // ── SetLangAlt + round-trip ───────────────────────────────────────────────

    [Fact]
    public void SetLangAlt_XDefault_ThenSerializeAndParse_PreservesLangAlt()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", "Hello World");

        XmpPacket parsed = XmpPacket.Parse(pkt.Serialize());

        XmpProperty? prop = parsed.Get(XmpSchemas.Dc, "title");
        Assert.NotNull(prop);
        Assert.Equal(XmpValueKind.LangAlt, prop!.Kind);
        Assert.Equal("Hello World", prop.LangAlt["x-default"]);
    }

    [Fact]
    public void SetLangAlt_TwoLangs_ThenSerializeAndParse_PreservesBoth()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", "Hello", "x-default");
        pkt.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", "Hallo", "de");

        XmpPacket parsed = XmpPacket.Parse(pkt.Serialize());

        XmpProperty? prop = parsed.Get(XmpSchemas.Dc, "title");
        Assert.NotNull(prop);
        Assert.Equal("Hello", prop!.LangAlt["x-default"]);
        Assert.Equal("Hallo", prop.LangAlt["de"]);
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingProperty_DisappearsFromSerialized()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "App");
        pkt.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Producer", "Lib");
        pkt.Remove(XmpSchemas.Xmp, "CreatorTool");

        XmpPacket parsed = XmpPacket.Parse(pkt.Serialize());
        Assert.Null(parsed.Get(XmpSchemas.Xmp, "CreatorTool"));
        Assert.NotNull(parsed.Get(XmpSchemas.Pdf, "Producer"));
    }

    [Fact]
    public void Remove_AbsentProperty_IsNoOp()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.Remove(XmpSchemas.Dc, "title"); // should not throw
        Assert.Empty(pkt.Properties);
    }

    // ── Empty packet serialize → parse ────────────────────────────────────────

    [Fact]
    public void EmptyPacket_SerializeThenParse_ReturnsEmptyPacket()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        byte[] bytes = pkt.Serialize();
        XmpPacket parsed = XmpPacket.Parse(bytes);
        Assert.Empty(parsed.Properties);
    }

    // ── Padding and trailer PI ────────────────────────────────────────────────

    [Fact]
    public void Serialize_ContainsTrailerXpacketEndW()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        string text = Encoding.UTF8.GetString(pkt.Serialize());
        Assert.Contains("<?xpacket end=\"w\"?>", text);
    }

    [Fact]
    public void Serialize_HasAtLeast2KbPaddingBeforeTrailer()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        string text = Encoding.UTF8.GetString(pkt.Serialize());
        int trailerPos = text.IndexOf("<?xpacket end=\"w\"?>", StringComparison.Ordinal);
        Assert.True(trailerPos > 0);
        // Check that there are at least 2048 whitespace-ish chars before the trailer
        // (the padding region is ~2KB of spaces/newlines)
        string beforeTrailer = text[..trailerPos];
        int spacePad = beforeTrailer.Reverse().TakeWhile(c => c is ' ' or '\n' or '\r').Count();
        Assert.True(spacePad >= 2000, $"Expected >=2000 padding chars, got {spacePad}");
    }

    // ── UTF-8 encoding ────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_OutputIsValidUtf8()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", "Résumé");
        byte[] bytes = pkt.Serialize();
        // Should not throw; non-ASCII chars must be encoded as UTF-8
        string decoded = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Résumé", decoded);
    }

    // ── Unknown-namespace property preserved through Serialize → Parse ─────────

    [Fact]
    public void ParsedUnknownNamespaceProp_SurvivesSerializeAndParse()
    {
        // Build a packet that has an unknown namespace property
        string xml = """
            <?xpacket begin="﻿" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about=""
                    xmlns:ex="http://example.com/ns/"
                    xmlns:xmp="http://ns.adobe.com/xap/1.0/">
                  <ex:customProp>Preserved</ex:customProp>
                  <xmp:CreatorTool>App</xmp:CreatorTool>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        byte[] original = Encoding.UTF8.GetBytes(xml);
        XmpPacket pkt = XmpPacket.Parse(original);

        // Mutate: add a known property
        pkt.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Producer", "PdfLib");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());
        Assert.Equal("Preserved", reparsed.Get("http://example.com/ns/", "customProp")?.Value);
        Assert.Equal("App", reparsed.Get(XmpSchemas.Xmp, "CreatorTool")?.Value);
        Assert.Equal("PdfLib", reparsed.Get(XmpSchemas.Pdf, "Producer")?.Value);
    }

    // ── Culture-invariance ────────────────────────────────────────────────────

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void Serialize_UnderNonInvariantCulture_RoundTripsCorrectly(string cultureName)
    {
        CultureInfo prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            XmpPacket pkt = XmpPacket.CreateEmpty();
            pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreateDate",
                          "2026-06-20T13:45:00+00:00");
            pkt.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", "Kultur Test");

            XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());

            Assert.Equal("2026-06-20T13:45:00+00:00",
                         reparsed.Get(XmpSchemas.Xmp, "CreateDate")?.Value);
            Assert.Equal("Kultur Test",
                         reparsed.Get(XmpSchemas.Dc, "title")?.LangAlt["x-default"]);
        }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    // ── Header PI present ─────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ContainsXpacketBeginHeader()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        string text = Encoding.UTF8.GetString(pkt.Serialize());
        Assert.Contains("<?xpacket begin=", text);
        Assert.Contains("id=\"W5M0MpCehiHzreSzNTczkc9d\"", text);
    }

    // ── SetSimple overwrite ───────────────────────────────────────────────────

    [Fact]
    public void SetSimple_OverwritesExistingProperty()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "OldApp");
        pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "NewApp");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());
        Assert.Equal("NewApp", reparsed.Get(XmpSchemas.Xmp, "CreatorTool")?.Value);
        Assert.Equal(1, reparsed.Properties.Count(p => p.LocalName == "CreatorTool"));
    }

    // ── Special XML characters escaped ────────────────────────────────────────

    [Fact]
    public void SetSimple_SpecialXmlChars_EscapedAndRoundTripped()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Keywords", "a & b < c > d");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());
        Assert.Equal("a & b < c > d", reparsed.Get(XmpSchemas.Pdf, "Keywords")?.Value);
    }
}
