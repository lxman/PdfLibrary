using FontParser.Reader;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Bitmap.Common
{
    public class BitmapSize
    {
        public IndexSubtableList IndexSubtableList { get; set; }

        public uint ColorRef { get; }

        public SbitLineMetrics HorizontalMetrics { get; }

        public SbitLineMetrics VerticalMetrics { get; }

        public ushort StartGlyphIndex { get; }

        public ushort EndGlyphIndex { get; }

        public byte PpemX { get; }

        public byte PpemY { get; }

        public BitmapDepth BitDepth { get; }

        public BitmapFlags Flags { get; }

        private readonly uint _indexSubtableListOffset;
        private readonly uint _indexSubtableListSize;
        private readonly uint _indexSubtableCount;

        public BitmapSize(BigEndianReader reader)
        {
            _indexSubtableListOffset = reader.ReadUInt32();
            _indexSubtableListSize = reader.ReadUInt32();
            _indexSubtableCount = reader.ReadUInt32();
            ColorRef = reader.ReadUInt32();
            HorizontalMetrics = new SbitLineMetrics(reader);
            VerticalMetrics = new SbitLineMetrics(reader);
            StartGlyphIndex = reader.ReadUShort();
            EndGlyphIndex = reader.ReadUShort();
            PpemX = reader.ReadByte();
            PpemY = reader.ReadByte();
            BitDepth = (BitmapDepth)reader.ReadByte();
            Flags = (BitmapFlags)reader.ReadByte();
        }

        public void LoadIndexSubtableList(BigEndianReader reader)
        {
            reader.Seek(_indexSubtableListOffset);
            IndexSubtableList = new IndexSubtableList(reader, _indexSubtableCount);
        }
    }
}