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
/// PDF/A clause 6.2.2 (<see cref="ContentStreamOperatorRule"/>), calibrated against veraPDF's Op_Undefined rule
/// (test <c>false</c> — any operator token not in the ISO 32000-1 set fails). The walk is usage-sensitive: an
/// operator only counts if it is in content that is actually reached (a page, or a form invoked via <c>Do</c>),
/// so a stray operator in a form that is never invoked is not a violation — matching veraPDF and keeping the
/// 0-false-positive invariant.
/// </summary>
public class ContentStreamOperatorRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static byte[] Ops(string s) => Encoding.ASCII.GetBytes(s);
    private static Finding[] Findings(PdfDocument doc) =>
        new ContentStreamOperatorRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();

    /// <summary>A one-page document whose /Contents is <paramref name="pageContent"/>. When
    /// <paramref name="formContent"/> is given, a Form XObject (obj 10) with that content is registered under
    /// /Fm0 in the page resources — invoked only if <paramref name="pageContent"/> issues <c>/Fm0 Do</c>.</summary>
    private static PdfDocument Doc(string pageContent, string? formContent = null)
    {
        var doc = new PdfDocument();
        var resources = new PdfDictionary();
        if (formContent is not null)
        {
            var formDict = new PdfDictionary
            {
                [N("Type")] = N("XObject"),
                [N("Subtype")] = N("Form"),
                [N("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10)),
            };
            doc.AddObject(10, 0, new PdfStream(formDict, Ops(formContent)));
            resources[N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(10) };
        }

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4),
            [N("Resources")] = resources,
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
    public void An_undefined_operator_in_page_content_is_flagged()
    {
        Finding f = Assert.Single(Findings(Doc("0 0 10 10 re f\nBogusOp\n")));
        Assert.Equal("content-stream-operator", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.2.2"), f.Clause);
        Assert.Contains("BogusOp", f.Message);
    }

    [Fact]
    public void A_page_of_only_valid_operators_passes()
    {
        Assert.Empty(Findings(Doc("q 1 0 0 1 0 0 cm 0 0 10 10 re f Q BT /F0 12 Tf (hi) Tj ET\n")));
    }

    [Fact]
    public void An_undefined_operator_in_an_invoked_form_is_flagged()
    {
        Finding f = Assert.Single(Findings(Doc("q /Fm0 Do Q\n", formContent: "0 0 5 5 re f\nBogusOp\n")));
        Assert.Contains("BogusOp", f.Message);
    }

    [Fact]
    public void An_undefined_operator_in_an_uninvoked_form_is_not_flagged()
    {
        // The form is present in resources but never invoked via Do — veraPDF does not check unreached content.
        Assert.Empty(Findings(Doc("0 0 10 10 re f\n", formContent: "BogusOp\n")));
    }

    [Fact]
    public void An_undefined_operator_inside_BX_EX_is_still_flagged()
    {
        // The clause explicitly applies "even if such operators are bracketed by the BX/EX compatibility operators".
        Finding f = Assert.Single(Findings(Doc("BX BogusOp EX\n")));
        Assert.Contains("BogusOp", f.Message);
    }
}
