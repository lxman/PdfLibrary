using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>Mutation facade over a loaded <see cref="PdfDocument"/>. Obtain via <see cref="PdfDocument.Edit"/>.</summary>
public sealed class PdfDocumentEditor
{
    private readonly PdfDocument _document;

    internal PdfDocumentEditor(PdfDocument document)
    {
        _document = document;
        Pages = new PdfPageCollection(document);
    }

    /// <summary>The document's pages and page operations.</summary>
    public PdfPageCollection Pages { get; }

    /// <summary>Loads a document and enters edit mode.</summary>
    public static PdfDocumentEditor Open(string path, string? password = null) =>
        PdfDocument.Load(path, password ?? "").Edit();

    /// <summary>Creates a blank, editable document (zero pages).</summary>
    public static PdfDocumentEditor CreateBlank() => PdfDocument.CreateEmpty().Edit();

    public void Save(string path, PdfSaveOptions? options = null)
    {
        using FileStream stream = File.Create(path);
        Save(stream, options);
    }

    public void Save(Stream stream, PdfSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new PdfSaveOptions();
        ISet<int>? live = options.RemoveOrphans ? ObjectGraphWalker.CollectReachable(_document) : null;
        if (options.UseObjectStreams)
            ObjectStreamWriter.Write(_document, stream, live);
        else
            PdfDocumentSerializer.Write(_document, stream, live);
    }

    public void Append(PdfDocument source) => Pages.Append(source);

    public PdfDocument Extract(int start, int count)
    {
        PdfDocument target = PdfDocument.CreateEmpty();
        for (var i = 0; i < count; i++)
        {
            PdfPage srcPage = _document.GetPage(start + i)
                ?? throw new ArgumentOutOfRangeException(nameof(start));
            AppendClonedPage(target, _document, srcPage);
        }
        return target;
    }

    public static PdfDocument Merge(IEnumerable<PdfDocument> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        PdfDocument target = PdfDocument.CreateEmpty();
        foreach (PdfDocument source in sources)
        {
            int count = source.PageCount;
            for (var i = 0; i < count; i++)
                AppendClonedPage(target, source, source.GetPage(i)!);
        }
        return target;
    }

    private static void AppendClonedPage(PdfDocument target, PdfDocument source, PdfPage srcPage)
    {
        PdfIndirectReference newRef = ObjectGraphCloner.CloneInto(target, source, srcPage.Dictionary);
        PageTreeOps.InsertPageRef(target, newRef, PageTreeOps.PageDicts(target).Count);
    }
}
