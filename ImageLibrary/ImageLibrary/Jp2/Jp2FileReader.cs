using System;
using System.Text;

namespace ImageLibrary.Jp2;

/// <summary>
/// Reads JP2 file format (boxes) and extracts the codestream.
/// </summary>
internal class Jp2FileReader
{
    private readonly byte[] _data;
    private int _position;

    // JP2 box types
    private static readonly uint JP2_SIGNATURE = 0x6A502020;  // "jP  "
    private static readonly uint FTYP = 0x66747970;          // "ftyp"
    private static readonly uint JP2H = 0x6A703268;          // "jp2h"
    private static readonly uint IHDR = 0x69686472;          // "ihdr"
    private static readonly uint COLR = 0x636F6C72;          // "colr"
    private static readonly uint CDEF = 0x63646566;          // "cdef"
    private static readonly uint PCLR = 0x70636C72;          // "pclr" - palette
    private static readonly uint CMAP = 0x636D6170;          // "cmap" - component mapping
    private static readonly uint JP2C = 0x6A703263;          // "jp2c"

    public Jp2FileReader(byte[] data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>
    /// Checks if the data starts with a JP2 signature.
    /// </summary>
    public static bool IsJp2File(byte[] data)
    {
        // Check for JP2 signature box
        if (data.Length < 12) return false;
        uint length = ReadUInt32BE(data, 0);
        uint type = ReadUInt32BE(data, 4);
        return length == 12 && type == JP2_SIGNATURE;
    }

    /// <summary>
    /// Checks if the data starts with a raw codestream (SOC marker).
    /// </summary>
    public static bool IsRawCodestream(byte[] data)
    {
        return data.Length >= 2 && data[0] == 0xFF && data[1] == 0x4F;
    }

    /// <summary>
    /// Reads the JP2 file and extracts information and the codestream.
    /// </summary>
    public Jp2FileInfo Read()
    {
        var info = new Jp2FileInfo();

        while (_position < _data.Length)
        {
            Jp2Box? box = ReadBox();
            if (box == null) break;

            switch (box.Type)
            {
                case var t when t == JP2_SIGNATURE:
                    // Verify signature content
                    if (box.Data.Length != 4 ||
                        box.Data[0] != 0x0D || box.Data[1] != 0x0A ||
                        box.Data[2] != 0x87 || box.Data[3] != 0x0A)
                    {
                        throw new Jp2Exception("Invalid JP2 signature");
                    }
                    break;

                case var t when t == FTYP:
                    info.Brand = Encoding.ASCII.GetString(box.Data, 0, 4);
                    break;

                case var t when t == JP2H:
                    ParseJp2HeaderBox(box.Data, info);
                    break;

                case var t when t == JP2C:
                    info.CodestreamData = box.Data;
                    break;
            }
        }

        if (info.CodestreamData == null)
        {
            throw new Jp2Exception("No codestream found in JP2 file");
        }

        return info;
    }

    private void ParseJp2HeaderBox(byte[] data, Jp2FileInfo info)
    {
        var pos = 0;
        while (pos < data.Length)
        {
            if (pos + 8 > data.Length) break;

            uint boxLength = ReadUInt32BE(data, pos);
            uint boxType = ReadUInt32BE(data, pos + 4);
            var dataOffset = 8;

            if (boxLength == 1)
            {
                // Extended length
                dataOffset = 16;
            }
            else if (boxLength == 0)
            {
                boxLength = (uint)(data.Length - pos);
            }

            int dataLength = (int)boxLength - dataOffset;

            if (boxType == IHDR)
            {
                // Image Header box
                info.Height = (int)ReadUInt32BE(data, pos + dataOffset);
                info.Width = (int)ReadUInt32BE(data, pos + dataOffset + 4);
                info.ComponentCount = ReadUInt16BE(data, pos + dataOffset + 8);
                info.BitDepth = data[pos + dataOffset + 10];
            }
            else if (boxType == COLR)
            {
                // Color Specification box
                byte method = data[pos + dataOffset];
                info.ColorMethod = method;
                if (method == 1 && dataLength >= 7)
                {
                    info.ColorSpace = (int)ReadUInt32BE(data, pos + dataOffset + 3);
                }
            }
            else if (boxType == CDEF)
            {
                // Channel Definition box
                // Maps codestream components to output channels
                int cdefOffset = pos + dataOffset;
                int nDefs = ReadUInt16BE(data, cdefOffset);
                info.ChannelDefinitions = new ChannelDefinition[nDefs];
                int defOffset = cdefOffset + 2;
                for (var i = 0; i < nDefs && defOffset + 6 <= pos + (int)boxLength; i++)
                {
                    info.ChannelDefinitions[i] = new ChannelDefinition
                    {
                        Channel = ReadUInt16BE(data, defOffset),
                        Type = ReadUInt16BE(data, defOffset + 2),
                        Association = ReadUInt16BE(data, defOffset + 4),
                    };
                    defOffset += 6;
                }
            }
            else if (boxType == PCLR)
            {
                // Palette box
                // NE (2 bytes): number of entries
                // NPC (1 byte): number of palette columns (components per entry)
                // Bi (NPC bytes): bit depth for each column (0-based, actual = Bi+1)
                // Cij (NE * NPC entries): palette values
                int pclrOffset = pos + dataOffset;
                int numEntries = ReadUInt16BE(data, pclrOffset);
                int numColumns = data[pclrOffset + 2];

                var bitDepths = new int[numColumns];
                for (var c = 0; c < numColumns; c++)
                {
                    // Bit depth is stored as (actual_bits - 1) in low 7 bits, sign in bit 7
                    bitDepths[c] = (data[pclrOffset + 3 + c] & 0x7F) + 1;
                }

                info.Palette = new Jp2Palette
                {
                    NumEntries = numEntries,
                    NumColumns = numColumns,
                    BitDepths = bitDepths,
                    Entries = new int[numEntries, numColumns]
                };

                // Read palette entries
                int entryOffset = pclrOffset + 3 + numColumns;
                for (var e = 0; e < numEntries && entryOffset < pos + (int)boxLength; e++)
                {
                    for (var c = 0; c < numColumns; c++)
                    {
                        int bits = bitDepths[c];
                        if (bits <= 8)
                        {
                            info.Palette.Entries[e, c] = data[entryOffset++];
                        }
                        else if (bits <= 16)
                        {
                            info.Palette.Entries[e, c] = ReadUInt16BE(data, entryOffset);
                            entryOffset += 2;
                        }
                    }
                }
            }
            else if (boxType == CMAP)
            {
                // Component Mapping box
                // Maps codestream components through palette to output components
                // Each entry: CMP (2 bytes), MTYP (1 byte), PCOL (1 byte)
                int cmapOffset = pos + dataOffset;
                int numMappings = dataLength / 4;
                info.ComponentMappings = new ComponentMapping[numMappings];

                for (var i = 0; i < numMappings; i++)
                {
                    info.ComponentMappings[i] = new ComponentMapping
                    {
                        Component = ReadUInt16BE(data, cmapOffset + i * 4),
                        MappingType = data[cmapOffset + i * 4 + 2],
                        PaletteColumn = data[cmapOffset + i * 4 + 3]
                    };
                }
            }

            pos += (int)boxLength;
        }
    }

    private Jp2Box? ReadBox()
    {
        if (_position + 8 > _data.Length)
        {
            return null;
        }

        uint boxLength = ReadUInt32BE(_data, _position);
        uint boxType = ReadUInt32BE(_data, _position + 4);
        var dataOffset = 8;
        long actualLength = boxLength;

        if (boxLength == 1)
        {
            // Extended length (8 bytes)
            if (_position + 16 > _data.Length) return null;
            actualLength = (long)ReadUInt32BE(_data, _position + 8) << 32 |
                           ReadUInt32BE(_data, _position + 12);
            dataOffset = 16;
        }
        else if (boxLength == 0)
        {
            // Box extends to end of file
            actualLength = _data.Length - _position;
        }

        int dataLength = (int)actualLength - dataOffset;
        if (_position + dataOffset + dataLength > _data.Length)
        {
            throw new Jp2Exception($"Box extends beyond file end: type={boxType:X8}");
        }

        var box = new Jp2Box
        {
            Type = boxType,
            Data = new byte[dataLength],
        };
        Array.Copy(_data, _position + dataOffset, box.Data, 0, dataLength);

        _position += (int)actualLength;
        return box;
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return (uint)(data[offset] << 24) | (uint)(data[offset + 1] << 16) |
               (uint)(data[offset + 2] << 8) | data[offset + 3];
    }

    private static ushort ReadUInt16BE(byte[] data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }
}

/// <summary>
/// Information extracted from a JP2 file.
/// </summary>
internal class Jp2FileInfo
{
    public string Brand { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int ComponentCount { get; set; }
    public int BitDepth { get; set; }
    public int ColorMethod { get; set; }
    public int ColorSpace { get; set; }
    public byte[]? CodestreamData { get; set; }

    /// <summary>
    /// Channel definitions from the cdef box, mapping codestream components to output channels.
    /// </summary>
    public ChannelDefinition[]? ChannelDefinitions { get; set; }

    /// <summary>
    /// Palette from the pclr box (for indexed color images).
    /// </summary>
    public Jp2Palette? Palette { get; set; }

    /// <summary>
    /// Component mappings from the cmap box (maps codestream components through palette).
    /// </summary>
    public ComponentMapping[]? ComponentMappings { get; set; }
}

/// <summary>
/// Channel definition from the cdef box.
/// Maps a codestream component to an output channel.
/// </summary>
internal struct ChannelDefinition
{
    /// <summary>Channel number (codestream component index).</summary>
    public int Channel { get; set; }

