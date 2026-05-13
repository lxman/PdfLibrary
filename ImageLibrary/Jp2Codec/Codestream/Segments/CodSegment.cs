using System;
using System.IO;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// COD marker segment (ISO/IEC 15444-1 A.6.1) — Coding style default.
    /// Required in the main header. Sets default coding parameters for all
    /// components; COC segments may override per-component.
    /// </summary>
    internal sealed class CodSegment
    {
        // ---- Scod flags (Table A-13) ----
        /// <summary>True if precinct sizes are explicitly listed in SPcod (Scod bit 0).</summary>
        public bool UseExplicitPrecincts { get; }

        /// <summary>True if SOP markers are present in the packet stream (Scod bit 1).</summary>
        public bool UseSopMarkers { get; }

        /// <summary>True if EPH markers follow each packet header (Scod bit 2).</summary>
        public bool UseEphMarkers { get; }

        // ---- SGcod parameters (Table A-14, A-16) ----
        /// <summary>Order in which packets appear within each tile-part.</summary>
        public ProgressionOrder ProgressionOrder { get; }

        /// <summary>Number of quality layers (1..65 535).</summary>
        public int NumberOfLayers { get; }

        /// <summary>
        /// True if a multiple-component transform applies to the first three
        /// components (Table A-17, MCT byte = 1).
        /// </summary>
        public bool UseMultipleComponentTransform { get; }

        // ---- SPcod parameters (Table A-15, A-18..A-21) ----
        /// <summary>Number of wavelet decomposition levels (NL, 0..32).</summary>
        public int DecompositionLevels { get; }

        /// <summary>Code-block width = 2^<see cref="CodeBlockWidthExponent"/>. Range 2..10.</summary>
        public int CodeBlockWidthExponent { get; }

        /// <summary>Code-block height = 2^<see cref="CodeBlockHeightExponent"/>. Range 2..10.</summary>
        public int CodeBlockHeightExponent { get; }

        /// <summary>Code-block coding-style flags (Table A-19).</summary>
        public CodeBlockStyle CodeBlockStyle { get; }

        /// <summary>Wavelet transform kernel (Table A-20).</summary>
        public WaveletTransform WaveletTransform { get; }

        /// <summary>
        /// Precinct width exponent per resolution level (length = NL + 1).
        /// PPx values are 0..15 → precinct width = 2^PPx. Only meaningful when
        /// <see cref="UseExplicitPrecincts"/> is true; otherwise all entries
        /// default to 15 (i.e. 32 768).
        /// </summary>
        public int[] PrecinctWidthExponents { get; }

        /// <summary>Precinct height exponent per resolution level (length = NL + 1).</summary>
        public int[] PrecinctHeightExponents { get; }

        public CodSegment(
            bool useExplicitPrecincts,
            bool useSopMarkers,
            bool useEphMarkers,
            ProgressionOrder progressionOrder,
            int numberOfLayers,
            bool useMultipleComponentTransform,
            int decompositionLevels,
            int codeBlockWidthExponent,
            int codeBlockHeightExponent,
            CodeBlockStyle codeBlockStyle,
            WaveletTransform waveletTransform,
            int[] precinctWidthExponents,
            int[] precinctHeightExponents)
        {
            UseExplicitPrecincts = useExplicitPrecincts;
            UseSopMarkers = useSopMarkers;
            UseEphMarkers = useEphMarkers;
            ProgressionOrder = progressionOrder;
            NumberOfLayers = numberOfLayers;
            UseMultipleComponentTransform = useMultipleComponentTransform;
            DecompositionLevels = decompositionLevels;
            CodeBlockWidthExponent = codeBlockWidthExponent;
            CodeBlockHeightExponent = codeBlockHeightExponent;
            CodeBlockStyle = codeBlockStyle;
            WaveletTransform = waveletTransform;
            PrecinctWidthExponents = precinctWidthExponents
                ?? throw new ArgumentNullException(nameof(precinctWidthExponents));
            PrecinctHeightExponents = precinctHeightExponents
                ?? throw new ArgumentNullException(nameof(precinctHeightExponents));
        }

        public static CodSegment Parse(CodestreamReader r)
        {
            byte scod = r.ReadByte();
            bool explicitPrecincts = (scod & 0x01) != 0;
            bool sop = (scod & 0x02) != 0;
            bool eph = (scod & 0x04) != 0;

            // SGcod (5 bytes; Table A-14)
            byte progByte = r.ReadByte();
            if (progByte > 4)
                throw new InvalidDataException($"COD: SGcod progression order {progByte} not in [0, 4].");
            var progression = (ProgressionOrder)progByte;

            ushort layers = r.ReadUInt16BigEndian();
            if (layers < 1)
                throw new InvalidDataException($"COD: SGcod number of layers must be >= 1, got {layers}.");

            byte mct = r.ReadByte();
            if (mct > 1)
                throw new InvalidDataException($"COD: SGcod multiple-component transform {mct} not in [0, 1].");

            // SPcod (Table A-15, A-18..A-21)
            byte nl = r.ReadByte();
            if (nl > 32)
                throw new InvalidDataException($"COD: SPcod decomposition levels {nl} > 32.");

            // Stored values are exponent-2 (i.e. xcb_value = xcb_exponent - 2).
            int xcbExp = r.ReadByte() + 2;
            int ycbExp = r.ReadByte() + 2;
            if (xcbExp < 2 || xcbExp > 10 || ycbExp < 2 || ycbExp > 10)
                throw new InvalidDataException(
                    $"COD: SPcod code-block exponents ({xcbExp}, {ycbExp}) outside [2, 10].");
            if (xcbExp + ycbExp > 12)
                throw new InvalidDataException(
                    $"COD: SPcod code-block area exponent sum {xcbExp + ycbExp} > 12 (4096-coeff cap).");

            var style = (CodeBlockStyle)r.ReadByte();

            byte tx = r.ReadByte();
            if (tx > 1)
                throw new InvalidDataException($"COD: SPcod transform {tx} not in [0, 1].");
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
                    // Default precinct size per Table A-21: PPx = PPy = 15 (max).
                    ppx[i] = 15;
                    ppy[i] = 15;
                }
            }

            return new CodSegment(
                explicitPrecincts, sop, eph,
                progression, layers, mct != 0,
                nl, xcbExp, ycbExp, style, transform,
                ppx, ppy);
        }
    }
}
