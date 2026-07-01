using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.TileAssembly
{
    /// <summary>
    /// Walks the codestream from the end of the main header through the
    /// final EOC, parsing each SOT-delimited tile-part. The packet body
    /// bytes from each tile-part of a tile are concatenated in
    /// <c>TPsot</c> order to produce a single contiguous packet stream per
    /// tile. Per ISO/IEC 15444-1 D.3 a tile-part header may carry COD /
    /// QCD / COC / QCC overrides that apply to the whole tile (only in the
    /// first tile-part of that tile); we keep the first occurrence and
    /// reject duplicates from later tile-parts of the same tile.
    /// </summary>
    internal static class TilePartAssembler
    {
        public static IReadOnlyList<AssembledTile> Assemble(
            CodestreamReader reader, int numberOfComponents,
            IReadOnlyList<PpmSegment>? mainHeaderPpmSegments = null)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));
            if (numberOfComponents < 1)
                throw new ArgumentOutOfRangeException(nameof(numberOfComponents));

            // When PPM is in the main header, each tile-part in codestream
            // order consumes the next (Nppm, header bytes) chunk. The
            // resulting per-tile-part chunks are concatenated within each
            // tile to form that tile's packed-header stream.
            PpmStreamSlicer? ppmSlicer =
                mainHeaderPpmSegments is { Count: > 0 }
                    ? new PpmStreamSlicer(mainHeaderPpmSegments)
                    : null;

            // Indexed by tile index — gaps possible if the codestream skips tiles,
            // but Part 1 requires every tile to ship; we still tolerate.
            var byTile = new Dictionary<int, TileAccumulator>();

            while (!reader.IsAtEnd)
            {
                ushort marker = reader.PeekUInt16BigEndian();
                if (marker == MarkerCode.Eoc)
                {
                    reader.ReadMarker();
                    break;
                }
                if (marker != MarkerCode.Sot)
                {
                    throw new InvalidDataException(
                        $"Expected SOT or EOC at codestream position {reader.Position}; got {MarkerCode.Format(marker)}.");
                }

                int sotStartPosition = reader.Position; // position OF the SOT marker

                var _ = ReadOnlySpan<byte>.Empty;
                TilePartHeader header = TilePartHeaderParser.Parse(reader, numberOfComponents);
                SotSegment sot = header.Sot;

                int bodyStart = header.PacketBodyStartPosition;

                // Psot is the length from SOT marker to end of tile-part body.
                // Psot == 0 means "extends to next SOT or EOC" (D.4).
                int bodyLength;
                if (sot.TilePartLength == 0)
                {
                    bodyLength = LocateNextDelimiter(reader, bodyStart);
                }
                else
                {
                    long bodyEnd = (long)sotStartPosition + sot.TilePartLength;
                    if (bodyEnd < bodyStart || bodyEnd > reader.Length)
                        throw new InvalidDataException(
                            $"SOT Psot={sot.TilePartLength} at codestream position {sotStartPosition} " +
                            $"runs past end of codestream (length {reader.Length}).");
                    bodyLength = checked((int)(bodyEnd - bodyStart));
                }

                reader.Seek(bodyStart);
                byte[] bodyBytes = reader.ReadBytes(bodyLength);

                // Resolve the tile-part's packed-header chunk per A.7.4 / A.7.5.
                // PPM (main header) takes precedence — we consume the next
                // PPM-stream chunk regardless of whether the tile-part also
                // carries PPT. If only PPT is present (no main-header PPM),
                // concatenate the PPT segments of this tile-part.
                byte[] packedHeaderChunk;
                if (ppmSlicer is not null)
                {
                    packedHeaderChunk = ppmSlicer.NextTilePartChunk();
                }
                else if (header.PptSegments.Count > 0)
                {
                    packedHeaderChunk = PptStreamBuilder.Concatenate(header.PptSegments);
                }
                else
                {
                    packedHeaderChunk = Array.Empty<byte>();
                }

                if (!byTile.TryGetValue(sot.TileIndex, out TileAccumulator? acc))
                {
                    acc = new TileAccumulator(sot.TileIndex);
                    byTile[sot.TileIndex] = acc;
                }

                acc.AddTilePart(sot.TilePartIndex, bodyBytes, packedHeaderChunk, header);
            }

            var result = new List<AssembledTile>(byTile.Count);
            foreach (KeyValuePair<int, TileAccumulator> kv in byTile)
            {
                result.Add(kv.Value.ToAssembledTile());
            }
            result.Sort((a, b) => a.TileIndex.CompareTo(b.TileIndex));
            return result;
        }

        /// <summary>
        /// For Psot=0 tile-parts: scan forward from <paramref name="bodyStart"/>
        /// to find the next SOT or EOC marker. The packet body itself may not
        /// contain bare 0xFFxx with xx >= 0x90 unless 0xFFxx is a SOP/EPH
        /// in-bitstream marker. SOP (0xFF91) and EPH (0xFF92) are legal inside
        /// the packet body; SOT (0xFF90) and EOC (0xFFD9) are not — the only
        /// way they appear is at the next tile-part boundary. We exploit this:
        /// scan for 0xFF90 or 0xFFD9.
        /// </summary>
        private static int LocateNextDelimiter(CodestreamReader reader, int bodyStart)
        {
            int cursor = bodyStart;
            int end = reader.Length;
            // The reader is positioned past the SOD; we don't need to mutate
            // its cursor while scanning, just access bytes by Seek+Read.
            // Use a temporary lookup helper that doesn't disturb the caller's
            // cursor state.
            reader.Seek(cursor);
            while (cursor + 1 < end)
            {
                reader.Seek(cursor);
                if (reader.ReadByte() != 0xFF) { cursor++; continue; }
                if (reader.IsAtEnd) break;
                byte next = reader.ReadByte();
                cursor += 2;
                if (next == 0x90 /* SOT */ || next == 0xD9 /* EOC */)
                {
                    return cursor - 2 - bodyStart;
                }
            }
            // No further delimiter — body runs to end of codestream.
            return end - bodyStart;
        }

        private sealed class TileAccumulator
        {
            public int TileIndex { get; }
            private readonly SortedDictionary<int, (byte[] body, byte[] packedHeaders)> _partsByOrder = new();
            private CodSegment? _codOverride;
            private QcdSegment? _qcdOverride;
            private readonly List<CocSegment> _cocs = new();
            private readonly List<QccSegment> _qccs = new();

            public TileAccumulator(int tileIndex)
            {
                TileIndex = tileIndex;
            }

            public void AddTilePart(int tilePartIndex, byte[] body, byte[] packedHeaders, TilePartHeader header)
            {
                if (_partsByOrder.ContainsKey(tilePartIndex))
                    throw new InvalidDataException(
                        $"Duplicate tile-part TPsot={tilePartIndex} for tile {TileIndex}.");

                _partsByOrder.Add(tilePartIndex, (body, packedHeaders));

                // Tile-level coding overrides are signalled in the FIRST
                // tile-part of the tile only (per A.6.1 etc.). We accept them
                // greedily — if a later tile-part also carries one, the spec
                // says it shouldn't and we surface the violation.
                if (header.CodOverride is not null)
                {
                    if (_codOverride is not null)
                        throw new InvalidDataException(
                            $"Tile {TileIndex} has COD overrides in more than one tile-part.");
                    _codOverride = header.CodOverride;
                }
                if (header.QcdOverride is not null)
                {
                    if (_qcdOverride is not null)
                        throw new InvalidDataException(
                            $"Tile {TileIndex} has QCD overrides in more than one tile-part.");
                    _qcdOverride = header.QcdOverride;
                }
                foreach (CocSegment c in header.CocOverrides) _cocs.Add(c);
                foreach (QccSegment q in header.QccOverrides) _qccs.Add(q);
            }

            public AssembledTile ToAssembledTile()
            {
                // Sanity check: ensure tile-part indices are dense from 0.
                var expected = 0;
                foreach (int idx in _partsByOrder.Keys)
                {
                    if (idx != expected)
                        throw new InvalidDataException(
                            $"Tile {TileIndex} tile-part indices skip from {expected - 1} to {idx}.");
                    expected++;
                }

                var bodyTotal = 0;
                var headerTotal = 0;
                foreach ((byte[] body, byte[] packedHeaders) in _partsByOrder.Values)
                {
                    bodyTotal += body.Length;
                    headerTotal += packedHeaders.Length;
                }
                var bodyConcat = new byte[bodyTotal];
                var headerConcat = new byte[headerTotal];
                var bodyOffset = 0;
                var headerOffset = 0;
                foreach ((byte[] body, byte[] packedHeaders) in _partsByOrder.Values)
                {
                    Buffer.BlockCopy(body, 0, bodyConcat, bodyOffset, body.Length);
                    bodyOffset += body.Length;
                    Buffer.BlockCopy(packedHeaders, 0, headerConcat, headerOffset, packedHeaders.Length);
                    headerOffset += packedHeaders.Length;
                }
                return new AssembledTile(TileIndex, bodyConcat, headerConcat, _codOverride, _qcdOverride, _cocs, _qccs);
            }
        }
    }
}
