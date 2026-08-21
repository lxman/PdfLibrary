using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

public class ExplicitResourcesRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static byte[] Ops(string s) => Encoding.ASCII.GetBytes(s);

    private static Finding[] Findings(PdfDocument doc) =>
        new ExplicitResourcesRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();

    /// <summary>A one-page doc whose page /Resources and ancestor /Pages /Resources are set separately.</summary>
    private static PdfDocument Doc(string pageContent, PdfDictionary? pageResources, PdfDictionary? pagesResources)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops(pageContent)));

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
        };
        if (pageResources is not null) page[N("Resources")] = pageResources;
        doc.AddObject(3, 0, page);

        var pages = new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        };
        if (pagesResources is not null) pages[N("Resources")] = pagesResources;
        doc.AddObject(2, 0, pages);

        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfDictionary XObjectResources(string name, int objNum) =>
        new() { [N("XObject")] = new PdfDictionary { [N(name)] = Ref(objNum) } };

    [Fact]
    public void An_xobject_inherited_from_an_ancestor_pages_node_is_flagged()
    {
        Finding f = Assert.Single(Findings(Doc("/X0 Do\n", null, XObjectResources("X0", 10))));
        Assert.Equal("explicit-resources", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.2.2"), f.Clause);
        Assert.Contains("X0", f.Message);
        Assert.Equal(0, f.PageIndex);
    }

    [Fact]
    public void An_xobject_in_the_pages_own_resources_is_not_flagged()
    {
        Assert.Empty(Findings(Doc("/X0 Do\n", XObjectResources("X0", 10), null)));
    }

    [Fact]
    public void A_name_absent_everywhere_is_not_flagged()
    {
        // Not "inherited" — it resolves nowhere. That is a different defect; veraPDF's property is
        // inheritedResourceNames, so staying silent here is both faithful and the lower-FP choice.
        Assert.Empty(Findings(Doc("/X0 Do\n", null, null)));
    }

    [Fact]
    public void Device_colour_operators_are_not_resource_references()
    {
        // The fail-e/pass-b fixture pair turns on exactly this: identical structure, and the only
        // difference is whether the stream NAMES a resource.
        Assert.Empty(Findings(Doc("1 1 1 rg\n0 0 10 10 re f\n", null, XObjectResources("X0", 10))));
    }

    [Fact]
    public void A_device_colour_space_name_is_not_a_resource_reference()
    {
        var pagesRes = new PdfDictionary
        {
            [N("ColorSpace")] = new PdfDictionary { [N("DeviceRGB")] = Ref(11) },
        };
        Assert.Empty(Findings(Doc("/DeviceRGB cs\n", null, pagesRes)));
    }

    /// <summary>A page whose own /Resources hold a form and a colour space; the form's /Resources are
    /// supplied separately (null = the form has none, the fail-e shape).</summary>
    private static PdfDocument FormDoc(string formContent, PdfDictionary? formResources)
    {
        var doc = new PdfDocument();

        var formDict = new PdfDictionary
        {
            [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"),
            [N("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0),
                                       new PdfInteger(10), new PdfInteger(10)),
        };
        if (formResources is not null) formDict[N("Resources")] = formResources;
        doc.AddObject(10, 0, new PdfStream(formDict, Ops(formContent)));

        doc.AddObject(11, 0, new PdfArray(N("CalGray")));

        var pageResources = new PdfDictionary
        {
            [N("XObject")] = new PdfDictionary { [N("X0")] = Ref(10) },
            [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = Ref(11) },
        };

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops("/X0 Do\n")));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4), [N("Resources")] = pageResources,
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
    public void A_form_inheriting_a_colour_space_from_the_page_is_flagged()
    {
        Finding f = Assert.Single(Findings(FormDoc("/CS0 cs\n0.5 sc\n", formResources: null)));
        Assert.Contains("CS0", f.Message);
        Assert.Equal(10, f.ObjectNumber);
    }

    [Fact]
    public void A_form_with_no_resources_using_only_device_colour_is_not_flagged()
    {
        // The fail-e / pass-b discriminator.
        Assert.Empty(Findings(FormDoc("1 1 1 rg\n0 0 10 10 re f\n", formResources: null)));
    }

    [Fact]
    public void A_form_carrying_its_own_colour_space_is_not_flagged()
    {
        var own = new PdfDictionary
        {
            [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = Ref(11) },
        };
        Assert.Empty(Findings(FormDoc("/CS0 cs\n0.5 sc\n", own)));
    }
}
