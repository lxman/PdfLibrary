using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class LayerList
    {
        public List<IPaintTable> Layers { get; } = new List<IPaintTable>();

        private readonly uint _layerCount;
        private readonly uint[] _layerOffsets;
        private readonly long _start;

        public LayerList(BigEndianReader reader)
        {
            _start = reader.Position;
            _layerCount = reader.ReadUInt32();
            _layerOffsets = new uint[_layerCount];
            for (var i = 0; i < _layerCount; i++)
            {
                _layerOffsets[i] = reader.ReadUInt32();
            }
        }

        public void Process(BigEndianReader reader)
        {
            for (var i = 0; i < _layerCount; i++)
            {
                Layers.Add(PaintTableFactory.CreatePaintTable(reader, _start + _layerOffsets[i]));
            }
        }
    }
}