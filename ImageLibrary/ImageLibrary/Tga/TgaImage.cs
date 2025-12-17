using System;

namespace ImageLibrary.Tga;

/// <summary>
/// Represents a decoded TGA image with pixel data.
/// </summary>
public class TgaImage
{
    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Bits per pixel of the original image.</summary>
    public int BitsPerPixel { get; }

    /// <summary>
    /// Pixel data in BGRA format (32 bits per pixel).
    /// Stored top-down, left-to-right.
    /// </summary>
    public byte[] PixelData { get; }

    /// <summary>Stride of the BGRA pixel data (Width * 4).</summary>
    public int Stride => Width * 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="TgaImage"/> class with the specified dimensions and pixel data.
    /// </summary>
    /// <param name="width">The width of the image in pixels.</param>
    /// <param name="height">The height of the image in pixels.</param>
    /// <param name="bitsPerPixel">The bits per pixel of the original TGA image.</param>
    /// <param name="pixelData">The pixel data in BGRA format (32 bits per pixel).</param>
    public TgaImage(int width, int height, int bitsPerPixel, byte[] pixelData)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixelData.Length != width * height * 4)
            throw new ArgumentException($"Pixel data size mismatch: expected {width * height * 4}, got {pixelData.Length}");

        Width = width;
        Height = height;
        BitsPerPixel = bitsPerPixel;
        PixelData = pixelData;
    }

    /// <summary>
    /// Create a new empty TGA image with the specified dimensions.
    /// </summary>
    public TgaImage(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        BitsPerPixel = 32;
        PixelData = new byte[width * height * 4];
    }

    /// <summary>
    /// Get pixel color at the specified coordinates.
    /// </summary>
    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));

        int offset = (y * Width + x) * 4;
        return (PixelData[offset + 2], PixelData[offset + 1], PixelData[offset], PixelData[offset + 3]);
    }

    /// <summary>
    /// Set pixel color at the specified coordinates.
    /// </summary>
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
