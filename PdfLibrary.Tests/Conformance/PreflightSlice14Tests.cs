using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 14 — XMP predefined-schema property validation (ISO 19005-2, 6.6.2.3.1): the predefined-set
/// rule (<see cref="XmpPropertyPredefinedRule"/>) and the value-type rule
/// (<see cref="XmpPropertyTypeRule"/>). CI-safe synthetic fixtures — the corpus-driven parity harness
/// (Category=Parity) measures real detection; these guard the exact zero-false-positive behaviours.
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
        + "xmlns:photoshop=\"http://ns.adobe.com/photoshop/1.0/\"";

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
    public void Non_predefined_property_disengages_when_an_extension_schema_is_declared()
    {
        // Presence of the PDF/A extension-schema namespace anywhere makes the rule stand down
        // (the lossy parser cannot resolve extension-declared properties — conservative, zero-FP).
        Finding[] f = PredefinedFindings(
            "<rdf:Description rdf:about=\"\" xmlns:custom=\"http://example.com/custom/\" "
            + "xmlns:pdfaExtension=\"http://www.aiim.org/pdfa/ns/extension/\" "
            + "custom:madeUp=\"x\"/>");
        Assert.Empty(f);
    }

    [Fact]
    public void Structural_rdf_namespaces_are_never_flagged()
    {
        // An rdf:-namespaced attribute must never be reported as a stray property.
        Finding[] f = PredefinedFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} dc:format=\"application/pdf\"/>");
        Assert.Empty(f);
    }

    // ── Rule B: value type ───────────────────────────────────────────────────────

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
        // xmp:CreateDate is a scalar (date) — a Seq is the wrong shape (date CONTENT is not checked).
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

    [Fact]
    public void Struct_typed_property_is_skipped_even_when_malformed()
    {
        // xmpMM:DerivedFrom is a resourceref STRUCT — the lossy parser flattens it, so the rule must
        // skip it entirely (never a false positive), even given a bare simple value.
        Assert.Empty(TypeFindings(
            $"<rdf:Description rdf:about=\"\" {Ns} xmpMM:DerivedFrom=\"nonsense\"/>"));
    }

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
