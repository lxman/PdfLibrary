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
/// single-key <c>/AP /N</c>.</summary>
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
        string content = Encoding.ASCII.GetString(stream.Data);
        Assert.Contains("() Tj", content, StringComparison.Ordinal); // the shown text is the empty string
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
    public void Repair_writes_a_blank_appearance_for_a_valueless_Ch_list_widget_with_options()
    {
        // A list box (not combo) draws its full option list regardless of selection. The design doc's
        // measured blank-/Ch population is zero, but the write path must not silently no-op for the
        // shape that DOES occur in real documents: a real list box with real options.
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
        Assert.Null(widget.Get(ApKey)); // not written -- a blank appearance would have erased "Smith"
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
        // widgetA must not touch widgetB -- FieldAppearanceGenerator.Regenerate would happily write
        // to every widget under a field built by FormFieldTree, so R2's field VIEW must scope
        // WidgetDicts to just the requested object, not delegate to the full multi-widget field.
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
    public void Repair_throws_rather_than_silently_reporting_a_widget_it_could_not_actually_write()
    {
        // Classification only checks /FT and /V, not /Rect -- a widget with no /Rect at all is not
        // in the measured population, but FieldAppearanceGenerator silently skips writing anything
        // for one rather than crashing. WriteBlankAppearance must not swallow that mismatch as a
        // false "Repaired".
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

        Assert.Throws<InvalidOperationException>(() => editor.RepairAnnotationAppearances());
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
    }
}
