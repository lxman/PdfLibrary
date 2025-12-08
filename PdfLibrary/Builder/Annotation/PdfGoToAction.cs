using PdfLibrary.Builder.Bookmark;

namespace PdfLibrary.Builder.Annotation;

/// <summary>
/// Action that navigates to a page destination within the document
/// </summary>
public class PdfGoToAction : PdfLinkAction
{
    public override string ActionType => "GoTo";

    /// <summary>
    /// The destination to navigate to
    /// </summary>
    public PdfDestination Destination { get; }

    public PdfGoToAction(PdfDestination destination)
    {
        Destination = destination;
    }

    public PdfGoToAction(int pageIndex)
    {
        Destination = new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.Fit };
    }
}