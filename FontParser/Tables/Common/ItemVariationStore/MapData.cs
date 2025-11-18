namespace FontParser.Tables.Common.ItemVariationStore
{
    public class MapData
    {
        public byte OriginalData { get; }

        public uint OuterIndex { get; }

        public uint InnerIndex { get; }

        public MapData(byte originalData, int innerFactor, int outerFactor)
        {
            OriginalData = originalData;
            OuterIndex = (uint)(originalData >> innerFactor);
            InnerIndex = (uint)(originalData & (outerFactor - 1));
        }
    }
}