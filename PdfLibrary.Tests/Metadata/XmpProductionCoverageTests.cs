using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Metadata;
using PdfLibrary.Xmp;
using Xunit;
using Xunit.Sdk;

namespace PdfLibrary.Tests.Metadata;

/// <summary>What this engine does with EVERY production of the XMP serialization grammar — ISO
/// 16684-1 (XMP Specification Part 1) Annex C, "RDF parsing information", productions 7.2.9–7.2.34.
///
/// <para><b>Why this file exists.</b> The 2026-08-13 standards audit found nine round-trip defects by
/// reading the parser against the spec. Every one of them lived in a production nothing tested and no
/// comment described — the per-defect fixtures in <see cref="XmpRoundTripFidelityTests"/> cover the
/// nine shapes that were FOUND, which is exactly the coverage that let them survive the first pass.
/// This file is the complement: it enumerates the grammar rather than the bugs, so a tenth defect has
/// to displace a row that says out loud what we do today.</para>
///
/// <para><b>It is a description, not a wish.</b> Every <see cref="Handling"/> below records what the
/// engine ACTUALLY does, verified by the assertions in the same row — including where that is not
/// what the spec would prefer. A row that reads <see cref="Handling.Captured"/> is not a TODO: for
/// the productions XMP forbids outright it is the correct answer, because the input is already
/// invalid and mangling it into something legal-looking would destroy the evidence. Changing a
/// verdict here is a deliberate act, which is the entire point.</para>
///
/// <para><b>Deliberately not <c>LocalOnly</c> and sub-second</b>, like its sibling: <c>ci.yml</c>
/// filters <c>Category!=LocalOnly</c> and this repo has lost a fixture to that before.</para></summary>
public sealed class XmpProductionCoverageTests
{
    /// <summary>What the engine does with a production.</summary>
    public enum Handling
    {
        /// <summary>Parsed into the node model: the shape is represented by <see cref="XmpNode"/>
        /// facets, every consumer sees it, and the serializer rebuilds it from the model.</summary>
        Modelled,

        /// <summary>Snapshotted into <see cref="XmpNode.RawXml"/> and re-emitted from that snapshot.
        /// The node ALSO carries its normal best-effort classification (capture is additive), so
        /// conformance rules keep the verdict they always reached — but the model does not represent
        /// the shape, and the serializer copies rather than rebuilds it.</summary>
        Captured,

        /// <summary>Not surfaced as a property at all. The packet parses (the parser never throws);
        /// this particular construct contributes nothing to the model.</summary>
        Dropped,
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────────

    private static byte[] Packet(string body) => Encoding.UTF8.GetBytes($"""
        <x:xmpmeta xmlns:x="adobe:ns:meta/">
          <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about=""
                xmlns:dc="http://purl.org/dc/elements/1.1/"
                xmlns:ns="http://example.com/ns/">
        {body}
            </rdf:Description>
          </rdf:RDF>
        </x:xmpmeta>
        """);

    private const string ExNs = "http://example.com/ns/";

    private static XmpNode? Node(string body, string localName) =>
        XmpPacket.Parse(Packet(body)).Nodes.FirstOrDefault(n => n.LocalName == localName);

    /// <summary>The capture question is about the SUBTREE, not the top node: an unmodelled shape
    /// nested in an array item snapshots onto the ITEM while the property node's own RawXml stays
    /// null. Mirrors <c>XmpConformance.CarriesRawXml</c>, which learned this the same way.</summary>
    private static bool CarriesRawXml(XmpNode node) =>
        node.RawXml is not null || node.Children.Any(CarriesRawXml);

    /// <summary>Asserts the engine handles <paramref name="body"/>'s property exactly as the row
    /// claims, and returns the node so a row can make production-specific assertions on top.</summary>
    private static XmpNode AssertHandling(string body, string localName, Handling expected)
    {
        XmpNode? node = Node(body, localName);

        if (expected == Handling.Dropped)
        {
            Assert.Null(node);
            return new XmpNode(string.Empty, string.Empty, string.Empty);
        }

        Assert.NotNull(node);
        Assert.Equal(expected == Handling.Captured, CarriesRawXml(node!));

        // Whatever the handling, the packet must survive a round trip: re-serializing and re-parsing
        // yields the same handling verdict again. A shape that degrades on the second pass is the
        // failure mode this whole program was about.
        byte[] once = XmpPacket.Parse(Packet(body)).Serialize();
        XmpNode? again = XmpPacket.Parse(once).Nodes.FirstOrDefault(n => n.LocalName == localName);
        Assert.NotNull(again);
        Assert.Equal(expected == Handling.Captured, CarriesRawXml(again!));

        return node!;
    }

