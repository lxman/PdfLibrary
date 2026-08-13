using System.Text;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

/// <summary>Round-trip fidelity for RDF shapes the 2026-08-13 standards audit found PdfLibrary's own
/// parser/serializer losing or reshaping. One region per defect, keyed to
/// <c>Docs/superpowers/specs/2026-08-13-xmp-round-trip-fidelity-design.md</c>.
///
/// <para>Every case here was REPRODUCED against the shipped code before its fix was written — the
/// audit found these by reading, and a defect nobody has demonstrated may not be reachable. The
/// fragments are the ones the spec documents, so the spec and these tests cannot drift.</para>
///
/// <para>Deliberately not <c>LocalOnly</c> and sub-second: <c>ci.yml</c> filters
/// <c>Category!=LocalOnly</c>, and this repo has previously lost a fixture that way.</para></summary>
public sealed class XmpRoundTripFidelityTests
{
    private static byte[] Packet(string body) => Encoding.UTF8.GetBytes($"""
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                   xmlns:dc="http://purl.org/dc/elements/1.1/"
                   xmlns:xmp="http://ns.adobe.com/xap/1.0/">
            <rdf:Description rdf:about="">
        {body}
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """);

    private static string RoundTrip(string body) =>
        Encoding.UTF8.GetString(XmpPacket.Parse(Packet(body)).Serialize());

    // ── D1: xml:lang on a plain literal property ────────────────────────────────────────────────

    /// <summary>A language qualifier on a SIMPLE property survives the round trip.
    ///
    /// <para>It did not before 2026-08-13. <c>XmpTreeParser</c> populated
    /// <c>HasXmlLang</c>/<c>XmlLang</c> only in <c>SetArray</c>, for <c>rdf:li</c> items, so a plain
    /// literal's language was never read and could not be re-emitted; the <c>RawXml</c> safety net
    /// engages only in the <c>rdf:value</c> branch, which a plain literal never enters. Part 1 treats
    /// <c>xml:lang</c> as a qualifier legal on ANY value, not only on array items.</para>
    ///
    /// <para>The serializer needed no change: <c>EmitShape</c> already emitted the attribute for any
    /// node carrying it, ahead of its shape dispatch. The defect was entirely on the read side.</para></summary>
    [Fact]
    public void Xml_lang_on_a_simple_property_survives_the_round_trip()
    {
        string outXml = RoundTrip("""      <dc:source xml:lang="en-us">Photo by A. Person</dc:source>""");

        Assert.Contains("Photo by A. Person", outXml);
        Assert.Contains("xml:lang=\"en-us\"", outXml);
    }

    /// <summary>The language qualifier is readable from the parsed packet, not merely re-emitted —
    /// so a consumer sees it too, rather than it surviving only as bytes.</summary>
    [Fact]
    public void Xml_lang_on_a_simple_property_round_trips_through_a_second_parse()
    {
        string once = RoundTrip("""      <dc:source xml:lang="en-us">Photo by A. Person</dc:source>""");
        string twice = Encoding.UTF8.GetString(
            XmpPacket.Parse(Encoding.UTF8.GetBytes(once)).Serialize());

        // Idempotence is the real property: a qualifier that survives one pass but not the next is
        // still lost, just later — and a document is commonly re-saved more than once.
        Assert.Contains("xml:lang=\"en-us\"", twice);
    }

    /// <summary>A simple property with NO language is unchanged — the fix must not invent a
    /// qualifier, which would be its own corruption and would alter every existing document.</summary>
    [Fact]
    public void A_simple_property_without_a_language_gains_none()
    {
        string outXml = RoundTrip("""      <dc:source>Photo by A. Person</dc:source>""");

        Assert.Contains("Photo by A. Person", outXml);
        Assert.DoesNotContain("xml:lang", outXml);
    }

    /// <summary>A lang-alt still round-trips: its items' languages are read by <c>SetArray</c>, and
    /// the D1 fix must not disturb that path or double-emit the attribute.</summary>
    [Fact]
    public void A_lang_alt_still_round_trips_with_its_item_languages()
    {
        string outXml = RoundTrip("""
              <dc:title><rdf:Alt>
                <rdf:li xml:lang="x-default">Title</rdf:li>
                <rdf:li xml:lang="fr-FR">Titre</rdf:li>
              </rdf:Alt></dc:title>
        """);

        Assert.Contains("xml:lang=\"x-default\"", outXml);
        Assert.Contains("xml:lang=\"fr-FR\"", outXml);
        Assert.Contains("Titre", outXml);
    }

    // ── D3 / D5: rdf:parseType values XMP forbids ───────────────────────────────────────────────
    //
    // Part 1 §C.2.10 forbids parseType="Collection" and §C.2.11 forbids any other parseType (i.e.
    // anything but "Resource" and "Literal"; "Literal" is RDF-legal but has no XMP data model). A
    // document carrying one is ALREADY INVALID, so the goal here is not to model these shapes — it is
    // to stop silently mangling them, so the bytes a human would inspect still say what the producer
    // actually wrote. Preservation, not interpretation.

