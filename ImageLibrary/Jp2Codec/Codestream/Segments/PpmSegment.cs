using System;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// PPM marker segment (ISO/IEC 15444-1 A.7.4) — Packed packet headers,
    /// main header. The PPM marker stores packet headers for one or more
    /// tile-parts inside the main header, freeing the in-stream tile-part
    /// data to carry packet bodies only.
    ///
    /// PPM may appear multiple times in the main header; segments are
    /// concatenated by <see cref="ZppmIndex"/> to recover a single packed
    /// header byte stream. The stream is then sliced into one chunk per
    /// tile-part of the codestream, each chunk prefixed with a 4-byte
    /// big-endian length (Nppm) followed by Nppm bytes of packet headers
    /// for that tile-part. An Nppm chunk may straddle PPM marker
    /// boundaries; the slice walker handles this transparently.
    /// </summary>
    internal sealed class PpmSegment
    {
        /// <summary>Zppm: index of this PPM marker within all PPM markers (0..255).</summary>
        public byte ZppmIndex { get; }

        /// <summary>Raw payload bytes after Zppm (the packed-header data for this segment).</summary>
        public byte[] Payload { get; }

        public PpmSegment(byte zppmIndex, byte[] payload)
        {
            ZppmIndex = zppmIndex;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        /// <summary>Parse a PPM segment payload (after the Lppm length field).</summary>
        public static PpmSegment Parse(CodestreamReader r)
        {
            if (r is null) throw new ArgumentNullException(nameof(r));
            byte zppm = r.ReadByte();
            byte[] payload = r.ReadBytes(r.Remaining);
            return new PpmSegment(zppm, payload);
        }
    }
}
