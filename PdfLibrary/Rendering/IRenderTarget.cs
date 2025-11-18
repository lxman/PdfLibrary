using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;

namespace PdfLibrary.Rendering;

/// <summary>
/// Platform-agnostic rendering target for PDF content.
/// Implementations provide platform-specific rendering (WPF, SkiaSharp, Avalonia).
/// Enhanced with page lifecycle management for multi-page rendering support.
/// </summary>
public interface IRenderTarget
{
    // ==================== PAGE LIFECYCLE ====================

    /// <summary>
    /// Begin rendering a new page with specified dimensions.
    /// Called before any rendering operations for a page.
    /// </summary>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="width">Page width in PDF units (1/72 inch)</param>
    /// <param name="height">Page height in PDF units (1/72 inch)</param>
    void BeginPage(int pageNumber, double width, double height);

    /// <summary>
    /// Complete rendering of current page.
    /// Called after all rendering operations for a page.
    /// Implementations may flush buffers, finalize layout, etc.
    /// </summary>
    void EndPage();

    /// <summary>
    /// Clear all rendered content and reset state.
    /// Used when switching documents or resetting renderer.
    /// </summary>
    void Clear();

    /// <summary>
    /// Current page number being rendered (1-based).
    /// Updated by BeginPage().
    /// </summary>
    int CurrentPageNumber { get; }

    // ==================== PATH OPERATIONS ====================

    /// <summary>
    /// Stroke (outline) a path using current stroke state.
    /// </summary>
    void StrokePath(IPathBuilder path, PdfGraphicsState state);

    /// <summary>
    /// Fill a path using current fill state.
    /// </summary>
    /// <param name="path">The path to fill</param>
    /// <param name="state">Current graphics state</param>
    /// <param name="evenOdd">True for even-odd fill rule, false for non-zero winding</param>
    void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Fill and stroke a path in a single operation.
    /// </summary>
    void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Set clipping path for subsequent operations.
    /// </summary>
    void SetClippingPath(IPathBuilder path, bool evenOdd);

    // ==================== CONTENT OPERATIONS ====================

    /// <summary>
    /// Render text string with specified glyph widths.
    /// </summary>
    /// <param name="text">Decoded text string</param>
    /// <param name="glyphWidths">Width of each glyph in text space</param>
    /// <param name="state">Current graphics state (font, transform, colors)</param>
    /// <param name="font">PDF font object for rendering glyphs</param>
    /// <param name="charCodes">Original character codes from PDF (for glyph lookup in embedded fonts)</param>
    void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null);

    /// <summary>
    /// Render an image (XObject).
    /// Image should be drawn in a 1x1 unit square at origin, transformed by state.Ctm.
    /// </summary>
    void DrawImage(PdfImage image, PdfGraphicsState state);

    // ==================== STATE MANAGEMENT ====================

    /// <summary>
    /// Save current graphics state to stack (PDF 'q' operator).
    /// </summary>
    void SaveState();

    /// <summary>
    /// Restore graphics state from stack (PDF 'Q' operator).
    /// </summary>
    void RestoreState();
}
