using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfLibrary.Document;
using PdfLibrary.Rendering.SkiaSharp;
using Serilog;
using SkiaSharp;
using PdfDocument = PdfLibrary.Structure.PdfDocument;
using PdfPage = PdfLibrary.Document.PdfPage;

namespace PdfLibrary.Wpf.Viewer;

/// <summary>
/// The main window for the PdfLibrary Viewer
/// </summary>
public partial class MainWindow : Window
{
    // Document state
    private PdfDocument? _pdfDoc;
    private int _currentPage;
    private int _totalPages;
    private string? _currentFilePath;

    // Zoom state
    private double _zoomLevel = 1.0;
    private bool _isUpdatingZoom;

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
        Dispatcher.BeginInvoke(new Action(() => _ = LoadPdfAsync(filePath)));
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*",
            Title = "Open PDF File"
        };

        if (dialog.ShowDialog() != true) return;
        _ = LoadPdfAsync(dialog.FileName);
    }

    private async Task LoadPdfAsync(string filePath)
    {
        try
        {
            StatusText.Text = $"Loading {Path.GetFileName(filePath)}...";
            _currentFilePath = filePath;
            _currentPage = 1;
            _zoomLevel = 1.0;

            // Clear existing document
            _pdfDoc?.Dispose();

            // Load the PDF
            await Task.Run(() => LoadPdfDocument(filePath));

            // Get page count
            _totalPages = _pdfDoc?.GetPageCount() ?? 0;

            if (_totalPages == 0)
            {
                MessageBox.Show("Failed to load PDF.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Update UI
            TotalPagesText.Text = _totalPages.ToString();
            CurrentPageText.Text = "1";
            UpdateZoomDisplay();

            // Enable controls
            NextPageButton.IsEnabled = _totalPages > 1;
            PrevPageButton.IsEnabled = false;
            ExportButton.IsEnabled = true;
            PrintButton.IsEnabled = true;
            EnableZoomControls(true);

            // Render first page
            await RenderPageAsync();

            StatusText.Text = $"Loaded {Path.GetFileName(filePath)} ({_totalPages} pages)";
            Title = $"PdfLibrary Viewer - {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error loading PDF";
            MessageBox.Show($"Failed to load PDF: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Failed to load PDF");
        }
    }

    private void LoadPdfDocument(string filePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            _pdfDoc = PdfDocument.Load(stream);
            PdfRenderer.SetDocument(_pdfDoc);
            Log.Information("PdfLibrary loaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PdfLibrary failed to load");
            throw;
        }
    }

    private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        await RenderPageAsync();
        UpdateNavigationButtons();
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage >= _totalPages) return;
        _currentPage++;
        await RenderPageAsync();
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        CurrentPageText.Text = _currentPage.ToString();
        PrevPageButton.IsEnabled = _currentPage > 1;
        NextPageButton.IsEnabled = _currentPage < _totalPages;
    }

    private async Task RenderPageAsync()
    {
        StatusText.Text = $"Rendering page {_currentPage} at {_zoomLevel * 100:F0}%...";

        await Task.Run(RenderPage);

        StatusText.Text = $"Page {_currentPage} of {_totalPages} ({_zoomLevel * 100:F0}%)";
        UpdateNavigationButtons();
    }

    private void RenderPage()
    {
        try
        {
            if (_pdfDoc == null) return;

            Dispatcher.Invoke(() =>
            {
                PdfPage? page = _pdfDoc.GetPage(_currentPage - 1);
                if (page == null)
                {
                    StatusText.Text = $"Page {_currentPage} not found";
                    return;
                }

                // Get page dimensions for the render target
                PdfRectangle cropBox = page.GetCropBox();

                // Get or create the render target with proper dimensions
                SkiaSharpRenderTarget renderTarget = PdfRenderer.GetOrCreateRenderTarget(cropBox.Width, cropBox.Height, _zoomLevel);

                // Use the simplified public API
                page.Render(renderTarget, _currentPage, _zoomLevel);

                // Finalize and display the rendered content
                PdfRenderer.FinalizeRendering(_currentPage);

                Log.Information("PdfLibrary rendered page {Page} at {Zoom}%", _currentPage, _zoomLevel * 100);
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PdfLibrary render failed");
            Dispatcher.Invoke(() => StatusText.Text = $"Render error: {ex.Message}");
        }
    }

    // ==================== ZOOM CONTROLS ====================

    private async void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        await SetZoomAsync(_zoomLevel * 1.25);
    }

    private async void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        await SetZoomAsync(_zoomLevel / 1.25);
    }

    private async void FitToPage_Click(object sender, RoutedEventArgs e)
    {
        // Calculate zoom to fit page height in the viewport
        PdfPage? page = _pdfDoc?.GetPage(_currentPage - 1);
        if (page is null) return;
        PdfRectangle cropBox = page.GetCropBox();
        double viewportHeight = PdfScroll.ActualHeight - 20; // Account for margins
        double zoom = viewportHeight / cropBox.Height;
        await SetZoomAsync(zoom);
    }

    private async void FitToWidth_Click(object sender, RoutedEventArgs e)
    {
        // Calculate zoom to fit page width in the viewport
        PdfPage? page = _pdfDoc?.GetPage(_currentPage - 1);
        if (page is null) return;
        PdfRectangle cropBox = page.GetCropBox();
        double viewportWidth = PdfScroll.ActualWidth - 20; // Account for margins/scrollbar
        double zoom = viewportWidth / cropBox.Width;
        await SetZoomAsync(zoom);
    }

    private async void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        await SetZoomAsync(1.0);
    }

    private async void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingZoom) return; // Prevent feedback loop
        _zoomLevel = e.NewValue;
        UpdateZoomDisplay();
        if (_pdfDoc != null) // Only render if document is loaded
        {
            await RenderPageAsync();
        }
    }

    private async Task SetZoomAsync(double newZoom)
    {
        // Clamp zoom level
        newZoom = Math.Clamp(newZoom, 0.25, 4.0);
        if (Math.Abs(newZoom - _zoomLevel) < 0.01) return; // No significant change

        _zoomLevel = newZoom;

        // Update slider without triggering ValueChanged
        _isUpdatingZoom = true;
        ZoomSlider.Value = _zoomLevel;
        _isUpdatingZoom = false;

        UpdateZoomDisplay();
        await RenderPageAsync();
    }

    private void UpdateZoomDisplay()
    {
        if (ZoomText != null)
            ZoomText.Text = $"{_zoomLevel * 100:F0}";
    }

    private void EnableZoomControls(bool enabled)
    {
        ZoomInButton.IsEnabled = enabled;
        ZoomOutButton.IsEnabled = enabled;
        ZoomSlider.IsEnabled = enabled;
        FitToPageButton.IsEnabled = enabled;
        FitToWidthButton.IsEnabled = enabled;
        ActualSizeButton.IsEnabled = enabled;
    }

    // ==================== MOUSE WHEEL ZOOM ====================

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only zoom if Ctrl is held
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        e.Handled = true;

        // Zoom in/out by 10%
        double factor = e.Delta > 0 ? 1.1 : 0.9;
        _ = SetZoomAsync(_zoomLevel * factor);
    }

    // ==================== IMAGE EXPORT ====================

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfDoc == null || _currentFilePath == null)
        {
            MessageBox.Show("Please load a PDF first.", "No Document",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            Title = "Export Page as Image",
            FileName = $"Page{_currentPage}_Zoom{_zoomLevel * 100:F0}.png"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            StatusText.Text = "Exporting image...";

            await Task.Run(() => SaveImageToFile(dialog.FileName));

            StatusText.Text = $"Image saved to {dialog.FileName}";
            MessageBox.Show($"Page {_currentPage} saved to:\n{dialog.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error exporting image";
            MessageBox.Show($"Failed to export image: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Image export failed");
        }
    }

    private void SaveImageToFile(string path)
    {
        PdfPage? page = _pdfDoc?.GetPage(_currentPage - 1);
        if (page == null) return;

        // Use CropBox for output dimensions (visible area)
        PdfRectangle cropBox = page.GetCropBox();
        var width = (int)(cropBox.Width * _zoomLevel);
        var height = (int)(cropBox.Height * _zoomLevel);

        var renderTarget = new SkiaSharpRenderTarget(width, height, _pdfDoc);
        try
        {
            // Use the simplified public API
            page.Render(renderTarget, _currentPage, _zoomLevel);
            renderTarget.SaveToFile(path);
            Log.Information("Saved image to {Path}", path);
        }
        finally
        {
            renderTarget.Dispose();
        }
    }

    // ==================== PRINTING ====================

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfDoc == null)
        {
            MessageBox.Show("Please load a PDF first.", "No Document",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        try
        {
            StatusText.Text = "Printing...";

            // Get printer capabilities
            PrintCapabilities capabilities = printDialog.PrintQueue.GetPrintCapabilities(printDialog.PrintTicket);
            double printableWidth = capabilities.PageImageableArea?.ExtentWidth ?? 816; // Default to letter size
            double printableHeight = capabilities.PageImageableArea?.ExtentHeight ?? 1056;

            // Create a visual to print
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // Render the current page at print resolution
                BitmapSource bitmap = RenderPageToBitmap(printableWidth, printableHeight);
                dc.DrawImage(bitmap, new Rect(0, 0, printableWidth, printableHeight));
            }

            // Print
            string description = $"PdfLibrary - Page {_currentPage}";
            printDialog.PrintVisual(visual, description);

            StatusText.Text = $"Printed page {_currentPage}";
            Log.Information("Printed page {Page}", _currentPage);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Print error";
            MessageBox.Show($"Failed to print: {ex.Message}", "Print Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Print failed");
        }
    }

    private BitmapSource RenderPageToBitmap(double targetWidth, double targetHeight)
    {
        PdfPage? page = _pdfDoc?.GetPage(_currentPage - 1);
        if (page == null)
            throw new InvalidOperationException("Page not found");

        // Get page dimensions
        PdfRectangle cropBox = page.GetCropBox();

        // Calculate scale to fit the page in the printable area while maintaining aspect ratio
        double scaleX = targetWidth / cropBox.Width;
        double scaleY = targetHeight / cropBox.Height;
        double scale = Math.Min(scaleX, scaleY);

        var width = (int)(cropBox.Width * scale);
        var height = (int)(cropBox.Height * scale);

        // Render at calculated size using the simplified public API
        using var renderTarget = new SkiaSharpRenderTarget(width, height, _pdfDoc);
        page.Render(renderTarget, _currentPage, scale);

        // Get the SKImage and convert to WPF BitmapSource
        using SKImage? skImage = renderTarget.GetImage();
        using SKData? data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }
}
