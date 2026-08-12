using System;
using System.Text;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>
/// The totality contract on <see cref="XmpPacket.Serialize"/>. Backing the packet with an XML tree
/// made the writer stricter than the string concatenation it replaced: <c>System.Xml.Linq</c>
/// rejects a value carrying a NUL ("'.', hexadecimal value 0x00, is an invalid character") or an
/// unpaired surrogate ("The surrogate pair (0xD800, 0x62) is invalid"). That throw would surface out
/// of <c>PdfMetadata.Title</c>/<c>Subject</c>/<c>Keywords</c>, which re-serialize the packet on every
/// assignment — a property setter failing over document data a PDF library routinely meets. Values
/// are therefore sanitized to U+FFFD; only an illegal property NAME, which is caller error and
/// cannot be repaired, still throws.
/// </summary>
public class XmpPacketSerializeRobustnessTests
{
    private const string Ns = "http://example.com/robust/";
    private const string Replacement = "\uFFFD";

    // ── A value that XML 1.0 forbids ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_value_containing_a_nul_serializes_and_the_packet_still_reparses()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Producer", "before\0after");
        pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "Neighbour");

        byte[] bytes = pkt.Serialize(); // must not throw
        XmpPacket reparsed = XmpPacket.Parse(bytes);

        Assert.Equal("before" + Replacement + "after", reparsed.Get(XmpSchemas.Pdf, "Producer")?.Value);

        // The rest of the packet is unaffected — the string writer this replaced emitted the raw NUL
        // and the whole packet then failed to re-parse, taking every other property with it.
        Assert.Equal("Neighbour", reparsed.Get(XmpSchemas.Xmp, "CreatorTool")?.Value);
    }

    [Fact]
    public void A_value_containing_a_lone_surrogate_serializes_and_the_packet_still_reparses()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(Ns, "rb", "lonely", "a\uD800b");
        pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "Neighbour");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize()); // must not throw

        Assert.Equal("a" + Replacement + "b", reparsed.Get(Ns, "lonely")?.Value);
        Assert.Equal("Neighbour", reparsed.Get(XmpSchemas.Xmp, "CreatorTool")?.Value);
    }

    [Fact]
    public void A_well_formed_surrogate_pair_is_not_mangled()
    {
        // The sanitizer must let real astral-plane characters through — U+1F600 only exists as a
        // surrogate pair, so a naive "reject every surrogate" filter would destroy it.
        const string grinning = "\uD83D\uDE00";

        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Dc, XmpSchemas.DcPrefix, "rights", "grin " + grinning + " ok");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());

        Assert.Equal("grin " + grinning + " ok", reparsed.Get(XmpSchemas.Dc, "rights")?.Value);
    }

    [Fact]
    public void Tab_and_newline_are_kept()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(Ns, "rb", "whitespace", "a\tb\nc");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());

        Assert.Equal("a\tb\nc", reparsed.Get(Ns, "whitespace")?.Value);
    }

    [Fact]
    public void A_lang_alt_value_containing_a_control_character_is_sanitized_too()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", "Ti\u0001tle");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize()); // must not throw

        Assert.Equal("Ti" + Replacement + "tle", reparsed.Get(XmpSchemas.Dc, "title")?.LangAlt["x-default"]);
    }

    [Fact]
    public void Sanitizing_leaves_a_clean_value_byte_identical()
    {
        // The sanitizer allocates nothing and changes nothing when there is nothing to fix.
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(Ns, "rb", "clean", "perfectly ordinary text");

        string text = Encoding.UTF8.GetString(pkt.Serialize());

        Assert.Contains("perfectly ordinary text", text);
        Assert.DoesNotContain(Replacement, text);
    }

    // ── A name that is not a name ────────────────────────────────────────────────────────────────

    [Fact]
    public void An_illegal_property_name_throws_ArgumentException()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(Ns, "rb", "not a name", "value");

        // Deliberate and stable: ArgumentException, not the XmlException System.Xml.Linq raises for
        // this particular malformation (it raises ArgumentException for others).
        ArgumentException ex = Assert.Throws<ArgumentException>(() => pkt.Serialize());
        Assert.Contains("not a name", ex.Message);
    }

    [Fact]
    public void An_illegal_namespace_prefix_does_not_throw_and_the_property_survives()
    {
        // A prefix, unlike a name, is repairable — the serializer synthesizes a legal one.
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(Ns, "not a prefix", "thing", "value");

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());

        Assert.Equal("value", reparsed.Get(Ns, "thing")?.Value);
    }
}
