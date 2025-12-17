namespace ImageLibrary.Ccitt;

/// <summary>
/// Static helper class for CCITT Fax compression and decompression.
/// </summary>
public static class Ccitt
{
    /// <summary>
    /// Decompresses CCITT compressed data.
    /// </summary>
    /// <param name="data">The compressed data.</param>
    /// <param name="options">Decoding options (defaults to PDF Group 4).</param>
    /// <returns>Decompressed bitmap data.</returns>
    public static byte[] Decompress(byte[] data, CcittOptions? options = null)
    {
        var decoder = new CcittDecoder(options);
        return decoder.Decode(data);
    }

    /// <summary>
    /// Decompresses CCITT compressed data with PDF parameters.
    /// </summary>
    /// <param name="data">The compressed data.</param>
    /// <param name="k">PDF K parameter (-1 = Group 4, 0 = Group 3 1D, &gt;0 = Group 3 2D).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels (0 if unknown).</param>
    /// <param name="blackIs1">True if 0 = white and 1 = black.</param>
    /// <param name="encodedByteAlign">True if rows are byte-aligned.</param>
    /// <param name="endOfLine">True if EOL markers are present.</param>
    /// <param name="endOfBlock">True if the end-of-block marker is present.</param>
    /// <returns>Decompressed bitmap data.</returns>
    public static byte[] Decompress(byte[] data, int k, int width, int height = 0,
        bool blackIs1 = false, bool encodedByteAlign = false,
        bool endOfLine = false, bool endOfBlock = true)
    {
        CcittOptions options = CcittOptions.FromPdfK(k, width);
        options.Height = height;
        options.BlackIs1 = blackIs1;
        options.EncodedByteAlign = encodedByteAlign;
        options.EndOfLine = endOfLine;
        options.EndOfBlock = endOfBlock;

        return Decompress(data, options);
    }

    /// <summary>
    /// Compresses bitmap data using CCITT compression.
    /// </summary>
    /// <param name="data">Raw bitmap data (1 bit per pixel, packed, MSB first).</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="options">Encoding options (defaults to PDF Group 4).</param>
    /// <returns>Compressed data.</returns>
    public static byte[] Compress(byte[] data, int height, CcittOptions? options = null)
    {
        var encoder = new CcittEncoder(options);
        return encoder.Encode(data, height);
    }

    /// <summary>
    /// Compresses bitmap data using Group 4 (MMR) compression.
    /// </summary>
    /// <param name="data">Raw bitmap data.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="blackIs1">True if 1 = black, false if 1 = white.</param>
    /// <returns>Compressed data.</returns>
    public static byte[] CompressGroup4(byte[] data, int width, int height, bool blackIs1 = false)
    {
        var options = new CcittOptions
        {
            Group = CcittGroup.Group4,
            K = -1,
            Width = width,
            BlackIs1 = blackIs1,
            EndOfBlock = true
        };

        return Compress(data, height, options);
    }

    /// <summary>
    /// Compresses bitmap data using Group 3 1D (Modified Huffman) compression.
    /// </summary>
    /// <param name="data">Raw bitmap data.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="blackIs1">True if 1 = black, false if 1 = white.</param>
    /// <param name="endOfLine">True to include EOL markers.</param>
    /// <returns>Compressed data.</returns>
    public static byte[] CompressGroup3_1D(byte[] data, int width, int height,
        bool blackIs1 = false, bool endOfLine = true)
    {
        var options = new CcittOptions
        {
            Group = CcittGroup.Group3OneDimensional,
            K = 0,
            Width = width,
            BlackIs1 = blackIs1,
            EndOfLine = endOfLine,
            EndOfBlock = true
        };

        return Compress(data, height, options);
    }

    /// <summary>
    /// Compresses bitmap data using Group 3 2D (Modified READ) compression.
    /// </summary>
    /// <param name="data">Raw bitmap data.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="k">K parameter (number of 1D lines between 2D lines, typically 2 or 4).</param>
    /// <param name="blackIs1">True if 1 = black, false if 1 = white.</param>
    /// <param name="endOfLine">True to include EOL markers.</param>
    /// <returns>Compressed data.</returns>
    public static byte[] CompressGroup3_2D(byte[] data, int width, int height, int k = 4,
        bool blackIs1 = false, bool endOfLine = true)
    {
        var options = new CcittOptions
        {
            Group = CcittGroup.Group3TwoDimensional,
            K = k,
            Width = width,
            BlackIs1 = blackIs1,
            EndOfLine = endOfLine,
            EndOfBlock = true
        };

        return Compress(data, height, options);
    }

    /// <summary>
    /// Decompresses Group 4 (MMR) compressed data.
    /// </summary>
    /// <param name="data">The compressed data.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="blackIs1">True if 1 = black, false if 1 = white.</param>
    /// <returns>Decompressed bitmap data.</returns>
    public static byte[] DecompressGroup4(byte[] data, int width, int height, bool blackIs1 = false)
    {
        var options = new CcittOptions
        {
            Group = CcittGroup.Group4,
            K = -1,
            Width = width,
            Height = height,
            BlackIs1 = blackIs1,
            EndOfBlock = true
        };

        return Decompress(data, options);
    }

    /// <summary>
    /// Decompresses Group 3 1D (Modified Huffman) compressed data.
    /// </summary>
    /// <param name="data">The compressed data.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="blackIs1">True if 1 = black, false if 1 = white.</param>
    /// <param name="endOfLine">True if EOL markers are present.</param>
    /// <returns>Decompressed bitmap data.</returns>
    public static byte[] DecompressGroup3_1D(byte[] data, int width, int height,
        bool blackIs1 = false, bool endOfLine = true)
    {
        var options = new CcittOptions
        {
            Group = CcittGroup.Group3OneDimensional,
            K = 0,
            Width = width,
            Height = height,
            BlackIs1 = blackIs1,
            EndOfLine = endOfLine,
            EndOfBlock = true
        };

        return Decompress(data, options);
    }
}