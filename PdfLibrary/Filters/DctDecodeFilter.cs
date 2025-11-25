using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfLibrary.Filters;

/// <summary>
/// DCTDecode filter - JPEG compression using Discrete Cosine Transform (ISO 32000-1:2008 section 7.4.8)
/// Uses SixLabors.ImageSharp for JPEG decoding
/// </summary>
public class DctDecodeFilter : IStreamFilter
{
    public string Name => "DCTDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // For encoding, we'd need to know the image dimensions and format
        // This is typically not used in PDF creation workflows
        throw new NotSupportedException("DCTDecode encoding is not supported. Use pre-compressed JPEG data.");
    }

    public byte[] Decode(byte[] data)
    {
        return Decode(data, null);
    }

    public byte[] Decode(byte[] data, Dictionary<string, object>? parameters)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            // Load JPEG data using ImageSharp - use generic Image.Load to preserve original format
            using Image image = Image.Load(data);

            // Convert to raw RGB byte array
            // First convert to Rgb24 format, then extract pixels
            using Image<Rgb24> rgbImage = image.CloneAs<Rgb24>();

            var pixels = new byte[rgbImage.Width * rgbImage.Height * 3];
            rgbImage.CopyPixelDataTo(pixels);

            return pixels;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JPEG data: {ex.Message}", ex);
        }
    }
}
