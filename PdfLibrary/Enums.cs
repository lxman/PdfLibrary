// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming
#pragma warning disable CA1050

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
/// Types of path segments
/// </summary>
public enum PdfPathSegmentType
{
    /// <summary>
    /// Move to
    /// </summary>
    MoveTo,
    /// <summary>
    /// Line to
    /// </summary>
    LineTo,
    /// <summary>
    /// Curve to
    /// </summary>
    CurveTo,      // Cubic Bézier curve with two control points
    /// <summary>
    /// Curve to (vertical)
    /// </summary>
    CurveToV,     // Cubic Bezier with first control point = current point
    /// <summary>
    /// Curve to (horizontal)
    /// </summary>
    CurveToY,     // Cubic Bezier with the second control point = endpoint
    /// <summary>
    /// Close path
    /// </summary>
    ClosePath,
    /// <summary>
    /// Rectangle - used for drawing borders and filling rectangles
    /// </summary>
    Rectangle
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
/// Annotation flags (F entry in annotation dictionary)
/// </summary>
[Flags]
public enum PdfAnnotationFlags
{
    /// <summary>
    /// None
    /// </summary>
    None = 0,
    /// <summary>
    /// Invisible
    /// </summary>
    Invisible = 1 << 0,
    /// <summary>
    /// Hidden
    /// </summary>
    Hidden = 1 << 1,
    /// <summary>
    /// Print
    /// </summary>
    Print = 1 << 2,
    /// <summary>
    /// No zoom
    /// </summary>
    NoZoom = 1 << 3,
    /// <summary>
    /// No rotation
    /// </summary>
    NoRotate = 1 << 4,
    /// <summary>
    /// No view
    /// </summary>
    NoView = 1 << 5,
    /// <summary>
    /// Read only
    /// </summary>
    ReadOnly = 1 << 6,
    /// <summary>
    /// Locked
    /// </summary>
    Locked = 1 << 7,
    /// <summary>
    /// Toggle no view
    /// </summary>
    ToggleNoView = 1 << 8,
    /// <summary>
    /// Locked contents
    /// </summary>
    LockedContents = 1 << 9
}

/// <summary>
/// Highlight mode for link annotations
/// </summary>
public enum PdfLinkHighlightMode
{
    /// <summary>
    /// No highlighting
    /// </summary>
    None,

    /// <summary>
    /// Invert the colors within the annotation rectangle
    /// </summary>
    Invert,

    /// <summary>
    /// Invert the border of the annotation
    /// </summary>
    Outline,

    /// <summary>
    /// Display the annotation as if it were being pushed
    /// </summary>
    Push
}

/// <summary>
/// Standard icons for text annotations
/// </summary>
public enum PdfTextAnnotationIcon
{
    /// <summary>
    /// Comment icon
    /// </summary>
    Comment,
    /// <summary>
    /// Key icon
    /// </summary>
    Key,
    /// <summary>
    /// Note icon
    /// </summary>
    Note,
    /// <summary>
    /// Help icon
    /// </summary>
    Help,
    /// <summary>
    /// New Paragraph icon
    /// </summary>
    NewParagraph,
    /// <summary>
    /// Paragraph icon
    /// </summary>
    Paragraph,
    /// <summary>
    /// Insert icon
    /// </summary>
    Insert
}

/// <summary>
/// Encryption method for PDF documents.
/// </summary>
public enum PdfEncryptionMethod
{
    /// <summary>RC4 40-bit encryption (V=1, R=2). Legacy, not recommended.</summary>
    Rc4_40,

    /// <summary>RC4 128-bit encryption (V=2, R=3). Legacy, not recommended.</summary>
    Rc4_128,

    /// <summary>AES 128-bit encryption (V=4, R=4).</summary>
    Aes128,

    /// <summary>AES 256-bit encryption (V=5, R=6). Recommended.</summary>
    Aes256
}

/// <summary>
/// Page numbering styles
/// </summary>
public enum PdfPageLabelStyle
{
    /// <summary>
    /// No numeric portion - only prefix is used
    /// </summary>
    None,

    /// <summary>
    /// Decimal Arabic numerals (1, 2, 3, ...)
    /// </summary>
    Decimal,

    /// <summary>
    /// Uppercase Roman numerals (I, II, III, IV, ...)
    /// </summary>
    UppercaseRoman,

    /// <summary>
    /// Lowercase Roman numerals (i, ii, iii, iv, ...)
    /// </summary>
    LowercaseRoman,

    /// <summary>
    /// Uppercase letters (A, B, C, ... Z, AA, AB, ...)
    /// </summary>
    UppercaseLetters,

    /// <summary>
    /// Lowercase letters (a, b, c, ... z, aa, ab, ...)
    /// </summary>
    LowercaseLetters
}

/// <summary>
/// Types of PDF destinations that control how the page is displayed
/// </summary>
public enum PdfDestinationType
{
    /// <summary>
    /// Display page at specified coordinates and zoom level [/XYZ left top zoom]
    /// </summary>
    XYZ,

    /// <summary>
    /// Fit entire page in window [/Fit]
    /// </summary>
    Fit,

    /// <summary>
    /// Fit page width in window at specified top coordinate [/FitH top]
    /// </summary>
    FitH,

