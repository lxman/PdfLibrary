using System;
using System.IO;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// COC marker segment (ISO/IEC 15444-1 A.6.2) — Coding style component.
    /// Overrides the default COD parameters for one component. Optional; only
    /// the SPcoc fields differ from <see cref="CodSegment"/>'s SPcod (no
    /// SGcod, since the global coding-style parameters cannot vary per
    /// component).
    /// </summary>
    internal sealed class CocSegment
    {
        public int ComponentIndex { get; }
        public bool UseExplicitPrecincts { get; }
        public int DecompositionLevels { get; }
        public int CodeBlockWidthExponent { get; }
        public int CodeBlockHeightExponent { get; }
        public CodeBlockStyle CodeBlockStyle { get; }
        public WaveletTransform WaveletTransform { get; }
        public int[] PrecinctWidthExponents { get; }
        public int[] PrecinctHeightExponents { get; }

        public CocSegment(
            int componentIndex,
            bool useExplicitPrecincts,
            int decompositionLevels,
            int codeBlockWidthExponent,
            int codeBlockHeightExponent,
            CodeBlockStyle codeBlockStyle,
            WaveletTransform waveletTransform,
            int[] precinctWidthExponents,
            int[] precinctHeightExponents)
        {
            ComponentIndex = componentIndex;
            UseExplicitPrecincts = useExplicitPrecincts;
            DecompositionLevels = decompositionLevels;
            CodeBlockWidthExponent = codeBlockWidthExponent;
            CodeBlockHeightExponent = codeBlockHeightExponent;
            CodeBlockStyle = codeBlockStyle;
            WaveletTransform = waveletTransform;
            PrecinctWidthExponents = precinctWidthExponents ?? throw new ArgumentNullException(nameof(precinctWidthExponents));
            PrecinctHeightExponents = precinctHeightExponents ?? throw new ArgumentNullException(nameof(precinctHeightExponents));
        }

        /// <summary>
        /// Parse a COC payload. <paramref name="numberOfComponents"/> (Csiz from
        /// SIZ) selects the Ccoc field width — 1 byte when Csiz &lt; 257, else 2 bytes.
        /// </summary>
        public static CocSegment Parse(CodestreamReader r, int numberOfComponents)
        {
            int ccoc = numberOfComponents < 257
                ? r.ReadByte()
                : r.ReadUInt16BigEndian();

            byte scoc = r.ReadByte();
            bool explicitPrecincts = (scoc & 0x01) != 0;

            byte nl = r.ReadByte();
            if (nl > 32)
                throw new InvalidDataException($"COC: SPcoc decomposition levels {nl} > 32.");

            int xcbExp = r.ReadByte() + 2;
            int ycbExp = r.ReadByte() + 2;
            if (xcbExp < 2 || xcbExp > 10 || ycbExp < 2 || ycbExp > 10)
                throw new InvalidDataException(
                    $"COC: SPcoc code-block exponents ({xcbExp}, {ycbExp}) outside [2, 10].");
            if (xcbExp + ycbExp > 12)
                throw new InvalidDataException(
                    $"COC: SPcoc code-block area exponent sum {xcbExp + ycbExp} > 12.");

            var style = (CodeBlockStyle)r.ReadByte();

            byte tx = r.ReadByte();
            if (tx > 1)
                throw new InvalidDataException($"COC: SPcoc transform {tx} not in [0, 1].");
            var transform = (WaveletTransform)tx;

            int resolutionLevels = nl + 1;
            var ppx = new int[resolutionLevels];
            var ppy = new int[resolutionLevels];
            if (explicitPrecincts)
            {
                for (var i = 0; i < resolutionLevels; i++)
                {
                    byte pp = r.ReadByte();
                    ppx[i] = pp & 0x0F;
                    ppy[i] = (pp >> 4) & 0x0F;
                }
            }
            else
            {
                for (var i = 0; i < resolutionLevels; i++)
                {
                    ppx[i] = 15;
                    ppy[i] = 15;
                }
            }

            return new CocSegment(
                ccoc, explicitPrecincts,
                nl, xcbExp, ycbExp, style, transform,
                ppx, ppy);
        }
    }
}
