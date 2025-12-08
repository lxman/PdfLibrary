using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Annotation;

/// <summary>
/// Text annotation (sticky note / comment)
/// </summary>
public class PdfTextAnnotation : PdfAnnotation
{
    public override string Subtype => "Text";

    /// <summary>
    /// The text content of the note
    /// </summary>
    public string Contents { get; internal set; } = string.Empty;

    /// <summary>
    /// The icon name for the note
    /// </summary>
    public PdfTextAnnotationIcon Icon { get; internal set; } = PdfTextAnnotationIcon.Note;

    /// <summary>
    /// Whether the note popup is initially open
    /// </summary>
    public bool IsOpen { get; internal set; }

    /// <summary>
    /// Color of the annotation icon
    /// </summary>
    public PdfColor? Color { get; internal set; }

    internal PdfTextAnnotation(PdfRect rect, string contents) : base(rect)
    {
        Contents = contents;
    }
}