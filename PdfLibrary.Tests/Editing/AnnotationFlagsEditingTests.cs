using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Reading and writing annotation /F flags — the capability PDF/A's 6.3.2 repair needs.
///
/// <para>This type knows nothing about conformance: it does not know Print is required, and it does
/// not know that clearing a hiding bit reveals content. That policy lives in Pellucid's
/// AnnotationsDomain, exactly as XmpPacket.RemoveStructField knows how to remove a struct field but
/// not which fields are safe to remove.</para></summary>
public sealed class AnnotationFlagsEditingTests
{
    /// <summary>A one-page document with a link annotation carrying the given /F, or no /F when null.
    /// Built through the document graph rather than the annotation API because that API has no way to
    /// author a specific flags value — which is the gap this task closes.</summary>
    private static PdfDocumentEditor EditorWithAnnotation(int? flags)
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 72, 700, "Helvetica", 12))
            .ToByteArray();

        var editor = PdfDocumentEditor.Open(new MemoryStream(bytes));
        PdfDocument doc = editor.Document;

        var annot = new PdfDictionary();
        annot[new PdfName("Type")] = new PdfName("Annot");
        annot[new PdfName("Subtype")] = new PdfName("Link");
        annot[new PdfName("Rect")] = new PdfArray(
        [
            new PdfInteger(10), new PdfInteger(10), new PdfInteger(100), new PdfInteger(50),
        ]);
        if (flags is { } f) annot[new PdfName("F")] = new PdfInteger(f);

        int objectNumber = NextFreeObjectNumber(doc);
        doc.AddObject(objectNumber, 0, annot);

        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        page[new PdfName("Annots")] = new PdfArray([new PdfIndirectReference(objectNumber, 0)]);
        return editor;
    }

    /// <summary>One past the highest object number in use. PdfDocument exposes no allocator, so the
    /// fixture computes one; anything unused works because these documents are built here and thrown
    /// away.</summary>
    private static int NextFreeObjectNumber(PdfDocument doc)
    {
        var n = 1;
        while (doc.GetObject(n) is not null) n++;
        return n;
    }

    [Fact]
    public void Reads_the_flags_value_when_present()
    {
        PdfDocumentEditor editor = EditorWithAnnotation(0x24);

        PdfAnnotationInfo annot = Assert.Single(editor.Pages.GetAnnotations(0));

        Assert.Equal(0x24, annot.Flags);
    }

    /// <summary>Absent /F reads as null, NOT as 0. The two are different document states — an absent
    /// /F trips 6.3.2's first sub-test and a zero /F trips its second — and a repair that could not
    /// tell them apart would report the wrong thing about 99.99% of real findings.</summary>
    [Fact]
    public void Reads_null_when_the_flags_entry_is_absent()
    {
        PdfDocumentEditor editor = EditorWithAnnotation(null);

        PdfAnnotationInfo annot = Assert.Single(editor.Pages.GetAnnotations(0));

        Assert.Null(annot.Flags);
    }

    [Fact]
    public void Reads_zero_as_zero_not_null()
    {
        PdfDocumentEditor editor = EditorWithAnnotation(0);

        PdfAnnotationInfo annot = Assert.Single(editor.Pages.GetAnnotations(0));

        Assert.Equal(0, annot.Flags);
    }

    [Fact]
    public void Writes_the_flags_value()
    {
        PdfDocumentEditor editor = EditorWithAnnotation(null);
        int id = editor.Pages.GetAnnotations(0)[0].AnnotationId;

        editor.Pages.SetAnnotationFlags(0, id, 4);

        Assert.Equal(4, Assert.Single(editor.Pages.GetAnnotations(0)).Flags);
    }

    /// <summary>The write survives a save/reload — it edited the document, not a snapshot.</summary>
    [Fact]
    public void The_written_flags_survive_a_round_trip()
    {
        PdfDocumentEditor editor = EditorWithAnnotation(null);
        int id = editor.Pages.GetAnnotations(0)[0].AnnotationId;
        editor.Pages.SetAnnotationFlags(0, id, 4);

        using var saved = new MemoryStream();
        editor.Save(saved);
        var reloaded = PdfDocumentEditor.Open(new MemoryStream(saved.ToArray()));

        Assert.Equal(4, Assert.Single(reloaded.Pages.GetAnnotations(0)).Flags);
    }

    /// <summary>An unknown id changes nothing and does not throw. Callers ask about documents they
    /// have not inspected annotation by annotation, so "nothing to do" must be an ordinary answer —
    /// the same contract RemoveAnnotation already has.</summary>
    [Fact]
    public void An_unknown_annotation_id_is_a_no_op()
    {
        PdfDocumentEditor editor = EditorWithAnnotation(8);

        editor.Pages.SetAnnotationFlags(0, 99999, 4);

        Assert.Equal(8, Assert.Single(editor.Pages.GetAnnotations(0)).Flags);
    }
}
