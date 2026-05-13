using System;
using System.IO;

namespace Jp2Codec.Codestream.Segments
{
    /// <summary>
    /// QCC marker segment (ISO/IEC 15444-1 A.6.5) — Quantization component.
    /// Overrides the default QCD parameters for one component. The Ccqc field
    /// width depends on Csiz from SIZ (1 byte for Csiz &lt; 257, else 2).
    /// </summary>
    internal sealed class QccSegment
    {
        public int ComponentIndex { get; }
        public QuantizationStyle Style { get; }
        public int GuardBits { get; }
        public int[] Exponents { get; }
        public int[] Mantissas { get; }

        public QccSegment(
            int componentIndex,
            QuantizationStyle style,
            int guardBits,
            int[] exponents,
            int[] mantissas)
        {
            ComponentIndex = componentIndex;
            Style = style;
            GuardBits = guardBits;
            Exponents = exponents ?? throw new ArgumentNullException(nameof(exponents));
            Mantissas = mantissas ?? throw new ArgumentNullException(nameof(mantissas));
        }

        public static QccSegment Parse(CodestreamReader r, int numberOfComponents)
        {
            int cqcc = numberOfComponents < 257
                ? r.ReadByte()
                : r.ReadUInt16BigEndian();

            byte sqcc = r.ReadByte();
            var style = (QuantizationStyle)(sqcc & 0x1F);
            int guard = (sqcc >> 5) & 0x07;

            int[] exponents;
            int[] mantissas;

            switch (style)
            {
                case QuantizationStyle.None:
                {
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
                    if ((r.Remaining & 1) != 0)
                        throw new InvalidDataException(
                            $"QCC: scalar-quantization payload remainder {r.Remaining} is not a multiple of 2 bytes.");
                    int n = r.Remaining / 2;
                    if (style == QuantizationStyle.ScalarDerived && n != 1)
                        throw new InvalidDataException(
                            $"QCC: scalar-derived style must carry exactly one (epsilon, mu) pair; got {n}.");
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
                        $"QCC: unknown quantization style {(byte)style} (Sqcc low nibble).");
            }

            return new QccSegment(cqcc, style, guard, exponents, mantissas);
        }
    }
}
