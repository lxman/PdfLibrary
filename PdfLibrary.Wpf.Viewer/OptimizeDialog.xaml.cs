using System.Windows;
using PdfLibrary.Optimization;

namespace PdfLibrary.Wpf.Viewer;

/// <summary>
/// Modal options-capture dialog for PDF optimization. Holds no file or PDF logic — it maps the
/// control state to a <see cref="PdfOptimizationOptions"/> exposed via <see cref="Options"/> once the
/// user confirms. MainWindow owns the Save-As, run, and reload flow.
/// </summary>
public partial class OptimizeDialog : Window
{
    /// <summary>Populated when the dialog closes with DialogResult == true.</summary>
    public PdfOptimizationOptions Options { get; private set; } = new();

    public OptimizeDialog(long originalBytes)
    {
        InitializeComponent();
        OriginalSizeText.Text = $"Original: {FormatSize(originalBytes)}";
    }

    private void RecompressImagesCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Image tuning controls only apply when image recompression is enabled.
        bool on = RecompressImagesCheck.IsChecked == true;
        QualitySlider.IsEnabled = on;
        MaxPixelsBox.IsEnabled = on;
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValueText is not null)
            QualityValueText.Text = ((int)e.NewValue).ToString();
    }

    private void Optimize_Click(object sender, RoutedEventArgs e)
    {
        // Max-pixel dimension: blank / non-numeric / negative -> 0 (no downsampling).
        if (!int.TryParse(MaxPixelsBox.Text, out int maxPixels) || maxPixels < 0)
            maxPixels = 0;

        Options = new PdfOptimizationOptions
        {
            CompressStreams = CompressStreamsCheck.IsChecked == true,
            RemoveUnusedObjects = RemoveUnusedCheck.IsChecked == true,
            UseObjectStreams = UseObjectStreamsCheck.IsChecked == true,
            RecompressImages = RecompressImagesCheck.IsChecked == true,
            SubsetFonts = SubsetFontsCheck.IsChecked == true,
            ImageJpegQuality = (int)QualitySlider.Value,
            MaxImagePixelDimension = maxPixels,
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Human-readable byte size (shared with MainWindow's optimize-result message).</summary>
    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
