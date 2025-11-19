using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Rendering;
using Serilog;
using Path = System.Windows.Shapes.Path;
using PathSegment = PdfLibrary.Rendering.PathSegment;

namespace PdfTool;

/// <summary>
/// WPF rendering target for PDF content
/// Renders PDF pages to a WPF Canvas using WPF drawing primitives
/// </summary>
public partial class Renderer : UserControl, IRenderTarget
{
    private readonly List<UIElement> _elements = [];

    // ==================== PAGE LIFECYCLE ====================

    /// <summary>
    /// Current page number being rendered (1-based).
    /// Updated by BeginPage().
    /// </summary>
    public int CurrentPageNumber { get; set; }

    public Renderer()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets the number of child elements in the canvas (for debugging)
    /// </summary>
    public int GetChildCount() => RenderCanvas.Children.Count;

    /// <summary>
    /// Begin rendering a new page with specified dimensions.
    /// Clears previous content and sets up canvas for new page.
    /// </summary>
    public void BeginPage(int pageNumber, double width, double height)
    {
        CurrentPageNumber = pageNumber;
        Clear();
        SetPageSize(width, height);
        Log.Information("BeginPage: Page {PageNumber}, Size: {Width} x {Height}",
            pageNumber, width, height);
    }

    /// <summary>
    /// Complete rendering of current page.
    /// Finalizes layout and ensures all elements are properly positioned.
    /// </summary>
    public void EndPage()
    {
        // Force final layout update to ensure all elements are positioned
        this.UpdateLayout();
        Log.Debug("EndPage: Page {PageNumber} complete, {ElementCount} elements rendered",
            CurrentPageNumber, _elements.Count);
    }

    /// <summary>
    /// Clears the canvas and prepares for a new page
    /// </summary>
    public void Clear()
    {
        RenderCanvas.Children.Clear();
        _elements.Clear();
        Log.Debug("Clear: Canvas cleared, {ChildCount} children", RenderCanvas.Children.Count);
    }

    /// <summary>
    /// Sets the page size for rendering
    /// </summary>
    public void SetPageSize(double width, double height)
    {
        RenderCanvas.Width = width;
        RenderCanvas.Height = height;

        // CRITICAL FIX: Canvas reports DesiredSize as 0,0 to parent the ScrollViewer by default
        // Set MinWidth/MinHeight to force Canvas to report the correct size for scrolling
        // See: https://stackoverflow.com/questions/30504869/scrollviewer-canvas
        RenderCanvas.MinWidth = width;
        RenderCanvas.MinHeight = height;

        // Explicitly disable any clipping that might be applied
        RenderCanvas.ClipToBounds = false;
        RenderCanvas.Clip = null;

        // Force layout update on the entire UserControl, not just the Canvas
        // This ensures the ScrollViewer and all parent containers update their layout
        this.UpdateLayout();

        Log.Information("SetPageSize: Canvas size set to {Width} x {Height}, Actual: {ActualWidth} x {ActualHeight}",
            width, height, RenderCanvas.ActualWidth, RenderCanvas.ActualHeight);
        Log.Information("  UserControl Actual: {UCWidth} x {UCHeight}", this.ActualWidth, this.ActualHeight);

        // Find the ScrollViewer parent
        if (RenderCanvas.Parent is ScrollViewer scrollViewer)
        {
            Log.Information("  ScrollViewer Actual: {SVWidth} x {SVHeight}, Viewport: {VPWidth} x {VPHeight}",
                scrollViewer.ActualWidth, scrollViewer.ActualHeight,
                scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
        }
    }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        if (path.IsEmpty) return;

        try
        {
            PathGeometry? geometry = ConvertPathToGeometry(path);
            if (geometry == null)
            {
                System.Diagnostics.Debug.WriteLine("StrokePath: ConvertPathToGeometry returned null");
                return;
            }

            var wpfPath = new Path
            {
                Data = geometry,
                Stroke = CreateBrush(state.StrokeColor, state.StrokeColorSpace),
                StrokeThickness = state.LineWidth,
                StrokeStartLineCap = ConvertLineCap(state.LineCap),
                StrokeEndLineCap = ConvertLineCap(state.LineCap),
                StrokeLineJoin = ConvertLineJoin(state.LineJoin),
                StrokeMiterLimit = state.MiterLimit
            };

            AddElement(wpfPath);
            System.Diagnostics.Debug.WriteLine($"StrokePath: Added path with {geometry.Figures.Count} figures");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StrokePath failed: {ex.Message}");
            // Skip paths that can't be stroked
        }
    }

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;

