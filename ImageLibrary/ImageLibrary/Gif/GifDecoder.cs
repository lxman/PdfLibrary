using System;
using System.IO;
using System.Text;

namespace ImageLibrary.Gif;

/// <summary>
/// Decodes GIF image files.
/// </summary>
public static class GifDecoder
{
    /// <summary>
    /// Decode a GIF file from a byte array.
    /// </summary>
    public static GifFile Decode(byte[] data)
    {
        return Decode(data.AsSpan());
    }

    /// <summary>
    /// Decode a GIF file from a span.
    /// </summary>
    public static GifFile Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            return DecodeInternal(data.ToArray());
        }
        catch (GifException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new GifException($"Invalid data: {ex.Message}", ex);
        }
        catch (OverflowException ex)
        {
            throw new GifException($"Numeric overflow (image too large?): {ex.Message}", ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new GifException($"Data truncated or corrupted: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new GifException($"Failed to decode GIF: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decode a GIF file from a stream.
    /// </summary>
    public static GifFile Decode(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    /// <summary>
    /// Decode a GIF file from a file.
    /// </summary>
    public static GifFile Decode(string path)
    {
        return Decode(File.ReadAllBytes(path));
    }

    private static GifFile DecodeInternal(byte[] data)
    {
        if (data.Length < GifHeader.Size + LogicalScreenDescriptor.Size)
            throw new GifException("Data too small for GIF header");

        var offset = 0;

        // Read header
        GifHeader header = ReadHeader(data, ref offset);
        if (!header.IsValid)
            throw new GifException($"Invalid GIF signature: {header.Signature}{header.Version}");

        // Read logical screen descriptor
        LogicalScreenDescriptor screenDesc = ReadScreenDescriptor(data, ref offset);
        if (screenDesc.Width == 0 || screenDesc.Height == 0)
            throw new GifException("Invalid GIF dimensions");

        // Validate dimensions won't cause overflow (limit to 32K x 32K)
        if (screenDesc.Width > 32768 || screenDesc.Height > 32768)
            throw new GifException($"Image dimensions too large: {screenDesc.Width}x{screenDesc.Height}");

        // Read global color table
        GifColor[]? globalColorTable = null;
        if (screenDesc.HasGlobalColorTable)
        {
            globalColorTable = ReadColorTable(data, ref offset, screenDesc.GlobalColorTableSize);
        }

        var gifFile = new GifFile(screenDesc.Width, screenDesc.Height);

        // Set background color
        if (globalColorTable != null && screenDesc.BackgroundColorIndex < globalColorTable.Length)
        {
            GifColor bg = globalColorTable[screenDesc.BackgroundColorIndex];
            gifFile.BackgroundColor = (bg.B, bg.G, bg.R, 255);
        }

        // Process blocks
        GraphicsControlExtension? graphicsControl = null;
        var iterations = 0;
        const int maxIterations = 10000;

        while (offset < data.Length)
        {
            if (++iterations > maxIterations)
                throw new GifException("GIF decode exceeded maximum block iterations");

            byte blockType = data[offset++];

            switch (blockType)
            {
                case GifBlockTypes.Trailer:
                    return gifFile;

                case GifBlockTypes.ExtensionIntroducer:
                    if (offset >= data.Length)
                        throw new GifException("Unexpected end of data in extension");

                    byte extensionLabel = data[offset++];
                    switch (extensionLabel)
                    {
                        case GifBlockTypes.GraphicsControlLabel:
                            graphicsControl = ReadGraphicsControlExtension(data, ref offset);
                            break;
                        case GifBlockTypes.ApplicationLabel:
                            ReadApplicationExtension(data, ref offset, gifFile);
                            break;
                        default:
                            SkipSubBlocks(data, ref offset);
                            break;
                    }
                    break;

                case GifBlockTypes.ImageSeparator:
                    GifImage frame = DecodeFrame(data, ref offset, screenDesc,
                        globalColorTable, graphicsControl);
                    gifFile.Frames.Add(frame);
                    graphicsControl = null;
                    break;
            }
        }

        return gifFile;
    }

    /// <summary>
    /// Decode just the first frame as a simple GifImage.
    /// </summary>
    public static GifImage DecodeFirstFrame(byte[] data)
    {
        GifFile gifFile = Decode(data);
        if (gifFile.Frames.Count == 0)
            throw new GifException("GIF file contains no frames");
        return gifFile.Frames[0];
    }

    private static GifHeader ReadHeader(byte[] data, ref int offset)
    {
        string signature = Encoding.ASCII.GetString(data, offset, 3);
        string version = Encoding.ASCII.GetString(data, offset + 3, 3);
        offset += GifHeader.Size;
        return new GifHeader(signature, version);
    }

    private static LogicalScreenDescriptor ReadScreenDescriptor(byte[] data, ref int offset)
    {
        ushort width = ReadUInt16(data, ref offset);
        ushort height = ReadUInt16(data, ref offset);
        byte packed = data[offset++];
        byte bgIndex = data[offset++];
        byte aspectRatio = data[offset++];
        return new LogicalScreenDescriptor(width, height, packed, bgIndex, aspectRatio);
    }

    private static GifColor[] ReadColorTable(byte[] data, ref int offset, int count)
    {
        var colors = new GifColor[count];
        for (var i = 0; i < count; i++)
        {
            if (offset + 3 > data.Length)
                throw new GifException("Unexpected end of data in color table");
            colors[i] = new GifColor(data[offset], data[offset + 1], data[offset + 2]);
            offset += 3;
        }
        return colors;
    }

    private static GraphicsControlExtension ReadGraphicsControlExtension(byte[] data, ref int offset)
    {
        if (offset + 6 > data.Length)
            throw new GifException("Unexpected end of data in graphics control extension");

        byte blockSize = data[offset++];
        if (blockSize != 4)
            throw new GifException($"Invalid graphics control block size: {blockSize}");

        byte packed = data[offset++];
        ushort delay = ReadUInt16(data, ref offset);
        byte transparentIndex = data[offset++];
        byte terminator = data[offset++];

        return new GraphicsControlExtension(packed, delay, transparentIndex);
    }

    private static void ReadApplicationExtension(byte[] data, ref int offset, GifFile gifFile)
    {
        if (offset >= data.Length)
            throw new GifException("Unexpected end of data in application extension");

        byte blockSize = data[offset++];
        if (blockSize != 11 || offset + 11 > data.Length)
        {
            offset--;
            SkipSubBlocks(data, ref offset);
            return;
        }

        string appId = Encoding.ASCII.GetString(data, offset, 8);
        string authCode = Encoding.ASCII.GetString(data, offset + 8, 3);
        offset += 11;

        // Check for NETSCAPE extension (animation looping)
        if (appId == "NETSCAPE" && authCode == "2.0")
        {
            if (offset + 4 <= data.Length && data[offset] == 3)
            {
                offset++; // Sub-block size
                offset++; // Sub-block ID (always 1)
                gifFile.LoopCount = ReadUInt16(data, ref offset);
            }
        }

        SkipSubBlocks(data, ref offset);
    }

    private static void SkipSubBlocks(byte[] data, ref int offset)
    {
        while (offset < data.Length)
        {
            byte size = data[offset++];
            if (size == 0)
                break;
            offset += size;
            if (offset > data.Length)
                throw new GifException("Sub-block extends past data end");
        }
    }

    private static GifImage DecodeFrame(byte[] data, ref int offset,
        LogicalScreenDescriptor screenDesc, GifColor[]? globalColorTable,
        GraphicsControlExtension? graphicsControl)
    {
        if (offset + ImageDescriptor.Size > data.Length)
            throw new GifException("Unexpected end of data in image descriptor");

        // Read image descriptor
        ImageDescriptor imageDesc = ReadImageDescriptor(data, ref offset);

        // Validate frame dimensions
        if (imageDesc.Width == 0 || imageDesc.Height == 0)
            throw new GifException("Invalid frame dimensions");
        if (imageDesc.Width > 32768 || imageDesc.Height > 32768)
            throw new GifException($"Frame dimensions too large: {imageDesc.Width}x{imageDesc.Height}");

        // Read local color table if present
        GifColor[] colorTable;
        if (imageDesc.HasLocalColorTable)
        {
            colorTable = ReadColorTable(data, ref offset, imageDesc.LocalColorTableSize);
        }
        else if (globalColorTable != null)
        {
            colorTable = globalColorTable;
        }
        else
        {
            throw new GifException("No color table available for frame");
        }

        // Read LZW minimum code size
        if (offset >= data.Length)
            throw new GifException("Unexpected end of data before LZW data");
        int minCodeSize = data[offset++];

        // Find end of image data (series of sub-blocks ending with 0)
        int imageDataStart = offset;
        int imageDataEnd = imageDataStart;
        while (imageDataEnd < data.Length)
        {
            byte subBlockSize = data[imageDataEnd++];
            if (subBlockSize == 0)
                break;
            imageDataEnd += subBlockSize;
        }

        // Decode LZW data
        int imageDataLength = imageDataEnd - imageDataStart;
        var lzwDecoder = new LzwDecoder(data, imageDataStart, imageDataLength, minCodeSize);
        byte[] indices = lzwDecoder.Decode(imageDesc.Width * imageDesc.Height);

        offset = imageDataEnd;

        // Convert to BGRA
        int width = imageDesc.Width;
        int height = imageDesc.Height;
        var pixelData = new byte[width * height * 4];

        int transparentIndex = graphicsControl?.HasTransparency == true
            ? graphicsControl.Value.TransparentColorIndex
            : -1;

        if (imageDesc.IsInterlaced)
        {
            DecodeInterlaced(indices, pixelData, width, height, colorTable, transparentIndex);
        }
        else
        {
            DecodeSequential(indices, pixelData, width, height, colorTable, transparentIndex);
        }

        var frame = new GifImage(width, height, pixelData);

        if (graphicsControl.HasValue)
        {
            // Delay is in centiseconds (1/100s), convert to milliseconds
            frame.DelayMs = graphicsControl.Value.DelayTime * 10;
        }

        return frame;
    }

    private static ImageDescriptor ReadImageDescriptor(byte[] data, ref int offset)
    {
        ushort left = ReadUInt16(data, ref offset);
        ushort top = ReadUInt16(data, ref offset);
        ushort width = ReadUInt16(data, ref offset);
        ushort height = ReadUInt16(data, ref offset);
        byte packed = data[offset++];
        return new ImageDescriptor(left, top, width, height, packed);
    }

    private static void DecodeSequential(byte[] indices, byte[] pixelData,
        int width, int height, GifColor[] colorTable, int transparentIndex)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int srcIndex = y * width + x;
                int destOffset = srcIndex * 4;

                if (srcIndex >= indices.Length)
                    break;

                int colorIndex = indices[srcIndex];

                if (colorIndex == transparentIndex)
                {
                    pixelData[destOffset] = 0;
                    pixelData[destOffset + 1] = 0;
                    pixelData[destOffset + 2] = 0;
                    pixelData[destOffset + 3] = 0;
                }
                else if (colorIndex < colorTable.Length)
                {
                    GifColor color = colorTable[colorIndex];
                    pixelData[destOffset] = color.B;
                    pixelData[destOffset + 1] = color.G;
                    pixelData[destOffset + 2] = color.R;
                    pixelData[destOffset + 3] = 255;
                }
            }
        }
    }

    private static void DecodeInterlaced(byte[] indices, byte[] pixelData,
        int width, int height, GifColor[] colorTable, int transparentIndex)
    {
        // Interlace passes: rows 0, 8, 16... then 4, 12, 20... then 2, 6, 10... then 1, 3, 5...
        int[] startRows = [0, 4, 2, 1];
        int[] increments = [8, 8, 4, 2];

        var srcIndex = 0;
        for (var pass = 0; pass < 4; pass++)
        {
            for (int y = startRows[pass]; y < height; y += increments[pass])
            {
                for (var x = 0; x < width; x++)
                {
                    if (srcIndex >= indices.Length)
                        return;

                    int destOffset = (y * width + x) * 4;
                    int colorIndex = indices[srcIndex++];

                    if (colorIndex == transparentIndex)
                    {
                        pixelData[destOffset] = 0;
                        pixelData[destOffset + 1] = 0;
                        pixelData[destOffset + 2] = 0;
                        pixelData[destOffset + 3] = 0;
                    }
                    else if (colorIndex < colorTable.Length)
                    {
                        GifColor color = colorTable[colorIndex];
                        pixelData[destOffset] = color.B;
                        pixelData[destOffset + 1] = color.G;
                        pixelData[destOffset + 2] = color.R;
                        pixelData[destOffset + 3] = 255;
                    }
                }
            }
        }
    }

    private static ushort ReadUInt16(byte[] data, ref int offset)
    {
        var value = (ushort)(data[offset] | (data[offset + 1] << 8));
        offset += 2;
        return value;
    }
}
