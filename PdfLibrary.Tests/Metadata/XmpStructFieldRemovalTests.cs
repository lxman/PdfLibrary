using System;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using PdfLibrary.Xmp;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>Field-granularity removal: strip ONE named field from a struct-valued property, or from
/// every item of an array of structs, leaving the rest of the packet byte-for-byte alone.
///
/// <para><b>Why this exists.</b> PDF/A-2 validates structured types as CLOSED — one field the 2005-era
/// Annex B tables do not list invalidates the entire struct (see <c>XmpTypeContainer</c>). Two
/// post-2005 fields do this in the measured corpora: <c>stRef:originalDocumentID</c> (17 of 701
/// real-world documents) and <c>stEvt:changed</c> (18). Both are legitimate, current XMP vocabulary —
/// Adobe XMPCore and ExifTool both accept them — so the only route to conformance is removal, and
/// removal must be SURGICAL. Every existing write path on this facade replaces a whole property, which
/// for a struct means losing every sibling field: exactly what the round-trip program was built to
/// stop, and why <c>Pellucid.Core</c>'s XmpDomain refuses to rewrite structs at all.</para>
///
/// <para>This is the narrow capability that makes the refusal unnecessary for one specific repair,
/// without weakening it for anything else.</para></summary>
public sealed class XmpStructFieldRemovalTests
{
    private const string MmNs = "http://ns.adobe.com/xap/1.0/mm/";
    private const string RefNs = "http://ns.adobe.com/xap/1.0/sType/ResourceRef#";
    private const string EvtNs = "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#";

    /// <summary>A realistic media-management packet: a DerivedFrom struct and a two-entry History
    /// array of structs, each carrying one field PDF/A-2 does not know plus several it does.</summary>
    private static byte[] Packet() => Encoding.UTF8.GetBytes("""
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                   xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/"
                   xmlns:stEvt="http://ns.adobe.com/xap/1.0/sType/ResourceEvent#"
                   xmlns:stRef="http://ns.adobe.com/xap/1.0/sType/ResourceRef#">
            <rdf:Description rdf:about="">
              <dc:format>application/pdf</dc:format>
              <xmpMM:OriginalDocumentID>uuid:ORIGINAL</xmpMM:OriginalDocumentID>
              <xmpMM:DerivedFrom rdf:parseType="Resource">
                <stRef:instanceID>uuid:INSTANCE</stRef:instanceID>
                <stRef:documentID>uuid:DOCUMENT</stRef:documentID>
                <stRef:originalDocumentID>uuid:ORIGINAL</stRef:originalDocumentID>
              </xmpMM:DerivedFrom>
              <xmpMM:History>
                <rdf:Seq>
                  <rdf:li rdf:parseType="Resource">
                    <stEvt:action>created</stEvt:action>
                    <stEvt:when>2026-01-01T00:00:00Z</stEvt:when>
                    <stEvt:changed>/</stEvt:changed>
                  </rdf:li>
                  <rdf:li rdf:parseType="Resource">
                    <stEvt:action>saved</stEvt:action>
                    <stEvt:when>2026-02-02T00:00:00Z</stEvt:when>
                    <stEvt:changed>/metadata</stEvt:changed>
                  </rdf:li>
                </rdf:Seq>
              </xmpMM:History>
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """);

    private static string RoundTrip(XmpPacket packet) => Encoding.UTF8.GetString(packet.Serialize());

    // ── the struct case ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Removes_the_named_field_from_a_struct()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());

        bool removed = packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID");

