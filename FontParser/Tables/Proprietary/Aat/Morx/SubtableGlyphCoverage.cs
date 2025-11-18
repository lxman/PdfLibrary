using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class SubtableGlyphCoverage
    {
        public List<uint> SubtableOffsets { get; } = new List<uint>();

        public List<byte> CoverageBitfields { get; } = new List<byte>();

        public SubtableGlyphCoverage(BigEndianReader reader)
        {
        }
    }
}