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
}
