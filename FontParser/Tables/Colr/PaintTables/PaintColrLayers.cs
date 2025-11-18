using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintColrLayers : IPaintTable
    {
        public byte Format => 1;

        public List<IPaintTable> PaintTables { get; } = new List<IPaintTable>();

        public PaintColrLayers(BigEndianReader reader)
        {
            byte numLayers = reader.ReadByte();
            uint firstLayerOffset = reader.ReadUInt32();
        }
    }
}