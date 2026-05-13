using System;
using System.IO;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// SIZ marker segment (ISO/IEC 15444-1 A.5.1) — Image and tile size.
    /// Required in the main header, immediately after SOC. Conveys the
    /// reference grid geometry, tile dimensions, and per-component bit depth
    /// and subsampling factors.
    /// </summary>
    internal sealed class SizSegment
    {
        /// <summary>Capability indicator (Rsiz). 0 = Part 1 baseline; 1/2 = profiles.</summary>
        public ushort Capabilities { get; }

        /// <summary>Reference grid width (Xsiz). Image right edge in grid coordinates.</summary>
        public uint ReferenceGridWidth { get; }

        /// <summary>Reference grid height (Ysiz). Image bottom edge in grid coordinates.</summary>
        public uint ReferenceGridHeight { get; }

        /// <summary>Horizontal offset of the image origin on the reference grid (XOsiz).</summary>
        public uint ImageHorizontalOffset { get; }

        /// <summary>Vertical offset of the image origin on the reference grid (YOsiz).</summary>
        public uint ImageVerticalOffset { get; }

        /// <summary>Tile width on the reference grid (XTsiz).</summary>
        public uint TileWidth { get; }

        /// <summary>Tile height on the reference grid (YTsiz).</summary>
        public uint TileHeight { get; }

        /// <summary>Horizontal offset of the anchor of the first tile (XTOsiz).</summary>
        public uint TileHorizontalOffset { get; }

        /// <summary>Vertical offset of the anchor of the first tile (YTOsiz).</summary>
        public uint TileVerticalOffset { get; }

        /// <summary>Component descriptors, one per component (Csiz components).</summary>
        public SizComponent[] Components { get; }

        /// <summary>Convenience accessor: number of components (Csiz).</summary>
        public int NumberOfComponents => Components.Length;

        /// <summary>Image width in samples on the reference grid (Xsiz - XOsiz).</summary>
        public int ImageWidth => checked((int)(ReferenceGridWidth - ImageHorizontalOffset));

        /// <summary>Image height in samples on the reference grid (Ysiz - YOsiz).</summary>
        public int ImageHeight => checked((int)(ReferenceGridHeight - ImageVerticalOffset));

        /// <summary>
        /// Width of component <paramref name="c"/> on its own sampling grid, derived per A.5.1
        /// as ceil(Xsiz / XRsiz_c) - ceil(XOsiz / XRsiz_c).
        /// </summary>
        public int ComponentWidth(int c)
        {
            byte xr = Components[c].HorizontalSubsampling;
            return (int)(CeilDiv(ReferenceGridWidth, xr) - CeilDiv(ImageHorizontalOffset, xr));
        }

        /// <summary>
        /// Height of component <paramref name="c"/> on its own sampling grid, derived per A.5.1
        /// as ceil(Ysiz / YRsiz_c) - ceil(YOsiz / YRsiz_c).
        /// </summary>
        public int ComponentHeight(int c)
        {
            byte yr = Components[c].VerticalSubsampling;
            return (int)(CeilDiv(ReferenceGridHeight, yr) - CeilDiv(ImageVerticalOffset, yr));
        }

        public SizSegment(
            ushort capabilities,
            uint referenceGridWidth, uint referenceGridHeight,
            uint imageHorizontalOffset, uint imageVerticalOffset,
            uint tileWidth, uint tileHeight,
            uint tileHorizontalOffset, uint tileVerticalOffset,
            SizComponent[] components)
        {
            Capabilities = capabilities;
            ReferenceGridWidth = referenceGridWidth;
            ReferenceGridHeight = referenceGridHeight;
            ImageHorizontalOffset = imageHorizontalOffset;
            ImageVerticalOffset = imageVerticalOffset;
            TileWidth = tileWidth;
            TileHeight = tileHeight;
            TileHorizontalOffset = tileHorizontalOffset;
            TileVerticalOffset = tileVerticalOffset;
            Components = components ?? throw new ArgumentNullException(nameof(components));
        }

        /// <summary>
        /// Parse a SIZ payload from <paramref name="r"/>, which must be positioned
        /// just past the Lsiz field (i.e. produced by <see cref="CodestreamReader.ReadSegment"/>).
        /// </summary>
        public static SizSegment Parse(CodestreamReader r)
        {
            ushort rsiz = r.ReadUInt16BigEndian();
            uint xsiz = r.ReadUInt32BigEndian();
            uint ysiz = r.ReadUInt32BigEndian();
            uint xosiz = r.ReadUInt32BigEndian();
            uint yosiz = r.ReadUInt32BigEndian();
            uint xtsiz = r.ReadUInt32BigEndian();
            uint ytsiz = r.ReadUInt32BigEndian();
            uint xtosiz = r.ReadUInt32BigEndian();
            uint ytosiz = r.ReadUInt32BigEndian();
            ushort csiz = r.ReadUInt16BigEndian();

            // Spec A.5.1: Csiz is in [1, 16 384]. Reject obviously bad values
            // before allocating a per-component array.
            if (csiz < 1 || csiz > 16384)
                throw new InvalidDataException($"SIZ: Csiz={csiz} outside legal range [1, 16384].");

            if (xsiz <= xosiz || ysiz <= yosiz)
                throw new InvalidDataException(
                    $"SIZ: degenerate image extent — Xsiz={xsiz}, XOsiz={xosiz}, Ysiz={ysiz}, YOsiz={yosiz}.");

            if (xtsiz == 0 || ytsiz == 0)
                throw new InvalidDataException($"SIZ: tile dimensions must be non-zero (XTsiz={xtsiz}, YTsiz={ytsiz}).");

            var components = new SizComponent[csiz];
            for (var i = 0; i < csiz; i++)
            {
                byte ssiz = r.ReadByte();
                byte xrsiz = r.ReadByte();
                byte yrsiz = r.ReadByte();
                if (xrsiz == 0 || yrsiz == 0)
                    throw new InvalidDataException(
                        $"SIZ: component {i} subsampling must be >=1 (XRsiz={xrsiz}, YRsiz={yrsiz}).");

                bool isSigned = (ssiz & 0x80) != 0;
                int bitDepth = (ssiz & 0x7F) + 1; // Ssiz field stores (bitdepth - 1)
                if (bitDepth < 1 || bitDepth > 38)
                    throw new InvalidDataException($"SIZ: component {i} bit depth {bitDepth} outside [1, 38].");

                components[i] = new SizComponent(bitDepth, isSigned, xrsiz, yrsiz);
            }

            return new SizSegment(
                rsiz,
                xsiz, ysiz,
                xosiz, yosiz,
                xtsiz, ytsiz,
                xtosiz, ytosiz,
                components);
        }

        private static uint CeilDiv(uint num, byte den) => (num + den - 1u) / den;
    }

    /// <summary>One component entry from a SIZ segment (Ssiz/XRsiz/YRsiz triplet).</summary>
    internal readonly struct SizComponent
    {
        /// <summary>Bit depth of the component (1..38).</summary>
        public int BitDepth { get; }

        /// <summary>Whether the component samples are signed (two's-complement).</summary>
        public bool IsSigned { get; }

        /// <summary>Horizontal subsampling factor (XRsiz, 1..255). 1 = full resolution.</summary>
        public byte HorizontalSubsampling { get; }

        /// <summary>Vertical subsampling factor (YRsiz, 1..255). 1 = full resolution.</summary>
        public byte VerticalSubsampling { get; }

        public SizComponent(int bitDepth, bool isSigned, byte horizontalSubsampling, byte verticalSubsampling)
        {
            BitDepth = bitDepth;
            IsSigned = isSigned;
            HorizontalSubsampling = horizontalSubsampling;
            VerticalSubsampling = verticalSubsampling;
        }
    }
}