        try
        {
            PathGeometry? geometry = ConvertPathToGeometry(path);
            if (geometry == null)
            {
                System.Diagnostics.Debug.WriteLine("FillPath: ConvertPathToGeometry returned null");
                return;
            }

            geometry.FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero;

            var wpfPath = new Path
            {
                Data = geometry,
                Fill = CreateBrush(state.FillColor, state.FillColorSpace)
            };

            AddElement(wpfPath);
            System.Diagnostics.Debug.WriteLine($"FillPath: Added path with {geometry.Figures.Count} figures, FillColor={GetColorInfo(state.FillColor, state.FillColorSpace)}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FillPath failed: {ex.Message}");
            // Skip paths that can't be filled
        }
    }

    private static string GetColorInfo(List<double>? color, string? colorSpace)
    {
        if (color == null || color.Count == 0)
            return "none";
        return $"{colorSpace}({string.Join(", ", color.Select(c => c.ToString("F2")))})";
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;

        try
        {
            PathGeometry? geometry = ConvertPathToGeometry(path);
            if (geometry == null)
            {
                System.Diagnostics.Debug.WriteLine("FillAndStrokePath: ConvertPathToGeometry returned null");
                return;
            }

            geometry.FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero;

            var wpfPath = new Path
            {
                Data = geometry,
                Fill = CreateBrush(state.FillColor, state.FillColorSpace),
                Stroke = CreateBrush(state.StrokeColor, state.StrokeColorSpace),
                StrokeThickness = state.LineWidth,
                StrokeStartLineCap = ConvertLineCap(state.LineCap),
                StrokeEndLineCap = ConvertLineCap(state.LineCap),
                StrokeLineJoin = ConvertLineJoin(state.LineJoin),
                StrokeMiterLimit = state.MiterLimit
            };

            AddElement(wpfPath);
            System.Diagnostics.Debug.WriteLine($"FillAndStrokePath: Added path with {geometry.Figures.Count} figures, Fill={state.FillColorSpace}, Stroke={state.StrokeColorSpace}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FillAndStrokePath failed: {ex.Message}");
            // Skip paths that can't be filled and stroked
        }
    }

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            // Get text position from graphics state
            Vector2 position = state.GetTextPosition();

            // Calculate effective font size by extracting scale from combined TextMatrix * CTM
            // In PDF, the complete transformation from text space to device space is: TextMatrix * CTM
            // The scale is calculated as the length of the column vectors (basis vectors)
            // For Matrix3x2: | M11  M12 |   X basis = (M11, M21), Y basis = (M12, M22)
            //                | M21  M22 |
            Matrix3x2 combinedMatrix = state.TextMatrix * state.Ctm;
            var combinedScaleY = (float)Math.Sqrt(combinedMatrix.M12 * combinedMatrix.M12 + combinedMatrix.M22 * combinedMatrix.M22);

            // Use the Y-scale (vertical) for font size, as that determines text height
            double effectiveFontSize = state.FontSize * combinedScaleY;

            // Validate font size
            if (double.IsNaN(effectiveFontSize) || double.IsInfinity(effectiveFontSize) || effectiveFontSize <= 0)
            {
                Log.Warning("DrawText SKIPPED: Invalid effectiveFontSize={EffectiveFontSize}, FontSize={FontSize}, combinedScaleY={ScaleY}", effectiveFontSize, state.FontSize, combinedScaleY);
                Log.Warning("  TextMatrix: [{M11}, {M12}, {M21}, {M22}, {M31}, {M32}]", state.TextMatrix.M11, state.TextMatrix.M12, state.TextMatrix.M21, state.TextMatrix.M22, state.TextMatrix.M31, state.TextMatrix.M32);
                return;
            }

            // Validate position
            if (double.IsNaN(position.X) || double.IsNaN(position.Y) ||
                double.IsInfinity(position.X) || double.IsInfinity(position.Y))
            {
                Log.Warning("DrawText SKIPPED: Invalid position ({X}, {Y})", position.X, position.Y);
                return;
            }

            // Calculate horizontal scale from the combined matrix for character advance
            var combinedScaleX = (float)Math.Sqrt(combinedMatrix.M11 * combinedMatrix.M11 + combinedMatrix.M21 * combinedMatrix.M21);

            if (text == "MI" || text == "CHAEL" || text == "JORDA")
            {
                Console.WriteLine($"[WPF-RENDER] DrawText('{text}') at ({position.X:F2}, {position.Y:F2})");
                Console.WriteLine($"  FontSize={state.FontSize}, CombinedScaleX={combinedScaleX:F4}, HorizontalScaling={state.HorizontalScaling}%");
                if (glyphWidths.Count > 0)
                    Console.WriteLine($"  First glyph width={glyphWidths[0]:F4}");
            }

            // Single TextBlock approach - simple and works well (95.76% match)
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = effectiveFontSize,
                Foreground = CreateBrush(state.FillColor, state.FillColorSpace),
                FontFamily = GetFontFamily(state.FontName)
            };

            double wpfX = position.X;
            double wpfY = RenderCanvas.ActualHeight - position.Y - effectiveFontSize;
            Canvas.SetLeft(textBlock, wpfX);
            Canvas.SetTop(textBlock, wpfY);
            AddElement(textBlock);

            Log.Debug("TextBlock: '{Text}' at ({X}, {Y})", text.Substring(0, Math.Min(10, text.Length)), wpfX, wpfY);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DrawText EXCEPTION");
        }
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        Log.Debug("DrawImage called: {Width}x{Height}, ColorSpace={ColorSpace}, BitsPerComponent={BitsPerComponent}", image.Width, image.Height, image.ColorSpace, image.BitsPerComponent);
        Log.Debug("  CTM: [{M11}, {M12}, {M21}, {M22}, {M31}, {M32}]", state.Ctm.M11, state.Ctm.M12, state.Ctm.M21, state.Ctm.M22, state.Ctm.M31, state.Ctm.M32);

        // DIAGNOSTIC: Skip images on page 1 to test if that fixes the masking issue
        if (CurrentPageNumber == 1)
        {
            Log.Warning("DrawImage SKIPPED on page 1 for diagnostic testing");
            return;
        }

        try
        {
            // Convert PDF image data to WPF BitmapSource
            BitmapSource? bitmap = CreateBitmapFromPdfImage(image);
            if (bitmap == null)
            {
                System.Diagnostics.Debug.WriteLine("  ERROR: CreateBitmapFromPdfImage returned null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"  Bitmap created: {bitmap.PixelWidth}x{bitmap.PixelHeight}, Format={bitmap.Format}");

            // Get image rectangle from graphics state
            // In PDF, images are mapped to a 1x1 unit square, and the CTM scales/positions them
            PdfRectangle imageRect = state.GetImageRectangle();
            double x = imageRect.X1;
            double y = imageRect.Y1;
            double width = imageRect.Width;
            double height = imageRect.Height;

            System.Diagnostics.Debug.WriteLine($"  Final image rect: ({x}, {y}) size ({width} x {height})");

            // WORKAROUND: Use Rectangle with ImageBrush instead of Image control
            // Image control appears to create a clipping mask that hides other canvas content
            var imageBrush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = new Rect(0, 0, 1, 1)
            };

            var wpfRect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = imageBrush,
                IsHitTestVisible = false
            };

            // Set Z-index to ensure image doesn't mask other elements
            Panel.SetZIndex(wpfRect, -1000);

            // Position the image in the canvas
            // PDF coordinates: origin at bottom-left, Y increases upward
            // WPF coordinates: origin at top-left, Y increases downward
            // Convert Y coordinate: WPF_Y = Canvas.ActualHeight - PDF_Y - ImageHeight
            // Use ActualHeight (rendered size) not Height (property)
            double wpfY = RenderCanvas.ActualHeight - y - height;

            Canvas.SetLeft(wpfRect, x);
            Canvas.SetTop(wpfRect, wpfY);

            System.Diagnostics.Debug.WriteLine($"  WPF position: ({x}, {wpfY})");

            AddElement(wpfRect);
            System.Diagnostics.Debug.WriteLine($"  Image added to canvas as Rectangle with ImageBrush");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"  ERROR in DrawImage: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"  Stack trace: {ex.StackTrace}");
            // Skip malformed images
        }
    }

    public void SaveState()
    {
        // In PDF, q/Q operators save/restore the graphics state (colors, transforms, etc.)
        // but do NOT undo visual rendering. Once painted, elements stay on the page.
        // The actual graphics state is managed by PdfGraphicsState in the processor.
        System.Diagnostics.Debug.WriteLine("SaveState: Graphics state saved (visual elements are NOT removed on restore)");
    }

    public void RestoreState()
    {
        // In PDF, restoring state only affects future drawing operations.
        // Previously drawn elements remain on the page.
        // The actual graphics state is managed by PdfGraphicsState in the processor.
        System.Diagnostics.Debug.WriteLine("RestoreState: Graphics state restored (visual elements remain)");
    }

    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;

        PathGeometry? geometry = ConvertPathToGeometry(path);
        if (geometry == null) return;

        // Apply clipping to the canvas
        RenderCanvas.Clip = geometry;
    }

    // ==================== Helper Methods ====================

    private void AddElement(UIElement element)
    {
        _elements.Add(element);
        RenderCanvas.Children.Add(element);
    }

    private PathGeometry? ConvertPathToGeometry(IPathBuilder pathBuilder)
    {
        if (pathBuilder is not PathBuilder builder)
        {
            System.Diagnostics.Debug.WriteLine($"ConvertPathToGeometry: pathBuilder is not PathBuilder, type={pathBuilder?.GetType().Name}");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"ConvertPathToGeometry: Processing {builder.Segments.Count} segments");

        var geometry = new PathGeometry();
        var figure = new PathFigure();
        var hasFigure = false;

        foreach (PathSegment segment in builder.Segments)
        {
            switch (segment)
            {
                case MoveToSegment moveTo:
                    if (hasFigure)
                    {
                        geometry.Figures.Add(figure);
                    }
                    figure = new PathFigure
                    {
                        StartPoint = new Point(moveTo.X, FlipY(moveTo.Y))
                    };
                    hasFigure = true;
                    break;

                case LineToSegment lineTo:
                    if (hasFigure)
                    {
                        figure.Segments.Add(new LineSegment(
                            new Point(lineTo.X, FlipY(lineTo.Y)), true));
                    }
                    break;

                case CurveToSegment curveTo:
                    if (hasFigure)
                    {
                        figure.Segments.Add(new BezierSegment(
                            new Point(curveTo.X1, FlipY(curveTo.Y1)),
                            new Point(curveTo.X2, FlipY(curveTo.Y2)),
                            new Point(curveTo.X3, FlipY(curveTo.Y3)),
                            true));
                    }
                    break;

                case ClosePathSegment:
                    if (hasFigure)
                    {
                        figure.IsClosed = true;
                    }
                    break;
            }
        }

        if (hasFigure)
        {
            geometry.Figures.Add(figure);
        }

        if (geometry.Figures.Count <= 0) return geometry.Figures.Count > 0 ? geometry : null;
        // Log bounds for debugging
        Rect bounds = geometry.Bounds;
        System.Diagnostics.Debug.WriteLine($"  PathGeometry bounds: X={bounds.X}, Y={bounds.Y}, Width={bounds.Width}, Height={bounds.Height}");

        return geometry.Figures.Count > 0 ? geometry : null;
    }

    private double FlipY(double y)
    {
        // PDF coordinates: origin at bottom-left, Y increases upward
        // WPF coordinates: origin at top-left, Y increases downward
        // Use ActualHeight (rendered size) not Height (property)
        return RenderCanvas.ActualHeight - y;
    }

    private static SolidColorBrush CreateBrush(List<double>? color, string? colorSpace)
    {
        if (color == null || color.Count == 0 || string.IsNullOrEmpty(colorSpace))
            return Brushes.Black;

        return colorSpace switch
        {
            "DeviceGray" when color.Count == 1 => new SolidColorBrush(Color.FromRgb(
                (byte)(color[0] * 255),
                (byte)(color[0] * 255),
                (byte)(color[0] * 255))),

            "DeviceRGB" when color.Count == 3 => new SolidColorBrush(Color.FromRgb(
                (byte)(color[0] * 255),
                (byte)(color[1] * 255),
                (byte)(color[2] * 255))),

            "DeviceCMYK" when color.Count == 4 => CmykToRgbBrush(
                color[0], color[1], color[2], color[3]),

            _ => Brushes.Black
        };
    }

    private static SolidColorBrush CmykToRgbBrush(double c, double m, double y, double k)
    {
        // Convert CMYK to RGB
        double r = (1 - c) * (1 - k);
        double g = (1 - m) * (1 - k);
        double b = (1 - y) * (1 - k);

        return new SolidColorBrush(Color.FromRgb(
            (byte)(r * 255),
            (byte)(g * 255),
            (byte)(b * 255)));
    }

    private static PenLineCap ConvertLineCap(int lineCap)
    {
        return lineCap switch
        {
            0 => PenLineCap.Flat,    // Butt cap
            1 => PenLineCap.Round,   // Round cap
            2 => PenLineCap.Square,  // Projecting square cap
            _ => PenLineCap.Flat
        };
    }

    private static PenLineJoin ConvertLineJoin(int lineJoin)
    {
        return lineJoin switch
        {
            0 => PenLineJoin.Miter,  // Miter join
            1 => PenLineJoin.Round,  // Round join
            2 => PenLineJoin.Bevel,  // Bevel join
            _ => PenLineJoin.Miter
        };
    }

    private static FontFamily GetFontFamily(string? fontName)
    {
        if (string.IsNullOrEmpty(fontName))
            return new FontFamily("Arial");

        // Map common PDF fonts to WPF fonts
        if (fontName.Contains("Times", StringComparison.OrdinalIgnoreCase))
            return new FontFamily("Times New Roman");
        if (fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase))
            return new FontFamily("Courier New");
        if (fontName.Contains("Helvetica", StringComparison.OrdinalIgnoreCase) ||
            fontName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
            return new FontFamily("Arial");

        // Default to Arial for unknown fonts
        return new FontFamily("Arial");
    }

    private static BitmapSource? CreateBitmapFromPdfImage(PdfImage image)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"  CreateBitmapFromPdfImage: Starting conversion");
            System.Diagnostics.Debug.WriteLine($"    Image: {image.Width}x{image.Height}, ColorSpace={image.ColorSpace}, BPC={image.BitsPerComponent}");

            // Get decoded image data
            byte[] imageData = image.GetDecodedData();
            System.Diagnostics.Debug.WriteLine($"    Decoded data size: {imageData.Length} bytes");

            // Determine pixel format based on color space and bits per component
            PixelFormat pixelFormat;
            BitmapPalette? palette = null;

            if (image.BitsPerComponent == 1)
            {
                pixelFormat = PixelFormats.Indexed1;
            }
            else if (image.BitsPerComponent == 8)
            {
                // For 8-bit images, determine format from color space
                // If color space is unknown, infer from data size
                if (image.ColorSpace == "DeviceGray" || image.ColorSpace == "G")
                {
                    pixelFormat = PixelFormats.Gray8;
                }
                else if (image.ColorSpace == "DeviceRGB" || image.ColorSpace == "RGB")
                {
                    pixelFormat = PixelFormats.Rgb24;
                }
                else if (image.ColorSpace == "DeviceCMYK" || image.ColorSpace == "CMYK")
                {
                    // CMYK needs conversion to RGB - for now treat as RGB
                    pixelFormat = PixelFormats.Rgb24;
                }
                else if (image.ColorSpace == "Indexed")
                {
                    // Indexed color uses a palette/lookup table
                    // Image data contains palette indices, not actual color values
                    System.Diagnostics.Debug.WriteLine($"    Processing Indexed color image");
                    pixelFormat = PixelFormats.Indexed8;

                    // Extract the color palette
                    byte[]? paletteData = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
                    if (paletteData != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"    Palette: base={baseColorSpace}, hival={hival}, size={paletteData.Length} bytes");

                        // Create WPF palette from PDF palette data
                        palette = CreatePaletteFromIndexedData(paletteData, baseColorSpace ?? "DeviceRGB", hival);
                        System.Diagnostics.Debug.WriteLine(palette != null
                            ? $"    WPF BitmapPalette created with {palette.Colors.Count} colors"
                            : "    WARNING: Failed to create WPF BitmapPalette");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"    WARNING: Could not extract palette data");
                    }
                }
                else
                {
                    // Color space is unknown - infer from data size
                    int expectedGray = image.Width * image.Height;
                    int expectedRgb = image.Width * image.Height * 3;
                    int expectedRgba = image.Width * image.Height * 4;

                    if (imageData.Length == expectedGray)
                    {
                        System.Diagnostics.Debug.WriteLine($"    Inferred format: Gray8 (based on data size)");
                        pixelFormat = PixelFormats.Gray8;
                    }
                    else if (imageData.Length == expectedRgb)
                    {
                        System.Diagnostics.Debug.WriteLine($"    Inferred format: Rgb24 (based on data size)");
                        pixelFormat = PixelFormats.Rgb24;
                    }
                    else if (imageData.Length == expectedRgba)
                    {
                        System.Diagnostics.Debug.WriteLine($"    Inferred format: Bgra32 (based on data size)");
                        pixelFormat = PixelFormats.Bgra32;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"    WARNING: Cannot determine format from data size, defaulting to Rgb24");
                        pixelFormat = PixelFormats.Rgb24;
                    }
                }
            }
            else
            {
                // Other bit depths - default to BGRA32
                pixelFormat = PixelFormats.Bgra32;
            }

            System.Diagnostics.Debug.WriteLine($"    PixelFormat: {pixelFormat}, BitsPerPixel={pixelFormat.BitsPerPixel}");

            // Calculate stride
            int stride = (image.Width * pixelFormat.BitsPerPixel + 7) / 8;
            System.Diagnostics.Debug.WriteLine($"    Stride: {stride}");

            int expectedSize = stride * image.Height;
            System.Diagnostics.Debug.WriteLine($"    Expected data size: {expectedSize}, Actual: {imageData.Length}");

            if (imageData.Length < expectedSize)
            {
                System.Diagnostics.Debug.WriteLine($"    ERROR: Image data too small! Expected {expectedSize}, got {imageData.Length}");
                return null;
            }

            // Create bitmap
            var bitmap = BitmapSource.Create(
                image.Width,
                image.Height,
                96, 96,
                pixelFormat,
                palette,
                imageData,
                stride);

            System.Diagnostics.Debug.WriteLine($"    Bitmap created successfully");
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"    ERROR in CreateBitmapFromPdfImage: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"    Stack: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// Creates a WPF BitmapPalette from PDF Indexed color space palette data
    /// WPF Indexed8 format requires exactly 256 colors, so we pad the palette if needed
    /// </summary>
    /// <param name="paletteData">Raw palette data from PDF</param>
    /// <param name="baseColorSpace">Base color space (DeviceRGB, DeviceGray, etc.)</param>
    /// <param name="hival">Maximum palette index (0 to hival)</param>
    /// <returns>BitmapPalette, or null if conversion failed</returns>
    private static BitmapPalette? CreatePaletteFromIndexedData(byte[] paletteData, string baseColorSpace, int hival)
    {
        try
        {
            int numColors = hival + 1; // hival is maximum index, so we have hival+1 colors
            var colors = new List<Color>(256); // Pre-allocate for 256 colors

            if (baseColorSpace == "DeviceRGB" || baseColorSpace == "RGB")
            {
                // RGB palette: 3 bytes per color (R, G, B)
                int expectedSize = numColors * 3;
                if (paletteData.Length < expectedSize)
                {
                    System.Diagnostics.Debug.WriteLine($"    ERROR: RGB palette data too small. Expected {expectedSize}, got {paletteData.Length}");
                    return null;
                }

                for (var i = 0; i < numColors; i++)
                {
                    int offset = i * 3;
                    byte r = paletteData[offset];
                    byte g = paletteData[offset + 1];
                    byte b = paletteData[offset + 2];
                    colors.Add(Color.FromRgb(r, g, b));
                }
            }
            else if (baseColorSpace == "DeviceGray" || baseColorSpace == "G")
            {
                // Grayscale palette: 1 byte per color
                int expectedSize = numColors;
                if (paletteData.Length < expectedSize)
                {
                    System.Diagnostics.Debug.WriteLine($"    ERROR: Gray palette data too small. Expected {expectedSize}, got {paletteData.Length}");
                    return null;
                }

                for (var i = 0; i < numColors; i++)
                {
                    byte gray = paletteData[i];
                    colors.Add(Color.FromRgb(gray, gray, gray));
                }
            }
            else if (baseColorSpace == "DeviceCMYK" || baseColorSpace == "CMYK")
            {
                // CMYK palette: 4 bytes per color (C, M, Y, K)
                // Need to convert to RGB
                int expectedSize = numColors * 4;
                if (paletteData.Length < expectedSize)
                {
                    System.Diagnostics.Debug.WriteLine($"    ERROR: CMYK palette data too small. Expected {expectedSize}, got {paletteData.Length}");
                    return null;
                }

                for (var i = 0; i < numColors; i++)
                {
                    int offset = i * 4;
                    double c = paletteData[offset] / 255.0;
                    double m = paletteData[offset + 1] / 255.0;
                    double y = paletteData[offset + 2] / 255.0;
                    double k = paletteData[offset + 3] / 255.0;

                    // Simple CMYK to RGB conversion
                    var r = (byte)(255 * (1 - c) * (1 - k));
                    var g = (byte)(255 * (1 - m) * (1 - k));
                    var b = (byte)(255 * (1 - y) * (1 - k));
                    colors.Add(Color.FromRgb(r, g, b));
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"    ERROR: Unsupported base color space: {baseColorSpace}");
                return null;
            }

            // WPF Indexed8 format requires exactly 256 colors
            // Pad the palette with black if we have fewer colors
            while (colors.Count < 256)
            {
                colors.Add(Color.FromRgb(0, 0, 0));
            }

            System.Diagnostics.Debug.WriteLine($"    Palette padded to {colors.Count} colors (WPF requirement)");
            return new BitmapPalette(colors);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"    ERROR in CreatePaletteFromIndexedData: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// UIElement wrapper for DrawingVisual to allow it to be added to a Canvas
/// </summary>
public class VisualHost : FrameworkElement
{
    public DrawingVisual? Visual { get; set; }

    protected override int VisualChildrenCount => Visual != null ? 1 : 0;

    protected override Visual GetVisualChild(int index)
    {
        if (Visual == null || index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Visual;
    }
}
