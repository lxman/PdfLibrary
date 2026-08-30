using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using System.Text;

namespace PdfLibrary.Tests.Editing;

public class DocumentRequirementsRepairTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);
    private static PdfString S(string value) => new(Encoding.ASCII.GetBytes(value));

    private static PdfObject? Resolve(PdfDocument document, PdfObject? value) =>
        value is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : value;

    private static Finding[] Findings(PdfDocument document) =>
        [.. new DocumentRequirementsRule().Check(
            new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static PdfDocument Document(
        PdfObject requirements,
        bool indirect = false,
        Action<PdfDocument, PdfDictionary>? configure = null)
    {
        var document = new PdfDocument();
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        if (indirect)
        {
            document.AddObject(10, 0, requirements);
            catalog[N("Requirements")] = Ref(10);
        }
        else
        {
            catalog[N("Requirements")] = requirements;
        }
        configure?.Invoke(document, catalog);
        document.AddObject(1, 0, catalog);
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfArray RequirementDeclaration(string marker = "requirement") => new(
        new PdfDictionary
        {
            [N("Type")] = N("Requirement"),
            [N("S")] = N("EnableJavaScripts"),
            [N("RH")] = new PdfArray(new PdfDictionary
            {
                [N("Type")] = N("ReqHandler"), [N("S")] = N("JS"), [N("Script")] = S(marker),
            }),
            [N("V")] = N("2.0"),
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_full_rewrite_preserves_direct_and_indirect_requirement_semantics(bool indirect)
    {
        using PdfDocument source = Document(RequirementDeclaration(), indirect);
        using var saved = new MemoryStream();

        new PdfDocumentEditor(source).Save(saved);
        using PdfDocument reopened = PdfDocument.Load(new MemoryStream(saved.ToArray()));

        Assert.Single(Findings(reopened));
        PdfObject? raw = reopened.CatalogDictionary?.Get("Requirements");
        PdfArray array = Assert.IsType<PdfArray>(Resolve(reopened, raw));
        PdfDictionary requirement = Assert.IsType<PdfDictionary>(Resolve(reopened, array[0]));
        Assert.Equal("EnableJavaScripts", Assert.IsType<PdfName>(requirement.Get("S")).Value);
        Assert.Equal("2.0", Assert.IsType<PdfName>(requirement.Get("V")).Value);
        PdfArray handlers = Assert.IsType<PdfArray>(Resolve(reopened, requirement.Get("RH")));
        PdfDictionary handler = Assert.IsType<PdfDictionary>(Resolve(reopened, handlers[0]));
        Assert.Equal("JS", Assert.IsType<PdfName>(handler.Get("S")).Value);
        Assert.Equal("requirement", Encoding.ASCII.GetString(Assert.IsType<PdfString>(handler.Get("Script")).Bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plain_full_rewrite_preserves_malformed_requirement_value(bool indirect)
    {
        using PdfDocument source = Document(S("vendor requirement payload"), indirect);
        using var saved = new MemoryStream();

        new PdfDocumentEditor(source).Save(saved);
        using PdfDocument reopened = PdfDocument.Load(new MemoryStream(saved.ToArray()));

        Assert.Single(Findings(reopened));
        PdfString value = Assert.IsType<PdfString>(Resolve(reopened,
            reopened.CatalogDictionary?.Get("Requirements")));
        Assert.Equal("vendor requirement payload", Encoding.ASCII.GetString(value.Bytes));
    }

    [Fact]
    public void Repair_removes_only_catalog_host_and_round_trips_cleanly()
    {
        PdfArray shared = RequirementDeclaration("shared handler");
        using PdfDocument document = Document(shared, indirect: true, configure: (_, catalog) =>
            catalog[N("VendorRequirementsBackup")] = Ref(10));
        var editor = new PdfDocumentEditor(document);

        DocumentRequirementsRepairCandidate candidate =
            Assert.IsType<DocumentRequirementsRepairCandidate>(editor.PreviewDocumentRequirementsRepair().Candidate);
        Assert.Equal(1, candidate.CatalogObjectNumber);
        Assert.Equal(10, candidate.RequirementsObjectNumber);

        DocumentRequirementsRepairReport report = editor.RepairDocumentRequirements();
        Assert.NotNull(report.Repaired);
        Assert.Null(report.Refused);
        Assert.Empty(Findings(document));
        Assert.Same(shared, document.Objects[10]);
        Assert.True(shared.Count > 0);
        Assert.Equal(10, Assert.IsType<PdfIndirectReference>(
            document.CatalogDictionary?.Get("VendorRequirementsBackup")).ObjectNumber);
        Assert.Null(editor.RepairDocumentRequirements().Repaired);

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reopened = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.Empty(Findings(reopened));
        Assert.IsType<PdfArray>(Resolve(reopened,
            reopened.CatalogDictionary?.Get("VendorRequirementsBackup")));
    }

    [Fact]
    public void Malformed_value_is_still_a_removable_host_key()
    {
        using PdfDocument document = Document(new PdfInteger(42));
        var editor = new PdfDocumentEditor(document);

        Assert.NotNull(editor.PreviewDocumentRequirementsRepair().Candidate);
        Assert.NotNull(editor.RepairDocumentRequirements().Repaired);
        Assert.Empty(Findings(document));
    }

    [Fact]
    public void Signed_signature_refuses_without_writing()
    {
        using PdfDocument document = Document(RequirementDeclaration(), configure: (doc, catalog) =>
        {
            doc.AddObject(8, 0, new PdfDictionary
            {
                [N("FT")] = N("Sig"), [N("V")] = new PdfDictionary(),
            });
            catalog[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(8)) };
        });
        var editor = new PdfDocumentEditor(document);

        DocumentRequirementsRepairRefusal refusal = Assert.IsType<DocumentRequirementsRepairRefusal>(
            editor.PreviewDocumentRequirementsRepair().Refused);
        Assert.Contains("signature", refusal.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(editor.RepairDocumentRequirements().Repaired);
        Assert.Single(Findings(document));
    }

    [Fact]
    public void DocMdp_refuses_without_writing()
    {
        using PdfDocument document = Document(RequirementDeclaration(), configure: (_, catalog) =>
            catalog[N("Perms")] = new PdfDictionary { [N("DocMDP")] = new PdfDictionary() });
        var editor = new PdfDocumentEditor(document);

        DocumentRequirementsRepairRefusal refusal = Assert.IsType<DocumentRequirementsRepairRefusal>(
            editor.PreviewDocumentRequirementsRepair().Refused);
        Assert.Contains("DocMDP", refusal.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(document.CatalogDictionary?.Get("Requirements"));
    }
}
