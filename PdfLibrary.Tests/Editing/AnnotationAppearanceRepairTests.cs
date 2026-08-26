using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Tests for <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/> and
/// <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> -- both repairs of the PDF/A
/// clause 6.3.3 annotation-appearance remediation program
/// (<c>PdfLibrary.Conformance.Rules.AnnotationAppearanceRule</c>). R1 (6.3.3-t2, <c>:48-55</c>): a
/// widget's <c>/AP</c> dictionary that already validly contains <c>/N</c> loses the keys the rule
/// rejects (<c>/D</c>, <c>/R</c>) and nothing else. R2 (6.3.3-t1, <c>:37-46</c>, "WriteBlankAppearance"
/// region below): a value-less <c>/Tx</c>/<c>/Ch</c> widget with no <c>/AP</c> at all gains a blank
/// single-key <c>/AP /N</c> whose appearance stream is genuinely empty -- no font, no /Resources, no
/// document-level side effect (task 2 review, Criticals 3/4).</summary>
public sealed class AnnotationAppearanceRepairTests
{
    // ---- Fixture builders (mirrors AnnotationTypeRepairTests' convention) ----------------------

    private static PdfDocumentEditor NewEditor()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 72, 700, "Helvetica", 12));
        byte[] bytes = builder.ToByteArray();
        return PdfDocumentEditor.Open(new MemoryStream(bytes));
    }

    /// <summary>An AcroForm-bearing document whose /AcroForm /DR /Font holds a single NON-embedded
    /// standard-14 font (no /FontDescriptor) and whose /NeedAppearances starts true -- mirroring the
    /// 10 real corpus documents task 2 review Critical 3 measured. The plain <see cref="NewEditor"/>
    /// fixture has no /AcroForm at all, so a document-level side effect on /DR or /NeedAppearances is
    /// unreachable against it -- Criticals 3 and 4 were invisible to every test that used it (review
    /// Important 6).</summary>
    private static (PdfDocumentEditor Editor, PdfDocument Doc, PdfDictionary AcroForm, PdfDictionary Dr)
        NewEditorWithAcroForm()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var nonEmbeddedFont = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type1"),
            [new PdfName("BaseFont")] = new PdfName("Helvetica"),
            // deliberately no /FontDescriptor -- not embedded, exactly the shape ReferencedFontWalker
            // + FontEmbeddingRule would flag if a new /AP ever referenced it.
        };
        var fontDict = new PdfDictionary { [new PdfName("Helv")] = nonEmbeddedFont };
        var dr = new PdfDictionary { [new PdfName("Font")] = fontDict };
        var acroForm = new PdfDictionary
        {
            [new PdfName("DR")] = dr,
            [new PdfName("NeedAppearances")] = PdfBoolean.True,
        };

        PdfDictionary catalog = doc.CatalogDictionary!;
        catalog[new PdfName("AcroForm")] = acroForm;

        return (editor, doc, acroForm, dr);
    }

    private static void AddAnnotEntry(PdfDocument doc, int pageIndex, PdfObject entry)
    {
        PdfDictionary page = PageTreeOps.PageDicts(doc)[pageIndex];
        var name = new PdfName("Annots");
        if (page.Get(name) is PdfArray existing)
            existing.Add(entry);
        else
            page[name] = new PdfArray(entry);
    }

    /// <summary>A bare Widget annotation dictionary with a /Rect but no /AP -- callers add one.
    /// <paramref name="subtype"/> defaults to "Widget"; passing something else exercises the
    /// out-of-scope (non-widget) row.</summary>
    private static PdfDictionary MakeAnnotation(string subtype = "Widget") => new()
    {
        [new PdfName("Subtype")] = new PdfName(subtype),
        [new PdfName("Rect")] = new PdfArray(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(20), new PdfInteger(20)),
    };

    /// <summary>A minimal appearance-stream object -- content is irrelevant to R1, only that each
    /// call produces a distinct, independently resolvable object for /N, /D, or /R to point at.</summary>
    private static PdfStream MakeAppearanceStream(string marker) =>
        new(new PdfDictionary { [new PdfName("Subtype")] = new PdfName("Form") },
            Encoding.ASCII.GetBytes(marker));

    private static readonly PdfName ApKey = new("AP");
    private static readonly PdfName NKey = new("N");
    private static readonly PdfName DKey = new("D");
    private static readonly PdfName RKey = new("R");

    // ---- RepairAnnotationAppearances (the write side) --------------------------------------------

    [Fact]
    public void Repair_strips_D_and_keeps_N_byte_identical()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));

        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [DKey] = dRef, [NKey] = nRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(widgetRef.ObjectNumber, repair.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, Assert.Single(repair.Applied));
        Assert.Empty(report.Refused);

        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Single(appearance);
        Assert.False(appearance.ContainsKey(DKey));
        // /N must be the SAME resolved stream reference, not merely "a key named N" -- proves the
        // repair edited the dictionary in place rather than rebuilding /AP from scratch.
        var keptN = Assert.IsType<PdfIndirectReference>(appearance.Get(NKey));
        Assert.Equal(nRef.ObjectNumber, keptN.ObjectNumber);
    }

    [Fact]
    public void Repair_strips_R_too()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfIndirectReference rRef = doc.RegisterObject(MakeAppearanceStream("R"));

        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [RKey] = rRef, [NKey] = nRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, Assert.Single(repair.Applied));

        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Single(appearance);
        Assert.False(appearance.ContainsKey(RKey));
        var keptN = Assert.IsType<PdfIndirectReference>(appearance.Get(NKey));
        Assert.Equal(nRef.ObjectNumber, keptN.ObjectNumber);
    }

    [Fact]
    public void Repair_strips_both_D_and_R_together()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));
        PdfIndirectReference rRef = doc.RegisterObject(MakeAppearanceStream("R"));

        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [DKey] = dRef, [RKey] = rRef, [NKey] = nRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        editor.RepairAnnotationAppearances();

        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Single(appearance);
        Assert.True(appearance.ContainsKey(NKey));
    }

    [Fact]
    public void Repair_leaves_an_already_conforming_AP_untouched_no_gratuitous_rewrite()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfDictionary widget = MakeAnnotation();
        var appearance = new PdfDictionary { [NKey] = nRef };
        widget[ApKey] = appearance;
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
        // the SAME dictionary instance, unmutated -- proof there was no rewrite at all
        Assert.Same(appearance, widget.Get(ApKey));
        Assert.Single(appearance);
    }

    [Fact]
    public void Repair_refuses_an_AP_with_no_N_rather_than_fixing_it_by_deleting_everything()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));
        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [DKey] = dRef }; // no /N at all
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, refusal.Kind);
        Assert.Contains("/N", refusal.Reason, StringComparison.Ordinal);

        // NOT "fixed" by deleting /D -- the dictionary is untouched
        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.True(appearance.ContainsKey(DKey));
    }

    [Fact]
    public void Repair_refuses_a_stray_key_it_does_not_recognize_rather_than_a_blanket_delete()
    {
        // Design doc §8 "Over-broad R1" risk: only /D and /R are ever deleted -- an unmeasured third
        // key is left alone rather than wildcard-deleted as "everything that is not /N".
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [new PdfName("Zzz")] = PdfBoolean.True, [NKey] = nRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Contains("/Zzz", refusal.Reason, StringComparison.Ordinal);

        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Equal(2, appearance.Count);
        Assert.True(appearance.ContainsKey(new PdfName("Zzz")));
    }

    /// <summary>Task 1 review finding (fix round 1): the ORIGINAL classifier treated any appearance
    /// with a /D or /R as fully repairable regardless of what ELSE was present, so {/N, /D, /Zzz}
    /// stripped only /D and reported the object as Repaired while leaving a still-violating
    /// {/N, /Zzz} behind -- a false "fixed" claim. The repair must only be offered when EVERY other
    /// key besides /N is /D or /R; otherwise the whole dictionary is refused, matching how a lone
    /// unrecognized key already refused (the sibling test above).</summary>
    [Fact]
    public void Repair_refuses_a_mix_of_D_and_an_unrecognized_key_rather_than_a_partial_strip()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));
        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary
        {
            [DKey] = dRef, [new PdfName("Zzz")] = PdfBoolean.True, [NKey] = nRef,
        };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, refusal.Kind);
        Assert.Contains("/Zzz", refusal.Reason, StringComparison.Ordinal);

        // NOT partially stripped -- /D is still there too, exactly as the dictionary started
        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Equal(3, appearance.Count);
        Assert.True(appearance.ContainsKey(DKey));
        Assert.True(appearance.ContainsKey(new PdfName("Zzz")));
    }

    /// <summary>Final whole-branch review, I1: R1's guard (<c>recognizedKeyCount == appearance.Count</c>)
    /// only proves 6.3.3-t2 -- "stripping leaves exactly {/N}" -- it never inspects what /N actually
    /// is. <c>AnnotationAppearanceRule.cs:62-77</c> (t3) separately fires whenever the annotation is a
    /// Widget, its effective /FT is /Btn, and /N does not resolve to a non-empty
    /// <see cref="PdfDictionary"/>. So stripping /D from <c>{/N: &lt;bare stream&gt;, /D: &lt;stream&gt;}</c>
    /// on a /Btn widget would leave <c>{/N: &lt;stream&gt;}</c> behind -- t2-conformant, but STILL a t3
    /// violation -- which would falsify the <c>Repaired ⇒ fully 6.3.3-conformant</c> invariant this
    /// file's own doc comment (<c>AnnotationAppearanceRepair</c>, <c>:50-58</c>) asserts. This must be a
    /// refusal, not a repair.</summary>
    [Fact]
    public void Repair_refuses_a_Btn_widget_whose_N_is_a_bare_stream_rather_than_reporting_a_still_t3_violating_repair()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        // A bare appearance stream -- not a named-state subdictionary -- is exactly the shape 6.3.3-t3
        // rejects for a /Btn widget's /N.
        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));

        PdfDictionary widget = MakeAnnotation(); // Subtype Widget
        widget[new PdfName("FT")] = new PdfName("Btn");
        widget[ApKey] = new PdfDictionary { [DKey] = dRef, [NKey] = nRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, refusal.Kind);
        Assert.Contains("/N", refusal.Reason, StringComparison.Ordinal);

        // NOT stripped -- the dictionary is untouched, still {/D, /N}
        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Equal(2, appearance.Count);
        Assert.True(appearance.ContainsKey(DKey));
    }

    /// <summary>The positive counterpart: a /Btn widget whose /N genuinely IS a populated named-state
    /// subdictionary is still repaired -- the new t3 guard must not refuse every /Btn widget, only the
    /// bare-stream shape.</summary>
    [Fact]
    public void Repair_still_strips_D_for_a_Btn_widget_whose_N_is_a_populated_state_dictionary()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference offRef = doc.RegisterObject(MakeAppearanceStream("Off"));
        PdfIndirectReference yesRef = doc.RegisterObject(MakeAppearanceStream("Yes"));
        var states = new PdfDictionary { [new PdfName("Off")] = offRef, [new PdfName("Yes")] = yesRef };
        PdfIndirectReference nRef = doc.RegisterObject(states);
        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));

        PdfDictionary widget = MakeAnnotation();
        widget[new PdfName("FT")] = new PdfName("Btn");
        widget[ApKey] = new PdfDictionary { [DKey] = dRef, [NKey] = nRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(widgetRef.ObjectNumber, repair.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, Assert.Single(repair.Applied));
        Assert.Empty(report.Refused);

        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Single(appearance);
        Assert.True(appearance.ContainsKey(NKey));
    }

    [Fact]
    public void Repair_ignores_a_non_widget_annotation_with_the_same_shaped_defect()
    {
        // R1's measured population (design doc §2) is entirely /Widget; a non-widget annotation with
        // an identically-shaped {/D, /N} /AP is out of scope and must be left alone.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));
        PdfDictionary annot = MakeAnnotation("Square");
        annot[ApKey] = new PdfDictionary { [DKey] = dRef, [NKey] = nRef };
        PdfIndirectReference annotRef = doc.RegisterObject(annot);
        AddAnnotEntry(doc, 0, annotRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
        var appearance = (PdfDictionary)annot.Get(ApKey)!;
        Assert.Equal(2, appearance.Count);
        Assert.True(appearance.ContainsKey(DKey));
    }

    [Fact]
    public void Repair_applies_only_to_the_named_objects()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference n1 = doc.RegisterObject(MakeAppearanceStream("N1"));
        PdfIndirectReference d1 = doc.RegisterObject(MakeAppearanceStream("D1"));
        PdfDictionary widget1 = MakeAnnotation();
        widget1[ApKey] = new PdfDictionary { [DKey] = d1, [NKey] = n1 };
        PdfIndirectReference widget1Ref = doc.RegisterObject(widget1);
        AddAnnotEntry(doc, 0, widget1Ref);

        PdfIndirectReference n2 = doc.RegisterObject(MakeAppearanceStream("N2"));
        PdfIndirectReference d2 = doc.RegisterObject(MakeAppearanceStream("D2"));
        PdfDictionary widget2 = MakeAnnotation();
        widget2[ApKey] = new PdfDictionary { [DKey] = d2, [NKey] = n2 };
        PdfIndirectReference widget2Ref = doc.RegisterObject(widget2);
        AddAnnotEntry(doc, 0, widget2Ref);

        AnnotationAppearanceRepairReport report =
            editor.RepairAnnotationAppearances(new HashSet<int> { widget1Ref.ObjectNumber });

        Assert.Equal(widget1Ref.ObjectNumber, Assert.Single(report.Repaired).ObjectNumber);
        Assert.Single((PdfDictionary)widget1.Get(ApKey)!);
        // widget2 was not staged -- still carries /D
        Assert.True(((PdfDictionary)widget2.Get(ApKey)!).ContainsKey(DKey));
    }

    [Fact]
    public void Repair_with_null_filter_repairs_every_widget()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference n1 = doc.RegisterObject(MakeAppearanceStream("N1"));
        PdfIndirectReference d1 = doc.RegisterObject(MakeAppearanceStream("D1"));
        PdfDictionary widget1 = MakeAnnotation();
        widget1[ApKey] = new PdfDictionary { [DKey] = d1, [NKey] = n1 };
        AddAnnotEntry(doc, 0, doc.RegisterObject(widget1));

        PdfIndirectReference n2 = doc.RegisterObject(MakeAppearanceStream("N2"));
        PdfIndirectReference d2 = doc.RegisterObject(MakeAppearanceStream("D2"));
        PdfDictionary widget2 = MakeAnnotation();
        widget2[ApKey] = new PdfDictionary { [DKey] = d2, [NKey] = n2 };
        AddAnnotEntry(doc, 0, doc.RegisterObject(widget2));

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Equal(2, report.Repaired.Count);
        Assert.Single((PdfDictionary)widget1.Get(ApKey)!);
        Assert.Single((PdfDictionary)widget2.Get(ApKey)!);
    }

    // ---- PreviewAnnotationAppearanceRepairs (the read-only side) ---------------------------------

    [Fact]
    public void Preview_lists_a_strippable_AP_as_a_candidate_and_writes_nothing()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));
        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [DKey] = dRef, [NKey] = nRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairPreview preview = editor.PreviewAnnotationAppearanceRepairs();
        editor.PreviewAnnotationAppearanceRepairs(); // twice: no idempotency guard should trip

        AnnotationAppearanceRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(widgetRef.ObjectNumber, candidate.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, Assert.Single(candidate.WouldApply));
        Assert.Empty(preview.Refused);
        // nothing was written -- /D is still there
        Assert.True(((PdfDictionary)widget.Get(ApKey)!).ContainsKey(DKey));
    }

    [Fact]
    public void Preview_reports_the_same_refusal_the_write_side_would()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference dRef = doc.RegisterObject(MakeAppearanceStream("D"));
        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [DKey] = dRef };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairPreview preview = editor.PreviewAnnotationAppearanceRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationAppearanceRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.StripRejectedKeys, refusal.Kind);
    }

    [Fact]
    public void Preview_ignores_an_already_conforming_widget()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfDictionary widget = MakeAnnotation();
        widget[ApKey] = new PdfDictionary { [NKey] = nRef };
        AddAnnotEntry(doc, 0, doc.RegisterObject(widget));

        AnnotationAppearanceRepairPreview preview = editor.PreviewAnnotationAppearanceRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
    }

    // ---- WriteBlankAppearance (R2): blank appearance for a valueless /Tx or /Ch widget -----------

    private static readonly PdfName FtKey = new("FT");
    private static readonly PdfName VKey = new("V");
    private static readonly PdfName ParentKey = new("Parent");
    private static readonly PdfName FfKey = new("Ff");
    private static readonly PdfName OptKey = new("Opt");
    private const int ComboFlag = 1 << 17; // /Ff bit 18 (Table 230) -- 1-based, so bit 18 is 1<<17

    /// <summary>A /Tx or /Ch widget with a /Rect and its own /FT but no /AP and no /V -- the shape
    /// FormFieldTree.WalkField builds when a field has no /Kids (the field dict doubles as its own
    /// widget), and R2's most common target: everything the repair needs lives on one dict.</summary>
    private static PdfDictionary MakeBlankFieldWidget(string ft, int ff = 0) => new()
    {
        [new PdfName("Subtype")] = new PdfName("Widget"),
        [new PdfName("Rect")] = new PdfArray(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
        [FtKey] = new PdfName(ft),
        [FfKey] = new PdfInteger(ff),
    };

    [Fact]
    public void Repair_writes_a_blank_appearance_for_a_valueless_Tx_widget()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Tx");
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(widgetRef.ObjectNumber, repair.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, Assert.Single(repair.Applied));
        Assert.Empty(report.Refused);

        var appearance = Assert.IsType<PdfDictionary>(widget.Get(ApKey));
        Assert.Single(appearance);
        var nRef = Assert.IsType<PdfIndirectReference>(appearance.Get(NKey));
        var stream = Assert.IsType<PdfStream>(doc.GetObject(nRef.ObjectNumber));

        // A blank appearance draws no text: genuinely empty content (review Critical 3/4 fix), no
        // dangling font-setting operator, and no /Resources at all -- there is nothing to resource.
        Assert.Empty(stream.Data);
        Assert.False(stream.Dictionary.ContainsKey(new PdfName("Resources")));

        // R2's write is confined to the widget's own /AP -- it does not set /F (the Print flag is
        // the annotation-flags domain's key to own, not this repair's; review Important 5).
        Assert.Null(widget.Get(new PdfName("F")));
    }

    [Fact]
    public void Repair_writes_a_blank_appearance_for_a_valueless_Ch_combo_widget()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Ch", ComboFlag);
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, Assert.Single(repair.Applied));
        Assert.Empty(report.Refused);

        var appearance = Assert.IsType<PdfDictionary>(widget.Get(ApKey));
        Assert.Single(appearance);
        Assert.True(appearance.ContainsKey(NKey));
    }

    [Fact]
    public void Repair_writes_a_blank_appearance_for_a_Ch_list_widget_with_options()
    {
        // A /Ch widget with real /Opt entries still gets the same blank appearance -- R2's write
        // does not read /Opt at all (see WriteBlankAppearance's doc comment), so this and the
        // no-Opt sibling test below exercise the same code path; both are kept because "a /Ch
        // widget gets repaired" is worth pinning regardless of /Opt shape.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Ch"); // Ff=0 -> list box, not combo
        widget[OptKey] = new PdfArray(PdfString.FromText("Alpha"), PdfString.FromText("Beta"));
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, Assert.Single(repair.Applied));

        var appearance = Assert.IsType<PdfDictionary>(widget.Get(ApKey));
        Assert.Single(appearance);
        Assert.True(appearance.ContainsKey(NKey));
    }

    [Fact]
    public void Repair_writes_a_blank_appearance_for_a_Ch_list_widget_with_no_own_Opt()
    {
        // Task 2 review Critical 2's second crash trigger no longer exists: the original
        // implementation reused FieldAppearanceGenerator.RegenerateListField, which silently wrote
        // nothing for zero options, so ClassifyBlankAppearance's throw-on-no-write guard fired for
        // the ordinary shape of a list box authored with no /Opt. R2's write no longer depends on
        // /Opt (or combo/list shape) at all, so this is now just an ordinary repair, not a crash.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Ch"); // Ff=0 -> list box, no /Opt at all
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, Assert.Single(repair.Applied));
        Assert.True(((PdfDictionary)widget.Get(ApKey)!).ContainsKey(NKey));
    }

    [Fact]
    public void Repair_treats_a_widget_whose_V_is_inherited_from_Parent_as_value_bearing_not_blank()
    {
        // The sharpest correctness risk in the whole program (design doc §8): the widget carries NO
        // own /V at all, so reading only its own dict would (wrongly) treat it as blank. Its /Parent
        // field carries the real value, and /V MUST be resolved up /Parent exactly as
        // AnnotationAppearanceRule.EffectiveFieldType (:96-108) resolves /FT, or this repair would
        // silently overwrite a real value with an empty box -- the one way this program could
        // visibly falsify a document.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var fieldDict = new PdfDictionary
        {
            [FtKey] = new PdfName("Tx"),
            [VKey] = PdfString.FromText("Smith"),
        };
        PdfIndirectReference fieldRef = doc.RegisterObject(fieldDict);

        PdfDictionary widget = new()
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
            [ParentKey] = fieldRef,
            // deliberately no own /FT and no own /V -- both must be resolved via /Parent
        };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Contains("/V", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(widget.Get(ApKey)); // not written -- a blank appearance would have erased "Smith"
    }

    [Fact]
    public void Repair_treats_inherited_V_as_value_bearing_even_when_the_widget_has_its_own_FT()
    {
        // Isolates the /V walk from the /FT walk (task 2 review Minor finding): the widget carries
        // its own /FT, so /FT resolution never touches /Parent here, while /V still lives only on
        // /Parent. The refusal must still fire on the /V walk alone.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var fieldDict = new PdfDictionary { [VKey] = PdfString.FromText("Smith") }; // no /FT here
        PdfIndirectReference fieldRef = doc.RegisterObject(fieldDict);

        PdfDictionary widget = MakeBlankFieldWidget("Tx"); // widget carries its OWN /FT /Tx
        widget[ParentKey] = fieldRef; // /V lives only on Parent
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Contains("/V", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(widget.Get(ApKey));
    }

    [Fact]
    public void Repair_treats_a_direct_null_V_on_the_widget_as_omitted_and_inherits_Parent_V()
    {
        // Task 2 review Critical 1: ISO 32000-1 7.3.7 -- "specifying the null object as the value of
        // a dictionary entry shall be equivalent to omitting the entry." The widget's OWN /V key is
        // present but resolves to PdfNull; the walk must not stop there (key presence alone is not
        // enough) -- it must keep looking up /Parent and find "Smith".
        //
        // Final whole-branch review, I3: this pins a DEFENSIVE in-memory invariant, not a shape a real
        // parsed document can produce. PdfParser.cs:361 populates a dictionary via `dict[key] = value`,
        // and PdfDictionary.Set (:68-73) does `if (value is null or PdfNull) _entries.Remove(key)` --
        // so a parsed "/V null" is never actually stored; the walk never even sees a present-but-null
        // key from parsing. The sibling test below (a dangling indirect reference) is the one that
        // carries the real-document weight: a parsed "/V 12 0 R" pointing at an unregistered object IS
        // stored as a present key, and only resolves to null later, on lookup. This test still earns
        // its place as a defensive regression pin against a future in-memory construction path (a
        // programmatic writer, not the parser) producing the same shape.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var fieldDict = new PdfDictionary
        {
            [FtKey] = new PdfName("Tx"),
            [VKey] = PdfString.FromText("Smith"),
        };
        PdfIndirectReference fieldRef = doc.RegisterObject(fieldDict);

        PdfDictionary widget = new()
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
            [ParentKey] = fieldRef,
        };
        // NOT widget[VKey] = PdfNull.Instance -- PdfDictionary's indexer setter (Set()) treats
        // PdfNull the same as removing the key entirely, so that would silently produce "no /V key"
        // rather than "a /V key present and pointing at null", testing nothing. Add() bypasses that
        // stripping and genuinely stores the entry -- a shape no real parse actually produces (see
        // this test's own doc comment above). A parsed "0 0 R" (PdfParser.cs:176-184) also collapses
        // straight to PdfNull.Instance at parse time -- as a VALUE handed to `dict[key] = value`,
        // which Set() then strips right back out, same as a direct "/V null" -- so it is likewise
        // never reachable through the parser. Only a dangling reference to a NON-zero, never-registered
        // object number (the sibling test below) survives parsing as a present key.
        widget.Add(VKey, PdfNull.Instance);
        Assert.IsType<PdfNull>(widget.Get(VKey)); // fixture sanity: the key really is present
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Contains("/V", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(widget.Get(ApKey)); // not written -- would have erased "Smith"
    }

    [Fact]
    public void Repair_treats_an_indirect_V_that_resolves_to_nothing_as_omitted_and_inherits_Parent_V()
    {
        // The same ISO 32000-1 7.3.7 rule reached through an indirect reference rather than a direct
        // null -- and, unlike the sibling test above, THIS is the shape a real parsed document
        // actually produces (final whole-branch review, I3): a "/V 12 0 R" pointing at an object
        // number that is never registered (deleted, or simply absent from a malformed xref) parses
        // to a genuine PdfIndirectReference -- not PdfNull -- so `dict[key] = value` stores it as a
        // present, non-null key. It only resolves to a C# null LATER, on lookup, through
        // PdfDocument.GetObject failing to find that object number. (A literal object number 0 --
        // the coordinator's "0 0 R" -- collapses to PdfNull.Instance AT PARSE TIME instead, per
        // PdfParser.cs:176-184, and so is the OTHER, parser-unreachable shape the sibling test above
        // pins defensively; PdfIndirectReference also requires a positive object number, so it cannot
        // be constructed directly either. An out-of-range, never-registered POSITIVE number, as used
        // below, is what stands in for a genuine dangling reference.)
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var fieldDict = new PdfDictionary
        {
            [FtKey] = new PdfName("Tx"),
            [VKey] = PdfString.FromText("Smith"),
        };
        PdfIndirectReference fieldRef = doc.RegisterObject(fieldDict);

        PdfDictionary widget = new()
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
            [ParentKey] = fieldRef,
            [VKey] = new PdfIndirectReference(9999, 0), // never registered -- resolves to null
        };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Contains("/V", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(widget.Get(ApKey));
    }

    [Fact]
    public void Repair_treats_an_inherited_blank_V_from_Parent_as_blank()
    {
        // The positive counterpart: the field carries no /V at all (a genuinely blank field), and the
        // widget must still be repaired even though /FT and /V both live only on /Parent.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var fieldDict = new PdfDictionary { [FtKey] = new PdfName("Tx") }; // no /V at all
        PdfIndirectReference fieldRef = doc.RegisterObject(fieldDict);

        PdfDictionary widget = new()
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
            [ParentKey] = fieldRef,
        };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(widgetRef.ObjectNumber, repair.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, Assert.Single(repair.Applied));
        Assert.True(((PdfDictionary)widget.Get(ApKey)!).ContainsKey(NKey));
    }

    /// <summary>Final whole-branch review, M2: exercises <see cref="PdfDocumentEditor.IsEffectivelyBlank"/>'s
    /// <c>PdfString s => s.GetText().Length == 0</c> branch directly -- an empty string, not an absent
    /// key or an inherited blank. This is the branch the spec's §2 correction turns on (design doc §5:
    /// the 21 "value-bearing" widgets in `CACI CBP Form 78` turned out to inherit an EMPTY string, not
    /// a real value) yet no test constructed one directly before this. A /Tx widget with an own
    /// <c>/V ()</c> and no /AP must be repaired, not refused.</summary>
    [Fact]
    public void Repair_writes_a_blank_appearance_for_a_Tx_widget_whose_own_V_is_an_empty_string()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Tx");
        widget[VKey] = PdfString.FromText(""); // own /V present, resolves to a real but EMPTY string
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        AnnotationAppearanceRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(widgetRef.ObjectNumber, repair.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, Assert.Single(repair.Applied));
        Assert.Empty(report.Refused);

        var appearance = Assert.IsType<PdfDictionary>(widget.Get(ApKey));
        Assert.Single(appearance);
        Assert.True(appearance.ContainsKey(NKey));
    }

    [Fact]
    public void Repair_refuses_a_Tx_widget_whose_own_V_is_non_empty()
    {
        // The deferred value-bearing case (design doc §3 "Out"): out of scope, and must be REFUSED --
        // never silently skipped.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Tx");
        widget[VKey] = PdfString.FromText("already filled in");
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Contains("/V", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(widget.Get(ApKey));
    }

    [Fact]
    public void Repair_leaves_a_Tx_widget_with_existing_AP_N_untouched()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference nRef = doc.RegisterObject(MakeAppearanceStream("N"));
        PdfDictionary widget = MakeBlankFieldWidget("Tx");
        var appearance = new PdfDictionary { [NKey] = nRef };
        widget[ApKey] = appearance;
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
        Assert.Same(appearance, widget.Get(ApKey));
    }

    [Fact]
    public void Repair_scopes_WriteBlankAppearance_to_only_the_named_widget_not_a_sibling_of_the_same_field()
    {
        // Two widgets (e.g. the same field shown on two pages) share one field dict. Repairing only
        // widgetA must not touch widgetB -- WriteBlankAppearance touches only the one annotation it
        // is called with, never a sibling widget of the same field.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var fieldDict = new PdfDictionary { [FtKey] = new PdfName("Tx") };
        PdfIndirectReference fieldRef = doc.RegisterObject(fieldDict);

        PdfDictionary MakeKid() => new()
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
            [ParentKey] = fieldRef,
        };

        PdfDictionary widgetA = MakeKid();
        PdfIndirectReference widgetARef = doc.RegisterObject(widgetA);
        AddAnnotEntry(doc, 0, widgetARef);

        PdfDictionary widgetB = MakeKid();
        PdfIndirectReference widgetBRef = doc.RegisterObject(widgetB);
        AddAnnotEntry(doc, 0, widgetBRef);

        AnnotationAppearanceRepairReport report =
            editor.RepairAnnotationAppearances(new HashSet<int> { widgetARef.ObjectNumber });

        Assert.Equal(widgetARef.ObjectNumber, Assert.Single(report.Repaired).ObjectNumber);
        Assert.True(((PdfDictionary)widgetA.Get(ApKey)!).ContainsKey(NKey));
        Assert.Null(widgetB.Get(ApKey)); // untouched -- was not in the requested set
    }

    [Fact]
    public void Repair_refuses_a_widget_with_no_Rect_rather_than_crashing_mid_document()
    {
        // Task 2 review Critical 2: AnnotationAppearanceRule.cs:39-41 says a missing/malformed /Rect
        // is explicitly NOT treated as zero-sized, so the appearance stays required -- this widget IS
        // a genuine 6.3.3-t1 finding, just one this repair cannot safely build a bounding box for.
        // Classification must catch this BEFORE the write; the original implementation let
        // WriteBlankAppearance throw mid-RepairAnnotationAppearances instead, which would abort the
        // whole document's repair with any earlier widgets already mutated and no report returned at
        // all -- an exception is neither Repaired nor Refused, violating the Global Constraint.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var widget = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [FtKey] = new PdfName("Tx"),
            // no /Rect at all
        };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Contains("/Rect", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(widget.Get(ApKey));
    }

    [Fact]
    public void Repair_refuses_a_widget_with_a_malformed_Rect_rather_than_crashing()
    {
        // The other half of Critical 2's first trigger: /Rect is present but not a usable 4-element
        // array.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var widget = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [FtKey] = new PdfName("Tx"),
            [new PdfName("Rect")] = new PdfArray(new PdfInteger(0), new PdfInteger(0)), // only 2 elements
        };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Empty(report.Repaired);
        AnnotationAppearanceRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Null(widget.Get(ApKey));
    }

    [Fact]
    public void Repair_does_not_write_a_font_or_touch_AcroForm_DR_for_a_blank_appearance()
    {
        // Task 2 review Critical 3: the original implementation reused
        // FieldAppearanceGenerator.Regenerate wholesale, which resolved (and, absent a usable /DR
        // entry, synthesized) a font and wrote it into /AcroForm /DR /Font -- ReferencedFontWalker
        // then sees that font from the widget's own /AP, and FontEmbeddingRule raises a NEW
        // 6.2.11.4.1 finding on a document this repair is supposed to be improving. A blank
        // appearance draws no text, so it must carry no font resource and /DR must be untouched.
        (PdfDocumentEditor editor, PdfDocument doc, PdfDictionary acroForm, PdfDictionary dr) =
            NewEditorWithAcroForm();

        PdfDictionary widget = MakeBlankFieldWidget("Tx");
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        editor.RepairAnnotationAppearances();

        // /AcroForm /DR is the SAME dictionary instance afterward -- not merely equal in content, but
        // never even re-fetched-and-rewritten.
        Assert.Same(dr, acroForm.Get(new PdfName("DR")));
        var fontDict = (PdfDictionary)dr.Get(new PdfName("Font"))!;
        Assert.Single(fontDict); // still only the one pre-existing (non-embedded) font -- none added

        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        var nRef = (PdfIndirectReference)appearance.Get(NKey)!;
        var stream = (PdfStream)doc.GetObject(nRef.ObjectNumber)!;
        Assert.False(stream.Dictionary.ContainsKey(new PdfName("Resources")));
    }

    [Fact]
    public void Repair_does_not_flip_NeedAppearances_leaving_a_refused_sibling_widget_renderable()
    {
        // Task 2 review Critical 4: FieldAppearanceGenerator.Regenerate calls
        // SetNeedAppearancesFalse(doc), a document-level mutation from a call scoped to one object
        // number. A refused widget (no /AP) in the same AcroForm relies on /NeedAppearances staying
        // true so a viewer keeps synthesizing its appearance on the fly; flipping it false would
        // erase that widget's real value by a side channel -- the exact harm the refusal exists to
        // prevent.
        (PdfDocumentEditor editor, PdfDocument doc, PdfDictionary acroForm, PdfDictionary _) =
            NewEditorWithAcroForm();

        PdfDictionary blankWidget = MakeBlankFieldWidget("Tx");
        PdfIndirectReference blankRef = doc.RegisterObject(blankWidget);
        AddAnnotEntry(doc, 0, blankRef);

        PdfDictionary valueWidget = MakeBlankFieldWidget("Tx");
        valueWidget[VKey] = PdfString.FromText("already filled in");
        PdfIndirectReference valueRef = doc.RegisterObject(valueWidget);
        AddAnnotEntry(doc, 0, valueRef);

        AnnotationAppearanceRepairReport report = editor.RepairAnnotationAppearances();

        Assert.Single(report.Repaired);
        Assert.Single(report.Refused);
        // Same singleton reference as the fixture set -- the key was never even re-assigned.
        Assert.Same(PdfBoolean.True, acroForm.Get(new PdfName("NeedAppearances")));
        Assert.Null(valueWidget.Get(ApKey)); // the refused widget still has no /AP of its own
    }

    [Fact]
    public void Preview_lists_a_valueless_Tx_widget_as_a_WriteBlankAppearance_candidate_and_writes_nothing()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Tx");
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairPreview preview = editor.PreviewAnnotationAppearanceRepairs();
        editor.PreviewAnnotationAppearanceRepairs(); // twice: no idempotency guard should trip

        AnnotationAppearanceRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(widgetRef.ObjectNumber, candidate.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, Assert.Single(candidate.WouldApply));
        Assert.Empty(preview.Refused);
        Assert.Null(widget.Get(ApKey)); // nothing was written
    }

    [Fact]
    public void Preview_reports_the_same_refusal_the_write_side_would_for_a_value_bearing_widget()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeBlankFieldWidget("Tx");
        widget[VKey] = PdfString.FromText("already filled in");
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);

        AnnotationAppearanceRepairPreview preview = editor.PreviewAnnotationAppearanceRepairs();

        Assert.Empty(preview.Candidates);
        AnnotationAppearanceRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(widgetRef.ObjectNumber, refusal.ObjectNumber);
        Assert.Equal(AnnotationAppearanceRepairKind.WriteBlankAppearance, refusal.Kind);
        Assert.Contains("/V", refusal.Reason, StringComparison.Ordinal);
    }
}
