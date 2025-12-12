using PdfLibrary.Builder.Annotation;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Builder.FormField;
using PdfLibrary.Builder.Layer;

namespace PdfLibrary.Builder.Page;

/// <summary>
/// Fluent builder for creating PDF page content
/// </summary>
public class PdfPageBuilder(PdfSize size)
{
    private readonly List<PdfContentElement> _content = [];
    private readonly List<PdfFormFieldBuilder> _formFields = [];
    private readonly List<PdfAnnotation> _annotations = [];
    private PdfUnit _defaultUnit = PdfUnit.Points;
    private PdfOrigin _defaultOrigin = PdfOrigin.BottomLeft;

    /// <summary>
    /// Page size in points
    /// </summary>
    public PdfSize Size { get; } = size;

    /// <summary>
    /// Page height in points (for coordinate conversion)
    /// </summary>
    public double PageHeight => Size.Height;

    // ==================== UNIT & ORIGIN CONFIGURATION ====================

    /// <summary>
    /// Set the default unit for all subsequent coordinate values (plain doubles)
    /// </summary>
    public PdfPageBuilder WithUnit(PdfUnit unit)
    {
        _defaultUnit = unit;
        return this;
    }

    /// <summary>
    /// Set the default origin for all subsequent coordinate values
    /// </summary>
    public PdfPageBuilder WithOrigin(PdfOrigin origin)
    {
        _defaultOrigin = origin;
        return this;
    }

    /// <summary>
    /// Set default unit to inches - convenience method
    /// </summary>
    public PdfPageBuilder WithInches() => WithUnit(PdfUnit.Inches);

    /// <summary>
    /// Set default unit to millimeters - convenience method
    /// </summary>
    public PdfPageBuilder WithMillimeters() => WithUnit(PdfUnit.Millimeters);

    /// <summary>
    /// Set default unit to centimeters - convenience method
    /// </summary>
    public PdfPageBuilder WithCentimeters() => WithUnit(PdfUnit.Centimeters);

    /// <summary>
    /// Set default unit to points (PDF native) - convenience method
    /// </summary>
    public PdfPageBuilder WithPoints() => WithUnit(PdfUnit.Points);

    /// <summary>
    /// Set default origin to top-left (screen-like coordinates) - convenience method
    /// </summary>
    public PdfPageBuilder FromTopLeft() => WithOrigin(PdfOrigin.TopLeft);

    /// <summary>
    /// Set default origin to bottom-left (PDF native) - convenience method
    /// </summary>
    public PdfPageBuilder FromBottomLeft() => WithOrigin(PdfOrigin.BottomLeft);

    /// <summary>
    /// Convert a plain double value to points using the current default unit and origin
    /// </summary>
    private double ConvertToPoints(double value, bool isYCoordinate = false)
    {
        // First convert from default unit to points
        double points = _defaultUnit switch
        {
            PdfUnit.Points => value,
            PdfUnit.Inches => value * 72,
            PdfUnit.Millimeters => value * 2.834645669,
            PdfUnit.Centimeters => value * 28.34645669,
            _ => value
        };

        // Then adjust for origin if this is a Y coordinate
        if (isYCoordinate && _defaultOrigin == PdfOrigin.TopLeft)
        {
            points = PageHeight - points;
        }

        return points;
    }

    /// <summary>
    /// Create a field rectangle with the vertical center at the specified y coordinate.
    /// This aligns the field's center with text baseline when using the same y value.
    /// </summary>
    private PdfRect CreateFieldRect(double x, double y, double width, double height)
    {
        // Position the field so its vertical center aligns with y (text baseline)
        double fieldTop = y - (height / 2);
        return PdfRect.Create(x, fieldTop, width, height, _defaultUnit, _defaultOrigin, PageHeight);
    }

    // ==================== MEASUREMENT ====================

    /// <summary>
    /// Measure the width of text in points
    /// </summary>
    public static double MeasureText(string text, string fontName, double fontSize)
    {
        return PdfFontMetrics.MeasureText(text, fontName, fontSize);
    }

    /// <summary>
    /// Measure the width of text in inches
    /// </summary>
    public static double MeasureTextInches(string text, string fontName, double fontSize)
    {
        return PdfFontMetrics.MeasureText(text, fontName, fontSize) / 72.0;
    }

    // ==================== TEXT ====================

