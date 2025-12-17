using System;
using System.IO;

namespace ImageLibrary.Lzw;

/// <summary>
/// Static helper class for simple LZW compression and decompression operations.
/// </summary>
public static class Lzw
{
    /// <summary>
    /// Compresses the input data using LZW with PDF-compatible settings.
    /// </summary>
    /// <param name="input">The data to compress.</param>
    /// <returns>The compressed data.</returns>
    public static byte[] Compress(byte[] input)
    {
        return Compress(input, LzwOptions.PdfDefault);
    }

    /// <summary>
    /// Compresses the input data using LZW with the specified options.
    /// </summary>
    /// <param name="input">The data to compress.</param>
    /// <param name="options">The compression options.</param>
    /// <returns>The compressed data.</returns>
    public static byte[] Compress(byte[] input, LzwOptions options)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        using (var output = new MemoryStream())
        {
            using (var encoder = new LzwEncoder(output, options, leaveOpen: true))
            {
                encoder.Encode(input);
            }
            return output.ToArray();
        }
    }

    /// <summary>
    /// Decompresses the input data using LZW with PDF-compatible settings.
    /// </summary>
    /// <param name="input">The compressed data.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] Decompress(byte[] input)
    {
        return Decompress(input, LzwOptions.PdfDefault);
    }

    /// <summary>
    /// Decompresses the input data using LZW with the specified options.
    /// </summary>
    /// <param name="input">The compressed data.</param>
    /// <param name="options">The decompression options.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] Decompress(byte[] input, LzwOptions options)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        using (var inputStream = new MemoryStream(input))
        using (var decoder = new LzwDecoder(inputStream, options))
        {
            return decoder.Decode();
        }
    }

    /// <summary>
    /// Compresses data from the input stream and writes to the output stream.
    /// </summary>
    /// <param name="input">The input stream to compress.</param>
    /// <param name="output">The output stream to write compressed data to.</param>
    /// <param name="options">The compression options. If null, uses PDF defaults.</param>
    public static void Compress(Stream input, Stream output, LzwOptions? options = null)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (output == null)
            throw new ArgumentNullException(nameof(output));

        using (var encoder = new LzwEncoder(output, options, leaveOpen: true))
        {
            encoder.Encode(input);
        }
    }

    /// <summary>
    /// Decompresses data from the input stream and writes to the output stream.
    /// </summary>
    /// <param name="input">The input stream containing compressed data.</param>
    /// <param name="output">The output stream to write decompressed data to.</param>
    /// <param name="options">The decompression options. If null, uses PDF defaults.</param>
    public static void Decompress(Stream input, Stream output, LzwOptions? options = null)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (output == null)
            throw new ArgumentNullException(nameof(output));

        using (var decoder = new LzwDecoder(input, options, leaveOpen: true))
        {
            decoder.Decode(output);
        }
    }
}