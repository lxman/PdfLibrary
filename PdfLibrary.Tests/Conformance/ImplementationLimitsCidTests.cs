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
/// ISO 19005-2 clause 6.1.13 test 10 (<see cref="ImplementationLimitsRule"/>): no CID above 65535.
/// veraPDF's object is <c>CMapFile</c> with <c>maximalCID</c>, so this is a property of what the
/// embedded CMap DECLARES, not a scan of CIDs used in content.
///
/// <para>Corpus fixtures "veraPDF test suite 6-1-13-t08-fail-b.pdf" and "…-t10-fail-a.pdf" are the
/// end-to-end proof — both carry <c>&lt;3f00&gt; &lt;3fff&gt; 65536</c>, a top CID of 65791, in a
/// Type0 font whose /Encoding is a /CMap stream. These pin the logic without the corpus, which the
/// <c>Category=Parity</c> tests skip when it is absent.</para>
/// </summary>
public class ImplementationLimitsCidTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static Finding[] CidFindings(PdfDocument doc) =>
        [.. new ImplementationLimitsRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => f.Message.Contains("CID", System.StringComparison.Ordinal))];

    private const string OverLimitCMap =
        "/CIDInit /ProcSet findresource begin\n1 begincidrange\n<3f00> <3fff> 65536\nendcidrange\nend";

    private const string ConformingCMap =
        "/CIDInit /ProcSet findresource begin\n1 begincidrange\n<0000> <00ff> 0\nendcidrange\nend";

    /// <summary>A one-page document with a Type0 font whose /Encoding is the given CMap — as a
    /// stream when <paramref name="cmapBody"/> is given, otherwise the predefined name Identity-H.
    /// <paramref name="extraCatalogEntry"/> hangs an extra object off the catalog so the
    /// object-graph walk reaches it, which is how a caller adds an unrelated violation.</summary>
    private static PdfDocument Doc(string? cmapBody, PdfObject? extraCatalogEntry = null)
    {
        const string pageContent = "BT ET";
        var doc = new PdfDocument();

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("Test-Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(7)),
        };
        if (cmapBody is null)
            font[N("Encoding")] = N("Identity-H");
        else
        {
            doc.AddObject(6, 0, new PdfStream(
                new PdfDictionary { [N("Type")] = N("CMap") }, Encoding.ASCII.GetBytes(cmapBody)));
            font[N("Encoding")] = Ref(6);
        }

        doc.AddObject(7, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"), [N("Subtype")] = N("CIDFontType0"), [N("BaseFont")] = N("Test"),
        });
        doc.AddObject(5, 0, font);

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(5) },
            },
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
    public void An_over_limit_CID_in_an_embedded_CMap_is_flagged()
    {
        Finding f = Assert.Single(CidFindings(Doc(OverLimitCMap)));

        Assert.Equal("implementation-limits", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("6.1.13", f.Clause, System.StringComparison.Ordinal);
        Assert.Contains("65791", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_conforming_CMap_is_accepted()
    {
        Assert.Empty(CidFindings(Doc(ConformingCMap)));
    }

    [Fact]
    public void A_predefined_encoding_name_is_not_examined()
    {
        // Identity-H has no embedded file to read, and its maximum CID is exactly 65535 anyway.
        Assert.Empty(CidFindings(Doc(cmapBody: null)));
    }

    [Fact]
    public void The_message_avoids_the_word_integer()
    {
        // IsIntegerFinding distinguishes this rule's integer findings by a substring test on the
        // message. A CID finding containing "integer" would be misclassified by it.
        Finding f = Assert.Single(CidFindings(Doc(OverLimitCMap)));
        Assert.DoesNotContain("integer", f.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_out_of_range_integer_does_not_suppress_the_CID_finding()
    {
        // THE REGRESSION TEST for Check's scoping. Before this task, Check ended with a bare
        // `yield break` on the integer arm, which terminates the WHOLE iterator — so a document
        // carrying both violations would report the integer one and silently drop this one.
        //
        // The integer MUST live in the OBJECT GRAPH, not a content stream. `integerReported` is
        // set only by CheckStringsAndNames (the object walk), so the bare `yield break` fires only
        // when that arm reported. A content-stream integer leaves the flag false, the yield break
        // never runs, and this test would pass with the bug still present — a test that cannot
        // fail for the reason it exists.
        PdfDocument doc = Doc(OverLimitCMap, new PdfArray(new PdfInteger(2157483648L)));

        Finding[] all = [.. new ImplementationLimitsRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))];

        Assert.Contains(all, f => f.Message.Contains("integer", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, f => f.Message.Contains("CID", System.StringComparison.Ordinal));
    }
}
