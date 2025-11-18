using System.Text;
using FontParser.Reader;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Meta
{
    public class DataMap
    {
        public string Tag { get; }

        public string Data { get; private set; }

        private readonly uint _dataOffset;
        private readonly uint _dataLength;

        public DataMap(BigEndianReader reader)
        {
            Tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            _dataOffset = reader.ReadUInt32();
            _dataLength = reader.ReadUInt32();
        }

        public void Process(BigEndianReader reader)
        {
            reader.Seek(_dataOffset);
            Data = Encoding.ASCII.GetString(reader.ReadBytes(_dataLength));
        }
    }
}