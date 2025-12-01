using System;
using System.IO;

namespace Compressors.Jpeg;

/// <summary>
/// Static entry point for JPEG encoding and decoding.
/// Provides simple methods for common JPEG operations.
/// </summary>
public static class Jpeg
{
    /// <summary>
    /// Decodes a JPEG image from a byte array.
    /// </summary>
    /// <param name="jpegData">JPEG file data</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <returns>RGB pixel data (3 bytes per pixel)</returns>
    public static byte[] Decode(byte[] jpegData, out int width, out int height)
    {
        using var stream = new MemoryStream(jpegData);
        return Decode(stream, out width, out height);
    }

    /// <summary>
    /// Decodes a JPEG image from a stream.
    /// </summary>
    /// <param name="stream">Stream containing JPEG data</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <returns>RGB pixel data (3 bytes per pixel)</returns>
    public static byte[] Decode(Stream stream, out int width, out int height)
    {
        var decoder = new JpegDecoder();
        var result = decoder.Decode(stream);
        width = decoder.Width;
        height = decoder.Height;
        return result;
    }

    /// <summary>
    /// Decodes a JPEG image from a byte array with optional raw component output.
    /// </summary>
    /// <param name="jpegData">JPEG file data</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <param name="componentCount">Output: number of color components</param>
    /// <param name="convertToRgb">If true, converts to RGB. If false, returns raw component data.</param>
    /// <returns>Pixel data (RGB if converted, raw components otherwise)</returns>
    public static byte[] Decode(byte[] jpegData, out int width, out int height, out int componentCount, bool convertToRgb = true)
    {
        using var stream = new MemoryStream(jpegData);
        return Decode(stream, out width, out height, out componentCount, convertToRgb);
    }

    /// <summary>
    /// Decodes a JPEG image from a stream with optional raw component output.
    /// </summary>
    /// <param name="stream">Stream containing JPEG data</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <param name="componentCount">Output: number of color components</param>
    /// <param name="convertToRgb">If true, converts to RGB. If false, returns raw component data.</param>
    /// <returns>Pixel data (RGB if converted, raw components otherwise)</returns>
    public static byte[] Decode(Stream stream, out int width, out int height, out int componentCount, bool convertToRgb = true)
    {
        var decoder = new JpegDecoder();
        var result = decoder.Decode(stream, convertToRgb);
        width = decoder.Width;
        height = decoder.Height;
        componentCount = decoder.ComponentCount;
        return result;
    }

    /// <summary>
    /// Decodes a JPEG image and returns detailed decode information.
    /// </summary>
    /// <param name="jpegData">JPEG file data</param>
    /// <param name="convertToRgb">If true, converts to RGB. If false, returns raw component data.</param>
    /// <returns>Decode result with pixel data and metadata</returns>
    public static JpegDecodeResult DecodeWithInfo(byte[] jpegData, bool convertToRgb = true)
    {
        using var stream = new MemoryStream(jpegData);
        return DecodeWithInfo(stream, convertToRgb);
    }

    /// <summary>
    /// Decodes a JPEG image and returns detailed decode information.
    /// </summary>
    /// <param name="stream">Stream containing JPEG data</param>
    /// <param name="convertToRgb">If true, converts to RGB. If false, returns raw component data.</param>
    /// <returns>Decode result with pixel data and metadata</returns>
    public static JpegDecodeResult DecodeWithInfo(Stream stream, bool convertToRgb = true)
    {
        var decoder = new JpegDecoder();
        var data = decoder.Decode(stream, convertToRgb);
        return new JpegDecodeResult
        {
            Data = data,
            Width = decoder.Width,
            Height = decoder.Height,
            ComponentCount = decoder.ComponentCount,
            HasAdobeMarker = decoder.HasAdobeMarker,
            AdobeColorTransform = decoder.AdobeColorTransform
        };
    }

    /// <summary>
    /// Encodes RGB image data to JPEG format.
    /// </summary>
    /// <param name="rgb">RGB pixel data (3 bytes per pixel)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="quality">Quality factor 1-100 (default 75)</param>
    /// <param name="subsampling">Chroma subsampling mode (default 4:2:0)</param>
    /// <returns>JPEG file data</returns>
    public static byte[] Encode(
        byte[] rgb,
        int width,
        int height,
        int quality = 75,
        JpegSubsampling subsampling = JpegSubsampling.Subsampling420)
    {
        return Encode(rgb.AsSpan(), width, height, quality, subsampling);
    }

