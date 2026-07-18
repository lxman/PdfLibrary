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
/// PDF/A-2/3 clause 6.2.9 (<see cref="ProhibitedXObjectRule"/>): prohibited XObject constructs. Calibrated
/// against veraPDF's three PDFA-2 rules — form XObject keys (<c>/OPI</c>, <c>/PS</c>, <c>/Subtype2 = PS</c>),
/// reference XObjects (<c>/Ref</c> on a form), and PostScript XObjects (<c>/Subtype PS</c>).
/// </summary>
public class ProhibitedXObjectRuleTests
{
    private static PdfName N(string s) => new(s);
    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    /// <summary>A document whose only stream is a Form XObject carrying the given extra dictionary entries.
    /// CollectStreams enumerates the indirect object table, so the XObject need not be reached from a page.</summary>
    private static PdfDocument FormWith(params (string key, PdfObject value)[] entries)
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Form"),
        };
        foreach ((string key, PdfObject value) in entries)
            dict[N(key)] = value;

        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfStream(dict, Encoding.ASCII.GetBytes(" ")));
        return doc;
    }

    private static Finding[] Findings(PdfDocument doc) => new ProhibitedXObjectRule().Check(Ctx(doc)).ToArray();

    [Fact]
    public void A_form_xobject_with_an_OPI_key_is_flagged()
    {
        Finding f = Assert.Single(Findings(FormWith(("OPI", new PdfDictionary()))));
        Assert.Equal("prohibited-xobject", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.2.9"), f.Clause);
        Assert.Contains("OPI", f.Message);
    }

    [Fact]
    public void A_form_xobject_with_a_PS_key_is_flagged()
    {
        Finding f = Assert.Single(Findings(FormWith(("PS", new PdfDictionary()))));
        Assert.Equal("prohibited-xobject", f.RuleId);
        Assert.Contains("PS", f.Message);
    }

    [Fact]
    public void A_form_xobject_with_Subtype2_PS_is_flagged()
    {
        Finding f = Assert.Single(Findings(FormWith(("Subtype2", N("PS")))));
        Assert.Equal("prohibited-xobject", f.RuleId);
    }

    [Fact]
    public void A_clean_form_xobject_is_not_flagged()
    {
        Assert.Empty(Findings(FormWith()));
    }

    [Fact]
    public void A_Subtype2_value_other_than_PS_is_not_flagged()
    {
        // Only the value PS is prohibited; Subtype2 itself is legal (it names the form's post-processing type).
        Assert.Empty(Findings(FormWith(("Subtype2", N("PDF")))));
    }

    [Fact]
    public void A_reference_xobject_is_flagged()
    {
        // 6.2.9 test 2: a Form XObject carrying /Ref imports external content and is prohibited in PDF/A.
        Finding f = Assert.Single(Findings(FormWith(("Ref", new PdfDictionary()))));
        Assert.Equal("prohibited-xobject", f.RuleId);
        Assert.Contains("Ref", f.Message);
    }

    [Fact]
    public void A_postscript_xobject_is_flagged()
    {
        // 6.2.9 test 3: an XObject whose /Subtype is PS is a PostScript XObject, prohibited outright.
        var dict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("PS"),
        };
        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfStream(dict, Encoding.ASCII.GetBytes(" ")));

        Finding f = Assert.Single(Findings(doc));
        Assert.Equal("prohibited-xobject", f.RuleId);
        Assert.Contains("PostScript", f.Message);
    }

    [Fact]
    public void A_non_form_xobject_carrying_the_same_key_is_not_flagged()
    {
        // 6.2.9's key rules are scoped to Form XObjects (veraPDF object PDXForm); an image XObject with
        // /OPI is governed by a different clause and must not trip this rule.
        var dict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("OPI")] = new PdfDictionary(),
        };
        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfStream(dict, Encoding.ASCII.GetBytes(" ")));
        Assert.Empty(Findings(doc));
    }
}
