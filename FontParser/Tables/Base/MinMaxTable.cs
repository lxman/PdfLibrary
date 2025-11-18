using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Base.BaseCoord;

namespace FontParser.Tables.Base
{
    public class MinMaxTable
    {
        public IBaseCoordFormat? MinCoord { get; }

        public IBaseCoordFormat? MaxCoord { get; }

        public List<FeatMinMaxRecord> FeatMinMaxRecords { get; } = new List<FeatMinMaxRecord>();

        public MinMaxTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort minCoordOffset = reader.ReadUShort();
            ushort maxCoordOffset = reader.ReadUShort();
            ushort featMinMaxCount = reader.ReadUShort();
            for (var i = 0; i < featMinMaxCount; i++)
            {
                FeatMinMaxRecords.Add(new FeatMinMaxRecord(reader, position));
            }

            reader.Seek(position + minCoordOffset);
            ushort minFormat = reader.ReadUShort();
            MinCoord = minFormat switch
            {
                1 => new BaseCoordFormat1(reader),
                2 => new BaseCoordFormat2(reader),
                3 => new BaseCoordFormat3(reader),
                _ => MinCoord
            };
            reader.Seek(position + maxCoordOffset);
            ushort maxFormat = reader.ReadUShort();
            MaxCoord = maxFormat switch
            {
                1 => new BaseCoordFormat1(reader),
                2 => new BaseCoordFormat2(reader),
                3 => new BaseCoordFormat3(reader),
                _ => MaxCoord
            };
        }
    }
}