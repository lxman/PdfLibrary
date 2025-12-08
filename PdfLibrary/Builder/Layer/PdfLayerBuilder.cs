namespace PdfLibrary.Builder.Layer;

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