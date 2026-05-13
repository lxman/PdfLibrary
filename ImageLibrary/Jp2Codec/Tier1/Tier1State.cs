using System;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// Per-coefficient state grid for one code block during EBCOT Tier-1
    /// decoding (ISO/IEC 15444-1 Annex D). Holds the four bit-flags per
    /// coefficient that the three coding passes inspect and update —
    /// significance (σ), sign, visited-in-this-bit-plane (π), and
    /// already-refined (μ) — alongside an int per coefficient that
    /// accumulates the decoded magnitude bits MSB-first.
    ///
    /// Storage layout: row-major flat arrays with a one-coefficient guard
    /// band on every side. The guard band stays zero for the lifetime of
    /// the state, which lets every 8-neighbour context lookup proceed
    /// without bounds checks: the off-grid neighbours read as
    /// non-significant just like the spec requires for code-block edges.
    /// Height is also rounded up to a multiple of four so the pass loops
    /// can walk full stripes without a partial-stripe special case; the
    /// extra rows beyond <see cref="Height"/> are padding and never appear
    /// in the extracted output.
    /// </summary>
    internal sealed class Tier1State
    {
        public const byte SignificanceFlag = 0x01;
        public const byte SignFlag         = 0x02;
        public const byte VisitedFlag      = 0x04;
        public const byte RefinedFlag      = 0x08;

        private const byte FlagsExceptVisited = unchecked((byte)~VisitedFlag);

        public int Width { get; }
        public int Height { get; }
        public int PaddedHeight { get; }
        public int StripeCount { get; }

        private readonly byte[] _flags;
        private readonly int[] _magnitudes;
        private readonly int _stride;

        public Tier1State(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");

            Width = width;
            Height = height;
            PaddedHeight = (height + 3) & ~3;
            StripeCount = PaddedHeight / 4;

            _stride = width + 2;
            int totalRows = PaddedHeight + 2;
            _flags = new byte[_stride * totalRows];
            _magnitudes = new int[_stride * totalRows];
        }

        // Convert (x, y) in code-block coordinates (origin top-left) to a
        // flat index into the padded buffers. Both x and y may be -1 (one
        // step into the guard band) but no further.
        private int Index(int x, int y) => ((y + 1) * _stride) + (x + 1);

        public byte GetFlags(int x, int y) => _flags[Index(x, y)];

        public bool HasFlag(int x, int y, byte mask) => (_flags[Index(x, y)] & mask) != 0;

        public void SetFlag(int x, int y, byte mask) => _flags[Index(x, y)] |= mask;

        public int GetMagnitude(int x, int y) => _magnitudes[Index(x, y)];

        public void SetMagnitude(int x, int y, int magnitude) => _magnitudes[Index(x, y)] = magnitude;

        /// <summary>
        /// Clear the per-bit-plane visited flag (π) on every coefficient. Called
        /// once before each bit-plane's first pass so SPP can re-mark which
        /// coefficients it visits. The other three flags are preserved.
        /// </summary>
        public void ResetVisited()
        {
            for (var i = 0; i < _flags.Length; i++)
                _flags[i] &= FlagsExceptVisited;
        }

        /// <summary>
        /// Return the eight-neighbour significance pattern centred on
        /// (<paramref name="x"/>, <paramref name="y"/>) as a byte:
        /// bit 0 = NW, bit 1 = N, bit 2 = NE, bit 3 = W,
        /// bit 4 = E,  bit 5 = SW, bit 6 = S, bit 7 = SE.
        /// Off-grid neighbours read as zero thanks to the guard band.
        /// When <paramref name="maskSouthRow"/> is true the SW, S, SE bits
        /// are forced to zero — used by the vertically-causal context
        /// formation style (Annex D.7) to keep context computation
        /// independent of code-block scans that have not yet been processed.
        /// </summary>
        public byte GetSignificanceNeighbourhood(int x, int y, bool maskSouthRow = false)
        {
            int idx = Index(x, y);
            byte n = 0;
            if ((_flags[idx - _stride - 1] & SignificanceFlag) != 0) n |= 0x01;
            if ((_flags[idx - _stride    ] & SignificanceFlag) != 0) n |= 0x02;
            if ((_flags[idx - _stride + 1] & SignificanceFlag) != 0) n |= 0x04;
            if ((_flags[idx - 1          ] & SignificanceFlag) != 0) n |= 0x08;
            if ((_flags[idx + 1          ] & SignificanceFlag) != 0) n |= 0x10;
            if (maskSouthRow) return n;
            if ((_flags[idx + _stride - 1] & SignificanceFlag) != 0) n |= 0x20;
            if ((_flags[idx + _stride    ] & SignificanceFlag) != 0) n |= 0x40;
            if ((_flags[idx + _stride + 1] & SignificanceFlag) != 0) n |= 0x80;
            return n;
        }

        /// <summary>
        /// Count the significant 8-neighbours of (<paramref name="x"/>, <paramref name="y"/>).
        /// Cheaper than <see cref="GetSignificanceNeighbourhood"/> for the
        /// magnitude-refinement context which only needs "any vs. none".
        /// When <paramref name="maskSouthRow"/> is true the three southern
        /// neighbours are skipped — VSC convention, see
        /// <see cref="GetSignificanceNeighbourhood"/>.
        /// </summary>
        public int CountSignificantNeighbours(int x, int y, bool maskSouthRow = false)
        {
            int idx = Index(x, y);
            int count = 0;
            if ((_flags[idx - _stride - 1] & SignificanceFlag) != 0) count++;
            if ((_flags[idx - _stride    ] & SignificanceFlag) != 0) count++;
            if ((_flags[idx - _stride + 1] & SignificanceFlag) != 0) count++;
            if ((_flags[idx - 1          ] & SignificanceFlag) != 0) count++;
            if ((_flags[idx + 1          ] & SignificanceFlag) != 0) count++;
            if (maskSouthRow) return count;
            if ((_flags[idx + _stride - 1] & SignificanceFlag) != 0) count++;
            if ((_flags[idx + _stride    ] & SignificanceFlag) != 0) count++;
            if ((_flags[idx + _stride + 1] & SignificanceFlag) != 0) count++;
            return count;
        }

        /// <summary>
        /// Encoded (sig, sign) state of one cardinal neighbour as a small
        /// trit used by sign-coding (Annex D.5.2):
        ///   0 = neighbour insignificant (or off-grid),
        ///  +1 = neighbour significant with positive sign,
        ///  -1 = neighbour significant with negative sign.
        /// </summary>
        public int GetSignContribution(int x, int y, NeighbourDirection direction)
        {
            int idx = direction switch
            {
                NeighbourDirection.North => Index(x, y) - _stride,
                NeighbourDirection.South => Index(x, y) + _stride,
                NeighbourDirection.West  => Index(x, y) - 1,
                NeighbourDirection.East  => Index(x, y) + 1,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
            };
            byte f = _flags[idx];
            if ((f & SignificanceFlag) == 0) return 0;
            return (f & SignFlag) != 0 ? -1 : +1;
        }
    }

    internal enum NeighbourDirection
    {
        North,
        South,
        West,
        East,
    }
}