    // ── C.2.2–C.2.4: rdf:RDF and the nodeElement ─────────────────────────────────────────────────

    /// <summary>7.2.9 RDF / 7.2.10 nodeElementList / 7.2.11 nodeElement, top-level form. XMP requires
    /// the outermost element to be <c>rdf:RDF</c> and a top-level nodeElement to be
    /// <c>rdf:Description</c>; both are the canonical shape and are fully modelled.
    ///
    /// <para>Related and deliberately NOT here: <c>rdf:RDF</c> nested in a wrapper that is not
    /// <c>x:xmpmeta</c> is not found at all (defect D9). Dropped 2026-08-13 with a recorded ruling —
    /// the input is not a conformant packet per Part 3, and widening the search could only ADD
    /// findings, the one direction the parity contract forbids.</para></summary>
    [Fact]
    public void Production_7_2_9_rdf_RDF_with_a_top_level_description() =>
        AssertHandling("""      <dc:source>v</dc:source>""", "source", Handling.Modelled);

    /// <summary>C.2.4: "Other attributes (propertyAttr) of a top-level nodeElement become simple
    /// unqualified properties in the XMP packet" — the attribute serialization, which real producers
    /// use heavily (the ZUGFeRD generator writes <c>xmp:CreatorTool</c> this way).</summary>
    [Fact]
    public void Production_7_2_25_propertyAttr_on_a_top_level_nodeElement_is_a_property()
    {
        // The attribute form lives on rdf:Description itself, so it cannot go through Packet(body).
        byte[] bytes = Encoding.UTF8.GetBytes("""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/"
                                 dc:source="from an attribute"/>
              </rdf:RDF>
            </x:xmpmeta>
            """);

        XmpProperty? p = XmpPacket.Parse(bytes).Get("http://purl.org/dc/elements/1.1/", "source");

        Assert.NotNull(p);
        Assert.Equal(XmpValueKind.Simple, p!.Kind);
        Assert.Equal("from an attribute", p.Value);
    }

    /// <summary>C.2.4, inner nodeElement: its propertyAttrs "become simple unqualified fields of the
    /// XMP struct value represented by the nodeElement".</summary>
    [Fact]
    public void Production_7_2_25_propertyAttr_on_an_inner_nodeElement_is_a_struct_field()
    {
        XmpNode node = AssertHandling(
            """      <ns:Struct><rdf:Description ns:field="f"/></ns:Struct>""",
            "Struct", Handling.Modelled);

        Assert.True(node.IsStruct);
        XmpNode field = Assert.Single(node.Children);
        Assert.Equal("field", field.LocalName);
        Assert.Equal("f", field.Value);
    }

    // ── C.2.6: the resourcePropertyElt, in its three documented forms ────────────────────────────

    /// <summary>7.2.15 resourcePropertyElt, struct form: an inner <c>rdf:Description</c> nodeElement
    /// whose contained elements are the struct's fields.</summary>
    [Fact]
    public void Production_7_2_15_resourcePropertyElt_struct_form()
    {
        XmpNode node = AssertHandling(
            """      <ns:Struct><rdf:Description><ns:Field>f</ns:Field></rdf:Description></ns:Struct>""",
            "Struct", Handling.Modelled);

        Assert.True(node.IsStruct);
        Assert.Equal("Field", Assert.Single(node.Children).LocalName);
    }

