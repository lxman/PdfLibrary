using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class NameLookup
    {
        public string LookupName { get; }

        public PointersToAnchorClassLookups PointersToAnchorClassLookups { get; }

        public NameLookup(BigEndianReader reader, long start)
        {
            LookupName = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint offsetToPointer = reader.ReadUInt32();
            reader.Seek(start + offsetToPointer);
            PointersToAnchorClassLookups = new PointersToAnchorClassLookups(reader);
        }
    }
}