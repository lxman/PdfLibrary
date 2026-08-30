using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using System.Text;

namespace PdfLibrary.Tests.Editing;

public class AdditionalActionsRepairTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);
    private static PdfString S(string value) => new(Encoding.ASCII.GetBytes(value));

    private static Finding[] Findings(PdfDocument document) =>
        [.. new AdditionalActionsRule().Check(new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static PdfDocument Document(PdfObject? catalogActions = null, PdfObject? pageActions = null,
        bool nestedPageTree = false, Action<PdfDocument, PdfDictionary>? configure = null)
    {
        var document = new PdfDocument();
        int pageParent = nestedPageTree ? 5 : 2;
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(pageParent),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
        });
        if (pageActions is not null)
            ((PdfDictionary)document.Objects[3])[N("AA")] = pageActions;

        if (nestedPageTree)
        {
            document.AddObject(5, 0, new PdfDictionary
            {
                [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
                [N("Parent")] = Ref(2),
            });
        }

        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(nestedPageTree ? 5 : 3)),
            [N("Count")] = new PdfInteger(1),
        });
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        if (catalogActions is not null) catalog[N("AA")] = catalogActions;
        configure?.Invoke(document, catalog);
        document.AddObject(1, 0, catalog);
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfDictionary Action(string uri, PdfObject? next = null)
    {
        var action = new PdfDictionary { [N("S")] = N("URI"), [N("URI")] = S(uri) };
        if (next is not null) action[N("Next")] = next;
        return action;
    }

    [Fact]
    public void Catalog_and_nested_page_are_one_document_scoped_repair_and_round_trip_cleanly()
    {
        var catalogActions = new PdfDictionary
        {
            [N("WC")] = Action("catalog-will-close", Action("catalog-next")),
            [N("DP")] = Action("catalog-did-print"),
        };
        var pageActions = new PdfDictionary
        {
            [N("O")] = Action("page-open", new PdfArray(Action("page-next-one"), Action("page-next-two"))),
            [N("C")] = Action("page-close"),
        };
        using PdfDocument document = Document(catalogActions, pageActions, nestedPageTree: true);
        var editor = new PdfDocumentEditor(document);

        Assert.Equal(2, Findings(document).Length);
        AdditionalActionsRepairPreview preview = editor.PreviewAdditionalActionsRepair();
        Assert.Equal(2, preview.Candidates.Count);
        Assert.Equal(["DP", "WC"], preview.Candidates[0].TriggerKeys);
        Assert.Equal(["C", "O"], preview.Candidates[1].TriggerKeys);
        Assert.Equal(0, preview.Candidates[1].PageIndex);

        AdditionalActionsRepairReport report = editor.RepairAdditionalActions();
        Assert.Equal(2, report.Repaired.Count);
        Assert.Empty(Findings(document));
        Assert.Empty(editor.RepairAdditionalActions().Repaired);

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.Empty(Findings(reloaded));
    }

    [Fact]
    public void Removing_a_shared_indirect_actions_dictionary_does_not_mutate_an_unrelated_host()
    {
        var shared = new PdfDictionary { [N("O")] = Action("shared-page-open") };
        using PdfDocument document = Document(pageActions: Ref(10), configure: (doc, catalog) =>
        {
            doc.AddObject(10, 0, shared);
            catalog[N("Names")] = new PdfDictionary { [N("VendorActions")] = Ref(10) };
        });
        var editor = new PdfDocumentEditor(document);

        Assert.Single(editor.RepairAdditionalActions().Repaired);
        Assert.Null(((PdfDictionary)document.Objects[3]).Get("AA"));
        Assert.Same(shared, document.Objects[10]);
        Assert.True(shared.ContainsKey(N("O")));
        Assert.NotNull(document.CatalogDictionary?.Get("Names"));
    }

    [Fact]
    public void Malformed_additional_actions_value_is_still_a_removable_host_key()
    {
        using PdfDocument document = Document(catalogActions: S("vendor-payload"));
        var editor = new PdfDocumentEditor(document);

        AdditionalActionsRepairCandidate candidate =
            Assert.Single(editor.PreviewAdditionalActionsRepair().Candidates);
        Assert.Empty(candidate.TriggerKeys);
        Assert.Single(editor.RepairAdditionalActions().Repaired);
        Assert.Empty(Findings(document));
    }

    [Fact]
    public void Signed_signature_refuses_every_host_without_writing()
    {
        using PdfDocument document = Document(
            catalogActions: new PdfDictionary(),
            pageActions: new PdfDictionary(),
            configure: (doc, catalog) =>
            {
                doc.AddObject(8, 0, new PdfDictionary
                {
                    [N("FT")] = N("Sig"), [N("V")] = new PdfDictionary(),
                });
                catalog[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(8)) };
            });
        var editor = new PdfDocumentEditor(document);

        AdditionalActionsRepairPreview preview = editor.PreviewAdditionalActionsRepair();
        Assert.Empty(preview.Candidates);
        Assert.Equal(2, preview.Refused.Count);
        Assert.All(preview.Refused,
            refusal => Assert.Contains("signature", refusal.Reason, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(editor.RepairAdditionalActions().Repaired);
        Assert.Equal(2, Findings(document).Length);
    }

    [Fact]
    public void DocMdp_refuses_without_writing()
    {
        using PdfDocument document = Document(catalogActions: new PdfDictionary(), configure: (_, catalog) =>
            catalog[N("Perms")] = new PdfDictionary { [N("DocMDP")] = new PdfDictionary() });
        var editor = new PdfDocumentEditor(document);

        AdditionalActionsRepairRefusal refusal =
            Assert.Single(editor.PreviewAdditionalActionsRepair().Refused);
        Assert.Contains("DocMDP", refusal.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(document.CatalogDictionary?.Get("AA"));
    }
}
