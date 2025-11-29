namespace PdfLibrary.Builder;

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
    public double MeasureText(string text, string fontName, double fontSize)
    {
        return PdfFontMetrics.MeasureText(text, fontName, fontSize);
    }

    /// <summary>
    /// Measure the width of text in inches
    /// </summary>
    public double MeasureTextInches(string text, string fontName, double fontSize)
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
        var addedContent = _content.Skip(startIndex + 1).ToList();
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

// ==================== CONTENT ELEMENT CLASSES ====================

/// <summary>
/// Base class for page content elements
/// </summary>
public abstract class PdfContentElement
{
}

/// <summary>
/// Text content element
/// </summary>
public class PdfTextContent : PdfContentElement
{
    public string Text { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public string FontName { get; set; } = "Helvetica";
    public double FontSize { get; set; } = 12;
    public PdfColor FillColor { get; set; } = PdfColor.Black;
    public PdfColor? StrokeColor { get; set; }
    public double Rotation { get; set; }
    public double CharacterSpacing { get; set; }
    public double WordSpacing { get; set; }
    public double HorizontalScale { get; set; } = 100;
    public double TextRise { get; set; }
    public double LineSpacing { get; set; }
    public PdfTextRenderMode RenderMode { get; set; } = PdfTextRenderMode.Fill;
    public double StrokeWidth { get; set; } = 1;
    public PdfTextAlignment Alignment { get; set; } = PdfTextAlignment.Left;
    public double? MaxWidth { get; set; }
}

/// <summary>
/// Text rendering modes
/// </summary>
public enum PdfTextRenderMode
{
    /// <summary>
    /// Fill text (default)
    /// </summary>
    Fill = 0,

    /// <summary>
    /// Stroke text (outline only)
    /// </summary>
    Stroke = 1,

    /// <summary>
    /// Fill then stroke (filled with outline)
    /// </summary>
    FillStroke = 2,

    /// <summary>
    /// Invisible text (for searchable OCR layers)
    /// </summary>
    Invisible = 3,

    /// <summary>
    /// Fill and add to clipping path
    /// </summary>
    FillClip = 4,

    /// <summary>
    /// Stroke and add to clipping path
    /// </summary>
    StrokeClip = 5,

    /// <summary>
    /// Fill, stroke, and add to clipping path
    /// </summary>
    FillStrokeClip = 6,

    /// <summary>
    /// Add to clipping path only
    /// </summary>
    Clip = 7
}

/// <summary>
/// Fluent builder for text content
/// </summary>
public class PdfTextBuilder
{
    private readonly PdfPageBuilder _pageBuilder;
    private readonly PdfTextContent _content;

    internal PdfTextBuilder(PdfPageBuilder pageBuilder, PdfTextContent content)
    {
        _pageBuilder = pageBuilder;
        _content = content;
    }

    /// <summary>
    /// Set the font
    /// </summary>
    public PdfTextBuilder Font(string fontName, double fontSize = 12)
    {
        _content.FontName = fontName;
        _content.FontSize = fontSize;
        return this;
    }

    /// <summary>
    /// Set font size only
    /// </summary>
    public PdfTextBuilder Size(double fontSize)
    {
        _content.FontSize = fontSize;
        return this;
    }

    /// <summary>
    /// Set fill color
    /// </summary>
    public PdfTextBuilder Color(PdfColor color)
    {
        _content.FillColor = color;
        return this;
    }

    /// <summary>
    /// Set stroke color (for outline text)
    /// </summary>
    public PdfTextBuilder StrokeColor(PdfColor color, double width = 1)
    {
        _content.StrokeColor = color;
        _content.StrokeWidth = width;
        return this;
    }

    /// <summary>
    /// Rotate text by specified degrees
    /// </summary>
    public PdfTextBuilder Rotate(double degrees)
    {
        _content.Rotation = degrees;
        return this;
    }

