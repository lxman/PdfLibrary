using System;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// Variable-length code (ISO/IEC 15444-1 Table B.4) used in packet
    /// headers to signal the number of new coding passes contributed by a
    /// code-block in this packet. Range is 1..164.
    ///
    /// <list type="bullet">
    ///   <item><c>0</c>            → 1 pass</item>
    ///   <item><c>10</c>           → 2 passes</item>
    ///   <item><c>11_xx</c>        → 3..5 passes (xx ∈ {00, 01, 10})</item>
    ///   <item><c>1111_xxxxx</c>   → 6..36 passes (xxxxx ∈ 0..30)</item>
    ///   <item><c>1111_11111_xxxxxxx</c> → 37..164 passes</item>
    /// </list>
    /// </summary>
    internal static class CodingPassLengthCode
    {
        public static int Decode(PacketHeaderBitReader reader)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));

            if (reader.ReadBit() == 0) return 1;
            if (reader.ReadBit() == 0) return 2;

            int twoBits = reader.ReadBits(2);
            if (twoBits != 3) return 3 + twoBits;       // 3, 4, 5

            int fiveBits = reader.ReadBits(5);
            if (fiveBits != 31) return 6 + fiveBits;    // 6..36

            return 37 + reader.ReadBits(7);             // 37..164
        }
    }
}
