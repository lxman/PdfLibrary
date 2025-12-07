namespace PdfLibrary.Fixups;

/// <summary>
/// Interface for PDF rendering fixups that handle edge cases and non-standard PDF behavior.
/// Fixups are independent units that can be enabled/disabled via configuration.
/// </summary>
public interface IPdfFixup
{
    /// <summary>
    /// Unique identifier for this fixup (used in configuration).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description of what this fixup does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Whether this fixup is currently enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Priority for execution order when multiple fixups apply.
    /// Lower numbers execute first. Default is 100.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Called before rendering a page begins.
    /// </summary>
    /// <param name="context">The page rendering context.</param>
    void OnBeforePageRender(PageRenderContext context) { }

    /// <summary>
    /// Called when a text run is about to be rendered.
    /// </summary>
    /// <param name="context">The text run context.</param>
    void OnTextRun(TextRunContext context) { }

    /// <summary>
    /// Called after page rendering completes.
    /// </summary>
    /// <param name="context">The page rendering context.</param>
    void OnAfterPageRender(PageRenderContext context) { }
}
