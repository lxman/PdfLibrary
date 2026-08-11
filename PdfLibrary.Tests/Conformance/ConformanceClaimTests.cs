using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Tests for <see cref="ConformanceClaim.Read"/>: the reader `pellucid scan` uses to determine
/// the profile a document claims (as opposed to the profile it is being checked against). Mirrors
/// the XMP identification schemas already parsed by <c>PdfaIdentificationRule</c>,
/// <c>PdfxVersionRule</c>, and <c>UaIdentificationRule</c> — same namespace URIs, same value rules —
/// but exposed as a standalone, non-throwing classifier with no target profile in play.
/// </summary>
public class ConformanceClaimTests
{
    private const string PdfaIdNs = "http://www.aiim.org/pdfa/ns/id/";
    private const string PdfxIdNs = "http://www.npes.org/pdfx/ns/id/";
    private const string PdfUaIdNs = "http://www.aiim.org/pdfua/ns/id/";

    // ── fixture builders ─────────────────────────────────────────────────────

    private static MemoryStream OnePagePdf()
    {
        var ms = new MemoryStream();
        new PdfDocumentBuilder().AddPage(_ => { }).Save(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Builds a one-page document whose /Metadata stream is the given raw bytes (or, when
    /// <paramref name="xmpBytes"/> is null, a document with NO /Metadata stream at all).</summary>
    private static PdfDocument DocWithXmp(byte[]? xmpBytes)
    {
        using MemoryStream src = OnePagePdf();
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true);
        if (xmpBytes is not null)
            editor.Metadata.SetRawXmp(xmpBytes);
        var outMs = new MemoryStream();
        editor.Save(outMs);
        return PdfDocument.Load(new MemoryStream(outMs.ToArray()), "");
    }

    /// <summary>Builds a minimal, well-formed XMP packet declaring the given (prefix, namespace,
    /// localName, value) properties in one rdf:Description. Mirrors the template in the task brief.</summary>
    private static byte[] BuildXmp(params (string Prefix, string Ns, string Local, string Value)[] props)
    {
        var nsDecls = new StringBuilder();
        var seenPrefixes = new HashSet<string>();
        foreach ((string prefix, string ns, _, _) in props)
        {
            if (seenPrefixes.Add(prefix))
                nsDecls.Append($"\n          xmlns:{prefix}=\"{ns}\"");
        }

        var body = new StringBuilder();
        foreach ((string prefix, _, string local, string value) in props)
            body.Append($"\n        <{prefix}:{local}>{value}</{prefix}:{local}>");

        string xml =
            "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
            "  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
            "    <rdf:Description rdf:about=\"\"" + nsDecls + ">" + body + "\n" +
            "    </rdf:Description>\n" +
            "  </rdf:RDF>\n" +
            "</x:xmpmeta>\n" +
            "<?xpacket end=\"w\"?>";
        return Encoding.UTF8.GetBytes(xml);
    }

    private static byte[] PdfaXmp(string part, string conformance) =>
        BuildXmp(("pdfaid", PdfaIdNs, "part", part), ("pdfaid", PdfaIdNs, "conformance", conformance));

    private static byte[] PdfxXmp(string version) =>
        BuildXmp(("pdfxid", PdfxIdNs, "GTS_PDFXVersion", version));

    private static byte[] PdfUaXmp(string part) =>
        BuildXmp(("pdfuaid", PdfUaIdNs, "part", part));

    // ── 1. pdfaid part/conformance → profile mapping ────────────────────────

    [Theory]
    [InlineData("2", "B", ConformanceProfile.PdfA2b)]
    [InlineData("2", "U", ConformanceProfile.PdfA2u)]
    [InlineData("2", "A", ConformanceProfile.PdfA2u)]
    [InlineData("3", "B", ConformanceProfile.PdfA3b)]
    [InlineData("3", "U", ConformanceProfile.PdfA3b)]
    [InlineData("3", "A", ConformanceProfile.PdfA3b)]
    public void Read_maps_pdfaid_part_and_conformance_to_the_expected_profile(
        string part, string conformance, ConformanceProfile expected)
    {
        using PdfDocument doc = DocWithXmp(PdfaXmp(part, conformance));
        Assert.Equal(expected, ConformanceClaim.Read(doc));
    }

    // ── 2. pdfxid GTS_PDFXVersion ────────────────────────────────────────────

    [Theory]
    [InlineData("PDF/X-4")]
    [InlineData("PDF/X-4p")]
    public void Read_maps_pdfx4_versions_to_PdfX4(string version)
    {
        using PdfDocument doc = DocWithXmp(PdfxXmp(version));
        Assert.Equal(ConformanceProfile.PdfX4, ConformanceClaim.Read(doc));
    }

    // ── 3. pdfuaid part ───────────────────────────────────────────────────────

    [Fact]
    public void Read_maps_pdfuaid_part_1_to_PdfUA1()
    {
        using PdfDocument doc = DocWithXmp(PdfUaXmp("1"));
        Assert.Equal(ConformanceProfile.PdfUA1, ConformanceClaim.Read(doc));
    }

    // ── 4. precedence PDF/A > PDF/X-4 > PDF/UA-1 ────────────────────────────

    [Fact]
    public void Read_prefers_pdfa_over_pdfua_when_both_claimed()
    {
        byte[] xmp = BuildXmp(
            ("pdfaid", PdfaIdNs, "part", "2"),
            ("pdfaid", PdfaIdNs, "conformance", "B"),
            ("pdfuaid", PdfUaIdNs, "part", "1"));
        using PdfDocument doc = DocWithXmp(xmp);
        Assert.Equal(ConformanceProfile.PdfA2b, ConformanceClaim.Read(doc));
    }

    [Fact]
    public void Read_prefers_pdfx4_over_pdfua_when_both_claimed()
    {
        byte[] xmp = BuildXmp(
            ("pdfxid", PdfxIdNs, "GTS_PDFXVersion", "PDF/X-4"),
            ("pdfuaid", PdfUaIdNs, "part", "1"));
        using PdfDocument doc = DocWithXmp(xmp);
        Assert.Equal(ConformanceProfile.PdfX4, ConformanceClaim.Read(doc));
    }

    // ── 5. no claim / no schema / malformed XML → null, never throws ───────

    [Fact]
    public void Read_returns_null_when_there_is_no_metadata_at_all()
    {
        using PdfDocument doc = DocWithXmp(null);
        Assert.Null(ConformanceClaim.Read(doc));
    }

    [Fact]
    public void Read_returns_null_when_xmp_has_none_of_the_three_schemas()
    {
        byte[] xmp = BuildXmp(("dc", "http://purl.org/dc/elements/1.1/", "title", "Untitled"));
        using PdfDocument doc = DocWithXmp(xmp);
        Assert.Null(ConformanceClaim.Read(doc));
    }

    [Fact]
    public void Read_returns_null_and_does_not_throw_on_malformed_xml()
    {
        byte[] garbage = Encoding.UTF8.GetBytes("this is not xml at all { garbage )");
        using PdfDocument doc = DocWithXmp(garbage);
        Exception? thrown = Record.Exception(() => ConformanceClaim.Read(doc));
        Assert.Null(thrown);
        Assert.Null(ConformanceClaim.Read(doc));
    }

    // ── 6. unsupported pdfaid part alone → null ─────────────────────────────

    [Fact]
    public void Read_returns_null_for_an_unsupported_pdfa_part()
    {
        using PdfDocument doc = DocWithXmp(PdfaXmp("9", "B"));
        Assert.Null(ConformanceClaim.Read(doc));
    }

    // ── xmpUnreadable overload ───────────────────────────────────────────────

    [Fact]
    public void Read_reports_xmpUnreadable_true_when_metadata_exists_but_is_not_parseable_xml()
    {
        byte[] garbage = Encoding.UTF8.GetBytes("this is not xml at all { garbage )");
        using PdfDocument doc = DocWithXmp(garbage);
        ConformanceProfile? result = ConformanceClaim.Read(doc, out bool xmpUnreadable);
        Assert.Null(result);
        Assert.True(xmpUnreadable);
    }

    [Fact]
    public void Read_reports_xmpUnreadable_false_when_there_is_no_metadata_at_all()
    {
        using PdfDocument doc = DocWithXmp(null);
        ConformanceProfile? result = ConformanceClaim.Read(doc, out bool xmpUnreadable);
        Assert.Null(result);
        Assert.False(xmpUnreadable);
    }

    [Fact]
    public void Read_reports_xmpUnreadable_false_when_metadata_parses_but_claims_nothing_supported()
    {
        byte[] xmp = BuildXmp(("dc", "http://purl.org/dc/elements/1.1/", "title", "Untitled"));
        using PdfDocument doc = DocWithXmp(xmp);
        ConformanceProfile? result = ConformanceClaim.Read(doc, out bool xmpUnreadable);
        Assert.Null(result);
        Assert.False(xmpUnreadable);
    }
}
