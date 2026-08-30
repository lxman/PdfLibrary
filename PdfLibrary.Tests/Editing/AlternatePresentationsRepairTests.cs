using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using System.Text;

namespace PdfLibrary.Tests.Editing;

public class AlternatePresentationsRepairTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);
    private static PdfString S(string value) => new(Encoding.ASCII.GetBytes(value));

    private static Finding[] Findings(PdfDocument document) =>
        [.. new AlternatePresentationsRule().Check(
            new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static PdfDocument Document(
        PdfObject? alternatePresentations = null,
        PdfObject? presentationSteps = null,
        bool indirectNames = false,
        bool nestedPageTree = false,
        Action<PdfDocument, PdfDictionary>? configure = null)
    {
        var document = new PdfDocument();
        int pageParent = nestedPageTree ? 5 : 2;
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(pageParent),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
        };
        if (presentationSteps is not null)
            page[N("PresSteps")] = presentationSteps;
        document.AddObject(3, 0, page);

        if (nestedPageTree)
        {
            document.AddObject(5, 0, new PdfDictionary
            {
                [N("Type")] = N("Pages"),
                [N("Kids")] = new PdfArray(Ref(3)),
                [N("Count")] = new PdfInteger(1),
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
        if (alternatePresentations is not null)
        {
            var names = new PdfDictionary { [N("AlternatePresentations")] = alternatePresentations };
            if (indirectNames)
            {
                document.AddObject(9, 0, names);
                catalog[N("Names")] = Ref(9);
            }
            else
            {
                catalog[N("Names")] = names;
            }
        }
        configure?.Invoke(document, catalog);
        document.AddObject(1, 0, catalog);
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfDictionary Steps(string marker) => new()
    {
        [N("NA")] = new PdfDictionary { [N("S")] = N("JavaScript"), [N("JS")] = S(marker) },
        [N("Next")] = new PdfDictionary
        {
            [N("PA")] = new PdfDictionary { [N("S")] = N("JavaScript"), [N("JS")] = S("back") },
        },
    };

    [Fact]
    public void Indirect_names_and_nested_page_are_one_document_scoped_repair_and_round_trip_cleanly()
    {
        using PdfDocument document = Document(
            alternatePresentations: new PdfDictionary
            {
                [N("Names")] = new PdfArray(S("Deck"), new PdfDictionary { [N("Type")] = N("SlideShow") }),
            },
            presentationSteps: Ref(10),
            indirectNames: true,
            nestedPageTree: true,
            configure: (doc, _) => doc.AddObject(10, 0, Steps("forward")));
        var editor = new PdfDocumentEditor(document);

        Assert.Equal(2, Findings(document).Length);
        AlternatePresentationsRepairPreview preview = editor.PreviewAlternatePresentationsRepair();
        Assert.Equal(2, preview.Candidates.Count);
        Assert.Equal(AlternatePresentationsOwnerKind.NameDictionary, preview.Candidates[0].OwnerKind);
        Assert.Equal(9, preview.Candidates[0].ObjectNumber);
        Assert.Equal(AlternatePresentationsOwnerKind.Page, preview.Candidates[1].OwnerKind);
        Assert.Equal(0, preview.Candidates[1].PageIndex);
        Assert.Equal(10, preview.Candidates[1].StructureObjectNumber);

        AlternatePresentationsRepairReport report = editor.RepairAlternatePresentations();
        Assert.Equal(2, report.Repaired.Count);
        Assert.Empty(Findings(document));
        Assert.Empty(editor.RepairAlternatePresentations().Repaired);

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.Empty(Findings(reloaded));
    }

    [Fact]
    public void Removing_hosts_does_not_mutate_shared_indirect_presentation_structures()
    {
        var shared = Steps("shared-forward");
        using PdfDocument document = Document(
            alternatePresentations: Ref(10),
            presentationSteps: Ref(10),
            configure: (doc, catalog) =>
            {
                doc.AddObject(10, 0, shared);
                catalog[N("VendorPresentationBackup")] = Ref(10);
            });
        var editor = new PdfDocumentEditor(document);

        Assert.Equal(2, editor.RepairAlternatePresentations().Repaired.Count);
        Assert.Same(shared, document.Objects[10]);
        Assert.True(shared.ContainsKey(N("NA")));
        Assert.True(shared.ContainsKey(N("Next")));
        Assert.Equal(10, Assert.IsType<PdfIndirectReference>(
            document.CatalogDictionary?.Get("VendorPresentationBackup")).ObjectNumber);
    }

    [Fact]
    public void Malformed_values_are_still_removable_host_keys()
    {
        using PdfDocument document = Document(
            alternatePresentations: S("vendor-slideshow"),
            presentationSteps: new PdfInteger(42));
        var editor = new PdfDocumentEditor(document);

        Assert.Equal(2, editor.PreviewAlternatePresentationsRepair().Candidates.Count);
        Assert.Equal(2, editor.RepairAlternatePresentations().Repaired.Count);
        Assert.Empty(Findings(document));
    }

    [Fact]
    public void Signed_signature_refuses_every_host_without_writing()
    {
        using PdfDocument document = Document(
            alternatePresentations: new PdfDictionary(),
            presentationSteps: new PdfDictionary(),
            configure: (doc, catalog) =>
            {
                doc.AddObject(8, 0, new PdfDictionary
                {
                    [N("FT")] = N("Sig"), [N("V")] = new PdfDictionary(),
                });
                catalog[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(8)) };
            });
        var editor = new PdfDocumentEditor(document);

        AlternatePresentationsRepairPreview preview = editor.PreviewAlternatePresentationsRepair();
        Assert.Empty(preview.Candidates);
        Assert.Equal(2, preview.Refused.Count);
        Assert.All(preview.Refused,
            refusal => Assert.Contains("signature", refusal.Reason, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(editor.RepairAlternatePresentations().Repaired);
        Assert.Equal(2, Findings(document).Length);
    }

    [Fact]
    public void DocMdp_refuses_without_writing()
    {
        using PdfDocument document = Document(alternatePresentations: new PdfDictionary(), configure: (_, catalog) =>
            catalog[N("Perms")] = new PdfDictionary { [N("DocMDP")] = new PdfDictionary() });
        var editor = new PdfDocumentEditor(document);

        AlternatePresentationsRepairRefusal refusal =
            Assert.Single(editor.PreviewAlternatePresentationsRepair().Refused);
        Assert.Contains("DocMDP", refusal.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(document.GetCatalog()?.Dictionary.Get("Names"));
    }
}
