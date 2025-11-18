using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class CvtcTable : IPfedSubtable
    {
        public ushort Version { get; }

        public List<string> Comments { get; } = new List<string>();

        public CvtcTable(BigEndianReader reader)
        {
            long start = reader.Position;

            Version = reader.ReadUShort();
            ushort numComments = reader.ReadUShort();
            var offsets = new List<ushort>();
            for (var i = 0; i < numComments; i++)
            {
                offsets.Add(reader.ReadUShort());
            }
            offsets.ForEach(o => Comments.Add(reader.ReadNullTerminatedString(false)));
        }
    }
}