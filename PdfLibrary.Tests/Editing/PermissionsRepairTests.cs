using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public sealed class PermissionsRepairTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    [Fact]
    public void Repair_creates_an_unsigned_derivative_and_closes_both_permissions_branches()
    {
        using PdfDocument document = SignedDocument(includeForbiddenPermissionsKey: true, includeDigest: true);
        var editor = new PdfDocumentEditor(document);

        Assert.Equal(2, Findings(document).Length);
        PermissionsRepairPreview preview = editor.PreviewPermissionsRepair();
        Assert.True(preview.IsCandidate);
        Assert.Equal(1, preview.ForbiddenPermissionsKeyCount);
        Assert.Equal(1, preview.ForbiddenDigestKeyCount);
        Assert.Equal(1, preview.SignatureValueCount);
        Assert.Equal(1, preview.SignatureAppearanceCount);
        Assert.True(preview.HasDocMdp);
        Assert.True(preview.HasUsageRights);

        PermissionsRepairReport report = editor.RepairPermissions();
        Assert.True(report.Repaired);
        Assert.Equal(1, report.RemovedDigestKeyCount);
        Assert.Equal(1, report.ClearedSignatureValueCount);
        Assert.Equal(1, report.ClearedSignatureAppearanceCount);
        Assert.Equal(2, report.ScrubbedSignatureDictionaryCount);
        Assert.True(report.RemovedDocMdp);
        Assert.True(report.RemovedUsageRights);

        PdfDictionary catalog = (PdfDictionary)document.Objects[1];
        PdfDictionary field = (PdfDictionary)document.Objects[5];
        PdfDictionary widget = (PdfDictionary)document.Objects[6];
        PdfDictionary sigRef = (PdfDictionary)document.Objects[4];
        PdfDictionary signature = (PdfDictionary)document.Objects[8];
        Assert.Null(catalog.Get("Perms"));
        Assert.Null(field.Get("V"));
        Assert.Null(widget.Get("AP"));
        Assert.Null(widget.Get("AS"));
        Assert.Null(sigRef.Get("DigestMethod"));
        Assert.Null(signature.Get("ByteRange"));
        Assert.Null(signature.Get("Contents"));
        Assert.Null(signature.Get("Reference"));
        Assert.Empty(Findings(document));

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.False(reloaded.IsEncrypted);
        Assert.Empty(Findings(reloaded));
        Assert.False(new PdfDocumentEditor(reloaded).PreviewPermissionsRepair().IsCandidate);
    }

    [Fact]
    public void Allowed_DocMdp_without_a_legacy_digest_is_not_a_candidate_and_is_not_changed()
    {
        using PdfDocument document = SignedDocument(includeForbiddenPermissionsKey: false, includeDigest: false);
        var editor = new PdfDocumentEditor(document);

        Assert.Empty(Findings(document));
        Assert.False(editor.PreviewPermissionsRepair().IsCandidate);
        Assert.False(editor.RepairPermissions().Repaired);
        Assert.NotNull(((PdfDictionary)document.Objects[1]).Get("Perms"));
        Assert.NotNull(((PdfDictionary)document.Objects[5]).Get("V"));
        Assert.NotNull(((PdfDictionary)document.Objects[6]).Get("AP"));
    }

    [Fact]
    public void Forbidden_permissions_key_without_a_signature_is_repaired()
    {
        using PdfDocument document = BasicDocument();
        PdfDictionary catalog = (PdfDictionary)document.Objects[1];
        catalog[N("Perms")] = new PdfDictionary { [N("LegacyRights")] = new PdfDictionary() };
        var editor = new PdfDocumentEditor(document);

        PermissionsRepairPreview preview = editor.PreviewPermissionsRepair();
        Assert.True(preview.IsCandidate);
        Assert.Equal(1, preview.ForbiddenPermissionsKeyCount);
        Assert.Equal(0, preview.ForbiddenDigestKeyCount);
        Assert.Equal(0, preview.SignatureValueCount);

        Assert.True(editor.RepairPermissions().Repaired);
        Assert.Null(catalog.Get("Perms"));
        Assert.Empty(Findings(document));
    }

    private static Finding[] Findings(PdfDocument document) =>
        [.. new PermissionsRule().Check(new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static PdfDocument SignedDocument(bool includeForbiddenPermissionsKey, bool includeDigest)
    {
        PdfDocument document = BasicDocument();
        PdfDictionary catalog = (PdfDictionary)document.Objects[1];
        PdfDictionary page = (PdfDictionary)document.Objects[3];

        var sigRef = new PdfDictionary { [N("Type")] = N("SigRef") };
        if (includeDigest) sigRef[N("DigestMethod")] = N("MD5");
        document.AddObject(4, 0, sigRef);

        document.AddObject(6, 0, new PdfDictionary
        {
            [N("Type")] = N("Annot"), [N("Subtype")] = N("Widget"), [N("Parent")] = Ref(5),
            [N("AP")] = new PdfDictionary { [N("N")] = new PdfDictionary() }, [N("AS")] = N("Signed")
        });
        page[N("Annots")] = new PdfArray(Ref(6));

        document.AddObject(5, 0, new PdfDictionary
        {
            [N("FT")] = N("Sig"), [N("V")] = Ref(8), [N("Kids")] = new PdfArray(Ref(6))
        });
        catalog[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(5)) };

        document.AddObject(8, 0, new PdfDictionary
        {
            [N("Type")] = N("Sig"),
            [N("Contents")] = new PdfString(new byte[] { 1, 2, 3 }, PdfStringFormat.Hexadecimal),
            [N("ByteRange")] = new PdfArray(new PdfInteger(0), new PdfInteger(10), new PdfInteger(20), new PdfInteger(30)),
            [N("Reference")] = new PdfArray(Ref(4))
        });
        document.AddObject(9, 0, new PdfDictionary
        {
            [N("Type")] = N("Sig"),
            [N("Contents")] = new PdfString(new byte[] { 4, 5, 6 }, PdfStringFormat.Hexadecimal),
            [N("ByteRange")] = new PdfArray(new PdfInteger(0), new PdfInteger(5), new PdfInteger(10), new PdfInteger(15))
        });

        var permissions = new PdfDictionary { [N("DocMDP")] = Ref(8), [N("UR3")] = Ref(9) };
        if (includeForbiddenPermissionsKey) permissions[N("LegacyRights")] = new PdfDictionary();
        catalog[N("Perms")] = permissions;
        return document;
    }

    private static PdfDocument BasicDocument()
    {
        var document = new PdfDocument();
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100))
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1)
        });
        document.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }
}
