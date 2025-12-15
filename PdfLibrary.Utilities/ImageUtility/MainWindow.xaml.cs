using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ImageUtility;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private BitmapSource? _currentImage;
    private string? _currentFilePath;
    private double _zoomFactor = 1.0;
    private const double ZoomIncrement = 0.1;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 10.0;

    public MainWindow()
    {
        InitializeComponent();
        UpdateUI();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Image File",
            Filter = "All Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.tif|" +
                     "JPEG Files|*.jpg;*.jpeg|" +
                     "PNG Files|*.png|" +
                     "BMP Files|*.bmp|" +
                     "GIF Files|*.gif|" +
                     "TIFF Files|*.tiff;*.tif|" +
                     "All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadImage(dialog.FileName);
        }
    }

    private void LoadImage(string filePath)
    {
        try
        {
            StatusText.Text = "Loading image...";

            // Load image using WPF's built-in decoder
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze(); // Freeze for better performance

            _currentImage = bitmap;
            _currentFilePath = filePath;
            _zoomFactor = 1.0;

            ImageDisplay.Source = _currentImage;
            ApplyZoom();
            UpdateUI();

            StatusText.Text = $"Loaded: {Path.GetFileName(filePath)}";
            ImageInfoText.Text = $"{_currentImage.PixelWidth} × {_currentImage.PixelHeight} px | " +
                                $"{_currentImage.Format} | " +
                                $"{_currentImage.DpiX:F0} DPI";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load image:\n{ex.Message}", "Error",
                          MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to load image";
        }
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save Image As",
            Filter = "PNG Files|*.png|" +
                     "JPEG Files|*.jpg;*.jpeg|" +
                     "BMP Files|*.bmp|" +
                     "TIFF Files|*.tiff;*.tif|" +
                     "GIF Files|*.gif",
            DefaultExt = ".png"
        };

        if (_currentFilePath != null)
        {
            dialog.FileName = Path.GetFileNameWithoutExtension(_currentFilePath);
        }

        if (dialog.ShowDialog() == true)
        {
            SaveImage(dialog.FileName);
        }
    }

    private void SaveImage(string filePath)
    {
        if (_currentImage == null)
        {
            return;
        }

        try
        {
            StatusText.Text = "Saving image...";

            BitmapEncoder encoder = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".png" => new PngBitmapEncoder(),
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".bmp" => new BmpBitmapEncoder(),
                ".tif" or ".tiff" => new TiffBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };

            encoder.Frames.Add(BitmapFrame.Create(_currentImage));

            using var stream = new FileStream(filePath, FileMode.Create);
            encoder.Save(stream);

            StatusText.Text = $"Saved: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save image:\n{ex.Message}", "Error",
                          MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to save image";
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;

        _zoomFactor = Math.Min(_zoomFactor + ZoomIncrement, MaxZoom);
        ApplyZoom();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;

        _zoomFactor = Math.Max(_zoomFactor - ZoomIncrement, MinZoom);
        ApplyZoom();
    }

    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;

        _zoomFactor = 1.0;
        ApplyZoom();
    }

    private void FitToWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;

        double viewportWidth = ImageScrollViewer.ViewportWidth;
        double viewportHeight = ImageScrollViewer.ViewportHeight;

        double scaleX = viewportWidth / _currentImage.PixelWidth;
        double scaleY = viewportHeight / _currentImage.PixelHeight;

        _zoomFactor = Math.Min(scaleX, scaleY);
        _zoomFactor = Math.Max(MinZoom, Math.Min(_zoomFactor, MaxZoom));

        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (_currentImage == null) return;

        var transform = new ScaleTransform(_zoomFactor, _zoomFactor);
        ImageDisplay.LayoutTransform = transform;

        ZoomText.Text = $"{_zoomFactor * 100:F0}%";
    }

    private void UpdateUI()
    {
        bool hasImage = _currentImage != null;
        SaveAsMenuItem.IsEnabled = hasImage;

        if (!hasImage)
        {
            ImageInfoText.Text = "";
            ZoomText.Text = "100%";
        }
    }

    private void CodecSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new CodecSettingsWindow
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            // Settings were saved
            StatusText.Text = "Codec preferences updated";
        }
    }
}