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
            // Load JPEG data using ImageSharp
            using Image<Rgb24> image = Image.Load<Rgb24>(data);

            // Convert to raw RGB byte array
            var pixels = new byte[image.Width * image.Height * 3];

            image.ProcessPixelRows(accessor =>
            {
                var offset = 0;
                for (var y = 0; y < accessor.Height; y++)
                {
                    Span<Rgb24> row = accessor.GetRowSpan(y);
                    for (var x = 0; x < accessor.Width; x++)
                    {
                        Rgb24 pixel = row[x];
                        pixels[offset++] = pixel.R;
                        pixels[offset++] = pixel.G;
                        pixels[offset++] = pixel.B;
                    }
                }
            });

            return pixels;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JPEG data: {ex.Message}", ex);
        }
    }
}
