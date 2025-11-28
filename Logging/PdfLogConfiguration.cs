namespace Logging
{
    /// <summary>
    /// Configuration for PDF library logging.
    /// Controls which categories are logged and where logs are written.
    /// </summary>
    public class PdfLogConfiguration
    {
        // ==================== Core PdfLibrary Categories ====================

        /// <summary>
        /// Enable logging for image rendering operations.
        /// Default: false
        /// </summary>
        public bool LogImages { get; set; }

        /// <summary>
        /// Enable logging for text and font rendering operations.
        /// Default: false
        /// </summary>
        public bool LogText { get; set; }

        /// <summary>
        /// Enable logging for graphics path operations (stroke, fill, shapes).
        /// Default: false
        /// </summary>
        public bool LogGraphics { get; set; }

        /// <summary>
        /// Enable logging for transformation matrix operations (CTM).
        /// Default: true (transforms are central to debugging all rendering issues)
        /// </summary>
        public bool LogTransforms { get; set; }

        // ==================== External Categories ====================

        /// <summary>
        /// Enable logging for PdfTool application-level events.
        /// Default: false
        /// </summary>
        public bool LogPdfTool { get; set; }

        /// <summary>
        /// Enable logging for Melville library operations (third-party).
        /// Default: false
        /// </summary>
        public bool LogMelville { get; set; }

        /// <summary>
        /// Enable logging for timing measurements.
        /// Default: false
        /// </summary>
        public bool LogTimings { get; set; }

        // ==================== File Settings ====================

        /// <summary>
        /// Path to the log file.
        /// Default: "logs/pdflibrary.log"
        /// </summary>
        public string LogFilePath { get; set; } = "logs/pdflibrary.log";

        /// <summary>
        /// If true, append to existing log file. If false, clear log file on each run.
        /// Default: false (clear each run)
        /// </summary>
        public bool AppendToLog { get; set; }
    }
}