    /// <summary>
    /// Set character spacing (extra space between characters in points)
    /// </summary>
    public PdfTextBuilder CharacterSpacing(double spacing)
    {
        _content.CharacterSpacing = spacing;
        return this;
    }

    /// <summary>
    /// Set word spacing (extra space between words in points)
    /// </summary>
    public PdfTextBuilder WordSpacing(double spacing)
    {
        _content.WordSpacing = spacing;
        return this;
    }

    /// <summary>
    /// Set horizontal scaling (percentage, 100 = normal)
    /// </summary>
    public PdfTextBuilder HorizontalScale(double percentage)
    {
        _content.HorizontalScale = percentage;
        return this;
    }

    /// <summary>
    /// Condense text horizontally
    /// </summary>
    public PdfTextBuilder Condensed(double percentage = 80)
    {
        _content.HorizontalScale = percentage;
        return this;
    }

    /// <summary>
    /// Expand text horizontally
    /// </summary>
    public PdfTextBuilder Expanded(double percentage = 120)
    {
        _content.HorizontalScale = percentage;
        return this;
    }

    /// <summary>
    /// Set text rise (positive = superscript, negative = subscript)
    /// </summary>
    public PdfTextBuilder Rise(double points)
    {
        _content.TextRise = points;
        return this;
    }

    /// <summary>
    /// Format as superscript
    /// </summary>
    public PdfTextBuilder Superscript()
    {
        _content.TextRise = _content.FontSize * 0.4;
        _content.FontSize *= 0.7;
        return this;
    }

    /// <summary>
    /// Format as subscript
    /// </summary>
    public PdfTextBuilder Subscript()
    {
        _content.TextRise = -_content.FontSize * 0.2;
        _content.FontSize *= 0.7;
        return this;
    }

    /// <summary>
    /// Set line spacing for multiline text
    /// </summary>
    public PdfTextBuilder LineSpacing(double points)
    {
        _content.LineSpacing = points;
        return this;
    }

    /// <summary>
    /// Set text rendering mode
    /// </summary>
    public PdfTextBuilder RenderMode(PdfTextRenderMode mode)
    {
        _content.RenderMode = mode;
        return this;
    }

    /// <summary>
    /// Render as outline only (no fill)
    /// </summary>
    public PdfTextBuilder Outline(double strokeWidth = 1)
    {
        _content.RenderMode = PdfTextRenderMode.Stroke;
        _content.StrokeWidth = strokeWidth;
        _content.StrokeColor ??= _content.FillColor;
        return this;
    }

    /// <summary>
    /// Render as filled with outline
    /// </summary>
    public PdfTextBuilder FillAndOutline(PdfColor strokeColor, double strokeWidth = 1)
    {
        _content.RenderMode = PdfTextRenderMode.FillStroke;
        _content.StrokeColor = strokeColor;
        _content.StrokeWidth = strokeWidth;
        return this;
    }

    /// <summary>
    /// Make text invisible (useful for searchable OCR layers)
    /// </summary>
    public PdfTextBuilder Invisible()
    {
        _content.RenderMode = PdfTextRenderMode.Invisible;
        return this;
    }

    /// <summary>
    /// Set text alignment (for use with MaxWidth)
    /// </summary>
    public PdfTextBuilder Align(PdfTextAlignment alignment)
    {
        _content.Alignment = alignment;
        return this;
    }

    /// <summary>
    /// Set maximum width (text will wrap or be truncated)
    /// </summary>
    public PdfTextBuilder MaxWidth(double points)
    {
        _content.MaxWidth = points;
        return this;
    }

    /// <summary>
    /// Bold simulation using stroke
    /// </summary>
    public PdfTextBuilder Bold()
    {
        _content.RenderMode = PdfTextRenderMode.FillStroke;
        _content.StrokeColor = _content.FillColor;
        _content.StrokeWidth = _content.FontSize * 0.03;
        return this;
    }