    /// <summary>Channel type (0=color, 1=opacity, 2=premultiplied opacity).</summary>
    public int Type { get; set; }

    /// <summary>
    /// Association: which output position this channel maps to.
    /// For sYCC: 1=Y, 2=Cb, 3=Cr.
    /// For RGB: 1=R, 2=G, 3=B.
    /// </summary>
    public int Association { get; set; }
}

/// <summary>
/// A box from the JP2 file structure.
/// </summary>
internal class Jp2Box
{
    public uint Type { get; set; }
    public byte[] Data { get; set; } = [];
}

/// <summary>
/// Palette from the pclr box.
/// </summary>
internal class Jp2Palette
{
    public int NumEntries { get; set; }
    public int NumColumns { get; set; }
    public int[] BitDepths { get; set; } = [];
    public int[,] Entries { get; set; } = new int[0, 0];
}

/// <summary>
/// Component mapping from the cmap box.
/// Maps a codestream component through the palette.
/// </summary>
internal struct ComponentMapping
{
    /// <summary>Codestream component index.</summary>
    public int Component { get; set; }

    /// <summary>Mapping type: 0=direct, 1=palette mapping.</summary>
    public int MappingType { get; set; }

    /// <summary>Palette column index (only used if MappingType=1).</summary>
    public int PaletteColumn { get; set; }
}