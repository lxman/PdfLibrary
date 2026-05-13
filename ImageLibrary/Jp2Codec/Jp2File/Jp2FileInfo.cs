using System;

namespace Jp2Codec.Jp2File
{
    /// <summary>
    /// Result of walking a JP2 file or sniffing a raw J2K codestream — gives
    /// the consumer the embedded J2K codestream byte range plus any colorspace
    /// metadata the wrapper carried.
    /// </summary>
    internal sealed class Jp2FileInfo
    {
        /// <summary>True if the input began with a JP2 signature box; false if raw J2K.</summary>
        public bool IsJp2File { get; }

        /// <summary>Image height from ihdr (0 if raw J2K).</summary>
        public int Height { get; }

        /// <summary>Image width from ihdr (0 if raw J2K).</summary>
        public int Width { get; }

        /// <summary>Number of components from ihdr (0 if raw J2K).</summary>
        public int NumberOfComponents { get; }

        /// <summary>
        /// Per-component bit depth. Length 1 when ihdr.BPC carries a uniform
        /// value, otherwise length = NumberOfComponents (from bpcc).
        /// </summary>
        public int[] BitsPerComponent { get; }

        /// <summary>Whether each component is signed (length matches BitsPerComponent).</summary>
        public bool[] ComponentSigned { get; }

        /// <summary>Color-space hint from the first colr box.</summary>
        public Jp2ColorSpace ColorSpace { get; }

        /// <summary>
        /// Raw ICC profile bytes from the first colr box when METH=2 or 3
        /// (<see cref="Jp2ColorSpace.RestrictedIcc"/> /
        /// <see cref="Jp2ColorSpace.AnyIcc"/>); null otherwise.
        /// </summary>
        public byte[]? IccProfile { get; }

        /// <summary>Byte offset of the embedded J2K codestream within the input buffer.</summary>
        public int CodestreamOffset { get; }

        /// <summary>Length of the embedded J2K codestream in bytes.</summary>
        public int CodestreamLength { get; }

        /// <summary>
        /// JP2 palette box (pclr) when present; null otherwise. When present a
        /// <see cref="ComponentMapping"/> describes how palette columns map to
        /// output channels.
        /// </summary>
        public JpPalette? Palette { get; }

        /// <summary>JP2 component-mapping box (cmap) when present; null otherwise.</summary>
        public JpComponentMapping? ComponentMapping { get; }

        /// <summary>
        /// JP2 channel-definition box (cdef) when present; null otherwise.
        /// Defines the colour-channel association for each codestream
        /// component, so downstream colour conversion can find Y/Cb/Cr (or
        /// R/G/B) regardless of the order they appear in the codestream.
        /// </summary>
        public JpChannelDefinition? ChannelDefinition { get; }

        public Jp2FileInfo(
            bool isJp2File,
            int height, int width, int numberOfComponents,
            int[] bitsPerComponent, bool[] componentSigned,
            Jp2ColorSpace colorSpace,
            int codestreamOffset, int codestreamLength,
            JpPalette? palette = null,
            JpComponentMapping? componentMapping = null,
            byte[]? iccProfile = null,
            JpChannelDefinition? channelDefinition = null)
        {
            IsJp2File = isJp2File;
            Height = height;
            Width = width;
            NumberOfComponents = numberOfComponents;
            BitsPerComponent = bitsPerComponent ?? Array.Empty<int>();
            ComponentSigned = componentSigned ?? Array.Empty<bool>();
            ColorSpace = colorSpace;
            CodestreamOffset = codestreamOffset;
            CodestreamLength = codestreamLength;
            Palette = palette;
            ComponentMapping = componentMapping;
            IccProfile = iccProfile;
            ChannelDefinition = channelDefinition;
        }
    }
}
