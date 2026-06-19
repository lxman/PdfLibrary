using System;
using System.Collections.Generic;
using System.Linq;

namespace TiffCodec.Tests;

/// <summary>
/// Minimal little-endian TIFF writer for tests. Lays out the header, one IFD (tags sorted ascending,
/// as the format requires), external tag payloads (anything wider than the 4-byte inline slot), and
/// pixel/tile data blocks — resolving strip/tile offset and byte-count tags to the blocks they
/// reference. Offsets are word-aligned per TIFF 6.0.
/// </summary>
internal sealed class TiffBuilder
{
    public const ushort ImageWidth = 256, ImageHeight = 257, BitsPerSample = 258, Compression = 259,
        Photometric = 262, StripOffsets = 273, SamplesPerPixel = 277, RowsPerStrip = 278,
        StripByteCounts = 279, Predictor = 317, ColorMap = 320, TileWidth = 322, TileLength = 323,
        TileOffsets = 324, TileByteCounts = 325;

    private sealed class Entry
    {
        public ushort Tag;
        public ushort Type;
        public uint Count;
        public byte[]? Payload;   // value bytes (little-endian) when known up front
        public int[]? OffsetRefs; // value = offsets of these data blocks
        public int[]? LengthRefs; // value = byte lengths of these data blocks
    }

    private readonly List<Entry> _entries = [];
    private readonly List<byte[]> _blocks = [];

    public int AddBlock(byte[] data) { _blocks.Add(data); return _blocks.Count - 1; }

    public TiffBuilder Short(ushort tag, params ushort[] values)
    {
        var payload = new byte[values.Length * 2];
        for (var i = 0; i < values.Length; i++) { payload[i * 2] = (byte)values[i]; payload[i * 2 + 1] = (byte)(values[i] >> 8); }
        _entries.Add(new Entry { Tag = tag, Type = 3, Count = (uint)values.Length, Payload = payload });
        return this;
    }

    public TiffBuilder Long(ushort tag, params uint[] values)
    {
        _entries.Add(new Entry { Tag = tag, Type = 4, Count = (uint)values.Length, Payload = LongsToBytes(values) });
        return this;
    }

    public TiffBuilder OffsetsOf(ushort tag, params int[] blockIndices)
    {
        _entries.Add(new Entry { Tag = tag, Type = 4, Count = (uint)blockIndices.Length, OffsetRefs = blockIndices });
        return this;
    }

    public TiffBuilder LengthsOf(ushort tag, params int[] blockIndices)
    {
        _entries.Add(new Entry { Tag = tag, Type = 4, Count = (uint)blockIndices.Length, LengthRefs = blockIndices });
        return this;
    }

    public byte[] Build()
    {
        _entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        int n = _entries.Count;
        int cursor = 8 + 2 + 12 * n + 4; // header + IFD (count + entries + next-IFD pointer)

        var externalOffset = new int[n];
        for (var i = 0; i < n; i++)
        {
            var size = (int)(_entries[i].Count * TypeSize(_entries[i].Type));
            if (size <= 4) continue;
            externalOffset[i] = cursor;
            cursor += Align(size);
        }

        var blockOffset = new int[_blocks.Count];
        for (var b = 0; b < _blocks.Count; b++)
        {
            blockOffset[b] = cursor;
            cursor += Align(_blocks[b].Length);
        }

        foreach (Entry e in _entries)
        {
            if (e.OffsetRefs != null)
                e.Payload = LongsToBytes(e.OffsetRefs.Select(idx => (uint)blockOffset[idx]).ToArray());
            else if (e.LengthRefs != null)
                e.Payload = LongsToBytes(e.LengthRefs.Select(idx => (uint)_blocks[idx].Length).ToArray());
        }

        var file = new byte[cursor];
        file[0] = 0x49; file[1] = 0x49; // "II" little-endian
        WriteUInt16(file, 2, 42);
        WriteUInt32(file, 4, 8);

        var p = 8;
        WriteUInt16(file, p, (ushort)n); p += 2;
        for (var i = 0; i < n; i++)
        {
            Entry e = _entries[i];
            WriteUInt16(file, p, e.Tag); p += 2;
            WriteUInt16(file, p, e.Type); p += 2;
            WriteUInt32(file, p, e.Count); p += 4;
            byte[] payload = e.Payload!;
            if (payload.Length <= 4)
                Array.Copy(payload, 0, file, p, payload.Length); // left-justified; trailing bytes stay zero
            else
            {
                WriteUInt32(file, p, (uint)externalOffset[i]);
                Array.Copy(payload, 0, file, externalOffset[i], payload.Length);
            }
            p += 4;
        }
        WriteUInt32(file, p, 0); // no next IFD

        for (var b = 0; b < _blocks.Count; b++)
            Array.Copy(_blocks[b], 0, file, blockOffset[b], _blocks[b].Length);

        return file;
    }

    private static int Align(int n) => (n + 1) & ~1;
    private static int TypeSize(ushort type) => type switch { 1 => 1, 2 => 1, 3 => 2, 4 => 4, 5 => 8, _ => 1 };
    private static byte[] LongsToBytes(uint[] v) { var b = new byte[v.Length * 4]; for (var i = 0; i < v.Length; i++) WriteUInt32(b, i * 4, v[i]); return b; }
    private static void WriteUInt16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    private static void WriteUInt32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
}
