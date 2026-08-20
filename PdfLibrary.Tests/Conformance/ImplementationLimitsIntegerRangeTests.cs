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
/// ISO 19005-2 clause 6.1.13 test 1 (<see cref="ImplementationLimitsRule"/>): no integer outside
/// ±2147483647. Two paths, because the two parsers differ — the object parser keeps the true value
/// in <see cref="PdfInteger.LongValue"/>, while the content parser clamps to 0 and records the fact.
///
/// <para>Corpus fixtures "…6-1-13-t01-fail-b.pdf" (a <c>Td</c> operand) and "…-fail-c.pdf" (inside a
/// <c>/Dest</c> array on an outline item) are the end-to-end proof; these pin the logic without the
/// corpus.</para>
/// </summary>
public class ImplementationLimitsIntegerRangeTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static Finding[] IntegerFindings(PdfDocument doc) =>
        [.. new ImplementationLimitsRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => f.Message.Contains("integer", System.StringComparison.OrdinalIgnoreCase))];

    /// <summary>A one-page document; <paramref name="extraCatalogEntry"/> is hung off the catalog so
    /// the object-graph walk reaches it, mirroring fail-c's outline /Dest array.</summary>
    private static PdfDocument Doc(string pageContent, PdfObject? extraCatalogEntry = null)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Resources")] = new PdfDictionary(),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        if (extraCatalogEntry is not null)
            catalog[N("Dest")] = extraCatalogEntry;
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void An_out_of_range_integer_in_the_object_graph_is_flagged()
    {
        // fail-c's shape: /Dest [… /XYZ 0 0 2157483648]. The object parser keeps the true value.
        Finding f = Assert.Single(IntegerFindings(Doc(
            "BT ET",
            new PdfArray(N("XYZ"), new PdfInteger(0), new PdfInteger(0), new PdfInteger(2157483648L)))));

        Assert.Equal("implementation-limits", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("6.1.13", f.Clause, System.StringComparison.Ordinal);
        Assert.Contains("2157483648", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void An_out_of_range_integer_in_page_content_is_flagged()
    {
        // fail-b's shape exactly: "0 2157483648 Td". The content parser clamped it to 0, so this
        // can only be found via the recorded fact.
        Finding f = Assert.Single(IntegerFindings(Doc("q BT 50 150 Td 0 2157483648 Td ET Q")));
        Assert.Contains("6.1.13", f.Clause, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2147483647L)]
    [InlineData(-2147483648L)]
    [InlineData(0L)]
    public void An_in_range_integer_is_accepted(long value)
    {
        Assert.Empty(IntegerFindings(Doc("BT ET", new PdfArray(new PdfInteger(value)))));
    }

    [Fact]
    public void A_negative_out_of_range_integer_is_flagged()
    {
        // fail-a's shape: -2157483648 in a /Widths array.
        Assert.Single(IntegerFindings(Doc("BT ET", new PdfArray(new PdfInteger(-2157483648L)))));
    }

    [Fact]
    public void At_most_one_integer_finding_is_reported()
    {
        Assert.Single(IntegerFindings(Doc(
            "BT 0 2157483648 Td 0 2157483649 Td ET",
            new PdfArray(new PdfInteger(2157483650L)))));
    }
}
