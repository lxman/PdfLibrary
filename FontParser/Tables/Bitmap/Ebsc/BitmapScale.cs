using FontParser.Reader;
using FontParser.Tables.Bitmap.Common;

namespace FontParser.Tables.Bitmap.Ebsc
{
    public class BitmapScale
    {
        public SbitLineMetrics HorizontalLineMetrics { get; }

        public SbitLineMetrics VerticalLineMetrics { get; }

        public byte PpemX { get; }

        public byte PpemY { get; }

        public byte SubstitutePpemX { get; }

        public byte SubstitutePpemY { get; }

        public BitmapScale(BigEndianReader reader)
        {
            HorizontalLineMetrics = new SbitLineMetrics(reader);
            VerticalLineMetrics = new SbitLineMetrics(reader);
            PpemX = reader.ReadByte();
            PpemY = reader.ReadByte();
            SubstitutePpemX = reader.ReadByte();
            SubstitutePpemY = reader.ReadByte();
        }
    }
}