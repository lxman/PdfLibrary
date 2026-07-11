using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice B2 (PDF/UA-1) — Form XObject MCID reuse (ISO 14289-1:2014, 7.20; Matterhorn 30-002), covered by
/// <see cref="UaXObjectMcidRule"/>. A Form XObject whose own content is tagged (carries an <c>/MCID</c>)
/// maps to a single structure content item; drawing it with more than one <c>Do</c> maps that one item to
/// several places, breaking the structure↔content correspondence (ISO 32000-1 14.7.2). The rule flags a
/// form only when BOTH its own content contains an <c>/MCID</c>-bearing <c>BDC</c> AND it is referenced by
/// more than one <c>Do</c> edge across the document; a form drawn once, or one with no MCIDs (e.g. a reused
/// artifact-only header), is conformant.
/// <para>
/// The veraPDF PDF_UA-1 corpus fixtures (7.20-t02) and the PDF/UA reference-file pass-oracle back the
/// red/green surface (<see cref="CorpusOracleTests"/> / <see cref="PdfUaReferenceOracleTests"/>); these are
/// the hand-built edge cases.
/// </para>
/// </summary>
public class PreflightSlice25Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static PdfArray Box() =>
        new(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792));

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfUA1);

    private static Finding[] Findings(PdfDocument doc) => new UaXObjectMcidRule().Check(Ctx(doc)).ToArray();

    /// <summary>
    /// A document with one Form XObject <c>/Fm0</c> (object 9) whose body is <paramref name="formBody"/>, and
    /// one page per entry in <paramref name="pageContents"/> — each page's /Resources maps <c>/Fm0</c> to the
    /// form so a <c>/Fm0 Do</c> in its content is a reference edge to object 9.
    /// </summary>
    private static PdfDocument DocWithForm(string formBody, params string[] pageContents)
    {
        var doc = new PdfDocument();

        doc.AddObject(9, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"), [N("BBox")] = Box() },
            Encoding.ASCII.GetBytes(formBody)));

        var kids = new List<PdfObject>();
        int next = 10;
        foreach (string content in pageContents)
        {
            int contentObj = next++;
            int pageObj = next++;
            doc.AddObject(contentObj, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
            doc.AddObject(pageObj, 0, new PdfDictionary
            {
                [N("Type")] = N("Page"),
                [N("Parent")] = Ref(2),
                [N("Contents")] = Ref(contentObj),
                [N("Resources")] = new PdfDictionary
                {
                    [N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(9) },
                },
                [N("MediaBox")] = Box(),
            });
            kids.Add(Ref(pageObj));
        }

        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(kids.ToArray()),
            [N("Count")] = new PdfInteger(pageContents.Length),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>A one-page document with plain content and no XObjects at all.</summary>
    private static PdfDocument DocNoForm(string content)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary(),
            [N("MediaBox")] = Box(),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    // A tagged form (its own content carries an /MCID) vs an untagged / artifact-only form.
    private const string TaggedBody = "/P <</MCID 0>> BDC 0 0 10 10 re f EMC";
    private const string ArtifactBody = "/Artifact BMC 0 0 10 10 re f EMC"; // marked content, but no /MCID

    [Fact]
    public void Tagged_form_referenced_twice_on_one_page_is_flagged()
    {
        var doc = DocWithForm(TaggedBody, "/Fm0 Do /Fm0 Do");
        Finding f = Assert.Single(Findings(doc));
        Assert.Equal("ua-xobject-mcid", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Equal(9, f.ObjectNumber);
        Assert.Contains("referenced more than once", f.Message);
        Assert.Contains("ISO 14289-1:2014, 7.20", f.Clause);
    }

    [Fact]
    public void Tagged_form_referenced_once_passes()
    {
        var doc = DocWithForm(TaggedBody, "/Fm0 Do");
        Assert.Empty(Findings(doc));
    }

    [Fact] // referenced twice, but its content carries no /MCID (only an /Artifact BMC) — safe to reuse
    public void Untagged_form_referenced_twice_passes()
    {
        var doc = DocWithForm(ArtifactBody, "/Fm0 Do /Fm0 Do");
        Assert.Empty(Findings(doc));
    }

    [Fact] // a plain content form with no marked content at all, drawn twice — also safe
    public void Form_without_any_marked_content_referenced_twice_passes()
    {
        var doc = DocWithForm("0 0 10 10 re f", "/Fm0 Do /Fm0 Do");
        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Document_with_no_form_xobjects_has_no_findings()
    {
        Assert.Empty(Findings(DocNoForm("BT (hi) Tj ET")));
    }

    [Fact] // cross-page counting: one Do on each of two pages is still "more than once"
    public void Tagged_form_referenced_from_two_pages_is_flagged()
    {
        var doc = DocWithForm(TaggedBody, "/Fm0 Do", "/Fm0 Do");
        Finding f = Assert.Single(Findings(doc));
        Assert.Equal("ua-xobject-mcid", f.RuleId);
        Assert.Equal(9, f.ObjectNumber);
    }

    [Fact] // end-to-end through the registered rule set
    public void Flagged_form_surfaces_through_the_full_preflight()
    {
        PreflightResult result = Preflighter.Check(
            DocWithForm(TaggedBody, "/Fm0 Do /Fm0 Do"), ConformanceProfile.PdfUA1);
        Assert.Contains(result.Findings, f => f.RuleId == "ua-xobject-mcid" && f.ObjectNumber == 9);
    }

    [Fact] // the same rule is not registered for a non-UA profile
    public void Rule_does_not_apply_to_pdfa()
    {
        PreflightResult result = Preflighter.Check(
            DocWithForm(TaggedBody, "/Fm0 Do /Fm0 Do"), ConformanceProfile.PdfA2b);
        Assert.DoesNotContain(result.Findings, f => f.RuleId == "ua-xobject-mcid");
    }
}
