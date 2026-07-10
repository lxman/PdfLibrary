using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 17 — XMP extension-schema container structure (ISO 19005-2, 6.6.2.3.3,
/// <see cref="XmpExtensionSchemaStructureRule"/>). Each <c>pdfaExtension:schemas</c> entry and its nested
/// property / value-type / field descriptions must carry the mandatory fields and the conventional
/// namespace prefix. These CI-safe synthetic packets pin every required-field and wrong-prefix branch plus
/// the zero-false-positive pass cases (optional property/valueType/field, a fully valid schema); the
/// corpus-driven parity harness measures real detection.
/// </summary>
public class PreflightSlice17Tests
{
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
        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Metadata")] = new PdfIndirectReference(2, 0),
        };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    private static Finding[] Findings(string descriptions) =>
        new XmpExtensionSchemaStructureRule().Check(Ctx(DocWithXmp(Xmp(descriptions)))).ToArray();

    // Wraps one schema body (the <rdf:li> content) in a pdfaExtension:schemas Bag with every extension
    // namespace bound to its conventional prefix. Callers pass schema-description field markup.
    private static string Schemas(string schemaBody) =>
        "<rdf:Description rdf:about=\"\" "
        + "xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\" "
        + "xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\" "
        + "xmlns:pdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\" "
        + "xmlns:pdfaType=\"http://www.aiim.org/pdfa/ns/type#\" "
        + "xmlns:pdfaField=\"http://www.aiim.org/pdfa/ns/field#\">"
        + "<pdfaExtension:schemas><rdf:Bag><rdf:li rdf:parseType=\"Resource\">"
        + schemaBody
        + "</rdf:li></rdf:Bag></pdfaExtension:schemas></rdf:Description>";

    private const string SchemaNamespaceUri = "<pdfaSchema:namespaceURI>http://example.com/custom/</pdfaSchema:namespaceURI>";
    private const string SchemaPrefix = "<pdfaSchema:prefix>custom</pdfaSchema:prefix>";
    private const string SchemaSchema = "<pdfaSchema:schema>Custom schema</pdfaSchema:schema>";
    private const string SchemaRequired = SchemaNamespaceUri + SchemaPrefix + SchemaSchema;

    private static string PropertyBag(string body) =>
        "<pdfaSchema:property><rdf:Seq><rdf:li rdf:parseType=\"Resource\">" + body + "</rdf:li></rdf:Seq></pdfaSchema:property>";

    private static string ValueTypeBag(string body) =>
        "<pdfaSchema:valueType><rdf:Seq><rdf:li rdf:parseType=\"Resource\">" + body + "</rdf:li></rdf:Seq></pdfaSchema:valueType>";

    private static string FieldBag(string body) =>
        "<pdfaType:field><rdf:Seq><rdf:li rdf:parseType=\"Resource\">" + body + "</rdf:li></rdf:Seq></pdfaType:field>";

    private const string PropAll =
        "<pdfaProperty:name>n</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType>"
        + "<pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>d</pdfaProperty:description>";

    private const string TypeAll =
        "<pdfaType:type>MyType</pdfaType:type><pdfaType:namespaceURI>http://example.com/t/</pdfaType:namespaceURI>"
        + "<pdfaType:prefix>mt</pdfaType:prefix><pdfaType:description>d</pdfaType:description>";

    private const string FieldAll =
        "<pdfaField:name>fn</pdfaField:name><pdfaField:valueType>Text</pdfaField:valueType>"
        + "<pdfaField:description>d</pdfaField:description>";

    // ── pass cases (must never fire) ──────────────────────────────────────────────────────────────

    [Fact]
    public void No_extension_schema_present_is_clean() =>
        Assert.Empty(Findings("<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>t</dc:title></rdf:Description>"));

    [Fact]
    public void Minimal_valid_schema_without_optional_property_or_valueType_passes() =>
        Assert.Empty(Findings(Schemas(SchemaRequired)));

    [Fact]
    public void Fully_valid_schema_with_property_valueType_and_field_passes() =>
        Assert.Empty(Findings(Schemas(SchemaRequired + PropertyBag(PropAll) + ValueTypeBag(TypeAll + FieldBag(FieldAll)))));

    [Fact]
    public void Value_type_without_the_optional_field_array_passes() =>
        Assert.Empty(Findings(Schemas(SchemaRequired + ValueTypeBag(TypeAll))));

    // ── schema level ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Schema_missing_namespaceURI_fires() =>
        Assert.NotEmpty(Findings(Schemas(SchemaPrefix + SchemaSchema)));

    [Fact]
    public void Schema_missing_prefix_fires() =>
        Assert.NotEmpty(Findings(Schemas(SchemaNamespaceUri + SchemaSchema)));

    [Fact]
    public void Schema_missing_schema_fires() =>
        Assert.NotEmpty(Findings(Schemas(SchemaNamespaceUri + SchemaPrefix)));

    [Fact]
    public void Schema_with_wrong_prefix_fires()
    {
        // The schema# namespace bound to "nonpdfaSchema" instead of "pdfaSchema".
        string body =
            "<rdf:Description rdf:about=\"\" xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\" "
            + "xmlns:nonpdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\">"
            + "<pdfaExtension:schemas><rdf:Bag><rdf:li rdf:parseType=\"Resource\">"
            + "<nonpdfaSchema:namespaceURI>http://example.com/custom/</nonpdfaSchema:namespaceURI>"
            + "<nonpdfaSchema:prefix>custom</nonpdfaSchema:prefix>"
            + "<nonpdfaSchema:schema>Custom schema</nonpdfaSchema:schema>"
            + "</rdf:li></rdf:Bag></pdfaExtension:schemas></rdf:Description>";
        Assert.NotEmpty(Findings(body));
    }

    // ── property level ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<pdfaProperty:valueType>Text</pdfaProperty:valueType><pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>d</pdfaProperty:description>")] // no name
    [InlineData("<pdfaProperty:name>n</pdfaProperty:name><pdfaProperty:category>external</pdfaProperty:category><pdfaProperty:description>d</pdfaProperty:description>")] // no valueType
    [InlineData("<pdfaProperty:name>n</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType><pdfaProperty:description>d</pdfaProperty:description>")] // no category
    [InlineData("<pdfaProperty:name>n</pdfaProperty:name><pdfaProperty:valueType>Text</pdfaProperty:valueType><pdfaProperty:category>external</pdfaProperty:category>")] // no description
    public void Property_missing_a_required_field_fires(string propertyBody) =>
        Assert.NotEmpty(Findings(Schemas(SchemaRequired + PropertyBag(propertyBody))));

    [Fact]
    public void Property_with_wrong_prefix_fires()
    {
        string body =
            "<rdf:Description rdf:about=\"\" xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\" "
            + "xmlns:pdfaSchema=\"http://www.aiim.org/pdfa/ns/schema#\" "
            + "xmlns:nonpdfaProperty=\"http://www.aiim.org/pdfa/ns/property#\">"
            + "<pdfaExtension:schemas><rdf:Bag><rdf:li rdf:parseType=\"Resource\">" + SchemaRequired
            + "<pdfaSchema:property><rdf:Seq><rdf:li rdf:parseType=\"Resource\">"
            + "<nonpdfaProperty:name>n</nonpdfaProperty:name><nonpdfaProperty:valueType>Text</nonpdfaProperty:valueType>"
            + "<nonpdfaProperty:category>external</nonpdfaProperty:category><nonpdfaProperty:description>d</nonpdfaProperty:description>"
            + "</rdf:li></rdf:Seq></pdfaSchema:property>"
            + "</rdf:li></rdf:Bag></pdfaExtension:schemas></rdf:Description>";
        Assert.NotEmpty(Findings(body));
    }

    // ── value-type level ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<pdfaType:namespaceURI>http://example.com/t/</pdfaType:namespaceURI><pdfaType:prefix>mt</pdfaType:prefix><pdfaType:description>d</pdfaType:description>")] // no type
    [InlineData("<pdfaType:type>MyType</pdfaType:type><pdfaType:prefix>mt</pdfaType:prefix><pdfaType:description>d</pdfaType:description>")] // no namespaceURI
    [InlineData("<pdfaType:type>MyType</pdfaType:type><pdfaType:namespaceURI>http://example.com/t/</pdfaType:namespaceURI><pdfaType:description>d</pdfaType:description>")] // no prefix
    [InlineData("<pdfaType:type>MyType</pdfaType:type><pdfaType:namespaceURI>http://example.com/t/</pdfaType:namespaceURI><pdfaType:prefix>mt</pdfaType:prefix>")] // no description
    public void Value_type_missing_a_required_field_fires(string typeBody) =>
        Assert.NotEmpty(Findings(Schemas(SchemaRequired + ValueTypeBag(typeBody))));

    // ── field level ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<pdfaField:valueType>Text</pdfaField:valueType><pdfaField:description>d</pdfaField:description>")] // no name
    [InlineData("<pdfaField:name>fn</pdfaField:name><pdfaField:description>d</pdfaField:description>")] // no valueType
    [InlineData("<pdfaField:name>fn</pdfaField:name><pdfaField:valueType>Text</pdfaField:valueType>")] // no description
    public void Field_missing_a_required_field_fires(string fieldBody) =>
        Assert.NotEmpty(Findings(Schemas(SchemaRequired + ValueTypeBag(TypeAll + FieldBag(fieldBody)))));

    [Fact]
    public void Rule_targets_all_pdfa_profiles_only() =>
        Assert.Equal(ConformanceProfile.AllPdfA, new XmpExtensionSchemaStructureRule().AppliesToProfiles);
}
