using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;
using SkiaSharp;

namespace PdfLibrary.Tests.Editing;

public class ExplicitResourceRepairTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);
    private static byte[] Ops(string value) => Encoding.ASCII.GetBytes(value);

    private static Finding[] Findings(PdfDocument document) =>
        [.. new ExplicitResourcesRule()
            .Check(new ConformanceContext(document, ConformanceProfile.PdfA2b))];

    private static PdfArray Box(int width = 100, int height = 100) =>
        new(new PdfInteger(0), new PdfInteger(0), new PdfInteger(width), new PdfInteger(height));

    private static PdfDocument PageDocument(bool nestedPageTree = false, bool indirectResources = true)
    {
        var document = new PdfDocument();
        document.AddObject(10, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"), [N("BBox")] = Box() },
            Ops("1 0 0 rg 0 0 20 20 re f\n")));
        document.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops("/X0 Do\n")));

        PdfObject resources = new PdfDictionary
        {
            [N("XObject")] = new PdfDictionary { [N("X0")] = Ref(10) },
        };
        if (indirectResources)
        {
            document.AddObject(20, 0, resources);
            resources = Ref(20);
        }

        int parent = nestedPageTree ? 5 : 2;
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(parent),
            [N("MediaBox")] = Box(), [N("Contents")] = Ref(4),
        });

        if (nestedPageTree)
        {
            document.AddObject(5, 0, new PdfDictionary
            {
                [N("Type")] = N("Pages"), [N("Parent")] = Ref(2),
                [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
            });
        }

        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(nestedPageTree ? 5 : 3)),
            [N("Count")] = new PdfInteger(1), [N("Resources")] = resources,
        });
        document.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfDocument FormDocument(bool partialDirectResources = false)
    {
        var document = new PdfDocument();
        document.AddObject(11, 0, new PdfStream(
            new PdfDictionary { [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"), [N("BBox")] = Box(50, 50) },
            Ops("1 0 0 rg 0 0 50 50 re f\n")));

        var outerDictionary = new PdfDictionary
        {
            [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"), [N("BBox")] = Box(50, 50),
        };
        if (partialDirectResources)
            outerDictionary[N("Resources")] = new PdfDictionary { [N("ProcSet")] = new PdfArray(N("PDF")) };
        document.AddObject(10, 0, new PdfStream(outerDictionary, Ops("/X1 Do\n")));

        var pageResources = new PdfDictionary
        {
            [N("XObject")] = new PdfDictionary { [N("X0")] = Ref(10), [N("X1")] = Ref(11) },
        };
        document.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops("q 1 0 0 1 10 10 cm /X0 Do Q\n")));
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("MediaBox")] = Box(),
            [N("Contents")] = Ref(4), [N("Resources")] = pageResources,
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        document.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    private static PdfDocument Type3Document()
    {
        var document = new PdfDocument();
        document.AddObject(20, 0, new PdfStream(new PdfDictionary(), Ops("1000 0 d0 /CS0 cs 0.5 sc 0 0 500 500 re f\n")));
        document.AddObject(21, 0, new PdfDictionary { [N("glyph")] = Ref(20) });
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"), [N("Subtype")] = N("Type3"), [N("FontBBox")] = Box(500, 500),
            [N("FontMatrix")] = new PdfArray(new PdfReal(.001), new PdfInteger(0), new PdfInteger(0), new PdfReal(.001), new PdfInteger(0), new PdfInteger(0)),
            [N("CharProcs")] = Ref(21), [N("Encoding")] = new PdfDictionary(),
            [N("FirstChar")] = new PdfInteger(0), [N("LastChar")] = new PdfInteger(0),
            [N("Widths")] = new PdfArray(new PdfInteger(500)),
        };
        document.AddObject(10, 0, font);
        document.AddObject(12, 0, new PdfArray(N("CalGray")));
        document.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops("BT /F1 12 Tf (x) Tj ET\n")));
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("MediaBox")] = Box(), [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F1")] = Ref(10) },
                [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = Ref(12) },
            },
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        document.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        document.Trailer.Dictionary[N("Root")] = Ref(1);
        return document;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Page_repair_materializes_the_nearest_resources_reference_and_handles_nested_page_trees(bool nested)
    {
        using PdfDocument document = PageDocument(nested);
        var editor = new PdfDocumentEditor(document);
        PdfDictionary page = (PdfDictionary)document.Objects[3];
        PdfObject inherited = ((PdfDictionary)document.Objects[2]).Get("Resources")!;

        ExplicitResourceRepairCandidate candidate = Assert.Single(editor.PreviewExplicitResourceRepairs().Candidates);
        Assert.Equal(3, candidate.ObjectNumber);
        Assert.Equal(ExplicitResourceOwnerKind.Page, candidate.OwnerKind);

        ExplicitResourceRepairReport report = editor.RepairExplicitResources(new HashSet<int> { 3 });

        Assert.Single(report.Applied);
        Assert.Empty(report.Refused);
        Assert.Same(inherited, page.Get("Resources"));
        Assert.Empty(Findings(document));
        Assert.Empty(editor.RepairExplicitResources().Applied); // idempotent
    }

    [Fact]
    public void Form_repair_materializes_the_invoking_scope_resources_and_preserves_rendering_after_round_trip()
    {
        using PdfDocument document = FormDocument();
        byte[] before = RenderPixels(document.GetPage(0)!);
        var editor = new PdfDocumentEditor(document);

        ExplicitResourceRepairCandidate candidate = Assert.Single(editor.PreviewExplicitResourceRepairs().Candidates);
        Assert.Equal(10, candidate.ObjectNumber);
        Assert.Equal(ExplicitResourceOwnerKind.FormXObject, candidate.OwnerKind);
        ExplicitResourceRepairReport report = editor.RepairExplicitResources(new HashSet<int> { 10 });
        Assert.Single(report.Applied);
        Assert.Empty(Findings(document));

        using var output = new MemoryStream();
        editor.Save(output);
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(output.ToArray()));
        Assert.Equal(before, RenderPixels(reloaded.GetPage(0)!));
        Assert.Empty(Findings(reloaded));
    }

    [Fact]
    public void Type3_repair_materializes_the_invoking_scope_resources()
    {
        using PdfDocument document = Type3Document();
        var editor = new PdfDocumentEditor(document);

        ExplicitResourceRepairCandidate candidate = Assert.Single(editor.PreviewExplicitResourceRepairs().Candidates);
        Assert.Equal(10, candidate.ObjectNumber);
        Assert.Equal(ExplicitResourceOwnerKind.Type3Font, candidate.OwnerKind);

        Assert.Single(editor.RepairExplicitResources(new HashSet<int> { 10 }).Applied);
        Assert.NotNull(((PdfDictionary)document.Objects[10]).Get("Resources"));
        Assert.Empty(Findings(document));
    }

    [Fact]
    public void Existing_partial_resources_are_refused_not_merged_or_replaced()
    {
        using PdfDocument document = FormDocument(partialDirectResources: true);
        var editor = new PdfDocumentEditor(document);
        PdfDictionary form = ((PdfStream)document.Objects[10]).Dictionary;
        PdfObject original = form.Get("Resources")!;

        ExplicitResourceRepairPreview preview = editor.PreviewExplicitResourceRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains("not merge", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);

        ExplicitResourceRepairReport report = editor.RepairExplicitResources(new HashSet<int> { 10 });
        Assert.Empty(report.Applied);
        Assert.Single(report.Refused);
        Assert.Same(original, form.Get("Resources"));
    }

    [Fact]
    public void A_form_invoked_with_different_effective_resources_is_refused_as_ambiguous()
    {
        using PdfDocument document = FormDocument();
        PdfDictionary pages = (PdfDictionary)document.Objects[2];
        PdfDictionary firstPage = (PdfDictionary)document.Objects[3];
        PdfDictionary secondResources = new()
        {
            [N("XObject")] = new PdfDictionary { [N("X0")] = Ref(10), [N("X1")] = Ref(11) },
        };
        document.AddObject(5, 0, new PdfStream(new PdfDictionary(), Ops("/X0 Do\n")));
        document.AddObject(6, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("MediaBox")] = Box(),
            [N("Contents")] = Ref(5), [N("Resources")] = secondResources,
        });
        pages[N("Kids")] = new PdfArray(Ref(3), Ref(6));
        pages[N("Count")] = new PdfInteger(2);
        Assert.NotSame(firstPage.Get("Resources"), secondResources);

        var editor = new PdfDocumentEditor(document);
        ExplicitResourceRepairPreview preview = editor.PreviewExplicitResourceRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Contains("different effective", Assert.Single(preview.Refused).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Staged_object_numbers_are_an_exact_selection()
    {
        using PdfDocument document = FormDocument();
        var editor = new PdfDocumentEditor(document);

        ExplicitResourceRepairReport report = editor.RepairExplicitResources(new HashSet<int> { 999 });

        Assert.Empty(report.Applied);
        Assert.Equal(999, Assert.Single(report.Refused).ObjectNumber);
        Assert.Null(((PdfStream)document.Objects[10]).Dictionary.Get("Resources"));
    }

    private static byte[] RenderPixels(PdfPage page)
    {
        using SKImage image = page.RenderTo().ToImage();
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }
}
