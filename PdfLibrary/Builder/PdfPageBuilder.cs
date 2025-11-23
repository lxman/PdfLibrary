namespace PdfLibrary.Builder;

/// <summary>
/// Fluent builder for creating PDF page content
/// </summary>
public class PdfPageBuilder
{
    private readonly List<PdfContentElement> _content = [];
    private readonly List<PdfFormFieldBuilder> _formFields = [];
    private PdfUnit _defaultUnit = PdfUnit.Points;
    private PdfOrigin _defaultOrigin = PdfOrigin.BottomLeft;

    /// <summary>
    /// Page size in points
    /// </summary>
    public PdfSize Size { get; }

    /// <summary>
    /// Page height in points (for coordinate conversion)
    /// </summary>
    public double PageHeight => Size.Height;

    public PdfPageBuilder(PdfSize size)
    {
        Size = size;
    }

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
    /// Add text to the page using default unit and origin (returns builder for fluent configuration)
    /// </summary>
    public PdfTextBuilder AddText(string text, double x, double y)
    {
        // Convert using default unit and origin
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
    /// Add an image using default unit and origin
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
    /// Add an image with explicit unit specification
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
    /// Add an image from a file using default unit and origin
    /// </summary>
    public PdfImageBuilder AddImageFromFile(string filePath, double left, double top, double width, double height)
    {
        byte[] imageData = File.ReadAllBytes(filePath);
        return AddImage(imageData, left, top, width, height);
    }

    /// <summary>
    /// Add an image from a file with explicit unit specification
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
    /// Add a rectangle to the page using default unit and origin
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
    /// Add a rectangle to the page with explicit unit specification
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
    /// Add a text field to the page using current default units/origin.
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
    /// Add a checkbox to the page using current default units/origin.
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
    /// Add a dropdown (combo box) to the page using current default units/origin.
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

    /// <summary>
    /// Get the content elements
    /// </summary>
    internal IReadOnlyList<PdfContentElement> Content => _content;

    /// <summary>
    /// Get the form fields
    /// </summary>
    internal IReadOnlyList<PdfFormFieldBuilder> FormFields => _formFields;
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
    /// <summary>Auto-detect best compression based on image type</summary>
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
public class PdfImageBuilder
{
    private readonly PdfImageContent _content;

    public PdfImageBuilder(PdfImageContent content)
    {
        _content = content;
    }

    /// <summary>
    /// Set image opacity (0.0 = transparent, 1.0 = opaque)
    /// </summary>
    public PdfImageBuilder Opacity(double opacity)
    {
        _content.Opacity = Math.Clamp(opacity, 0, 1);
        return this;
    }

    /// <summary>
    /// Rotate the image by the specified degrees
    /// </summary>
    public PdfImageBuilder Rotate(double degrees)
    {
        _content.Rotation = degrees;
        return this;
    }

    /// <summary>
    /// Preserve the image's aspect ratio when scaling
    /// </summary>
    public PdfImageBuilder PreserveAspectRatio(bool preserve = true)
    {
        _content.PreserveAspectRatio = preserve;
        return this;
    }

    /// <summary>
    /// Stretch image to fill the entire rect (don't preserve aspect ratio)
    /// </summary>
    public PdfImageBuilder Stretch()
    {
        _content.PreserveAspectRatio = false;
        return this;
    }

    /// <summary>
    /// Set the compression method for the image
    /// </summary>
    public PdfImageBuilder Compression(PdfImageCompression compression, int jpegQuality = 85)
    {
        _content.Compression = compression;
        _content.JpegQuality = Math.Clamp(jpegQuality, 1, 100);
        return this;
    }

    /// <summary>
    /// Enable or disable image interpolation (smoothing when scaled)
    /// </summary>
    public PdfImageBuilder Interpolate(bool interpolate = true)
    {
        _content.Interpolate = interpolate;
        return this;
    }

    /// <summary>
    /// Disable interpolation for crisp pixel-perfect rendering
    /// </summary>
    public PdfImageBuilder NearestNeighbor()
    {
        _content.Interpolate = false;
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
/// Represents a color in PDF
/// </summary>
public readonly struct PdfColor
{
    public double R { get; }
    public double G { get; }
    public double B { get; }

    public PdfColor(double r, double g, double b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>
    /// Create a color from 0-255 RGB values
    /// </summary>
    public static PdfColor FromRgb(int r, int g, int b)
        => new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>
    /// Create a grayscale color (0 = black, 1 = white)
    /// </summary>
    public static PdfColor Gray(double value) => new(value, value, value);

    // Common colors
    public static readonly PdfColor Black = new(0, 0, 0);
    public static readonly PdfColor White = new(1, 1, 1);
    public static readonly PdfColor Red = new(1, 0, 0);
    public static readonly PdfColor Green = new(0, 1, 0);
    public static readonly PdfColor Blue = new(0, 0, 1);
    public static readonly PdfColor Yellow = new(1, 1, 0);
    public static readonly PdfColor Cyan = new(0, 1, 1);
    public static readonly PdfColor Magenta = new(1, 0, 1);
    public static readonly PdfColor LightGray = new(0.75, 0.75, 0.75);
    public static readonly PdfColor DarkGray = new(0.25, 0.25, 0.25);
}
