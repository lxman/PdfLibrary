using System;
using System.IO;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// "Comma code" used in packet headers to signal the increment to a
    /// code-block's Lblock length-prefix value (ISO/IEC 15444-1 B.10.7).
    /// A run of <c>n</c> '1' bits followed by a single '0' bit encodes
    /// the integer <c>n</c>.
    /// </summary>
    internal static class LblockIncrement
    {
        // A run longer than this is almost certainly a malformed stream; the
        // largest Lblock the spec ever needs is bounded by code-block byte
        // sizes that fit in 32-bit ints, so an increment past ~32 would only
        // appear in pathological / corrupt inputs.
        private const int SanityCap = 32;

        public static int Read(PacketHeaderBitReader reader)
        {
            if (reader is null) throw new ArgumentNullException(nameof(reader));

            var increment = 0;
            while (reader.ReadBit() == 1)
            {
                increment++;
                if (increment > SanityCap)
                    throw new InvalidDataException(
                        "Lblock increment exceeded sanity cap; codestream is malformed.");
            }
            return increment;
        }
    }
}