    /// <summary>7.2.15 resourcePropertyElt, array form — an <c>rdf:Bag</c>/<c>Seq</c>/<c>Alt</c>
    /// nodeElement. C.2.6 notes the canonical array form is itself a Typed Node, "more easily dealt
    /// with as direct special cases"; this engine takes exactly that route, which is also why the
    /// typed-node capture below must exclude rdf:-namespaced children or every array would freeze
    /// into raw XML.</summary>
    [Theory]
    [InlineData("Bag", false, false)]
    [InlineData("Seq", true, false)]
    [InlineData("Alt", true, true)]
    public void Production_7_2_15_resourcePropertyElt_array_form(string container, bool ordered, bool alternate)
    {
        XmpNode node = AssertHandling(
            $"""      <ns:Array><rdf:{container}><rdf:li>one</rdf:li></rdf:{container}></ns:Array>""",
            "Array", Handling.Modelled);

        Assert.True(node.IsArray);
        Assert.Equal(ordered, node.IsArrayOrdered);
        Assert.Equal(alternate, node.IsArrayAlternate);
    }

    /// <summary>7.2.15 resourcePropertyElt, GENERAL QUALIFIER form: C.2.6's "pseudo-structs with a
    /// special rdf:value field". CAPTURED, not modelled — <see cref="XmpNode"/> has nowhere to hang a
    /// qualifier, and without the snapshot the qualifier became a struct field and ate the value.
    ///
    /// <para>Modelling qualifiers properly is explicitly out of scope in the design: the capture
    /// preserves them today and parity does not require them, so it is a larger change with no
    /// demonstrated need.</para></summary>
    [Fact]
    public void Production_7_2_15_resourcePropertyElt_general_qualifier_form()
    {
        XmpNode node = AssertHandling("""
              <ns:Prop>
                <rdf:Description>
                  <rdf:value>the value</rdf:value>
                  <ns:Qual>the qualifier</ns:Qual>
                </rdf:Description>
              </ns:Prop>
        """, "Prop", Handling.Captured);

        // Capture is ADDITIVE: the node still classifies as it always did, so every reader that is
        // not the serializer keeps its pre-existing verdict.
        Assert.True(node.IsSimple);
        Assert.Equal("the value", node.Value);
    }

    /// <summary>C.2.6 / 7.9.2.5, the RDF TYPED NODE form: <c>&lt;ns:Prop&gt;&lt;ns:Type&gt;…</c>,
    /// shorthand that elevates an <c>rdf:type</c> qualifier into the element name. CAPTURED (defect
    /// D4). PRESERVED, NOT REINTERPRETED — deciding that a property's single element child names a
    /// TYPE rather than a FIELD is ambiguous in real packets, and guessing wrong destroys a field
    /// name, a worse loss than the one being repaired.</summary>
    [Fact]
    public void Production_7_9_2_5_typed_node_form() =>
        AssertHandling(
            """      <ns:Prop><ns:Type><ns:Field>f</ns:Field></ns:Type></ns:Prop>""",
            "Prop", Handling.Captured);

    // ── C.2.7–C.2.11: the literal and parseType propertyElts ─────────────────────────────────────

    /// <summary>7.2.16 literalPropertyElt — the typical simple property.</summary>
    [Fact]
    public void Production_7_2_16_literalPropertyElt()
    {
        XmpNode node = AssertHandling("""      <dc:source>v</dc:source>""", "source", Handling.Modelled);

        Assert.True(node.IsSimple);
        Assert.Equal("v", node.Value);
    }

    /// <summary>7.2.16 with <c>xml:lang</c>: C.2.7 says the attribute "becomes an xml:lang qualifier
    /// on the XMP value" — on ANY value, not only on array items. Reading it only inside
    /// <c>rdf:li</c> handling was defect D1.</summary>
    [Fact]
    public void Production_7_2_16_literalPropertyElt_with_xml_lang()
    {
        XmpNode node = AssertHandling(
            """      <dc:source xml:lang="en-us">v</dc:source>""", "source", Handling.Modelled);

        Assert.True(node.HasXmlLang);
        Assert.Equal("en-us", node.XmlLang);
    }

