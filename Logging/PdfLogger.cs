using System;
using System.IO;
using Serilog;
using Serilog.Core;

namespace Logging
{
    /// <summary>
    /// Central logging facility for PDF library operations.
    /// Provides category-based logging with configurable output.
    /// </summary>
    public static class PdfLogger
    {
        private static PdfLogConfiguration _config = new PdfLogConfiguration();
        private static Logger? _logger;
        private static readonly object LockObject = new object();
        private static bool _isInitialized = false;

        /// <summary>
        /// Initialize the logger with the specified configuration.
        /// Must be called once at application startup before any logging occurs.
        /// </summary>
        /// <param name="config">Logging configuration</param>
        public static void Initialize(PdfLogConfiguration config)
        {
            lock (LockObject)
            {
                _config = config ?? throw new ArgumentNullException(nameof(config));

                // Dispose existing logger if reinitializing
                _logger?.Dispose();

                // Ensure log directory exists
                string? logDirectory = Path.GetDirectoryName(_config.LogFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                // Delete existing log file if not appending
                if (!_config.AppendToLog && File.Exists(_config.LogFilePath))
                {
                    try
                    {
                        File.Delete(_config.LogFilePath);
                    }
                    catch
                    {
                        // Ignore errors if file is locked
                    }
                }

                // Configure Serilog
                _logger = new LoggerConfiguration()
                    .WriteTo.File(
                        _config.LogFilePath,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Message:lj}{NewLine}",
                        shared: true,  // Allow multiple processes to write
                        flushToDiskInterval: TimeSpan.FromSeconds(1)
                    )
                    .CreateLogger();

                _isInitialized = true;
            }
        }

        /// <summary>
        /// Log a message for the specified category.
        /// The message will only be logged if the category is enabled in configuration.
        /// </summary>
        /// <param name="category">The logging category</param>
        /// <param name="message">The message to log</param>
        public static void Log(LogCategory category, string message)
        {
            // Auto-initialize with defaults if not explicitly initialized
            if (!_isInitialized)
            {
                Initialize(new PdfLogConfiguration());
            }

            // Check if this category is enabled
            if (!IsCategoryEnabled(category))
                return;

            // Get category prefix
            string prefix = GetCategoryPrefix(category);

            // Log with category prefix
            _logger?.Information($"{prefix} {message}");
        }

        /// <summary>
        /// Checks if a specific category is enabled in the current configuration.
        /// </summary>
        public static bool IsCategoryEnabled(LogCategory category)
        {
            return category switch
            {
                LogCategory.Images => _config.LogImages,
                LogCategory.Text => _config.LogText,
                LogCategory.Graphics => _config.LogGraphics,
                LogCategory.Transforms => _config.LogTransforms,
                LogCategory.PdfTool => _config.LogPdfTool,
                LogCategory.Melville => _config.LogMelville,
                _ => false
            };
        }

        /// <summary>
        /// Gets the current configuration (read-only).
        /// </summary>
        public static PdfLogConfiguration GetConfiguration()
        {
            return _config;
        }

        /// <summary>
        /// Closes and flushes the logger. Call at application shutdown.
        /// </summary>
        public static void Shutdown()
        {
            lock (LockObject)
            {
                _logger?.Dispose();
                _logger = null;
                _isInitialized = false;
            }
        }

        private static string GetCategoryPrefix(LogCategory category)
        {
            return category switch
            {
                LogCategory.Images => "[Images]",
                LogCategory.Text => "[Text]",
                LogCategory.Graphics => "[Graphics]",
                LogCategory.Transforms => "[Transforms]",
                LogCategory.PdfTool => "[PdfTool]",
                LogCategory.Melville => "[Melville]",
                _ => "[Unknown]"
            };
        }
    }
}
