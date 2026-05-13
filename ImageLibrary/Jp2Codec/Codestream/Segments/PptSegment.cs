using System;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// PPT marker segment (ISO/IEC 15444-1 A.7.5) — Packed packet headers,
    /// tile-part header. The per-tile-part counterpart to <see cref="PpmSegment"/>:
    /// stores the packet headers for the containing tile-part inside that
    /// tile-part's header, freeing its in-stream data to carry packet bodies
    /// only.
    ///
    /// PPT may appear multiple times in a tile-part header; segments are
    /// concatenated by <see cref="ZpptIndex"/>. The concatenation is the
    /// raw packet-header byte stream for the tile-part — there is no Nppm-
    /// length prefix because everything in PPT belongs to a single
    /// tile-part.
    /// </summary>
    internal sealed class PptSegment
    {
        /// <summary>Zppt: index of this PPT marker within the tile-part (0..255).</summary>
        public byte ZpptIndex { get; }

        /// <summary>Raw payload bytes after Zppt (the packed-header data for this segment).</summary>
        public byte[] Payload { get; }

        public PptSegment(byte zpptIndex, byte[] payload)
        {
            ZpptIndex = zpptIndex;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        /// <summary>Parse a PPT segment payload (after the Lppt length field).</summary>
        public static PptSegment Parse(CodestreamReader r)
        {
            if (r is null) throw new ArgumentNullException(nameof(r));
            byte zppt = r.ReadByte();
            byte[] payload = r.ReadBytes(r.Remaining);
            return new PptSegment(zppt, payload);
        }
    }
}