    /// <summary>An <c>rdf:parseType="Collection"</c> property survives intact.
    ///
    /// <para>Before 2026-08-13 it lost everything after the first item AND was re-emitted as
    /// <c>parseType="Resource"</c> — silently rewriting one forbidden production into a different,
    /// legal-looking one. <c>el.Element(Rdf + "Description")</c> returns only the FIRST match, and the
    /// struct branch then treated that single Description as the whole value.</para></summary>
    [Fact]
    public void A_parse_type_collection_property_survives_the_round_trip()
    {
        string outXml = RoundTrip("""
              <xmp:Things rdf:parseType="Collection">
                <rdf:Description><dc:title>one</dc:title></rdf:Description>
                <rdf:Description><dc:title>two</dc:title></rdf:Description>
              </xmp:Things>
        """);

        Assert.Contains("one", outXml);
        Assert.Contains("two", outXml);                       // was dropped entirely
        Assert.Contains("parseType=\"Collection\"", outXml);  // was rewritten to "Resource"
    }

    /// <summary>An <c>rdf:parseType="Literal"</c> property keeps its mixed content.
    ///
    /// <para>Before 2026-08-13, <c>rich &lt;b&gt;text&lt;/b&gt;</c> came back as
    /// <c>&lt;ns1:b&gt;text&lt;/ns1:b&gt;</c>: the bare text node "rich" was destroyed and the literal
    /// was reshaped into a struct, because <c>HasStructContent</c> sees element children and the
    /// struct branch keeps only element-shaped fields.</para></summary>
    [Fact]
    public void A_parse_type_literal_property_keeps_its_mixed_content()
    {
        string outXml = RoundTrip("""
              <xmp:Note rdf:parseType="Literal"><p xmlns="http://www.w3.org/1999/xhtml">rich <b>text</b></p></xmp:Note>
        """);

        Assert.Contains("rich", outXml);   // the bare text node, previously destroyed
        Assert.Contains("text", outXml);
        Assert.Contains("parseType=\"Literal\"", outXml);
    }

    /// <summary>An ORDINARY <c>parseType="Resource"</c> struct is untouched by the D3/D5 fix.
    ///
    /// <para>The guard that matters most here. Capturing verbatim is the right answer for a shape the
    /// model cannot express and the WRONG answer for one it can: routing ordinary structs through the
    /// raw path would freeze them against every later edit and change the serializer's output for the
    /// overwhelming majority of real packets. Passed before the fix as well as after.</para></summary>
    [Fact]
    public void An_ordinary_resource_struct_is_not_captured_verbatim()
    {
        string outXml = RoundTrip("""
              <xmp:Thing rdf:parseType="Resource">
                <dc:title>a</dc:title>
              </xmp:Thing>
        """);

        Assert.Contains("parseType=\"Resource\"", outXml);
        Assert.Contains("<dc:title>a</dc:title>", outXml);
        // The serializer re-declares namespaces on the elements it builds; a verbatim capture would
        // instead reproduce the source fragment's own prefixes. Asserting the REBUILT shape is what
        // distinguishes "went through the normal path" from "was snapshotted".
        Assert.DoesNotContain("parseType=\"Collection\"", outXml);
    }

    // ── D2: the rdf:Alt projection ──────────────────────────────────────────────────────────────
    //
    // Unlike D1/D3/D5 this is NOT document data loss — the probe confirmed the serializer works from
    // the node, so a multi-item untagged Alt re-serializes with every item intact. The damage is in
    // the PROJECTION: XmpProperty reported Kind=LangAlt with ONE entry, because it keyed every item
    // on `XmlLang ?? "x-default"` and later items overwrote earlier ones.
    //
    // It matters because consumers act on the projection and can write back. Pellucid's
    // XmpDomain.ComparableValue reads exactly this to decide whether a rewrite would narrow a value,
    // so the fixer could judge — and rewrite — a property having seen one of its three values.

    private static XmpProperty? Project(string body, string ns, string name) =>
        XmpPacket.Parse(Packet(body)).Get(ns, name);

    private const string XmpNs = "http://ns.adobe.com/xap/1.0/";
    private const string DcNs = "http://purl.org/dc/elements/1.1/";

    /// <summary>A multi-item <c>rdf:Alt</c> whose items carry no <c>xml:lang</c> is an ordinary
    /// alternatives array, not a language alternative (Part 1 §6.3.4: Alt is general-purpose;
    /// language is one use of it, not its definition). All items must reach the consumer.</summary>
    [Fact]
    public void A_multi_item_alt_without_languages_projects_every_item()
    {
        XmpProperty? p = Project("""
              <xmp:Nickname><rdf:Alt>
                <rdf:li>first</rdf:li><rdf:li>second</rdf:li><rdf:li>third</rdf:li>
              </rdf:Alt></xmp:Nickname>
        """, XmpNs, "Nickname");

        Assert.NotNull(p);
        Assert.Equal(XmpValueKind.Array, p!.Kind);
        Assert.Equal(["first", "second", "third"], p.Items);
    }