    /// <summary>
    /// Add text to the page using the default unit and origin (returns builder for fluent configuration)
    /// </summary>
    public PdfTextBuilder AddText(string text, double x, double y)
    {
        // Convert using the default unit and origin
        double xPt = ConvertToPoints(x, isYCoordinate: false);
        double yPt = ConvertToPoints(y, isYCoordinate: true);

        var content = new PdfTextContent
        {
            Text = text,
            X = xPt,
            Y = yPt
        };
        _content.Add(content);
        return new PdfTextBuilder(this, content);
    }

    /// <summary>
    /// Add text to the page with explicit unit specification (returns builder for fluent configuration)
    /// </summary>
    public PdfTextBuilder AddText(string text, PdfLength x, PdfLength y)
    {
        // Convert using explicit units/origins from PdfLength
        // X coordinates never flip - always use BottomLeft origin
        double xPt = x.ToPoints(PageHeight, PdfOrigin.BottomLeft);
        double yPt = y.ToPoints(PageHeight, y.Origin ?? _defaultOrigin);

        var content = new PdfTextContent
        {
            Text = text,
            X = xPt,
            Y = yPt
        };
        _content.Add(content);
        return new PdfTextBuilder(this, content);
    }

    /// <summary>
    /// Add text to the page with basic font settings
    /// </summary>
    public PdfPageBuilder AddText(string text, double x, double y, string fontName, double fontSize)
    {
        _content.Add(new PdfTextContent
        {
            Text = text,
            X = x,
            Y = y,
            FontName = fontName,
            FontSize = fontSize
        });
        return this;
    }

    /// <summary>
    /// Add text at a position specified in inches from top-left (returns builder for fluent configuration)
    /// </summary>
    public PdfTextBuilder AddTextInches(string text, double left, double top)
    {
        double x = left * 72;
        double y = PageHeight - (top * 72);
        return AddText(text, x, y);
    }

    /// <summary>
    /// Add text at a position specified in inches from top-left with basic font settings
    /// </summary>
    public PdfPageBuilder AddTextInches(string text, double left, double top, string fontName, double fontSize)
    {
        double x = left * 72;
        double y = PageHeight - (top * 72);
        return AddText(text, x, y, fontName, fontSize);
    }

    // ==================== IMAGES ====================

    /// <summary>
    /// Add an image to the page with fluent configuration
    /// </summary>
    public PdfImageBuilder AddImage(byte[] imageData, PdfRect rect)
    {
        var content = new PdfImageContent
        {
            ImageData = imageData,
            Rect = rect
        };
        _content.Add(content);
        return new PdfImageBuilder(content);
    }

    /// <summary>
    /// Add an image using the default unit and origin
    /// </summary>
    public PdfImageBuilder AddImage(byte[] imageData, double left, double top, double width, double height)
    {
        double leftPt = ConvertToPoints(left, isYCoordinate: false);
        double topPt = ConvertToPoints(top, isYCoordinate: true);
        double widthPt = ConvertToPoints(width, isYCoordinate: false);
        double heightPt = ConvertToPoints(height, isYCoordinate: false);

        // Convert to PDF rect (bottom-left coordinates)
        PdfRect rect = _defaultOrigin == PdfOrigin.TopLeft
            ? new PdfRect(leftPt, topPt - heightPt, leftPt + widthPt, topPt)
            : new PdfRect(leftPt, topPt, leftPt + widthPt, topPt + heightPt);

        return AddImage(imageData, rect);
    }

    /// <summary>
    /// Add an image with an explicit unit specification
    /// </summary>
    public PdfImageBuilder AddImage(byte[] imageData, PdfLength left, PdfLength top, PdfLength width, PdfLength height)
    {
        PdfOrigin origin = top.Origin ?? _defaultOrigin;
        // X coordinates and dimensions never flip - always use BottomLeft origin
        double leftPt = left.ToPoints(PageHeight, PdfOrigin.BottomLeft);
        double topPt = top.ToPoints(PageHeight, origin);
        double widthPt = width.ToPoints(PageHeight, PdfOrigin.BottomLeft);
        double heightPt = height.ToPoints(PageHeight, PdfOrigin.BottomLeft);

        // Convert to PDF rect (bottom-left coordinates)
        PdfRect rect = origin == PdfOrigin.TopLeft
            ? new PdfRect(leftPt, topPt - heightPt, leftPt + widthPt, topPt)
            : new PdfRect(leftPt, topPt, leftPt + widthPt, topPt + heightPt);

        return AddImage(imageData, rect);
    }

