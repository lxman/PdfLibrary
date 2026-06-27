using PdfLibrary.Builder;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// One visual placement (widget annotation) of a form field: where it sits and on which page.
/// A field may have several (e.g. a radio group, or the same field repeated across pages).
/// </summary>
public sealed class PdfFieldWidget
{
    /// <summary>0-based index of the page this widget annotation appears on.</summary>
    public int PageIndex { get; }

    /// <summary>Widget rectangle in PDF user space (Y-up). Map to pixels via PageGeometry.</summary>
    public PdfRect Rect { get; }

    /// <summary>The "on" appearance state for checkbox/radio widgets (/AP /N key); null otherwise.</summary>
    public string? OnStateName { get; }

    /// <summary>The field this widget belongs to.</summary>
    public PdfFormField Field { get; }

    internal PdfFieldWidget(PdfFormField field, int pageIndex, PdfRect rect, string? onStateName)
    {
        Field = field;
        PageIndex = pageIndex;
        Rect = rect;
        OnStateName = onStateName;
    }
}
