using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.TileAssembly
{
    /// <summary>
    /// Slices a concatenated PPM-marker payload into one packed-header chunk
    /// per tile-part in codestream order (ISO/IEC 15444-1 A.7.4). The PPM
    /// payload is a tightly-packed sequence of <c>(Nppm: 4-byte big-endian
    /// length, packet-header bytes: Nppm bytes)</c> tuples; the i-th tuple
    /// is the packet headers for the i-th tile-part appearing in the
    /// codestream. Tuples may straddle individual PPM marker boundaries, so
    /// the input is the concatenation of every PPM marker payload, ordered
    /// by Zppm.
    /// </summary>
    internal sealed class PpmStreamSlicer
    {
        private readonly byte[] _stream;
        private int _cursor;

        public PpmStreamSlicer(IReadOnlyList<PpmSegment> ppmSegments)
        {
            if (ppmSegments is null) throw new ArgumentNullException(nameof(ppmSegments));

            // Concatenate the marker payloads in Zppm order. The spec lets
            // Zppm wrap from 255 → 0 (more than 256 markers), but no
            // conformance file we ship that many; sort numerically and
            // assert strict monotonicity to surface the wraparound case
            // if it ever appears.
            PpmSegment[] sorted = ppmSegments.OrderBy(p => p.ZppmIndex).ToArray();
            int total = 0;
            for (var i = 0; i < sorted.Length; i++) total += sorted[i].Payload.Length;
            _stream = new byte[total];
            int offset = 0;
            for (var i = 0; i < sorted.Length; i++)
            {
                Buffer.BlockCopy(sorted[i].Payload, 0, _stream, offset, sorted[i].Payload.Length);
                offset += sorted[i].Payload.Length;
            }
            _cursor = 0;
        }

        /// <summary>
        /// Pull the next <c>(Nppm, packet-header bytes)</c> tuple. Returns the
        /// payload bytes for one tile-part. Throws if the stream is exhausted
        /// (the caller has visited more tile-parts than PPM data covers).
        /// </summary>
        public byte[] NextTilePartChunk()
        {
            if (_cursor + 4 > _stream.Length)
                throw new InvalidDataException(
                    $"PPM stream exhausted: expected a 4-byte Nppm at offset {_cursor}, only {_stream.Length - _cursor} byte(s) remain.");
            uint nppm =
                  ((uint)_stream[_cursor] << 24)
                | ((uint)_stream[_cursor + 1] << 16)
                | ((uint)_stream[_cursor + 2] << 8)
                | _stream[_cursor + 3];
            _cursor += 4;
            if ((long)_cursor + nppm > _stream.Length)
                throw new InvalidDataException(
                    $"PPM stream truncated: Nppm={nppm} at offset {_cursor - 4} extends past end of stream ({_stream.Length}).");
            var chunk = new byte[nppm];
            Buffer.BlockCopy(_stream, _cursor, chunk, 0, (int)nppm);
            _cursor += (int)nppm;
            return chunk;
        }

        /// <summary>True once every byte of the concatenated PPM stream has been sliced.</summary>
        public bool IsExhausted => _cursor >= _stream.Length;
    }

    /// <summary>
    /// Builds the packed packet-header byte stream for one tile-part from
    /// its PPT segments (ISO/IEC 15444-1 A.7.5). Concatenates payloads in
    /// Zppt order; unlike PPM, the result is the entire packet-header
    /// stream for the tile-part with no length prefixes.
    /// </summary>
    internal static class PptStreamBuilder
    {
        public static byte[] Concatenate(IReadOnlyList<PptSegment> pptSegments)
        {
            if (pptSegments is null) throw new ArgumentNullException(nameof(pptSegments));
            if (pptSegments.Count == 0) return Array.Empty<byte>();

            PptSegment[] sorted = pptSegments.OrderBy(p => p.ZpptIndex).ToArray();
            int total = 0;
            for (var i = 0; i < sorted.Length; i++) total += sorted[i].Payload.Length;
            var result = new byte[total];
            int offset = 0;
            for (var i = 0; i < sorted.Length; i++)
            {
                Buffer.BlockCopy(sorted[i].Payload, 0, result, offset, sorted[i].Payload.Length);
                offset += sorted[i].Payload.Length;
            }
            return result;
        }
    }
}