    /// <summary>7.2.17 parseTypeLiteralPropertyElt. <b>XMP forbids this production</b> (C.2.8: "not
    /// allowed by XMP"), so the packet is already invalid on arrival. CAPTURED: the job is not to
    /// model it, it is to stop MANGLING it — parsed as a struct, <c>rich &lt;b&gt;text&lt;/b&gt;</c>
    /// lost the bare word "rich" and the literal became a struct (defect D5).</summary>
    [Fact]
    public void Production_7_2_17_parseTypeLiteralPropertyElt()
    {
        AssertHandling(
            """      <ns:Prop rdf:parseType="Literal">rich <ns:b>text</ns:b></ns:Prop>""",
            "Prop", Handling.Captured);

        string outXml = Encoding.UTF8.GetString(XmpPacket.Parse(Packet(
            """      <ns:Prop rdf:parseType="Literal">rich <ns:b>text</ns:b></ns:Prop>""")).Serialize());

        Assert.Contains("rich ", outXml);
        Assert.Contains("parseType=\"Literal\"", outXml);
    }

    /// <summary>7.2.18 parseTypeResourcePropertyElt — the struct shorthand, and the ONE parseType
    /// this model represents. C.2.9 calls it "a cleaner way to represent a struct"; real producers
    /// use it for every <c>xmpMM:History</c> item.</summary>
    [Fact]
    public void Production_7_2_18_parseTypeResourcePropertyElt()
    {
        XmpNode node = AssertHandling(
            """      <ns:Struct rdf:parseType="Resource"><ns:Field>f</ns:Field></ns:Struct>""",
            "Struct", Handling.Modelled);

        Assert.True(node.IsStruct);
        Assert.Equal("Field", Assert.Single(node.Children).LocalName);
    }

    /// <summary>7.2.19 parseTypeCollectionPropertyElt. <b>XMP forbids this production</b> (C.2.10).
    /// CAPTURED: parsed as a struct it lost every item after the first AND came back out as
    /// <c>parseType="Resource"</c> — one forbidden production silently rewritten into a legal-looking
    /// different one (defect D3).</summary>
    [Fact]
    public void Production_7_2_19_parseTypeCollectionPropertyElt()
    {
        const string body = """
              <ns:List rdf:parseType="Collection">
                <rdf:Description><ns:Field>one</ns:Field></rdf:Description>
                <rdf:Description><ns:Field>two</ns:Field></rdf:Description>
              </ns:List>
        """;
        AssertHandling(body, "List", Handling.Captured);

        string outXml = Encoding.UTF8.GetString(XmpPacket.Parse(Packet(body)).Serialize());

        Assert.Contains("two", outXml);                          // item two is not destroyed
        Assert.Contains("parseType=\"Collection\"", outXml);     // and the production is not rewritten
    }

    /// <summary>7.2.20 parseTypeOtherPropertyElt — any <c>rdf:parseType</c> value that is not
    /// Resource, Literal or Collection. <b>XMP forbids this production</b> (C.2.11). CAPTURED, by the
    /// same "not Resource" test that catches Literal and Collection, so an unknown future parseType
    /// is preserved rather than reshaped.</summary>
    [Fact]
    public void Production_7_2_20_parseTypeOtherPropertyElt()
    {
        AssertHandling(
            """      <ns:Prop rdf:parseType="Whatever"><ns:Field>f</ns:Field></ns:Prop>""",
            "Prop", Handling.Captured);

        string outXml = Encoding.UTF8.GetString(XmpPacket.Parse(Packet(
            """      <ns:Prop rdf:parseType="Whatever"><ns:Field>f</ns:Field></ns:Prop>""")).Serialize());

        Assert.Contains("parseType=\"Whatever\"", outXml);
    }

    // ── C.2.12: the emptyPropertyElt, all four XMP mapping rules ─────────────────────────────────
    //
    // C.2.12 gives four rules that must be applied IN ORDER. Each is a row here, in that order, so a
    // future reader can check the engine against the list without re-deriving it.

    /// <summary>Rule 1: "If there is an rdf:value attribute, then this is a simple property. All
    /// other attributes are qualifiers." The value is modelled; the qualifiers force a capture, the
    /// same answer as the element-form qualified value above.
    ///
    /// <para>Works here because a QUALIFIER attribute is present. That is load-bearing, not
    /// incidental — see the two rows below, where the same <c>rdf:value</c> attribute without a
    /// qualifier beside it is lost.</para></summary>
    [Fact]
    public void Production_7_2_21_emptyPropertyElt_rule1_rdf_value_attribute()
    {
        XmpNode node = AssertHandling(
            """      <ns:Prop rdf:value="the value" ns:Qual="the qualifier"/>""",
            "Prop", Handling.Captured);

        Assert.True(node.IsSimple);
        Assert.Equal("the value", node.Value);
    }

