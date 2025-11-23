using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.SkiaSharp;
using Microsoft.Win32;
using PDFiumSharp;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Serilog;
using SkiaSharp;
using PdfDocument = PdfLibrary.Structure.PdfDocument;
using PdfPage = PdfLibrary.Document.PdfPage;

namespace PdfTool;

/// <summary>
/// The main window for the Comparative PDF Renderer
/// Displays PdfLibrary, PDFium, and Melville.Pdf renderings side-by-side
/// </summary>
public partial class MainWindow : Window
{
    // Document state
    private PdfDocument? _pdfLibraryDoc;
    private PDFiumSharp.PdfDocument? _pdfiumDoc;
    private DocumentRenderer? _melvilleDoc;
    private int _currentPage;
    private int _totalPages;
    private string? _currentFilePath;

    // Zoom state
    private double _zoomLevel = 1.0;
    private bool _isUpdatingZoom;

    // Scroll synchronization
    private bool _isSyncingScroll;

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

            // Clear existing documents
            _pdfLibraryDoc?.Dispose();
            _pdfiumDoc = null; // PDFiumSharp handles cleanup internally
            _melvilleDoc = null;

            // Load all 3 renderers in parallel
            Task[] loadTasks =
            [
                Task.Run(() => LoadPdfLibrary(filePath)),
                Task.Run(() => LoadPdfium(filePath)),
                Task.Run(async () => await LoadMelvilleAsync(filePath))
            ];

            await Task.WhenAll(loadTasks);

            // Get page count from the first successful loader
            _totalPages = _pdfLibraryDoc?.GetPageCount() ?? _pdfiumDoc?.Pages.Count ?? 0;

            if (_totalPages == 0)
            {
                MessageBox.Show("Failed to load PDF in any renderer.", "Error",
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
            CompareButton.IsEnabled = true;
            EnableZoomControls(true);

            // Render first page
            await RenderAllPanelsAsync();

            StatusText.Text = $"Loaded {Path.GetFileName(filePath)} ({_totalPages} pages)";
            Title = $"PDF Comparative Renderer - {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error loading PDF";
            MessageBox.Show($"Failed to load PDF: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Failed to load PDF");
        }
    }

