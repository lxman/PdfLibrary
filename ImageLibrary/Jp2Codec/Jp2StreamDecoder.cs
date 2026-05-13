using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Codestream;
using Jp2Codec.Jp2File;
using Jp2Codec.TileAssembly;
using Jp2Codec.Tiles;

namespace Jp2Codec
{
    /// <summary>
    /// Decoder for JPEG 2000 Part 1 bitstreams (ISO/IEC 15444-1). Accepts both
    /// the raw J2K codestream (starts with SOC marker 0xFF4F) and the JP2 file
    /// format (starts with the JP2 signature box).
    /// <para>
    /// <see cref="Decode(byte[])"/> returns per-component decoded samples in a
    /// <see cref="Jp2DecodeResult"/>. Use <c>Jp2Codec.Color.SrgbRenderer</c> /
    /// <c>Jp2Codec.Color.ChromaUpsampler</c> to map those samples to a flat
    /// sRGB byte raster when the caller needs a uniform colour-managed image.
    /// </para>
    /// <para>
    /// Supported features: 5/3 reversible and 9/7 irreversible DWT; multi-tile,
    /// subsampled, and multi-component images; LRCP / RLCP / RPCL / PCRL /
    /// CPRL progressions including tile-scoped overrides; SOP, EPH, PPM, PPT
    /// markers; LAZY / TERMALL / RESTART / VCAUSAL / SEGSYM code-block styles;
    /// JP2 wrapper boxes (colr — sRGB / sYCC / restricted-ICC / any-ICC,
    /// cdef, pclr+cmap). 20 of 21 ISO conformance .j2c files decode either
    /// bit-exact or structurally against the CSJ2K reference.
    /// </para>
    /// </summary>
    public sealed class Jp2StreamDecoder
    {
        /// <summary>Decode from a byte array containing JP2 or J2K data.</summary>
        public Jp2DecodeResult Decode(byte[] data)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            return DecodeCore(data);
        }

        /// <summary>Decode from a stream (the stream is fully read into memory first).</summary>
        public Jp2DecodeResult Decode(Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return DecodeCore(ms.ToArray());
        }

        /// <summary>
        /// Sniff the input and parse the main header without attempting packet
        /// body decoding. Returns the SIZ + JP2-wrapper info so consumers can
        /// inspect image geometry / colorspace without paying the cost of
        /// (or being blocked by) the full Tier-1 decode. Test seam.
        /// </summary>
        internal MainHeader InspectMainHeader(byte[] data, out Jp2FileInfo fileInfo)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            fileInfo = Jp2FileParser.Parse(data);
            var reader = new CodestreamReader(data, fileInfo.CodestreamOffset, fileInfo.CodestreamLength);
            return MainHeaderParser.Parse(reader);
        }

        private static Jp2DecodeResult DecodeCore(byte[] data)
        {
            Jp2FileInfo fileInfo = Jp2FileParser.Parse(data);
            var reader = new CodestreamReader(data, fileInfo.CodestreamOffset, fileInfo.CodestreamLength);
            MainHeader header = MainHeaderParser.Parse(reader);

            // Position the reader at the byte after the main header; from
            // here on it's tile-part bodies through to the EOC marker.
            reader.Seek(header.EndPosition);
            IReadOnlyList<AssembledTile> tiles = TilePartAssembler.Assemble(reader, header.Siz.NumberOfComponents, header.PpmSegments);
            return ImageAssembler.Decode(header, tiles, fileInfo);
        }
    }
}