    /// <summary>
    /// Return to page builder for adding more content
    /// </summary>
    public PdfPageBuilder Done()
    {
        return _pageBuilder;
    }

    /// <summary>
    /// Implicit conversion back to page builder
    /// </summary>
    public static implicit operator PdfPageBuilder(PdfTextBuilder builder)
    {
        return builder._pageBuilder;
    }
}

/// <summary>
/// Image compression options
/// </summary>
public enum PdfImageCompression
{
    /// <summary>Auto-detect the best compression based on the image type</summary>
    Auto,
    /// <summary>JPEG/DCT compression (lossy, good for photos)</summary>
    Jpeg,
    /// <summary>Flate/ZIP compression (lossless)</summary>
    Flate,
    /// <summary>No compression</summary>
    None
}

/// <summary>
/// Image content element
/// </summary>
public class PdfImageContent : PdfContentElement
{
    public byte[] ImageData { get; set; } = [];
    public PdfRect Rect { get; set; }
    public double Opacity { get; set; } = 1.0;
    public double Rotation { get; set; }
    public bool PreserveAspectRatio { get; set; } = true;
    public PdfImageCompression Compression { get; set; } = PdfImageCompression.Auto;
    public int JpegQuality { get; set; } = 85;
    public bool Interpolate { get; set; } = true;
}

/// <summary>
/// Fluent builder for image configuration
/// </summary>
public class PdfImageBuilder(PdfImageContent content)
{
    /// <summary>
    /// Set image opacity (0.0 = transparent, 1.0 = opaque)
    /// </summary>
    public PdfImageBuilder Opacity(double opacity)
    {
        content.Opacity = Math.Clamp(opacity, 0, 1);
        return this;
    }

    /// <summary>
    /// Rotate the image by the specified degrees
    /// </summary>
    public PdfImageBuilder Rotate(double degrees)
    {
        content.Rotation = degrees;
        return this;
    }

    /// <summary>
    /// Preserve the image's aspect ratio when scaling
    /// </summary>
    public PdfImageBuilder PreserveAspectRatio(bool preserve = true)
    {
        content.PreserveAspectRatio = preserve;
        return this;
    }

    /// <summary>
    /// Stretch image to fill the entire rect (don't preserve aspect ratio)
    /// </summary>
    public PdfImageBuilder Stretch()
    {
        content.PreserveAspectRatio = false;
        return this;
    }

    /// <summary>
    /// Set the compression method for the image
    /// </summary>
    public PdfImageBuilder Compression(PdfImageCompression compression, int jpegQuality = 85)
    {
        content.Compression = compression;
        content.JpegQuality = Math.Clamp(jpegQuality, 1, 100);
        return this;
    }

    /// <summary>
    /// Enable or disable image interpolation (smoothing when scaled)
    /// </summary>
    public PdfImageBuilder Interpolate(bool interpolate = true)
    {
        content.Interpolate = interpolate;
        return this;
    }

    /// <summary>
    /// Disable interpolation for crisp pixel-perfect rendering
    /// </summary>
    public PdfImageBuilder NearestNeighbor()
    {
        content.Interpolate = false;
        return this;
    }
}

/// <summary>
/// Rectangle content element
/// </summary>
public class PdfRectangleContent : PdfContentElement
{
    public PdfRect Rect { get; set; }
    public PdfColor? FillColor { get; set; }
    public PdfColor? StrokeColor { get; set; }
    public double LineWidth { get; set; } = 1;
}

/// <summary>
/// Line content element
/// </summary>
public class PdfLineContent : PdfContentElement
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public PdfColor StrokeColor { get; set; } = PdfColor.Black;
    public double LineWidth { get; set; } = 1;
}

// ==================== COLOR ====================

