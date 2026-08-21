using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.RepairFileSpecNames"/> (ISO 19005-2 6.8 / ISO 14289-1 7.11).
///
/// <para>Fixture convention mirrors <see cref="CidToGidMapIdentityWriteTests"/> and
/// <see cref="SymbolicEncodingRemovalTests"/> — hand-built <see cref="PdfDocument"/> construction via
/// <c>AddObject</c>, since the method resolves its filespecs by walking the catalog name tree and (when
/// asked) page /Annots directly, needing no vendored fixture.</para>
///
/// <para>The catalog-arm fixture registers a single filespec (object 10) as the sole leaf entry of
/// /Names /EmbeddedFiles, keyed by <c>nameTreeKey</c> — matching the corpus shape Task 1 measured (55/55
/// affected documents: /F present and non-empty, /UF absent). The annotation-arm fixture instead reaches
/// the same shape of filespec via a page's /Annots[].FS, per <c>EmbeddedFileSpecRule.CollectFileSpecs</c>
/// (PdfLibrary/Conformance/Rules/EmbeddedFileSpecRule.cs:111-131), which this method's PDF/UA-1 arm
/// mirrors.</para>
/// </summary>
public class FileSpecNameRepairTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static PdfDictionary Dict(PdfDocument document, int objectNumber) =>
        (PdfDictionary)document.Objects[objectNumber];

    /// <summary>A minimal valid document (catalog object 1 → empty page tree object 2) plus a filespec
    /// dictionary at object 10, registered as the sole leaf entry of /Names /EmbeddedFiles under
    /// <paramref name="nameTreeKey"/>. <paramref name="f"/>/<paramref name="uf"/> are stored directly
    /// (null omits the key entirely); <paramref name="includeEf"/> controls whether the filespec carries
    /// an /EF entry at all (EmbeddedFileSpecRule skips any filespec without one).</summary>
    private static PdfDocument BuildCatalogFilespecDocument(
        string nameTreeKey, PdfObject? f, PdfObject? uf, bool includeEf = true)
    {
        var doc = new PdfDocument();

        var specDict = new PdfDictionary { [N("Type")] = N("Filespec") };
        if (includeEf) specDict[N("EF")] = new PdfDictionary();
        if (f is not null) specDict[N("F")] = f;
        if (uf is not null) specDict[N("UF")] = uf;
        doc.AddObject(10, 0, specDict);

        var namesArray = new PdfArray();
        namesArray.Add(PdfString.FromText(nameTreeKey));
        namesArray.Add(Ref(10));
        var embeddedFilesLeaf = new PdfDictionary { [N("Names")] = namesArray };
        var namesDict = new PdfDictionary { [N("EmbeddedFiles")] = embeddedFilesLeaf };

        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(),
            [N("Count")] = new PdfInteger(0),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(2),
            [N("Names")] = namesDict,
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>A minimal valid document reaching the same filespec shape through a page's
    /// /Annots[].FS instead of the catalog name tree — the PDF/UA-1 arm's own reach.</summary>
    private static PdfDocument BuildAnnotationFilespecDocument(PdfObject? f, PdfObject? uf, bool includeEf = true)
    {
        var doc = new PdfDocument();

        var specDict = new PdfDictionary { [N("Type")] = N("Filespec") };
        if (includeEf) specDict[N("EF")] = new PdfDictionary();
        if (f is not null) specDict[N("F")] = f;
        if (uf is not null) specDict[N("UF")] = uf;
        doc.AddObject(10, 0, specDict);

        var annotDict = new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N("FileAttachment"),
            [N("FS")] = Ref(10),
        };
        var pageDict = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Annots")] = new PdfArray(annotDict),
        };
        doc.AddObject(3, 0, pageDict);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void Fills_uf_from_f()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("report.txt", f: PdfString.FromText("report.txt"), uf: null).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        FileSpecNameRepair repair = Assert.Single(report.Repaired);
        Assert.Equal("report.txt", repair.Name);
        Assert.False(repair.WroteF);
        Assert.True(repair.WroteUf);
        Assert.Empty(report.Declined);
        Assert.Equal("report.txt", ((PdfString)Dict(editor.Document, 10).Get("UF")!).GetText());
    }

    [Fact]
    public void Fills_f_from_uf()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("report.txt", f: null, uf: PdfString.FromText("report.txt")).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        FileSpecNameRepair repair = Assert.Single(report.Repaired);
        Assert.Equal("report.txt", repair.Name);
        Assert.True(repair.WroteF);
        Assert.False(repair.WroteUf);
        Assert.Empty(report.Declined);
        Assert.Equal("report.txt", ((PdfString)Dict(editor.Document, 10).Get("F")!).GetText());
    }

    [Fact]
    public void Declines_a_filespec_carrying_neither_key()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("orphan.txt", f: null, uf: null).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        Assert.Empty(report.Repaired);
        Assert.Equal("orphan.txt", Assert.Single(report.Declined));
        Assert.Null(Dict(editor.Document, 10).Get("F"));
        Assert.Null(Dict(editor.Document, 10).Get("UF"));
    }

    [Fact]
    public void Declines_a_filespec_whose_only_present_key_is_empty()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("empty.txt", f: PdfString.FromText(""), uf: null).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        Assert.Empty(report.Repaired);
        Assert.Equal("empty.txt", Assert.Single(report.Declined));
        Assert.Null(Dict(editor.Document, 10).Get("UF"));
    }

    [Fact]
    public void Skips_a_filespec_with_both_keys()
    {
        using PdfDocumentEditor editor = BuildCatalogFilespecDocument(
            "both.txt", f: PdfString.FromText("both.txt"), uf: PdfString.FromText("both.txt")).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Declined);
    }

    /// <summary>Review fix (round 1): both /F and /UF structurally PRESENT is not the same as both
    /// USABLE. /F () is present-but-empty — still a genuine PDF/UA-1 7.11 violation
    /// (<c>EmbeddedFileSpecRule.NonEmpty</c>) even though PDF/A's presence-only test (:51) would call it
    /// fine. The repair must overwrite the empty /F with /UF's usable text, not take the
    /// both-keys-present skip branch <see cref="Skips_a_filespec_with_both_keys"/> exercises.</summary>
    [Fact]
    public void Repairs_f_when_f_is_present_but_empty_and_uf_is_usable()
    {
        using PdfDocumentEditor editor = BuildCatalogFilespecDocument(
            "partial.txt", f: PdfString.FromText(""), uf: PdfString.FromText("report.txt")).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        FileSpecNameRepair repair = Assert.Single(report.Repaired);
        Assert.Equal("report.txt", repair.Name);
        Assert.True(repair.WroteF);
        Assert.False(repair.WroteUf);
        Assert.Empty(report.Declined);
        Assert.Equal("report.txt", ((PdfString)Dict(editor.Document, 10).Get("F")!).GetText());
    }

    /// <summary>Mirror of <see cref="Repairs_f_when_f_is_present_but_empty_and_uf_is_usable"/> in the
    /// other direction: /UF () present-but-empty, /F usable — the empty /UF must be overwritten.</summary>
    [Fact]
    public void Repairs_uf_when_uf_is_present_but_empty_and_f_is_usable()
    {
        using PdfDocumentEditor editor = BuildCatalogFilespecDocument(
            "partial2.txt", f: PdfString.FromText("report.txt"), uf: PdfString.FromText("")).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        FileSpecNameRepair repair = Assert.Single(report.Repaired);
        Assert.Equal("report.txt", repair.Name);
        Assert.False(repair.WroteF);
        Assert.True(repair.WroteUf);
        Assert.Empty(report.Declined);
        Assert.Equal("report.txt", ((PdfString)Dict(editor.Document, 10).Get("UF")!).GetText());
    }

    /// <summary>Review fix (round 1) corollary: both keys PRESENT but BOTH empty means neither is
    /// usable — this is the "neither usable" decline branch, not the "both usable" skip branch, even
    /// though the old presence-only predicate would have taken the skip branch and silently done
    /// nothing (no repair, no decline visibility either).</summary>
    [Fact]
    public void Declines_a_filespec_whose_two_present_keys_are_both_empty()
    {
        using PdfDocumentEditor editor = BuildCatalogFilespecDocument(
            "bothempty.txt", f: PdfString.FromText(""), uf: PdfString.FromText("")).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        Assert.Empty(report.Repaired);
        Assert.Equal("bothempty.txt", Assert.Single(report.Declined));
    }

    [Fact]
    public void Skips_a_filespec_with_no_ef_entry()
    {
        using PdfDocumentEditor editor = BuildCatalogFilespecDocument(
            "noef.txt", f: PdfString.FromText("noef.txt"), uf: null, includeEf: false).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Declined);
        Assert.Null(Dict(editor.Document, 10).Get("UF"));
    }

    [Fact]
    public void Includes_annotation_filespecs_only_when_asked()
    {
        using PdfDocumentEditor editor =
            BuildAnnotationFilespecDocument(f: PdfString.FromText("attach.bin"), uf: null).Edit();

        FileSpecNameRepairReport withoutAnnotations = editor.RepairFileSpecNames(includeAnnotationSpecs: false);
        Assert.Empty(withoutAnnotations.Repaired);
        Assert.Empty(withoutAnnotations.Declined);
        Assert.Null(Dict(editor.Document, 10).Get("UF"));

        FileSpecNameRepairReport withAnnotations = editor.RepairFileSpecNames(includeAnnotationSpecs: true);
        FileSpecNameRepair repair = Assert.Single(withAnnotations.Repaired);
        Assert.Equal("attach.bin", repair.Name);
        Assert.True(repair.WroteUf);
        Assert.Equal("attach.bin", ((PdfString)Dict(editor.Document, 10).Get("UF")!).GetText());
    }

    [Fact]
    public void Is_idempotent()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("report.txt", f: PdfString.FromText("report.txt"), uf: null).Edit();

        FileSpecNameRepairReport first = editor.RepairFileSpecNames(includeAnnotationSpecs: false);
        Assert.Single(first.Repaired);
        string stateAfterFirst = Dict(editor.Document, 10).ToPdfString();

        FileSpecNameRepairReport second = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        Assert.Empty(second.Repaired);
        Assert.Empty(second.Declined);
        Assert.Equal(stateAfterFirst, Dict(editor.Document, 10).ToPdfString());
    }

    [Fact]
    public void Round_trips_a_non_latin1_name_through_utf16be()
    {
        const string name = "日本語.pdf";
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument(name, f: PdfString.FromText(name), uf: null).Edit();

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);

        FileSpecNameRepair repair = Assert.Single(report.Repaired);
        Assert.Equal(name, repair.Name);
        var uf = (PdfString)Dict(editor.Document, 10).Get("UF")!;
        Assert.Equal(name, uf.GetText());
        Assert.StartsWith("<FEFF", uf.ToPdfString());
    }

    // ── PreviewFileSpecNameRepairs (Task 5, 2026-08-21 font-dictionary and embedded-file remediation)
    // ── the read-only twin RepairFileSpecNames must never disagree with, since EmbeddedFileDomain.Propose
    // ── may only call the preview, never the write. Same fixtures as the write tests above, on purpose:
    // ── the point is that the two methods answer identically for identical document state.

    [Fact]
    public void Preview_reports_it_would_fill_uf_from_f_and_writes_nothing()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("report.txt", f: PdfString.FromText("report.txt"), uf: null).Edit();
        string before = Dict(editor.Document, 10).ToPdfString();

        FileSpecNameRepairPreview preview = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: false);

        FileSpecNameRepairCandidate candidate = Assert.Single(preview.WouldRepair);
        Assert.Equal("report.txt", candidate.Name);
        Assert.False(candidate.WouldWriteF);
        Assert.True(candidate.WouldWriteUf);
        Assert.Empty(preview.Declined);
        Assert.Null(Dict(editor.Document, 10).Get("UF"));
        Assert.Equal(before, Dict(editor.Document, 10).ToPdfString());
    }

    [Fact]
    public void Preview_reports_it_would_fill_f_from_uf()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("report.txt", f: null, uf: PdfString.FromText("report.txt")).Edit();

        FileSpecNameRepairPreview preview = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: false);

        FileSpecNameRepairCandidate candidate = Assert.Single(preview.WouldRepair);
        Assert.Equal("report.txt", candidate.Name);
        Assert.True(candidate.WouldWriteF);
        Assert.False(candidate.WouldWriteUf);
        Assert.Empty(preview.Declined);
        Assert.Null(Dict(editor.Document, 10).Get("F"));
    }

    [Fact]
    public void Preview_declines_a_filespec_carrying_neither_key_and_writes_nothing()
    {
        using PdfDocumentEditor editor =
            BuildCatalogFilespecDocument("orphan.txt", f: null, uf: null).Edit();

        FileSpecNameRepairPreview preview = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: false);

        Assert.Empty(preview.WouldRepair);
        Assert.Equal("orphan.txt", Assert.Single(preview.Declined));
        Assert.Null(Dict(editor.Document, 10).Get("F"));
        Assert.Null(Dict(editor.Document, 10).Get("UF"));
    }

    [Fact]
    public void Preview_reports_nothing_for_a_filespec_with_both_keys_already_usable()
    {
        using PdfDocumentEditor editor = BuildCatalogFilespecDocument(
            "both.txt", f: PdfString.FromText("both.txt"), uf: PdfString.FromText("both.txt")).Edit();

        FileSpecNameRepairPreview preview = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: false);

        Assert.Empty(preview.WouldRepair);
        Assert.Empty(preview.Declined);
    }

    [Fact]
    public void Preview_includes_annotation_filespecs_only_when_asked()
    {
        using PdfDocumentEditor editor =
            BuildAnnotationFilespecDocument(f: PdfString.FromText("attach.bin"), uf: null).Edit();

        FileSpecNameRepairPreview withoutAnnotations = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: false);
        Assert.Empty(withoutAnnotations.WouldRepair);
        Assert.Empty(withoutAnnotations.Declined);

        FileSpecNameRepairPreview withAnnotations = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: true);
        FileSpecNameRepairCandidate candidate = Assert.Single(withAnnotations.WouldRepair);
        Assert.Equal("attach.bin", candidate.Name);
        Assert.True(candidate.WouldWriteUf);
        Assert.Null(Dict(editor.Document, 10).Get("UF"));
    }

    /// <summary>The binding requirement: what the preview reports is EXACTLY what a subsequent repair
    /// call does, for a document mixing a repairable filespec (object 10) and a declined one (object
    /// 11) — proves the two methods share one walk and one predicate rather than merely happening to
    /// agree on today's single-filespec fixtures above.</summary>
    [Fact]
    public void Preview_and_repair_agree_on_a_mixed_document()
    {
        var doc = new PdfDocument();

        var repairable = new PdfDictionary { [N("Type")] = N("Filespec"), [N("EF")] = new PdfDictionary() };
        repairable[N("F")] = PdfString.FromText("report.txt");
        doc.AddObject(10, 0, repairable);

        var declinedSpec = new PdfDictionary { [N("Type")] = N("Filespec"), [N("EF")] = new PdfDictionary() };
        doc.AddObject(11, 0, declinedSpec);

        var namesArray = new PdfArray();
        namesArray.Add(PdfString.FromText("report.txt"));
        namesArray.Add(Ref(10));
        namesArray.Add(PdfString.FromText("orphan.txt"));
        namesArray.Add(Ref(11));
        var embeddedFilesLeaf = new PdfDictionary { [N("Names")] = namesArray };
        var namesDict = new PdfDictionary { [N("EmbeddedFiles")] = embeddedFilesLeaf };

        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(), [N("Count")] = new PdfInteger(0),
        });
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2), [N("Names")] = namesDict,
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        using PdfDocumentEditor editor = doc.Edit();

        FileSpecNameRepairPreview preview = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: false);
        FileSpecNameRepairCandidate previewed = Assert.Single(preview.WouldRepair);
        Assert.Equal("orphan.txt", Assert.Single(preview.Declined));

        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: false);
        FileSpecNameRepair repaired = Assert.Single(report.Repaired);

        Assert.Equal(previewed.Name, repaired.Name);
        Assert.Equal(previewed.WouldWriteF, repaired.WroteF);
        Assert.Equal(previewed.WouldWriteUf, repaired.WroteUf);
        Assert.Equal(preview.Declined, report.Declined);
    }
}
