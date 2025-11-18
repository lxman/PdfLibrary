using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Kern
{
    public class KernTable : IFontTable
    {
        public static string Tag => "kern";

        public ushort Version { get; }

        public List<IKernSubtable> Subtables { get; } = new List<IKernSubtable>();

        public KernTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version = reader.ReadUShort();
            ushort nTables = reader.ReadUShort();
            for (var i = 0; i < nTables; i++)
            {
                byte tableVersion = reader.PeekBytes(2)[1];
                switch (tableVersion)
                {
                    case 0:
                        Subtables.Add(new KernSubtableFormat0(reader));
                        break;

                    case 2:
                        Subtables.Add(new KernSubtableFormat2(reader));
                        break;
                }
            }
        }
    }
}