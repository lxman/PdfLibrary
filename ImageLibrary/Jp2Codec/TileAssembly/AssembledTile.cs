using System;
using System.Collections.Generic;
using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.TileAssembly
{
    /// <summary>
    /// A single tile's worth of decoded codestream after tile-part assembly:
    /// the concatenated packet body bytes from every tile-part of this tile
    /// (in TPsot order), plus the effective COD/QCD/COC/QCC overrides for
    /// the tile. The first tile-part of a tile may carry per-tile coding
    /// overrides; later tile-parts of the same tile inherit them. ISO/IEC
    /// 15444-1 D.3 forbids splitting a packet across tile-parts, so the
    /// concatenation produces a single contiguous packet stream for the tile.
    /// </summary>
    internal sealed class AssembledTile
    {
        /// <summary>Tile index Isot (0 .. number-of-tiles − 1).</summary>
        public int TileIndex { get; }

        /// <summary>Concatenated packet body bytes for the tile (one stream, packet-aligned).</summary>
        public byte[] PacketBody { get; }

        /// <summary>
        /// Concatenated packet-header bytes for the tile when PPM (main-header
        /// packed headers, A.7.4) or PPT (tile-part packed headers, A.7.5)
        /// was used. Empty when packet headers are inline with the body. The
        /// concatenation respects tile-part order: tile-part 0's chunk first,
        /// then tile-part 1's, etc.
        /// </summary>
        public byte[] PackedPacketHeaders { get; }

        /// <summary>True when PPM/PPT supplies packet headers separately from the body stream.</summary>
        public bool UsesPackedHeaders => PackedPacketHeaders.Length > 0;

        /// <summary>Per-tile COD override if any tile-part of this tile carried one.</summary>
        public CodSegment? CodOverride { get; }

        /// <summary>Per-tile QCD override if any tile-part of this tile carried one.</summary>
        public QcdSegment? QcdOverride { get; }

        /// <summary>Per-tile COC overrides, indexed by signalling order (component already encoded inside).</summary>
        public IReadOnlyList<CocSegment> CocOverrides { get; }

        /// <summary>Per-tile QCC overrides.</summary>
        public IReadOnlyList<QccSegment> QccOverrides { get; }

        public AssembledTile(
            int tileIndex,
            byte[] packetBody,
            byte[] packedPacketHeaders,
            CodSegment? codOverride,
            QcdSegment? qcdOverride,
            IReadOnlyList<CocSegment> cocOverrides,
            IReadOnlyList<QccSegment> qccOverrides)
        {
            TileIndex = tileIndex;
            PacketBody = packetBody ?? throw new ArgumentNullException(nameof(packetBody));
            PackedPacketHeaders = packedPacketHeaders ?? throw new ArgumentNullException(nameof(packedPacketHeaders));
            CodOverride = codOverride;
            QcdOverride = qcdOverride;
            CocOverrides = cocOverrides ?? throw new ArgumentNullException(nameof(cocOverrides));
            QccOverrides = qccOverrides ?? throw new ArgumentNullException(nameof(qccOverrides));
        }
    }
}
