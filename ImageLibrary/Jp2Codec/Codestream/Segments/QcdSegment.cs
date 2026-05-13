using System;
using System.IO;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// Quantization style per ISO/IEC 15444-1 Table A-28.
    /// </summary>
    internal enum QuantizationStyle : byte
    {
        /// <summary>No quantization (used with 5/3 reversible transform; mantissa is implicitly 0).</summary>
        None = 0,
        /// <summary>Scalar quantization, derived — one step in QCD, others derived by the spec rules.</summary>
        ScalarDerived = 1,
        /// <summary>Scalar quantization, expounded — one step explicit per subband.</summary>
        ScalarExpounded = 2,
    }

    /// <summary>
    /// QCD marker segment (ISO/IEC 15444-1 A.6.4) — Quantization default.
    /// Required in the main header. Provides quantization step sizes for the
    /// wavelet subbands of every component (unless overridden by a QCC).
    ///
    /// The number of subbands equals 3*NL + 1 (with NL coming from COD). For
    /// derived style only the LL subband is encoded; the rest are reconstructed
    /// at use-site per E.1.2.
    /// </summary>
    internal sealed class QcdSegment
    {
        /// <summary>Quantization style (low 5 bits of Sqcd).</summary>
        public QuantizationStyle Style { get; }

        /// <summary>Number of guard bits (high 3 bits of Sqcd, 0..7).</summary>
        public int GuardBits { get; }

        /// <summary>
        /// Per-subband exponent epsilon (5 bits each). Length = 1 for derived
        /// style, 3*NL + 1 for none/expounded. For style <see cref="QuantizationStyle.None"/>
        /// (reversible) the mantissa is implicit zero.
        /// </summary>
        public int[] Exponents { get; }

        /// <summary>
        /// Per-subband mantissa mu (11 bits each). Empty for reversible style;
        /// length matches <see cref="Exponents"/> for derived/expounded.
        /// </summary>
        public int[] Mantissas { get; }

        public QcdSegment(
            QuantizationStyle style,
            int guardBits,
            int[] exponents,
            int[] mantissas)
        {
            Style = style;
            GuardBits = guardBits;
            Exponents = exponents ?? throw new ArgumentNullException(nameof(exponents));
            Mantissas = mantissas ?? throw new ArgumentNullException(nameof(mantissas));
        }

        /// <summary>
        /// Parse a QCD payload. The reader length determines how many
        /// subbands are encoded — the caller (main-header walker) does not
        /// need to know NL beforehand.
        /// </summary>
        public static QcdSegment Parse(CodestreamReader r)
        {
            byte sqcd = r.ReadByte();
            var style = (QuantizationStyle)(sqcd & 0x1F);
            int guard = (sqcd >> 5) & 0x07;

            int[] exponents;
            int[] mantissas;

            switch (style)
            {
                case QuantizationStyle.None:
                {
                    // Reversible: 1 byte per subband; bits 3..7 = exponent.
                    int n = r.Remaining;
                    exponents = new int[n];
                    mantissas = Array.Empty<int>();
                    for (var i = 0; i < n; i++)
                        exponents[i] = (r.ReadByte() >> 3) & 0x1F;
                    break;
                }
                case QuantizationStyle.ScalarDerived:
                case QuantizationStyle.ScalarExpounded:
                {
                    // 2 bytes per subband; high 5 bits = exponent, low 11 bits = mantissa.
                    if ((r.Remaining & 1) != 0)
                        throw new InvalidDataException(
                            $"QCD: scalar-quantization payload remainder {r.Remaining} is not a multiple of 2 bytes.");
                    int n = r.Remaining / 2;
                    if (style == QuantizationStyle.ScalarDerived && n != 1)
                    {
                        throw new InvalidDataException(
                            $"QCD: scalar-derived style must carry exactly one (epsilon, mu) pair; got {n}.");
                    }
                    exponents = new int[n];
                    mantissas = new int[n];
                    for (var i = 0; i < n; i++)
                    {
                        ushort word = r.ReadUInt16BigEndian();
                        exponents[i] = (word >> 11) & 0x1F;
                        mantissas[i] = word & 0x7FF;
                    }
                    break;
                }
                default:
                    throw new InvalidDataException(
                        $"QCD: unknown quantization style {(byte)style} (Sqcd low nibble).");
            }

            return new QcdSegment(style, guard, exponents, mantissas);
        }
    }
}
