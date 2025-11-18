using System;
using FontParser.Reader;
using FontParser.Tables.Proprietary.Aat.Morx.LookupTables;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class LookupTable
    {
        public ushort Format { get; }

        public IFsHeader FsHeader { get; }

        public LookupTable(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            FsHeader = Format switch
            {
                0 => new Format0(reader),
                2 => new LookupTablesFormat2(reader),
                4 => new LookupTablesFormat4(reader),
                6 => new LookupTablesFormat6(reader),
                8 => new LookupTablesFormat8(reader),
                10 => new LookupTablesFormat10(reader),
                _ => throw new NotSupportedException($"LookupType {Format} is not supported.")
            };
        }
    }
}