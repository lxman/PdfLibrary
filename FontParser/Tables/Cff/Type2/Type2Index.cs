using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type2
{
    public class Type2Index
    {
        public List<List<byte>> Data { get; } = new List<List<byte>>();

        public Type2Index(BigEndianReader reader)
        {
            uint count = reader.ReadUInt32();
            if (count == 0) return;

            byte offSize = reader.ReadByte();
            var offsets = new uint[count + 1];
            for (var i = 0; i < count + 1; i++)
            {
                offsets[i] = reader.ReadOffset(offSize);
            }

            for (var i = 0; i < count; i++)
            {
                uint length = offsets[i + 1] - offsets[i];
                Data.Add(new List<byte>(reader.ReadBytes((int)length)));
            }
        }
    }
}