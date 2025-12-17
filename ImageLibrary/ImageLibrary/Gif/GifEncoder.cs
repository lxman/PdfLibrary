using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ImageLibrary.Gif;

/// <summary>
/// Encodes images to GIF format.
/// </summary>
public static class GifEncoder
{
    /// <summary>
    /// Encode a single image to GIF format.
    /// </summary>
    public static byte[] Encode(GifImage image, int maxColors = 256)
    {
        var gifFile = new GifFile(image.Width, image.Height);
        gifFile.Frames.Add(image);
        return Encode(gifFile, maxColors);
    }

    /// <summary>
    /// Encode a GIF file (potentially with multiple frames).
    /// </summary>
    public static byte[] Encode(GifFile gifFile, int maxColors = 256)
    {
        if (gifFile.Frames.Count == 0)
            throw new GifException("GIF file must have at least one frame");

        if (maxColors < 2 || maxColors > 256)
            throw new ArgumentOutOfRangeException(nameof(maxColors), "Max colors must be between 2 and 256");

        using var ms = new MemoryStream();

        // Build global color table from first frame
        GifImage? firstFrame = gifFile.Frames[0];
        (GifColor[] colorTable, byte[] indices) = BuildColorTable(firstFrame, maxColors);
        int colorTableSize = GetColorTableSize(colorTable.Length);
        int colorBits = GetColorBits(colorTableSize);

        // Write header
        WriteHeader(ms);

        // Write logical screen descriptor
        WriteScreenDescriptor(ms, gifFile.Width, gifFile.Height, colorTableSize, colorBits);

        // Write global color table
        WriteColorTable(ms, colorTable, colorTableSize);

        // Write NETSCAPE extension for animated GIFs
        if (gifFile.Frames.Count > 1 || gifFile.LoopCount > 0)
        {
            WriteNetscapeExtension(ms, gifFile.LoopCount);
        }

        // Write frames
        for (var i = 0; i < gifFile.Frames.Count; i++)
        {
            GifImage? frame = gifFile.Frames[i];
            byte[] frameIndices;

            if (i == 0)
            {
                frameIndices = indices;
            }
            else
            {
                // Re-quantize subsequent frames to use the same color table
                frameIndices = QuantizeToColorTable(frame, colorTable);
            }

            // Write graphics control extension if needed
            if (frame.DelayMs > 0 || gifFile.Frames.Count > 1)
            {
                WriteGraphicsControlExtension(ms, frame.DelayMs, -1);
            }

            // Write image
            WriteImage(ms, frame.Width, frame.Height, frameIndices, colorBits);
        }

        // Write trailer
        ms.WriteByte(GifBlockTypes.Trailer);

        return ms.ToArray();
    }

    /// <summary>
    /// Encode a single image to GIF format and write to a stream.
    /// </summary>
    public static void Encode(GifImage image, Stream stream, int maxColors = 256)
    {
        byte[] data = Encode(image, maxColors);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Encode a single image to GIF format and write to a file.
    /// </summary>
    public static void Encode(GifImage image, string path, int maxColors = 256)
    {
        File.WriteAllBytes(path, Encode(image, maxColors));
    }

    /// <summary>
    /// Encode a GIF file to a stream.
    /// </summary>
    public static void Encode(GifFile gifFile, Stream stream, int maxColors = 256)
    {
        byte[] data = Encode(gifFile, maxColors);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Encode a GIF file to a file.
    /// </summary>
    public static void Encode(GifFile gifFile, string path, int maxColors = 256)
    {
        File.WriteAllBytes(path, Encode(gifFile, maxColors));
    }

    private static (GifColor[] colors, byte[] indices) BuildColorTable(GifImage image, int maxColors)
    {
        // Simple median cut color quantization
        var uniqueColors = new Dictionary<int, int>(); // RGB -> count
        byte[] pixelData = image.PixelData;

        for (var i = 0; i < pixelData.Length; i += 4)
        {
            // Skip transparent pixels
            if (pixelData[i + 3] < 128)
                continue;

            int rgb = (pixelData[i + 2] << 16) | (pixelData[i + 1] << 8) | pixelData[i];
            uniqueColors.TryGetValue(rgb, out int count);
            uniqueColors[rgb] = count + 1;
        }

        GifColor[] colorTable;
        if (uniqueColors.Count <= maxColors)
        {
            // Use all unique colors
            colorTable = new GifColor[uniqueColors.Count];
            var idx = 0;
            foreach (int rgb in uniqueColors.Keys)
            {
                colorTable[idx++] = new GifColor(
                    (byte)((rgb >> 16) & 0xFF),
                    (byte)((rgb >> 8) & 0xFF),
                    (byte)(rgb & 0xFF));
            }
        }
        else
        {
            // Need to quantize
            colorTable = QuantizeColors(uniqueColors, maxColors);
        }

        // Map pixels to indices
        var indices = new byte[image.Width * image.Height];
        for (var i = 0; i < indices.Length; i++)
        {
            int pixelOffset = i * 4;
            byte b = pixelData[pixelOffset];
            byte g = pixelData[pixelOffset + 1];
            byte r = pixelData[pixelOffset + 2];
            indices[i] = (byte)FindClosestColor(colorTable, r, g, b);
        }

        return (colorTable, indices);
    }

    private static GifColor[] QuantizeColors(Dictionary<int, int> colors, int maxColors)
    {
        // Simple popularity-based quantization
        List<KeyValuePair<int, int>> sorted = colors.OrderByDescending(kv => kv.Value).Take(maxColors).ToList();

        var result = new GifColor[sorted.Count];
        for (var i = 0; i < sorted.Count; i++)
        {
            int rgb = sorted[i].Key;
            result[i] = new GifColor(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF));
        }

        return result;
    }

    private static byte[] QuantizeToColorTable(GifImage image, GifColor[] colorTable)
    {
        var indices = new byte[image.Width * image.Height];
        byte[] pixelData = image.PixelData;

        for (var i = 0; i < indices.Length; i++)
        {
            int pixelOffset = i * 4;
            byte b = pixelData[pixelOffset];
            byte g = pixelData[pixelOffset + 1];
            byte r = pixelData[pixelOffset + 2];
            indices[i] = (byte)FindClosestColor(colorTable, r, g, b);
        }

        return indices;
    }

    private static int FindClosestColor(GifColor[] colorTable, byte r, byte g, byte b)
    {
        var bestIndex = 0;
        var bestDist = int.MaxValue;

        for (var i = 0; i < colorTable.Length; i++)
        {
            int dr = r - colorTable[i].R;
            int dg = g - colorTable[i].G;
            int db = b - colorTable[i].B;
            int dist = dr * dr + dg * dg + db * db;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
                if (dist == 0)
                    break;
            }
        }

        return bestIndex;
    }

