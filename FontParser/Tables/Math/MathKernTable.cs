using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Math
{
    public class MathKernTable
    {
        public List<MathValueRecord> CorrectionHeights { get; } = new List<MathValueRecord>();

        public List<MathValueRecord> KernValues { get; } = new List<MathValueRecord>();

        public MathKernTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort heightCount = reader.ReadUShort();

            for (var i = 0; i < heightCount; i++)
            {
                CorrectionHeights.Add(new MathValueRecord(reader, position));
            }

            for (var i = 0; i < heightCount + 1; i++)
            {
                KernValues.Add(new MathValueRecord(reader, position));
            }
        }
    }
}