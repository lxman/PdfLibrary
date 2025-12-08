using PdfLibrary.Builder.Page;

namespace PdfLibrary.Builder.FormField;

/// <summary>
/// Base class for form field builders
/// </summary>
public abstract class PdfFormFieldBuilder(string name, PdfRect rect)
{
    /// <summary>
    /// Field name (used as the field identifier in the PDF)
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Field rectangle in PDF coordinates
    /// </summary>
    public PdfRect Rect { get; protected set; } = rect;

    /// <summary>
    /// Tooltip text shown on hover
    /// </summary>
    public string? Tooltip { get; protected set; }

    /// <summary>
    /// Whether the field is read-only
    /// </summary>
    public bool IsReadOnly { get; protected set; }

    /// <summary>
    /// Whether the field is required
    /// </summary>
    public bool IsRequired { get; protected set; }

    /// <summary>
    /// Whether the field should not be exported
    /// </summary>
    public bool NoExport { get; protected set; }

    /// <summary>
    /// Border color
    /// </summary>
    public PdfColor? BorderColor { get; protected set; }

    /// <summary>
    /// Background color
    /// </summary>
    public PdfColor? BackgroundColor { get; protected set; }

    /// <summary>
    /// Border width in points
    /// </summary>
    public double BorderWidth { get; protected set; } = 1;

    /// <summary>
    /// Border style
    /// </summary>
    public PdfBorderStyle BorderStyle { get; protected set; } = PdfBorderStyle.Solid;

    /// <summary>
    /// Dash pattern for dashed borders (dash length, gap length)
    /// </summary>
    public double[]? DashPattern { get; protected set; }
}