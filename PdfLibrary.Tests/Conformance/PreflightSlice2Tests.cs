using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 2 of the preflight: file-structure rules for the trailer and streams, plus the source-bytes
/// plumbing. Covers the refined <c>file-id</c> rule, <c>post-eof</c>, the stream filter whitelist, and
/// the external-file-key prohibition.
/// </summary>
public class PreflightSlice2Tests
{
    private static PdfString Str(params byte[] bytes) => new(bytes);
    private static PdfName N(string v) => new(v);

    /// <summary>
    /// A genuinely conformant fixture (embedded font + valid PDF/A XMP) serialized to bytes, so it
    /// satisfies every rule added through the current slice while keeping a clean file structure.
    /// </summary>
    private static byte[] CleanBuilderBytes() => ConformanceFixtures.CleanConformantBytes();

    /// <summary>An in-memory document holding one indirect stream whose dictionary the caller configures.</summary>
    private static PdfDocument DocWithStream(Action<PdfDictionary> configure)
    {
        var doc = new PdfDocument();
        var dict = new PdfDictionary();
        configure(dict);
        doc.AddObject(1, 0, new PdfStream(dict, [0x00]));
        return doc;
    }

    // ── file-id refinement ──────────────────────────────────────────────────

    [Fact]
    public void FileId_single_nonempty_element_is_warning_not_error()
    {
        var doc = new PdfDocument();
        doc.Trailer.Id = new PdfArray(Str(0x01, 0x02)); // present & non-empty, but not two strings
        Finding finding = Assert.Single(new FileIdentifierRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal("file-id", finding.RuleId);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void FileId_two_empty_strings_is_error()
    {
        var doc = new PdfDocument();
        doc.Trailer.Id = new PdfArray(Str(), Str()); // two empty byte strings
        Finding finding = Assert.Single(new FileIdentifierRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void FileId_indirect_id_is_error()
    {
        var doc = new PdfDocument();
        // /ID present but an indirect reference (not a direct array) — Trailer.Id yields null.
        doc.Trailer.Dictionary[N("ID")] = new PdfIndirectReference(9, 0);
        Finding finding = Assert.Single(new FileIdentifierRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    // ── post-eof ────────────────────────────────────────────────────────────

    [Fact]
    public void PostEof_flags_data_after_eof()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("dummy\n%%EOF\ngarbage");
        Finding finding = Assert.Single(new PostEofDataRule().Check(
            new ConformanceContext(new PdfDocument(), ConformanceProfile.PdfA2b, bytes)));

        Assert.Equal("post-eof", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Theory]
    [InlineData("dummy\n%%EOF")]      // no trailing data
    [InlineData("dummy\n%%EOF\n")]    // single LF
    [InlineData("dummy\n%%EOF\r\n")]  // single CRLF
    public void PostEof_passes_with_optional_single_eol(string content)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(content);
        Assert.Empty(new PostEofDataRule().Check(
            new ConformanceContext(new PdfDocument(), ConformanceProfile.PdfA2b, bytes)));
    }

    [Fact]
    public void PostEof_flags_two_end_of_line_markers()
    {
        // Only a *single* optional EOL is permitted, so a second one is extra data.
        byte[] bytes = Encoding.ASCII.GetBytes("dummy\n%%EOF\n\n");
        Assert.Single(new PostEofDataRule().Check(
            new ConformanceContext(new PdfDocument(), ConformanceProfile.PdfA2b, bytes)));
    }

    [Fact]
    public void PostEof_skips_with_info_when_source_bytes_unavailable()
    {
        Finding finding = Assert.Single(new PostEofDataRule().Check(
            new ConformanceContext(new PdfDocument(), ConformanceProfile.PdfA2b)));

        Assert.Equal(FindingSeverity.Info, finding.Severity);
    }

    // ── stream filters ──────────────────────────────────────────────────────

    [Fact]
    public void StreamFilters_flags_lzw()
    {
        PdfDocument doc = DocWithStream(d => d[N("Filter")] = N("LZWDecode"));
        Finding finding = Assert.Single(new StreamFiltersRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal("stream-filters", finding.RuleId);
        Assert.Contains("LZWDecode", finding.Message);
    }

    [Fact]
    public void StreamFilters_allows_flate()
    {
        PdfDocument doc = DocWithStream(d => d[N("Filter")] = N("FlateDecode"));
        Assert.Empty(new StreamFiltersRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void StreamFilters_flags_lzw_inside_filter_array()
    {
        PdfDocument doc = DocWithStream(d => d[N("Filter")] = new PdfArray(N("ASCII85Decode"), N("LZWDecode")));
        Finding finding = Assert.Single(new StreamFiltersRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));

        Assert.Contains("LZWDecode", finding.Message);
    }

    [Fact]
    public void StreamFilters_allows_identity_crypt()
    {
        PdfDocument doc = DocWithStream(d =>
        {
            d[N("Filter")] = N("Crypt");
            var parms = new PdfDictionary();
            parms[N("Name")] = N("Identity");
            d[N("DecodeParms")] = parms;
        });
        Assert.Empty(new StreamFiltersRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void StreamFilters_flags_non_identity_crypt()
    {
        PdfDocument doc = DocWithStream(d =>
        {
            d[N("Filter")] = N("Crypt");
            var parms = new PdfDictionary();
            parms[N("Name")] = N("StdCF"); // a named crypt filter, not Identity
            d[N("DecodeParms")] = parms;
        });
        Finding finding = Assert.Single(new StreamFiltersRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));

        Assert.Contains("Crypt", finding.Message);
    }

    // ── external-file keys ──────────────────────────────────────────────────

    [Theory]
    [InlineData("F")]
    [InlineData("FFilter")]
    [InlineData("FDecodeParms")]
    public void StreamExternalFile_flags_each_external_key(string key)
    {
        PdfDocument doc = DocWithStream(d => d[N(key)] = Str(0x78));
        Finding finding = Assert.Single(new StreamExternalFileRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));

        Assert.Equal("stream-external-file", finding.RuleId);
        Assert.Contains("/" + key, finding.Message);
    }

    [Fact]
    public void StreamExternalFile_passes_for_plain_stream()
    {
        PdfDocument doc = DocWithStream(d => d[N("Filter")] = N("FlateDecode"));
        Assert.Empty(new StreamExternalFileRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }

    // ── Preflighter integration (source bytes) ──────────────────────────────

    [Fact]
    public void Check_bytes_clean_document_conforms()
    {
        PreflightResult result = Preflighter.Check(CleanBuilderBytes(), ConformanceProfile.PdfA2b);

        Assert.True(result.Conforms);
        Assert.Empty(result.Errors);
        // Source bytes were available and clean, so post-eof produces no finding at all.
        Assert.DoesNotContain(result.Findings, f => f.RuleId == "post-eof");
    }

    [Fact]
    public void Check_document_without_bytes_reports_posteof_info()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(CleanBuilderBytes()));
        PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfA2b);

        Assert.True(result.Conforms);
        Assert.Contains(result.Findings, f => f is { RuleId: "post-eof", Severity: FindingSeverity.Info });
    }
}
