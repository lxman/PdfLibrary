using System;

namespace PbmCodec;

/// <summary>
/// Represents a decoded Netpbm image (PBM / PGM / PPM) with pixel data
/// normalised to top-down BGRA32 layout.
/// </summary>
public class PbmImage
{
    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The Netpbm variant the image was decoded from (or last encoded as).</summary>
    public PbmFormat SourceFormat { get; set; }

    /// <summary>
    /// Pixel data in BGRA format (32 bits per pixel).
    /// Stored top-down, left-to-right.
    /// </summary>
    public byte[] PixelData { get; }

    /// <summary>Stride of the BGRA pixel data (Width * 4).</summary>
    public int Stride => Width * 4;

    public PbmImage(int width, int height, byte[] pixelData)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixelData.Length != width * height * 4)
            throw new ArgumentException($"Pixel data size mismatch: expected {width * height * 4}, got {pixelData.Length}");

        Width = width;
        Height = height;
        PixelData = pixelData;
    }

    public PbmImage(int width, int height) : this(width, height, new byte[width * height * 4])
    {
    }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));

        int offset = (y * Width + x) * 4;
        return (PixelData[offset + 2], PixelData[offset + 1], PixelData[offset], PixelData[offset + 3]);
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));

        int offset = (y * Width + x) * 4;
        PixelData[offset] = b;
        PixelData[offset + 1] = g;
        PixelData[offset + 2] = r;
        PixelData[offset + 3] = a;
    }
}
