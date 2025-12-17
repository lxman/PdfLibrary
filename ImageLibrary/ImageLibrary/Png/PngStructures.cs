using System;

namespace ImageLibrary.Png;

/// <summary>
/// PNG file signature (8 bytes).
/// </summary>
internal static class PngSignature
{
    public static readonly byte[] Bytes = [137, 80, 78, 71, 13, 10, 26, 10];
    public const int Length = 8;

    public static bool IsValid(byte[] data, int offset = 0)
    {
        if (data.Length < offset + Length)
            return false;

        for (var i = 0; i < Length; i++)
        {
            if (data[offset + i] != Bytes[i])
                return false;
        }
        return true;
    }
}

/// <summary>
/// PNG chunk header.
/// </summary>
internal readonly struct PngChunk
{
    /// <summary>Length of the chunk data (not including type or CRC).</summary>
    public uint Length { get; }

    /// <summary>Chunk type as 4-character string.</summary>
    public string Type { get; }

    /// <summary>Offset in the file where chunk data begins.</summary>
    public int DataOffset { get; }

    /// <summary>CRC-32 of type + data.</summary>
    public uint Crc { get; }

    public const int HeaderSize = 8; // Length + Type
    public const int FooterSize = 4; // CRC

    public PngChunk(uint length, string type, int dataOffset, uint crc)
    {
        Length = length;
        Type = type;
        DataOffset = dataOffset;
        Crc = crc;
    }

    /// <summary>True if this is a critical chunk (uppercase first letter).</summary>
    public bool IsCritical => Type.Length > 0 && char.IsUpper(Type[0]);
}

/// <summary>
/// IHDR chunk data - image header.
/// </summary>
internal readonly struct IhdrChunk
{
    /// <summary>Image width in pixels.</summary>
    public uint Width { get; }

    /// <summary>Image height in pixels.</summary>
    public uint Height { get; }

    /// <summary>Bit depth (1, 2, 4, 8, or 16).</summary>
    public byte BitDepth { get; }

    /// <summary>Color type.</summary>
    public PngColorType ColorType { get; }

    /// <summary>Compression method (always 0 = deflate).</summary>
    public byte CompressionMethod { get; }

    /// <summary>Filter method (always 0 = adaptive filtering).</summary>
    public byte FilterMethod { get; }

    /// <summary>Interlace method.</summary>
    public PngInterlaceMethod InterlaceMethod { get; }

    public const int Size = 13;
    public const string ChunkType = "IHDR";

    /// <summary>Number of channels for this color type.</summary>
    public int Channels => ColorType switch
    {
        PngColorType.Grayscale => 1,
        PngColorType.Rgb => 3,
        PngColorType.Indexed => 1,
        PngColorType.GrayscaleAlpha => 2,
        PngColorType.Rgba => 4,
        _ => 0
    };

    /// <summary>Bytes per pixel (may be fractional for low bit depths, returns minimum of 1).</summary>
    public int BytesPerPixel => Math.Max(1, (Channels * BitDepth + 7) / 8);

    /// <summary>Bits per pixel.</summary>
    public int BitsPerPixel => Channels * BitDepth;

    public IhdrChunk(uint width, uint height, byte bitDepth, PngColorType colorType,
        byte compressionMethod, byte filterMethod, PngInterlaceMethod interlaceMethod)
    {
        Width = width;
        Height = height;
        BitDepth = bitDepth;
        ColorType = colorType;
        CompressionMethod = compressionMethod;
        FilterMethod = filterMethod;
        InterlaceMethod = interlaceMethod;
    }
}

/// <summary>
/// PNG color types.
/// </summary>
public enum PngColorType : byte
{
    /// <summary>Grayscale (bit depths: 1, 2, 4, 8, 16).</summary>
    Grayscale = 0,

    /// <summary>RGB truecolor (bit depths: 8, 16).</summary>
    Rgb = 2,

    /// <summary>Indexed color with palette (bit depths: 1, 2, 4, 8).</summary>
    Indexed = 3,

    /// <summary>Grayscale with alpha (bit depths: 8, 16).</summary>
    GrayscaleAlpha = 4,

    /// <summary>RGBA truecolor with alpha (bit depths: 8, 16).</summary>
    Rgba = 6
}

/// <summary>
/// PNG interlace methods.
/// </summary>
internal enum PngInterlaceMethod : byte
{
    /// <summary>No interlacing.</summary>
    None = 0,

    /// <summary>Adam7 interlacing.</summary>
    Adam7 = 1
}

/// <summary>
/// PNG filter types.
/// </summary>
internal enum PngFilterType : byte
{
    /// <summary>No filtering.</summary>
    None = 0,

    /// <summary>Byte to the left.</summary>
    Sub = 1,

    /// <summary>Byte above.</summary>
    Up = 2,

    /// <summary>Average of left and above.</summary>
    Average = 3,

    /// <summary>Paeth predictor.</summary>
    Paeth = 4
}

/// <summary>
/// PNG chunk type constants.
/// </summary>
internal static class PngChunkTypes
{
    public const string IHDR = "IHDR";
    public const string PLTE = "PLTE";
    public const string IDAT = "IDAT";
    public const string IEND = "IEND";
    public const string tRNS = "tRNS";
    public const string gAMA = "gAMA";
    public const string cHRM = "cHRM";
    public const string sRGB = "sRGB";
    public const string iCCP = "iCCP";
    public const string tEXt = "tEXt";
    public const string zTXt = "zTXt";
    public const string iTXt = "iTXt";
    public const string bKGD = "bKGD";
    public const string pHYs = "pHYs";
    public const string tIME = "tIME";
}

/// <summary>
/// RGB color for palette.
/// </summary>
internal readonly struct PngColor
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public PngColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }
}
