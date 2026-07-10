using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 14 + XMP value-type extension — XMP predefined/declared-schema property validation
/// (ISO 19005-2, 6.6.2.3.1): the predefined-set rule (<see cref="XmpPropertyPredefinedRule"/>) and the
/// value-type rule (<see cref="XmpPropertyTypeRule"/>). These CI-safe synthetic fixtures pin the exact
/// zero-false-positive behaviours over the full value-type surface (simple, array, lang-alt, date,
/// struct, array-of-struct, and extension-schema resolution); the corpus-driven parity harness
/// (Category=Parity) measures real detection.
/// </summary>
public class PreflightSlice14Tests
{
    // Wraps XMP <rdf:Description> content into a full packet.
    private static byte[] Xmp(string descriptions) => Encoding.UTF8.GetBytes(
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + descriptions
        + "</rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");

    private static PdfDocument DocWithXmp(byte[] xmp)
    {
        var doc = new PdfDocument();
        doc.AddObject(2, 0, new PdfStream(new PdfDictionary(), xmp));
        var catalog = new PdfDictionary();
        catalog[new PdfName("Type")] = new PdfName("Catalog");
        catalog[new PdfName("Metadata")] = new PdfIndirectReference(2, 0);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    private static Finding[] PredefinedFindings(string descriptions) =>
        new XmpPropertyPredefinedRule().Check(Ctx(DocWithXmp(Xmp(descriptions)))).ToArray();

    private static Finding[] TypeFindings(string descriptions) =>
        new XmpPropertyTypeRule().Check(Ctx(DocWithXmp(Xmp(descriptions)))).ToArray();

    // Common namespace declarations for the test Description elements.
    private const string Ns =
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" "
        + "xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" "
        + "xmlns:xmpRights=\"http://ns.adobe.com/xap/1.0/rights/\" "
        + "xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\" "
        + "xmlns:stRef=\"http://ns.adobe.com/xap/1.0/sType/ResourceRef#\" "
        + "xmlns:photoshop=\"http://ns.adobe.com/photoshop/1.0/\"";

    // A valid extension schema declaring one property (custom:<name> of the given XMP valueType),
    // followed by a Description that uses <custom:name> with the given serialization.
    private static string WithExtensionSchema(string name, string valueType, string usage) =>
        "<rdf:Description rdf:about=\"\" "
        + "xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\" "
        + "xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\" "
        + "xmlns:pdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\">"
        + "<pdfaExtension:schemas><rdf:Bag><rdf:li rdf:parseType=\"Resource\">"
        + "<pdfaSchema:namespaceURI>http://example.com/custom/</pdfaSchema:namespaceURI>"
        + "<pdfaSchema:prefix>custom</pdfaSchema:prefix>"
        + "<pdfaSchema:schema>Custom schema</pdfaSchema:schema>"
        + "<pdfaSchema:property><rdf:Seq><rdf:li rdf:parseType=\"Resource\">"
        + $"<pdfaProperty:name>{name}</pdfaProperty:name>"
        + $"<pdfaProperty:valueType>{valueType}</pdfaProperty:valueType>"
        + "<pdfaProperty:category>external</pdfaProperty:category>"
        + "</rdf:li></rdf:Seq></pdfaSchema:property>"
        + "</rdf:li></rdf:Bag></pdfaExtension:schemas></rdf:Description>"
        + "<rdf:Description rdf:about=\"\" xmlns:custom=\"http://example.com/custom/\">" + usage + "</rdf:Description>";

    // ── Rule A: predefined-or-extension-declared ─────────────────────────────────

    [Fact]
    public void Predefined_properties_are_not_flagged()
    {
        // dc:format (mimetype) + xmpRights:Marked (boolean) — both predefined.
        Finding[] f = PredefinedFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} dc:format=\"application/pdf\" xmpRights:Marked=\"True\"/>");
        Assert.Empty(f);
    }

    [Fact]
    public void Non_predefined_property_without_extension_schema_is_flagged()
    {
        Finding[] f = PredefinedFindings(
            "<rdf:Description rdf:about=\"\" xmlns:custom=\"http://example.com/custom/\" "
            + "custom:madeUp=\"x\"/>");
        Finding finding = Assert.Single(f);
        Assert.Equal("pdfa-xmp-property-predefined", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Extension_declared_property_passes_rule_a()
    {
        // custom:madeUp is declared (valueType Text) by a valid extension schema → resolved, not flagged.
        Assert.Empty(PredefinedFindings(
            WithExtensionSchema("madeUp", "Text", "<custom:madeUp>hello</custom:madeUp>")));
    }

    [Fact]
    public void Property_not_declared_by_its_extension_schema_is_flagged()
    {
        // The schema declares custom:madeUp, but the packet uses custom:other → still undefined.
        Finding[] f = PredefinedFindings(
            WithExtensionSchema("madeUp", "Text", "<custom:other>x</custom:other>"));
        Assert.Equal("pdfa-xmp-property-predefined", Assert.Single(f).RuleId);
    }

    [Fact]
    public void Extension_schema_container_property_is_never_flagged()
    {
        // The pdfaExtension:schemas container itself (and its nested schema/property nodes) is the
        // standard's own structure — never reported as a stray property.
        Assert.Empty(PredefinedFindings(
            WithExtensionSchema("madeUp", "Text", "<custom:madeUp>hello</custom:madeUp>")));
    }

    [Fact]
    public void Structural_rdf_namespaces_are_never_flagged()
    {
        // An rdf:-namespaced attribute must never be reported as a stray property.
        Finding[] f = PredefinedFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} dc:format=\"application/pdf\"/>");
        Assert.Empty(f);
    }

    // ── Rule B: value type — simple / array / lang-alt ───────────────────────────

    [Fact]
    public void Correct_boolean_value_passes()
    {
        Assert.Empty(TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} xmpRights:Marked=\"True\"/>"));
    }

    [Fact]
    public void Wrong_boolean_value_is_flagged()
    {
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} xmpRights:Marked=\"yes\"/>");
        Finding finding = Assert.Single(f);
        Assert.Equal("pdfa-xmp-property-type", finding.RuleId);
    }

    [Fact]
    public void Wrong_integer_value_is_flagged()
    {
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} photoshop:Urgency=\"high\"/>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    [Fact]
    public void Simple_type_given_as_array_is_a_shape_error()
    {
        // xmp:CreateDate is a scalar (date) — a Seq is the wrong shape.
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns}><xmp:CreateDate><rdf:Seq>"
            + "<rdf:li>2020-01-01</rdf:li></rdf:Seq></xmp:CreateDate></rdf:Description>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    [Fact]
    public void LangAlt_type_given_as_simple_is_a_shape_error()
    {
        // dc:rights is lang alt; a plain simple value is the wrong shape.
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} dc:rights=\"all rights reserved\"/>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    [Fact]
    public void Bag_type_given_as_seq_is_a_shape_error()
    {
        // dc:creator is "seq propername" (ordered); a Bag is the wrong shape.
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns}><dc:creator><rdf:Bag>"
            + "<rdf:li>Jane</rdf:li></rdf:Bag></dc:creator></rdf:Description>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    // ── Rule B: date content ─────────────────────────────────────────────────────

    [Fact]
    public void Valid_iso8601_date_passes()
    {
        Assert.Empty(TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} xmp:CreateDate=\"2016-02-01T13:19:21+01:00\"/>"));
    }

    [Fact]
    public void Garbage_date_is_flagged()
    {
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} xmp:CreateDate=\"last Tuesday\"/>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    // ── Rule B: structured value types ───────────────────────────────────────────

    [Fact]
    public void Struct_typed_property_given_as_simple_is_flagged()
    {
        // xmpMM:DerivedFrom is a resourceref STRUCT — a bare simple value is not a struct.
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} xmpMM:DerivedFrom=\"nonsense\"/>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    [Fact]
    public void Valid_struct_passes()
    {
        // A well-formed resourceref struct (parseType=Resource) with known, correctly-typed fields.
        Assert.Empty(TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns}><xmpMM:DerivedFrom rdf:parseType=\"Resource\">"
            + "<stRef:instanceID>uuid:1234</stRef:instanceID>"
            + "<stRef:documentID>uuid:5678</stRef:documentID>"
            + "</xmpMM:DerivedFrom></rdf:Description>"));
    }

    [Fact]
    public void Struct_with_unknown_field_is_flagged()
    {
        // A field name not defined for resourceref makes the struct invalid.
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns}><xmpMM:DerivedFrom rdf:parseType=\"Resource\">"
            + "<stRef:instanceID>uuid:1234</stRef:instanceID>"
            + "<stRef:bogusField>x</stRef:bogusField>"
            + "</xmpMM:DerivedFrom></rdf:Description>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    [Fact]
    public void Valid_array_of_structs_passes()
    {
        // xmpMM:History is "seq resourceevent" — an ordered array of well-formed resourceevent structs.
        Assert.Empty(TypeFindings(
            "<rdf:Description rdf:about=\"\" xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\" "
            + "xmlns:stEvt=\"http://ns.adobe.com/xap/1.0/sType/ResourceEvent#\">"
            + "<xmpMM:History><rdf:Seq><rdf:li rdf:parseType=\"Resource\">"
            + "<stEvt:action>created</stEvt:action>"
            + "<stEvt:when>2016-02-01T13:19:21+01:00</stEvt:when>"
            + "</rdf:li></rdf:Seq></xmpMM:History></rdf:Description>"));
    }

    [Fact]
    public void Array_of_structs_given_as_wrong_array_kind_is_flagged()
    {
        // xmpMM:History is "seq …" (ordered); a Bag is the wrong array shape.
        Finding[] f = TypeFindings(
            "<rdf:Description rdf:about=\"\" xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\" "
            + "xmlns:stEvt=\"http://ns.adobe.com/xap/1.0/sType/ResourceEvent#\">"
            + "<xmpMM:History><rdf:Bag><rdf:li rdf:parseType=\"Resource\">"
            + "<stEvt:action>created</stEvt:action></rdf:li></rdf:Bag></xmpMM:History></rdf:Description>");
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    // ── Rule B: extension-declared property types ────────────────────────────────

    [Fact]
    public void Extension_declared_property_value_is_type_checked_and_passes()
    {
        // custom:num declared as Integer; "5" is a valid integer.
        Assert.Empty(TypeFindings(
            WithExtensionSchema("num", "Integer", "<custom:num>5</custom:num>")));
    }

    [Fact]
    public void Extension_declared_property_with_bad_value_is_flagged()
    {
        // custom:num declared as Integer; "abc" is not.
        Finding[] f = TypeFindings(
            WithExtensionSchema("num", "Integer", "<custom:num>abc</custom:num>"));
        Assert.Equal("pdfa-xmp-property-type", Assert.Single(f).RuleId);
    }

    // ── Combined clean case ──────────────────────────────────────────────────────

    [Fact]
    public void Correct_shapes_and_values_pass_cleanly()
    {
        // dc:creator seq, dc:title lang-alt, dc:format mimetype, photoshop:Urgency integer — all valid.
        Finding[] f = TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} dc:format=\"application/pdf\" photoshop:Urgency=\"5\">"
            + "<dc:creator><rdf:Seq><rdf:li>Jane</rdf:li></rdf:Seq></dc:creator>"
            + "<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Doc</rdf:li></rdf:Alt></dc:title>"
            + "</rdf:Description>");
        Assert.Empty(f);
    }
}
