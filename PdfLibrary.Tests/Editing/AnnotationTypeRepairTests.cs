using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Optimization;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using SkiaSharp;
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
    /// plus a /Rect (<paramref name="rect"/> when given, else the default [0 0 100 100]), matching
    /// what a real annotation always carries. No /AP -- callers add one.</summary>
    private static PdfDictionary MakeAnnotation(string? subtype, PdfObject? rect = null)
    {
        var annot = new PdfDictionary();
        if (subtype is not null)
            annot[new PdfName("Subtype")] = new PdfName(subtype);
        annot[new PdfName("Rect")] = rect ?? new PdfArray(
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

    /// <summary>Like <see cref="MakeFormXObject()"/>, but with caller-controlled /BBox and /Matrix --
    /// for Task 3's geometry-refusal tests. Passing <see langword="null"/> for <paramref name="bbox"/>
    /// omits the key entirely (the "missing /BBox" case); any other value is written verbatim,
    /// including a deliberately malformed one (a non-array, a too-short array, a degenerate box).
    /// <paramref name="matrix"/> follows the same convention; omitting it exercises the "absent
    /// /Matrix defaults to identity" path Task 3 relies on.</summary>
    private static PdfStream MakeFormXObjectWithGeometry(PdfObject? bbox, PdfObject? matrix = null)
    {
        var dict = new PdfDictionary { [new PdfName("Subtype")] = new PdfName("Form") };
        if (bbox is not null) dict[new PdfName("BBox")] = bbox;
        if (matrix is not null) dict[new PdfName("Matrix")] = matrix;
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

    // ---- A hiding /F refuses ---------------------------------------------------------------------

    // ISO 32000-1 12.5.3, Table 165: Invisible (1) suppresses display of an annotation "if it does not
    // belong to one of the standard annotation types and no annotation handler is available" -- the
    // spec's own exception to the 12.5.5 NOTE 3 sentence this whole repair rests on; Hidden (2)
    // suppresses display and print "regardless of its annotation type or whether an annotation handler
    // is available"; NoView (0x20) suppresses on-screen display; ToggleNoView (0x100) inverts NoView
    // for certain events, so the annotation is concealed for some of them either way. Baking the
    // appearance of any of them into page content would REVEAL what the author concealed --
    // permanently, automatically, and one save stage after AnnotationsDomain declined to do exactly
    // that (Pellucid docs/REMEDIATION-CHOICES.md entry 5).
    [Theory]
    [InlineData(0x1, "Invisible")]
    [InlineData(0x2, "Hidden")]
    [InlineData(0x20, "NoView")]
    [InlineData(0x100, "ToggleNoView")]
    public void A_prohibited_annotation_whose_F_hides_it_is_refused_and_names_the_flag(
        int flags, string flagName)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        // Everything else about this annotation is perfectly flattenable: a resolvable Form XObject
        // appearance with sound geometry. /F is the ONLY reason to refuse.
        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        annot[new PdfName("F")] = new PdfInteger(flags);
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationTypeRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal("3D", refusal.Subtype);
        Assert.Contains(flagName, refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("/F", refusal.Reason, StringComparison.Ordinal);
    }

    // A hiding bit refuses whatever ELSE /F carries -- it is a mask test, not an equality test. /F 70
    // is Print (4) + Hidden (2) + ReadOnly (64): a printable, hidden annotation.
    [Fact]
    public void A_hiding_bit_alongside_Print_still_refuses()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        annot[new PdfName("F")] = new PdfInteger(70);
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Contains("Hidden", Assert.Single(preview.Refused).Reason, StringComparison.Ordinal);
    }

    // THE CONTROL, and it is the real corpus shape rather than an invented one: measured 2026-08-24
    // with pikepdf over D:\PdfCorpora\real-world\local-708 and the 4,000-document web-crawl sample,
    // every one of the 11 prohibited-subtype annotations in either corpus carries a NON-hiding /F --
    // 10 of them /F 68 (Print + ReadOnly, the ten SCV documents this program exists for) and one
    // /F 4 (Print). Not one carries a hiding bit. So this test is what proves the refusal above did
    // not cost the program its ten documents.
    [Theory]
    [InlineData(68)] // Print + ReadOnly -- all ten local-708 documents
    [InlineData(4)]  // Print -- the single web-crawl occurrence
    public void A_prohibited_annotation_whose_F_does_not_hide_it_is_still_a_candidate(int flags)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        annot[new PdfName("F")] = new PdfInteger(flags);
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Equal(annotRef.ObjectNumber, Assert.Single(preview.Candidates).ObjectNumber);
        Assert.Empty(preview.Refused);
    }

    // A /F that carries no number cannot be read for hiding bits, so it reads as an ABSENT /F --
    // matching AnnotationFlagsRule's own read and veraPDF's. Deliberately not a refusal: a flag word
    // that cannot be read cannot be said to conceal anything, and refusing on one would decline this
    // repair over a malformation the annotation-flags rule owns.
    [Fact]
    public void A_non_numeric_F_reads_as_absent_and_leaves_the_annotation_a_candidate()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        annot[new PdfName("F")] = new PdfName("Hidden"); // a NAME, not an integer -- malformed
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Single(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    // /F 2.0 is a REAL, not an integer, but a producer that wrote it still meant Hidden. Same
    // coercion AnnotationFlagsRule and PdfPageCollection.Annotations already make.
    [Fact]
    public void A_real_valued_F_is_read_for_its_integer_bits()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        annot[new PdfName("F")] = new PdfReal(2.0);
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Contains("Hidden", Assert.Single(preview.Refused).Reason, StringComparison.Ordinal);
    }

    // The refusal reaches the WRITE side too, because both share ClassifyAnnotationTypes -- and it
    // reaches it as a refusal in the report, not as a silent skip. Nothing on the page is touched:
    // no /Contents materialized, the annotation still in /Annots. This is the assertion that matters,
    // since a hidden annotation reaching the bake step is precisely the permanent reveal.
    [Fact]
    public void Repair_refuses_a_staged_hidden_annotation_and_leaves_the_page_untouched()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;
        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        page.Remove(new PdfName("Contents")); // so "nothing was touched" is easy to see

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        annot[new PdfName("F")] = new PdfInteger(2); // Hidden
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        Assert.Empty(report.Applied);
        AnnotationTypeRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Contains("Hidden", refusal.Reason, StringComparison.Ordinal);

        Assert.Null(page.Get("Contents"));
        PdfArray? annots = page.Get("Annots") as PdfArray;
        Assert.NotNull(annots);
        Assert.Single(annots!);
    }

    // The premise behind the refusal, proven with the engine's own renderer rather than asserted from
    // the spec: PdfRenderer honours Hidden and NoView and draws nothing for such an annotation. On the
    // production page shape -- which has NO /Contents of its own, so the annotation IS the entire
    // visible page -- a Hidden annotation therefore renders to a blank sheet while the same document
    // with /F 4 renders its artwork. Baking the appearance would put that artwork on the page
    // permanently. That is the reveal, and it is measurable, not theoretical.
    [Fact]
    public void A_hidden_annotation_contributes_nothing_to_the_rendered_page_today()
    {
        using PdfDocument hiddenDoc =
            PdfDocument.Load(new MemoryStream(DocWithFlattenableAnnotationNoContents(flags: 2)));
        using PdfDocument printableDoc =
            PdfDocument.Load(new MemoryStream(DocWithFlattenableAnnotationNoContents(flags: 4)));

        byte[] hiddenPixels = RenderPixels(hiddenDoc.GetPage(0)!);
        byte[] printablePixels = RenderPixels(printableDoc.GetPage(0)!);

        Assert.NotEqual(hiddenPixels, printablePixels);

        // And the classifier's two answers line up with those two renders: the hidden one is refused,
        // the printable one is the candidate this program flattens.
        Assert.Empty(hiddenDoc.Edit().PreviewAnnotationTypeRepairs().Candidates);
        Assert.Single(printableDoc.Edit().PreviewAnnotationTypeRepairs().Candidates);
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

    // ---- Geometry refusals are reachable from the PREVIEW, not just the repair ------------------

    // Every row of the spec's second (geometry) classification table, asserted against
    // PreviewAnnotationTypeRepairs rather than RepairAnnotationTypes. These checks used to live on
    // the write side alone, so the preview called each of these a candidate and the repair then
    // refused it -- and Pellucid's AnnotationTypeDomain.CollectForSave discards the report
    // RepairAnnotationTypes returns, so that refusal reached no surface at all: the desktop row said
    // "fix applied", the file was rewritten, the finding survived the reload, and nothing explained
    // it. `pellucid fix` produced no needsDecision row either.
    /// <summary>One geometry shape the §12.5.5 placement cannot be computed for, and the word its
    /// refusal must name. The three geometry entries are FACTORIES, not values: each case is built
    /// twice (once per test below) into two different documents, and a PdfObject registered into one
    /// document must never be handed to another.</summary>
    private sealed record GeometryCase(
        string Name, Func<PdfObject?> Rect, Func<PdfObject?> BBox, Func<PdfObject?> Matrix,
        string Expect);

    private static PdfArray Nums(params double[] values) =>
        new(values.Select(v => (PdfObject)new PdfReal(v)).ToArray());

    private static readonly GeometryCase[] DegenerateGeometryCases =
    [
        new("missing /Rect", () => null, () => Nums(0, 0, 100, 100), () => null, "/Rect"),
        new("/Rect is not four numbers",
            () => new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfName("oops"),
                               new PdfInteger(100)),
            () => Nums(0, 0, 100, 100), () => null, "/Rect"),
        new("zero-area /Rect",
            () => Nums(10, 10, 10, 50), () => Nums(0, 0, 100, 100), () => null, "degenerate"),
        new("missing /BBox", () => Nums(0, 0, 100, 100), () => null, () => null, "/BBox"),
        new("zero-width /BBox",
            () => Nums(0, 0, 100, 100), () => Nums(10, 10, 10, 50), () => null, "degenerate"),
        new("/Matrix present but too short",
            () => Nums(0, 0, 100, 100), () => Nums(0, 0, 100, 100), () => Nums(1, 0), "/Matrix"),
        // A non-finite /Matrix term. ReadNumberArray cannot tell a PdfReal holding NaN from a
        // non-number object -- both mean "this array does not carry six usable numbers" -- so it lands
        // on the malformed-/Matrix reason rather than on ComputeAA's own degeneracy screen. Either
        // would be a correct refusal; this pins which one it actually is.
        new("/Matrix carrying a non-finite term",
            () => Nums(0, 0, 100, 100), () => Nums(0, 0, 100, 100),
            () => Nums(double.NaN, 0, 0, 1, 0, 0), "/Matrix"),
        // A well-formed /Matrix that COLLAPSES the transformed box onto a point: every term zero.
        // ComputeAA's own null this time, not a read failure.
        new("/Matrix collapsing the transformed box",
            () => Nums(0, 0, 100, 100), () => Nums(0, 0, 100, 100), () => Nums(0, 0, 0, 0, 0, 0),
            "degenerate"),
    ];

    /// <summary>Registers one <see cref="GeometryCase"/> as a prohibited-subtype annotation with a
    /// resolvable Form XObject appearance on page 0 of <paramref name="doc"/> -- so the ONLY reason to
    /// refuse it is its geometry -- and returns its object number.</summary>
    private static int AddGeometryCase(PdfDocument doc, GeometryCase geometryCase)
    {
        PdfIndirectReference formRef = doc.RegisterObject(
            MakeFormXObjectWithGeometry(geometryCase.BBox(), geometryCase.Matrix()));
        var annot = new PdfDictionary { [new PdfName("Subtype")] = new PdfName("3D") };
        if (geometryCase.Rect() is { } rect) annot[new PdfName("Rect")] = rect;
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);
        return annotRef.ObjectNumber;
    }

    // One document per case rather than one [Theory] row per case: a PdfObject is not xUnit-
    // serialisable theory data, and the case name is carried in every assertion message instead.
    [Fact]
    public void Preview_refuses_every_geometry_shape_the_repair_could_not_place()
    {
        foreach (GeometryCase geometryCase in DegenerateGeometryCases)
        {
            PdfDocumentEditor editor = NewEditor();
            int objectNumber = AddGeometryCase(editor.Document, geometryCase);

            AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

            Assert.True(preview.Candidates.Count == 0,
                $"[{geometryCase.Name}] should not be a candidate, but preview reported "
                + $"{preview.Candidates.Count}");
            Assert.True(preview.Refused.Count == 1,
                $"[{geometryCase.Name}] should be exactly one refusal, but preview reported "
                + $"{preview.Refused.Count}");
            AnnotationTypeRefusal refusal = preview.Refused[0];
            Assert.True(refusal.ObjectNumber == objectNumber,
                $"[{geometryCase.Name}] refusal named object {refusal.ObjectNumber}, expected "
                + $"{objectNumber}");
            Assert.Equal("3D", refusal.Subtype);
            Assert.True(refusal.Reason.Contains(geometryCase.Expect, StringComparison.OrdinalIgnoreCase),
                $"[{geometryCase.Name}] reason should mention '{geometryCase.Expect}' but was: "
                + refusal.Reason);
        }
    }

    // The invariant itself, on ONE document carrying every geometry shape at once plus a healthy
    // candidate: everything the repair refuses, the preview already refused -- same object numbers,
    // same reasons -- and everything the preview called a candidate, the repair applied. This is the
    // assertion that would have caught the original defect; the per-row theory above only proves each
    // refusal exists.
    [Fact]
    public void Preview_and_repair_agree_on_every_annotation_of_one_document()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        foreach (GeometryCase geometryCase in DegenerateGeometryCases)
            AddGeometryCase(doc, geometryCase);

        // Plus the shapes that refuse for non-geometry reasons, so the agreement is proven across
        // every refusal path this classifier has rather than only the ones this fix moved.
        PdfDictionary noAp = MakeAnnotation("Sound");
        AddAnnotEntry(doc, 0, doc.RegisterObject(noAp));

        PdfDictionary hidden = MakeAnnotation("3D");
        hidden[new PdfName("AP")] = new PdfDictionary
        {
            [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()),
        };
        hidden[new PdfName("F")] = new PdfInteger(2);
        AddAnnotEntry(doc, 0, doc.RegisterObject(hidden));

        PdfDictionary good = MakeAnnotation("3D");
        good[new PdfName("AP")] = new PdfDictionary
        {
            [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()),
        };
        PdfIndirectReference goodRef = doc.RegisterObject(good);
        AddAnnotEntry(doc, 0, goodRef);

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();
        Assert.Equal(goodRef.ObjectNumber, Assert.Single(preview.Candidates).ObjectNumber);
        Assert.Equal(10, preview.Refused.Count); // 8 geometry rows + no-/AP + hidden

        // Stage EVERYTHING -- candidates and refusals alike, which is the worst case a caller could
        // hand this method -- and the repair must reach the identical verdicts.
        var everything = preview.Candidates.Select(c => c.ObjectNumber)
            .Concat(preview.Refused.Select(f => f.ObjectNumber))
            .ToHashSet();
        AnnotationTypeRepairReport report = editor.RepairAnnotationTypes(everything);

        Assert.Equal(goodRef.ObjectNumber, Assert.Single(report.Applied).ObjectNumber);
        Assert.Equal(
            preview.Refused.Select(f => (f.ObjectNumber, f.Subtype, f.Reason)).OrderBy(t => t.ObjectNumber),
            report.Refused.Select(f => (f.ObjectNumber, f.Subtype, f.Reason)).OrderBy(t => t.ObjectNumber));
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
        // refusal: a perfectly bakeable appearance, but /F hides the annotation
        AddCase(a =>
        {
            a[new PdfName("AP")] =
                new PdfDictionary { [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()) };
            a[new PdfName("F")] = new PdfInteger(2);
        });

        AnnotationTypeRepairPreview preview = editor.PreviewAnnotationTypeRepairs();

        var accountedFor = preview.Candidates.Select(c => c.ObjectNumber)
            .Concat(preview.Refused.Select(r => r.ObjectNumber))
            .ToHashSet();
        foreach (int n in expectedObjectNumbers)
            Assert.Contains(n, accountedFor);
        Assert.Equal(expectedObjectNumbers.Count, accountedFor.Count); // no duplicates, nothing extra
        Assert.Single(preview.Candidates);
        Assert.Equal(5, preview.Refused.Count);
    }

    // ── Task 3: RepairAnnotationTypes (the write side) ───────────────────────────────────────────

    /// <summary>Resolves the live object for <paramref name="objectNumber"/> off the document the
    /// editor wraps, mirroring <c>StreamFilterRepairTests.FindStream</c>'s convention.</summary>
    private static PdfDictionary FindDict(PdfDocumentEditor editor, int objectNumber) =>
        (PdfDictionary)editor.Document.Objects[objectNumber];

    /// <summary>The decoded text of every stream in <paramref name="page"/>'s (already-resolved)
    /// /Contents -- a single stream or an array of them -- concatenated in order, for substring
    /// assertions against the baked invocation. Empty when /Contents is absent.</summary>
    private static string DecodedPageContentText(PdfDocument doc, PdfDictionary page)
    {
        PdfObject? raw = page.Get("Contents");
        PdfObject? resolved = raw is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : raw;

        var sb = new StringBuilder();
        switch (resolved)
        {
            case PdfStream single:
                sb.Append(Encoding.Latin1.GetString(single.GetDecodedData()));
                break;
            case PdfArray array:
                foreach (PdfObject entry in array)
                {
                    PdfObject? s = entry is PdfIndirectReference er ? doc.GetObject(er.ObjectNumber) : entry;
                    if (s is PdfStream stream)
                        sb.Append(Encoding.Latin1.GetString(stream.GetDecodedData())).Append(' ');
                }
                break;
        }
        return sb.ToString();
    }

    /// <summary>Builds a one-page document matching this program's measured production shape (spec
    /// §2, 2026-08-24-annotation-type-remediation-design.md): the page has NO /Contents at all,
    /// carries exactly one annotation (/Subtype /3D, /Rect == /BBox == /MediaBox, /Matrix absent --
    /// identity), whose /AP /N is a Form XObject with a real, visible vector fill (so a render
    /// comparison has actual content to prove unchanged, not a blank canvas either side), plus a
    /// /3DD stream referenced only from the annotation. Fixed object numbers, mirroring
    /// <c>ImageDictionaryRepairTests.DocWithImageKeys</c>'s convention: 1 catalog, 2 pages, 3 the
    /// page, 10 the annotation, 20 the Form XObject, 30 the /3DD stream.
    ///
    /// <para><paramref name="flags"/> is the annotation's /F. It defaults to 4 (Print) -- the
    /// non-hiding shape the corpus actually has (measured: /F 68 = Print + ReadOnly on all ten, /F 4
    /// on the single web-crawl occurrence; neither carries a hiding bit). A caller passes a hiding
    /// value to exercise the refusal branch against the same otherwise-identical document.</para></summary>
    private static byte[] DocWithFlattenableAnnotationNoContents(int flags = 4)
    {
        var doc = new PdfDocument();

        var formDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Form"),
            [new PdfName("BBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(150)),
        };
        byte[] formContent = Encoding.ASCII.GetBytes("1 0 0 rg 20 20 160 110 re f");
        doc.AddObject(20, 0, new PdfStream(formDict, formContent));

        var threeDD = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("3DRef"),
            [new PdfName("Subtype")] = new PdfName("U3D"),
        };
        doc.AddObject(30, 0,
            new PdfStream(threeDD, "not really 3D model data, just something to be dropped"u8.ToArray()));

        var annot = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("3D"),
            [new PdfName("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(150)),
            [new PdfName("F")] = new PdfInteger(flags),
            [new PdfName("AP")] = new PdfDictionary
            {
                [new PdfName("N")] = new PdfIndirectReference(20, 0),
            },
            [new PdfName("3DD")] = new PdfIndirectReference(30, 0),
        };
        doc.AddObject(10, 0, annot);

        var page = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Parent")] = new PdfIndirectReference(2, 0),
            [new PdfName("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(150)),
            [new PdfName("Annots")] = new PdfArray(new PdfIndirectReference(10, 0)),
            // Deliberately NO /Contents -- the measured production shape: "the page draws nothing
            // of its own" (spec §2).
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)),
            [new PdfName("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Pages")] = new PdfIndirectReference(2, 0),
        });
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);

        using var ms = new MemoryStream();
        doc.Edit().Save(ms);
        return ms.ToArray();
    }

    /// <summary>Renders <paramref name="page"/> at native (72 DPI) scale and returns its raw pixel
    /// bytes -- not a re-encoded format, to keep the comparison free of any encoder-level
    /// nondeterminism (metadata, compression level) that a PNG round-trip could introduce.</summary>
    private static byte[] RenderPixels(PdfPage page)
    {
        using SKImage image = page.RenderTo().ToImage();
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }

    [Fact]
    public void Repair_bakes_the_candidate_and_removes_it_from_Annots()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        AnnotationTypeRepair applied = Assert.Single(report.Applied);
        Assert.Equal(annotRef.ObjectNumber, applied.ObjectNumber);
        Assert.Equal("3D", applied.Subtype);
        Assert.Equal(0, applied.PageIndex);
        Assert.Empty(report.Refused);

        // MakeFormXObject's /BBox [0 0 100 100] equals MakeAnnotation's default /Rect exactly, so
        // AA is the identity (translate-by-zero) -- the corpus's own shape.
        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        string content = DecodedPageContentText(doc, page);
        Assert.Contains("1 0 0 1 0 0 cm", content, StringComparison.Ordinal);
        Assert.Contains(" Do", content, StringComparison.Ordinal);

        Assert.Null(page.Get("Annots")); // the only annotation on this page -- key dropped, not just emptied
    }

    [Fact]
    public void Repair_applies_only_to_the_staged_set_leaving_the_other_a_candidate()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary annot1 = MakeAnnotation("3D");
        annot1[new PdfName("AP")] = new PdfDictionary
        {
            [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()),
        };
        PdfIndirectReference ref1 = doc.RegisterObject(annot1);
        AddAnnotEntry(doc, 0, ref1);

        PdfDictionary annot2 = MakeAnnotation("Sound");
        annot2[new PdfName("AP")] = new PdfDictionary
        {
            [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()),
        };
        PdfIndirectReference ref2 = doc.RegisterObject(annot2);
        AddAnnotEntry(doc, 0, ref2);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { ref1.ObjectNumber });

        Assert.Equal(ref1.ObjectNumber, Assert.Single(report.Applied).ObjectNumber);
        Assert.Empty(report.Refused);

        // The untouched candidate is still on the page and still classifies as a candidate.
        PdfArray? annots = PageTreeOps.PageDicts(doc)[0].Get("Annots") as PdfArray;
        Assert.NotNull(annots);
        PdfIndirectReference survivor = Assert.IsType<PdfIndirectReference>(Assert.Single(annots!));
        Assert.Equal(ref2.ObjectNumber, survivor.ObjectNumber);

        AnnotationTypeRepairCandidate stillCandidate =
            Assert.Single(editor.PreviewAnnotationTypeRepairs().Candidates);
        Assert.Equal(ref2.ObjectNumber, stillCandidate.ObjectNumber);
    }

    // The enumerator hazard this guards: EnumerateIndirectAnnotations is a generator that walks a
    // page's live /Annots array while yielding from it. If RepairAnnotationTypes mutated that array
    // (removing the first candidate) before the generator finished walking the SAME page's remaining
    // entries, the still-in-flight List<T> enumerator underneath /Annots would throw -- or silently
    // skip an annotation. Two staged candidates on one page is the minimal fixture that would catch
    // a regression back to iterate-and-mutate.
    [Fact]
    public void Repair_of_two_staged_candidates_on_the_same_page_does_not_throw_and_applies_both()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary annot1 = MakeAnnotation("3D");
        annot1[new PdfName("AP")] = new PdfDictionary
        {
            [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()),
        };
        PdfIndirectReference ref1 = doc.RegisterObject(annot1);
        AddAnnotEntry(doc, 0, ref1);

        PdfDictionary annot2 = MakeAnnotation("Sound");
        annot2[new PdfName("AP")] = new PdfDictionary
        {
            [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()),
        };
        PdfIndirectReference ref2 = doc.RegisterObject(annot2);
        AddAnnotEntry(doc, 0, ref2);

        AnnotationTypeRepairReport report = editor.RepairAnnotationTypes(
            new HashSet<int> { ref1.ObjectNumber, ref2.ObjectNumber });

        Assert.Equal(2, report.Applied.Count);
        Assert.Empty(report.Refused);
        Assert.Null(PageTreeOps.PageDicts(doc)[0].Get("Annots")); // both removed -- key dropped
    }

    [Fact]
    public void Repair_gives_a_page_with_no_Contents_its_first_content_stream_carrying_the_bake()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;
        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        page.Remove(new PdfName("Contents")); // the production shape: no /Contents at all
        Assert.Null(page.Get("Contents"));

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        Assert.Single(report.Applied);
        Assert.NotNull(page.Get("Contents"));
        string content = DecodedPageContentText(doc, page);
        Assert.Contains(" Do", content, StringComparison.Ordinal);
        Assert.Contains("cm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_leaves_Annots_present_when_a_non_finding_entry_remains()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary candidate = MakeAnnotation("3D");
        candidate[new PdfName("AP")] = new PdfDictionary
        {
            [new PdfName("N")] = doc.RegisterObject(MakeFormXObject()),
        };
        PdfIndirectReference candidateRef = doc.RegisterObject(candidate);
        AddAnnotEntry(doc, 0, candidateRef);

        PdfDictionary link = MakeAnnotation("Link"); // permitted subtype -- never a finding
        PdfIndirectReference linkRef = doc.RegisterObject(link);
        AddAnnotEntry(doc, 0, linkRef);

        editor.RepairAnnotationTypes(new HashSet<int> { candidateRef.ObjectNumber });

        PdfArray? annots = PageTreeOps.PageDicts(doc)[0].Get("Annots") as PdfArray;
        Assert.NotNull(annots);
        PdfIndirectReference survivor = Assert.IsType<PdfIndirectReference>(Assert.Single(annots!));
        Assert.Equal(linkRef.ObjectNumber, survivor.ObjectNumber);
    }

    [Fact]
    public void Repair_refuses_a_candidate_whose_ComputeAA_is_degenerate_and_leaves_it_untouched()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;
        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        page.Remove(new PdfName("Contents")); // so "nothing was touched" is easy to see

        // Zero-width BBox -- AppearancePlacement.ComputeAA returns null for this.
        PdfArray degenerateBbox = new(
            new PdfInteger(10), new PdfInteger(10), new PdfInteger(10), new PdfInteger(50));
        PdfIndirectReference formRef =
            doc.RegisterObject(MakeFormXObjectWithGeometry(degenerateBbox));
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        Assert.Empty(report.Applied);
        AnnotationTypeRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(annotRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Contains("degenerate", refusal.Reason, StringComparison.OrdinalIgnoreCase);

        // Nothing about the page was touched: no /Contents materialized, annotation still present.
        Assert.Null(page.Get("Contents"));
        PdfArray? annots = page.Get("Annots") as PdfArray;
        Assert.NotNull(annots);
        Assert.Single(annots!);
    }

    [Fact]
    public void Repair_refuses_a_candidate_whose_Rect_is_missing()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var annot = new PdfDictionary(); // no /Rect at all
        annot[new PdfName("Subtype")] = new PdfName("3D");
        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        Assert.Empty(report.Applied);
        AnnotationTypeRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("/Rect", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_refuses_a_candidate_whose_BBox_is_missing()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObjectWithGeometry(bbox: null));
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        Assert.Empty(report.Applied);
        AnnotationTypeRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("/BBox", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_refuses_a_candidate_whose_Matrix_is_present_but_malformed()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        // /Matrix present but only 2 elements -- malformed, not "absent" (which would default to
        // identity). Reuses MakeFormXObjectWithGeometry's default BBox by passing it explicitly.
        PdfArray bbox = new(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100));
        PdfArray badMatrix = new(new PdfInteger(1), new PdfInteger(0));
        PdfIndirectReference formRef =
            doc.RegisterObject(MakeFormXObjectWithGeometry(bbox, badMatrix));
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        Assert.Empty(report.Applied);
        AnnotationTypeRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("/Matrix", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Repair_defaults_an_absent_Matrix_to_identity()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        // BBox [0 0 200 100] onto Rect [10 20 210 120] (same size, offset origin): AA should be a
        // pure translate (identity scale), proving /Matrix defaulted to identity rather than refusing.
        PdfArray bbox = new(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(200), new PdfInteger(100));
        PdfArray rect = new(
            new PdfInteger(10), new PdfInteger(20), new PdfInteger(210), new PdfInteger(120));
        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObjectWithGeometry(bbox));
        PdfDictionary annot = MakeAnnotation("3D", rect);
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });

        Assert.Single(report.Applied);
        Assert.Empty(report.Refused);
        string content = DecodedPageContentText(doc, PageTreeOps.PageDicts(doc)[0]);
        Assert.Contains("1 0 0 1 10 20 cm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void The_3DD_stream_is_unreachable_after_repair()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference threeDDRef =
            doc.RegisterObject(new PdfStream(new PdfDictionary(), "model bytes"u8.ToArray()));
        PdfIndirectReference formRef = doc.RegisterObject(MakeFormXObject());
        PdfDictionary annot = MakeAnnotation("3D");
        annot[new PdfName("AP")] = new PdfDictionary { [new PdfName("N")] = formRef };
        annot[new PdfName("3DD")] = threeDDRef;
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        // Before the repair, /3DD IS reachable (from Root -> Pages -> Page -> Annots -> annot -> 3DD).
        Assert.Contains(threeDDRef.ObjectNumber, ObjectGraphWalker.CollectReachable(doc));

        AnnotationTypeRepairReport report =
            editor.RepairAnnotationTypes(new HashSet<int> { annotRef.ObjectNumber });
        Assert.Single(report.Applied);

        // The repair never touches /3DD directly -- this must fall out of the annotation itself
        // becoming unreachable once removed from /Annots, not an explicit deletion.
        HashSet<int> reachableAfter = ObjectGraphWalker.CollectReachable(doc);
        Assert.DoesNotContain(annotRef.ObjectNumber, reachableAfter);
        Assert.DoesNotContain(threeDDRef.ObjectNumber, reachableAfter);
    }

    [Fact]
    public void RenderInvariance_RepairDoesNotChangeTheRenderedPage()
    {
        byte[] pdf = DocWithFlattenableAnnotationNoContents();

        // Determinism control: two INDEPENDENT loads of the unmodified document must render
        // byte-identical pixels. If this ever failed, a divergence measured below could not be
        // attributed to the repair -- the renderer itself would not be a trustworthy oracle.
        using PdfDocument controlA = PdfDocument.Load(new MemoryStream(pdf));
        using PdfDocument controlB = PdfDocument.Load(new MemoryStream(pdf));
        byte[] renderControlA = RenderPixels(controlA.GetPage(0)!);
        byte[] renderControlB = RenderPixels(controlB.GetPage(0)!);
        Assert.Equal(renderControlA, renderControlB);

        // The actual claim: baking the appearance into page content and removing the annotation
        // must not change what the page renders -- the load-bearing proof for this whole program
        // (spec §10).
        using PdfDocument before = PdfDocument.Load(new MemoryStream(pdf));
        byte[] renderBefore = RenderPixels(before.GetPage(0)!);

        using PdfDocument after = PdfDocument.Load(new MemoryStream(pdf));
        PdfDocumentEditor editor = after.Edit();
        int annotNumber = Assert.Single(editor.PreviewAnnotationTypeRepairs().Candidates).ObjectNumber;
        AnnotationTypeRepairReport report = editor.RepairAnnotationTypes(new HashSet<int> { annotNumber });
        Assert.Single(report.Applied);
        Assert.Empty(report.Refused);

        byte[] renderAfter = RenderPixels(after.GetPage(0)!);
        Assert.Equal(renderBefore, renderAfter);
    }

    // The Round-trip-through-save gate this whole program's Definition of Done rests on: the repair
    // must survive being written out and reloaded, not just hold in-memory. Combines the DoD #4/#5
    // claims into one fixture -- the annotation is gone, the reloaded page renders pixel-identical to
    // the untouched original, and the saved file is materially smaller than an equivalent unrepaired
    // save (the /3DD payload did not survive the WRITER's own reachability walk -- not merely an
    // in-memory ObjectGraphWalker call, which The_3DD_stream_is_unreachable_after_repair already
    // covers). A byte-count comparison, rather than asserting a specific object number is absent from
    // the reloaded file, deliberately does not assume the serializer preserves object numbers
    // verbatim across a save/reload round trip -- nothing in this repair program requires that, and a
    // test should not require it either.
    [Fact]
    public void RoundTrip_SaveAndReload_AnnotationGone_FileShrinks_RenderUnchanged()
    {
        byte[] pdf = DocWithFlattenableAnnotationNoContents();

        using PdfDocument beforeDoc = PdfDocument.Load(new MemoryStream(pdf));
        byte[] renderBefore = RenderPixels(beforeDoc.GetPage(0)!);

        using PdfDocument unrepairedDoc = PdfDocument.Load(new MemoryStream(pdf));
        using var unrepairedSaved = new MemoryStream();
        unrepairedDoc.Edit().Save(unrepairedSaved); // baseline: same document, saved untouched

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfDocumentEditor editor = doc.Edit();
        int annotNumber = Assert.Single(editor.PreviewAnnotationTypeRepairs().Candidates).ObjectNumber;
        AnnotationTypeRepairReport report = editor.RepairAnnotationTypes(new HashSet<int> { annotNumber });
        Assert.Single(report.Applied);
        Assert.Empty(report.Refused);

        using var repairedSaved = new MemoryStream();
        editor.Save(repairedSaved); // RemoveOrphans defaults true -- this is what must drop /3DD

        Assert.True(repairedSaved.Length < unrepairedSaved.Length,
            $"repaired save ({repairedSaved.Length} bytes) should be smaller than an equivalent "
            + $"unrepaired save ({unrepairedSaved.Length} bytes) -- the /3DD stream should not have "
            + "survived the writer's reachability walk.");

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(repairedSaved.ToArray()));
        PdfPage reloadedPage = reloaded.GetPage(0) ?? throw new InvalidOperationException("page 0 missing");

        Assert.Null(reloadedPage.GetAnnotations()); // /Annots key gone -- the annotation is gone

        byte[] renderAfter = RenderPixels(reloadedPage);
        Assert.Equal(renderBefore, renderAfter);
    }
}
