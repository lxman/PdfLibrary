namespace PdfLibrary.Editing;

/// <summary>Options for <see cref="PdfDocumentEditor.Save(System.IO.Stream, PdfSaveOptions)"/>.</summary>
public sealed class PdfSaveOptions
{
    /// <summary>Drop objects no longer reachable from the catalog/info (e.g. deleted pages). Default true.</summary>
    public bool RemoveOrphans { get; set; } = true;

    /// <summary>Pack output using object streams + a cross-reference stream. Default false (classic xref).</summary>
    public bool UseObjectStreams { get; set; }
}
