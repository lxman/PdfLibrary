using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class AnchorClassLookup
    {
        public string LookupSubtableName { get; }

        public PointerToAnchorClassNamePointer PointerToAnchorClassNamePointer { get; }

        public AnchorClassLookup(BigEndianReader reader)
        {
            ushort offsetToName = reader.ReadUShort();
            ushort offsetToPointer = reader.ReadUShort();
            reader.Seek(offsetToName);
            LookupSubtableName = reader.ReadNullTerminatedString(false);
            reader.Seek(offsetToPointer);
            PointerToAnchorClassNamePointer = new PointerToAnchorClassNamePointer(reader);
        }
    }
}