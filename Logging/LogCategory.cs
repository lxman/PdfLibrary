namespace Logging
{
    /// <summary>
    /// Categories for filtering PDF library logs
    /// </summary>
    public enum LogCategory
    {
        /// <summary>
        /// Image rendering operations (XObject images, inline images, image transforms)
        /// </summary>
        Images,

        /// <summary>
        /// Text and font rendering operations (glyphs, character decoding, text positioning)
        /// </summary>
        Text,

        /// <summary>
        /// Graphics path operations (stroke, fill, shapes, colors, optional content)
        /// </summary>
        Graphics,

        /// <summary>
        /// Transformation matrix operations (CTM, state save/restore)
        /// </summary>
        Transforms,

        /// <summary>
        /// PdfTool application-level events (resources, operators, parser)
        /// </summary>
        PdfTool,

        /// <summary>
        /// Melville library operations (third-party rendering)
        /// </summary>
        Melville
    }
}
