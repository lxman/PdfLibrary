using System.IO;
using System.Windows;
using Logging;
using Serilog;

namespace PdfTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Use an absolute path for the log file in the application directory
        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string logFile = Path.Combine(appDirectory, "pdftool-log.txt");

        // Clear the old log file if it exists
        if (File.Exists(logFile))
        {
            try
            {
                // Try to delete the file
                File.Delete(logFile);
            }
            catch
            {
                try
                {
                    // If delete fails (the file locked), truncate it instead
                    File.WriteAllText(logFile, string.Empty);
                }
                catch
                {
                    // Ignore if we can't clear it - Serilog will append
                }
            }
        }

        // Configure Serilog to write to a file (will create a fresh file each run)
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logFile, shared: true)
            .CreateLogger();

        Log.Information("PdfTool starting...");
        if (e.Args.Length > 0)
        {
            Log.Information("Command line argument: {FilePath}", e.Args[0]);
        }

        // Initialize PdfLibrary logger
        string pdfLibraryLogFile = Path.Combine(appDirectory, "logs", "pdflibrary.log");
        PdfLogger.Initialize(new PdfLogConfiguration
        {
            LogImages = true,         // Enable image logging for debugging
            LogText = false,          // Disable text logging
            LogGraphics = false,      // Disable graphics logging
            LogTransforms = true,     // Enable transform logging (default ON)
            LogPdfTool = false,       // Disable PdfTool app logging
            LogMelville = true,       // Enable Melville library logging
            AppendToLog = false,      // Clear log on each run
            LogFilePath = pdfLibraryLogFile
        });

        PdfLogger.Log(LogCategory.PdfTool, "PdfTool initialized PdfLibrary logging");

        // Create the main window
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        // If a PDF file path was provided as a command line argument, load it
        if (e.Args.Length > 0)
        {
            mainWindow.LoadPdfFromCommandLine(e.Args[0]);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("PdfTool exiting...");
        PdfLogger.Shutdown();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}