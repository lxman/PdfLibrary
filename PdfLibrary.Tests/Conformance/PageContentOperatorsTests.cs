using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// One parse of a page's content, shared by every rule that reads it.
///
/// <para>Measured 2026-08-20: <c>PdfContentParser.Parse</c> was 20.8% of a single-threaded
/// <c>pellucid scan</c> over the gwg-gos print corpus and the dominant cost on the PDF/UA reference
/// files, because SEVEN consumers each rebuilt the same combined byte array and re-parsed it —
/// <c>ConformanceContext</c>'s own glyph/ToUnicode walks, <c>DeviceColourAnalysis</c>,
/// <c>IccCmykOverprintRule</c>, <c>ImplementationLimitsRule</c>, <c>RenderingIntentRule</c>,
/// <c>UaXObjectMcidRule</c> and <c>ContentWalk</c>. Each also carried its own copy of the
/// combine-and-tolerate-failure boilerplate, so the duplication was of logic as well as work.</para>
/// </summary>
public class PageContentOperatorsTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static byte[] Ops(string s) => Encoding.ASCII.GetBytes(s);

    /// <summary>A one-page document whose /Contents is each of <paramref name="contentStreams"/> in
    /// order — one stream when a single string is given, an array when several are.</summary>
    private static ConformanceContext Ctx(params string[] contentStreams)
    {
        var doc = new PdfDocument();
        var refs = new List<PdfObject>();
        for (var i = 0; i < contentStreams.Length; i++)
        {
            doc.AddObject(10 + i, 0, new PdfStream(new PdfDictionary(), Ops(contentStreams[i])));
            refs.Add(Ref(10 + i));
        }

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Resources")] = new PdfDictionary(),
        };
        if (contentStreams.Length == 1) page[N("Contents")] = refs[0];
        else if (contentStreams.Length > 1) page[N("Contents")] = new PdfArray(refs.ToArray());

        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return new ConformanceContext(doc, ConformanceProfile.PdfA2b);
    }

    [Fact]
    public void A_page_is_parsed_once_and_the_result_reused()
    {
        ConformanceContext context = Ctx("q 1 0 0 1 10 10 cm Q");

        IReadOnlyList<PdfOperator> first = context.PageContentOperators(context.Pages[0]);
        IReadOnlyList<PdfOperator> second = context.PageContentOperators(context.Pages[0]);

        Assert.Same(first, second); // the same parse, not an equal one
        Assert.NotEmpty(first);
    }

    [Fact]
    public void Every_content_stream_of_a_page_contributes()
    {
        // ISO 32000-1 7.8.2: a page's content streams are one logical stream. Dropping any of them
        // would silently narrow every rule that reads content.
        ConformanceContext context = Ctx("q Q", "q Q", "q Q");

        IReadOnlyList<PdfOperator> ops = context.PageContentOperators(context.Pages[0]);

        Assert.Equal(6, ops.Count(o => o.Name is "q" or "Q"));
    }

    [Fact]
    public void Streams_are_separated_so_a_token_cannot_straddle_the_join()
    {
        // Concatenated without a separator, "Q" + "q" lexes as one "Qq" token and BOTH operators are
        // lost. The separator is what makes the join safe.
        ConformanceContext context = Ctx("q Q", "q Q");

        IReadOnlyList<PdfOperator> ops = context.PageContentOperators(context.Pages[0]);

        Assert.Equal(2, ops.Count(o => o.Name == "q"));
        Assert.Equal(2, ops.Count(o => o.Name == "Q"));
        Assert.DoesNotContain(ops, o => o.Name == "Qq");
    }

    [Fact]
    public void A_page_with_no_content_yields_an_empty_list_rather_than_throwing()
    {
        ConformanceContext context = Ctx();

        Assert.Empty(context.PageContentOperators(context.Pages[0]));
    }
}
