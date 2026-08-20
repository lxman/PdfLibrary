using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A-2/3 clause 6.1.4 test 2 (<see cref="XrefTableSpacingRule"/>): the <c>xref</c> keyword and the
/// cross-reference subsection header shall be separated by a SINGLE EOL marker. Calibrated against
/// veraPDF's PDFA-2 rule (object <c>CosXRef</c>, <c>xrefEOLMarkersComplyPDFA</c>).
///
/// <para>Corpus fixtures "veraPDF test suite 6-1-4-t01-fail-a.pdf" (<c>xref SP LF</c>) and "…-fail-b.pdf"
/// (<c>xref LF LF</c>, plus a SECOND well-formed table) are the end-to-end proof; these pin the byte
/// logic without the corpus, which the <c>Category=Parity</c> tests skip when it is absent.</para>
///
/// <para>The rule reads only <see cref="ConformanceContext.SourceBytes"/> and never the object graph,
/// so these drive it over raw byte strings rather than assembling loadable PDFs — the shapes under
/// test are malformed on purpose, and several could not survive a parse.</para>
/// </summary>
public class XrefTableSpacingRuleTests
{
    private static Finding[] Run(string raw) => Run(Encoding.ASCII.GetBytes(raw));

    private static Finding[] Run(byte[]? sourceBytes)
    {
        var doc = new PdfDocument();
        var catalog = new PdfDictionary { [new PdfName("Type")] = new PdfName("Catalog") };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);

        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b, sourceBytes);
        return [.. new XrefTableSpacingRule().Check(ctx)];
    }

    [Theory]
    [InlineData("xref\n0 15\n0000000000 65535 f\r\n")]      // LF
    [InlineData("xref\r\n0 15\r\n0000000000 65535 f\r\n")]  // CRLF
    [InlineData("xref\r0 15\r0000000000 65535 f\r\n")]      // lone CR
    public void A_single_EOL_marker_is_accepted(string raw)
    {
        Assert.Empty(Run(raw));
    }

    [Fact]
    public void A_space_before_the_EOL_is_flagged()
    {
        // Exactly corpus fixture 6-1-4-t01-fail-a.pdf: "xref \n0 15\n".
        Finding f = Assert.Single(Run("xref \n0 15\n0000000000 65535 f\r\n"));

        Assert.Equal("xref-spacing", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("SPACE LF", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Two_EOL_markers_are_flagged()
    {
        // Exactly corpus fixture 6-1-4-t01-fail-b.pdf: "xref\n\n0 15\n".
        Finding f = Assert.Single(Run("xref\n\n0 15\n0000000000 65535 f\r\n"));
        Assert.Contains("LF LF", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Every_table_is_checked_not_only_the_first()
    {
        // fail-b's real shape: a malformed original table and a well-formed incremental one. Stopping
        // at the first match would still have found this file's violation by luck of ordering, so the
        // reverse order is what actually pins the behaviour.
        Finding f = Assert.Single(Run(
            "xref\n0 2\n0000000000 65535 f\r\n"        // well-formed, first
            + "trailer<</Size 2>>\n"
            + "xref\n\n0 15\n0000000000 65535 f\r\n")); // malformed, second

        Assert.Contains("LF LF", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_word_xref_in_document_text_is_not_a_table()
    {
        // Both corpus fixtures embed this sentence in their own document information, so a keyword
        // scan alone reports a violation on a file whose actual table is fine. What disqualifies it is
        // that no subsection header follows.
        Assert.Empty(Run(
            "(The xref keyword and the following cross reference subsection header) Tj\n"
            + "xref\n0 2\n0000000000 65535 f\r\n"));
    }

    [Fact]
    public void The_startxref_keyword_is_not_a_table()
    {
        // The separator here is DELIBERATELY malformed ("SPACE LF"). An earlier version of this test
        // used "startxref\n0 15\n", which passed with the 'start' guard deleted — a single LF is
        // conforming, so the guard was never reached and the test proved nothing. Verified by probe:
        // with IsStartXref removed, this input yields a finding and this test fails.
        Assert.Empty(Run("startxref \n0 15\n"));
    }

    [Fact]
    public void A_keyword_with_no_subsection_header_is_not_a_table()
    {
        Assert.Empty(Run("xref\nfoo bar\n"));
    }

    [Fact]
    public void A_keyword_at_end_of_file_does_not_throw()
    {
        Assert.Empty(Run("trailer\nxref"));
        Assert.Empty(Run("xref\n0 "));
    }

    [Fact]
    public void An_in_memory_document_reports_nothing()
    {
        // Byte-level rule with no bytes: it cannot run, and must not guess.
        Assert.Empty(Run((byte[]?)null));
    }

    [Fact]
    public void A_cross_reference_stream_is_out_of_scope()
    {
        // A cross-reference STREAM carries no xref keyword and no subsection header, so the clause has
        // nothing to constrain. Nothing here should match.
        Assert.Empty(Run("15 0 obj\n<</Type/XRef/Size 16/W[1 2 1]>>\nstream\n\x01\x00\x10\x00\nendstream\nendobj\n"));
    }
}