        Assert.True(removed);
        string outXml = RoundTrip(packet);
        Assert.DoesNotContain("stRef:originalDocumentID", outXml);
    }

    /// <summary>The sibling fields survive. This is the assertion the whole capability exists for:
    /// every other write path on this facade replaces the property and takes them with it.</summary>
    [Fact]
    public void Leaves_the_structs_other_fields_intact()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());
        packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID");

        string outXml = RoundTrip(packet);

        Assert.Contains("uuid:INSTANCE", outXml);
        Assert.Contains("uuid:DOCUMENT", outXml);
        Assert.Contains("stRef:instanceID", outXml);
        Assert.Contains("stRef:documentID", outXml);
    }

    /// <summary>Unrelated properties are untouched — including the TOP-LEVEL twin of the field being
    /// removed, which is what makes this particular removal lossless in the measured corpus (17 of 17
    /// documents carrying <c>stRef:originalDocumentID</c> also carry an identical
    /// <c>xmpMM:OriginalDocumentID</c>).</summary>
    [Fact]
    public void Leaves_unrelated_properties_including_the_top_level_twin()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());
        packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID");

        Assert.Equal("uuid:ORIGINAL", packet.Get(MmNs, "OriginalDocumentID")!.Value);
        Assert.Equal("application/pdf", packet.Get("http://purl.org/dc/elements/1.1/", "format")!.Value);
        Assert.Contains("stEvt:changed", RoundTrip(packet)); // the field we deliberately do NOT strip
    }

    // ── the array-of-structs case ────────────────────────────────────────────────────────────────

    /// <summary>An array of structs strips the field from EVERY item. <c>xmpMM:History</c> is the
    /// shape that matters: a per-item repair that fixed only the first entry would leave the property
    /// invalid and the finding open, while having already destroyed data.</summary>
    [Fact]
    public void Removes_the_named_field_from_every_item_of_an_array_of_structs()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());

        bool removed = packet.RemoveStructField(MmNs, "History", EvtNs, "changed");

        Assert.True(removed);
        string outXml = RoundTrip(packet);
        Assert.DoesNotContain("stEvt:changed", outXml);
        // Both entries survive, with their own fields.
        Assert.Contains("created", outXml);
        Assert.Contains("saved", outXml);
        Assert.Contains("2026-01-01T00:00:00Z", outXml);
        Assert.Contains("2026-02-02T00:00:00Z", outXml);
    }

    // ── guards: it must not remove what it was not asked to ──────────────────────────────────────

    /// <summary>A field of the right local name in a DIFFERENT namespace is not the same field.
    /// Struct fields routinely live in a namespace other than the property's.</summary>
    [Fact]
    public void Does_not_remove_a_same_named_field_from_another_namespace()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());

        bool removed = packet.RemoveStructField(MmNs, "DerivedFrom", EvtNs, "originalDocumentID");

        Assert.False(removed);
        Assert.Contains("stRef:originalDocumentID", RoundTrip(packet));
    }

    /// <summary>Absent property, absent field, and a property that is not a struct at all are all
    /// no-ops reporting false — never a throw, and never a partial edit. A fixer asks about documents
    /// it has not inspected field-by-field, so "nothing to do" must be an ordinary answer.</summary>
    [Theory]
    [InlineData(MmNs, "NoSuchProperty", RefNs, "originalDocumentID")]
    [InlineData(MmNs, "DerivedFrom", RefNs, "noSuchField")]
    [InlineData(MmNs, "OriginalDocumentID", RefNs, "originalDocumentID")] // a simple property
    [InlineData("http://purl.org/dc/elements/1.1/", "format", RefNs, "originalDocumentID")]
    public void Reports_false_and_changes_nothing_when_there_is_nothing_to_remove(
        string ns, string local, string fieldNs, string fieldLocal)
    {
        XmpPacket packet = XmpPacket.Parse(Packet());
        string before = RoundTrip(packet);

        bool removed = packet.RemoveStructField(ns, local, fieldNs, fieldLocal);

        Assert.False(removed);
        Assert.Equal(before, RoundTrip(packet));
    }

    /// <summary>Removing the last field leaves an EMPTY struct rather than deleting the property.
    /// Deleting it would be a different repair than the one asked for — the caller asked to remove a
    /// field, and a fixer that quietly removes more than it was asked to is the failure mode this
    /// whole area is built around.</summary>
    [Fact]
    public void Removing_the_only_field_leaves_an_empty_struct_not_a_deleted_property()
    {
        byte[] oneField = Encoding.UTF8.GetBytes("""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                       xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/"
                       xmlns:stRef="http://ns.adobe.com/xap/1.0/sType/ResourceRef#">
                <rdf:Description rdf:about="">
                  <xmpMM:DerivedFrom rdf:parseType="Resource">
                    <stRef:originalDocumentID>uuid:ORIGINAL</stRef:originalDocumentID>
                  </xmpMM:DerivedFrom>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """);
        XmpPacket packet = XmpPacket.Parse(oneField);

        Assert.True(packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID"));

        Assert.NotNull(packet.Get(MmNs, "DerivedFrom"));
        Assert.DoesNotContain("originalDocumentID", RoundTrip(packet));
    }

    /// <summary>Null arguments are caller error, matching every other setter on this facade.</summary>
    [Fact]
    public void Null_arguments_throw()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());

        Assert.Throws<ArgumentNullException>(() => packet.RemoveStructField(null!, "DerivedFrom", RefNs, "x"));
        Assert.Throws<ArgumentNullException>(() => packet.RemoveStructField(MmNs, null!, RefNs, "x"));
        Assert.Throws<ArgumentNullException>(() => packet.RemoveStructField(MmNs, "DerivedFrom", null!, "x"));
        Assert.Throws<ArgumentNullException>(() => packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, null!));
    }

    /// <summary>The removal survives a re-parse: it edited the MODEL, not just the emitted bytes.</summary>
    [Fact]
    public void The_removal_survives_a_round_trip()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());
        packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID");

        XmpPacket reparsed = XmpPacket.Parse(packet.Serialize());
        string outXml = Encoding.UTF8.GetString(reparsed.Serialize());

        Assert.DoesNotContain("stRef:originalDocumentID", outXml);
        Assert.Contains("stRef:instanceID", outXml);
        Assert.Equal("uuid:ORIGINAL", reparsed.Get(MmNs, "OriginalDocumentID")!.Value);
    }

    // ── the point of the whole exercise: does the strip CLOSE the finding? ───────────────────────

    /// <summary>Strip the field, and the conformance rule that flagged the struct stops flagging it.
    ///
    /// <para>The capability tests above prove the removal is surgical; this proves it is USEFUL.
    /// Without it the repair could remove exactly the right field, damage nothing, and still leave the
    /// document non-conformant — which is the shape of a fix that satisfies its own tests and does not
    /// help anyone. It also pins the closed-struct behaviour from the other side: the finding exists
    /// BEFORE and is gone AFTER, so a future widening of the field tables makes this fail loudly
    /// rather than quietly turning the repair into a no-op.</para></summary>
    [Fact]
    public void Stripping_the_field_closes_the_conformance_finding()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());

        Finding[] before = TypeFindings(packet.Serialize());
        Assert.Contains(before, f => f.RuleId == "pdfa-xmp-property-type");

        packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID");
        packet.RemoveStructField(MmNs, "History", EvtNs, "changed");

        Assert.DoesNotContain(TypeFindings(packet.Serialize()), f => f.RuleId == "pdfa-xmp-property-type");
    }

    /// <summary>Each field is independently load-bearing: stripping ONLY <c>stRef:originalDocumentID</c>
    /// — the automatic half of the agreed split — closes <c>xmpMM:DerivedFrom</c> while
    /// <c>xmpMM:History</c> keeps both its <c>stEvt:changed</c> data AND its finding. That is the
    /// intended outcome, not a shortfall: the History repair destroys provenance recorded nowhere else,
    /// so it stays a decision a person makes per document.</summary>
    [Fact]
    public void Stripping_only_the_lossless_field_closes_only_its_own_struct()
    {
        XmpPacket packet = XmpPacket.Parse(Packet());
        packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID");

        Finding[] after = TypeFindings(packet.Serialize());

        Assert.Contains("stEvt:changed", RoundTrip(packet));
        Finding remaining = Assert.Single(after, f => f.RuleId == "pdfa-xmp-property-type");
        Assert.Contains("History", remaining.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs the real rule over a real document carrying the given packet — the same route
    /// <c>PreflightSlice14Tests</c> uses, so the two cannot drift on what "flagged" means.</summary>
    private static Finding[] TypeFindings(byte[] xmp)
    {
        var doc = new PdfDocument();
        doc.AddObject(2, 0, new PdfStream(new PdfDictionary(), xmp));
        var catalog = new PdfDictionary();
        catalog[new PdfName("Type")] = new PdfName("Catalog");
        catalog[new PdfName("Metadata")] = new PdfIndirectReference(2, 0);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);

        return new XmpPropertyTypeRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .ToArray();
    }

    /// <summary>A struct captured as RawXml (a shape the node model cannot express) is NOT edited —
    /// the snapshot is what the serializer emits, so removing a child node would change the model
    /// while the packet still wrote the field back out. Reporting false is the honest answer: the
    /// caller learns the repair did not happen instead of believing a lie.</summary>
    [Fact]
    public void Refuses_to_edit_a_struct_preserved_as_a_verbatim_snapshot()
    {
        byte[] qualified = Encoding.UTF8.GetBytes("""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                       xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/"
                       xmlns:stRef="http://ns.adobe.com/xap/1.0/sType/ResourceRef#">
                <rdf:Description rdf:about="">
                  <xmpMM:DerivedFrom rdf:parseType="Collection">
                    <stRef:originalDocumentID>uuid:ORIGINAL</stRef:originalDocumentID>
                  </xmpMM:DerivedFrom>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            """);
        XmpPacket packet = XmpPacket.Parse(qualified);

        bool removed = packet.RemoveStructField(MmNs, "DerivedFrom", RefNs, "originalDocumentID");

        Assert.False(removed);
        Assert.Contains("originalDocumentID", RoundTrip(packet));
    }
}
