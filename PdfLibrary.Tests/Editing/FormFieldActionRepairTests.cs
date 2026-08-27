using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class FormFieldActionRepairTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    private static Finding[] Findings(PdfDocument document) =>
        [.. new FormFieldActionsRule().Check(new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static PdfDocument Document(
        PdfDictionary widget,
        PdfDictionary? field = null,
        Action<PdfDocument, PdfDictionary>? configure = null)
    {
        var document = new PdfDocument();
        document.AddObject(4, 0, widget);
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
            [N("Annots")] = new PdfArray(Ref(4)),
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        if (field is not null)
        {
            document.AddObject(5, 0, field);
            catalog[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(5)) };
        }
        configure?.Invoke(document, catalog);
        document.AddObject(1, 0, catalog);
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfDictionary Widget(PdfObject? action = null, PdfObject? additional = null)
    {
        var widget = new PdfDictionary { [N("Type")] = N("Annot"), [N("Subtype")] = N("Widget") };
        if (action is not null) widget[N("A")] = action;
        if (additional is not null) widget[N("AA")] = additional;
        return widget;
    }

    [Fact]
    public void Widget_action_repair_removes_only_the_host_entry_and_survives_round_trip()
    {
        var action = new PdfDictionary { [N("S")] = N("URI") };
        using PdfDocument document = Document(Widget(action: Ref(10)), configure: (doc, _) => doc.AddObject(10, 0, action));
        var editor = new PdfDocumentEditor(document);

        FormFieldActionRepairCandidate candidate = Assert.Single(editor.PreviewFormFieldActionRepairs().Candidates);
        Assert.True(candidate.RemovesAction);
        Assert.False(candidate.RemovesAdditionalActions);
        Assert.Single(editor.RepairFormFieldActions(new HashSet<int> { 4 }).Repaired);
        Assert.Null(((PdfDictionary)document.Objects[4]).Get("A"));
        Assert.Same(action, document.Objects[10]);
        Assert.Empty(Findings(document));

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.Empty(Findings(reloaded));
    }

    [Fact]
    public void Merged_field_widget_is_one_candidate_and_one_removal_closes_both_findings()
    {
        PdfDictionary merged = Widget(additional: new PdfDictionary());
        using PdfDocument document = Document(merged, configure: (_, catalog) =>
            catalog[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(4)) });
        var editor = new PdfDocumentEditor(document);

        Assert.Equal(2, Findings(document).Length);
        FormFieldActionRepairCandidate candidate = Assert.Single(editor.PreviewFormFieldActionRepairs().Candidates);
        Assert.Equal(FormFieldActionOwnerKind.MergedWidgetField, candidate.OwnerKind);
        Assert.Single(editor.RepairFormFieldActions(new HashSet<int> { 4 }).Repaired);
        Assert.Empty(Findings(document));
        Assert.Empty(editor.RepairFormFieldActions().Repaired);
    }

    [Fact]
    public void Pure_field_additional_actions_are_repaired_without_touching_unrelated_widget()
    {
        var field = new PdfDictionary { [N("FT")] = N("Tx"), [N("AA")] = new PdfDictionary() };
        using PdfDocument document = Document(Widget(), field);
        var editor = new PdfDocumentEditor(document);

        FormFieldActionRepairCandidate candidate = Assert.Single(editor.PreviewFormFieldActionRepairs().Candidates);
        Assert.Equal(5, candidate.ObjectNumber);
        Assert.Equal(FormFieldActionOwnerKind.Field, candidate.OwnerKind);
        Assert.Single(editor.RepairFormFieldActions(new HashSet<int> { 5 }).Repaired);
        Assert.Empty(Findings(document));
    }

    [Fact]
    public void Direct_widget_is_refused_as_addressless()
    {
        using PdfDocument document = Document(Widget());
        PdfDictionary page = (PdfDictionary)document.Objects[3];
        page[N("Annots")] = new PdfArray(Widget(action: new PdfDictionary { [N("S")] = N("URI") }));
        var editor = new PdfDocumentEditor(document);

        FormFieldActionRepairPreview preview = editor.PreviewFormFieldActionRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Null(Assert.Single(preview.Refused).ObjectNumber);
        Assert.Contains("direct", preview.Refused[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Removing_a_shared_AA_host_entry_does_not_mutate_the_shared_dictionary_or_other_host()
    {
        var shared = new PdfDictionary { [N("E")] = new PdfDictionary { [N("S")] = N("URI") } };
        using PdfDocument document = Document(Widget(additional: Ref(10)), configure: (doc, _) =>
        {
            doc.AddObject(10, 0, shared);
            var page = (PdfDictionary)doc.Objects[3];
            doc.AddObject(6, 0, new PdfDictionary
            {
                [N("Type")] = N("Annot"), [N("Subtype")] = N("Link"), [N("AA")] = Ref(10),
            });
            page[N("Annots")] = new PdfArray(Ref(4), Ref(6));
        });
        var editor = new PdfDocumentEditor(document);

        Assert.Single(editor.RepairFormFieldActions(new HashSet<int> { 4 }).Repaired);
        Assert.Null(((PdfDictionary)document.Objects[4]).Get("AA"));
        Assert.NotNull(((PdfDictionary)document.Objects[6]).Get("AA"));
        Assert.Same(shared, document.Objects[10]);
        Assert.True(shared.ContainsKey(N("E")));
    }

    [Fact]
    public void Exact_selection_repairs_only_the_selected_host()
    {
        using PdfDocument document = Document(Widget(action: new PdfDictionary { [N("S")] = N("URI") }),
            configure: (doc, _) =>
            {
                doc.AddObject(6, 0, Widget(additional: new PdfDictionary()));
                ((PdfDictionary)doc.Objects[3])[N("Annots")] = new PdfArray(Ref(4), Ref(6));
            });
        var editor = new PdfDocumentEditor(document);

        FormFieldActionRepairReport report = editor.RepairFormFieldActions(new HashSet<int> { 6 });
        Assert.Equal(6, Assert.Single(report.Repaired).ObjectNumber);
        Assert.NotNull(((PdfDictionary)document.Objects[4]).Get("A"));
        Assert.Null(((PdfDictionary)document.Objects[6]).Get("AA"));
        Assert.Single(Findings(document));
    }

    [Fact]
    public void Signed_signature_field_refuses_every_candidate()
    {
        var signature = new PdfDictionary { [N("FT")] = N("Sig"), [N("V")] = new PdfDictionary() };
        using PdfDocument document = Document(
            Widget(action: new PdfDictionary { [N("S")] = N("URI") }), signature);
        var editor = new PdfDocumentEditor(document);

        FormFieldActionRepairPreview preview = editor.PreviewFormFieldActionRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains("signature", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(((PdfDictionary)document.Objects[4]).Get("A"));
    }

    [Fact]
    public void DocMdp_refuses_every_candidate()
    {
        using PdfDocument document = Document(
            Widget(action: new PdfDictionary { [N("S")] = N("URI") }),
            configure: (_, catalog) =>
                catalog[N("Perms")] = new PdfDictionary { [N("DocMDP")] = new PdfDictionary() });
        var editor = new PdfDocumentEditor(document);

        FormFieldActionRepairPreview preview = editor.PreviewFormFieldActionRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains("DocMDP", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_selected_object_is_reported_without_writing()
    {
        using PdfDocument document = Document(Widget(action: new PdfDictionary { [N("S")] = N("URI") }));
        var editor = new PdfDocumentEditor(document);

        FormFieldActionRepairReport report = editor.RepairFormFieldActions(new HashSet<int> { 999 });
        Assert.Empty(report.Repaired);
        Assert.Equal(999, Assert.Single(report.Refused).ObjectNumber);
        Assert.NotNull(((PdfDictionary)document.Objects[4]).Get("A"));
    }
}
