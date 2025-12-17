using System;
using System.Collections.Generic;

namespace ImageLibrary.Gif;

/// <summary>
/// Represents a decoded GIF image (single frame).
/// </summary>
public class GifImage
{
    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Pixel data in BGRA format (32 bits per pixel).
    /// Stored top-down, left-to-right.
    /// </summary>
    public byte[] PixelData { get; }

    /// <summary>Stride of the BGRA pixel data (Width * 4).</summary>
    public int Stride => Width * 4;

    /// <summary>Frame delay in milliseconds (for animated GIFs).</summary>
    public int DelayMs { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GifImage"/> class with the specified dimensions and pixel data.
    /// </summary>
    /// <param name="width">The width of the image in pixels.</param>
    /// <param name="height">The height of the image in pixels.</param>
    /// <param name="pixelData">The pixel data in BGRA format (32 bits per pixel).</param>
    public GifImage(int width, int height, byte[] pixelData)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixelData.Length != width * height * 4)
            throw new ArgumentException($"Pixel data size mismatch: expected {width * height * 4}, got {pixelData.Length}");

        Width = width;
        Height = height;
        PixelData = pixelData;
    }

    /// <summary>
    /// Create a new empty GIF image with the specified dimensions.
    /// </summary>
    public GifImage(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
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

/// <summary>
/// Represents a complete GIF file which may contain multiple frames.
/// </summary>
public class GifFile
{
    /// <summary>Logical screen width.</summary>
    public int Width { get; }

    /// <summary>Logical screen height.</summary>
    public int Height { get; }

    /// <summary>Frames in the GIF (1 for static, multiple for animated).</summary>
    public List<GifImage> Frames { get; }

    /// <summary>Number of times animation should loop (0 = infinite).</summary>
    public int LoopCount { get; set; }

    /// <summary>Background color (BGRA).</summary>
    public (byte B, byte G, byte R, byte A) BackgroundColor { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GifFile"/> class with the specified logical screen dimensions.
    /// </summary>
    /// <param name="width">The logical screen width.</param>
    /// <param name="height">The logical screen height.</param>
    public GifFile(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        Frames = [];
    }

    /// <summary>
    /// Get the first frame (convenience for static GIFs).
    /// </summary>
    public GifImage? FirstFrame => Frames.Count > 0 ? Frames[0] : null;
}
