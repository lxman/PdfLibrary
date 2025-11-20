namespace PdfLibrary.Builder;

/// <summary>
/// Fluent builder for creating PDF page content
/// </summary>
public class PdfPageBuilder
{
    private readonly List<PdfContentElement> _content = new();
    private readonly List<PdfFormFieldBuilder> _formFields = new();

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

    // ==================== TEXT ====================

    /// <summary>
    /// Add text to the page
    /// </summary>
    public PdfPageBuilder AddText(string text, double x, double y, string fontName = "Helvetica", double fontSize = 12)
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
    /// Add text at a position specified in inches from top-left
    /// </summary>
    public PdfPageBuilder AddTextInches(string text, double left, double top, string fontName = "Helvetica", double fontSize = 12)
    {
        double x = left * 72;
        double y = PageHeight - (top * 72);
        return AddText(text, x, y, fontName, fontSize);
    }

    // ==================== IMAGES ====================

    /// <summary>
    /// Add an image to the page
    /// </summary>
    public PdfPageBuilder AddImage(byte[] imageData, PdfRect rect)
    {
        _content.Add(new PdfImageContent
        {
            ImageData = imageData,
            Rect = rect
        });
        return this;
    }

    /// <summary>
    /// Add an image at a position specified in inches from top-left
    /// </summary>
    public PdfPageBuilder AddImageInches(byte[] imageData, double left, double top, double width, double height)
    {
        var rect = PdfRect.FromInches(left, top, width, height, PageHeight);
        return AddImage(imageData, rect);
    }

    // ==================== SHAPES ====================

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
    /// Add a text field to the page
    /// </summary>
    public PdfTextFieldBuilder AddTextField(string name, PdfRect rect)
    {
        var field = new PdfTextFieldBuilder(name, rect);
        _formFields.Add(field);
        return field;
    }

    /// <summary>
    /// Add a checkbox to the page
    /// </summary>
    public PdfCheckboxBuilder AddCheckbox(string name, PdfRect rect)
    {
        var field = new PdfCheckboxBuilder(name, rect);
        _formFields.Add(field);
        return field;
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
    /// Add a dropdown (combo box) to the page
    /// </summary>
    public PdfDropdownBuilder AddDropdown(string name, PdfRect rect)
    {
        var field = new PdfDropdownBuilder(name, rect);
        _formFields.Add(field);
        return field;
    }

    /// <summary>
    /// Add a signature field to the page
    /// </summary>
    public PdfSignatureFieldBuilder AddSignatureField(string name, PdfRect rect)
    {
        var field = new PdfSignatureFieldBuilder(name, rect);
        _formFields.Add(field);
        return field;
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
}

/// <summary>
/// Image content element
/// </summary>
public class PdfImageContent : PdfContentElement
{
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public PdfRect Rect { get; set; }
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
