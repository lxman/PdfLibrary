namespace ImageLibrary.Tiff;

/// <summary>
/// TIFF tag identifiers for common IFD entries.
/// </summary>
internal enum TiffTag : ushort
{
    /// <summary>Image width in pixels.</summary>
    ImageWidth = 256,

    /// <summary>Image height in pixels.</summary>
    ImageHeight = 257,

    /// <summary>Number of bits per component.</summary>
    BitsPerSample = 258,

    /// <summary>Compression scheme (1=None, 2=CCITT 1D, 3=CCITT Group 3, 4=CCITT Group 4, 5=LZW, 7=JPEG, 8=DEFLATE, 32946=DEFLATE).</summary>
    Compression = 259,

    /// <summary>Photometric interpretation (0=WhiteIsZero, 1=BlackIsZero, 2=RGB, 3=Palette, 4=Transparency Mask, 5=CMYK, 6=YCbCr).</summary>
    PhotometricInterpretation = 262,

    /// <summary>Fill order for bits within bytes (1=MSB-first [default], 2=LSB-first).</summary>
    FillOrder = 266,

    /// <summary>Image description string.</summary>
    ImageDescription = 270,

    /// <summary>Number of strips in image.</summary>
    StripOffsets = 273,

    /// <summary>Samples per pixel (1=grayscale, 3=RGB, 4=RGBA).</summary>
    SamplesPerPixel = 277,

    /// <summary>Number of rows per strip.</summary>
    RowsPerStrip = 278,

    /// <summary>Byte counts for each strip.</summary>
    StripByteCounts = 279,

    /// <summary>X resolution in pixels per resolution unit.</summary>
    XResolution = 282,

    /// <summary>Y resolution in pixels per resolution unit.</summary>
    YResolution = 283,

    /// <summary>Storage organization (1=chunky, 2=planar).</summary>
    PlanarConfiguration = 284,

    /// <summary>Options for CCITT Group 3 encoding (bit 0: 2D encoding used, bit 1: uncompressed mode, bit 2: fill bits before EOL).</summary>
    T4Options = 292,

    /// <summary>Resolution unit (1=none, 2=inch, 3=centimeter).</summary>
    ResolutionUnit = 296,

    /// <summary>Software that created the image.</summary>
    Software = 305,

    /// <summary>Color map for palette color images.</summary>
    ColorMap = 320,

    /// <summary>Tile width.</summary>
    TileWidth = 322,

    /// <summary>Tile height.</summary>
    TileLength = 323,

    /// <summary>Tile offsets.</summary>
    TileOffsets = 324,

    /// <summary>Tile byte counts.</summary>
    TileByteCounts = 325,

    /// <summary>Extra samples description.</summary>
    ExtraSamples = 338,

    /// <summary>Sample format (1=unsigned int, 2=signed int, 3=IEEE float).</summary>
    SampleFormat = 339,

    /// <summary>JPEG quantization and Huffman tables (TIFF Technical Note #2).</summary>
    JpegTables = 347,

    /// <summary>Predictor (1=none, 2=horizontal differencing).</summary>
    Predictor = 317
}
