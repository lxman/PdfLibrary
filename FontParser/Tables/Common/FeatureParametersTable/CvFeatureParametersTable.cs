using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.FeatureParametersTable
{
    public class CvFeatureParametersTable : IFeatureParametersTable
    {
        public ushort Format { get; }

        public ushort FeatureUILabelNameId { get; }

        public ushort FeatureUITooltipTextNameId { get; }

        public ushort SampleTextNameId { get; }

        public ushort NumNamedParameters { get; }

        public ushort FirstParamUILabelNameId { get; }

        public List<uint> UnicodeScalarValues { get; } = new List<uint>();

        public CvFeatureParametersTable(BigEndianReader reader)
        {
            Format = reader.ReadUShort();
            FeatureUILabelNameId = reader.ReadUShort();
            FeatureUITooltipTextNameId = reader.ReadUShort();
            SampleTextNameId = reader.ReadUShort();
            NumNamedParameters = reader.ReadUShort();
            FirstParamUILabelNameId = reader.ReadUShort();
            ushort charCount = reader.ReadUShort();
            for (var i = 0; i < charCount; i++)
            {
                UnicodeScalarValues.Add(reader.ReadUInt24());
            }
        }
    }
}