using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Optimization;
using PdfLibrary.Rendering.Wpf;
using Serilog;
using PdfDocument = PdfLibrary.Structure.PdfDocument;
using PdfPage = PdfLibrary.Document.PdfPage;

namespace PdfLibrary.Wpf.Viewer;

/// <summary>
/// The main window for the PdfLibrary Viewer
/// </summary>
public partial class MainWindow : Window
{
    // PDF points are 1/72 inch; WPF DIUs are 1/96 inch. Both are on-screen lengths,
    // so one PDF point equals 96/72 DIUs regardless of monitor DPI.
    private const double DiusPerPdfPoint = 96.0 / 72.0;

    // Document state
    private PdfDocument? _pdfDoc;
    private PdfDocumentEditor? _editor;
    private int _currentPage;
    private int _totalPages;
    private string? _currentFilePath;

    // Zoom state expressed as a fraction of physical-actual-size on screen
    // (1.0 = 1 inch of PDF renders as 1 inch on the monitor).
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
            _editor?.Dispose();
            _editor = null;
            _pdfDoc?.Dispose();

            // Load the PDF
            await Task.Run(() => LoadPdfDocument(filePath));

            // Get page count
            _totalPages = _editor?.Pages.Count ?? 0;

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
            OptimizeButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
            FlattenButton.IsEnabled = true;
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
            // Editor wraps the already-loaded document (no second file read).
            // _ownsDocument stays false so _pdfDoc.Dispose() drives lifetime.
            _editor = _pdfDoc.Edit();
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
            if (_editor == null) return;

            Dispatcher.Invoke(() =>
            {
                if (_currentPage < 1 || _currentPage > _editor.Pages.Count) return;
                PdfPage page = _editor.Pages[_currentPage - 1];

                // Translate user-facing zoom (fraction of actual size) into the pixel
                // scale the renderer expects (WPF pixels per PDF point). The DPI scale
                // factor here is what makes physical-actual-size match what's on screen.
                double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                double pixelScale = _zoomLevel * DiusPerPdfPoint * dpiScale;

                // Render the page to a WPF DrawingGroup (vector, resolution-independent).
                // RenderToDrawing must run on an STA thread — Dispatcher.Invoke satisfies that.
                DrawingGroup dg = page.RenderToDrawing(pixelScale);
                dg.Freeze();

                PageGeometry geo = page.GetGeometry(pixelScale);
                PdfView.ShowPage(dg, geo.PixelWidth, geo.PixelHeight, dpiScale);

                // Populate the overlay with form-field controls (Task 3).
                BuildOverlay(page, geo, dpiScale);

                Log.Information("PdfLibrary rendered page {Page} at {Zoom}% (pixel scale {PixelScale:F3})", _currentPage, _zoomLevel * 100, pixelScale);
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PdfLibrary render failed");
            Dispatcher.Invoke(() => StatusText.Text = $"Render error: {ex.Message}");
        }
    }

    /// <summary>
    /// Populates <see cref="WpfPageView.Overlay"/> with WPF controls for each form field
    /// widget on the current page.
    /// </summary>
    private void BuildOverlay(PdfPage page, PageGeometry geo, double dpiScale)
    {
        Canvas overlay = PdfView.Overlay;
        overlay.Children.Clear();
        if (_editor is null) return;
        int pageIndex = _currentPage - 1;

        foreach (PdfFormField field in _editor.Forms)
        foreach (PdfFieldWidget widget in field.Widgets)
        {
            if (widget.PageIndex != pageIndex) continue;   // includes -1 orphans → skipped
            ImageRect ir = geo.MapRectToImage(widget.Rect);
            FrameworkElement? control = CreateControl(field, widget);
            if (control is null) continue;
            Canvas.SetLeft(control, ir.X / dpiScale);
            Canvas.SetTop(control, ir.Y / dpiScale);
            control.Width = ir.Width / dpiScale;
            control.Height = ir.Height / dpiScale;
            overlay.Children.Add(control);
        }
    }

