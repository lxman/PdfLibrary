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
    /// page, 10 the annotation, 20 the Form XObject, 30 the /3DD stream.</summary>
    private static byte[] DocWithFlattenableAnnotationNoContents()
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
            [new PdfName("F")] = new PdfInteger(4), // Print -- not Hidden, not NoView
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