    private void LoadPdfLibrary(string filePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            _pdfLibraryDoc = PdfDocument.Load(stream);
            PdfLibraryRenderer.SetDocument(_pdfLibraryDoc);
            Log.Information("PdfLibrary loaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PdfLibrary failed to load");
            Dispatcher.Invoke(() => ShowError("PdfLibrary", ex.Message));
        }
    }

    private void LoadPdfium(string filePath)
    {
        try
        {
            _pdfiumDoc = new PDFiumSharp.PdfDocument(filePath);
            Log.Information("PDFium loaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PDFium failed to load");
            Dispatcher.Invoke(() => ShowError("PDFium", ex.Message));
        }
    }

    private async Task LoadMelvilleAsync(string filePath)
    {
        try
        {
            var reader = new PdfReader();
            _melvilleDoc = await reader.ReadFromFileAsync(filePath);
            Log.Information("Melville.Pdf loaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Melville.Pdf failed to load");
            Dispatcher.Invoke(() => ShowError("Melville", ex.Message));
        }
    }

    private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1) return;
        _currentPage--;
        await RenderAllPanelsAsync();
        UpdateNavigationButtons();
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage >= _totalPages) return;
        _currentPage++;
        await RenderAllPanelsAsync();
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        CurrentPageText.Text = _currentPage.ToString();
        PrevPageButton.IsEnabled = _currentPage > 1;
        NextPageButton.IsEnabled = _currentPage < _totalPages;
    }

    private async Task RenderAllPanelsAsync()
    {
        StatusText.Text = $"Rendering page {_currentPage} at {_zoomLevel * 100:F0}%...";

        // Render all 3 in parallel (with individual error handling)
        Task[] renderTasks =
        [
            Task.Run(RenderPdfLibrary),
            Task.Run(RenderPdfium),
            Task.Run(async () => await RenderMelvilleAsync())
        ];

        // Wait for all to complete (don't fail if one fails)
        await Task.WhenAll(renderTasks.Select(t => t.ContinueWith(_ => { })));

        StatusText.Text = $"Page {_currentPage} of {_totalPages} ({_zoomLevel * 100:F0}%)";
        UpdateNavigationButtons();
    }

    private void RenderPdfLibrary()
    {
        try
        {
            if (_pdfLibraryDoc == null) return;

            Dispatcher.Invoke(() =>
            {
                PdfPage? page = _pdfLibraryDoc.GetPage(_currentPage - 1);
                if (page == null)
                {
                    ShowError("PdfLibrary", $"Page {_currentPage} not found");
                    return;
                }

                PdfRectangle mediaBox = page.GetMediaBox();
                double width = mediaBox.Width * _zoomLevel;
                double height = mediaBox.Height * _zoomLevel;

                var optionalContentManager = new OptionalContentManager(_pdfLibraryDoc);
                var renderer = new PdfRenderer(
                    PdfLibraryRenderer, page.GetResources(), optionalContentManager, _pdfLibraryDoc);
                renderer.RenderPage(page, _currentPage);

                Log.Information("PdfLibrary rendered page {Page}", _currentPage);
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PdfLibrary render failed");
            Dispatcher.Invoke(() => ShowError("PdfLibrary", ex.Message));
        }
    }

    private void RenderPdfium()
    {
        try
        {
            if (_pdfiumDoc == null || _pdfLibraryDoc == null) return;

            // Get dimensions from PdfLibrary page to match sizing (PDFium uses different units)
            PdfPage? pdfLibPage = _pdfLibraryDoc.GetPage(_currentPage - 1);
            if (pdfLibPage == null) return;

            PdfRectangle mediaBox = pdfLibPage.GetMediaBox();
            var width = (int)(mediaBox.Width * _zoomLevel);
            var height = (int)(mediaBox.Height * _zoomLevel);

            PDFiumSharp.PdfPage? page = _pdfiumDoc.Pages[_currentPage - 1];

            Log.Information("PDFium rendering: Page {Page}, PDFium size=({PdfiumW}x{PdfiumH}), Using PdfLib size=({Width}x{Height}), Zoom={Zoom}",
                _currentPage, page.Width, page.Height, width, height, _zoomLevel);

            // Create bitmap without alpha channel
            // Extract pixel data from PDFiumBitmap before Dispatcher.Invoke
            byte[] pixelData;
            int stride = width * 4; // 4 bytes per pixel (BGRA)

            using (var bitmap = new PDFiumBitmap(width, height, false))
            {
                // Fill with white background (BGRA format: 0xAARRGGBB)
                bitmap.FillRectangle(0, 0, width, height, 0xFFFFFFFF);

                // Render without destination rect - let PDFium scale to bitmap size
                page.Render(bitmap);

                Log.Information("PDFium render complete, extracting pixel data");

                // Extract pixel data while bitmap is still alive
                using var stream = new MemoryStream();
                bitmap.Save(stream, 0); // 0 = BMP format
                stream.Position = 0;

                // Load BMP to get pixel data
                var decoder = new BmpBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.Default);
                BitmapFrame? frame = decoder.Frames[0];

                // Extract pixels to byte array
                pixelData = new byte[height * stride];
                frame.CopyPixels(pixelData, stride, 0);
            }
            // PDFiumBitmap is now safely disposed

            Log.Information("PDFium pixel data extracted, converting to WriteableBitmap on UI thread");

            // Marshal pixel data to UI thread and create WriteableBitmap
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Create WriteableBitmap with 96 DPI (WPF default)
                    var writeableBitmap = new WriteableBitmap(
                        width, height,
                        96.0, 96.0, // DpiX, DpiY
                        PixelFormats.Bgra32,
                        null);

                    // Lock the bitmap for writing
                    writeableBitmap.Lock();

                    try
                    {
                        // Copy pixel data to WriteableBitmap
                        Marshal.Copy(pixelData, 0, writeableBitmap.BackBuffer, pixelData.Length);
                        writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        writeableBitmap.Unlock();
                    }

                    writeableBitmap.Freeze();
                    PdfiumImage.Source = writeableBitmap;
                    PdfiumError.Visibility = Visibility.Collapsed;
                    Log.Information("PDFium rendered page {Page}, WriteableBitmap size: {Width}x{Height}, DPI: 96x96",
                        _currentPage, writeableBitmap.PixelWidth, writeableBitmap.PixelHeight);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to create WriteableBitmap from pixel data");
                    ShowError("PDFium", "Failed to create bitmap: " + ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PDFium render failed");
            Dispatcher.Invoke(() => ShowError("PDFium", ex.Message));
        }
    }

    private async Task RenderMelvilleAsync()
    {
        try
        {
            if (_melvilleDoc is null || _pdfLibraryDoc is null) return;

            // Get dimensions from the PdfLibrary page to match sizing
            PdfPage? pdfLibPage = _pdfLibraryDoc.GetPage(_currentPage - 1);
            if (pdfLibPage is null) return;

            PdfRectangle mediaBox = pdfLibPage.GetMediaBox();
            var width = (int)(mediaBox.Width * _zoomLevel);
            var height = (int)(mediaBox.Height * _zoomLevel);

            // Render to SKSurface using 1-based page number and explicit size
            // Pass -1 for both width and height to let Melville determine size, then scale
            SKSurface surface = await RenderWithSkia.ToSurfaceAsync(_melvilleDoc, _currentPage, width, height);
            SKImage? image = surface.Snapshot();

            // Convert SKImage to WPF BitmapSource
            await Dispatcher.InvokeAsync(() =>
            {
                using SKData? data = image.Encode();
                using var stream = new MemoryStream();
                data.SaveTo(stream);
                stream.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = stream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                MelvilleImage.Source = bitmapImage;
                MelvilleError.Visibility = Visibility.Collapsed;
                Log.Information("Melville.Pdf rendered page {Page}", _currentPage);
            });

            surface.Dispose();
            image.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Melville.Pdf render failed");
            await Dispatcher.InvokeAsync(() => ShowError("Melville", ex.Message));
        }
    }

    private void ShowError(string renderer, string message)
    {
        switch (renderer)
        {
            case "PDFium":
                PdfiumError.Text = $"PDFium Error:\n{message}";
                PdfiumError.Visibility = Visibility.Visible;
                PdfiumImage.Source = null;
                break;
            case "Melville":
                MelvilleError.Text = $"Melville.Pdf Error:\n{message}";
                MelvilleError.Visibility = Visibility.Visible;
                MelvilleImage.Source = null;
                break;
            case "PdfLibrary":
                // PdfLibrary renders in SkiaRenderer control, which doesn't have an error display yet
                // Could add one if needed
                break;
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
        PdfPage? page = _pdfLibraryDoc?.GetPage(_currentPage - 1);
        if (page is null) return;
        PdfRectangle mediaBox = page.GetMediaBox();
        double viewportHeight = PdfLibraryScroll.ActualHeight - 20; // Account for margins
        double zoom = viewportHeight / mediaBox.Height;
        await SetZoomAsync(zoom);
    }

    private async void FitToWidth_Click(object sender, RoutedEventArgs e)
    {
        // Calculate zoom to fit page width in the viewport
        PdfPage? page = _pdfLibraryDoc?.GetPage(_currentPage - 1);
        if (page is null) return;
        PdfRectangle mediaBox = page.GetMediaBox();
        double viewportWidth = PdfLibraryScroll.ActualWidth - 20; // Account for margins/scrollbar
        double zoom = viewportWidth / mediaBox.Width;
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
        if (_pdfLibraryDoc != null) // Only render if document is loaded
        {
            await RenderAllPanelsAsync();
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
        await RenderAllPanelsAsync();
    }

    private void UpdateZoomDisplay()
    {
        ZoomText?.Text = $"{_zoomLevel * 100:F0}";
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

    // ==================== SCROLL SYNCHRONIZATION ====================

    private void PdfLibraryScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll) return;
        _isSyncingScroll = true;

        // Sync PDFium and Melville scrollbars to match PdfLibrary
        PdfiumScroll.ScrollToVerticalOffset(e.VerticalOffset);
        PdfiumScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        MelvilleScroll.ScrollToVerticalOffset(e.VerticalOffset);
        MelvilleScroll.ScrollToHorizontalOffset(e.HorizontalOffset);

        _isSyncingScroll = false;
    }

    private void PdfiumScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll) return;
        _isSyncingScroll = true;

        PdfLibraryScroll.ScrollToVerticalOffset(e.VerticalOffset);
        PdfLibraryScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        MelvilleScroll.ScrollToVerticalOffset(e.VerticalOffset);
        MelvilleScroll.ScrollToHorizontalOffset(e.HorizontalOffset);

        _isSyncingScroll = false;
    }

    private void MelvilleScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll) return;
        _isSyncingScroll = true;

        PdfLibraryScroll.ScrollToVerticalOffset(e.VerticalOffset);
        PdfLibraryScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        PdfiumScroll.ScrollToVerticalOffset(e.VerticalOffset);
        PdfiumScroll.ScrollToHorizontalOffset(e.HorizontalOffset);

        _isSyncingScroll = false;
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

    // ==================== COMPARISON EXPORT ====================

    private async void CompareButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfLibraryDoc == null || _currentFilePath == null)
        {
            MessageBox.Show("Please load a PDF first.", "No Document",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText.Text = "Exporting comparison images...";

            string outputDir = Path.Combine(Path.GetDirectoryName(_currentFilePath)!, "Comparison");
            Directory.CreateDirectory(outputDir);

            var baseName = $"Page{_currentPage}_Zoom{_zoomLevel * 100:F0}";

            // Save all 3 renderers
            await Task.WhenAll(
                SavePdfLibraryImageAsync(Path.Combine(outputDir, $"PdfLibrary_{baseName}.png")),
                SavePdfiumImageAsync(Path.Combine(outputDir, $"PDFium_{baseName}.png")),
                SaveMelvilleImageAsync(Path.Combine(outputDir, $"Melville_{baseName}.png"))
            );

            StatusText.Text = $"Comparison images saved to {outputDir}";
            MessageBox.Show(
                $"Comparison images saved:\n\n" +
                $"• PdfLibrary_{baseName}.png\n" +
                $"• PDFium_{baseName}.png\n" +
                $"• Melville_{baseName}.png\n\n" +
                $"Location: {outputDir}",
                "Comparison Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error exporting comparison";
            MessageBox.Show($"Failed to export comparison: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Comparison export failed");
        }
    }

    private Task SavePdfLibraryImageAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                PdfPage? page = _pdfLibraryDoc?.GetPage(_currentPage - 1);
                if (page == null) return;

                PdfRectangle mediaBox = page.GetMediaBox();
                var width = (int)(mediaBox.Width * _zoomLevel);
                var height = (int)(mediaBox.Height * _zoomLevel);

                var renderTarget = new SkiaSharpRenderTarget(width, height, _pdfLibraryDoc);
                try
                {
                    renderTarget.BeginPage(_currentPage, mediaBox.Width * _zoomLevel, mediaBox.Height * _zoomLevel);
                    var optionalContentManager = new OptionalContentManager(_pdfLibraryDoc);
                    var renderer = new PdfRenderer(renderTarget, page.GetResources(),
                        optionalContentManager, _pdfLibraryDoc);
                    renderer.RenderPage(page);
                    renderTarget.EndPage();
                    renderTarget.SaveToFile(path);
                    Log.Information("Saved PdfLibrary image to {Path}", path);
                }
                finally
                {
                    renderTarget.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save PdfLibrary image");
            }
        });
    }

    private Task SavePdfiumImageAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_pdfiumDoc == null) return;

                PDFiumSharp.PdfPage? page = _pdfiumDoc.Pages[_currentPage - 1];
                var width = (int)(page.Width * _zoomLevel);
                var height = (int)(page.Height * _zoomLevel);

                using var bitmap = new PDFiumBitmap(width, height, true);
                bitmap.FillRectangle(0, 0, width, height, 0xFFFFFFFF);
                page.Render(bitmap, (0, 0, width, height));

                // Convert to PNG via SkiaSharp
                string tempPath = Path.GetTempFileName() + ".bmp";
                try
                {
                    bitmap.Save(tempPath);
                    using SKBitmap? skBitmap = SKBitmap.Decode(tempPath);
                    using SKImage? image = SKImage.FromBitmap(skBitmap);
                    using SKData? data = image.Encode(SKEncodedImageFormat.Png, 100);
                    using FileStream stream = File.OpenWrite(path);
                    data.SaveTo(stream);
                    Log.Information("Saved PDFium image to {Path}", path);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save PDFium image");
            }
        });
    }

    private async Task SaveMelvilleImageAsync(string path)
    {
        try
        {
            if (_melvilleDoc == null || _pdfLibraryDoc == null) return;

            // Get dimensions from the PdfLibrary page to match sizing
            PdfPage? pdfLibPage = _pdfLibraryDoc.GetPage(_currentPage - 1);
            if (pdfLibPage == null) return;

            PdfRectangle mediaBox = pdfLibPage.GetMediaBox();
            var width = (int)(mediaBox.Width * _zoomLevel);
            var height = (int)(mediaBox.Height * _zoomLevel);

            await using FileStream stream = File.OpenWrite(path);
            await RenderWithSkia.ToPngStreamAsync(_melvilleDoc, _currentPage, stream, width, height);
            Log.Information("Saved Melville.Pdf image to {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save Melville.Pdf image");
        }
    }
}
