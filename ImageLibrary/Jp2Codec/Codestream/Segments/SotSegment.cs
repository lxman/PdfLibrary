using System.IO;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// SOT marker segment (ISO/IEC 15444-1 A.4.2) — Start of tile-part. Marks
    /// the beginning of a tile-part within a codestream; carries enough info
    /// for random tile access (length, tile index, tile-part index).
    /// </summary>
    internal sealed class SotSegment
    {
        /// <summary>Tile index (Isot, 0..number-of-tiles − 1).</summary>
        public int TileIndex { get; }

        /// <summary>
        /// Tile-part length in bytes (Psot), measured from the first byte of
        /// SOT through the end of the tile-part. Zero means "tile-part extends
        /// to EOC or the next SOT" (used by streaming encoders).
        /// </summary>
        public uint TilePartLength { get; }

        /// <summary>Tile-part index of this tile (TPsot, 0..TNsot − 1).</summary>
        public int TilePartIndex { get; }

        /// <summary>
        /// Total number of tile-parts of this tile (TNsot). Zero means
        /// unknown (legal per the spec — only the final tile-part is required
        /// to set it).
        /// </summary>
        public int TilePartCount { get; }

        public SotSegment(int tileIndex, uint tilePartLength, int tilePartIndex, int tilePartCount)
        {
            TileIndex = tileIndex;
            TilePartLength = tilePartLength;
            TilePartIndex = tilePartIndex;
            TilePartCount = tilePartCount;
        }

        public static SotSegment Parse(CodestreamReader r)
        {
            if (r.Length != 8)
                throw new InvalidDataException(
                    $"SOT: payload must be exactly 8 bytes after Lsot, got {r.Length}.");

            ushort isot = r.ReadUInt16BigEndian();
            uint psot = r.ReadUInt32BigEndian();
            byte tpsot = r.ReadByte();
            byte tnsot = r.ReadByte();

            return new SotSegment(isot, psot, tpsot, tnsot);
        }
    }
}
