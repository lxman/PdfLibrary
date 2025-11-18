using System;
using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.FeatureParametersTable;

namespace FontParser.Tables.Gsub
{
    public class GsubTable : IFontTable
    {
        public static string Tag => "GSUB";

        public ScriptList ScriptList { get; }

        public FeatureList FeatureList { get; }

        public GsubLookupList GsubLookupList { get; }

        public FeatureVariationsTable? FeatureVariationsTable { get; }

        public GsubTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            var header = new GsubHeader(reader);
            reader.Seek(header.ScriptListOffset);
            ScriptList = new ScriptList(reader);
            reader.Seek(header.FeatureListOffset);
            FeatureList = new FeatureList(reader);
            reader.Seek(header.LookupListOffset);
            GsubLookupList = new GsubLookupList(reader);
            if (header.FeatureVariationsOffset is null || header.FeatureVariationsOffset == 0) return;
            reader.Seek(Convert.ToUInt32(header.FeatureVariationsOffset));
            FeatureVariationsTable = new FeatureVariationsTable(reader);
        }
    }
}