using FontParser.Reader;

namespace FontParser.Tables.Math
{
    public class MathKernInfoRecord
    {
        public MathKernTable? TopRightMathKern { get; }

        public MathKernTable? TopLeftMathKern { get; }

        public MathKernTable? BottomRightMathKern { get; }

        public MathKernTable? BottomLeftMathKern { get; }

        public MathKernInfoRecord(BigEndianReader reader, long mathKernInfoTableStart, ushort[] offsets)
        {
            ushort topRightMathKernOffset = offsets[0];
            ushort topLeftMathKernOffset = offsets[1];
            ushort bottomRightMathKernOffset = offsets[2];
            ushort bottomLeftMathKernOffset = offsets[3];

            if (topRightMathKernOffset > 0)
            {
                reader.Seek(mathKernInfoTableStart + topRightMathKernOffset);
                TopRightMathKern = new MathKernTable(reader);
            }
            if (topLeftMathKernOffset > 0)
            {
                reader.Seek(mathKernInfoTableStart + topLeftMathKernOffset);
                TopLeftMathKern = new MathKernTable(reader);
            }
            if (bottomRightMathKernOffset > 0)
            {
                reader.Seek(mathKernInfoTableStart + bottomRightMathKernOffset);
                BottomRightMathKern = new MathKernTable(reader);
            }

            if (bottomLeftMathKernOffset == 0) return;
            reader.Seek(mathKernInfoTableStart + bottomLeftMathKernOffset);
            BottomLeftMathKern = new MathKernTable(reader);
        }
    }
}