using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Optional.Dsig
{
    public class DsigTable : IFontTable
    {
        public static string Tag => "DSIG";

        public uint Version { get; }

        public PermissionFlags PermissionFlags { get; }

        public List<SigRecord> SigRecords { get; } = new List<SigRecord>();

        public DsigTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version = reader.ReadUInt32();
            ushort _numSigs = reader.ReadUShort();
            if (_numSigs == 0) return;
            PermissionFlags = (PermissionFlags)reader.ReadUShort();

            for (var i = 0; i < _numSigs; i++)
            {
                SigRecords.Add(new SigRecord(reader));
            }

            SigRecords.ForEach(r => r.ReadSignature(reader));
        }
    }
}