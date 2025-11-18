using System.Text;
using FontParser.Reader;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Svg
{
    public class DocumentIndexEntry
    {
        public ushort StartGlyphId { get; }

        public ushort EndGlyphId { get; }

        public string Instructions { get; private set; }

        private byte[] _svgDocument;

        private readonly uint _svgDocOffset;
        private readonly uint _svgDocLength;

        public DocumentIndexEntry(BigEndianReader reader, long docIndexStart)
        {
            StartGlyphId = reader.ReadUShort();
            EndGlyphId = reader.ReadUShort();
            _svgDocOffset = reader.ReadUInt32();
            _svgDocLength = reader.ReadUInt32();
        }

        public void ReadDocument(BigEndianReader reader, long docIndexStart)
        {
            reader.Seek(docIndexStart + _svgDocOffset);
            _svgDocument = reader.ReadBytes(_svgDocLength);
            if (_svgDocument.IsCompressed())
            {
                Instructions = Encoding.UTF8.GetString(_svgDocument.Decompress());
                return;
            }
            Instructions = Encoding.UTF8.GetString(_svgDocument);
        }
    }
}