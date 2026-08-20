using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// ISO 19005-2 6.6.2.1 tests 2 and 3 — the XMP packet HEADER must carry neither <c>bytes</c> nor
/// <c>encoding</c>. Corpus fixtures "veraPDF test suite 6-6-2-1-t01-fail-b.pdf" (<c>bytes="870"</c>)
/// and "…-fail-c.pdf" (<c>encoding="UTF-8"</c>) are the end-to-end proof; these pin the same behaviour
/// without the corpus, which is a sibling checkout the <c>Category=Parity</c> tests SKIP when absent.
///
/// <para>Note the fixture FILENAMES say t01 while veraPDF records the failing rules as tests 2 and 3 —
/// a corpus file is named for the clause's test SECTION, not the rule that fires on it. Read
/// <c>verapdf-verdicts.json</c>, never the filename.</para>
/// </summary>
public class XmpPacketHeaderRuleTests
{
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

    private static byte[] Packet(string header) => Encoding.UTF8.GetBytes(
        header
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
        + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\"/></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");

    private static Finding[] Check(byte[] xmp) =>
        new XmpPacketHeaderRule().Check(new ConformanceContext(DocWithXmp(xmp), ConformanceProfile.PdfA2b))
            .ToArray();

    private const string CleanHeader = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>";

    [Fact]
    public void A_clean_packet_header_is_accepted()
    {
        Assert.Empty(Check(Packet(CleanHeader)));
    }

    [Fact]
    public void The_bytes_attribute_is_flagged()
    {
        // Exactly the fail-b fixture's shape, attribute order included: it puts bytes BEFORE begin.
        Finding f = Assert.Single(Check(Packet(
            "<?xpacket bytes=\"870\" begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>")));

        Assert.Equal("xmp-packet-header", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("bytes", f.Message, System.StringComparison.Ordinal);
        Assert.Equal(2, f.ObjectNumber);   // names the /Metadata stream, not the catalog
    }

    [Fact]
    public void The_encoding_attribute_is_flagged()
    {
        Finding f = Assert.Single(Check(Packet(
            "<?xpacket encoding=\"UTF-8\" begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>")));

        Assert.Contains("encoding", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Both_attributes_are_reported_separately()
    {
        // One finding each: a caller fixing them needs to know both are present, and collapsing them
        // into one sentence would under-report a packet carrying two distinct violations.
        Finding[] findings = Check(Packet(
            "<?xpacket begin=\"\" bytes=\"870\" encoding=\"UTF-8\" id=\"W5M0Mp\"?>"));

        Assert.Equal(2, findings.Length);
    }

    [Fact]
    public void Single_quoted_attributes_are_read()
    {
        // The real fixtures single-quote begin; an attribute scanner that only understood double
        // quotes would desynchronise on the first one and miss everything after it.
        Assert.Single(Check(Packet("<?xpacket bytes='870' begin='' id='W5M0Mp'?>")));
    }

    [Fact]
    public void A_forbidden_name_inside_an_attribute_VALUE_is_not_a_finding()
    {
        // The reason values are skipped wholesale rather than searched. /begin/ carries an arbitrary
        // BOM string and /id/ is opaque, so scanning the raw header text for "bytes=" would be
        // reading data as structure — and this file is CONFORMING.
        Assert.Empty(Check(Packet("<?xpacket begin=\"\" id=\"bytes=870 encoding=UTF-8\"?>")));
    }

    [Fact]
    public void The_trailer_instruction_is_not_scanned()
    {
        // <?xpacket end='w'?> is a different instruction with its own legal attribute, and the clause
        // names the header. A scanner that walked every xpacket PI would flag conforming files.
        Assert.Empty(Check(Encoding.UTF8.GetBytes(
            CleanHeader + "<x:xmpmeta/><?xpacket end=\"w\" bytes=\"870\"?>")));
    }

    [Fact]
    public void A_document_with_no_metadata_stream_reports_nothing()
    {
        // MetadataPresentRule owns the absent case; reporting here too would double-count it.
        var doc = new PdfDocument();
        var catalog = new PdfDictionary { [new PdfName("Type")] = new PdfName("Catalog") };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);

        Assert.Empty(new XmpPacketHeaderRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void Bytes_that_are_not_an_xmp_packet_report_nothing()
    {
        // Tolerance, matching XmpPacket.Parse's own contract: a /Metadata stream holding something
        // else is a different defect and not this rule's to invent.
        Assert.Empty(Check(Encoding.UTF8.GetBytes("not xmp at all")));
    }

    [Fact]
    public void An_unterminated_header_reports_nothing()
    {
        // Truncated packet: there is no header to judge, and guessing where it ended would risk
        // reporting an attribute that is really body text.
        Assert.Empty(Check(Encoding.UTF8.GetBytes("<?xpacket bytes=\"870\" begin=\"\"")));
    }
}
