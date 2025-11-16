using PdfLibrary.Content;
using PdfLibrary.Document;

namespace PdfLibrary.Rendering;

/// <summary>
/// Platform-specific rendering target interface
/// Implemented by WPF, SkiaSharp, System.Drawing, HTML Canvas, etc.
/// </summary>
public interface IRenderTarget
{
    /// <summary>
    /// Stroke the current path with the current graphics state
    /// </summary>
    void StrokePath(IPathBuilder path, PdfGraphicsState state);

    /// <summary>
    /// Fill the current path with the current graphics state
    /// </summary>
    /// <param name="path">The path to fill</param>
    /// <param name="state">Current graphics state</param>
    /// <param name="evenOdd">Use even-odd rule (true) or nonzero winding rule (false)</param>
    void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Fill and stroke the current path
    /// </summary>
    void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd);

    /// <summary>
    /// Draw text at the current position with the current graphics state
    /// </summary>
    /// <param name="text">The text to draw (decoded string)</param>
    /// <param name="glyphWidths">Width of each glyph for proper spacing</param>
    /// <param name="state">Current graphics state</param>
    void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state);

    /// <summary>
    /// Draw an image with the current transformation matrix
    /// The image should be drawn in a 1x1 unit square at origin, transformed by state.Ctm
    /// </summary>
    void DrawImage(PdfImage image, PdfGraphicsState state);

    /// <summary>
    /// Save the current graphics state to the stack
    /// </summary>
    void SaveState();

    /// <summary>
    /// Restore the graphics state from the stack
    /// </summary>
    void RestoreState();

    /// <summary>
    /// Set the clipping path (path will be intersected with current clip)
    /// </summary>
    void SetClippingPath(IPathBuilder path, bool evenOdd);
}
