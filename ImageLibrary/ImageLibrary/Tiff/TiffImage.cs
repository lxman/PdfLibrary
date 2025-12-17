using System;

namespace ImageLibrary.Tiff;

/// <summary>
/// Represents a decoded TIFF image with 32-bit BGRA pixel data.
/// </summary>
public sealed class TiffImage
{
    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the raw pixel data in 32-bit BGRA format (bottom-to-top, left-to-right).
    /// Each pixel is 4 bytes: Blue, Green, Red, Alpha.
    /// </summary>
    public byte[] PixelData { get; }

    /// <summary>
    /// Creates a new TIFF image with the specified dimensions and optional pixel data.
    /// </summary>
    /// <param name="width">The width of the image in pixels.</param>
    /// <param name="height">The height of the image in pixels.</param>
    /// <param name="pixelData">Optional pixel data in BGRA format. If null, a new array is allocated.</param>
    /// <exception cref="ArgumentException">Thrown when dimensions are invalid or pixel data size is incorrect.</exception>
    public TiffImage(int width, int height, byte[]? pixelData = null)
    {
        if (width <= 0)
            throw new ArgumentException("Width must be positive.", nameof(width));
        if (height <= 0)
            throw new ArgumentException("Height must be positive.", nameof(height));

        Width = width;
        Height = height;

        int expectedSize = width * height * 4;
        if (pixelData == null)
        {
            PixelData = new byte[expectedSize];
        }
        else
        {
            if (pixelData.Length != expectedSize)
                throw new ArgumentException($"Pixel data size must be {expectedSize} bytes (width * height * 4).", nameof(pixelData));
            PixelData = pixelData;
        }
    }

    /// <summary>
    /// Gets the color of the pixel at the specified coordinates.
    /// </summary>
    /// <param name="x">The x-coordinate (0-based, left to right).</param>
    /// <param name="y">The y-coordinate (0-based, bottom to top).</param>
    /// <returns>A tuple containing (Blue, Green, Red, Alpha) components.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when coordinates are out of bounds.</exception>
    public (byte Blue, byte Green, byte Red, byte Alpha) GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        int offset = (y * Width + x) * 4;
        return (PixelData[offset], PixelData[offset + 1], PixelData[offset + 2], PixelData[offset + 3]);
    }

    /// <summary>
    /// Sets the color of the pixel at the specified coordinates.
    /// </summary>
    /// <param name="x">The x-coordinate (0-based, left to right).</param>
    /// <param name="y">The y-coordinate (0-based, bottom to top).</param>
    /// <param name="blue">The blue component (0-255).</param>
    /// <param name="green">The green component (0-255).</param>
    /// <param name="red">The red component (0-255).</param>
    /// <param name="alpha">The alpha component (0-255, 255 = opaque).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when coordinates are out of bounds.</exception>
    public void SetPixel(int x, int y, byte blue, byte green, byte red, byte alpha = 255)
    {
        if (x < 0 || x >= Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        int offset = (y * Width + x) * 4;
        PixelData[offset] = blue;
        PixelData[offset + 1] = green;
        PixelData[offset + 2] = red;
        PixelData[offset + 3] = alpha;
    }
}
