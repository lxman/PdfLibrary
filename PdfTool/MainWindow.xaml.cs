using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Serilog;
using SkiaSharp;
using PdfDocument = PdfLibrary.Structure.PdfDocument;
using PdfPage = PdfLibrary.Document.PdfPage;

namespace PdfTool;

/// <summary>
/// Main window for the PDF Viewer application
/// Demonstrates how to use the PDF rendering system
/// </summary>
public partial class MainWindow : Window
{
    private PdfDocument? _document;
    private int _currentPage = 0;
    private string? _currentFilePath;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Loads a PDF file from command line arguments after the window is loaded
    /// </summary>
    public void LoadPdfFromCommandLine(string filePath)
    {
        // Use Dispatcher to ensure the window is fully loaded before calling LoadPdf
        Dispatcher.BeginInvoke(new Action(() => LoadPdf(filePath)));
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
            Title = "Open PDF File"
        };

        if (dialog.ShowDialog() != true) return;
        LoadPdf(dialog.FileName);
        Title = $"PDF Viewer - {Path.GetFileName(dialog.FileName)}";
    }

    private void LoadPdf(string filePath)
    {
        try
        {
            StatusText.Text = $"Loading {Path.GetFileName(filePath)}...";

            // Store file path for comparison feature
            _currentFilePath = filePath;

            // Load PDF document
            using FileStream stream = File.OpenRead(filePath);
            _document = PdfDocument.Load(stream);

            // Set document on renderer for resolving indirect references
            PdfRenderer.SetDocument(_document);

            _currentPage = 0;

            // Update UI
            TotalPagesText.Text = _document.GetPageCount().ToString();
            CurrentPageText.Text = "0";

            // Enable navigation buttons
            NextPageButton.IsEnabled = _document.GetPageCount() > 0;
            PrevPageButton.IsEnabled = false;
            CompareButton.IsEnabled = _document.GetPageCount() > 0;

            // DIAGNOSTIC: Skip page 1, start at page 2 to avoid image masking issue
            /*
            if (_document.GetPageCount() >= 2)
            {
                _currentPage = 2;
                RenderCurrentPage();
            }
            */
            if (_document.GetPageCount() > 0)
            {
                _currentPage = 1;
                RenderCurrentPage();
            }

            StatusText.Text = $"Loaded {Path.GetFileName(filePath)} ({_document.GetPageCount()} pages)";
            Title = $"PDF Tool - {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error loading PDF";
            MessageBox.Show($"Failed to load PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null || _currentPage <= 1) return;

        _currentPage--;
        RenderCurrentPage();
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null || _currentPage >= _document.GetPageCount()) return;

        _currentPage++;
        RenderCurrentPage();
    }

    private void RenderCurrentPage()
    {
        if (_document == null || _currentPage < 1 || _currentPage > _document.GetPageCount())
            return;

        try
        {
            StatusText.Text = $"Rendering page {_currentPage}...";

            // Get the page (GetPage uses 0-based indexing)
            PdfPage? page = _document.GetPage(_currentPage - 1);
            if (page == null)
            {
                StatusText.Text = $"Error: Page {_currentPage} not found";
                MessageBox.Show($"Page {_currentPage} could not be loaded", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get page dimensions
            PdfRectangle mediaBox = page.GetMediaBox();

            // Begin rendering the page (sets up SkiaSharp render target)
            PdfRenderer.BeginPage(_currentPage, mediaBox.Width, mediaBox.Height);

            // Create an Optional Content Manager for layer visibility
            var optionalContentManager = new OptionalContentManager(_document);

            // Create a renderer and render the page
            var renderer = new PdfLibrary.Rendering.PdfRenderer(PdfRenderer, page.GetResources(), optionalContentManager, _document);
            renderer.RenderPage(page);

            // End rendering (captures the rendered image for display)
            PdfRenderer.EndPage();

            // Log completion
            Log.Information("RenderCurrentPage: Page {PageNumber} rendered", _currentPage);

            // Update UI
            CurrentPageText.Text = _currentPage.ToString();
            PrevPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _document.GetPageCount();

            StatusText.Text = $"Page {_currentPage} of {_document.GetPageCount()}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error rendering page {_currentPage}";
            MessageBox.Show($"Failed to render page {_currentPage}: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CompareButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null || _currentFilePath == null || _currentPage < 1)
        {
            MessageBox.Show("Please load a PDF and navigate to a page first.", "No Page",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText.Text = "Generating comparison images...";

            // Create output directory
            string outputDir = Path.Combine(Path.GetDirectoryName(_currentFilePath) ?? ".", "Comparison");
            Directory.CreateDirectory(outputDir);

            string pdfLibraryPath = Path.Combine(outputDir, $"PdfLibrary_Page{_currentPage}.png");
            string pdfiumPath = Path.Combine(outputDir, $"PDFium_Page{_currentPage}.png");

            // Get current page dimensions
            var page = _document.GetPage(_currentPage - 1);
            if (page == null)
            {
                MessageBox.Show("Could not get current page.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var mediaBox = page.GetMediaBox();
            int width = (int)Math.Ceiling(mediaBox.Width);
            int height = (int)Math.Ceiling(mediaBox.Height);

            // Render with PdfLibrary
            var renderTarget = new SkiaSharpRenderTarget(width, height, _document);
            try
            {
                renderTarget.BeginPage(_currentPage, mediaBox.Width, mediaBox.Height);
                var optionalContentManager = new OptionalContentManager(_document);
                var renderer = new PdfLibrary.Rendering.PdfRenderer(renderTarget, page.GetResources(), optionalContentManager, _document);
                renderer.RenderPage(page);
                renderTarget.EndPage();
                renderTarget.SaveToFile(pdfLibraryPath);
            }
            finally
            {
                renderTarget.Dispose();
            }

            // Render with PDFium
            using (var pdfiumDoc = new PDFiumSharp.PdfDocument(_currentFilePath))
            {
                var pdfiumPage = pdfiumDoc.Pages[_currentPage - 1];
                int pdfiumWidth = (int)pdfiumPage.Width;
                int pdfiumHeight = (int)pdfiumPage.Height;

                using var pdfiumBitmap = new PDFiumSharp.PDFiumBitmap(pdfiumWidth, pdfiumHeight, true);
                pdfiumBitmap.FillRectangle(0, 0, pdfiumWidth, pdfiumHeight, 0xFFFFFFFF);
                pdfiumPage.Render(pdfiumBitmap, (0, 0, pdfiumWidth, pdfiumHeight),
                    PDFiumSharp.Enums.PageOrientations.Normal,
                    PDFiumSharp.Enums.RenderingFlags.None);

                // PDFium saves as BMP, so convert to PNG via SkiaSharp
                string tempPath = Path.GetTempFileName() + ".bmp";
                try
                {
                    pdfiumBitmap.Save(tempPath);
                    using var skBitmap = SKBitmap.Decode(tempPath);
                    using var image = SKImage.FromBitmap(skBitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using var stream = File.OpenWrite(pdfiumPath);
                    data.SaveTo(stream);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }

            StatusText.Text = $"Comparison images saved to {outputDir}";
            MessageBox.Show($"Comparison images saved:\n\n• {Path.GetFileName(pdfLibraryPath)}\n• {Path.GetFileName(pdfiumPath)}\n\nLocation: {outputDir}",
                "Comparison Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error generating comparison";
            MessageBox.Show($"Failed to generate comparison: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Comparison failed");
        }
    }
}
