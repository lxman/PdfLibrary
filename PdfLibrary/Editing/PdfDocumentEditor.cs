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
}
