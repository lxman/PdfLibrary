using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Tests for <see cref="PdfDocumentEditor.RepairAnnotationAppearances"/> and
/// <see cref="PdfDocumentEditor.PreviewAnnotationAppearanceRepairs"/> -- R1 of the PDF/A clause 6.3.3
/// annotation-appearance remediation program (<c>PdfLibrary.Conformance.Rules.AnnotationAppearanceRule</c>,
/// 6.3.3-t2, <c>:48-55</c>): a widget's <c>/AP</c> dictionary that already validly contains <c>/N</c>
/// loses the keys the rule rejects (<c>/D</c>, <c>/R</c>) and nothing else. R2 (a later task, writing a
/// blank appearance for a value-less <c>/Tx</c>/<c>/Ch</c> widget) is out of scope here.</summary>
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

        var appearance = (PdfDictionary)widget.Get(ApKey)!;
        Assert.Equal(2, appearance.Count);
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
}
