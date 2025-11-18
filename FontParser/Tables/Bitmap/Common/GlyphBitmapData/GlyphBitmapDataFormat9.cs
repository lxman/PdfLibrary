using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Bitmap.Ebdt;

namespace FontParser.Tables.Bitmap.Common.GlyphBitmapData
{
    public class GlyphBitmapDataFormat9 : IGlyphBitmapData
    {
        public BigGlyphMetricsRecord BigMetrics { get; }

        public List<EbdtComponent> EbdtComponents { get; } = new List<EbdtComponent>();

        public GlyphBitmapDataFormat9(BigEndianReader reader)
        {
            BigMetrics = new BigGlyphMetricsRecord(reader);
            ushort componentCount = reader.ReadUShort();
            for (var i = 0; i < componentCount; i++)
            {
                EbdtComponents.Add(new EbdtComponent(reader));
            }
        }
    }
}