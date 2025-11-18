using System.Collections.Generic;
using FontParser.Extensions;

namespace FontParser.Tables.TtTables
{
    public class CvtTable : IFontTable
    {
        public static string Tag => "cvt ";

        private readonly ushort[] _data;
        private readonly int _fWordCount;

        public CvtTable(byte[] data)
        {
            _fWordCount = data.Length / 2;
            _data = new ushort[_fWordCount];
            for (var i = 0; i < _fWordCount; i++)
            {
                _data[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
            }
        }

        public List<float>? GetCvtValues(long origin, long count)
        {
            if (origin < 0 || origin >= _fWordCount || count < 0 || origin + count > _fWordCount)
            {
                return null;
            }
            var cvtValues = new List<float>();
            for (long i = origin; i < origin + count; i++)

            {
                cvtValues.Add(_data[i].ToF26Dot6());
            }

            return cvtValues;
        }

        public float? GetCvtValue(int location)
        {
            if (location < 0 || location >= _fWordCount)
            {
                return null;
            }
            return _data[location].ToF26Dot6();
        }

        public void WriteCvtValue(int location, float value)
        {
            if (location < 0 || location >= _fWordCount)
            {
                return;
            }
            _data[location] = (ushort)value.FromF26Dot6();
        }
    }
}