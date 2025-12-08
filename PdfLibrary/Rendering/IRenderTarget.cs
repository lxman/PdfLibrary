using System.Numerics;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;

namespace PdfLibrary.Rendering;

/// <summary>
/// Platform-agnostic rendering target for PDF content.
/// Implementations provide platform-specific rendering (WPF, SkiaSharp, Avalonia).
/// Enhanced with page lifecycle management for multipage rendering support.
/// </summary>
public interface IRenderTarget
{
    // ==================== PAGE LIFECYCLE ====================

    /// <summary>
    /// Begin rendering a new page with specified dimensions.
    /// Called before any rendering operations for a page.
    /// </summary>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="width">Page width in PDF units (1/72 inch) - from CropBox</param>
    /// <param name="height">Page height in PDF units (1/72 inch) - from CropBox</param>
    /// <param name="scale">Scale factor for rendering (1.0 = 100%)</param>
    /// <param name="cropOffsetX">X offset of CropBox from MediaBox origin</param>
    /// <param name="cropOffsetY">Y offset of CropBox from MediaBox origin</param>
    void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0);

    /// <summary>
    /// Complete rendering of the current page.
    /// Called after all rendering operations for a page.
    /// Implementations may flush buffers, finalize layout, etc.
    /// </summary>
    void EndPage();

    /// <summary>
    /// Clear all rendered content and reset the state.
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
    /// Stroke (outline) a path using the current stroke state.
    /// </summary>
    void StrokePath(IPathBuilder path, PdfGraphicsState state);

    /// <summary>
    /// Fill a path using the current fill state.
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
    /// Fill a path using a tiling pattern.
    /// </summary>
    /// <param name="path">The path to fill</param>
    /// <param name="state">Current graphics state</param>
    /// <param name="evenOdd">True for even-odd fill rule, false for non-zero winding</param>
    /// <param name="pattern">The tiling pattern definition</param>
    /// <param name="renderPatternContent">Callback to render the pattern's content stream</param>
    void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent);

    /// <summary>
    /// Set the clipping path for subsequent operations.
    /// </summary>
    /// <param name="path">The clipping path</param>
    /// <param name="state">Current graphics state for coordinate transformation</param>
    /// <param name="evenOdd">True for even-odd fill rule, false for non-zero winding</param>
    void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

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
    /// Measures the width of text as it would be rendered with the system font.
    /// Used by fixups to detect width mismatches between PDF metrics and system fonts.
    /// This is particularly important for Base14 fonts where PDFs reference standard fonts
    /// by name without embedding, and system substitutes may have different metrics.
    /// </summary>
    /// <param name="text">The text to measure</param>
    /// <param name="state">Current graphics state containing font size and transform info</param>
    /// <param name="font">PDF font object</param>
    /// <returns>Width in user space units</returns>
    float MeasureTextWidth(string text, PdfGraphicsState state, PdfFont font);

    /// <summary>
    /// Render an image (XObject).
    /// Image should be drawn in a 1x1 unit square at origin, transformed by state.Ctm.
    /// </summary>
    void DrawImage(PdfImage image, PdfGraphicsState state);

    // ==================== STATE MANAGEMENT ====================

    /// <summary>
    /// Save the current graphics state to stack (PDF 'q' operator).
    /// </summary>
    void SaveState();

    /// <summary>
    /// Restore the graphics state from the stack (PDF 'Q' operator).
    /// </summary>
    void RestoreState();

    /// <summary>
    /// Apply Current Transformation Matrix (CTM) to the canvas.
    /// Called when PDF 'cm' operator modifies the CTM.
    /// Following Melville.Pdf architecture: CTM is applied to canvas,
    /// glyph transformations are applied separately.
    /// </summary>
    void ApplyCtm(Matrix3x2 ctm);

    /// <summary>
    /// Called when the graphics state is changed via the gs operator (ExtGState).
    /// Implementations should update rendering parameters like alpha, blend mode, etc.
    /// </summary>
    /// <param name="state">The updated graphics state</param>
    void OnGraphicsStateChanged(PdfGraphicsState state);

    /// <summary>
    /// Renders a soft mask using the provided content renderer callback.
    /// The implementation creates an appropriate offscreen surface, calls the render callback,
    /// and applies the resulting mask for subsequent drawing operations.
    /// </summary>
    /// <param name="maskSubtype">The mask subtype: "Alpha" or "Luminosity"</param>
    /// <param name="renderMaskContent">Callback that renders the mask content to the provided render target</param>
    void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent);

    /// <summary>
    /// Clears the current soft mask, reverting to normal rendering.
    /// </summary>
    void ClearSoftMask();

    /// <summary>
    /// Gets the page dimensions for soft mask rendering.
    /// Returns the width, height (in pixels), and scale factor.
    /// </summary>
    (int width, int height, double scale) GetPageDimensions();
}
