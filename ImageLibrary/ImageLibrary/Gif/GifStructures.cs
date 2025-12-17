namespace ImageLibrary.Gif;

/// <summary>
/// GIF file header (6 bytes).
/// </summary>
internal readonly struct GifHeader
{
    /// <summary>Signature ("GIF").</summary>
    public string Signature { get; }

    /// <summary>Version ("87a" or "89a").</summary>
    public string Version { get; }

    public const int Size = 6;
    public const string Gif87a = "GIF87a";
    public const string Gif89a = "GIF89a";

    public bool IsValid => Signature == "GIF" && (Version == "87a" || Version == "89a");

    public GifHeader(string signature, string version)
    {
        Signature = signature;
        Version = version;
    }
}

/// <summary>
/// Logical Screen Descriptor (7 bytes).
/// </summary>
internal readonly struct LogicalScreenDescriptor
{
    /// <summary>Width of the logical screen in pixels.</summary>
    public ushort Width { get; }

    /// <summary>Height of the logical screen in pixels.</summary>
    public ushort Height { get; }

    /// <summary>Packed field containing color table info.</summary>
    public byte PackedFields { get; }

    /// <summary>Background color index in the global color table.</summary>
    public byte BackgroundColorIndex { get; }

    /// <summary>Pixel aspect ratio (0 = not specified).</summary>
    public byte PixelAspectRatio { get; }

    public const int Size = 7;

    /// <summary>True if a global color table follows.</summary>
    public bool HasGlobalColorTable => (PackedFields & 0x80) != 0;

    /// <summary>Color resolution (bits per primary color minus 1).</summary>
    public int ColorResolution => ((PackedFields >> 4) & 0x07) + 1;

    /// <summary>True if global color table is sorted by importance.</summary>
    public bool IsGlobalColorTableSorted => (PackedFields & 0x08) != 0;

    /// <summary>Size of global color table: 2^(N+1) entries.</summary>
    public int GlobalColorTableSize => HasGlobalColorTable ? 1 << ((PackedFields & 0x07) + 1) : 0;

    public LogicalScreenDescriptor(ushort width, ushort height, byte packedFields,
        byte backgroundColorIndex, byte pixelAspectRatio)
    {
        Width = width;
        Height = height;
        PackedFields = packedFields;
        BackgroundColorIndex = backgroundColorIndex;
        PixelAspectRatio = pixelAspectRatio;
    }
}

/// <summary>
/// Image Descriptor (9 bytes, excluding separator).
/// </summary>
internal readonly struct ImageDescriptor
{
    /// <summary>Column position of left edge of image.</summary>
    public ushort Left { get; }

    /// <summary>Row position of top edge of image.</summary>
    public ushort Top { get; }

    /// <summary>Width of the image in pixels.</summary>
    public ushort Width { get; }

    /// <summary>Height of the image in pixels.</summary>
    public ushort Height { get; }

    /// <summary>Packed field containing image info.</summary>
    public byte PackedFields { get; }

    public const int Size = 9;
    public const byte Separator = 0x2C;

    /// <summary>True if a local color table follows.</summary>
    public bool HasLocalColorTable => (PackedFields & 0x80) != 0;

    /// <summary>True if image is interlaced.</summary>
    public bool IsInterlaced => (PackedFields & 0x40) != 0;

    /// <summary>True if local color table is sorted.</summary>
    public bool IsLocalColorTableSorted => (PackedFields & 0x20) != 0;

    /// <summary>Size of local color table: 2^(N+1) entries.</summary>
    public int LocalColorTableSize => HasLocalColorTable ? 1 << ((PackedFields & 0x07) + 1) : 0;

    public ImageDescriptor(ushort left, ushort top, ushort width, ushort height, byte packedFields)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        PackedFields = packedFields;
    }
}

/// <summary>
/// Graphics Control Extension (GIF89a).
/// </summary>
internal readonly struct GraphicsControlExtension
{
    /// <summary>Packed field.</summary>
    public byte PackedFields { get; }

    /// <summary>Delay time in centiseconds (1/100th of a second).</summary>
    public ushort DelayTime { get; }

    /// <summary>Transparent color index.</summary>
    public byte TransparentColorIndex { get; }

    public const byte ExtensionIntroducer = 0x21;
    public const byte GraphicsControlLabel = 0xF9;

    /// <summary>Disposal method (0-7).</summary>
    public GifDisposalMethod DisposalMethod => (GifDisposalMethod)((PackedFields >> 2) & 0x07);

    /// <summary>True if user input is expected before continuing.</summary>
    public bool UserInputFlag => (PackedFields & 0x02) != 0;

    /// <summary>True if a transparent color is specified.</summary>
    public bool HasTransparency => (PackedFields & 0x01) != 0;

    public GraphicsControlExtension(byte packedFields, ushort delayTime, byte transparentColorIndex)
    {
        PackedFields = packedFields;
        DelayTime = delayTime;
        TransparentColorIndex = transparentColorIndex;
    }
}

/// <summary>
/// GIF disposal method for animated GIFs.
/// </summary>
internal enum GifDisposalMethod : byte
{
    /// <summary>No disposal specified.</summary>
    NotSpecified = 0,

    /// <summary>Do not dispose - leave graphic in place.</summary>
    DoNotDispose = 1,

    /// <summary>Restore to background color.</summary>
    RestoreToBackground = 2,

    /// <summary>Restore to previous frame.</summary>
    RestoreToPrevious = 3
}

/// <summary>
/// RGB color entry in color table.
/// </summary>
internal readonly struct GifColor
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public const int Size = 3;

    public GifColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }
}

/// <summary>
/// Block types in GIF files.
/// </summary>
internal static class GifBlockTypes
{
    public const byte ExtensionIntroducer = 0x21;
    public const byte ImageSeparator = 0x2C;
    public const byte Trailer = 0x3B;

    // Extension labels
    public const byte GraphicsControlLabel = 0xF9;
    public const byte CommentLabel = 0xFE;
    public const byte ApplicationLabel = 0xFF;
    public const byte PlainTextLabel = 0x01;
}