    private static int GetColorTableSize(int numColors)
    {
        // Color table size must be a power of 2
        var size = 2;
        while (size < numColors)
            size *= 2;
        return Math.Min(size, 256);
    }

    private static int GetColorBits(int colorTableSize)
    {
        var bits = 1;
        while ((1 << bits) < colorTableSize)
            bits++;
        return bits;
    }

    private static void WriteHeader(Stream stream)
    {
        byte[] header = Encoding.ASCII.GetBytes(GifHeader.Gif89a);
        stream.Write(header, 0, header.Length);
    }

    private static void WriteScreenDescriptor(Stream stream, int width, int height,
        int colorTableSize, int colorBits)
    {
        // Width
        stream.WriteByte((byte)(width & 0xFF));
        stream.WriteByte((byte)((width >> 8) & 0xFF));

        // Height
        stream.WriteByte((byte)(height & 0xFF));
        stream.WriteByte((byte)((height >> 8) & 0xFF));

        // Packed fields:
        // Global Color Table Flag = 1 (bit 7)
        // Color Resolution = colorBits - 1 (bits 4-6)
        // Sort Flag = 0 (bit 3)
        // Size of Global Color Table = colorBits - 1 (bits 0-2)
        var packed = (byte)(0x80 | ((colorBits - 1) << 4) | (colorBits - 1));
        stream.WriteByte(packed);

        // Background color index
        stream.WriteByte(0);

        // Pixel aspect ratio
        stream.WriteByte(0);
    }

    private static void WriteColorTable(Stream stream, GifColor[] colors, int tableSize)
    {
        for (var i = 0; i < tableSize; i++)
        {
            if (i < colors.Length)
            {
                stream.WriteByte(colors[i].R);
                stream.WriteByte(colors[i].G);
                stream.WriteByte(colors[i].B);
            }
            else
            {
                // Pad with black
                stream.WriteByte(0);
                stream.WriteByte(0);
                stream.WriteByte(0);
            }
        }
    }

    private static void WriteNetscapeExtension(Stream stream, int loopCount)
    {
        stream.WriteByte(GifBlockTypes.ExtensionIntroducer);
        stream.WriteByte(GifBlockTypes.ApplicationLabel);
        stream.WriteByte(11); // Block size

        // NETSCAPE2.0
        byte[] netscape = Encoding.ASCII.GetBytes("NETSCAPE2.0");
        stream.Write(netscape, 0, 11);

        // Sub-block
        stream.WriteByte(3); // Sub-block size
        stream.WriteByte(1); // Sub-block ID
        stream.WriteByte((byte)(loopCount & 0xFF));
        stream.WriteByte((byte)((loopCount >> 8) & 0xFF));

        // Block terminator
        stream.WriteByte(0);
    }

    private static void WriteGraphicsControlExtension(Stream stream, int delayMs, int transparentIndex)
    {
        stream.WriteByte(GifBlockTypes.ExtensionIntroducer);
        stream.WriteByte(GifBlockTypes.GraphicsControlLabel);
        stream.WriteByte(4); // Block size

        // Packed fields
        byte packed = 0;
        if (transparentIndex >= 0)
            packed |= 0x01;
        stream.WriteByte(packed);

        // Delay time in centiseconds
        int delayCentiseconds = delayMs / 10;
        stream.WriteByte((byte)(delayCentiseconds & 0xFF));
        stream.WriteByte((byte)((delayCentiseconds >> 8) & 0xFF));

        // Transparent color index
        stream.WriteByte(transparentIndex >= 0 ? (byte)transparentIndex : (byte)0);

        // Block terminator
        stream.WriteByte(0);
    }

    private static void WriteImage(Stream stream, int width, int height, byte[] indices, int colorBits)
    {
        // Image separator
        stream.WriteByte(GifBlockTypes.ImageSeparator);

        // Left position
        stream.WriteByte(0);
        stream.WriteByte(0);

        // Top position
        stream.WriteByte(0);
        stream.WriteByte(0);

        // Width
        stream.WriteByte((byte)(width & 0xFF));
        stream.WriteByte((byte)((width >> 8) & 0xFF));

        // Height
        stream.WriteByte((byte)(height & 0xFF));
        stream.WriteByte((byte)((height >> 8) & 0xFF));

        // Packed fields (no local color table, not interlaced)
        stream.WriteByte(0);

        // LZW minimum code size
        int minCodeSize = Math.Max(2, colorBits);
        stream.WriteByte((byte)minCodeSize);

        // LZW compressed data
        var encoder = new LzwEncoder(minCodeSize);
        byte[] compressed = encoder.Encode(indices);
        stream.Write(compressed, 0, compressed.Length);
    }
}