    /// <summary>A SINGLE-item untagged <c>rdf:Alt</c> still projects as a Lang Alt under
    /// <c>x-default</c>.
    ///
    /// <para>Load-bearing, and the reason the old behaviour existed: a <c>dc:title</c> written without
    /// <c>xml:lang</c> has to keep reaching <c>PdfMetadata.Title</c> and <c>UaTitleRule</c>. What the
    /// old code never intended was to let SIBLING items overwrite one another. Passed before the fix
    /// as well as after.</para></summary>
    [Fact]
    public void A_single_item_alt_without_a_language_still_projects_as_lang_alt()
    {
        XmpProperty? p = Project("""      <dc:title><rdf:Alt><rdf:li>Only</rdf:li></rdf:Alt></dc:title>""",
                                 DcNs, "title");

        Assert.NotNull(p);
        Assert.Equal(XmpValueKind.LangAlt, p!.Kind);
        Assert.Equal("Only", p.LangAlt["x-default"]);
    }

    /// <summary>A genuine lang alt — every item carrying <c>xml:lang</c> — is unchanged.</summary>
    [Fact]
    public void A_real_lang_alt_still_projects_as_lang_alt_with_every_language()
    {
        XmpProperty? p = Project("""
              <dc:title><rdf:Alt>
                <rdf:li xml:lang="x-default">Title</rdf:li>
                <rdf:li xml:lang="fr-FR">Titre</rdf:li>
              </rdf:Alt></dc:title>
        """, DcNs, "title");

        Assert.NotNull(p);
        Assert.Equal(XmpValueKind.LangAlt, p!.Kind);
        Assert.Equal("Title", p.LangAlt["x-default"]);
        Assert.Equal("Titre", p.LangAlt["fr-FR"]);
    }

    /// <summary>The document was never damaged by D2 and must stay undamaged: the node keeps every
    /// item and the serializer emits them, regardless of how the projection reads it.</summary>
    [Fact]
    public void A_multi_item_alt_still_round_trips_every_item()
    {
        string outXml = RoundTrip("""
              <xmp:Nickname><rdf:Alt>
                <rdf:li>first</rdf:li><rdf:li>second</rdf:li><rdf:li>third</rdf:li>
              </rdf:Alt></xmp:Nickname>
        """);

        Assert.Contains("first", outXml);
        Assert.Contains("second", outXml);
        Assert.Contains("third", outXml);
        Assert.Contains("<rdf:Alt>", outXml);   // still an Alt, not silently downgraded to a Seq
    }

    // ── D4: the RDF typed-node struct form ──────────────────────────────────────────────────────
    //
    // <ns:Prop><ns:Type>fields</ns:Type></ns:Prop> — Part 1 §7.9.2.5's "Typed Node form of a
    // nodeElement", equivalent to <rdf:Description rdf:type="ns:Type">. The type name is an ASSERTION
    // about the value, not a field of it.
    //
    // The mildest of the nine: no content is lost and the parse is idempotent. What is lost is the
    // type assertion — the outer element silently GAINS rdf:parseType="Resource" on output, turning
    // "this value is a typed node of type X" into "this is a struct with a field named X". Different
    // RDF, same bytes-worth of text.
    //
    // Preserved rather than reinterpreted, deliberately. Reinterpreting means deciding that a
    // property's single element child is a TYPE rather than a FIELD, and for real packets that is
    // ambiguous: <xmp:Prop><xmp:Field>text</xmp:Field></xmp:Prop> must stay a struct with one field.
    // Guessing wrong there would destroy a field name, which is worse than the assertion being lost —
    // and no producer in the corpus emits typed nodes at all.

    /// <summary>A typed-node struct is re-emitted as it was written, not rewritten into a struct
    /// whose field is named after the type.</summary>
    [Fact]
    public void A_typed_node_struct_keeps_its_type_assertion()
    {
        string outXml = RoundTrip("""
              <xmp:Thing><xmp:ThingType rdf:parseType="Resource">
                <dc:title>a</dc:title>
              </xmp:ThingType></xmp:Thing>
        """);

        Assert.Contains("ThingType", outXml);
        Assert.Contains("a", outXml);
        // The defect: the OUTER element gained parseType="Resource", asserting that ThingType is a
        // field of Thing rather than Thing's type.
        Assert.DoesNotContain("<xmp:Thing rdf:parseType=\"Resource\">", outXml);
    }

    /// <summary>A property whose single element child is a SIMPLE value is a struct with one field,
    /// and must not be mistaken for a typed node.
    ///
    /// <para>The guard that constrains the D4 fix. Treating this child as a type name would destroy
    /// the field name outright — a worse loss than the one D4 repairs. Passed before the fix as well
    /// as after.</para></summary>
    [Fact]
    public void A_struct_with_one_simple_field_is_not_mistaken_for_a_typed_node()
    {
        string outXml = RoundTrip("""      <xmp:Thing><xmp:Field>text</xmp:Field></xmp:Thing>""");

        Assert.Contains("Field", outXml);
        Assert.Contains("text", outXml);
    }
}
