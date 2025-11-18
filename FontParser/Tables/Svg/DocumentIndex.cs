using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Svg
{
    public class DocumentIndex
    {
        public List<DocumentIndexEntry> Entries { get; } = new List<DocumentIndexEntry>();

        public DocumentIndex(BigEndianReader reader)
        {
            long docIndexStart = reader.Position;
            ushort numEntries = reader.ReadUShort();
            for (var i = 0; i < numEntries; i++)
            {
                Entries.Add(new DocumentIndexEntry(reader, docIndexStart));
            }
            Entries.ForEach(e => e.ReadDocument(reader, docIndexStart));
        }
    }
}