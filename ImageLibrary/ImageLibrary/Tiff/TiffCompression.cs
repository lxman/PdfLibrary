namespace ImageLibrary.Tiff;

/// <summary>
/// TIFF compression schemes.
/// </summary>
public enum TiffCompression : ushort
{
    /// <summary>No compression.</summary>
    None = 1,

    /// <summary>CCITT modified Huffman RLE (CCITT Group 3 1D).</summary>
    CcittRle = 2,

    /// <summary>CCITT Group 3 fax encoding.</summary>
    CcittGroup3 = 3,

    /// <summary>CCITT Group 4 fax encoding.</summary>
    CcittGroup4 = 4,

    /// <summary>LZW compression.</summary>
    Lzw = 5,

    /// <summary>Old-style JPEG compression (deprecated).</summary>
    OldJpeg = 6,

    /// <summary>JPEG compression.</summary>
    Jpeg = 7,

    /// <summary>Adobe DEFLATE compression.</summary>
    AdobeDeflate = 8,

    /// <summary>PackBits compression (Macintosh RLE).</summary>
    PackBits = 32773,

    /// <summary>DEFLATE compression (zlib).</summary>
    Deflate = 32946
}