    /// <summary>
    /// Add an image at a position specified in inches from top-left
    /// </summary>
    /// <param name="imageData">The image file data (PNG, JPEG, etc.)</param>
    /// <param name="left">Left position in inches</param>
    /// <param name="top">Top position in inches</param>
    /// <param name="width">Width in inches</param>
    /// <param name="height">Height in inches</param>
    /// <returns>The image builder for fluent configuration</returns>
    public PdfImageBuilder AddImageInches(byte[] imageData, double left, double top, double width, double height)
    {
        PdfRect rect = PdfRect.FromInches(left, top, width, height, PageHeight);
        return AddImage(imageData, rect);
    }

    /// <summary>
    /// Add an image from a file path
    /// </summary>
    public PdfImageBuilder AddImageFromFile(string filePath, PdfRect rect)
    {
        byte[] imageData = File.ReadAllBytes(filePath);
        return AddImage(imageData, rect);
    }

    /// <summary>
    /// Add an image from a file using the default unit and origin
    /// </summary>
    public PdfImageBuilder AddImageFromFile(string filePath, double left, double top, double width, double height)
    {
        byte[] imageData = File.ReadAllBytes(filePath);
        return AddImage(imageData, left, top, width, height);
    }

    /// <summary>
    /// Add an image from a file with an explicit unit specification
    /// </summary>
    public PdfImageBuilder AddImageFromFile(string filePath, PdfLength left, PdfLength top, PdfLength width, PdfLength height)
    {
        byte[] imageData = File.ReadAllBytes(filePath);
        return AddImage(imageData, left, top, width, height);
    }

    /// <summary>
    /// Add an image from a file path at a position specified in inches
    /// </summary>
    public PdfImageBuilder AddImageFromFileInches(string filePath, double left, double top, double width, double height)
    {
        PdfRect rect = PdfRect.FromInches(left, top, width, height, PageHeight);
        return AddImageFromFile(filePath, rect);
    }

    // ==================== SHAPES ====================

    /// <summary>
    /// Add a rectangle to the page using the default unit and origin
    /// </summary>
    public PdfPageBuilder AddRectangle(double left, double top, double width, double height,
        PdfColor? fillColor = null, PdfColor? strokeColor = null, double lineWidth = 1)
    {
        double leftPt = ConvertToPoints(left, isYCoordinate: false);
        double topPt = ConvertToPoints(top, isYCoordinate: true);
        double widthPt = ConvertToPoints(width, isYCoordinate: false);
        double heightPt = ConvertToPoints(height, isYCoordinate: false);

        // Convert to PDF rect (bottom-left coordinates)
        PdfRect rect = _defaultOrigin == PdfOrigin.TopLeft
            ? new PdfRect(leftPt, topPt - heightPt, leftPt + widthPt, topPt)
            : new PdfRect(leftPt, topPt, leftPt + widthPt, topPt + heightPt);

        return AddRectangle(rect, fillColor, strokeColor, lineWidth);
    }

    /// <summary>
    /// Add a rectangle to the page with an explicit unit specification
    /// </summary>
    public PdfPageBuilder AddRectangle(PdfLength left, PdfLength top, PdfLength width, PdfLength height,
        PdfColor? fillColor = null, PdfColor? strokeColor = null, double lineWidth = 1)
    {
        PdfOrigin origin = top.Origin ?? _defaultOrigin;
        // X coordinates and dimensions never flip - always use BottomLeft origin
        double leftPt = left.ToPoints(PageHeight, PdfOrigin.BottomLeft);
        double topPt = top.ToPoints(PageHeight, origin);
        double widthPt = width.ToPoints(PageHeight, PdfOrigin.BottomLeft);
        double heightPt = height.ToPoints(PageHeight, PdfOrigin.BottomLeft);

        // Convert to PDF rect (bottom-left coordinates)
        PdfRect rect = origin == PdfOrigin.TopLeft
            ? new PdfRect(leftPt, topPt - heightPt, leftPt + widthPt, topPt)
            : new PdfRect(leftPt, topPt, leftPt + widthPt, topPt + heightPt);

        return AddRectangle(rect, fillColor, strokeColor, lineWidth);
    }

