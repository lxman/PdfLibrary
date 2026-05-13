using System;
using System.IO;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// Comment registration value per ISO/IEC 15444-1 Table A-37.
    /// </summary>
    internal enum CommentRegistration : ushort
    {
        /// <summary>Binary data; no text interpretation.</summary>
        Binary = 0,
        /// <summary>ISO/IEC 8859-15:1999 (Latin-9) — closest spec equivalent of ASCII text.</summary>
        Latin9 = 1,
    }

    /// <summary>
    /// COM marker segment (ISO/IEC 15444-1 A.9.2) — Comment. Optional, may
    /// appear in either the main header or a tile-part header; carries
    /// free-form metadata such as the producing software name.
    /// </summary>
    internal sealed class ComSegment
    {
        public CommentRegistration Registration { get; }
        public byte[] Data { get; }

        public ComSegment(CommentRegistration registration, byte[] data)
        {
            Registration = registration;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public string GetTextOrEmpty()
        {
            if (Registration != CommentRegistration.Latin9 || Data.Length == 0)
                return string.Empty;
            // Latin-9 differs from Latin-1 only in 8 code points (€, Š, š, Ž, ž, Œ, œ, Ÿ);
            // direct byte→char preserves all ASCII bytes (the common case for encoder
            // identifiers like "Kakadu", "OpenJPEG", "ImageMagick") and only mis-maps
            // those 8 high-byte glyphs — which is acceptable for a "comment" field.
            var chars = new char[Data.Length];
            for (var i = 0; i < Data.Length; i++) chars[i] = (char)Data[i];
            return new string(chars);
        }

        public static ComSegment Parse(CodestreamReader r)
        {
            if (r.Length < 2)
                throw new InvalidDataException($"COM: payload must be >= 2 bytes (got {r.Length}).");
            ushort rcom = r.ReadUInt16BigEndian();
            byte[] data = r.ReadBytes(r.Remaining);
            return new ComSegment((CommentRegistration)rcom, data);
        }
    }
}
