using FontParser.Reader;

namespace FontParser.Tables.TtTables.Glyf
{
    public class GlyphHeader
    {
        public static long RecordSize => 10;

        public short NumberOfContours { get; private set; }

        public short XMin { get; private set; }

        public short YMin { get; private set; }

        public short XMax { get; private set; }

        public short YMax { get; private set; }

        public GlyphHeader(byte[] data)
        {
            if (data.Length == 0) return;
            using var reader = new BigEndianReader(data);
            NumberOfContours = reader.ReadShort();
            XMin = reader.ReadShort();
            YMin = reader.ReadShort();
            XMax = reader.ReadShort();
            YMax = reader.ReadShort();
        }

        public void Woff2Reconstruct(short numberOfContours, short xMin, short yMin, short xMax, short yMax)
        {
            NumberOfContours = numberOfContours;
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public override string ToString()
        {
            return $"Number of Contours: {NumberOfContours}, XMin: {XMin}, YMin: {YMin}, XMax: {XMax}, YMax: {YMax}";
        }
    }
}