using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class ObjectGraphClonerTests
{
    private static PdfObject? Deref(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : o;

    [Fact]
    public void Clone_SharedObject_IsCopiedOnce()
    {
        var source = new PdfDocument();
        var font = new PdfDictionary();
        font[PdfName.TypeName] = new PdfName("Font");
        font[PdfName.Subtype] = new PdfName("Type1");
        font[new PdfName("BaseFont")] = new PdfName("Helvetica");
        source.AddObject(7, 0, font);

        var fontDict = new PdfDictionary();
        fontDict[new PdfName("F1")] = new PdfIndirectReference(7, 0);
        fontDict[new PdfName("F2")] = new PdfIndirectReference(7, 0);
        var resources = new PdfDictionary();
        resources[new PdfName("Font")] = fontDict;
        source.AddObject(6, 0, resources);

        var page = new PdfDictionary();
        page[PdfName.TypeName] = new PdfName("Page");
        page[new PdfName("MediaBox")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(10), new PdfReal(10));
        page[new PdfName("Resources")] = new PdfIndirectReference(6, 0);
        source.AddObject(5, 0, page);

        PdfDocument target = PdfDocument.CreateEmpty();
        PdfIndirectReference cloned = ObjectGraphCloner.CloneInto(target, source, page);

        var clonedPage = (PdfDictionary)target.GetObject(cloned.ObjectNumber)!;
        var res = (PdfDictionary)Deref(target, clonedPage[new PdfName("Resources")])!;
        var fonts = (PdfDictionary)res[new PdfName("Font")];
        var f1 = (PdfIndirectReference)fonts[new PdfName("F1")];
        var f2 = (PdfIndirectReference)fonts[new PdfName("F2")];
        Assert.Equal(f1.ObjectNumber, f2.ObjectNumber);

        int fontCopies = target.Objects.Values.Count(o =>
            o is PdfDictionary d && d.TryGetValue(PdfName.Subtype, out PdfObject s) && s is PdfName { Value: "Type1" });
        Assert.Equal(1, fontCopies);
    }

    [Fact]
    public void Clone_BackReferenceToPage_Terminates_AndRewires()
    {
        var source = new PdfDocument();
        var annot = new PdfDictionary();
        annot[PdfName.Subtype] = new PdfName("Widget");
        annot[new PdfName("P")] = new PdfIndirectReference(5, 0);
        source.AddObject(10, 0, annot);

        var page = new PdfDictionary();
        page[PdfName.TypeName] = new PdfName("Page");
        page[new PdfName("MediaBox")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(10), new PdfReal(10));
        page[new PdfName("Annots")] = new PdfArray(new PdfIndirectReference(10, 0));
        source.AddObject(5, 0, page);

        PdfDocument target = PdfDocument.CreateEmpty();
        PdfIndirectReference cloned = ObjectGraphCloner.CloneInto(target, source, page);

        var clonedPage = (PdfDictionary)target.GetObject(cloned.ObjectNumber)!;
        var annots = (PdfArray)clonedPage[new PdfName("Annots")];
        var clonedAnnot = (PdfDictionary)Deref(target, annots[0])!;
        var p = (PdfIndirectReference)clonedAnnot[new PdfName("P")];
        Assert.Equal(cloned.ObjectNumber, p.ObjectNumber);

        int pageCopies = target.Objects.Values.Count(o =>
            o is PdfDictionary d && d.TryGetValue(PdfName.TypeName, out PdfObject t) && t is PdfName { Value: "Page" });
        Assert.Equal(1, pageCopies);
    }

    [Fact]
    public void Clone_StreamBytes_ArePreserved()
    {
        var source = new PdfDocument();
        byte[] content = "BT /F1 12 Tf (hi) Tj ET"u8.ToArray();
        var stream = new PdfStream(new PdfDictionary(), content);
        source.AddObject(8, 0, stream);

        var page = new PdfDictionary();
        page[PdfName.TypeName] = new PdfName("Page");
        page[new PdfName("MediaBox")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(10), new PdfReal(10));
        page[new PdfName("Contents")] = new PdfIndirectReference(8, 0);
        source.AddObject(5, 0, page);

        PdfDocument target = PdfDocument.CreateEmpty();
        PdfIndirectReference cloned = ObjectGraphCloner.CloneInto(target, source, page);

        var clonedPage = (PdfDictionary)target.GetObject(cloned.ObjectNumber)!;
        var clonedStream = (PdfStream)Deref(target, clonedPage[new PdfName("Contents")])!;
        Assert.Equal(content, clonedStream.GetDecodedData());
    }
}
