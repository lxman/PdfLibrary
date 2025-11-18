using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Feat
{
    public class FeatTable : IFontTable
    {
        public static string Tag => "feat";

        public Header Header { get; }

        public List<FeatureName> Names { get; } = new List<FeatureName>();

        public List<SettingName> SettingNames { get; } = new List<SettingName>();

        public FeatTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Header = new Header(reader);
            for (var i = 0; i < Header.FeatureCount; i++)
            {
                Names.Add(new FeatureName(reader));
            }
            for (var i = 0; i < Header.FeatureCount; i++)
            {
                SettingNames.Add(new SettingName(reader));
            }
        }
    }
}