/// <summary>
/// Color space types supported by the PDF builder
/// </summary>
public enum PdfColorSpace
{
    /// <summary>RGB color space (DeviceRGB)</summary>
    DeviceRGB,
    /// <summary>Grayscale color space (DeviceGray)</summary>
    DeviceGray,
    /// <summary>CMYK color space (DeviceCMYK)</summary>
    DeviceCMYK
}

/// <summary>
/// Represents a color in PDF with support for multiple color spaces
/// </summary>
public readonly struct PdfColor
{
    /// <summary>The color space this color is defined in</summary>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>Color components (interpretation depends on ColorSpace)</summary>
    public double[] Components { get; }

    // Convenience accessors for RGB
    public double R => ColorSpace == PdfColorSpace.DeviceRGB ? Components[0] : 0;
    public double G => ColorSpace == PdfColorSpace.DeviceRGB ? Components[1] : 0;
    public double B => ColorSpace == PdfColorSpace.DeviceRGB ? Components[2] : 0;

    // Convenience accessors for Gray
    public double GrayValue => ColorSpace == PdfColorSpace.DeviceGray ? Components[0] : 0;

    // Convenience accessors for CMYK
    public double C => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[0] : 0;
    public double M => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[1] : 0;
    public double Y => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[2] : 0;
    public double K => ColorSpace == PdfColorSpace.DeviceCMYK ? Components[3] : 0;

    /// <summary>
    /// Create an RGB color (values 0-1)
    /// </summary>
    public PdfColor(double r, double g, double b)
    {
        ColorSpace = PdfColorSpace.DeviceRGB;
        Components = [r, g, b];
    }

    /// <summary>
    /// Create a color with explicit color space and components (internal constructor)
    /// </summary>
    private PdfColor(PdfColorSpace colorSpace, double[] components)
    {
        ColorSpace = colorSpace;
        Components = components;
    }

    /// <summary>
    /// Create a color with explicit color space and components
    /// </summary>
    public static PdfColor FromComponents(PdfColorSpace colorSpace, params double[] components)
        => new(colorSpace, components);

    /// <summary>
    /// Create a color from 0-255 RGB values
    /// </summary>
    public static PdfColor FromRgb(int r, int g, int b)
        => new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>
    /// Create a grayscale color using DeviceGray color space (0 = black, 1 = white)
    /// </summary>
    public static PdfColor FromGray(double value)
        => FromComponents(PdfColorSpace.DeviceGray, value);

    /// <summary>
    /// Create a grayscale color as RGB (0 = black, 1 = white) - legacy method
    /// </summary>
    public static PdfColor Gray(double value) => new(value, value, value);

    /// <summary>
    /// Create a CMYK color (values 0-1)
    /// </summary>
    public static PdfColor FromCmyk(double c, double m, double y, double k)
        => FromComponents(PdfColorSpace.DeviceCMYK, c, m, y, k);

    /// <summary>
    /// Create a CMYK color from 0-100 percentage values
    /// </summary>
    public static PdfColor FromCmykPercent(double c, double m, double y, double k)
        => FromComponents(PdfColorSpace.DeviceCMYK, c / 100.0, m / 100.0, y / 100.0, k / 100.0);

    // Common colors (DeviceRGB) - use explicit constructor to avoid ambiguity with params overload
    public static readonly PdfColor Black = new PdfColor(0, 0, 0);
    public static readonly PdfColor White = new PdfColor(1, 1, 1);
    public static readonly PdfColor Red = new PdfColor(1, 0, 0);
    public static readonly PdfColor Green = new PdfColor(0, 1, 0);
    public static readonly PdfColor Blue = new PdfColor(0, 0, 1);
    public static readonly PdfColor Yellow = new PdfColor(1, 1, 0);
    public static readonly PdfColor Cyan = new PdfColor(0, 1, 1);
    public static readonly PdfColor Magenta = new PdfColor(1, 0, 1);
    public static readonly PdfColor LightGray = new PdfColor(0.75, 0.75, 0.75);
    public static readonly PdfColor DarkGray = new PdfColor(0.25, 0.25, 0.25);

    // Common CMYK colors
    public static readonly PdfColor CmykBlack = FromCmyk(0, 0, 0, 1);
    public static readonly PdfColor CmykWhite = FromCmyk(0, 0, 0, 0);
    public static readonly PdfColor CmykCyan = FromCmyk(1, 0, 0, 0);
    public static readonly PdfColor CmykMagenta = FromCmyk(0, 1, 0, 0);
    public static readonly PdfColor CmykYellow = FromCmyk(0, 0, 1, 0);
    public static readonly PdfColor CmykRed = FromCmyk(0, 1, 1, 0);
    public static readonly PdfColor CmykGreen = FromCmyk(1, 0, 1, 0);
    public static readonly PdfColor CmykBlue = FromCmyk(1, 1, 0, 0);
}

