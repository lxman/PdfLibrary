using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 3 of the preflight: metadata rules. Covers <c>metadata</c> (an embedded /Metadata stream must
/// exist) and <c>pdfa-id</c> (the XMP must carry a pdfaid:part / pdfaid:conformance matching the target
/// profile).
/// </summary>
public class PreflightSlice3Tests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>An in-memory document whose catalog points at a /Metadata stream holding <paramref name="xmp"/>.</summary>
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

    /// <summary>An in-memory document with a catalog but no /Metadata key.</summary>
    private static PdfDocument DocWithCatalogNoMetadata()
    {
        var doc = new PdfDocument();
        var catalog = new PdfDictionary();
        catalog[new PdfName("Type")] = new PdfName("Catalog");
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>XMP bytes carrying pdfaid identification in attribute form.</summary>
    private static byte[] Xmp(string part, string conformance) => Encoding.UTF8.GetBytes(
        $"<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
        $"<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        $"<rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" " +
        $"pdfaid:part=\"{part}\" pdfaid:conformance=\"{conformance}\"/></rdf:RDF></x:xmpmeta>" +
        $"<?xpacket end=\"w\"?>");

    /// <summary>XMP bytes with a well-formed packet but no pdfaid schema at all.</summary>
    private static byte[] XmpWithoutPdfaId() => Encoding.UTF8.GetBytes(
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
        "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
        "dc:format=\"application/pdf\"/></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");

    private static ConformanceContext Ctx(PdfDocument doc, ConformanceProfile profile) => new(doc, profile);

    // ── metadata-present ─────────────────────────────────────────────────────

    [Fact]
    public void Metadata_present_when_stream_exists_passes()
    {
        PdfDocument doc = DocWithXmp(Xmp("2", "B"));
        Assert.Empty(new MetadataPresentRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void Metadata_absent_with_catalog_is_error()
    {
        PdfDocument doc = DocWithCatalogNoMetadata();
        Finding finding = Assert.Single(new MetadataPresentRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal("metadata", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void Metadata_absent_with_no_catalog_is_error()
    {
        Finding finding = Assert.Single(
            new MetadataPresentRule().Check(Ctx(new PdfDocument(), ConformanceProfile.PdfA2b)));

        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    // ── pdfa-id: passing cases ───────────────────────────────────────────────

    [Theory]
    [InlineData(ConformanceProfile.PdfA2b, "2", "B")]
    [InlineData(ConformanceProfile.PdfA2u, "2", "U")]
    [InlineData(ConformanceProfile.PdfA3b, "3", "B")]
    public void PdfaId_passes_for_matching_identification(ConformanceProfile profile, string part, string conf)
    {
        PdfDocument doc = DocWithXmp(Xmp(part, conf));
        Assert.Empty(new PdfaIdentificationRule().Check(Ctx(doc, profile)));
    }

    // ── pdfa-id: failing cases ───────────────────────────────────────────────

    [Fact]
    public void PdfaId_wrong_part_is_error()
    {
        // Targeting 2b but the packet declares part 3.
        PdfDocument doc = DocWithXmp(Xmp("3", "B"));
        Finding finding = Assert.Single(new PdfaIdentificationRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal("pdfa-id", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Contains("part", finding.Message);
    }

    [Fact]
    public void PdfaId_conformance_B_invalid_for_u_profile_is_error()
    {
        // 2u accepts only U or A; conformance B is not valid.
        PdfDocument doc = DocWithXmp(Xmp("2", "B"));
        Finding finding = Assert.Single(new PdfaIdentificationRule().Check(Ctx(doc, ConformanceProfile.PdfA2u)));

        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Contains("conformance", finding.Message);
    }

    [Fact]
    public void PdfaId_missing_pdfaid_is_error()
    {
        PdfDocument doc = DocWithXmp(XmpWithoutPdfaId());
        Finding finding = Assert.Single(new PdfaIdentificationRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Contains("pdfaid", finding.Message);
    }

    [Fact]
    public void PdfaId_no_metadata_stream_is_error()
    {
        Finding finding = Assert.Single(
            new PdfaIdentificationRule().Check(Ctx(new PdfDocument(), ConformanceProfile.PdfA2b)));

        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    // ── sanity: the attribute-form parser reads pdfaid ───────────────────────

    [Fact]
    public void Xmp_helper_round_trips_pdfaid_through_parser()
    {
        PdfLibrary.Metadata.XmpPacket packet = PdfLibrary.Metadata.XmpPacket.Parse(Xmp("2", "B"));
        Assert.Equal("2", packet.Get("http://www.aiim.org/pdfa/ns/id/", "part")?.Value);
        Assert.Equal("B", packet.Get("http://www.aiim.org/pdfa/ns/id/", "conformance")?.Value);
    }
}
