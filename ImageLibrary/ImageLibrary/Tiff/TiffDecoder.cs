using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ImageLibrary.Ccitt;
using ImageLibrary.Jpeg;
using ImageLibrary.Lzw;

namespace ImageLibrary.Tiff;

/// <summary>
/// Decodes TIFF (Tagged Image File Format) images from byte arrays, streams, or files.
/// </summary>
public static class TiffDecoder
{
    /// <summary>
    /// Decodes a TIFF image from a byte array.
    /// </summary>
    /// <param name="data">The TIFF file data.</param>
    /// <returns>The decoded TIFF image.</returns>
    /// <exception cref="TiffException">Thrown when the TIFF data is invalid or unsupported.</exception>
    public static TiffImage Decode(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        using var stream = new MemoryStream(data);
        return Decode(stream);
    }

    /// <summary>
    /// Decodes a TIFF image from a stream.
    /// </summary>
    /// <param name="stream">The stream containing TIFF data.</param>
    /// <returns>The decoded TIFF image.</returns>
    /// <exception cref="TiffException">Thrown when the TIFF data is invalid or unsupported.</exception>
    public static TiffImage Decode(Stream stream)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return DecodeInternal(reader);
    }

    /// <summary>
    /// Decodes a TIFF image from a file.
    /// </summary>
    /// <param name="path">The path to the TIFF file.</param>
    /// <returns>The decoded TIFF image.</returns>
    /// <exception cref="TiffException">Thrown when the TIFF data is invalid or unsupported.</exception>
    public static TiffImage Decode(string path)
    {
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        using FileStream stream = File.OpenRead(path);
        return Decode(stream);
    }

    private static TiffImage DecodeInternal(BinaryReader reader)
    {
        // Read byte order marker
        byte[] byteOrder = reader.ReadBytes(2);
        bool littleEndian = byteOrder[0] switch
        {
            0x49 when byteOrder[1] == 0x49 => true,
            0x4D when byteOrder[1] == 0x4D => false,
            _ => throw new TiffException($"Invalid TIFF byte order marker: 0x{byteOrder[0]:X2}{byteOrder[1]:X2}")
        };

        // Read the magic number (42 for TIFF)
        ushort magic = ReadUInt16(reader, littleEndian);
        if (magic != 42)
        {
            throw new TiffException($"Invalid TIFF magic number: {magic} (expected 42)");
        }

        // Read the first IFD offset
        uint ifdOffset = ReadUInt32(reader, littleEndian);

        // Parse IFD and decode image
        return DecodeIfd(reader, ifdOffset, littleEndian);
    }

    private static TiffImage DecodeIfd(BinaryReader reader, uint ifdOffset, bool littleEndian)
    {
        reader.BaseStream.Seek(ifdOffset, SeekOrigin.Begin);

        // Read the number of directory entries
        ushort entryCount = ReadUInt16(reader, littleEndian);

        // Read all IFD entries
        var tags = new Dictionary<TiffTag, object>();
        for (var i = 0; i < entryCount; i++)
        {
            ushort tagId = ReadUInt16(reader, littleEndian);
            ushort fieldType = ReadUInt16(reader, littleEndian);
            uint count = ReadUInt32(reader, littleEndian);
            uint valueOffset = ReadUInt32(reader, littleEndian);

            object value = ReadTagValue(reader, fieldType, count, valueOffset, littleEndian);
            tags[(TiffTag)tagId] = value;
        }

        // Extract required fields
        if (!tags.ContainsKey(TiffTag.ImageWidth))
            throw new TiffException("Missing required ImageWidth tag");
        if (!tags.ContainsKey(TiffTag.ImageHeight))
            throw new TiffException("Missing required ImageHeight tag");

        var width = Convert.ToInt32(tags[TiffTag.ImageWidth]);
        var height = Convert.ToInt32(tags[TiffTag.ImageHeight]);

        // Extract compression (default: uncompressed)
        TiffCompression compression = tags.TryGetValue(TiffTag.Compression, out object? tag)
            ? (TiffCompression)Convert.ToUInt16(tag)
            : TiffCompression.None;

        // Extract photometric interpretation (default: black is zero)
        TiffPhotometricInterpretation photometric = tags.TryGetValue(TiffTag.PhotometricInterpretation, out object? tag1)
            ? (TiffPhotometricInterpretation)Convert.ToUInt16(tag1)
            : TiffPhotometricInterpretation.BlackIsZero;

        // Extract samples per pixel (default: 1)
        int samplesPerPixel = tags.TryGetValue(TiffTag.SamplesPerPixel, out object? tag2)
            ? Convert.ToInt32(tag2)
            : 1;

        // Extract planar configuration (default: 1 = chunky)
        int planarConfiguration = tags.TryGetValue(TiffTag.PlanarConfiguration, out object? tag3)
            ? Convert.ToInt32(tag3)
            : 1;

        // Extract bits per sample (default: 1)
        int[] bitsPerSample;
        if (tags.TryGetValue(TiffTag.BitsPerSample, out object? bpsValue))
        {
            bitsPerSample = bpsValue switch
            {
                int[] arr => arr,
                ushort[] arr2 => arr2.Select(x => (int)x).ToArray(),
                _ => [Convert.ToInt32(bpsValue)]
            };
        }
        else
        {
            bitsPerSample = [1];
        }

        // Get strip/tile data (decompressed)
        byte[] decompressedData;
        if (tags.ContainsKey(TiffTag.StripOffsets))
        {
            decompressedData = ReadStripData(reader, tags, littleEndian, compression, width, height);
        }
        else if (tags.ContainsKey(TiffTag.TileOffsets))
        {
            decompressedData = ReadTileData(reader, tags, littleEndian, width, height, compression);
        }
        else
        {
            throw new TiffException("Missing StripOffsets or TileOffsets tag");
        }

        // Convert to BGRA format
        // Note: JPEG decompression already produces BGRA data, so skip conversion for JPEG
        byte[] pixelData;
        if (compression == TiffCompression.Jpeg || compression == TiffCompression.OldJpeg)
        {
            pixelData = decompressedData; // Already in BGRA format from DecompressJpeg
        }
        else
        {
            pixelData = ConvertToBgra(decompressedData, width, height, photometric, samplesPerPixel, bitsPerSample, planarConfiguration, littleEndian);
        }

        // Apply aspect ratio correction for images with non-square pixels
        double xResolution = ReadRational(reader, tags, TiffTag.XResolution, littleEndian);
        double yResolution = ReadRational(reader, tags, TiffTag.YResolution, littleEndian);

        int outputWidth = width;
        int outputHeight = height;

        // If resolutions differ significantly, scale to correct the aspect ratio
        if (!(xResolution > 0) || !(yResolution > 0)) return new TiffImage(outputWidth, outputHeight, pixelData);
        double ratio = xResolution / yResolution;

        // Only apply correction if the difference is > 5% (to avoid rounding errors for square pixels)
        if (!(Math.Abs(ratio - 1.0) > 0.05)) return new TiffImage(outputWidth, outputHeight, pixelData);
        outputHeight = (int)Math.Round(height * ratio);
        pixelData = ScaleImageVertical(pixelData, width, height, outputHeight);

        return new TiffImage(outputWidth, outputHeight, pixelData);
    }

    private static byte[] ReadStripData(BinaryReader reader, Dictionary<TiffTag, object> tags, bool littleEndian, TiffCompression compression, int width, int height)
    {
        object offsetsObj = tags[TiffTag.StripOffsets];
        object byteCountsObj = tags[TiffTag.StripByteCounts];

        uint[] offsets = ConvertToUInt32Array(offsetsObj);
        uint[] byteCounts = ConvertToUInt32Array(byteCountsObj);

        if (offsets.Length != byteCounts.Length)
            throw new TiffException($"StripOffsets count ({offsets.Length}) != StripByteCounts count ({byteCounts.Length})");

        // Check if this is a tiled image stored with StripOffsets (hybrid format)
        // If TileWidth and TileLength are present, treat strips as tiles and arrange spatially
        bool hasTileDimensions = tags.ContainsKey(TiffTag.TileWidth) && tags.ContainsKey(TiffTag.TileLength);
        if (hasTileDimensions)
        {
            return ReadStripDataAsTiles(reader, tags, offsets, byteCounts, compression, width, height);
        }

        // For uncompressed data, we can concatenate directly
        if (compression == TiffCompression.None)
        {
            using var output = new MemoryStream();
            for (var i = 0; i < offsets.Length; i++)
            {
                reader.BaseStream.Seek(offsets[i], SeekOrigin.Begin);
                byte[] stripData = reader.ReadBytes((int)byteCounts[i]);
                output.Write(stripData, 0, stripData.Length);
            }
            return output.ToArray();
        }

        // Get RowsPerStrip to calculate strip height (default to full image height if not specified)
        // Note: TIFF spec allows RowsPerStrip = 2^32-1 (0xFFFFFFFF) to indicate single strip
        int rowsPerStrip = height;
        if (tags.TryGetValue(TiffTag.RowsPerStrip, out object? rowsPerStripObj))
        {
            uint rowsPerStripValue = rowsPerStripObj is uint u ? u : Convert.ToUInt32(rowsPerStripObj);
            // Use full height if the value is 2^32-1 or larger than height
            rowsPerStrip = (rowsPerStripValue == 0xFFFFFFFF || rowsPerStripValue >= (uint)height)
                ? height
                : (int)rowsPerStripValue;
        }

        // For compressed data, decompress each strip independently then concatenate
        using (var output = new MemoryStream())
        {
            for (var i = 0; i < offsets.Length; i++)
            {
                reader.BaseStream.Seek(offsets[i], SeekOrigin.Begin);
                byte[] compressedStrip = reader.ReadBytes((int)byteCounts[i]);

                // Calculate actual strip height (the last strip may be shorter)
                int stripHeight = Math.Min(rowsPerStrip, height - (i * rowsPerStrip));

                byte[] decompressedStrip = DecompressData(compressedStrip, compression, width, stripHeight, tags);
                output.Write(decompressedStrip, 0, decompressedStrip.Length);
            }
            return output.ToArray();
        }
    }

    private static byte[] ReadStripDataAsTiles(BinaryReader reader, Dictionary<TiffTag, object> tags, uint[] offsets, uint[] byteCounts, TiffCompression compression, int width, int height)
    {
        var tileWidth = Convert.ToInt32(tags[TiffTag.TileWidth]);
        var tileHeight = Convert.ToInt32(tags[TiffTag.TileLength]);

        // Get samples per pixel and bits per sample to calculate bytes per pixel
        int samplesPerPixel = tags.TryGetValue(TiffTag.SamplesPerPixel, out object? sppObj)
            ? Convert.ToInt32(sppObj)
            : 1;

        int[] bitsPerSample;
        if (tags.TryGetValue(TiffTag.BitsPerSample, out object? bpsValue))
        {
            bitsPerSample = bpsValue switch
            {
                int[] arr => arr,
                ushort[] arr2 => arr2.Select(x => (int)x).ToArray(),
                _ => [Convert.ToInt32(bpsValue)]
            };
        }
        else
        {
            bitsPerSample = [1];
        }

        int bytesPerPixel = (bitsPerSample[0] * samplesPerPixel + 7) / 8;

        // Calculate tile layout
        int tilesAcross = (width + tileWidth - 1) / tileWidth;
        int tilesDown = (height + tileHeight - 1) / tileHeight;

        // Allocate output buffer for entire image
        var output = new byte[width * height * bytesPerPixel];

        // Process each strip as a tile and place it at the correct position
        for (var stripIndex = 0; stripIndex < offsets.Length; stripIndex++)
        {
            // Calculate tile position in the grid (row-major order: across first, then down)
            int tileX = stripIndex % tilesAcross;
            int tileY = stripIndex / tilesAcross;

            // Calculate pixel position in the image
            int pixelX = tileX * tileWidth;
            int pixelY = tileY * tileHeight;

            // Read and decompress strip/tile
            reader.BaseStream.Seek(offsets[stripIndex], SeekOrigin.Begin);
            byte[] compressedStrip = reader.ReadBytes((int)byteCounts[stripIndex]);
            byte[] decompressedStrip = compression == TiffCompression.None
                ? compressedStrip
                : DecompressData(compressedStrip, compression, tileWidth, tileHeight, tags);

            // Calculate actual tile dimensions (edge tiles may be smaller)
            int actualTileWidth = Math.Min(tileWidth, width - pixelX);
            int actualTileHeight = Math.Min(tileHeight, height - pixelY);

            // Copy tile data to correct position in output buffer
            for (var y = 0; y < actualTileHeight; y++)
            {
                int srcOffset = y * tileWidth * bytesPerPixel;
                int dstOffset = ((pixelY + y) * width + pixelX) * bytesPerPixel;
                int bytesToCopy = actualTileWidth * bytesPerPixel;

                Array.Copy(decompressedStrip, srcOffset, output, dstOffset, bytesToCopy);
            }
        }

        return output;
    }

    private static byte[] ReadTileData(BinaryReader reader, Dictionary<TiffTag, object> tags, bool littleEndian, int width, int height, TiffCompression compression)
    {
        object offsetsObj = tags[TiffTag.TileOffsets];
        object byteCountsObj = tags[TiffTag.TileByteCounts];
        var tileWidth = Convert.ToInt32(tags[TiffTag.TileWidth]);
        var tileHeight = Convert.ToInt32(tags[TiffTag.TileLength]);

        uint[] offsets = ConvertToUInt32Array(offsetsObj);
        uint[] byteCounts = ConvertToUInt32Array(byteCountsObj);

        // Get samples per pixel and bits per sample to calculate bytes per pixel
        int samplesPerPixel = tags.TryGetValue(TiffTag.SamplesPerPixel, out object? sppObj)
            ? Convert.ToInt32(sppObj)
            : 1;

        int[] bitsPerSample;
        if (tags.TryGetValue(TiffTag.BitsPerSample, out object? bpsValue))
        {
            bitsPerSample = bpsValue switch
            {
                int[] arr => arr,
                ushort[] arr2 => arr2.Select(x => (int)x).ToArray(),
                _ => [Convert.ToInt32(bpsValue)]
            };
        }
        else
        {
            bitsPerSample = [1];
        }

        int bytesPerPixel = (bitsPerSample[0] * samplesPerPixel + 7) / 8;

        // Calculate tile layout
        int tilesAcross = (width + tileWidth - 1) / tileWidth;
        int tilesDown = (height + tileHeight - 1) / tileHeight;

        // Allocate output buffer for entire image
        var output = new byte[width * height * bytesPerPixel];

        // Process each tile and place it at the correct position
        for (var tileIndex = 0; tileIndex < offsets.Length; tileIndex++)
        {
            // Calculate tile position in the grid (column-major order: down first, then across)
            int tileX = tileIndex / tilesDown;
            int tileY = tileIndex % tilesDown;

            // Calculate pixel position in the image
            int pixelX = tileX * tileWidth;
            int pixelY = tileY * tileHeight;

            // Read and decompress tile
            reader.BaseStream.Seek(offsets[tileIndex], SeekOrigin.Begin);
            byte[] compressedTile = reader.ReadBytes((int)byteCounts[tileIndex]);
            byte[] decompressedTile = compression == TiffCompression.None
                ? compressedTile
                : DecompressData(compressedTile, compression, tileWidth, tileHeight, tags);

            // Debug logging for tiles
            if (tileIndex < 4)
            {
                Console.WriteLine($"Tile {tileIndex}: compressed={compressedTile.Length} bytes, " +
                                $"decompressed={decompressedTile.Length} bytes, " +
                                $"expected={tileWidth * tileHeight * bytesPerPixel} bytes");
                Console.Write($"  First 16 decompressed bytes: ");
                for (var i = 0; i < Math.Min(16, decompressedTile.Length); i++)
                    Console.Write($"{decompressedTile[i]:X2} ");
                Console.WriteLine();
            }

            // Calculate actual tile dimensions (edge tiles may be smaller)
            int actualTileWidth = Math.Min(tileWidth, width - pixelX);
            int actualTileHeight = Math.Min(tileHeight, height - pixelY);

            // Copy tile data to correct position in output buffer
            for (var y = 0; y < actualTileHeight; y++)
            {
                int srcOffset = y * tileWidth * bytesPerPixel;
                int dstOffset = ((pixelY + y) * width + pixelX) * bytesPerPixel;
                int bytesToCopy = actualTileWidth * bytesPerPixel;

                Array.Copy(decompressedTile, srcOffset, output, dstOffset, bytesToCopy);
            }
        }

        return output;
    }

    private static byte[] DecompressData(byte[] data, TiffCompression compression, int width, int height, Dictionary<TiffTag, object> tags)
    {
        return compression switch
        {
            TiffCompression.None => data,
            TiffCompression.CcittRle or TiffCompression.CcittGroup3 or TiffCompression.CcittGroup4 => DecompressCcitt(
                data, compression, width, height, tags),
            TiffCompression.Lzw => DecompressLzw(data, tags),
            TiffCompression.AdobeDeflate or TiffCompression.Deflate => DecompressDeflate(data, width, height, tags),
            TiffCompression.PackBits => DecompressPackBits(data),
            TiffCompression.Jpeg or TiffCompression.OldJpeg => DecompressJpeg(data, width, height, tags),
            _ => throw new TiffException($"Unsupported TIFF compression: {compression}")
        };
    }

    private static byte[] DecompressCcitt(byte[] data, TiffCompression compression, int width, int height, Dictionary<TiffTag, object> tags)
    {
        uint t4Options = tags.TryGetValue(TiffTag.T4Options, out object? tag) ? Convert.ToUInt32(tag) : 0;

        // Read FillOrder tag (266) to determine bit packing order
        // FillOrder=1 (default): MSB-first
        // FillOrder=2: LSB-first
        var fillOrder = 1; // Default to MSB-first
        if (tags.TryGetValue(TiffTag.FillOrder, out object? fillOrderTag))
        {
            fillOrder = Convert.ToInt32(fillOrderTag);
        }

        // If FillOrder=2, reverse bits in each byte before decompressing
        byte[] processedData = data;
        if (fillOrder == 2)
        {
            processedData = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                processedData[i] = ReverseBits(data[i]);
            }
        }

        // Read PhotometricInterpretation to determine BlackIs1 setting
        // WhiteIsZero (0): 0=white, 1=black → BlackIs1 = true
        // BlackIsZero (1): 0=black, 1=white → BlackIs1 = false
        TiffPhotometricInterpretation photometric = tags.TryGetValue(TiffTag.PhotometricInterpretation, out object? photometricTag)
            ? (TiffPhotometricInterpretation)Convert.ToUInt16(photometricTag)
            : TiffPhotometricInterpretation.WhiteIsZero; // CCITT default

        bool blackIs1 = photometric == TiffPhotometricInterpretation.WhiteIsZero;

        var options = new CcittOptions
        {
            Width = width,
            Height = height,
            BlackIs1 = blackIs1
        };

        // Set the group based on the compression type
        switch (compression)
        {
            case TiffCompression.CcittRle:
            case TiffCompression.CcittGroup3:
                // Check the T4Options tag to determine if 2D encoding is used (bit 0)
                bool use2D = (t4Options & 0x1) != 0;  // Bit 0: 0=1D encoding, 1=2D encoding

                if (use2D)
                {
                    options.Group = CcittGroup.Group3TwoDimensional;
                    options.K = 2;  // K=2 for mixed 1D/2D encoding (typical for Group 3 2D)
                }
                else
                {
                    options.Group = CcittGroup.Group3OneDimensional;
                    options.K = 0;
                }

                options.EndOfLine = true;
                options.EndOfBlock = false;  // Explicitly disable for Group 3
                break;

            case TiffCompression.CcittGroup4:
                options.Group = CcittGroup.Group4;
                options.K = -1;
                options.EndOfLine = false;   // Group 4 doesn't use EOL
                options.EndOfBlock = true;
                break;
            case TiffCompression.None:
            case TiffCompression.Lzw:
            case TiffCompression.OldJpeg:
            case TiffCompression.Jpeg:
            case TiffCompression.AdobeDeflate:
            case TiffCompression.PackBits:
            case TiffCompression.Deflate:
            default:
                throw new ArgumentOutOfRangeException(nameof(compression), compression, null);
        }

        return Ccitt.Ccitt.Decompress(processedData, options);
    }

    private static byte ReverseBits(byte b)
    {
        // Reverse the bit order within a byte
        // Example: 0b10110010 -> 0b01001101
        b = (byte)((b & 0xF0) >> 4 | (b & 0x0F) << 4);
        b = (byte)((b & 0xCC) >> 2 | (b & 0x33) << 2);
        b = (byte)((b & 0xAA) >> 1 | (b & 0x55) << 1);
        return b;
    }

    private static byte[] DecompressLzw(byte[] data, Dictionary<TiffTag, object> tags)
    {
        // Read FillOrder tag (266) to determine bit packing order
        // FillOrder=1 (default): MSB-first
        // FillOrder=2: LSB-first
        var fillOrder = 1; // Default to MSB-first
        if (tags.TryGetValue(TiffTag.FillOrder, out object? tag))
        {
            fillOrder = Convert.ToInt32(tag);
        }

        // Try the specified bit order first with EarlyChange=true (TIFF 6.0 standard)
        LzwBitOrder primaryBitOrder = fillOrder == 2 ? LzwBitOrder.LsbFirst : LzwBitOrder.MsbFirst;
        LzwBitOrder fallbackBitOrder = fillOrder == 2 ? LzwBitOrder.MsbFirst : LzwBitOrder.LsbFirst;

        // Try different combinations of bit order and early change settings
        (LzwBitOrder, bool)[] attempts =
        [
            (primaryBitOrder, true),   // Primary bit order with EarlyChange=true (TIFF 6.0)
            (fallbackBitOrder, true),  // Opposite bit order with EarlyChange=true
            (primaryBitOrder, false),  // Primary bit order with EarlyChange=false
            (fallbackBitOrder, false)  // Opposite bit order with EarlyChange=false
        ];

        Exception? lastException = null;
        foreach ((LzwBitOrder bitOrder, bool earlyChange) in attempts)
        {
            try
            {
                var options = new LzwOptions
                {
                    BitOrder = bitOrder,
                    EarlyChange = earlyChange,
                    EmitInitialClearCode = false
                };
                return Lzw.Lzw.Decompress(data, options);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        // All attempts failed, throw the last exception
        throw lastException ?? new InvalidDataException("Failed to decompress LZW data");
    }

    private static byte[] DecompressDeflate(byte[] data, int width, int height, Dictionary<TiffTag, object> tags)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        // Skip zlib header (2 bytes) for raw DEFLATE
        input.Seek(2, SeekOrigin.Begin);

        using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
        {
            deflate.CopyTo(output);
        }

        byte[] decompressed = output.ToArray();

        // Apply predictor if present
        if (!tags.TryGetValue(TiffTag.Predictor, out object? tag)) return decompressed;
        var predictor = Convert.ToInt32(tag);
        if (predictor == 2) // Horizontal differencing
        {
            decompressed = ApplyHorizontalDifferencing(decompressed, width, height);
        }

        return decompressed;
    }

    private static byte[] DecompressPackBits(byte[] data)
    {
        using var output = new MemoryStream();
        var i = 0;
        while (i < data.Length)
        {
            var n = (sbyte)data[i++];

            if (n >= 0)
            {
                // Copy next n+1 bytes literally
                int count = n + 1;
                for (var j = 0; j < count && i < data.Length; j++)
                {
                    output.WriteByte(data[i++]);
                }
            }
            else if (n != -128)
            {
                // Replicate next byte -n+1 times
                int count = -n + 1;
                if (i >= data.Length) continue;
                byte value = data[i++];
                for (var j = 0; j < count; j++)
                {
                    output.WriteByte(value);
                }
            }
            // n == -128 is a no-op
        }
        return output.ToArray();
    }

    private static byte[] DecompressJpeg(byte[] data, int width, int height, Dictionary<TiffTag, object> tags)
    {
        // TIFF JPEG (Technical Note #2) can store JPEG tables separately in JPEGTables tag (347)
        // If present, we need to combine the tables with the abbreviated strip/tile data
        byte[] jpegData = data;

        if (tags.TryGetValue(TiffTag.JpegTables, out object? jpegTablesObj))
        {
            byte[] jpegTables = (byte[])jpegTablesObj;

            // JPEGTables contains: SOI (0xFFD8) + DHT/DQT/... + EOI (0xFFD9)
            // Strip data contains: SOI (0xFFD8) + SOS + compressed data + EOI (0xFFD9)
            // Combine as: SOI + (tables without SOI/EOI) + (strip data without SOI/EOI) + EOI

            // Extract tables (skip first 2 bytes SOI and last 2 bytes EOI)
            int tablesLength = jpegTables.Length - 4; // Remove SOI and EOI
            byte[] tablesContent = new byte[tablesLength];
            Array.Copy(jpegTables, 2, tablesContent, 0, tablesLength);

            // Extract strip data (skip first 2 bytes SOI and last 2 bytes EOI)
            int stripLength = data.Length - 4;
            byte[] stripContent = new byte[stripLength];
            Array.Copy(data, 2, stripContent, 0, stripLength);

            // Combine: SOI + tables + strip content + EOI
            jpegData = new byte[2 + tablesLength + stripLength + 2];
            jpegData[0] = 0xFF; // SOI
            jpegData[1] = 0xD8;
            Array.Copy(tablesContent, 0, jpegData, 2, tablesLength);
            Array.Copy(stripContent, 0, jpegData, 2 + tablesLength, stripLength);
            jpegData[jpegData.Length - 2] = 0xFF; // EOI
            jpegData[jpegData.Length - 1] = 0xD9;
        }

        // Decode JPEG data using the ImageLibrary JPEG decoder
        var decoder = new JpegDecoder(jpegData);
        var decodedImage = decoder.Decode();

        // Verify dimensions match expected tile/strip dimensions
        if (decodedImage.Width != width || decodedImage.Height != height)
        {
            throw new TiffException($"JPEG dimensions ({decodedImage.Width}x{decodedImage.Height}) do not match expected dimensions ({width}x{height})");
        }

        // Convert RGB (3 bytes/pixel) to BGRA (4 bytes/pixel)
        // JPEG decoder outputs: R, G, B, R, G, B, ...
        // TIFF expects: B, G, R, A, B, G, R, A, ...
        int pixelCount = width * height;
        byte[] bgra = new byte[pixelCount * 4];

        for (int i = 0; i < pixelCount; i++)
        {
            int rgbOffset = i * 3;
            int bgraOffset = i * 4;

            byte r = decodedImage.RgbData[rgbOffset];
            byte g = decodedImage.RgbData[rgbOffset + 1];
            byte b = decodedImage.RgbData[rgbOffset + 2];

            bgra[bgraOffset] = b;     // Blue
            bgra[bgraOffset + 1] = g; // Green
            bgra[bgraOffset + 2] = r; // Red
            bgra[bgraOffset + 3] = 255; // Alpha (opaque)
        }

        return bgra;
    }

    private static byte[] ApplyHorizontalDifferencing(byte[] data, int width, int height)
    {
        // TIFF Predictor 2: horizontal differencing (similar to PNG Sub filter)
        var result = new byte[data.Length];
        int bytesPerRow = data.Length / height;

        for (var row = 0; row < height; row++)
        {
            int rowOffset = row * bytesPerRow;
            result[rowOffset] = data[rowOffset]; // First pixel unchanged

            for (var col = 1; col < bytesPerRow; col++)
            {
                result[rowOffset + col] = (byte)(data[rowOffset + col] + result[rowOffset + col - 1]);
            }
        }

        return result;
    }

    private static byte[] ConvertToBgra(byte[] data, int width, int height, TiffPhotometricInterpretation photometric,
        int samplesPerPixel, int[] bitsPerSample, int planarConfiguration, bool littleEndian)
    {
        var pixelData = new byte[width * height * 4];

        switch (photometric)
        {
            case TiffPhotometricInterpretation.WhiteIsZero when bitsPerSample[0] == 1:
                // 1-bit bi-level image (white is zero)
                ConvertBilevelToBgra(data, pixelData, width, height, invertColors: true);
                break;
            case TiffPhotometricInterpretation.BlackIsZero when bitsPerSample[0] == 1:
                // 1-bit bi-level image (black is zero)
                ConvertBilevelToBgra(data, pixelData, width, height, invertColors: false);
                break;
            case TiffPhotometricInterpretation.BlackIsZero when bitsPerSample[0] == 8 && samplesPerPixel == 1:
                // 8-bit grayscale
                ConvertGrayscaleToBgra(data, pixelData, width, height);
                break;
            case TiffPhotometricInterpretation.BlackIsZero when bitsPerSample[0] == 16 && samplesPerPixel == 1:
                // 16-bit grayscale
                ConvertGrayscale16ToBgra(data, pixelData, width, height, littleEndian);
                break;
            case TiffPhotometricInterpretation.WhiteIsZero when bitsPerSample[0] == 16 && samplesPerPixel == 1:
                // 16-bit grayscale (inverted)
                ConvertGrayscale16ToBgra(data, pixelData, width, height, littleEndian, invertColors: true);
                break;
            case TiffPhotometricInterpretation.Rgb when bitsPerSample[0] == 8 && samplesPerPixel == 3:
                // 24-bit RGB
                if (planarConfiguration == 2)
                    ConvertPlanarRgbToBgra(data, pixelData, width, height);
                else
                    ConvertRgbToBgra(data, pixelData, width, height);
                break;
            case TiffPhotometricInterpretation.Rgb when bitsPerSample[0] == 8 && samplesPerPixel == 4:
                // 32-bit RGBA
                ConvertRgbaToBgra(data, pixelData, width, height);
                break;
            case TiffPhotometricInterpretation.Rgb when bitsPerSample[0] == 16 && samplesPerPixel == 3:
                // 48-bit RGB (16-bit per channel)
                if (planarConfiguration == 2)
                    ConvertPlanarRgb16ToBgra(data, pixelData, width, height, littleEndian);
                else
                    ConvertRgb16ToBgra(data, pixelData, width, height, littleEndian);
                break;
            case TiffPhotometricInterpretation.Rgb when bitsPerSample[0] == 16 && samplesPerPixel == 4:
                // 64-bit RGBA (16-bit per channel)
                ConvertRgba16ToBgra(data, pixelData, width, height, littleEndian);
                break;
            default:
                throw new TiffException($"Unsupported TIFF format: {photometric} with {bitsPerSample[0]} bits/sample and {samplesPerPixel} samples/pixel");
        }

        return pixelData;
    }

    private static void ConvertBilevelToBgra(byte[] data, byte[] pixelData, int width, int height, bool invertColors)
    {
        var pixelIndex = 0;
        int bytesPerRow = (width + 7) / 8;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int byteIndex = y * bytesPerRow + (x / 8);
                int bitIndex = 7 - (x % 8);
                bool isSet = (data[byteIndex] & (1 << bitIndex)) != 0;

                byte value;
                if (invertColors)
                    value = (byte)(isSet ? 0 : 255); // White is zero
                else
                    value = (byte)(isSet ? 255 : 0); // Black is zero

                pixelData[pixelIndex++] = value; // Blue
                pixelData[pixelIndex++] = value; // Green
                pixelData[pixelIndex++] = value; // Red
                pixelData[pixelIndex++] = 255;   // Alpha
            }
        }
    }

    private static void ConvertGrayscaleToBgra(byte[] data, byte[] pixelData, int width, int height)
    {
        var pixelIndex = 0;
        foreach (byte gray in data)
        {
            pixelData[pixelIndex++] = gray; // Blue
            pixelData[pixelIndex++] = gray; // Green
            pixelData[pixelIndex++] = gray; // Red
            pixelData[pixelIndex++] = 255;  // Alpha
        }
    }

    private static void ConvertRgbToBgra(byte[] data, byte[] pixelData, int width, int height)
    {
        var pixelIndex = 0;
        for (var i = 0; i < data.Length; i += 3)
        {
            byte r = data[i];
            byte g = data[i + 1];
            byte b = data[i + 2];

            pixelData[pixelIndex++] = b; // Blue
            pixelData[pixelIndex++] = g; // Green
            pixelData[pixelIndex++] = r; // Red
            pixelData[pixelIndex++] = 255; // Alpha
        }
    }

    private static void ConvertPlanarRgbToBgra(byte[] data, byte[] pixelData, int width, int height)
    {
        // Planar format: all R values, then all G values, then all B values
        int pixelCount = width * height;
        var rOffset = 0;
        int gOffset = pixelCount;
        int bOffset = pixelCount * 2;

        var pixelIndex = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            byte r = data[rOffset + i];
            byte g = data[gOffset + i];
            byte b = data[bOffset + i];

            pixelData[pixelIndex++] = b; // Blue
            pixelData[pixelIndex++] = g; // Green
            pixelData[pixelIndex++] = r; // Red
            pixelData[pixelIndex++] = 255; // Alpha
        }
    }

    private static void ConvertRgbaToBgra(byte[] data, byte[] pixelData, int width, int height)
    {
        var pixelIndex = 0;
        for (var i = 0; i < data.Length; i += 4)
        {
            byte r = data[i];
            byte g = data[i + 1];
            byte b = data[i + 2];
            byte a = data[i + 3];

            pixelData[pixelIndex++] = b; // Blue
            pixelData[pixelIndex++] = g; // Green
            pixelData[pixelIndex++] = r; // Red
            pixelData[pixelIndex++] = a; // Alpha
        }
    }

    private static void ConvertGrayscale16ToBgra(byte[] data, byte[] pixelData, int width, int height, bool littleEndian, bool invertColors = false)
    {
        // First pass: find min/max for normalization
        ushort minVal = ushort.MaxValue, maxVal = 0;

        for (var i = 0; i < data.Length; i += 2)
        {
            ushort value16 = littleEndian
                ? (ushort)(data[i] | (data[i + 1] << 8))
                : (ushort)((data[i] << 8) | data[i + 1]);

            minVal = Math.Min(minVal, value16);
            maxVal = Math.Max(maxVal, value16);
        }

        // Calculate scaling factor for normalization
        float range = maxVal - minVal;
        float scale = range > 0 ? 255.0f / range : 0;

        // Second pass: convert with normalization
        var pixelIndex = 0;
        for (var i = 0; i < data.Length; i += 2)
        {
            ushort value16 = littleEndian
                ? (ushort)(data[i] | (data[i + 1] << 8))
                : (ushort)((data[i] << 8) | data[i + 1]);

            // Normalize to 0-255 range
            byte gray = (byte)Math.Min(255, (int)((value16 - minVal) * scale));

            if (invertColors)
                gray = (byte)(255 - gray);

            pixelData[pixelIndex++] = gray; // Blue
            pixelData[pixelIndex++] = gray; // Green
            pixelData[pixelIndex++] = gray; // Red
            pixelData[pixelIndex++] = 255;  // Alpha
        }
    }

    private static void ConvertRgb16ToBgra(byte[] data, byte[] pixelData, int width, int height, bool littleEndian)
    {
        var pixelIndex = 0;
        for (var i = 0; i < data.Length; i += 6) // 6 bytes = 3 channels × 2 bytes
        {
            // Read 16-bit values respecting byte order
            ushort r16 = littleEndian
                ? (ushort)(data[i] | (data[i + 1] << 8))
                : (ushort)((data[i] << 8) | data[i + 1]);
            ushort g16 = littleEndian
                ? (ushort)(data[i + 2] | (data[i + 3] << 8))
                : (ushort)((data[i + 2] << 8) | data[i + 3]);
            ushort b16 = littleEndian
                ? (ushort)(data[i + 4] | (data[i + 5] << 8))
                : (ushort)((data[i + 4] << 8) | data[i + 5]);

            // Downsample to 8-bit
            byte r = (byte)((r16 + 128) >> 8);
            byte g = (byte)((g16 + 128) >> 8);
            byte b = (byte)((b16 + 128) >> 8);

            pixelData[pixelIndex++] = b; // Blue
            pixelData[pixelIndex++] = g; // Green
            pixelData[pixelIndex++] = r; // Red
            pixelData[pixelIndex++] = 255; // Alpha
        }
    }

    private static void ConvertPlanarRgb16ToBgra(byte[] data, byte[] pixelData, int width, int height, bool littleEndian)
    {
        // Planar format: all R values, then all G values, then all B values (each 16-bit)
        int pixelCount = width * height;
        var rOffset = 0;
        int gOffset = pixelCount * 2; // Each value is 2 bytes
        int bOffset = pixelCount * 4;

        var pixelIndex = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            int rPos = rOffset + (i * 2);
            int gPos = gOffset + (i * 2);
            int bPos = bOffset + (i * 2);

            // Read 16-bit values respecting byte order
            ushort r16 = littleEndian
                ? (ushort)(data[rPos] | (data[rPos + 1] << 8))
                : (ushort)((data[rPos] << 8) | data[rPos + 1]);
            ushort g16 = littleEndian
                ? (ushort)(data[gPos] | (data[gPos + 1] << 8))
                : (ushort)((data[gPos] << 8) | data[gPos + 1]);
            ushort b16 = littleEndian
                ? (ushort)(data[bPos] | (data[bPos + 1] << 8))
                : (ushort)((data[bPos] << 8) | data[bPos + 1]);

            // Downsample to 8-bit
            byte r = (byte)((r16 + 128) >> 8);
            byte g = (byte)((g16 + 128) >> 8);
            byte b = (byte)((b16 + 128) >> 8);

            pixelData[pixelIndex++] = b; // Blue
            pixelData[pixelIndex++] = g; // Green
            pixelData[pixelIndex++] = r; // Red
            pixelData[pixelIndex++] = 255; // Alpha
        }
    }

    private static void ConvertRgba16ToBgra(byte[] data, byte[] pixelData, int width, int height, bool littleEndian)
    {
        var pixelIndex = 0;
        for (var i = 0; i < data.Length; i += 8) // 8 bytes = 4 channels × 2 bytes
        {
            // Read 16-bit values respecting byte order
            ushort r16 = littleEndian
                ? (ushort)(data[i] | (data[i + 1] << 8))
                : (ushort)((data[i] << 8) | data[i + 1]);
            ushort g16 = littleEndian
                ? (ushort)(data[i + 2] | (data[i + 3] << 8))
                : (ushort)((data[i + 2] << 8) | data[i + 3]);
            ushort b16 = littleEndian
                ? (ushort)(data[i + 4] | (data[i + 5] << 8))
                : (ushort)((data[i + 4] << 8) | data[i + 5]);
            ushort a16 = littleEndian
                ? (ushort)(data[i + 6] | (data[i + 7] << 8))
                : (ushort)((data[i + 6] << 8) | data[i + 7]);

            // Downsample to 8-bit
            byte r = (byte)((r16 + 128) >> 8);
            byte g = (byte)((g16 + 128) >> 8);
            byte b = (byte)((b16 + 128) >> 8);
            byte a = (byte)((a16 + 128) >> 8);

            pixelData[pixelIndex++] = b; // Blue
            pixelData[pixelIndex++] = g; // Green
            pixelData[pixelIndex++] = r; // Red
            pixelData[pixelIndex++] = a; // Alpha
        }
    }

    private static uint[] ConvertToUInt32Array(object value)
    {
        return value switch
        {
            uint[] arr => arr,
            int[] arr2 => arr2.Select(x => (uint)x).ToArray(),
            ushort[] arr3 => arr3.Select(x => (uint)x).ToArray(),
            uint single => [single],
            int single2 => [(uint)single2],
            ushort single3 => [single3],
            _ => throw new TiffException($"Cannot convert {value.GetType().Name} to uint[]")
        };
    }

    private static object ReadTagValue(BinaryReader reader, ushort fieldType, uint count, uint valueOffset, bool littleEndian)
    {
        int typeSize = GetFieldTypeSize(fieldType);
        uint dataSize = count * (uint)typeSize;

        // If data fits in 4 bytes, it's stored directly in valueOffset
        byte[] data;
        if (dataSize <= 4)
        {
            data = BitConverter.GetBytes(valueOffset);
            if (!littleEndian)
                Array.Reverse(data);
        }
        else
        {
            // Otherwise, valueOffset points to the data
            long currentPosition = reader.BaseStream.Position;
            reader.BaseStream.Seek(valueOffset, SeekOrigin.Begin);
            data = reader.ReadBytes((int)dataSize);
            reader.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
        }

        return ParseFieldValue(data, fieldType, count, littleEndian);
    }

    private static object ParseFieldValue(byte[] data, ushort fieldType, uint count, bool littleEndian)
    {
        switch (fieldType)
        {
            case 1: // BYTE
                return count == 1 ? data[0] : data;

            case 3: // SHORT (ushort)
                if (count == 1)
                    return ReadUInt16FromBytes(data, 0, littleEndian);
            {
                var values = new ushort[count];
                for (var i = 0; i < count; i++)
                    values[i] = ReadUInt16FromBytes(data, i * 2, littleEndian);
                return values;
            }

            case 4: // LONG (uint)
                if (count == 1)
                    return ReadUInt32FromBytes(data, 0, littleEndian);
            {
                var values = new uint[count];
                for (var i = 0; i < count; i++)
                    values[i] = ReadUInt32FromBytes(data, i * 4, littleEndian);
                return values;
            }

            case 2: // ASCII
                return Encoding.ASCII.GetString(data, 0, (int)count - 1); // -1 to skip null terminator

            case 5: // RATIONAL (two LONGs: numerator/denominator)
                // Just return raw bytes, will be parsed by ReadRational when needed
                return data;

            default:
                // For unsupported types, return raw data
                return data;
        }
    }

    private static int GetFieldTypeSize(ushort fieldType)
    {
        return fieldType switch
        {
            1 => 1, // BYTE
            2 => 1, // ASCII
            3 => 2, // SHORT
            4 => 4, // LONG
            5 => 8, // RATIONAL
            _ => 1
        };
    }

    private static ushort ReadUInt16(BinaryReader reader, bool littleEndian)
    {
        byte[] bytes = reader.ReadBytes(2);
        if (littleEndian)
            return BitConverter.ToUInt16(bytes, 0);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static uint ReadUInt32(BinaryReader reader, bool littleEndian)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (littleEndian)
            return BitConverter.ToUInt32(bytes, 0);
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }

    private static ushort ReadUInt16FromBytes(byte[] data, int offset, bool littleEndian)
    {
        if (littleEndian)
            return BitConverter.ToUInt16(data, offset);
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUInt32FromBytes(byte[] data, int offset, bool littleEndian)
    {
        if (littleEndian)
            return BitConverter.ToUInt32(data, offset);
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
    }

    /// <summary>
    /// Scales BGRA image data vertically using bilinear interpolation.
    /// Used to correct aspect ratio for images with non-square pixels.
    /// </summary>
    private static byte[] ScaleImageVertical(byte[] pixelData, int width, int height, int newHeight)
    {
        if (newHeight == height)
            return pixelData;

        var scaledData = new byte[width * newHeight * 4];
        float scaleRatio = (float)height / newHeight;

        for (var y = 0; y < newHeight; y++)
        {
            float srcY = y * scaleRatio;
            var srcY0 = (int)srcY;
            int srcY1 = Math.Min(srcY0 + 1, height - 1);
            float yFrac = srcY - srcY0;

            for (var x = 0; x < width; x++)
            {
                int srcIdx0 = (srcY0 * width + x) * 4;
                int srcIdx1 = (srcY1 * width + x) * 4;
                int dstIdx = (y * width + x) * 4;

                // Bilinear interpolation for each channel
                for (var c = 0; c < 4; c++)
                {
                    float value0 = pixelData[srcIdx0 + c];
                    float value1 = pixelData[srcIdx1 + c];
                    scaledData[dstIdx + c] = (byte)(value0 * (1 - yFrac) + value1 * yFrac);
                }
            }
        }

        return scaledData;
    }

    /// <summary>
    /// Reads a RATIONAL tag value (two LONGs: numerator and denominator).
    /// </summary>
    private static double ReadRational(BinaryReader reader, Dictionary<TiffTag, object> tags, TiffTag tag, bool littleEndian)
    {
        if (!tags.TryGetValue(tag, out object? value))
            return 1.0;

        if (value is not byte[] data || data.Length < 8) return 1.0;
        uint numerator = ReadUInt32FromBytes(data, 0, littleEndian);
        uint denominator = ReadUInt32FromBytes(data, 4, littleEndian);
        return denominator > 0 ? (double)numerator / denominator : 1.0;

    }
}
