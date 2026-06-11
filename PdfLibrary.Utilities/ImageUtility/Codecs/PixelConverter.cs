namespace ImageUtility.Codecs;

/// <summary>
/// Conversions between PixelFormat layouts and the canonical BGRA32 (top-down)
/// layout used by the in-house image codecs.
/// </summary>
internal static class PixelConverter
{
    /// <summary>
    /// Convert any supported PixelFormat to top-down BGRA32 with full alpha.
    /// </summary>
    public static byte[] ToBgra32(byte[] data, int width, int height, PixelFormat format)
    {
        int pixelCount = width * height;
        var output = new byte[pixelCount * 4];

        switch (format)
        {
            case PixelFormat.Bgra32:
                Array.Copy(data, output, output.Length);
                break;

            case PixelFormat.Rgba32:
                for (var i = 0; i < pixelCount; i++)
                {
                    int o = i * 4;
                    output[o]     = data[o + 2]; // B
                    output[o + 1] = data[o + 1]; // G
                    output[o + 2] = data[o];     // R
                    output[o + 3] = data[o + 3]; // A
                }
                break;

            case PixelFormat.Rgb24:
                for (var i = 0; i < pixelCount; i++)
                {
                    int src = i * 3;
                    int dst = i * 4;
                    output[dst]     = data[src + 2]; // B
                    output[dst + 1] = data[src + 1]; // G
                    output[dst + 2] = data[src];     // R
                    output[dst + 3] = 255;
                }
                break;

            case PixelFormat.Bgr24:
                for (var i = 0; i < pixelCount; i++)
                {
                    int src = i * 3;
                    int dst = i * 4;
                    output[dst]     = data[src];     // B
                    output[dst + 1] = data[src + 1]; // G
                    output[dst + 2] = data[src + 2]; // R
                    output[dst + 3] = 255;
                }
                break;

            case PixelFormat.Gray8:
                for (var i = 0; i < pixelCount; i++)
                {
                    int dst = i * 4;
                    byte g = data[i];
                    output[dst]     = g;
                    output[dst + 1] = g;
                    output[dst + 2] = g;
                    output[dst + 3] = 255;
                }
                break;

            case PixelFormat.Cmyk32:
                // Naive CMYK -> RGB (matches the renderer's DeviceCMYK path). Without this, every CMYK
                // image (CMYK JPEG / TIFF) threw here even though the codecs decoded it fine.
                for (var i = 0; i < pixelCount; i++)
                {
                    int o = i * 4;
                    int c = data[o], m = data[o + 1], y = data[o + 2], k = data[o + 3];
                    output[o]     = (byte)((255 - y) * (255 - k) / 255); // B
                    output[o + 1] = (byte)((255 - m) * (255 - k) / 255); // G
                    output[o + 2] = (byte)((255 - c) * (255 - k) / 255); // R
                    output[o + 3] = 255;
                }
                break;

            default:
                throw new NotSupportedException(
                    $"Cannot convert pixel format {format} to BGRA32");
        }

        return output;
    }

    /// <summary>
    /// True if the source format carries an alpha channel that should be preserved.
    /// </summary>
    public static bool HasAlpha(PixelFormat format) => format is PixelFormat.Bgra32 or PixelFormat.Rgba32;
}
