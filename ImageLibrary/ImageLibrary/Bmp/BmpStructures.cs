using System;

namespace ImageLibrary.Bmp;

/// <summary>
/// BMP file header (14 bytes) - appears at the start of every BMP file.
/// </summary>
internal readonly struct BitmapFileHeader
{
    /// <summary>File type signature - must be 0x4D42 ('BM').</summary>
    public ushort Type { get; }

    /// <summary>Total size of the BMP file in bytes.</summary>
    public uint FileSize { get; }

    /// <summary>Reserved - must be 0.</summary>
    public ushort Reserved1 { get; }

    /// <summary>Reserved - must be 0.</summary>
    public ushort Reserved2 { get; }

    /// <summary>Offset from start of file to pixel data.</summary>
    public uint PixelDataOffset { get; }

    public const int Size = 14;
    public const ushort BmpSignature = 0x4D42; // 'BM' in little-endian

    public BitmapFileHeader(ushort type, uint fileSize, ushort reserved1, ushort reserved2, uint pixelDataOffset)
    {
        Type = type;
        FileSize = fileSize;
        Reserved1 = reserved1;
        Reserved2 = reserved2;
        PixelDataOffset = pixelDataOffset;
    }
}

/// <summary>
/// BMP info header (40 bytes) - BITMAPINFOHEADER, the most common format.
/// </summary>
internal readonly struct BitmapInfoHeader
{
    /// <summary>Size of this header structure (40 for BITMAPINFOHEADER).</summary>
    public uint HeaderSize { get; }

    /// <summary>Width of bitmap in pixels.</summary>
    public int Width { get; }

    /// <summary>
    /// Height of bitmap in pixels.
    /// Positive = bottom-up DIB (origin at lower-left).
    /// Negative = top-down DIB (origin at upper-left).
    /// </summary>
    public int Height { get; }

    /// <summary>Number of color planes - must be 1.</summary>
    public ushort Planes { get; }

    /// <summary>Bits per pixel (1, 4, 8, 16, 24, or 32).</summary>
    public ushort BitsPerPixel { get; }

    /// <summary>Compression type.</summary>
    public BmpCompression Compression { get; }

    /// <summary>Size of pixel data in bytes (can be 0 for uncompressed).</summary>
    public uint ImageSize { get; }

    /// <summary>Horizontal resolution in pixels per meter.</summary>
    public int XPixelsPerMeter { get; }

    /// <summary>Vertical resolution in pixels per meter.</summary>
    public int YPixelsPerMeter { get; }

    /// <summary>Number of colors in the palette (0 = max for bit depth).</summary>
    public uint ColorsUsed { get; }

    /// <summary>Number of important colors (0 = all).</summary>
    public uint ColorsImportant { get; }

    public const int Size = 40;

    /// <summary>True if the bitmap is stored top-down (negative height).</summary>
    public bool IsTopDown => Height < 0;

    /// <summary>Absolute height of the bitmap.</summary>
    public int AbsoluteHeight => Math.Abs(Height);

    /// <summary>
    /// Calculate the stride (bytes per row, padded to 4-byte boundary).
    /// </summary>
    public int Stride => ((Width * BitsPerPixel + 31) / 32) * 4;

    public BitmapInfoHeader(
        uint headerSize, int width, int height, ushort planes, ushort bitsPerPixel,
        BmpCompression compression, uint imageSize, int xPixelsPerMeter, int yPixelsPerMeter,
        uint colorsUsed, uint colorsImportant)
    {
        HeaderSize = headerSize;
        Width = width;
        Height = height;
        Planes = planes;
        BitsPerPixel = bitsPerPixel;
        Compression = compression;
        ImageSize = imageSize;
        XPixelsPerMeter = xPixelsPerMeter;
        YPixelsPerMeter = yPixelsPerMeter;
        ColorsUsed = colorsUsed;
        ColorsImportant = colorsImportant;
    }
}

/// <summary>
/// BMP compression types.
/// </summary>
internal enum BmpCompression : uint
{
    /// <summary>Uncompressed RGB.</summary>
    Rgb = 0,

    /// <summary>RLE encoding for 8-bpp bitmaps.</summary>
    Rle8 = 1,

    /// <summary>RLE encoding for 4-bpp bitmaps.</summary>
    Rle4 = 2,

    /// <summary>Uncompressed with color masks (16/32-bpp).</summary>
    BitFields = 3,

    /// <summary>JPEG compression (for printers).</summary>
    Jpeg = 4,

    /// <summary>PNG compression (for printers).</summary>
    Png = 5,

    /// <summary>Uncompressed with alpha color mask.</summary>
    AlphaBitFields = 6
}

/// <summary>
/// RGBA color palette entry.
/// </summary>
internal readonly struct RgbQuad
{
    public byte Blue { get; }
    public byte Green { get; }
    public byte Red { get; }
    public byte Reserved { get; } // Alpha in some cases

    public const int Size = 4;

    public RgbQuad(byte blue, byte green, byte red, byte reserved = 0)
    {
        Blue = blue;
        Green = green;
        Red = red;
        Reserved = reserved;
    }
}