    /// <summary>Rule 1 with no qualifier attribute beside the <c>rdf:value</c> — the shape that WAS
    /// defect D10 (found by this file 2026-08-13, fixed the same day). The value was destroyed: the
    /// property survived with an empty value and re-serializing wrote that emptiness back.
    ///
    /// <para>Cause: the attribute form of <c>rdf:value</c> was consulted only inside the struct
    /// branch, which is entered on struct CONTENT — an element child, or a property attribute that
    /// could be a field. <c>rdf:value</c> is rdf:-namespaced, so it is not itself struct content, and
    /// an element carrying nothing else never reached the code that would read it. With one qualifier
    /// attribute present (the row above) the branch WAS entered and the value was found — the working
    /// shape and the broken one differed by an attribute with nothing to do with the value, which is
    /// why reading the parser missed it twice.</para></summary>
    [Fact]
    public void Production_7_2_21_emptyPropertyElt_rule1_without_a_qualifier()
    {
        XmpNode node = AssertHandling("""      <ns:Prop rdf:value="the value"/>""", "Prop", Handling.Modelled);

        Assert.True(node.IsSimple);
        Assert.Equal("the value", node.Value);

        // And it survives the save, in the canonical element form. Rule 1 says the value IS the
        // property's value; which RDF alternate carried it in is not itself information, the same
        // reading under which an attribute-form property on rdf:Description comes back as an element.
        string outXml = Encoding.UTF8.GetString(
            XmpPacket.Parse(Packet("""      <ns:Prop rdf:value="the value"/>""")).Serialize());
        Assert.Contains(">the value<", outXml);
    }

    /// <summary>Rule 1 with an <c>xml:lang</c> beside it — still rule 1, and the language qualifier
    /// rides along. <c>xml:lang</c> is not a competing mapping rule; C.2.12 lists it among the
    /// attributes rule 3 ignores precisely because it is a qualifier in any position.</summary>
    [Fact]
    public void Production_7_2_21_emptyPropertyElt_rule1_with_xml_lang()
    {
        XmpNode node = AssertHandling(
            """      <ns:Prop rdf:value="the value" xml:lang="en-us"/>""", "Prop", Handling.Modelled);

        Assert.True(node.IsSimple);
        Assert.Equal("the value", node.Value);
        Assert.Equal("en-us", node.XmlLang);
    }

    /// <summary>Rule ORDER: C.2.12 requires its four rules "be applied in the order shown", so
    /// <c>rdf:value</c> (rule 1) outranks <c>rdf:resource</c> (rule 2). The engine applied them
    /// backwards — the URI became the value and the <c>rdf:value</c> was dropped, so the property
    /// came back asserting something the producer did not write (D10, second face).
    ///
    /// <para>CAPTURED rather than merely re-ordered. Rule 1 makes every other attribute a qualifier,
    /// and this model has nowhere to hang one — modelling the value alone would fix the projection
    /// while quietly dropping the <c>rdf:resource</c> from the saved document. The snapshot keeps
    /// both, and the projection answers rule 1. C.2.12 calls this combination "discouraged" and no
    /// corpus document uses it, so preserving what was written beats interpreting it.</para></summary>
    [Fact]
    public void Production_7_2_21_emptyPropertyElt_applies_rule1_before_rule2()
    {
        const string body = """      <ns:Prop rdf:value="the value" rdf:resource="http://example.com/v"/>""";

        XmpNode node = AssertHandling(body, "Prop", Handling.Captured);

        Assert.True(node.IsSimple);
        Assert.Equal("the value", node.Value);   // rule 1 wins
        Assert.False(node.IsUriValue);           // the value is not the URI reference

        string outXml = Encoding.UTF8.GetString(XmpPacket.Parse(Packet(body)).Serialize());
        Assert.Contains("rdf:value=\"the value\"", outXml);
        Assert.Contains("rdf:resource=\"http://example.com/v\"", outXml);
    }

