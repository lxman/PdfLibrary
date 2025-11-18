using System;
using FontParser.Reader;

namespace FontParser.Tables.Fftm
{
    public class FftmTable : IFontTable
    {
        public static string Tag => "FFTM";

        public uint Version { get; }

        public DateTime FFTimestamp { get; }

        public DateTime CreatedFFTimestamp { get; }

        public DateTime ModifiedFFTimestamp { get; }

        public FftmTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            var baseTime = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Version = reader.ReadUInt32();
            FFTimestamp = baseTime.AddSeconds(reader.ReadLong());
            CreatedFFTimestamp = baseTime.AddSeconds(reader.ReadLong());
            ModifiedFFTimestamp = baseTime.AddSeconds(reader.ReadLong());
        }
    }
}