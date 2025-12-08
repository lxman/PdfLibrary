namespace PdfLibrary.Builder.Annotation;

/// <summary>
/// Action that opens an external URI (URL)
/// </summary>
public class PdfUriAction : PdfLinkAction
{
    public override string ActionType => "URI";

    /// <summary>
    /// The URI to open
    /// </summary>
    public string Uri { get; }

    public PdfUriAction(string uri)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
    }
}