    /// <summary>
    /// Fit page height in window at specified left coordinate [/FitV left]
    /// </summary>
    FitV,

    /// <summary>
    /// Fit specified rectangle in window [/FitR left bottom right top]
    /// </summary>
    FitR,

    /// <summary>
    /// Fit bounding box of page contents in window [/FitB]
    /// </summary>
    FitB,

    /// <summary>
    /// Fit width of bounding box in window [/FitBH top]
    /// </summary>
    FitBH,

    /// <summary>
    /// Fit height of bounding box in window [/FitBV left]
    /// </summary>
    FitBV
}

/// <summary>
/// Units of measurement for PDF coordinates
/// </summary>
public enum PdfUnit
{
    /// <summary>
    /// PDF points (72 points per inch) - native PDF unit
    /// </summary>
    Points,

    /// <summary>
    /// Inches (1 inch = 72 points)
    /// </summary>
    Inches,

    /// <summary>
    /// Millimeters (1 mm = 2.834645669 points)
    /// </summary>
    Millimeters,

    /// <summary>
    /// Centimeters (1 cm = 28.34645669 points)
    /// </summary>
    Centimeters
}

/// <summary>
/// Coordinate system origin for specifying positions
/// </summary>
public enum PdfOrigin
{
    /// <summary>
    /// Origin in the bottom-left corner (PDF native)
    /// Y increases upward
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Origin in the top-left corner (screen-like)
    /// Y increases downward
    /// </summary>
    TopLeft
}

/// <summary>
/// Border styles for form fields
/// </summary>
public enum PdfBorderStyle
{
    /// <summary>
    /// Solid border (default)
    /// </summary>
    Solid,

    /// <summary>
    /// Dashed border
    /// </summary>
    Dashed,

    /// <summary>
    /// 3D beveled border (raised appearance)
    /// </summary>
    Beveled,

    /// <summary>
    /// 3D inset border (sunken appearance)
    /// </summary>
    Inset,

    /// <summary>
    /// Single line at bottom (underline style)
    /// </summary>
    Underline
}

/// <summary>
/// Represents PDF document permissions as defined in ISO 32000-1:2008 Table 22.
/// These permissions are set by the document creator and enforced by PDF readers.
/// Note: These are "honor system" restrictions - the content is still decryptable.
/// </summary>
[Flags]
public enum PdfPermissionFlags
{
    /// <summary>No permissions granted</summary>
    None = 0,

    /// <summary>Bit 3: Print the document</summary>
    Print = 1 << 2,

    /// <summary>Bit 4: Modify contents (other than annotations, form fields, etc.)</summary>
    ModifyContents = 1 << 3,

    /// <summary>Bit 5: Copy or extract text and graphics</summary>
    CopyContent = 1 << 4,

    /// <summary>Bit 6: Add or modify annotations and form fields</summary>
    ModifyAnnotations = 1 << 5,

    /// <summary>Bit 9: Fill in form fields (even if bit 6 is clear)</summary>
    FillForms = 1 << 8,

    /// <summary>Bit 10: Extract text/graphics for accessibility</summary>
    ExtractForAccessibility = 1 << 9,

    /// <summary>Bit 11: Assemble document (insert, rotate, delete pages, bookmarks)</summary>
    AssembleDocument = 1 << 10,

    /// <summary>Bit 12: Print high quality (degraded printing if clear)</summary>
    PrintHighQuality = 1 << 11,

    /// <summary>All permissions granted</summary>
    All = Print | ModifyContents | CopyContent | ModifyAnnotations |
          FillForms | ExtractForAccessibility | AssembleDocument | PrintHighQuality
}

/// <summary>
/// Text alignment options
/// </summary>
public enum PdfTextAlignment
{
    /// <summary>
    /// Left alignment (default)
    /// </summary>
    Left = 0,
    /// <summary>
    /// Center alignment
    /// </summary>
    Center = 1,
    /// <summary>
    /// Right alignment
    /// </summary>
    Right = 2
}

/// <summary>
/// Check mark styles for checkboxes and radio buttons
/// </summary>
public enum PdfCheckStyle
{
    /// <summary>
    /// Check mark style (default)
    /// </summary>
    Check,
    /// <summary>
    /// Circle
    /// </summary>
    Circle,
    /// <summary>
    /// Cross
    /// </summary>
    Cross,
    /// <summary>
    /// Diamond
    /// </summary>
    Diamond,
    /// <summary>
    /// Square
    /// </summary>
    Square,
    /// <summary>
    /// Star
    /// </summary>
    Star
}

/// <summary>
/// Image format for encoded output.
/// </summary>
public enum ImageFormat
{
    /// <summary>
    /// Portable Network Graphics (PNG)
    /// </summary>
    Png,
    /// <summary>
    /// Joint Photographic Experts Group (JPEG)
    /// </summary>
    Jpeg,
    /// <summary>
    /// Web Picture Format (WebP)
    /// </summary>
    Webp,
    /// <summary>
    /// Graphics Interchange Format (GIF)
    /// </summary>
    Gif,
    /// <summary>
    /// Windows Bitmap (BMP)
    /// </summary>
    Bmp
}

