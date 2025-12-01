using System;
using System.IO;

namespace Compressors.Jpeg2k;

/// <summary>
/// JPEG2000 compression/decompression library.
/// Implements ITU-T T.800 (JPEG2000 Part 1) codestream.
/// </summary>
public static class Jpeg2000
{
    /// <summary>
    /// Encodes grayscale image data to JPEG2000 codestream.
    /// </summary>
    /// <param name="imageData">Grayscale pixel data (row-major order)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="quality">Quality level (1-100, higher = better quality)</param>
    /// <param name="lossy">True for lossy (9/7 wavelet), false for lossless (5/3 wavelet)</param>
    /// <param name="numLevels">Number of DWT decomposition levels (1-10)</param>
    /// <returns>JPEG2000 codestream bytes</returns>
    public static byte[] Encode(
        ReadOnlySpan<byte> imageData,
        int width,
        int height,
        int quality = 75,
        bool lossy = true,
        int numLevels = 5)
    {
        var encoder = new Jp2kEncoder(quality, numLevels, lossy);
        return encoder.Encode(imageData, width, height, 1);
    }

    /// <summary>
    /// Encodes image data to JPEG2000 codestream with specified number of components.
    /// </summary>
    /// <param name="imageData">Component-interleaved pixel data (R,G,B,R,G,B,... or C,M,Y,K,...)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="numComponents">Number of components (1=grayscale, 3=RGB, 4=CMYK)</param>
    /// <param name="quality">Quality level (1-100, higher = better quality)</param>
    /// <param name="lossy">True for lossy (9/7 wavelet), false for lossless (5/3 wavelet)</param>
    /// <param name="numLevels">Number of DWT decomposition levels (1-10)</param>
    /// <param name="useColorTransform">Apply ICT/RCT color transform for RGB (default true)</param>
    /// <returns>JPEG2000 codestream bytes</returns>
    public static byte[] Encode(
        ReadOnlySpan<byte> imageData,
        int width,
        int height,
        int numComponents,
        int quality = 75,
        bool lossy = true,
        int numLevels = 5,
        bool useColorTransform = true)
    {
        var encoder = new Jp2kEncoder(quality, numLevels, lossy, useColorTransform: useColorTransform);
        return encoder.Encode(imageData, width, height, numComponents);
    }

    /// <summary>
    /// Encodes grayscale image data to JPEG2000 codestream.
    /// </summary>
    /// <param name="imageData">Grayscale pixel data (row-major order)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="output">Output stream</param>
    /// <param name="quality">Quality level (1-100, higher = better quality)</param>
    /// <param name="lossy">True for lossy (9/7 wavelet), false for lossless (5/3 wavelet)</param>
    /// <param name="numLevels">Number of DWT decomposition levels (1-10)</param>
    public static void Encode(
        ReadOnlySpan<byte> imageData,
        int width,
        int height,
        Stream output,
        int quality = 75,
        bool lossy = true,
        int numLevels = 5)
    {
        var encoder = new Jp2kEncoder(quality, numLevels, lossy);
        encoder.Encode(imageData, width, height, 1, output);
    }

    /// <summary>
    /// Encodes image data to JPEG2000 codestream with specified number of components.
    /// </summary>
    /// <param name="imageData">Component-interleaved pixel data (R,G,B,R,G,B,... or C,M,Y,K,...)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="numComponents">Number of components (1=grayscale, 3=RGB, 4=CMYK)</param>
    /// <param name="output">Output stream</param>
    /// <param name="quality">Quality level (1-100, higher = better quality)</param>
    /// <param name="lossy">True for lossy (9/7 wavelet), false for lossless (5/3 wavelet)</param>
    /// <param name="numLevels">Number of DWT decomposition levels (1-10)</param>
    /// <param name="useColorTransform">Apply ICT/RCT color transform for RGB (default true)</param>
    public static void Encode(
        ReadOnlySpan<byte> imageData,
        int width,
        int height,
        int numComponents,
        Stream output,
        int quality = 75,
        bool lossy = true,
        int numLevels = 5,
        bool useColorTransform = true)
    {
        var encoder = new Jp2kEncoder(quality, numLevels, lossy, useColorTransform: useColorTransform);
        encoder.Encode(imageData, width, height, numComponents, output);
    }

    /// <summary>
    /// Decodes a JPEG2000 codestream to grayscale image data.
    /// </summary>
    /// <param name="data">JPEG2000 codestream bytes</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <returns>Grayscale pixel data (row-major order)</returns>
    public static byte[] Decode(byte[] data, out int width, out int height)
    {
        var decoder = new Jp2kDecoder();
        var result = decoder.Decode(data);
        width = decoder.Width;
        height = decoder.Height;
        return result;
    }

    /// <summary>
    /// Decodes a JPEG2000 codestream to image data with component information.
    /// </summary>
    /// <param name="data">JPEG2000 codestream bytes</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <param name="numComponents">Output: number of components</param>
    /// <returns>Component-interleaved pixel data (R,G,B,R,G,B,... for RGB)</returns>
    public static byte[] Decode(byte[] data, out int width, out int height, out int numComponents)
    {
        var decoder = new Jp2kDecoder();
        var result = decoder.Decode(data);
        width = decoder.Width;
        height = decoder.Height;
        numComponents = decoder.NumComponents;
        return result;
    }

    /// <summary>
    /// Decodes a JPEG2000 codestream to grayscale image data.
    /// </summary>
    /// <param name="stream">Input stream containing JPEG2000 codestream</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <returns>Grayscale pixel data (row-major order)</returns>
    public static byte[] Decode(Stream stream, out int width, out int height)
    {
        var decoder = new Jp2kDecoder();
        var result = decoder.Decode(stream);
        width = decoder.Width;
        height = decoder.Height;
        return result;
    }

    /// <summary>
    /// Decodes a JPEG2000 codestream to image data with component information.
    /// </summary>
    /// <param name="stream">Input stream containing JPEG2000 codestream</param>
    /// <param name="width">Output: image width</param>
    /// <param name="height">Output: image height</param>
    /// <param name="numComponents">Output: number of components</param>
    /// <returns>Component-interleaved pixel data (R,G,B,R,G,B,... for RGB)</returns>
    public static byte[] Decode(Stream stream, out int width, out int height, out int numComponents)
    {
        var decoder = new Jp2kDecoder();
        var result = decoder.Decode(stream);
        width = decoder.Width;
        height = decoder.Height;
        numComponents = decoder.NumComponents;
        return result;
    }

    /// <summary>
    /// Checks if the data starts with a valid JPEG2000 codestream signature.
    /// </summary>
    /// <param name="data">Data to check</param>
    /// <returns>True if data appears to be a JPEG2000 codestream</returns>
    public static bool IsJpeg2000Codestream(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return false;

        // Check for SOC marker (0xFF4F)
        return data[0] == 0xFF && data[1] == 0x4F;
    }

    /// <summary>
    /// Checks if the data starts with a valid JP2 file signature.
    /// </summary>
    /// <param name="data">Data to check</param>
    /// <returns>True if data appears to be a JP2 file</returns>
    public static bool IsJp2File(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return false;

        // JP2 signature box
        return data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x00 && data[3] == 0x0C &&
               data[4] == 0x6A && data[5] == 0x50 && data[6] == 0x20 && data[7] == 0x20 &&
               data[8] == 0x0D && data[9] == 0x0A && data[10] == 0x87 && data[11] == 0x0A;
    }
}
