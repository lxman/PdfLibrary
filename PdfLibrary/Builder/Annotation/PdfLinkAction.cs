namespace PdfLibrary.Builder.Annotation;

/// <summary>
/// Base class for link actions
/// </summary>
public abstract class PdfLinkAction
{
    public abstract string ActionType { get; }
}