// ==================== PATH DRAWING ====================

/// <summary>
/// Types of path segments
/// </summary>
public enum PdfPathSegmentType
{
    MoveTo,
    LineTo,
    CurveTo,      // Cubic Bezier curve with two control points
    CurveToV,     // Cubic Bezier with first control point = current point
    CurveToY,     // Cubic Bezier with second control point = endpoint
    ClosePath,
    Rectangle
}

/// <summary>
/// Represents a segment in a path
/// </summary>
public class PdfPathSegment
{
    public PdfPathSegmentType Type { get; init; }
    public double[] Points { get; init; } = [];
}

/// <summary>
/// Fill rules for paths
/// </summary>
public enum PdfFillRule
{
    /// <summary>Non-zero winding number rule (default)</summary>
    NonZeroWinding,
    /// <summary>Even-odd rule</summary>
    EvenOdd
}

/// <summary>
/// Line cap styles
/// </summary>
public enum PdfLineCap
{
    /// <summary>Butt cap - the stroke ends at the endpoint</summary>
    Butt = 0,
    /// <summary>Round cap - semicircular arc at the endpoint</summary>
    Round = 1,
    /// <summary>Projecting square cap - extends beyond the endpoint</summary>
    Square = 2
}

/// <summary>
/// Line join styles
/// </summary>
public enum PdfLineJoin
{
    /// <summary>Miter join - outer edges extended to meet</summary>
    Miter = 0,
    /// <summary>Round join - circular arc at the corner</summary>
    Round = 1,
    /// <summary>Bevel join - straight line across the corner</summary>
    Bevel = 2
}

/// <summary>
/// Path content element supporting complex shapes
/// </summary>
public class PdfPathContent : PdfContentElement
{
    public List<PdfPathSegment> Segments { get; } = [];
    public PdfColor? FillColor { get; set; }
    public PdfColor? StrokeColor { get; set; }
    public double LineWidth { get; set; } = 1;
    public PdfFillRule FillRule { get; set; } = PdfFillRule.NonZeroWinding;
    public PdfLineCap LineCap { get; set; } = PdfLineCap.Butt;
    public PdfLineJoin LineJoin { get; set; } = PdfLineJoin.Miter;
    public double MiterLimit { get; set; } = 10;
    public double[]? DashPattern { get; set; }
    public double DashPhase { get; set; }
    public bool IsClippingPath { get; set; }
    public double FillOpacity { get; set; } = 1.0;
    public double StrokeOpacity { get; set; } = 1.0;
}

/// <summary>
/// Fluent builder for creating paths with lines, curves, and shapes
/// </summary>
public class PdfPathBuilder
{
    private readonly PdfPageBuilder _pageBuilder;
    private readonly PdfPathContent _content;
    private double _currentX;
    private double _currentY;
    private double _startX;
    private double _startY;

    internal PdfPathBuilder(PdfPageBuilder pageBuilder, PdfPathContent content)
    {
        _pageBuilder = pageBuilder;
        _content = content;
    }

