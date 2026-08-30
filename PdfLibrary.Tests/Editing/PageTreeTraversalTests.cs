using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Issue 91: every document/editing page view must share one recursive, cycle-safe order.</summary>
public class PageTreeTraversalTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    private static PdfDocument NestedDocument(bool cycle = false, bool duplicateFirstPage = false)
    {
        var document = new PdfDocument();
        document.AddObject(4, 0, Page(3, "FIRST"));
        document.AddObject(5, 0, Page(3, "SECOND"));

        var branchKids = new PdfArray(Ref(4));
        if (cycle)
            branchKids.Add(Ref(2));
        branchKids.Add(Ref(5));
        if (duplicateFirstPage)
            branchKids.Add(Ref(4));

        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Parent")] = Ref(2),
            [N("Kids")] = branchKids,
            [N("Count")] = new PdfInteger(duplicateFirstPage ? 3 : 2),
            [N("MediaBox")] = Box(),
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(duplicateFirstPage ? 3 : 2),
        });
        document.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(2),
        });
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfDictionary Page(int parent, string marker) => new()
    {
        [N("Type")] = N("Page"),
        [N("Parent")] = Ref(parent),
        [N("Marker")] = PdfString.FromText(marker),
    };

    private static PdfArray Box() =>
        new(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792));

    private static string Marker(PdfDictionary page) => ((PdfString)page.Get("Marker")!).GetText();

    [Fact]
    public void Document_and_editing_views_share_recursive_cycle_safe_deduplicated_order()
    {
        using PdfDocument document = NestedDocument(cycle: true, duplicateFirstPage: true);
        string rootBefore = document.PageTreeRootDictionary!.ToPdfString();

        IReadOnlyList<PdfDictionary> editingPages = PageTreeOps.PageDicts(document);
        List<PdfLibrary.Document.PdfPage> documentPages = document.GetPages();
        using var editor = new PdfDocumentEditor(document);

        Assert.Equal(["FIRST", "SECOND"], editingPages.Select(Marker));
        Assert.Equal(editingPages, documentPages.Select(page => page.Dictionary));
        Assert.Equal(documentPages.Count, editor.Pages.Count);
        Assert.Equal(["FIRST", "SECOND"], editor.Pages.Select(page => Marker(page.Dictionary)));
        Assert.Same(editingPages[1], editor.Pages[1].Dictionary);
        Assert.Equal(rootBefore, document.PageTreeRootDictionary.ToPdfString());
    }

    [Fact]
    public void Read_helpers_do_not_create_missing_page_tree_entries()
    {
        using PdfDocument document = NestedDocument();
        PdfDictionary root = document.PageTreeRootDictionary!;
        root.Remove(N("Kids"));

        Assert.Empty(PageTreeOps.Kids(document));
        Assert.Empty(PageTreeOps.PageDicts(document));
        Assert.False(root.ContainsKey(N("Kids")));
    }

    [Fact]
    public void Edit_normalization_terminates_on_cycles_and_keeps_each_page_once()
    {
        using PdfDocument document = NestedDocument(cycle: true, duplicateFirstPage: true);

        using PdfDocumentEditor editor = document.Edit();

        Assert.Equal(2, editor.Pages.Count);
        PdfArray rootKids = (PdfArray)document.PageTreeRootDictionary!.Get("Kids")!;
        Assert.Equal(2, rootKids.Count);
        Assert.All(rootKids, kid => Assert.IsType<PdfIndirectReference>(kid));
        Assert.Equal(new[] { "FIRST", "SECOND" }, editor.Pages.Select(page => Marker(page.Dictionary)));
    }

    [Fact]
    public void Page_mutations_accept_a_tree_nested_after_the_editor_was_created()
    {
        using PdfDocument document = NestedDocument();
        using var editor = new PdfDocumentEditor(document);

        editor.Pages.Move(0, 1);
        editor.Pages.Rotate(0, 90);
        editor.Pages.InsertBlank(1, 200, 300);

        Assert.Equal(3, editor.Pages.Count);
        Assert.Equal("SECOND", Marker(editor.Pages[0].Dictionary));
        Assert.Equal(90, (int)((PdfInteger)editor.Pages[0].Dictionary.Get("Rotate")!).Value);
        Assert.Equal(200, editor.Pages[1].Width, 3);
        Assert.Equal("FIRST", Marker(editor.Pages[2].Dictionary));
    }

    [Fact]
    public void Destination_repair_finds_links_on_nested_pages()
    {
        using PdfDocument document = NestedDocument();
        PdfDictionary first = (PdfDictionary)document.GetObject(4)!;
        PdfDictionary second = (PdfDictionary)document.GetObject(5)!;
        first[N("Annots")] = new PdfArray(new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N("Link"),
            [N("Dest")] = new PdfArray(Ref(5), N("Fit")),
        });

        DestinationRepairer.OnPageRemoved(document, second);

        Assert.Empty((PdfArray)first.Get("Annots")!);
    }

    [Fact]
    public void Annotation_filespec_repair_finds_a_nested_page()
    {
        using PdfDocument document = NestedDocument();
        var spec = new PdfDictionary
        {
            [N("Type")] = N("Filespec"),
            [N("EF")] = new PdfDictionary(),
            [N("F")] = PdfString.FromText("nested.bin"),
        };
        document.AddObject(10, 0, spec);
        ((PdfDictionary)document.GetObject(5)!)[N("Annots")] = new PdfArray(new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N("FileAttachment"),
            [N("FS")] = Ref(10),
        });
        using var editor = new PdfDocumentEditor(document);

        FileSpecNameRepairPreview preview = editor.PreviewFileSpecNameRepairs(includeAnnotationSpecs: true);
        FileSpecNameRepairReport report = editor.RepairFileSpecNames(includeAnnotationSpecs: true);

        Assert.Equal("nested.bin", Assert.Single(preview.WouldRepair).Name);
        Assert.Equal("nested.bin", Assert.Single(report.Repaired).Name);
        Assert.Equal("nested.bin", ((PdfString)spec.Get("UF")!).GetText());
    }

    [Fact]
    public void Merge_copies_nested_source_pages_once_in_document_order()
    {
        using PdfDocument source = NestedDocument();
        using PdfDocument merged = PdfDocumentEditor.Merge([source]);

        Assert.Equal(2, merged.GetPages().Count);
        Assert.Equal(
            new[] { "FIRST", "SECOND" },
            merged.GetPages().Select(page => Marker(page.Dictionary)));
    }
}
