using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Layer;

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