using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing;

/// <summary>Mutation facade over a loaded <see cref="PdfDocument"/>. Obtain via <see cref="PdfDocument.Edit"/>.</summary>
public sealed partial class PdfDocumentEditor : IDisposable
{
    private readonly PdfDocument _document;
    private bool _ownsDocument;

    internal PdfDocumentEditor(PdfDocument document)
    {
        _document = document;
        Pages = new PdfPageCollection(document);
    }

    /// <summary>The document's pages and page operations.</summary>
    public PdfPageCollection Pages { get; }

    /// <summary>Disposes the underlying document only if this editor created it (via <see cref="Open(string, string)"/>/<see cref="CreateBlank"/>).</summary>
    public void Dispose()
    {
        if (_ownsDocument) _document.Dispose();
    }

    /// <summary>Loads a document and enters edit mode.</summary>
    public static PdfDocumentEditor Open(string path, string? password = null)
    {
        PdfDocument document = PdfDocument.Load(path, password ?? "");
        PdfDocumentEditor editor = document.Edit();
        editor._ownsDocument = true;
        return editor;
    }

    /// <summary>Loads a document from a stream and enters edit mode.</summary>
    /// <param name="stream">Stream containing the PDF.</param>
    /// <param name="password">Password for encrypted documents (null/empty for none).</param>
    /// <param name="leaveOpen">If false, the stream is disposed when the editor (which owns the document) is disposed.</param>
    public static PdfDocumentEditor Open(Stream stream, string? password = null, bool leaveOpen = false)
    {
        PdfDocument document = PdfDocument.Load(stream, password ?? "", leaveOpen);
        PdfDocumentEditor editor = document.Edit();
        editor._ownsDocument = true;
        return editor;
    }

    /// <summary>Creates a blank, editable document (zero pages).</summary>
    public static PdfDocumentEditor CreateBlank()
    {
        var document = PdfDocument.CreateEmpty();
        PdfDocumentEditor editor = document.Edit();
        editor._ownsDocument = true;
        return editor;
    }

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
        var target = PdfDocument.CreateEmpty();
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
        var target = PdfDocument.CreateEmpty();
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
        AcroFormMerger.MergeImportedFields(target, source, (PdfDictionary)target.GetObject(newRef.ObjectNumber)!);
    }
}
