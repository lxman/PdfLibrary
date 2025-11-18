using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type1
{
    public class Encoding0 : IEncoding
    {
        public byte Format => 0;

        public List<byte> CodeArray { get; }

        public Encoding0(BigEndianReader reader)
        {
            byte nCodes = reader.ReadByte();
            CodeArray = reader.ReadBytes(nCodes).ToList();
        }
    }
}