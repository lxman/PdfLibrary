namespace ImageLibrary.Tga;

/// <summary>
/// TGA file header (18 bytes).
/// </summary>
internal readonly struct TgaHeader
{
    /// <summary>Length of the image ID field (0-255).</summary>
    public byte IdLength { get; }

    /// <summary>Color map type (0 = no color map, 1 = has color map).</summary>
    public byte ColorMapType { get; }

    /// <summary>Image type.</summary>
    public TgaImageType ImageType { get; }

    /// <summary>Index of first color map entry.</summary>
    public ushort ColorMapFirstEntry { get; }

    /// <summary>Number of color map entries.</summary>
    public ushort ColorMapLength { get; }

    /// <summary>Bits per color map entry (15, 16, 24, or 32).</summary>
    public byte ColorMapEntrySize { get; }

    /// <summary>X origin of image.</summary>
    public ushort XOrigin { get; }

    /// <summary>Y origin of image.</summary>
    public ushort YOrigin { get; }

    /// <summary>Width of image in pixels.</summary>
    public ushort Width { get; }

    /// <summary>Height of image in pixels.</summary>
    public ushort Height { get; }

    /// <summary>Bits per pixel (8, 16, 24, or 32).</summary>
    public byte PixelDepth { get; }

    /// <summary>Image descriptor byte.</summary>
    public byte ImageDescriptor { get; }

    public const int Size = 18;

    /// <summary>Number of alpha/attribute bits per pixel (bits 0-3 of descriptor).</summary>
    public int AlphaBits => ImageDescriptor & 0x0F;

    /// <summary>True if image is stored right-to-left (bit 4 of descriptor).</summary>
    public bool IsRightToLeft => (ImageDescriptor & 0x10) != 0;

    /// <summary>True if image is stored top-to-bottom (bit 5 of descriptor).</summary>
    public bool IsTopToBottom => (ImageDescriptor & 0x20) != 0;

    /// <summary>True if this image type uses RLE compression.</summary>
    public bool IsRleCompressed => ImageType == TgaImageType.RleColorMapped ||
                                    ImageType == TgaImageType.RleTrueColor ||
                                    ImageType == TgaImageType.RleGrayscale;

    /// <summary>True if this image has a color map/palette.</summary>
    public bool HasColorMap => ColorMapType == 1;

    public TgaHeader(
        byte idLength, byte colorMapType, TgaImageType imageType,
        ushort colorMapFirstEntry, ushort colorMapLength, byte colorMapEntrySize,
        ushort xOrigin, ushort yOrigin, ushort width, ushort height,
        byte pixelDepth, byte imageDescriptor)
    {
        IdLength = idLength;
        ColorMapType = colorMapType;
        ImageType = imageType;
        ColorMapFirstEntry = colorMapFirstEntry;
        ColorMapLength = colorMapLength;
        ColorMapEntrySize = colorMapEntrySize;
        XOrigin = xOrigin;
        YOrigin = yOrigin;
        Width = width;
        Height = height;
        PixelDepth = pixelDepth;
        ImageDescriptor = imageDescriptor;
    }
}

/// <summary>
/// TGA image type enumeration.
/// </summary>
internal enum TgaImageType : byte
{
    /// <summary>No image data.</summary>
    NoImage = 0,

    /// <summary>Uncompressed color-mapped (paletted) image.</summary>
    ColorMapped = 1,

    /// <summary>Uncompressed true-color (RGB) image.</summary>
    TrueColor = 2,

    /// <summary>Uncompressed grayscale image.</summary>
    Grayscale = 3,

    /// <summary>RLE-compressed color-mapped image.</summary>
    RleColorMapped = 9,

    /// <summary>RLE-compressed true-color image.</summary>
    RleTrueColor = 10,

    /// <summary>RLE-compressed grayscale image.</summary>
    RleGrayscale = 11
}
