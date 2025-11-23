namespace PdfLibrary.Logging;

/// <summary>
/// Categories for PDF library logging.
/// Each category can be independently enabled or disabled.
/// </summary>
public enum LogCategory
{
    /// <summary>
    /// Image rendering operations (XObject images, inline images)
    /// </summary>
    Images,

    /// <summary>
    /// Text and font rendering operations
    /// </summary>
    Text,

    /// <summary>
    /// Graphics path operations (stroke, fill, shapes)
    /// </summary>
    Graphics,

    /// <summary>
    /// Transformation matrix operations (CTM, coordinate transformations)
    /// </summary>
    Transforms,

    /// <summary>
    /// PdfTool application-level logging
    /// </summary>
    PdfTool,

    /// <summary>
    /// Melville library logging (third-party)
    /// </summary>
    Melville
}
