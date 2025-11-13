using CoreJ2K;
using CoreJ2K.Util;
using PdfLibrary.Filters.JpxDecode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PdfLibrary.Filters;

/// <summary>
/// JPXDecode filter - JPEG 2000 compression (ISO 32000-1:2008 section 7.4.10)
/// Uses Melville.CSJ2K for JPEG 2000 decoding
/// </summary>
public class JpxDecodeFilter : IStreamFilter
{
    public string Name => "JPXDecode";

    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        throw new NotSupportedException("JPEG 2000 encoding is not supported.");
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
            // Register RawImageCreator for RawImage support
            RawImageCreator.Register();

            // Decode JPEG 2000 data using Melville.CSJ2K
            using var inputStream = new MemoryStream(data);
            PortableImage portableImage = J2kImage.FromStream(inputStream);

            // Convert to RawImage to get raw bytes
            var rawImage = portableImage.As<RawImage>();

            int components = rawImage.Components;
            byte[] imageBytes = rawImage.Bytes;

            // If already RGB, return as-is
            if (components == 3)
            {
                return imageBytes;
            }

            // Calculate dimensions
            int pixelCount = imageBytes.Length / components;
            var pixels = new byte[pixelCount * 3];

            switch (components)
            {
                case 1:
                {
                    // Grayscale - replicate to RGB
                    var offset = 0;
                    for (var i = 0; i < pixelCount; i++)
                    {
                        byte gray = imageBytes[i];
                        pixels[offset++] = gray;
                        pixels[offset++] = gray;
                        pixels[offset++] = gray;
                    }

                    break;
                }
                case 4:
                {
                    // RGBA - strip alpha channel
                    var srcOffset = 0;
                    var dstOffset = 0;
                    for (var i = 0; i < pixelCount; i++)
                    {
                        pixels[dstOffset++] = imageBytes[srcOffset++]; // R
                        pixels[dstOffset++] = imageBytes[srcOffset++]; // G
                        pixels[dstOffset++] = imageBytes[srcOffset++]; // B
                        srcOffset++; // Skip alpha
                    }

                    break;
                }
                default:
                {
                    // Unknown format - use ImageSharp as fallback
                    using Image<Rgb24> image = SixLabors.ImageSharp.Image.Load<Rgb24>(new MemoryStream(imageBytes));
                    var fallbackPixels = new byte[image.Width * image.Height * 3];

                    image.ProcessPixelRows(accessor =>
                    {
                        var offset = 0;
                        for (var y = 0; y < accessor.Height; y++)
                        {
                            Span<Rgb24> row = accessor.GetRowSpan(y);
                            for (var x = 0; x < accessor.Width; x++)
                            {
                                Rgb24 pixel = row[x];
                                fallbackPixels[offset++] = pixel.R;
                                fallbackPixels[offset++] = pixel.G;
                                fallbackPixels[offset++] = pixel.B;
                            }
                        }
                    });

                    return fallbackPixels;
                }
            }

            return pixels;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode JPEG 2000 data: {ex.Message}", ex);
        }
    }
}
