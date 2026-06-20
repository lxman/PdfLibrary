using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Stamping;

/// <summary>
/// Compiles authored stamp content into a self-contained Form XObject and clones it into a target
/// document. Reuses the creation builder for content generation and the editing cloner for cross-doc copy.
/// </summary>
internal static class FormXObjectCompiler
{
    /// <summary>Builds a Form XObject of size width×height from <paramref name="author"/> and deep-copies
    /// it (with its resources) into <paramref name="target"/>; returns the target-side XObject reference.</summary>
    public static PdfIndirectReference CompileInto(PdfDocument target, double width, double height,
        Action<PdfPageBuilder> author)
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(new PdfSize(width, height), author)
            .ToByteArray();

        using PdfDocument stamp = PdfDocument.Load(new MemoryStream(bytes));
        PdfPage page = stamp.GetPage(0)
            ?? throw new InvalidOperationException("Stamp compilation produced no page.");

        byte[] content = ConcatDecoded(page.GetContents());

        var xobjDict = new PdfDictionary();
        xobjDict[PdfName.TypeName] = new PdfName("XObject");
        xobjDict[PdfName.Subtype] = new PdfName("Form");
        xobjDict[new PdfName("BBox")] =
            new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height));

        PdfObject? res = page.Dictionary.Get(new PdfName("Resources"));
        if (res is PdfIndirectReference rr) res = stamp.GetObject(rr.ObjectNumber);
        xobjDict[new PdfName("Resources")] = res is PdfDictionary resDict ? resDict : new PdfDictionary();

        var xobjStream = new PdfStream(xobjDict, content);
        PdfIndirectReference srcRef = stamp.RegisterObject(xobjStream);

        return (PdfIndirectReference)ObjectGraphCloner.CloneValue(target, stamp, srcRef);
    }

    private static byte[] ConcatDecoded(List<PdfStream> streams)
    {
        using var ms = new MemoryStream();
        foreach (PdfStream s in streams)
        {
            byte[] data = s.GetDecodedData();
            ms.Write(data, 0, data.Length);
            ms.WriteByte((byte)'\n');
        }
        return ms.ToArray();
    }
}
