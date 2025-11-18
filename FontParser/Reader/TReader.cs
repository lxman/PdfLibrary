namespace FontParser.Reader
{
    public class TReader<T>
    {
        public bool EndOfData => _position >= _data.Length;

        private readonly T[] _data;
        private int _position;

        public TReader(T[] input)
        {
            _data = input;
            _position = 0;
        }

        public T Read()
        {
            return _data[_position++];
        }

        public T Peek()
        {
            return _data[_position];
        }

        public T[] Read(int count)
        {
            T[] toReturn = _data[_position..(_position + count)];
            _position += count;
            return toReturn;
        }

        public T[] Peek(int count)
        {
            return _data[_position..(_position + count)];
        }
    }
}