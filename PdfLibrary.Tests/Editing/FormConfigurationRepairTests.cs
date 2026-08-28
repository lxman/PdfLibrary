using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class FormConfigurationRepairTests
{
    private enum AppearanceKind { Stream, ButtonStates, Missing }

    private sealed record FieldSpec(
        string Name,
        string Type = "Tx",
        string? Value = null,
        AppearanceKind Appearance = AppearanceKind.Stream,
        bool DirectWidget = false,
        bool MissingRect = false);

    private sealed class Fixture : IDisposable
    {
        public required PdfDocument Document { get; init; }
        public required PdfDictionary Catalog { get; init; }
        public required PdfDictionary AcroForm { get; init; }
        public required IReadOnlyList<PdfDictionary> Fields { get; init; }
        public required IReadOnlyList<PdfDictionary> Widgets { get; init; }
        public void Dispose() => Document.Dispose();
    }

    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    private static Finding[] Findings(PdfDocument document) =>
        [.. new FormConfigRule().Check(new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static Fixture Build(
        IReadOnlyList<FieldSpec>? specs = null,
        bool needAppearances = false,
        string? dynamicRender = null,
        string? templateXml = null,
        bool needsRendering = false,
        bool duplicateWidgetOnSecondPage = false,
        bool docMdp = false,
        bool ur3 = false,
        bool malformedXfaArray = false)
    {
        specs ??= [new FieldSpec("form[0].page[0].name[0]")];
        var document = new PdfDocument();
        var fieldRefs = new PdfArray();
        var annotRefs = new PdfArray();
        var fields = new List<PdfDictionary>();
        var widgets = new List<PdfDictionary>();

        for (var i = 0; i < specs.Count; i++)
        {
            FieldSpec spec = specs[i];
            int fieldNumber = 10 + i * 3;
            int widgetNumber = fieldNumber + 1;
            int appearanceNumber = fieldNumber + 2;

            var field = new PdfDictionary
            {
                [N("FT")] = N(spec.Type),
                [N("T")] = PdfString.FromText(spec.Name),
            };
            if (spec.Value is not null)
                field[N("V")] = spec.Type == "Btn" ? N(spec.Value) : PdfString.FromText(spec.Value);

            var widget = new PdfDictionary
            {
                [N("Type")] = N("Annot"),
                [N("Subtype")] = N("Widget"),
                [N("Parent")] = Ref(fieldNumber),
            };
            if (!spec.MissingRect)
                widget[N("Rect")] = new PdfArray(
                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(20), new PdfInteger(10));

            if (spec.Appearance != AppearanceKind.Missing)
            {
                if (spec.Appearance == AppearanceKind.Stream)
                {
                    document.AddObject(appearanceNumber, 0, new PdfStream([]));
                    widget[N("AP")] = new PdfDictionary { [N("N")] = Ref(appearanceNumber) };
                }
                else
                {
                    document.AddObject(appearanceNumber, 0, new PdfStream([]));
                    widget[N("AP")] = new PdfDictionary
                    {
                        [N("N")] = new PdfDictionary { [N(spec.Value ?? "Off")] = Ref(appearanceNumber) },
                    };
                    widget[N("AS")] = N(spec.Value ?? "Off");
                }
            }

            document.AddObject(fieldNumber, 0, field);
            if (spec.DirectWidget)
            {
                field[N("Kids")] = new PdfArray(widget);
                annotRefs.Add(widget);
            }
            else
            {
                document.AddObject(widgetNumber, 0, widget);
                field[N("Kids")] = new PdfArray(Ref(widgetNumber));
                annotRefs.Add(Ref(widgetNumber));
            }
            fieldRefs.Add(Ref(fieldNumber));
            fields.Add(field);
            widgets.Add(widget);
        }

        var acroForm = new PdfDictionary { [N("Fields")] = fieldRefs };
        if (needAppearances)
            acroForm[N("NeedAppearances")] = PdfBoolean.True;

        if (dynamicRender is not null || templateXml is not null || malformedXfaArray)
        {
            string configXml =
                $"<config xmlns='http://www.xfa.org/schema/xci/3.3/'><present><pdf><dynamicRender>{dynamicRender ?? "forbidden"}</dynamicRender></pdf></present></config>";
            templateXml ??=
                "<template xmlns='http://www.xfa.org/schema/xfa-template/3.3/'>"
              + "<subform name='form'><subform name='page'><field name='name'/></subform></subform>"
              + "</template>";
            document.AddObject(60, 0, new PdfStream(Encoding.UTF8.GetBytes(configXml)));
            document.AddObject(61, 0, new PdfStream(Encoding.UTF8.GetBytes(templateXml)));
            acroForm[N("XFA")] = malformedXfaArray
                ? new PdfArray(PdfString.FromText("config"), Ref(60), PdfString.FromText("template"))
                : new PdfArray(
                    PdfString.FromText("config"), Ref(60),
                    PdfString.FromText("template"), Ref(61));
        }

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
            [N("Annots")] = annotRefs,
        };
        document.AddObject(3, 0, page);

        var pageKids = new PdfArray(Ref(3));
        if (duplicateWidgetOnSecondPage)
        {
            document.AddObject(4, 0, new PdfDictionary
            {
                [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
                [N("MediaBox")] = new PdfArray(
                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
                [N("Annots")] = new PdfArray(annotRefs[0]),
            });
            pageKids.Add(Ref(4));
        }
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = pageKids,
            [N("Count")] = new PdfInteger(pageKids.Count),
        });

        var catalog = new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("AcroForm")] = acroForm,
        };
        if (needsRendering)
            catalog[N("NeedsRendering")] = PdfBoolean.True;
        if (docMdp || ur3)
        {
            var perms = new PdfDictionary();
            if (docMdp) perms[N("DocMDP")] = new PdfDictionary();
            if (ur3) perms[N("UR3")] = new PdfDictionary();
            catalog[N("Perms")] = perms;
        }
        document.AddObject(1, 0, catalog);
        document.Trailer.Dictionary[N("Root")] = Ref(1);

        return new Fixture
        {
            Document = document, Catalog = catalog, AcroForm = acroForm, Fields = fields, Widgets = widgets,
        };
    }

    [Fact]
    public void NeedAppearances_with_complete_current_appearances_is_a_candidate()
    {
        using Fixture fixture = Build(needAppearances: true);
        var editor = new PdfDocumentEditor(fixture.Document);

        FormConfigurationRepairCandidate? candidate = editor.PreviewFormConfigurationRepair().Candidate;
        Assert.NotNull(candidate);

        Assert.True(candidate.RemovesNeedAppearances);
        Assert.False(candidate.RemovesXfa);
        Assert.Empty(editor.PreviewFormConfigurationRepair().Refused);
    }

    [Fact]
    public void NeedAppearances_with_missing_current_appearance_is_refused()
    {
        using Fixture fixture = Build(
            [new FieldSpec("form[0].page[0].name[0]", Appearance: AppearanceKind.Missing)],
            needAppearances: true);
        var preview = new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair();

        Assert.Null(preview.Candidate);
        Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("appearance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NeedAppearances_false_is_not_a_condition()
    {
        using Fixture fixture = Build();
        fixture.AcroForm[N("NeedAppearances")] = PdfBoolean.False;

        FormConfigurationRepairPreview preview =
            new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair();

        Assert.Null(preview.Candidate);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void NeedAppearances_button_requires_the_selected_state_to_resolve_to_a_stream()
    {
        using Fixture safe = Build(
            [new FieldSpec("button[0]", Type: "Btn", Value: "Yes", Appearance: AppearanceKind.ButtonStates)],
            needAppearances: true);
        using Fixture unsafeFixture = Build(
            [new FieldSpec("button[0]", Type: "Btn", Value: "Yes", Appearance: AppearanceKind.ButtonStates)],
            needAppearances: true);
        PdfDictionary normal = (PdfDictionary)((PdfDictionary)unsafeFixture.Widgets[0].Get("AP")!).Get("N")!;
        normal[N("Yes")] = new PdfDictionary();

        Assert.NotNull(new PdfDocumentEditor(safe.Document).PreviewFormConfigurationRepair().Candidate);
        Assert.Null(new PdfDocumentEditor(unsafeFixture.Document).PreviewFormConfigurationRepair().Candidate);
    }

    [Fact]
    public void Static_XFAF_with_exact_indexed_SOM_mapping_is_a_candidate()
    {
        const string template =
            "<template xmlns='http://www.xfa.org/schema/xfa-template/3.3/'>"
          + "<subform name='form'><subform name='page'><field name='name'/><field name='name'/></subform></subform>"
          + "</template>";
        using Fixture fixture = Build(
            [
                new FieldSpec("form[0].page[0].name[0]"),
                new FieldSpec("form[0].page[0].name[1]"),
            ], dynamicRender: "forbidden", templateXml: template);

        FormConfigurationRepairCandidate? candidate =
            new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair().Candidate;
        Assert.NotNull(candidate);

        Assert.True(candidate.RemovesXfa);
        Assert.Equal(2, candidate.XfaPacketCount);
        Assert.Equal(2, candidate.PreservedFieldCount);
    }

    [Fact]
    public void Static_XFAF_allows_a_blank_text_field_without_an_appearance()
    {
        using Fixture fixture = Build(
            [new FieldSpec("form[0].page[0].name[0]", Appearance: AppearanceKind.Missing)],
            dynamicRender: "forbidden");

        FormConfigurationRepairCandidate? candidate =
            new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair().Candidate;

        Assert.NotNull(candidate);
        Assert.True(candidate.RemovesXfa);
    }

    [Theory]
    [InlineData("required")]
    [InlineData("allowed")]
    [InlineData("")]
    public void Dynamic_or_unrecognized_XFA_is_refused(string value)
    {
        using Fixture fixture = Build(dynamicRender: value);
        FormConfigurationRepairPreview preview =
            new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair();

        Assert.Null(preview.Candidate);
        Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("dynamicRender", StringComparison.Ordinal));
    }

    [Fact]
    public void XFA_only_and_partial_AcroForm_mappings_are_refused()
    {
        using Fixture xfaOnly = Build([], dynamicRender: "forbidden");
        using Fixture partial = Build(
            [new FieldSpec("form[0].page[0].other[0]")], dynamicRender: "forbidden");

        Assert.Null(new PdfDocumentEditor(xfaOnly.Document).PreviewFormConfigurationRepair().Candidate);
        Assert.Null(new PdfDocumentEditor(partial.Document).PreviewFormConfigurationRepair().Candidate);
    }

    [Fact]
    public void NeedsRendering_is_never_removed_and_blocks_XFA_removal()
    {
        using Fixture fixture = Build(dynamicRender: "forbidden", needsRendering: true);
        var editor = new PdfDocumentEditor(fixture.Document);

        FormConfigurationRepairPreview preview = editor.PreviewFormConfigurationRepair();
        FormConfigurationRepairReport report = editor.RepairFormConfiguration();

        Assert.Null(preview.Candidate);
        Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("NeedsRendering", StringComparison.Ordinal));
        Assert.Null(report.Repaired);
        Assert.NotNull(fixture.AcroForm.Get("XFA"));
        Assert.NotNull(fixture.Catalog.Get("NeedsRendering"));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Unsafe_widget_shapes_are_refused(bool direct, bool missingRect, bool duplicatePage)
    {
        using Fixture fixture = Build(
            [new FieldSpec("form[0].page[0].name[0]", DirectWidget: direct, MissingRect: missingRect)],
            dynamicRender: "forbidden", duplicateWidgetOnSecondPage: duplicatePage);

        Assert.Null(new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair().Candidate);
    }

    [Fact]
    public void Unsupported_field_and_nonempty_value_without_AP_are_refused()
    {
        using Fixture unsupported = Build(
            [new FieldSpec("form[0].page[0].name[0]", Type: "Unknown")], dynamicRender: "forbidden");
        using Fixture missingAp = Build(
            [new FieldSpec(
                "form[0].page[0].name[0]", Value: "redacted", Appearance: AppearanceKind.Missing)],
            dynamicRender: "forbidden");

        Assert.Null(new PdfDocumentEditor(unsupported.Document).PreviewFormConfigurationRepair().Candidate);
        Assert.Null(new PdfDocumentEditor(missingAp.Document).PreviewFormConfigurationRepair().Candidate);
    }

    [Fact]
    public void Signed_signature_and_DocMDP_documents_are_refused()
    {
        using Fixture signed = Build(
            [new FieldSpec("form[0].page[0].name[0]", Type: "Sig", Value: "signed")],
            needAppearances: true);
        using Fixture docMdp = Build(needAppearances: true, docMdp: true);

        Assert.Null(new PdfDocumentEditor(signed.Document).PreviewFormConfigurationRepair().Candidate);
        Assert.Null(new PdfDocumentEditor(docMdp.Document).PreviewFormConfigurationRepair().Candidate);
    }

    [Fact]
    public void UR3_is_disclosed_but_preserved_for_static_XFAF()
    {
        using Fixture fixture = Build(dynamicRender: "forbidden", ur3: true);
        var editor = new PdfDocumentEditor(fixture.Document);

        FormConfigurationRepairCandidate? candidate = editor.PreviewFormConfigurationRepair().Candidate;
        Assert.NotNull(candidate);
        FormConfigurationRepair? repair = editor.RepairFormConfiguration().Repaired;
        Assert.NotNull(repair);

        Assert.True(candidate.InvalidatesUsageRightsSignature);
        Assert.True(repair.InvalidatedUsageRightsSignature);
        Assert.NotNull(fixture.Catalog.Get("Perms"));
    }

    [Fact]
    public void UR3_blocks_automatic_NeedAppearances_only_repair()
    {
        using Fixture fixture = Build(needAppearances: true, ur3: true);
        var editor = new PdfDocumentEditor(fixture.Document);

        FormConfigurationRepairPreview preview = editor.PreviewFormConfigurationRepair();
        FormConfigurationRepairReport report = editor.RepairFormConfiguration();

        Assert.Null(preview.Candidate);
        Assert.Contains(preview.Refused, refusal => refusal.Reason.Contains("UR3", StringComparison.Ordinal));
        Assert.Null(report.Repaired);
        Assert.NotNull(fixture.AcroForm.Get("NeedAppearances"));
        Assert.NotNull(fixture.Catalog.Get("Perms"));
    }

    [Fact]
    public void Repair_removes_only_selected_keys_and_is_idempotent_after_round_trip()
    {
        using Fixture fixture = Build(
            [new FieldSpec(
                "form[0].page[0].name[0]", Type: "Btn", Value: "Yes",
                Appearance: AppearanceKind.ButtonStates)],
            needAppearances: true, dynamicRender: "forbidden", ur3: true);
        fixture.Fields[0][N("DV")] = N("Yes");
        PdfObject fieldsBefore = Assert.IsAssignableFrom<PdfObject>(fixture.AcroForm.Get("Fields"));
        PdfObject permsBefore = Assert.IsAssignableFrom<PdfObject>(fixture.Catalog.Get("Perms"));
        PdfObject valueBefore = Assert.IsAssignableFrom<PdfObject>(fixture.Fields[0].Get("V"));
        PdfObject defaultValueBefore = Assert.IsAssignableFrom<PdfObject>(fixture.Fields[0].Get("DV"));
        PdfObject appearanceStateBefore = Assert.IsAssignableFrom<PdfObject>(fixture.Widgets[0].Get("AS"));
        PdfObject apBefore = Assert.IsAssignableFrom<PdfObject>(fixture.Widgets[0].Get("AP"));
        PdfObject pagesBefore = Assert.IsAssignableFrom<PdfObject>(fixture.Catalog.Get("Pages"));
        PdfDictionary pageBefore = Assert.Single(fixture.Document.GetPages()).Dictionary;
        var editor = new PdfDocumentEditor(fixture.Document);

        FormConfigurationRepair? repair = editor.RepairFormConfiguration().Repaired;
        Assert.NotNull(repair);

        Assert.True(repair.RemovedNeedAppearances);
        Assert.True(repair.RemovedXfa);
        Assert.Null(fixture.AcroForm.Get("NeedAppearances"));
        Assert.Null(fixture.AcroForm.Get("XFA"));
        Assert.Same(fieldsBefore, fixture.AcroForm.Get("Fields"));
        Assert.Same(permsBefore, fixture.Catalog.Get("Perms"));
        Assert.Same(valueBefore, fixture.Fields[0].Get("V"));
        Assert.Same(defaultValueBefore, fixture.Fields[0].Get("DV"));
        Assert.Same(appearanceStateBefore, fixture.Widgets[0].Get("AS"));
        Assert.Same(apBefore, fixture.Widgets[0].Get("AP"));
        Assert.Same(pagesBefore, fixture.Catalog.Get("Pages"));
        Assert.Same(pageBefore, Assert.Single(fixture.Document.GetPages()).Dictionary);
        Assert.Empty(Findings(fixture.Document));
        Assert.Null(editor.RepairFormConfiguration().Repaired);

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reopened = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.Empty(Findings(reopened));
        Assert.Null(new PdfDocumentEditor(reopened).RepairFormConfiguration().Repaired);
    }

    [Fact]
    public void Malformed_packet_array_is_refused_without_mutation()
    {
        using Fixture fixture = Build(dynamicRender: "forbidden", malformedXfaArray: true);
        var editor = new PdfDocumentEditor(fixture.Document);

        Assert.Null(editor.RepairFormConfiguration().Repaired);
        Assert.NotNull(fixture.AcroForm.Get("XFA"));
    }

    [Fact]
    public void Single_stream_and_malformed_XML_XFA_are_refused()
    {
        using Fixture singleStream = Build(dynamicRender: "forbidden");
        singleStream.AcroForm[N("XFA")] = Ref(60);
        using Fixture malformedXml = Build(
            dynamicRender: "forbidden", templateXml: "<template><field name='name'></template>");

        Assert.Null(new PdfDocumentEditor(singleStream.Document).PreviewFormConfigurationRepair().Candidate);
        Assert.Null(new PdfDocumentEditor(malformedXml.Document).PreviewFormConfigurationRepair().Candidate);
    }

    [Fact]
    public void Repair_reclassifies_live_state_and_refuses_preview_drift()
    {
        using Fixture fixture = Build(needAppearances: true);
        var editor = new PdfDocumentEditor(fixture.Document);
        Assert.NotNull(editor.PreviewFormConfigurationRepair().Candidate);

        fixture.Widgets[0].Remove(N("AP"));
        FormConfigurationRepairReport report = editor.RepairFormConfiguration();

        Assert.Null(report.Repaired);
        Assert.NotNull(fixture.AcroForm.Get("NeedAppearances"));
        Assert.Contains(report.Refused, refusal => refusal.Reason.Contains("appearance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Page_widget_outside_the_field_tree_is_refused()
    {
        using Fixture fixture = Build(dynamicRender: "forbidden");
        var foreign = new PdfDictionary
        {
            [N("Type")] = N("Annot"), [N("Subtype")] = N("Widget"),
            [N("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(5), new PdfInteger(5)),
        };
        ((PdfArray)((PdfDictionary)fixture.Document.Objects[3]).Get("Annots")!).Add(foreign);

        Assert.Null(new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair().Candidate);
    }

    [Fact]
    public void Document_without_form_configuration_condition_is_a_no_op()
    {
        using Fixture fixture = Build();
        FormConfigurationRepairPreview preview =
            new PdfDocumentEditor(fixture.Document).PreviewFormConfigurationRepair();

        Assert.Null(preview.Candidate);
        Assert.Empty(preview.Refused);
    }
}
