namespace PdfLibrary.Builder;

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

/// <summary>
/// Layer intent specifies the intended use of the layer
/// </summary>
public enum PdfLayerIntent
{
    /// <summary>
    /// Layer is intended for viewing on screen
    /// </summary>
    View,

    /// <summary>
    /// Layer is intended for design purposes (may contain auxiliary content)
    /// </summary>
    Design,

    /// <summary>
    /// Layer is used for all purposes
    /// </summary>
    All
}

/// <summary>
/// Fluent builder for configuring layer properties
/// </summary>
public class PdfLayerBuilder
{
    private readonly PdfLayer _layer;

    internal PdfLayerBuilder(PdfLayer layer)
    {
        _layer = layer;
    }

    /// <summary>
    /// Gets the underlying layer
    /// </summary>
    public PdfLayer Layer => _layer;

    /// <summary>
    /// Make the layer hidden by default
    /// </summary>
    public PdfLayerBuilder Hidden()
    {
        _layer.IsVisibleByDefault = false;
        return this;
    }

    /// <summary>
    /// Make the layer visible by default (this is the default)
    /// </summary>
    public PdfLayerBuilder Visible()
    {
        _layer.IsVisibleByDefault = true;
        return this;
    }

    /// <summary>
    /// Lock the layer so users cannot toggle its visibility
    /// </summary>
    public PdfLayerBuilder Locked()
    {
        _layer.IsLocked = true;
        return this;
    }

    /// <summary>
    /// Set the layer intent
    /// </summary>
    public PdfLayerBuilder WithIntent(PdfLayerIntent intent)
    {
        _layer.Intent = intent;
        return this;
    }

    /// <summary>
    /// Configure print state (whether layer content prints)
    /// </summary>
    public PdfLayerBuilder PrintWhenVisible()
    {
        _layer.PrintState = null; // Same as view state
        return this;
    }

    /// <summary>
    /// Never print this layer's content
    /// </summary>
    public PdfLayerBuilder NeverPrint()
    {
        _layer.PrintState = false;
        return this;
    }

    /// <summary>
    /// Always print this layer's content (even if hidden on screen)
    /// </summary>
    public PdfLayerBuilder AlwaysPrint()
    {
        _layer.PrintState = true;
        return this;
    }

    /// <summary>
    /// Configure export state (whether layer content exports)
    /// </summary>
    public PdfLayerBuilder ExportWhenVisible()
    {
        _layer.ExportState = null; // Same as view state
        return this;
    }

    /// <summary>
    /// Never export this layer's content
    /// </summary>
    public PdfLayerBuilder NeverExport()
    {
        _layer.ExportState = false;
        return this;
    }

    /// <summary>
    /// Always export this layer's content
    /// </summary>
    public PdfLayerBuilder AlwaysExport()
    {
        _layer.ExportState = true;
        return this;
    }

    /// <summary>
    /// Implicit conversion to PdfLayer for convenience
    /// </summary>
    public static implicit operator PdfLayer(PdfLayerBuilder builder) => builder._layer;
}

/// <summary>
/// Content element that wraps other content in a layer
/// </summary>
public class PdfLayerContent : PdfContentElement
{
    /// <summary>
    /// The layer this content belongs to
    /// </summary>
    public PdfLayer Layer { get; }

    /// <summary>
    /// The content elements within this layer
    /// </summary>
    public List<PdfContentElement> Content { get; } = [];

    internal PdfLayerContent(PdfLayer layer)
    {
        Layer = layer;
    }
}

/// <summary>
/// Fluent builder for adding content to a layer
/// </summary>
public class PdfLayerContentBuilder
{
    private readonly PdfPageBuilder _pageBuilder;
    private readonly PdfLayerContent _layerContent;
    private readonly List<PdfContentElement> _originalContent;
    private readonly int _startIndex;

    internal PdfLayerContentBuilder(PdfPageBuilder pageBuilder, PdfLayerContent layerContent,
        List<PdfContentElement> originalContent, int startIndex)
    {
        _pageBuilder = pageBuilder;
        _layerContent = layerContent;
        _originalContent = originalContent;
        _startIndex = startIndex;
    }

    /// <summary>
    /// Add text to the layer using default units
    /// </summary>
    public PdfTextBuilder AddText(string text, double x, double y)
    {
        return _pageBuilder.AddText(text, x, y);
    }

    /// <summary>
    /// Add text to the layer with explicit units
    /// </summary>
    public PdfTextBuilder AddText(string text, PdfLength x, PdfLength y)
    {
        return _pageBuilder.AddText(text, x, y);
    }

    /// <summary>
    /// Add a rectangle to the layer
    /// </summary>
    public PdfLayerContentBuilder AddRectangle(double left, double top, double width, double height,
        PdfColor? fillColor = null, PdfColor? strokeColor = null, double lineWidth = 1)
    {
        _pageBuilder.AddRectangle(left, top, width, height, fillColor, strokeColor, lineWidth);
        return this;
    }

    /// <summary>
    /// Add a line to the layer
    /// </summary>
    public PdfLayerContentBuilder AddLine(double x1, double y1, double x2, double y2,
        PdfColor? strokeColor = null, double lineWidth = 1)
    {
        _pageBuilder.AddLine(x1, y1, x2, y2, strokeColor, lineWidth);
        return this;
    }

    /// <summary>
    /// Begin a path in the layer
    /// </summary>
    public PdfPathBuilder AddPath()
    {
        return _pageBuilder.AddPath();
    }

    /// <summary>
    /// Add a circle to the layer
    /// </summary>
    public PdfPathBuilder AddCircle(double centerX, double centerY, double radius)
    {
        return _pageBuilder.AddCircle(centerX, centerY, radius);
    }

    /// <summary>
    /// Add an ellipse to the layer
    /// </summary>
    public PdfPathBuilder AddEllipse(double centerX, double centerY, double radiusX, double radiusY)
    {
        return _pageBuilder.AddEllipse(centerX, centerY, radiusX, radiusY);
    }

    /// <summary>
    /// Add a rounded rectangle to the layer
    /// </summary>
    public PdfPathBuilder AddRoundedRectangle(double x, double y, double width, double height, double cornerRadius)
    {
        return _pageBuilder.AddRoundedRectangle(x, y, width, height, cornerRadius);
    }

    /// <summary>
    /// Add an image to the layer
    /// </summary>
    public PdfImageBuilder AddImage(byte[] imageData, double left, double top, double width, double height)
    {
        return _pageBuilder.AddImage(imageData, left, top, width, height);
    }

    /// <summary>
    /// Add an image from file to the layer
    /// </summary>
    public PdfImageBuilder AddImageFromFile(string filePath, double left, double top, double width, double height)
    {
        return _pageBuilder.AddImageFromFile(filePath, left, top, width, height);
    }

    /// <summary>
    /// Finish adding content to this layer and return to the page builder
    /// </summary>
    public PdfPageBuilder Done()
    {
        // Move all content added since starting the layer into the layer content
        List<PdfContentElement> addedContent = _originalContent.Skip(_startIndex).ToList();

        // Remove from the original list
        _originalContent.RemoveRange(_startIndex, addedContent.Count);

        // Add to layer content
        _layerContent.Content.AddRange(addedContent);

        return _pageBuilder;
    }
}