    /// <summary>
    /// Encodes RGB image data to JPEG format.
    /// </summary>
    public static byte[] Encode(
        ReadOnlySpan<byte> rgb,
        int width,
        int height,
        int quality = 75,
        JpegSubsampling subsampling = JpegSubsampling.Subsampling420)
    {
        using var stream = new MemoryStream();
        Encode(rgb, width, height, stream, quality, subsampling);
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes RGB image data to JPEG format and writes to a stream.
    /// </summary>
    public static void Encode(
        ReadOnlySpan<byte> rgb,
        int width,
        int height,
        Stream output,
        int quality = 75,
        JpegSubsampling subsampling = JpegSubsampling.Subsampling420)
    {
        var encoder = new JpegEncoder(quality, subsampling);
        encoder.Encode(rgb, width, height, output);
    }

    /// <summary>
    /// Encodes grayscale image data to JPEG format.
    /// </summary>
    /// <param name="grayscale">Grayscale pixel data (1 byte per pixel)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="quality">Quality factor 1-100 (default 75)</param>
    /// <returns>JPEG file data</returns>
    public static byte[] EncodeGrayscale(
        byte[] grayscale,
        int width,
        int height,
        int quality = 75)
    {
        return EncodeGrayscale(grayscale.AsSpan(), width, height, quality);
    }

    /// <summary>
    /// Encodes grayscale image data to JPEG format.
    /// </summary>
    public static byte[] EncodeGrayscale(
        ReadOnlySpan<byte> grayscale,
        int width,
        int height,
        int quality = 75)
    {
        using var stream = new MemoryStream();
        EncodeGrayscale(grayscale, width, height, stream, quality);
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes grayscale image data to JPEG format and writes to a stream.
    /// </summary>
    public static void EncodeGrayscale(
        ReadOnlySpan<byte> grayscale,
        int width,
        int height,
        Stream output,
        int quality = 75)
    {
        var encoder = new JpegEncoder(quality, JpegSubsampling.Subsampling444);
        encoder.EncodeGrayscale(grayscale, width, height, output);
    }

    /// <summary>
    /// Gets information about a JPEG image without fully decoding it.
    /// </summary>
    /// <param name="jpegData">JPEG file data</param>
    /// <returns>Image information</returns>
    public static JpegInfo GetInfo(byte[] jpegData)
    {
        using var stream = new MemoryStream(jpegData);
        return GetInfo(stream);
    }

    /// <summary>
    /// Gets information about a JPEG image without fully decoding it.
    /// </summary>
    public static JpegInfo GetInfo(Stream stream)
    {
        var info = new JpegInfo();

        // Read SOI marker
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != JpegConstants.SOI)
            throw new InvalidDataException("Not a valid JPEG file");

        while (true)
        {
            // Read marker
            int b = stream.ReadByte();
            if (b != 0xFF)
                break;

            while ((b = stream.ReadByte()) == 0xFF) { }

            if (b < 0)
                break;

            byte marker = (byte)b;

            // Handle markers without length
            if (marker == JpegConstants.SOI || marker == JpegConstants.EOI ||
                (marker >= JpegConstants.RST0 && marker <= JpegConstants.RST7))
            {
                if (marker == JpegConstants.EOI)
                    break;
                continue;
            }

            // Read length
            int lenHigh = stream.ReadByte();
            int lenLow = stream.ReadByte();
            if (lenHigh < 0 || lenLow < 0)
                break;

            int length = (lenHigh << 8) | lenLow;

            // Process SOF markers
            if (marker >= JpegConstants.SOF0 && marker <= JpegConstants.SOF3)
            {
                int precision = stream.ReadByte();
                int height = (stream.ReadByte() << 8) | stream.ReadByte();
                int width = (stream.ReadByte() << 8) | stream.ReadByte();
                int components = stream.ReadByte();

                info.Width = width;
                info.Height = height;
                info.BitsPerSample = precision;
                info.ComponentCount = components;
                info.IsProgressive = marker == JpegConstants.SOF2;
                info.IsBaseline = marker == JpegConstants.SOF0;

                // We have the info we need
                break;
            }
            else
            {
                // Skip segment
                stream.Seek(length - 2, SeekOrigin.Current);
            }
        }

        return info;
    }
}

/// <summary>
/// Information about a JPEG image.
/// </summary>
public class JpegInfo
{
    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Bits per sample (usually 8).
    /// </summary>
    public int BitsPerSample { get; set; }

    /// <summary>
    /// Number of color components (1 = grayscale, 3 = color).
    /// </summary>
    public int ComponentCount { get; set; }

    /// <summary>
    /// True if this is a baseline JPEG (SOF0).
    /// </summary>
    public bool IsBaseline { get; set; }

    /// <summary>
    /// True if this is a progressive JPEG (SOF2).
    /// </summary>
    public bool IsProgressive { get; set; }

    /// <summary>
    /// True if this is a grayscale image.
    /// </summary>
    public bool IsGrayscale => ComponentCount == 1;

    /// <summary>
    /// True if this is a color image.
    /// </summary>
    public bool IsColor => ComponentCount >= 3;
}

/// <summary>
/// Result of decoding a JPEG image with metadata.
/// </summary>
public class JpegDecodeResult
{
    /// <summary>
    /// Decoded pixel data.
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Number of color components (1=grayscale, 3=YCbCr/RGB, 4=CMYK/YCCK).
    /// </summary>
    public int ComponentCount { get; set; }

    /// <summary>
    /// True if an Adobe APP14 marker was found.
    /// </summary>
    public bool HasAdobeMarker { get; set; }

    /// <summary>
    /// Adobe color transform value (0=Unknown/CMYK, 1=YCbCr, 2=YCCK).
    /// </summary>
    public byte AdobeColorTransform { get; set; }

    /// <summary>
    /// True if this is an Adobe YCCK image.
    /// </summary>
    public bool IsYcck => ComponentCount == 4 && HasAdobeMarker && AdobeColorTransform == 2;

    /// <summary>
    /// True if this is a CMYK image.
    /// </summary>
    public bool IsCmyk => ComponentCount == 4;

    /// <summary>
    /// True if this is a grayscale image.
    /// </summary>
    public bool IsGrayscale => ComponentCount == 1;
}
