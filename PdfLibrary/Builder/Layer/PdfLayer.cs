namespace PdfLibrary.Builder.Layer;

/// <summary>
/// Represents an Optional Content Group (OCG) layer in a PDF document.
/// Layers allow content to be selectively shown or hidden.
/// </summary>
public class PdfLayer
{
    private static int _nextId = 1;

    /// <summary>
    /// Unique identifier for this layer (used internally)
    /// </summary>
    internal int Id { get; }

    /// <summary>
    /// The display name of the layer (shown in PDF viewer's layer panel)
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Whether the layer is visible by default when the document is opened
    /// </summary>
    public bool IsVisibleByDefault { get; internal set; } = true;

    /// <summary>
    /// Whether the layer is locked (cannot be toggled by the user)
    /// </summary>
    public bool IsLocked { get; internal set; }

    /// <summary>
    /// The intent of this layer (View, Design, or All)
    /// </summary>
    public PdfLayerIntent Intent { get; internal set; } = PdfLayerIntent.View;

    /// <summary>
    /// Print state - whether the layer prints (null = same as view state)
    /// </summary>
    public bool? PrintState { get; internal set; }

    /// <summary>
    /// Export state - whether the layer exports (null = same as view state)
    /// </summary>
    public bool? ExportState { get; internal set; }

    /// <summary>
    /// Creates a new layer with the specified name
    /// </summary>
    internal PdfLayer(string name)
    {
        Id = _nextId++;
        Name = name;
    }

    /// <summary>
    /// The internal resource name used in content streams
    /// </summary>
    internal string ResourceName => $"OC{Id}";
}