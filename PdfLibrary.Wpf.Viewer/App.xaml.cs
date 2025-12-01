using System.IO;
using System.Windows;
using Logging;
using Serilog;

namespace PdfLibrary.Wpf.Viewer;

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
        string logFile = Path.Combine(appDirectory, "viewer-log.txt");

        // Clear the old log file if it exists
        if (File.Exists(logFile))
        {
            try
            {
                File.Delete(logFile);
            }
            catch
            {
                try
                {
                    File.WriteAllText(logFile, string.Empty);
                }
                catch
                {
                    // Ignore if we can't clear it - Serilog will append
                }
            }
        }

        // Configure Serilog to write to a file
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logFile, shared: true)
            .CreateLogger();

        Log.Information("PdfLibrary.Wpf.Viewer starting...");
        if (e.Args.Length > 0)
        {
            Log.Information("Command line argument: {FilePath}", e.Args[0]);
        }

        // Initialize PdfLibrary logger
        string pdfLibraryLogFile = Path.Combine(appDirectory, "logs", "pdflibrary.log");
        PdfLogger.Initialize(new PdfLogConfiguration
        {
            LogImages = true,
            LogText = false,
            LogGraphics = true,
            LogTransforms = true,
            LogPdfTool = false,
            LogMelville = false,
            LogTimings = false,
            AppendToLog = false,
            LogFilePath = pdfLibraryLogFile
        });

        PdfLogger.Log(LogCategory.PdfTool, "PdfLibrary.Wpf.Viewer initialized PdfLibrary logging");

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
        Log.Information("PdfLibrary.Wpf.Viewer exiting...");
        PdfLogger.Shutdown();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
