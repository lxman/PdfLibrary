using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ImageLibrary.Tiff;

/// <summary>
/// Encodes TIFF (Tagged Image File Format) images to byte arrays, streams, or files.
/// </summary>
public static class TiffEncoder
{
    /// <summary>
    /// Encodes a TIFF image to a byte array.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="compression">The compression type to use (default: None).</param>
    /// <returns>The encoded TIFF data.</returns>
    public static byte[] Encode(TiffImage image, TiffCompression compression = TiffCompression.None)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));

        using (var stream = new MemoryStream())
        {
            Encode(image, stream, compression);
            return stream.ToArray();
        }
    }

    /// <summary>
    /// Encodes a TIFF image to a stream.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="compression">The compression type to use (default: None).</param>
    public static void Encode(TiffImage image, Stream stream, TiffCompression compression = TiffCompression.None)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            EncodeInternal(writer, image, compression);
        }
    }

    /// <summary>
    /// Encodes a TIFF image to a file.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="path">The file path to write to.</param>
    /// <param name="compression">The compression type to use (default: None).</param>
    public static void Encode(TiffImage image, string path, TiffCompression compression = TiffCompression.None)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
        if (path == null)
            throw new ArgumentNullException(nameof(path));

        using (FileStream stream = File.Create(path))
        {
            Encode(image, stream, compression);
        }
    }

    private static void EncodeInternal(BinaryWriter writer, TiffImage image, TiffCompression compression)
    {
        // Validate compression
        if (compression != TiffCompression.None && compression != TiffCompression.Lzw)
        {
            throw new TiffException($"Unsupported TIFF compression for encoding: {compression}. Use None or Lzw.");
        }

        // Convert BGRA to RGB
        byte[] rgbData = ConvertBgraToRgb(image.PixelData, image.Width, image.Height);

        // Compress if needed
        byte[] imageData = compression == TiffCompression.Lzw
            ? Lzw.Lzw.Compress(rgbData)
            : rgbData;

        // Write TIFF header
        bool littleEndian = BitConverter.IsLittleEndian;
        if (littleEndian)
        {
            writer.Write((byte)0x49); // 'I'
            writer.Write((byte)0x49); // 'I'
        }
        else
        {
            writer.Write((byte)0x4D); // 'M'
            writer.Write((byte)0x4D); // 'M'
        }

        WriteUInt16(writer, 42, littleEndian); // Magic number

        // Calculate IFD offset (header is 8 bytes, image data follows)
        var ifdOffset = (uint)(8 + imageData.Length);
        WriteUInt32(writer, ifdOffset, littleEndian);

        // Write image data
        writer.Write(imageData);

        // Write IFD
        WriteIfd(writer, image.Width, image.Height, imageData.Length, compression, littleEndian);
    }

    private static void WriteIfd(BinaryWriter writer, int width, int height, int imageDataLength, TiffCompression compression, bool littleEndian)
    {
        var tags = new List<IfdEntry>
        {
            new IfdEntry { Tag = TiffTag.ImageWidth, Type = 4, Count = 1, Value = (uint)width },
            new IfdEntry { Tag = TiffTag.ImageHeight, Type = 4, Count = 1, Value = (uint)height },
            new IfdEntry { Tag = TiffTag.BitsPerSample, Type = 3, Count = 3, Value = new ushort[] { 8, 8, 8 } },
            new IfdEntry { Tag = TiffTag.Compression, Type = 3, Count = 1, Value = (ushort)compression },
            new IfdEntry { Tag = TiffTag.PhotometricInterpretation, Type = 3, Count = 1, Value = (ushort)TiffPhotometricInterpretation.Rgb },
            new IfdEntry { Tag = TiffTag.StripOffsets, Type = 4, Count = 1, Value = 8u }, // Image data starts at offset 8
            new IfdEntry { Tag = TiffTag.SamplesPerPixel, Type = 3, Count = 1, Value = (ushort)3 },
            new IfdEntry { Tag = TiffTag.RowsPerStrip, Type = 4, Count = 1, Value = (uint)height },
            new IfdEntry { Tag = TiffTag.StripByteCounts, Type = 4, Count = 1, Value = (uint)imageDataLength },
            new IfdEntry { Tag = TiffTag.XResolution, Type = 5, Count = 1, Value = new uint[] { 72, 1 } }, // 72 DPI
            new IfdEntry { Tag = TiffTag.YResolution, Type = 5, Count = 1, Value = new uint[] { 72, 1 } }, // 72 DPI
            new IfdEntry { Tag = TiffTag.PlanarConfiguration, Type = 3, Count = 1, Value = (ushort)1 }, // Chunky
            new IfdEntry { Tag = TiffTag.ResolutionUnit, Type = 3, Count = 1, Value = (ushort)2 } // Inches
        };

        // Write number of entries
        WriteUInt16(writer, (ushort)tags.Count, littleEndian);

        // Calculate data offset (after all IFD entries and next IFD pointer)
        long dataOffset = writer.BaseStream.Position + (tags.Count * 12) + 4;

        // Write IFD entries
        foreach (IfdEntry? entry in tags)
        {
            WriteIfdEntry(writer, entry, ref dataOffset, littleEndian);
        }

        // Write next IFD offset (0 = no more IFDs)
        WriteUInt32(writer, 0, littleEndian);
    }

    private static void WriteIfdEntry(BinaryWriter writer, IfdEntry entry, ref long dataOffset, bool littleEndian)
    {
        WriteUInt16(writer, (ushort)entry.Tag, littleEndian);
        WriteUInt16(writer, entry.Type, littleEndian);
        WriteUInt32(writer, entry.Count, littleEndian);

        int typeSize = GetFieldTypeSize(entry.Type);
        uint dataSize = entry.Count * (uint)typeSize;

        if (dataSize <= 4)
        {
            // Value fits in 4 bytes - write directly
            WriteValueInline(writer, entry.Value, entry.Type, entry.Count, littleEndian);
        }
        else
        {
            // Value doesn't fit - write offset and defer data
            WriteUInt32(writer, (uint)dataOffset, littleEndian);

            // Remember current position
            long currentPosition = writer.BaseStream.Position;

            // Write data at offset
            writer.BaseStream.Seek(dataOffset, SeekOrigin.Begin);
            WriteValueData(writer, entry.Value, entry.Type, entry.Count, littleEndian);

            // Update data offset for next entry
            dataOffset = writer.BaseStream.Position;

            // Return to IFD entry position
            writer.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
        }
    }

    private static void WriteValueInline(BinaryWriter writer, object value, ushort type, uint count, bool littleEndian)
    {
        var bytes = new byte[4];

        switch (type)
        {
            case 3: // SHORT
                if (value is ushort single)
                {
                    WriteUInt16ToBytes(bytes, 0, single, littleEndian);
                }
                else if (value is ushort[] arr && arr.Length == 2)
                {
                    WriteUInt16ToBytes(bytes, 0, arr[0], littleEndian);
                    WriteUInt16ToBytes(bytes, 2, arr[1], littleEndian);
                }
                break;

            case 4: // LONG
                if (value is uint uintValue)
                {
                    WriteUInt32ToBytes(bytes, 0, uintValue, littleEndian);
                }
                break;
        }

        writer.Write(bytes);
    }

    private static void WriteValueData(BinaryWriter writer, object value, ushort type, uint count, bool littleEndian)
    {
        switch (type)
        {
            case 3: // SHORT array
                if (value is ushort[] arr)
                {
                    foreach (ushort val in arr)
                        WriteUInt16(writer, val, littleEndian);
                }
                break;

            case 5: // RATIONAL (two LONGs: numerator/denominator)
                if (value is uint[] rational && rational.Length == 2)
                {
                    WriteUInt32(writer, rational[0], littleEndian);
                    WriteUInt32(writer, rational[1], littleEndian);
                }
                break;
        }
    }

    private static byte[] ConvertBgraToRgb(byte[] bgraData, int width, int height)
    {
        var rgbData = new byte[width * height * 3];
        var srcIndex = 0;
        var dstIndex = 0;

        for (var i = 0; i < width * height; i++)
        {
            byte b = bgraData[srcIndex++];
            byte g = bgraData[srcIndex++];
            byte r = bgraData[srcIndex++];
            srcIndex++; // Skip alpha

            rgbData[dstIndex++] = r;
            rgbData[dstIndex++] = g;
            rgbData[dstIndex++] = b;
        }

        return rgbData;
    }

    private static int GetFieldTypeSize(ushort fieldType)
    {
        switch (fieldType)
        {
            case 1: return 1; // BYTE
            case 2: return 1; // ASCII
            case 3: return 2; // SHORT
            case 4: return 4; // LONG
            case 5: return 8; // RATIONAL
            default: return 1;
        }
    }

    private static void WriteUInt16(BinaryWriter writer, ushort value, bool littleEndian)
    {
        if (littleEndian)
        {
            writer.Write(value);
        }
        else
        {
            writer.Write((byte)(value >> 8));
            writer.Write((byte)(value & 0xFF));
        }
    }

    private static void WriteUInt32(BinaryWriter writer, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            writer.Write(value);
        }
        else
        {
            writer.Write((byte)(value >> 24));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
    }

    private static void WriteUInt16ToBytes(byte[] buffer, int offset, ushort value, bool littleEndian)
    {
        if (littleEndian)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)(value >> 8);
        }
        else
        {
            buffer[offset] = (byte)(value >> 8);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }
    }

    private static void WriteUInt32ToBytes(byte[] buffer, int offset, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)(value >> 24);
        }
        else
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }
    }

    private class IfdEntry
    {
        public TiffTag Tag { get; set; }
        public ushort Type { get; set; }
        public uint Count { get; set; }
        public object Value { get; set; } = null!;
    }
}