    /// <summary>Rule 2: "If there is an rdf:resource attribute, then this is a simple property with a
    /// URI value." Modelled, and §7.5's attribute FORM is preserved on the way out (defect D6) — the
    /// URI always survived; what was lost was the RDF meaning.</summary>
    [Fact]
    public void Production_7_2_21_emptyPropertyElt_rule2_rdf_resource_attribute()
    {
        XmpNode node = AssertHandling(
            """      <ns:Prop rdf:resource="http://example.com/v"/>""", "Prop", Handling.Modelled);

        Assert.True(node.IsSimple);
        Assert.True(node.IsUriValue);
        Assert.Equal("http://example.com/v", node.Value);
    }

    /// <summary>Rule 3: "If there are no attributes other than xml:lang, rdf:ID, or rdf:nodeID, then
    /// this is a simple property with an empty value."</summary>
    [Fact]
    public void Production_7_2_21_emptyPropertyElt_rule3_empty_value()
    {
        XmpNode node = AssertHandling("""      <ns:Prop/>""", "Prop", Handling.Modelled);

        Assert.True(node.IsSimple);
        Assert.Equal(string.Empty, node.Value);
    }

    /// <summary>Rule 4: "Finally, this is a struct, and the attributes other than xml:lang, rdf:ID,
    /// or rdf:nodeID are the fields."</summary>
    [Fact]
    public void Production_7_2_21_emptyPropertyElt_rule4_struct_of_simple_fields()
    {
        XmpNode node = AssertHandling(
            """      <ns:Prop ns:Field1="one" ns:Field2="two"/>""", "Prop", Handling.Modelled);

        Assert.True(node.IsStruct);
        Assert.Equal(["Field1", "Field2"], node.Children.Select(c => c.LocalName));
    }

    // ── The attributes XMP singles out (C.2.1, C.2.4, C.2.7) ─────────────────────────────────────

    /// <summary>7.2.24 aboutAttr — the one RDF identity attribute XMP allows, and only on a top-level
    /// nodeElement. It identifies the described resource rather than carrying a property value, so it
    /// is not surfaced as a property; the serializer writes its own <c>rdf:about=""</c>.</summary>
    [Fact]
    public void Production_7_2_24_aboutAttr_is_not_a_property()
    {
        Assert.Null(Node("""      <dc:source>v</dc:source>""", "about"));

        string outXml = Encoding.UTF8.GetString(
            XmpPacket.Parse(Packet("""      <dc:source>v</dc:source>""")).Serialize());
        Assert.Contains("rdf:about=\"\"", outXml);
    }

    /// <summary>7.2.22 idAttr — <c>rdf:ID</c> is "not allowed in XMP" at every production that
    /// mentions it (C.2.5, C.2.6, C.2.7, C.2.9, C.2.12). Never modelled, and since 2026-08-13 never
    /// RE-EMITTED either: it is stripped from the capture snapshot at any depth, because passing it
    /// through made us the writer of a forbidden construct (defect D7).</summary>
    [Fact]
    public void Production_7_2_22_idAttr_is_dropped_and_never_re_emitted()
    {
        const string body = """
              <ns:Prop rdf:ID="r1">
                <rdf:Description>
                  <rdf:value>v</rdf:value>
                  <ns:Qual rdf:ID="deep">q</ns:Qual>
                </rdf:Description>
              </ns:Prop>
        """;
        AssertHandling(body, "Prop", Handling.Captured);

        string outXml = Encoding.UTF8.GetString(XmpPacket.Parse(Packet(body)).Serialize());

        Assert.DoesNotContain("rdf:ID", outXml);
        Assert.Contains("q", outXml); // the subtree it was attached to is otherwise intact
    }

    /// <summary>7.2.23 nodeIdAttr — <c>rdf:nodeID</c> is likewise "not allowed in XMP" (C.2.5,
    /// C.2.12). It is an RDF blank-node label with no XMP meaning, and it is not surfaced as a
    /// property or a field.</summary>
    [Fact]
    public void Production_7_2_23_nodeIdAttr_is_not_a_property_or_field()
    {
        XmpNode node = AssertHandling(
            """      <ns:Prop rdf:nodeID="n1"/>""", "Prop", Handling.Modelled);

        Assert.DoesNotContain(node.Children, c => c.LocalName == "nodeID");
    }