    private static FrameworkElement? CreateControl(PdfFormField field, PdfFieldWidget widget)
    {
        switch (field)
        {
            case PdfTextField tf:
            {
                var tb = new TextBox
                {
                    Text = tf.Value ?? "",
                    AcceptsReturn = tf.IsMultiline,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    BorderThickness = new Thickness(1),
                    IsReadOnly = tf.IsReadOnly        // display value but block edits
                };
                if (tf.MaxLength is { } ml && ml > 0) tb.MaxLength = ml;
                if (!tf.IsReadOnly)
                    tb.LostFocus += (_, _) => tf.Value = tb.Text;
                return tb;
            }
            case PdfButtonField bf when bf.Kind == ButtonKind.Checkbox:
            {
                var cb = new CheckBox
                {
                    IsChecked = bf.IsChecked,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsEnabled = !bf.IsReadOnly        // display state but block interaction
                };
                if (!bf.IsReadOnly)
                {
                    cb.Checked   += (_, _) => bf.Check();
                    cb.Unchecked += (_, _) => bf.Uncheck();
                }
                return cb;
            }
            case PdfButtonField bf when bf.Kind == ButtonKind.Radio:
            {
                var rb = new RadioButton
                {
                    GroupName = field.FullName,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsChecked = widget.OnStateName != null && widget.OnStateName == bf.SelectedOption,
                    IsEnabled = !bf.IsReadOnly        // display selection but block interaction
                };
                string? on = widget.OnStateName;
                if (!bf.IsReadOnly)
                    rb.Checked += (_, _) => { if (on != null) bf.SelectedOption = on; };
                return rb;
            }
            case PdfChoiceField cf when cf.IsCombo:
            {
                var combo = new ComboBox { IsEnabled = !cf.IsReadOnly };
                foreach ((string export, string display) in cf.Options)
                    combo.Items.Add(new ComboBoxItem { Content = display, Tag = export });
                string? sel = cf.SelectedValues.Count > 0 ? cf.SelectedValues[0] : null;
                if (sel != null) combo.SelectedIndex = IndexOfExport(cf, sel);
                if (!cf.IsReadOnly)
                {
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string ex)
                            cf.SelectedValues = new[] { ex };
                    };
                }
                return combo;
            }
            case PdfChoiceField cf:   // list box
            {
                var lb = new ListBox
                {
                    SelectionMode = cf.IsMultiSelect ? SelectionMode.Multiple : SelectionMode.Single,
                    IsEnabled = !cf.IsReadOnly        // display selection but block interaction
                };
                foreach ((string export, string display) in cf.Options)
                    lb.Items.Add(new ListBoxItem { Content = display, Tag = export });
                // Preselect
                foreach (string sv in cf.SelectedValues)
                {
                    int idx = IndexOfExport(cf, sv);
                    if (idx >= 0) lb.SelectedItems.Add(lb.Items[idx]);
                }
                if (!cf.IsReadOnly)
                {
                    lb.SelectionChanged += (_, _) =>
                        cf.SelectedValues = lb.SelectedItems.Cast<ListBoxItem>()
                            .Select(i => (string)i.Tag).ToArray();
                }
                return lb;
            }
            case PdfSignatureField:
                return new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                    Child = new TextBlock
                    {
                        Text = "Signature",
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
            default:
                return null;   // push buttons, unknown
        }
    }

    private static int IndexOfExport(PdfChoiceField cf, string export)
    {
        for (int i = 0; i < cf.Options.Count; i++)
            if (cf.Options[i].Export == export) return i;
        return -1;
    }

    // ==================== SAVE / FLATTEN ====================

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = "filled.pdf",
            Title = "Save Edited PDF"
        };
        if (dlg.ShowDialog() != true) return;
        _editor.Save(dlg.FileName);
        StatusText.Text = $"Saved to {dlg.FileName}";
        Log.Information("Saved edited PDF to {Path}", dlg.FileName);
    }

    private void FlattenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        _editor.Forms.Flatten();
        // Re-render: fields are now baked into the page content; overlay rebuilds empty.
        RenderPage();
        StatusText.Text = "Fields flattened and re-rendered.";
        Log.Information("Flattened form fields on page {Page}", _currentPage);
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
        // Fit the entire page in the viewport (limiting dimension wins).
        PdfPage? page = _editor?.Pages[_currentPage - 1];
        if (page is null) return;
        double viewportWidth = PdfScroll.ActualWidth - 60;
        double viewportHeight = PdfScroll.ActualHeight - 50;
        double pageWidthDiu = page.Width * DiusPerPdfPoint;
        double pageHeightDiu = page.Height * DiusPerPdfPoint;
        double zoom = Math.Min(viewportWidth / pageWidthDiu, viewportHeight / pageHeightDiu);
        await SetZoomAsync(zoom);
    }

    private async void FitToWidth_Click(object sender, RoutedEventArgs e)
    {
        // Fit the page width to the viewport. _zoomLevel is now a fraction of
        // actual-screen-size, so we compare DIUs to DIUs and the render path
        // handles the DPI conversion.
        PdfPage? page = _editor?.Pages[_currentPage - 1];
        if (page is null) return;
        double viewportWidth = PdfScroll.ActualWidth - 60;
        double pageWidthDiu = page.Width * DiusPerPdfPoint;
        double zoom = viewportWidth / pageWidthDiu;
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
        // RenderToDrawing and RenderTargetBitmap both require STA — marshal to the UI dispatcher.
        RenderTargetBitmap? rtb = null;
        Dispatcher.Invoke(() =>
        {
            if (_editor == null) return;
            PdfPage page = _editor.Pages[_currentPage - 1];
            // Preserve the same pixel scale as the old code: zoom without DPI correction.
            rtb = RenderPageBitmap(page, _zoomLevel);
        });
        if (rtb == null) return;

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using FileStream fs = File.Create(path);
        enc.Save(fs);
        Log.Information("Saved image to {Path}", path);
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
            var description = $"PdfLibrary - Page {_currentPage}";
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
        if (_editor == null)
            throw new InvalidOperationException("No document loaded");

        PdfPage page = _editor.Pages[_currentPage - 1];

        // Calculate scale to fit the page in the printable area while maintaining aspect ratio.
        // DrawingGroup is resolution-independent so this renders correctly at print target size.
        double scaleX = targetWidth / page.Width;
        double scaleY = targetHeight / page.Height;
        double pixelScale = Math.Min(scaleX, scaleY);

        // Already on the UI (STA) thread — RenderPageBitmap is safe to call directly here.
        return RenderPageBitmap(page, pixelScale);
    }

    private static RenderTargetBitmap RenderPageBitmap(PdfPage page, double pixelScale)
    {
        DrawingGroup dg = page.RenderToDrawing(pixelScale);
        PageGeometry geo = page.GetGeometry(pixelScale);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
            dc.DrawDrawing(dg);
        var rtb = new RenderTargetBitmap(geo.PixelWidth, geo.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    // ==================== OPTIMIZE ====================

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfDoc == null || _currentFilePath == null)
        {
            MessageBox.Show("Please load a PDF first.", "No Document",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string sourcePath = _currentFilePath;
        long originalBytes = new FileInfo(sourcePath).Length;

        var optionsDialog = new OptimizeDialog(originalBytes) { Owner = this };
        if (optionsDialog.ShowDialog() != true) return;

        var saveDialog = new SaveFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf",
            Title = "Save Optimized PDF",
            FileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}.optimized.pdf"
        };
        if (saveDialog.ShowDialog() != true) return;

        string outputPath = saveDialog.FileName;
        // Never optimize over the file that's currently open — it would fight the loaded document.
        if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Choose an output file different from the source.", "Optimize",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText.Text = "Optimizing...";
            await Task.Run(() => OptimizeToFile(sourcePath, outputPath, optionsDialog.Options));

            long optimizedBytes = new FileInfo(outputPath).Length;
            string result = FormatOptimizeResult(originalBytes, optimizedBytes);

            // Per design: open the optimized result, then surface the size delta. LoadPdfAsync
            // overwrites StatusText, so set the result text afterwards.
            await LoadPdfAsync(outputPath);
            StatusText.Text = result;
            Log.Information("Optimized {Src} -> {Out}: {Result}", sourcePath, outputPath, result);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Optimize failed";
            MessageBox.Show($"Failed to optimize: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Log.Error(ex, "Optimize failed");
        }
    }

    private static void OptimizeToFile(string sourcePath, string outputPath, PdfOptimizationOptions options)
    {
        // Fresh load: Optimize mutates the model, and the displayed _pdfDoc came from a closed stream.
        // The viewer already opened this file (empty password), so a fresh Load by path succeeds too.
        using PdfDocument doc = PdfDocument.Load(sourcePath);
        using FileStream outStream = File.Create(outputPath);
        PdfOptimizer.Optimize(doc, outStream, options);
    }

    private static string FormatOptimizeResult(long original, long optimized)
    {
        double pct = original > 0 ? (1.0 - (double)optimized / original) * 100.0 : 0;
        string sign = pct >= 0 ? "−" : "+"; // shrink shows −, growth shows +
        return $"Optimized {OptimizeDialog.FormatSize(original)} → {OptimizeDialog.FormatSize(optimized)} ({sign}{Math.Abs(pct):F0}%)";
    }
}
