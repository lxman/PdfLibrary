using System.Collections.Generic;
using FontParser.Tables.Proprietary.Pfed.SubTables;

namespace FontParser.Tables.Proprietary.Pfed
{
    public class PfedTable : IFontTable
    {
        public static string Tag => "PfEd";

        public uint Version { get; }

        public List<IPfedSubtable> Subtables { get; } = new List<IPfedSubtable>();

        public PfedTable(byte[] data)
        {
            // https://fontforge.org/docs/techref/non-standard.html
            // TODO: Implement PfedTable
            // This is a proprietary table, theoretically only used by FontForge.
            // It appears that the format has changed over time, so it's not clear
            //using var reader = new BigEndianReader(data);
            //Version = reader.ReadUInt32();
            //uint count = reader.ReadUInt32();
            //var tocEntries = new List<TocEntry>();
            //for (var i = 0; i < count; i++)
            //{
            //    tocEntries.Add(new TocEntry(reader));
            //}

            //for (var i = 0; i < count; i++)
            //{
            //    reader.Seek(tocEntries[i].Offset);
            //    string tag = tocEntries[i].Tag;
            //    switch (tag)
            //    {
            //        case "colr":
            //            Subtables.Add(new ColrTable(reader));
            //            break;
            //        case "cmnt":
            //            Subtables.Add(new CmntTable(reader));
            //            break;
            //        case "fcmt":
            //            Subtables.Add(new FcmtTable(reader));
            //            break;
            //        case "GPOS":
            //        case "GSUB":
            //            Subtables.Add(new GcmnTable(reader));
            //            break;
            //        default:
            //            break;
            //    }
            //}
        }
    }
}