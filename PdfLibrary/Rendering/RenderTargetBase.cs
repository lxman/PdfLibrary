using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;

namespace PdfLibrary.Rendering;

/// <summary>
/// Abstract base class for IRenderTarget implementations.
/// Provides default implementations for page lifecycle methods,
/// simplifying migration from previous IRenderTarget versions.
/// </summary>
public abstract class RenderTargetBase : IRenderTarget
{
    // ==================== PAGE LIFECYCLE ====================

    /// <summary>
    /// Current page number being rendered (1-based).
    /// </summary>
    public virtual int CurrentPageNumber { get; protected set; }

    /// <summary>
    /// Begin rendering a new page.
    /// Default implementation updates CurrentPageNumber and calls Clear().
    /// Override to add platform-specific initialization.
    /// </summary>
    public virtual void BeginPage(int pageNumber, double width, double height)
    {
        CurrentPageNumber = pageNumber;
        Clear();
    }

    /// <summary>
    /// Complete rendering of current page.
    /// Default implementation is a no-op.
    /// Override to add platform-specific finalization (flush buffers, update layout, etc.).
    /// </summary>
    public virtual void EndPage()
    {
        // Default: no-op
        // Derived classes override if they need to flush buffers, finalize layout, etc.
    }

    /// <summary>
    /// Clear all rendered content and reset state.
    /// Default implementation is a no-op.
    /// Derived classes should override to clear platform-specific rendering surfaces.
    /// </summary>
    public virtual void Clear()
    {
        // Default: no-op
        // Derived classes override to clear their rendering surface
    }

    // ==================== ABSTRACT RENDERING METHODS ====================
    // Derived classes must implement these methods

    /// <summary>
    /// Stroke (outline) a path using current stroke state.
    /// </summary>
    public abstract void StrokePath(IPathBuilder path, PdfGraphicsState state);

    /// <summary>
    /// Fill a path using current fill state.
    /// </summary>
    public abstract void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Fill and stroke a path in a single operation.
    /// </summary>
    public abstract void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Set clipping path for subsequent operations.
    /// </summary>
    public abstract void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Render text string with specified glyph widths.
    /// </summary>
    public abstract void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null);

    /// <summary>
    /// Render an image (XObject).
    /// </summary>
    public abstract void DrawImage(PdfImage image, PdfGraphicsState state);

    /// <summary>
    /// Save current graphics state to stack (PDF 'q' operator).
    /// </summary>
    public abstract void SaveState();

    /// <summary>
    /// Restore graphics state from stack (PDF 'Q' operator).
    /// </summary>
    public abstract void RestoreState();
}