    /// <summary>
    /// Move to a new position without drawing
    /// </summary>
    public PdfPathBuilder MoveTo(double x, double y)
    {
        _content.Segments.Add(new PdfPathSegment { Type = PdfPathSegmentType.MoveTo, Points = [x, y] });
        _currentX = _startX = x;
        _currentY = _startY = y;
        return this;
    }

    /// <summary>
    /// Draw a line to the specified position
    /// </summary>
    public PdfPathBuilder LineTo(double x, double y)
    {
        _content.Segments.Add(new PdfPathSegment { Type = PdfPathSegmentType.LineTo, Points = [x, y] });
        _currentX = x;
        _currentY = y;
        return this;
    }

    /// <summary>
    /// Draw a cubic Bezier curve with two control points
    /// </summary>
    public PdfPathBuilder CurveTo(double cp1X, double cp1Y, double cp2X, double cp2Y, double endX, double endY)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.CurveTo,
            Points = [cp1X, cp1Y, cp2X, cp2Y, endX, endY]
        });
        _currentX = endX;
        _currentY = endY;
        return this;
    }

    /// <summary>
    /// Draw a cubic Bezier curve where the first control point is the current point
    /// </summary>
    public PdfPathBuilder CurveToV(double cp2X, double cp2Y, double endX, double endY)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.CurveToV,
            Points = [cp2X, cp2Y, endX, endY]
        });
        _currentX = endX;
        _currentY = endY;
        return this;
    }

    /// <summary>
    /// Draw a cubic Bezier curve where the second control point equals the endpoint
    /// </summary>
    public PdfPathBuilder CurveToY(double cp1X, double cp1Y, double endX, double endY)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.CurveToY,
            Points = [cp1X, cp1Y, endX, endY]
        });
        _currentX = endX;
        _currentY = endY;
        return this;
    }

    /// <summary>
    /// Draw a quadratic Bezier curve (converted to cubic internally)
    /// </summary>
    public PdfPathBuilder QuadraticCurveTo(double cpX, double cpY, double endX, double endY)
    {
        // Convert quadratic to cubic Bezier
        // CP1 = P0 + 2/3 * (CP - P0) = P0 + 2/3*CP - 2/3*P0 = 1/3*P0 + 2/3*CP
        // CP2 = P2 + 2/3 * (CP - P2) = P2 + 2/3*CP - 2/3*P2 = 1/3*P2 + 2/3*CP
        double cp1X = _currentX + 2.0 / 3.0 * (cpX - _currentX);
        double cp1Y = _currentY + 2.0 / 3.0 * (cpY - _currentY);
        double cp2X = endX + 2.0 / 3.0 * (cpX - endX);
        double cp2Y = endY + 2.0 / 3.0 * (cpY - endY);

        return CurveTo(cp1X, cp1Y, cp2X, cp2Y, endX, endY);
    }

    /// <summary>
    /// Draw a circular arc (approximated with Bezier curves)
    /// </summary>
    public PdfPathBuilder Arc(double centerX, double centerY, double radius, double startAngle, double endAngle)
    {
        return EllipticalArc(centerX, centerY, radius, radius, startAngle, endAngle);
    }

    /// <summary>
    /// Draw an elliptical arc (approximated with Bezier curves)
    /// </summary>
    public PdfPathBuilder EllipticalArc(double centerX, double centerY, double radiusX, double radiusY,
        double startAngleDegrees, double endAngleDegrees)
    {
        double startRad = startAngleDegrees * Math.PI / 180.0;
        double endRad = endAngleDegrees * Math.PI / 180.0;

        // Normalize angles
        while (endRad < startRad)
            endRad += 2 * Math.PI;

        // Break arc into segments of at most 90 degrees for better approximation
        double totalAngle = endRad - startRad;
        var segments = (int)Math.Ceiling(Math.Abs(totalAngle) / (Math.PI / 2));
        double angleStep = totalAngle / segments;

        double currentAngle = startRad;

        // Move to start point
        double startX = centerX + radiusX * Math.Cos(currentAngle);
        double startY = centerY + radiusY * Math.Sin(currentAngle);
        MoveTo(startX, startY);

        for (var i = 0; i < segments; i++)
        {
            double nextAngle = currentAngle + angleStep;
            AddArcSegment(centerX, centerY, radiusX, radiusY, currentAngle, nextAngle);
            currentAngle = nextAngle;
        }

        return this;
    }

    private void AddArcSegment(double cx, double cy, double rx, double ry, double startAngle, double endAngle)
    {
        // Use the standard cubic Bezier approximation for circular arcs
        double angle = endAngle - startAngle;
        double alpha = Math.Sin(angle) * (Math.Sqrt(4 + 3 * Math.Pow(Math.Tan(angle / 2), 2)) - 1) / 3;

        double x1 = Math.Cos(startAngle);
        double y1 = Math.Sin(startAngle);
        double x2 = Math.Cos(endAngle);
        double y2 = Math.Sin(endAngle);

        double cp1X = cx + rx * (x1 - alpha * y1);
        double cp1Y = cy + ry * (y1 + alpha * x1);
        double cp2X = cx + rx * (x2 + alpha * y2);
        double cp2Y = cy + ry * (y2 - alpha * x2);
        double endX = cx + rx * x2;
        double endY = cy + ry * y2;

        CurveTo(cp1X, cp1Y, cp2X, cp2Y, endX, endY);
    }

    /// <summary>
    /// Add a rectangle to the path
    /// </summary>
    public PdfPathBuilder Rectangle(double x, double y, double width, double height)
    {
        _content.Segments.Add(new PdfPathSegment
        {
            Type = PdfPathSegmentType.Rectangle,
            Points = [x, y, width, height]
        });
        _currentX = x;
        _currentY = y;
        return this;
    }

    /// <summary>
    /// Add a rounded rectangle to the path
    /// </summary>
    public PdfPathBuilder RoundedRectangle(double x, double y, double width, double height, double cornerRadius)
    {
        double r = Math.Min(cornerRadius, Math.Min(width / 2, height / 2));

        // Start at top-left corner after the curve
        MoveTo(x + r, y);

        // Top edge and top-right corner
        LineTo(x + width - r, y);
        CurveTo(x + width - r * 0.45, y, x + width, y + r * 0.45, x + width, y + r);

        // Right edge and bottom-right corner
        LineTo(x + width, y + height - r);
        CurveTo(x + width, y + height - r * 0.45, x + width - r * 0.45, y + height, x + width - r, y + height);

        // Bottom edge and bottom-left corner
        LineTo(x + r, y + height);
        CurveTo(x + r * 0.45, y + height, x, y + height - r * 0.45, x, y + height - r);

        // Left edge and top-left corner
        LineTo(x, y + r);
        CurveTo(x, y + r * 0.45, x + r * 0.45, y, x + r, y);

        return ClosePath();
    }

    /// <summary>
    /// Add a circle to the path
    /// </summary>
    public PdfPathBuilder Circle(double centerX, double centerY, double radius)
    {
        return Ellipse(centerX, centerY, radius, radius);
    }

    /// <summary>
    /// Add an ellipse to the path
    /// </summary>
    public PdfPathBuilder Ellipse(double centerX, double centerY, double radiusX, double radiusY)
    {
        // Use Bezier approximation for ellipse (4 curves)
        const double k = 0.5522847498; // 4/3 * (sqrt(2) - 1)
        double kx = radiusX * k;
        double ky = radiusY * k;

        MoveTo(centerX + radiusX, centerY);
        CurveTo(centerX + radiusX, centerY + ky, centerX + kx, centerY + radiusY, centerX, centerY + radiusY);
        CurveTo(centerX - kx, centerY + radiusY, centerX - radiusX, centerY + ky, centerX - radiusX, centerY);
        CurveTo(centerX - radiusX, centerY - ky, centerX - kx, centerY - radiusY, centerX, centerY - radiusY);
        CurveTo(centerX + kx, centerY - radiusY, centerX + radiusX, centerY - ky, centerX + radiusX, centerY);

        return ClosePath();
    }

    /// <summary>
    /// Close the current subpath by drawing a line to the start point
    /// </summary>
    public PdfPathBuilder ClosePath()
    {
        _content.Segments.Add(new PdfPathSegment { Type = PdfPathSegmentType.ClosePath, Points = [] });
        _currentX = _startX;
        _currentY = _startY;
        return this;
    }

    // ==================== STYLING ====================

    /// <summary>
    /// Set fill color
    /// </summary>
    public PdfPathBuilder Fill(PdfColor color)
    {
        _content.FillColor = color;
        return this;
    }

    /// <summary>
    /// Set stroke color
    /// </summary>
    public PdfPathBuilder Stroke(PdfColor color, double lineWidth = 1)
    {
        _content.StrokeColor = color;
        _content.LineWidth = lineWidth;
        return this;
    }

    /// <summary>
    /// Set line width for stroke
    /// </summary>
    public PdfPathBuilder LineWidth(double width)
    {
        _content.LineWidth = width;
        return this;
    }

    /// <summary>
    /// Set line cap style
    /// </summary>
    public PdfPathBuilder LineCap(PdfLineCap cap)
    {
        _content.LineCap = cap;
        return this;
    }

    /// <summary>
    /// Set line join style
    /// </summary>
    public PdfPathBuilder LineJoin(PdfLineJoin join)
    {
        _content.LineJoin = join;
        return this;
    }

    /// <summary>
    /// Set miter limit for miter joins
    /// </summary>
    public PdfPathBuilder MiterLimit(double limit)
    {
        _content.MiterLimit = limit;
        return this;
    }

    /// <summary>
    /// Set dash pattern for strokes
    /// </summary>
    public PdfPathBuilder DashPattern(double[] pattern, double phase = 0)
    {
        _content.DashPattern = pattern;
        _content.DashPhase = phase;
        return this;
    }

    /// <summary>
    /// Set a simple dash pattern
    /// </summary>
    public PdfPathBuilder Dashed(double dashLength = 3, double gapLength = 3)
    {
        return DashPattern([dashLength, gapLength]);
    }

    /// <summary>
    /// Set a dotted pattern
    /// </summary>
    public PdfPathBuilder Dotted(double dotSize = 1, double gapSize = 2)
    {
        _content.LineCap = PdfLineCap.Round;
        return DashPattern([0, dotSize + gapSize]);
    }

    /// <summary>
    /// Set fill rule
    /// </summary>
    public PdfPathBuilder FillRule(PdfFillRule rule)
    {
        _content.FillRule = rule;
        return this;
    }

    /// <summary>
    /// Set fill opacity (0-1)
    /// </summary>
    public PdfPathBuilder FillOpacity(double opacity)
    {
        _content.FillOpacity = Math.Clamp(opacity, 0, 1);
        return this;
    }

    /// <summary>
    /// Set stroke opacity (0-1)
    /// </summary>
    public PdfPathBuilder StrokeOpacity(double opacity)
    {
        _content.StrokeOpacity = Math.Clamp(opacity, 0, 1);
        return this;
    }

    /// <summary>
    /// Use this path as a clipping path
    /// </summary>
    public PdfPathBuilder AsClippingPath()
    {
        _content.IsClippingPath = true;
        return this;
    }

    /// <summary>
    /// Return to page builder
    /// </summary>
    public PdfPageBuilder Done()
    {
        return _pageBuilder;
    }

    /// <summary>
    /// Implicit conversion back to page builder
    /// </summary>
    public static implicit operator PdfPageBuilder(PdfPathBuilder builder)
    {
        return builder._pageBuilder;
    }
}
