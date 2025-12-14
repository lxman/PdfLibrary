using SkiaSharp;

namespace Generator;

public static class LogoGenerator
{
    public static void GenerateCompanyLogo(string outputPath)
    {
        const int width = 200;
        const int height = 60;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        // White background
        canvas.Clear(SKColors.White);

        // Blue rectangle background
        using (var paint = new SKPaint())
        {
            paint.Color = new SKColor(44, 62, 80); // #2C3E50
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawRect(0, 0, width, height, paint);
        }

        // White "PDF" text
        using (var paint = new SKPaint())
        {
            paint.Color = SKColors.White;
            paint.TextSize = 32;
            paint.IsAntialias = true;
            paint.Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

            var text = "PdfLibrary";
            var textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);

            var x = (width - textBounds.Width) / 2 - textBounds.Left;
            var y = (height - textBounds.Height) / 2 - textBounds.Top;

            canvas.DrawText(text, x, y, paint);
        }

        // Save as JPEG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }
}
