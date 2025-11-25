using CoreJ2K.j2k.image;
using CoreJ2K.j2k.image.input;

namespace Compressors.Jpeg2000
{
    /// <summary>
    /// Image reader that provides raw byte data to the JPEG 2000 encoder
    /// </summary>
    internal sealed class RawImageReader : ImgReader
    {
        private readonly byte[] _data;
        private readonly int _width;
        private readonly int _height;
        private readonly int _components;

        public RawImageReader(byte[] data, int width, int height, int components)
        {
            _data = data;
            _width = width;
            _height = height;
            _components = components;

            // Set protected fields from ImgReader base class
            w = width;
            h = height;
            nc = components;
        }

        public override int ImgWidth => _width;
        public override int ImgHeight => _height;
        public override int NumComps => _components;
        public override int TileWidth => _width;
        public override int TileHeight => _height;
        public override int NomTileWidth => _width;
        public override int NomTileHeight => _height;
        public override int TileIdx => 0;
        public override int TilePartULX => 0;
        public override int TilePartULY => 0;
        public override int ImgULX => 0;
        public override int ImgULY => 0;

        public override void Close()
        {
            // Nothing to close
        }

        public override bool IsOrigSigned(int compIndex) => false;

        public override int GetFixedPoint(int compIndex) => 0;

        public override int getNomRangeBits(int compIndex) => 8; // 8 bits per component

        public override int getCompSubsX(int c) => 1;
        public override int getCompSubsY(int c) => 1;
        public override int getTileCompWidth(int t, int c) => _width;
        public override int getTileCompHeight(int t, int c) => _height;
        public override int getCompImgWidth(int c) => _width;
        public override int getCompImgHeight(int c) => _height;
        public override int getCompULX(int c) => 0;
        public override int getCompULY(int c) => 0;

        public override void setTile(int x, int y)
        {
            // Single tile, nothing to do
        }

        public override void nextTile()
        {
            // Single tile, nothing to do
        }

        public override Coord getTile(Coord co)
        {
            co ??= new Coord();
            co.x = 0;
            co.y = 0;
            return co;
        }

        public override Coord getNumTiles(Coord co)
        {
            co ??= new Coord();
            co.x = 1;
            co.y = 1;
            return co;
        }

        public override int getNumTiles() => 1;

        public override DataBlk GetInternCompData(DataBlk blk, int compIndex)
        {
            return GetCompData(blk, compIndex);
        }

        public override DataBlk GetCompData(DataBlk blk, int compIndex)
        {
            // Create a new DataBlkInt if needed
            if (blk == null || blk.DataType != DataBlk.TYPE_INT)
            {
                blk = new DataBlkInt(blk?.ulx ?? 0, blk?.uly ?? 0, blk?.w ?? _width, blk?.h ?? _height);
            }

            var dataBlkInt = (DataBlkInt)blk;

            // Ensure data array is allocated
            int size = dataBlkInt.w * dataBlkInt.h;
            if (dataBlkInt.data_array == null || dataBlkInt.data_array.Length < size)
            {
                dataBlkInt.data_array = new int[size];
            }

            // Fill the data array with component values from interleaved source
            int destIdx = 0;
            for (int y = dataBlkInt.uly; y < dataBlkInt.uly + dataBlkInt.h; y++)
            {
                for (int x = dataBlkInt.ulx; x < dataBlkInt.ulx + dataBlkInt.w; x++)
                {
                    int srcIdx = (y * _width + x) * _components + compIndex;
                    // Shift to signed range (JPEG 2000 uses level shift)
                    dataBlkInt.data_array[destIdx++] = _data[srcIdx] - 128;
                }
            }

            dataBlkInt.offset = 0;
            dataBlkInt.scanw = dataBlkInt.w;
            dataBlkInt.progressive = false;

            return dataBlkInt;
        }
    }
}
