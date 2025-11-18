using System.Text;
using FontParser.Reader;

#pragma warning disable CS8601 // Possible null reference assignment.

namespace FontParser.Tables.Proprietary.Pfed.SubTables
{
    public class FcmtTable : IPfedSubtable
    {
        public ushort Version { get; }

        public string? Comment { get; }

        public FcmtTable(BigEndianReader reader)
        {
            Version = reader.ReadUShort();
            ushort commentLength = reader.ReadUShort();
            Comment = Version switch
            {
                0 => Encoding.BigEndianUnicode.GetString(reader.ReadBytes(commentLength)),
                1 => Encoding.UTF8.GetString(reader.ReadBytes(commentLength)),
                _ => Comment
            };
        }
    }
}