    /// <summary>7.2.27 datatypeAttr — <c>rdf:datatype</c> is "not allowed in XMP" (C.2.7). XMP has no
    /// datatype concept: every value is text plus its qualifiers.</summary>
    [Fact]
    public void Production_7_2_27_datatypeAttr_is_not_a_property_or_field()
    {
        XmpNode node = AssertHandling(
            """      <ns:Prop rdf:datatype="http://www.w3.org/2001/XMLSchema#integer">42</ns:Prop>""",
            "Prop", Handling.Modelled);

        Assert.True(node.IsSimple);
        Assert.Equal("42", node.Value);
        Assert.DoesNotContain(node.Children, c => c.LocalName == "datatype");
    }

    /// <summary>C.2.4: "XMP does not allow an xml:lang attribute on a nodeElement." One on an inner
    /// <c>rdf:Description</c> is dropped — not carried onto the struct, not re-emitted. The input is
    /// already non-conformant and the attribute has no XMP meaning in that position, so declining to
    /// invent one is the safe direction: nothing is claimed that the producer did not write.</summary>
    [Fact]
    public void Production_7_2_11_xml_lang_on_an_inner_nodeElement_is_dropped()
    {
        XmpNode node = AssertHandling(
            """      <ns:Prop><rdf:Description xml:lang="en"><ns:F>f</ns:F></rdf:Description></ns:Prop>""",
            "Prop", Handling.Modelled);

        Assert.True(node.IsStruct);
        Assert.False(node.HasXmlLang);
    }

