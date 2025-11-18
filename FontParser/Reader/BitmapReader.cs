namespace FontParser.Reader
{
    public class BitmapReader
    {
        private readonly byte[] _data;
        private int _currentByteIndex;
        private byte _currentByte;
        private int _currentBitIndex;

        public BitmapReader(byte[] data)
        {
            _data = data;
            _currentByte = _data[0];
            _currentByteIndex = 0;
            _currentBitIndex = 7;
        }

        public bool Read()
        {
            if (_currentBitIndex == -1)
            {
                if (_currentByteIndex == _data.Length - 1)
                {
                    return false;
                }
                _currentByte = _data[++_currentByteIndex];
                _currentBitIndex = 7;
            }

            bool bit = (_currentByte & (1 << _currentBitIndex)) != 0;
            _currentBitIndex--;
            return bit;
        }
    }
}