using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using Serilog;

namespace PdfTool;

/// <summary>
/// Main window for the PDF Viewer application
/// Demonstrates how to use the PDF rendering system
/// </summary>
public partial class MainWindow : Window
{
    private PdfDocument? _document;
    private int _currentPage = 0;

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

            // Load PDF document
            using FileStream stream = File.OpenRead(filePath);
            _document = PdfDocument.Load(stream);

            _currentPage = 0;

            // Update UI
            TotalPagesText.Text = _document.GetPageCount().ToString();
            CurrentPageText.Text = "0";

            // Enable navigation buttons
            NextPageButton.IsEnabled = _document.GetPageCount() > 0;
            PrevPageButton.IsEnabled = false;

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

            // Clear previous rendering
            PdfRenderer.Clear();

            // Set current page number for diagnostics
            PdfRenderer.CurrentPageNumber = _currentPage;

            // Get the page (GetPage uses 0-based indexing)
            PdfPage? page = _document.GetPage(_currentPage - 1);
            if (page == null)
            {
                StatusText.Text = $"Error: Page {_currentPage} not found";
                MessageBox.Show($"Page {_currentPage} could not be loaded", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Set page size
            PdfRectangle mediaBox = page.GetMediaBox();
            PdfRenderer.SetPageSize(mediaBox.Width, mediaBox.Height);

            // Create an Optional Content Manager for layer visibility
            var optionalContentManager = new OptionalContentManager(_document);

            // Create a renderer and render the page
            var renderer = new PdfLibrary.Rendering.PdfRenderer(PdfRenderer, page.GetResources(), optionalContentManager, _document);
            renderer.RenderPage(page);

            // Log how many elements were added to canvas
            Log.Information("RenderCurrentPage: Canvas now has {ChildCount} elements", PdfRenderer.GetChildCount());

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
}