    /// <summary>C.2.3: "In XMP, a top-level nodeElement can only be rdf:Description." A typed node in
    /// top-level position is therefore not a conformant packet, and this engine reads nothing from
    /// it — the parse yields an EMPTY packet rather than guessing at the producer's intent.
    ///
    /// <para>Under-reporting, which is the direction a subset validator is allowed to err in. Same
    /// reasoning that dropped defect D9 (an <c>rdf:RDF</c> in a non-<c>x:xmpmeta</c> wrapper): seeing
    /// MORE in a malformed packet can only add findings, and the parity snapshot cannot vouch for a
    /// document shape the corpus does not contain.</para></summary>
    [Fact]
    public void Production_7_2_11_a_top_level_typed_node_yields_nothing()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <ns:MyType xmlns:ns="http://example.com/ns/"><ns:Field>f</ns:Field></ns:MyType>
              </rdf:RDF>
            </x:xmpmeta>
            """);

        Assert.Empty(XmpPacket.Parse(bytes).Properties);
    }

    /// <summary>7.2.6 propertyElementURIs excludes the RDF syntax terms, so an rdf:-namespaced
    /// element in property position is not a property at all. DROPPED — the parser treats every
    /// rdf:-namespaced child as structural, which is what keeps <c>rdf:type</c> from becoming a
    /// struct field and is the same test that handles the oldTerms below.
    ///
    /// <para>The one genuinely dropped production, and it is the right answer: <c>rdf:type</c> is an
    /// RDF type assertion, not an XMP property, and there is no XMP property name it could be given
    /// without inventing one.</para></summary>
    [Fact]
    public void Production_7_2_6_an_rdf_namespaced_element_in_property_position_is_dropped() =>
        AssertHandling(
            """      <rdf:type rdf:resource="http://example.com/Type"/>""", "type", Handling.Dropped);

    /// <summary>7.2.4 oldTerms — <c>rdf:aboutEach</c>, <c>rdf:aboutEachPrefix</c>, <c>rdf:bagID</c>:
    /// withdrawn from RDF before XMP existed and excluded from every URI set in the grammar
    /// (7.2.5–7.2.7). They are rdf:-namespaced, which is the same test that keeps <c>rdf:type</c>
    /// out, so they never become properties or fields.</summary>
    [Theory]
    [InlineData("aboutEach")]
    [InlineData("aboutEachPrefix")]
    [InlineData("bagID")]
    public void Production_7_2_4_oldTerms_are_not_properties(string term)
    {
        XmpNode node = AssertHandling(
            $"""      <ns:Prop rdf:parseType="Resource"><rdf:{term}>x</rdf:{term}><ns:Field>f</ns:Field></ns:Prop>""",
            "Prop", Handling.Modelled);

        Assert.Equal("Field", Assert.Single(node.Children).LocalName);
    }

    // ── The coverage claim itself ────────────────────────────────────────────────────────────────

    /// <summary>The roll-call: Annex C defines exactly seven propertyElt forms (7.2.14), and this
    /// asserts the engine's handling of every one of them in a single place — the assertion that
    /// makes this a COVERAGE test rather than a pile of fixtures.
    ///
    /// <para>It RUNS each form's fixture rather than restating a verdict the rows above established.
    /// A table that merely repeated the answers would be a comment with a test's privileges: it could
    /// go stale the moment the parser changed, which is precisely the failure mode this file exists
    /// to prevent.</para>
    ///
    /// <para>The form names are written out from the spec rather than discovered by reflection over
    /// test names, deliberately: the claim being made is that a human read the grammar and wrote down
    /// which productions exist. Reflection would only prove this file is self-consistent.</para></summary>
    [Fact]
    public void Every_propertyElt_form_in_production_7_2_14_is_covered()
    {
        // 7.2.14 propertyElt: the seven alternatives, each with the fixture that exercises it.
        (string Form, string Body, string Local, Handling Handling)[] forms =
        [
            ("resourcePropertyElt", """      <ns:Prop><rdf:Description><ns:Field>f</ns:Field></rdf:Description></ns:Prop>""",
                "Prop", Handling.Modelled),
            ("literalPropertyElt", """      <ns:Prop>v</ns:Prop>""",
                "Prop", Handling.Modelled),
            ("parseTypeLiteralPropertyElt", """      <ns:Prop rdf:parseType="Literal">rich <ns:b>text</ns:b></ns:Prop>""",
                "Prop", Handling.Captured),      // forbidden by XMP
            ("parseTypeResourcePropertyElt", """      <ns:Prop rdf:parseType="Resource"><ns:Field>f</ns:Field></ns:Prop>""",
                "Prop", Handling.Modelled),
            ("parseTypeCollectionPropertyElt", """      <ns:Prop rdf:parseType="Collection"><rdf:Description><ns:Field>f</ns:Field></rdf:Description></ns:Prop>""",
                "Prop", Handling.Captured),      // forbidden by XMP
            ("parseTypeOtherPropertyElt", """      <ns:Prop rdf:parseType="Whatever"><ns:Field>f</ns:Field></ns:Prop>""",
                "Prop", Handling.Captured),      // forbidden by XMP
            ("emptyPropertyElt", """      <ns:Prop ns:Field1="one"/>""",
                "Prop", Handling.Modelled),
        ];

        Assert.Equal(7, forms.Length);
        Assert.All(forms, f => AssertHandling(f.Body, f.Local, f.Handling));

        // The invariant the audit's worst findings all violated: a production XMP FORBIDS is
        // captured — never modelled, never dropped. Reshaping an invalid production into a
        // legal-looking one destroys the evidence that the producer wrote something invalid, which is
        // worse than either preserving it or refusing it.
        Assert.All(
            forms.Where(f => f.Form.StartsWith("parseType") && f.Form != "parseTypeResourcePropertyElt"),
            f => Assert.Equal(Handling.Captured, f.Handling));
    }

    /// <summary>The harness discriminates. Every row above asserts a handling verdict, and a verdict
    /// only means something if the WRONG one fails — a check that would pass either way is the shape
    /// of test that let nine defects through in the first place.</summary>
    [Fact]
    public void The_handling_assertion_fails_when_the_verdict_is_wrong()
    {
        const string modelled = """      <ns:Prop>v</ns:Prop>""";
        const string captured = """      <ns:Prop rdf:parseType="Literal">rich <ns:b>text</ns:b></ns:Prop>""";

        Assert.ThrowsAny<XunitException>(() => AssertHandling(modelled, "Prop", Handling.Captured));
        Assert.ThrowsAny<XunitException>(() => AssertHandling(captured, "Prop", Handling.Modelled));
        Assert.ThrowsAny<XunitException>(() => AssertHandling(modelled, "Prop", Handling.Dropped));
        Assert.ThrowsAny<XunitException>(() => AssertHandling(modelled, "NoSuchProperty", Handling.Modelled));
    }
}
