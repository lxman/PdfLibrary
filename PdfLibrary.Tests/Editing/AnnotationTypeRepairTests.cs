using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Tests for <see cref="PdfDocumentEditor.PreviewAnnotationTypeRepairs"/> -- the read-only
/// preview of PDF/A clause 6.3.1 annotation-type defects
/// (<c>PdfLibrary.Conformance.Rules.AnnotationTypeRule</c>). One test per row of the classification
/// table in <c>docs/superpowers/specs/2026-08-24-annotation-type-remediation-design.md</c> §6, plus a
/// completeness test tying every shape <c>AnnotationTypeRule.Check</c> can raise back to a candidate
/// or a refusal. Task 3 (the write side, <c>RepairAnnotationTypes</c>) is out of scope here.</summary>
public sealed class AnnotationTypeRepairTests
{
    // ---- Fixture builders ---------------------------------------------------------------------

    /// <summary>A document with <paramref name="pageCount"/> pages, each carrying trivial page
    /// content, opened through the normal <c>Edit()</c> path (materializes, flattens the page tree) --
    /// the same path <see cref="PdfDocumentEditor.PreviewAnnotationTypeRepairs"/> always runs under.
    /// Mirrors <c>AnnotationFlagsEditingTests.EditorWithAnnotation</c>'s convention.</summary>
    private static PdfDocumentEditor NewEditor(int pageCount = 1)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create();
        for (var i = 0; i < pageCount; i++)
            builder = builder.AddPage(p => p.AddText("x", 72, 700, "Helvetica", 12));
        byte[] bytes = builder.ToByteArray();
        return PdfDocumentEditor.Open(new MemoryStream(bytes));
    }

    /// <summary>Appends <paramref name="entry"/> (an indirect reference, or -- for the
    /// "not an indirect object" row -- a direct dictionary) to the <c>/Annots</c> array of the page at
    /// <paramref name="pageIndex"/>, creating the array if the page has none yet.</summary>
    private static void AddAnnotEntry(PdfDocument doc, int pageIndex, PdfObject entry)
    {
        PdfDictionary page = PageTreeOps.PageDicts(doc)[pageIndex];
        var name = new PdfName("Annots");
        if (page.Get(name) is PdfArray existing)
            existing.Add(entry);
        else
            page[name] = new PdfArray(entry);
    }

    /// <summary>A bare annotation dictionary: <paramref name="subtype"/> (omitted entirely when null)
    /// plus a /Rect, matching what a real annotation always carries. No /AP -- callers add one.</summary>
    private static PdfDictionary MakeAnnotation(string? subtype)
    {
        var annot = new PdfDictionary();
        if (subtype is not null)
            annot[new PdfName("Subtype")] = new PdfName(subtype);
        annot[new PdfName("Rect")] = new PdfArray(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100));
        return annot;
    }

    /// <summary>A minimal Form XObject stream (/Subtype /Form, a /BBox, trivial content) -- the shape
    /// <see cref="PdfDocumentEditor.PreviewAnnotationTypeRepairs"/> looks for at /AP /N.</summary>
    private static PdfStream MakeFormXObject()
    {
        var dict = new PdfDictionary { [new PdfName("Subtype")] = new PdfName("Form") };
        dict[new PdfName("BBox")] = new PdfArray(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100));
        return new PdfStream(dict, "q Q"u8.ToArray());
    }

    /// <summary>A non-Form XObject stream (an Image), for the "/N is not a Form" refusal row.</summary>
    private static PdfStream MakeImageXObject()
    {
        var dict = new PdfDictionary { [new PdfName("Subtype")] = new PdfName("Image") };
        return new PdfStream(dict, [0, 0, 0]);
    }

    // ---- Classification table rows -------------------------------------------------------------

    [Fact]
    public void Prohibited_subtype_with_a_resolvable_Form_appearance_on_a_found_page_is_a_candidate()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        AnnotationTypeRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(annotRef.ObjectNumber, candidate.ObjectNumber);
        Assert.Equal("3D", candidate.Subtype);
        Assert.Equal(0, candidate.PageIndex);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void Prohibited_subtype_with_no_AP_at_all_is_a_refusal()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary annot = MakeAnnotation("Sound");
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationTypeRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal("Sound", refusal.Subtype);
        Assert.Contains("/AP", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Prohibited_subtype_with_AP_but_no_N_is_a_refusal()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary annot = MakeAnnotation("Screen");
        annot[new PdfName("AP")] = new PdfDictionary(); // present, but no /N
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationTypeRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Contains("/N", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Prohibited_subtype_whose_N_is_not_a_Form_is_a_refusal()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference imageRef = doc.RegisterObject(MakeImageXObject());
        PdfDictionary annot = MakeAnnotation("Movie");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = imageRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationTypeRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Contains("Form XObject", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void State_keyed_N_whose_AS_names_a_stream_in_it_is_a_candidate()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference onRef = doc.RegisterObject(MakeFormXObject());
        PdfIndirectReference offRef = doc.RegisterObject(MakeFormXObject());
        var stateDict = new PdfDictionary
        {
            [new PdfName("On")] = onRef,
            [new PdfName("Off")] = offRef,
        };
        PdfDictionary annot = MakeAnnotation("Stamp3D"); // any prohibited name -- not a real ISO subtype
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = stateDict };
        annot[new PdfName("AS")] = new PdfName("On");
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        AnnotationTypeRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(annotRef.ObjectNumber, candidate.ObjectNumber);
        Assert.Empty(preview.Refused);
    }

    [Theory]
    [InlineData("On", false)]  // /AS names a state that is not in the dictionary
    [InlineData(null, true)]   // /AS is absent altogether
    public void State_keyed_N_whose_AS_names_no_stream_in_it_is_a_refusal(string? asValue, bool omitAs)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference offRef = doc.RegisterObject(MakeFormXObject());
        var stateDict = new PdfDictionary { [new PdfName("Off")] = offRef }; // no "On" entry
        PdfDictionary annot = MakeAnnotation("Screen");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = stateDict };
        if (!omitAs) annot[new PdfName("AS")] = new PdfName(asValue!);
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationTypeRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Contains("current appearance", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_Subtype_is_a_refusal_with_a_null_Subtype()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary annot = MakeAnnotation(null);
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationTypeRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Null(refusal.Subtype);
        Assert.Contains("no appearance-bearing type", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Permitted_subtype_is_neither_a_candidate_nor_a_refusal()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        // Even one that, if it were prohibited, would refuse (no /AP at all) -- the allowlist check
        // must short-circuit before the /AP walk runs.
        PdfDictionary annot = MakeAnnotation("Link");
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    // "Not an indirect object" (the classification table's last row): AnnotationTypeRule.Check still
    // raises a Finding for a direct annotation dictionary (with ObjectNumber null), but this editor's
    // per-object candidate/refusal contract has no object number to key it on. It must be excluded
    // from BOTH lists -- Pellucid's domain hard-blocks it instead, addressless.
    [Fact]
    public void A_direct_non_indirect_annotation_is_excluded_from_both_lists()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D"); // never registered -- stays a direct dictionary
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        AddAnnotEntry(doc, 0, annot); // the /Annots entry IS the dictionary itself, not a reference

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    // "Owning page not found (orphaned annotation)" is the one classification-table row this test file
    // cannot produce as a REFUSAL: AnnotationTypeRule.Check's own violation set is drawn from walking
    // Document.GetPages() -> each page's /Annots (ConformanceContext.CollectAnnotations), so an
    // annotation the rule can raise a Finding for is -- by that same construction -- always found
    // under some page. This editor's own enumeration mirrors that walk exactly (see
    // EnumerateIndirectAnnotations's doc comment), so "orphaned" cannot arise from a Finding either.
    // What this test proves instead: an annotation-shaped object that exists in the document graph but
    // is genuinely unreferenced by any page's /Annots is correctly invisible to Preview -- neither a
    // candidate nor a spurious refusal -- matching the rule's own blindness to it, rather than being
    // silently misclassified.
    [Fact]
    public void An_object_not_referenced_by_any_page_Annots_is_invisible_to_preview()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        doc.RegisterObject(annot); // registered in the document graph, but never added to any page

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void An_annotation_shared_across_two_pages_is_classified_once()
    {
        PdfDocumentEditor editor = NewEditor(pageCount: 2);
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);
        AddAnnotEntry(doc, 1, annotRef); // the same object, referenced from both pages

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        AnnotationTypeRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(0, candidate.PageIndex); // the first page it is found under, matching
                                               // ConformanceContext.CollectAnnotations's dedup order
        Assert.Empty(preview.Refused);
    }

    // Preview must be genuinely read-only -- calling it twice returns the same answer and mutates
    // nothing. This is the property that lets a Pellucid domain's Propose call it safely.
    [Fact]
    public void Preview_is_read_only_and_repeatable()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview first = editor.PreviewAnnotationTypeRepairs();
        AnnotationTypeRepairPreview second = editor.PreviewAnnotationTypeRepairs();

        Assert.Equal(annotRef.ObjectNumber, Assert.Single(first.Candidates).ObjectNumber);
        Assert.Equal(annotRef.ObjectNumber, Assert.Single(second.Candidates).ObjectNumber);
    }

    // ---- Completeness: every shape AnnotationTypeRule.Check can raise lands somewhere ----------

    // AnnotationTypeRule.Check raises exactly two Finding shapes: "no /Subtype" and "subtype
    // 'X' is not permitted". This test builds one document per structural variant this classifier
    // distinguishes within the "prohibited subtype" shape (the one with sub-cases) and asserts every
    // one lands in EXACTLY one of Candidates/Refused -- never neither, which is the property the
    // image-dictionary program had to be corrected into having (a violation that produced neither read
    // as "nothing wrong" to a caller checking only those two lists).
    [Fact]
    public void Every_prohibited_subtype_violation_shape_lands_in_exactly_one_bucket()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;
        var expectedObjectNumbers = new List<int>();

        void AddCase(Action<PdfDictionary> configure)
        {
            PdfDictionary annot = MakeAnnotation("3D");
            configure(annot);
            PdfIndirectReference r = doc.RegisterObject(annot);
            AddAnnotEntry(doc, 0, r);
            expectedObjectNumbers.Add(r.ObjectNumber);
        }

        // candidate: resolvable Form appearance
        AddCase(a => a[new PdfName("AP")] =
            new PdfDictionary { [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()) });
        // refusal: no /AP
        AddCase(_ => { });
        // refusal: /AP with no /N
        AddCase(a => a[new PdfName("AP")] = new PdfDictionary());
        // refusal: /N is not a Form
        AddCase(a => a[new PdfName("AP")] =
            new PdfDictionary { [new PdfName("N")] = doc.RegisterObject(MakeImageXObject()) });
        // refusal: state-keyed /N, /AS names nothing in it
        AddCase(a =>
        {
            a[new PdfName("AP")] = new PdfDictionary
            {
                [new PdfName("N")] = new PdfDictionary
                {
                    [new PdfName("Off")] = doc.RegisterObject(MakeFormXObject()),
                },
            };
            a[new PdfName("AS")] = new PdfName("On");
        });

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        var accountedFor = preview.Candidates.Select(c => c.ObjectNumber)
            .Concat(preview.Refused.Select(r => r.ObjectNumber))
            .ToHashSet();
        foreach (int n in expectedObjectNumbers)
            Assert.Contains(n, accountedFor);
        Assert.Equal(expectedObjectNumbers.Count, accountedFor.Count); // no duplicates, nothing extra
        Assert.Single(preview.Candidates);
        Assert.Equal(4, preview.Refused.Count);
    }
}
