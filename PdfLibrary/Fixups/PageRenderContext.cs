using PdfLibrary.Content;

namespace PdfLibrary.Fixups;

/// <summary>
/// Context information for page rendering fixups.
/// </summary>
public class PageRenderContext
{
    /// <summary>
    /// The current graphics state.
    /// </summary>
    public PdfGraphicsState GraphicsState { get; }

    /// <summary>
    /// The page being rendered.
    /// </summary>
    public object Page { get; }

    /// <summary>
    /// Custom data that can be used by fixups to store state.
    /// </summary>
    public Dictionary<string, object> CustomData { get; } = new();

    /// <summary>
    /// Creates a new page render context.
    /// </summary>
    /// <param name="page">The page being rendered.</param>
    /// <param name="graphicsState">The current graphics state.</param>
    public PageRenderContext(object page, PdfGraphicsState graphicsState)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
        GraphicsState = graphicsState ?? throw new ArgumentNullException(nameof(graphicsState));
    }
}
