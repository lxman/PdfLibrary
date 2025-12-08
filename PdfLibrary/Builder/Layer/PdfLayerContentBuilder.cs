using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.Layer;

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
