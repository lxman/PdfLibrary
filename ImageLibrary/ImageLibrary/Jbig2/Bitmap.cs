using System;

namespace ImageLibrary.Jbig2;

/// <summary>
/// A bi-level (1-bit per pixel) bitmap.
/// </summary>
public sealed class Bitmap
{
    private readonly byte[] _data;

    /// <summary>
    /// Gets the width of the bitmap in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the bitmap in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the number of bytes per row (stride) in the bitmap data.
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// Creates a new bitmap with the specified dimensions.
    /// </summary>
    public Bitmap(int width, int height)
    {
        ValidateDimensions(width, height);

        Width = width;
        Height = height;
        Stride = (width + 7) / 8;
        _data = new byte[Stride * height];
    }

    /// <summary>
    /// Creates a new bitmap with the specified dimensions and validates against options.
    /// </summary>
    public Bitmap(int width, int height, Jbig2DecoderOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        options.ValidateDimensions(width, height, "Bitmap");

        Width = width;
        Height = height;
        Stride = (width + 7) / 8;

        // Use checked arithmetic to prevent overflow
        long dataSize;
        try
        {
            dataSize = checked((long)Stride * height);
        }
        catch (OverflowException)
        {
            throw new Jbig2ResourceException($"Bitmap data size overflow: {Stride} * {height}");
        }

        if (dataSize > int.MaxValue)
            throw new Jbig2ResourceException($"Bitmap data size {dataSize} exceeds maximum array size");

        _data = new byte[dataSize];
    }

    /// <summary>
    /// Creates a bitmap wrapping existing data.
    /// </summary>
    public Bitmap(int width, int height, byte[] data)
    {
        ValidateDimensions(width, height);

        Width = width;
        Height = height;
        Stride = (width + 7) / 8;

        long requiredSize = (long)Stride * height;
        if (data.Length < requiredSize)
            throw new ArgumentException($"Data array too small: need {requiredSize}, got {data.Length}");

        _data = data;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Width must be positive, got {width}");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), $"Height must be positive, got {height}");

        // Check for overflow in stride calculation
        int stride = (width + 7) / 8;

        // Check for overflow in data size
        long dataSize = (long)stride * height;
        if (dataSize > int.MaxValue)
            throw new ArgumentException($"Bitmap dimensions {width}x{height} would require {dataSize} bytes, exceeding maximum");
    }

    /// <summary>
    /// Gets the raw byte data.
    /// </summary>
    public byte[] Data => _data;

    /// <summary>
    /// Gets a pixel value (0 or 1).
    /// </summary>
    public int GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return 0; // Out of bounds returns 0

        int byteIndex = y * Stride + (x >> 3);
        int bitIndex = 7 - (x & 7); // MSB first
        return (_data[byteIndex] >> bitIndex) & 1;
    }

    /// <summary>
    /// Sets a pixel value (0 or 1).
    /// </summary>
    public void SetPixel(int x, int y, int value)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return;

        int byteIndex = y * Stride + (x >> 3);
        int bitIndex = 7 - (x & 7);

        if (value != 0)
            _data[byteIndex] |= (byte)(1 << bitIndex);
        else
            _data[byteIndex] &= (byte)~(1 << bitIndex);
    }

    /// <summary>
    /// Fills the bitmap with a value (0 or 1).
    /// </summary>
    public void Fill(int value)
    {
        byte fill = value != 0 ? (byte)0xFF : (byte)0x00;
        Array.Fill(_data, fill);
    }

    /// <summary>
    /// Copies a region from another bitmap.
    /// </summary>
    public void Blit(Bitmap source, int destX, int destY, CombinationOperator op = CombinationOperator.Or)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        // Calculate actual bounds to avoid iterating over out-of-bounds pixels
        int srcStartX = Math.Max(0, -destX);
        int srcStartY = Math.Max(0, -destY);
        int srcEndX = Math.Min(source.Width, Width - destX);
        int srcEndY = Math.Min(source.Height, Height - destY);

        for (int sy = srcStartY; sy < srcEndY; sy++)
        {
            int dy = destY + sy;

            for (int sx = srcStartX; sx < srcEndX; sx++)
            {
                int dx = destX + sx;

                int srcPixel = source.GetPixel(sx, sy);
                int dstPixel = GetPixel(dx, dy);

                int result = op switch
                {
                    CombinationOperator.Or => dstPixel | srcPixel,
                    CombinationOperator.And => dstPixel & srcPixel,
                    CombinationOperator.Xor => dstPixel ^ srcPixel,
                    CombinationOperator.Xnor => 1 - (dstPixel ^ srcPixel),
                    CombinationOperator.Replace => srcPixel,
                    _ => srcPixel
                };

                SetPixel(dx, dy, result);
            }
        }
    }

    /// <summary>
    /// Creates a copy of this bitmap.
    /// </summary>
    public Bitmap Clone()
    {
        var clone = new Bitmap(Width, Height);
        Array.Copy(_data, clone._data, _data.Length);
        return clone;
    }
}

/// <summary>
/// Combination operators for bitmap compositing.
/// </summary>
public enum CombinationOperator
{
    /// <summary>
    /// Bitwise OR operation.
    /// </summary>
    Or = 0,

    /// <summary>
    /// Bitwise AND operation.
    /// </summary>
    And = 1,

    /// <summary>
    /// Bitwise XOR (exclusive OR) operation.
    /// </summary>
    Xor = 2,

    /// <summary>
    /// Bitwise XNOR (exclusive NOR) operation.
    /// </summary>
    Xnor = 3,

    /// <summary>
    /// Replace operation (source overwrites destination).
    /// </summary>
    Replace = 4
}
