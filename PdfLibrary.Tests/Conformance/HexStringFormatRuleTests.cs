using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A clause 6.1.6 (<see cref="HexStringFormatRule"/>), calibrated against veraPDF's PDFA-2B
/// rules: test 1 <c>(isHex != true) || hexCount % 2 == 0</c> and test 2
/// <c>(isHex != true) || containsOnlyHex == true</c>. Corpus fixtures
/// "veraPDF test suite 6-1-6-t01-fail-a.pdf" (<c>&lt;48455&gt; Tj</c>) and "…-t02-fail-a.pdf"
/// (<c>&lt;484!&gt; Tj</c>) are the end-to-end proof; these pin the logic without the corpus, which
/// the <c>Category=Parity</c> tests skip when it is absent.
/// </summary>
public class HexStringFormatRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static Finding[] Findings(PdfDocument doc) =>
        [.. new HexStringFormatRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))];

    /// <summary>A one-page document whose /Contents is <paramref name="pageContent"/>, parsed from
    /// BYTES so the lexer actually runs — an in-memory PdfString carries no written form.</summary>
    private static PdfDocument ContentDoc(string pageContent)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary(),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void An_odd_digit_count_in_page_content_is_flagged()
    {
        Finding f = Assert.Single(Findings(ContentDoc("BT <48455> Tj ET")));

        Assert.Equal("hex-string-format", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("6.1.6", f.Clause, System.StringComparison.Ordinal);
        Assert.Contains("odd", f.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_hex_character_in_page_content_is_flagged()
    {
        Finding f = Assert.Single(Findings(ContentDoc("BT <484!> Tj ET")));
        Assert.Contains("hexadecimal", f.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("BT <484550> Tj ET")]
    [InlineData("BT <48 45 50> Tj ET")]   // interior white space is legal and not counted
    [InlineData("BT <> Tj ET")]           // empty is even
    [InlineData("BT (HEP) Tj ET")]        // a literal string is not constrained at all
    public void A_well_formed_string_is_accepted(string content)
    {
        Assert.Empty(Findings(ContentDoc(content)));
    }

    [Fact]
    public void An_in_memory_document_reports_nothing()
    {
        // No written form to be wrong about. Same shape as XrefTableSpacingRule with no SourceBytes.
        var doc = new PdfDocument();
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Junk")] = new PdfString([0x48], PdfStringFormat.Hexadecimal),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Both_violations_are_reported_at_most_once_each()
    {
        Finding[] findings = Findings(ContentDoc("BT <48455> Tj <484!> Tj <9AB> Tj ET"));
        Assert.Equal(2, findings.Length);
    }
}