    /// <summary>
    /// Add a rectangle to the page
    /// </summary>
    public PdfPageBuilder AddRectangle(PdfRect rect, PdfColor? fillColor = null, PdfColor? strokeColor = null, double lineWidth = 1)
    {
        _content.Add(new PdfRectangleContent
        {
            Rect = rect,
            FillColor = fillColor,
            StrokeColor = strokeColor,
            LineWidth = lineWidth
        });
        return this;
    }

    /// <summary>
    /// Add a line to the page
    /// </summary>
    public PdfPageBuilder AddLine(double x1, double y1, double x2, double y2, PdfColor? strokeColor = null, double lineWidth = 1)
    {
        _content.Add(new PdfLineContent
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            StrokeColor = strokeColor ?? PdfColor.Black,
            LineWidth = lineWidth
        });
        return this;
    }

    /// <summary>
    /// Begin a new path for complex shapes, curves, and clipping
    /// </summary>
    public PdfPathBuilder AddPath()
    {
        var content = new PdfPathContent();
        _content.Add(content);
        return new PdfPathBuilder(this, content);
    }

    /// <summary>
    /// Add a circle to the page
    /// </summary>
    public PdfPathBuilder AddCircle(double centerX, double centerY, double radius)
    {
        return AddPath().Circle(centerX, centerY, radius);
    }

    /// <summary>
    /// Add an ellipse to the page
    /// </summary>
    public PdfPathBuilder AddEllipse(double centerX, double centerY, double radiusX, double radiusY)
    {
        return AddPath().Ellipse(centerX, centerY, radiusX, radiusY);
    }

    /// <summary>
    /// Add a rounded rectangle to the page
    /// </summary>
    public PdfPathBuilder AddRoundedRectangle(double x, double y, double width, double height, double cornerRadius)
    {
        return AddPath().RoundedRectangle(x, y, width, height, cornerRadius);
    }

    // ==================== FORM FIELDS ====================

    /// <summary>
    /// Add a text field to the page using explicit PdfRect
    /// </summary>
    public PdfTextFieldBuilder AddTextField(string name, PdfRect rect)
    {
        var field = new PdfTextFieldBuilder(name, rect);
        _formFields.Add(field);
        return field;
    }

    /// <summary>
    /// Add a text field to the page using the current default units / origin.
    /// The y coordinate represents the vertical center of the field (aligns with text baseline).
    /// </summary>
    public PdfTextFieldBuilder AddTextField(string name, double x, double y, double width, double height)
    {
        PdfRect rect = CreateFieldRect(x, y, width, height);
        return AddTextField(name, rect);
    }

    /// <summary>
    /// Add a checkbox to the page using explicit PdfRect
    /// </summary>
    public PdfCheckboxBuilder AddCheckbox(string name, PdfRect rect)
    {
        var field = new PdfCheckboxBuilder(name, rect);
        _formFields.Add(field);
        return field;
    }

    /// <summary>
    /// Add a checkbox to the page using the current default units / origin.
    /// The y coordinate represents the vertical center of the checkbox (aligns with text baseline).
    /// </summary>
    public PdfCheckboxBuilder AddCheckbox(string name, double x, double y, double size)
    {
        PdfRect rect = CreateFieldRect(x, y, size, size);
        return AddCheckbox(name, rect);
    }

    /// <summary>
    /// Add a radio button group to the page
    /// </summary>
    public PdfRadioGroupBuilder AddRadioGroup(string name)
    {
        var field = new PdfRadioGroupBuilder(name, PageHeight);
        _formFields.Add(field);
        return field;
    }

    /// <summary>
    /// Add a dropdown (combo box) to the page using explicit PdfRect
    /// </summary>
    public PdfDropdownBuilder AddDropdown(string name, PdfRect rect)
    {
        var field = new PdfDropdownBuilder(name, rect);
        _formFields.Add(field);
        return field;
    }

    /// <summary>
    /// Add a dropdown (combo box) to the page using the current default units / origin.
    /// The y coordinate represents the vertical center of the dropdown (aligns with text baseline).
    /// </summary>
    public PdfDropdownBuilder AddDropdown(string name, double x, double y, double width, double height)
    {
        PdfRect rect = CreateFieldRect(x, y, width, height);
        return AddDropdown(name, rect);
    }

    /// <summary>
    /// Add a signature field to the page using explicit PdfRect
    /// </summary>
    public PdfSignatureFieldBuilder AddSignatureField(string name, PdfRect rect)
    {
        var field = new PdfSignatureFieldBuilder(name, rect);
        _formFields.Add(field);
        return field;
    }

    /// <summary>
    /// Add a signature field to the page using current default units/origin.
    /// The y coordinate represents the vertical center of the signature field (aligns with text baseline).
    /// </summary>
    public PdfSignatureFieldBuilder AddSignatureField(string name, double x, double y, double width, double height)
    {
        PdfRect rect = CreateFieldRect(x, y, width, height);
        return AddSignatureField(name, rect);
    }

    // ==================== LAYERS ====================

    /// <summary>
    /// Add content to a layer (Optional Content Group)
    /// </summary>
    /// <param name="layer">The layer to add content to (must be defined via DefineLayer on the document)</param>
    /// <param name="configure">Action to add content to the layer</param>
    /// <returns>The page builder for chaining</returns>
    public PdfPageBuilder Layer(PdfLayer layer, Action<PdfLayerContentBuilder> configure)
    {
        var layerContent = new PdfLayerContent(layer);
        int startIndex = _content.Count;
        _content.Add(layerContent); // Add placeholder

        var builder = new PdfLayerContentBuilder(this, layerContent, _content, startIndex + 1);
        configure(builder);

        // Move any content added after the placeholder into the layer
        List<PdfContentElement> addedContent = _content.Skip(startIndex + 1).ToList();
        _content.RemoveRange(startIndex + 1, addedContent.Count);
        layerContent.Content.AddRange(addedContent);

        return this;
    }

    // ==================== ANNOTATIONS ====================

    /// <summary>
    /// Add a link annotation that navigates to a page in this document
    /// </summary>
    /// <param name="rect">The clickable rectangle area</param>
    /// <param name="pageIndex">The 0-based page index to navigate to</param>
    /// <param name="configure">Optional action to configure the link</param>
    /// <returns>The page builder for chaining</returns>
    public PdfPageBuilder AddLink(PdfRect rect, int pageIndex, Action<PdfLinkAnnotationBuilder>? configure = null)
    {
        var dest = new PdfDestination { PageIndex = pageIndex, Type = PdfDestinationType.Fit };
        var action = new PdfGoToAction(dest);
        var annotation = new PdfLinkAnnotation(rect, action);

        if (configure != null)
        {
            var builder = new PdfLinkAnnotationBuilder(annotation);
            configure(builder);
        }

        _annotations.Add(annotation);
        return this;
    }

    /// <summary>
    /// Add a link annotation that navigates to a page with specific destination settings
    /// </summary>
    /// <param name="rect">The clickable rectangle area</param>
    /// <param name="configureDest">Action to configure the destination</param>
    /// <param name="configureLink">Optional action to configure the link appearance</param>
    /// <returns>The page builder for chaining</returns>
    public PdfPageBuilder AddLink(PdfRect rect, Action<PdfBookmarkBuilder> configureDest, Action<PdfLinkAnnotationBuilder>? configureLink = null)
    {
        var bookmark = new PdfBookmark("_internal_");
        var bookmarkBuilder = new PdfBookmarkBuilder(bookmark);
        configureDest(bookmarkBuilder);

        var action = new PdfGoToAction(bookmark.Destination);
        var annotation = new PdfLinkAnnotation(rect, action);

        if (configureLink != null)
        {
            var builder = new PdfLinkAnnotationBuilder(annotation);
            configureLink(builder);
        }

        _annotations.Add(annotation);
        return this;
    }

    /// <summary>
    /// Add a link annotation that opens an external URL
    /// </summary>
    /// <param name="rect">The clickable rectangle area</param>
    /// <param name="url">The URL to open</param>
    /// <param name="configure">Optional action to configure the link</param>
    /// <returns>The page builder for chaining</returns>
    public PdfPageBuilder AddExternalLink(PdfRect rect, string url, Action<PdfLinkAnnotationBuilder>? configure = null)
    {
        var action = new PdfUriAction(url);
        var annotation = new PdfLinkAnnotation(rect, action);

        if (configure != null)
        {
            var builder = new PdfLinkAnnotationBuilder(annotation);
            configure(builder);
        }

        _annotations.Add(annotation);
        return this;
    }

    /// <summary>
    /// Add a link annotation using the default unit and origin
    /// </summary>
    public PdfPageBuilder AddLink(double x, double y, double width, double height, int pageIndex, Action<PdfLinkAnnotationBuilder>? configure = null)
    {
        double xPt = ConvertToPoints(x, isYCoordinate: false);
        double yPt = ConvertToPoints(y, isYCoordinate: true);
        double widthPt = ConvertToPoints(width, isYCoordinate: false);
        double heightPt = ConvertToPoints(height, isYCoordinate: false);

        PdfRect rect = _defaultOrigin == PdfOrigin.TopLeft
            ? new PdfRect(xPt, yPt - heightPt, xPt + widthPt, yPt)
            : new PdfRect(xPt, yPt, xPt + widthPt, yPt + heightPt);

        return AddLink(rect, pageIndex, configure);
    }

    /// <summary>
    /// Add an external link annotation using the default unit and origin
    /// </summary>
    public PdfPageBuilder AddExternalLink(double x, double y, double width, double height, string url, Action<PdfLinkAnnotationBuilder>? configure = null)
    {
        double xPt = ConvertToPoints(x, isYCoordinate: false);
        double yPt = ConvertToPoints(y, isYCoordinate: true);
        double widthPt = ConvertToPoints(width, isYCoordinate: false);
        double heightPt = ConvertToPoints(height, isYCoordinate: false);

        PdfRect rect = _defaultOrigin == PdfOrigin.TopLeft
            ? new PdfRect(xPt, yPt - heightPt, xPt + widthPt, yPt)
            : new PdfRect(xPt, yPt, xPt + widthPt, yPt + heightPt);

        return AddExternalLink(rect, url, configure);
    }

    /// <summary>
    /// Add a text annotation (sticky note / comment)
    /// </summary>
    /// <param name="x">X position (in current units)</param>
    /// <param name="y">Y position (in current units)</param>
    /// <param name="contents">The text content of the note</param>
    /// <param name="configure">Optional action to configure the annotation</param>
    /// <returns>The page builder for chaining</returns>
    public PdfPageBuilder AddNote(double x, double y, string contents, Action<PdfTextAnnotationBuilder>? configure = null)
    {
        double xPt = ConvertToPoints(x, isYCoordinate: false);
        double yPt = ConvertToPoints(y, isYCoordinate: true);

        // Text annotations typically use a small icon size
        var rect = new PdfRect(xPt, yPt - 24, xPt + 24, yPt);
        var annotation = new PdfTextAnnotation(rect, contents);

        if (configure != null)
        {
            var builder = new PdfTextAnnotationBuilder(annotation);
            configure(builder);
        }

        _annotations.Add(annotation);
        return this;
    }

    /// <summary>
    /// Add a highlight annotation
    /// </summary>
    /// <param name="rect">The rectangle to highlight</param>
    /// <param name="configure">Optional action to configure the highlight</param>
    /// <returns>The page builder for chaining</returns>
    public PdfPageBuilder AddHighlight(PdfRect rect, Action<PdfHighlightAnnotationBuilder>? configure = null)
    {
        var annotation = new PdfHighlightAnnotation(rect);
        annotation.QuadPoints.Add(PdfQuadPoints.FromRect(rect));

        if (configure != null)
        {
            var builder = new PdfHighlightAnnotationBuilder(annotation);
            configure(builder);
        }

        _annotations.Add(annotation);
        return this;
    }

    /// <summary>
    /// Add a highlight annotation using the default unit and origin
    /// </summary>
    public PdfPageBuilder AddHighlight(double x, double y, double width, double height, Action<PdfHighlightAnnotationBuilder>? configure = null)
    {
        double xPt = ConvertToPoints(x, isYCoordinate: false);
        double yPt = ConvertToPoints(y, isYCoordinate: true);
        double widthPt = ConvertToPoints(width, isYCoordinate: false);
        double heightPt = ConvertToPoints(height, isYCoordinate: false);

        PdfRect rect = _defaultOrigin == PdfOrigin.TopLeft
            ? new PdfRect(xPt, yPt - heightPt, xPt + widthPt, yPt)
            : new PdfRect(xPt, yPt, xPt + widthPt, yPt + heightPt);

        return AddHighlight(rect, configure);
    }

    /// <summary>
    /// Get the content elements
    /// </summary>
    internal IReadOnlyList<PdfContentElement> Content => _content;

    /// <summary>
    /// Get the form fields
    /// </summary>
    internal IReadOnlyList<PdfFormFieldBuilder> FormFields => _formFields;

    /// <summary>
    /// Get the annotations
    /// </summary>
    internal IReadOnlyList<PdfAnnotation> Annotations => _annotations;
}