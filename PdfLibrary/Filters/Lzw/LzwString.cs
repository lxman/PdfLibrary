using System.Text;

namespace PdfLibrary.Filters.Lzw
{
    public class LzwString(byte value, byte firstChar, int length, LzwString previous)
        : IComparable<LzwString>
    {
        /// <summary>
        /// Empty version of this string for comparisons (though can probably use null?)
        /// </summary>
        internal static readonly LzwString Empty = new(0, 0, 0, null);

        private readonly LzwString _previous = previous;

        public readonly int Length = length;
        public readonly byte Value = value;
        public readonly byte FirstChar = firstChar; // Copied forward for fast access

        private readonly byte[] _writeBuffer = new byte[length]; // Buffer used for writing out to the stream later

        public LzwString(byte code) : this(code, code, 1, null) { }

        public LzwString Concatenate(byte value)
        {
            return this == Empty
                ? new LzwString(value)
                : new LzwString(value, FirstChar, Length + 1, this);
        }

        public void WriteTo(Stream buffer)
        {
            switch (Length)
            {
                case 0:
                    return;
                case 1:
                    buffer.WriteByte(Value);
                    break;
                default:
                {
                    LzwString e = this;

                    // We're using a stream.
                    // We can't assume we're going to be able to seek it in every situation
                    // (ie we might be writing it out to a HTTP response, we can't very well go "oh wait here's some more data back here")
                    // So store the values in our small buffer and write it out to the stream.
                    for (int i = Length - 1; i >= 0; i--)
                    {
                        _writeBuffer[i] = e.Value;
                        //buffer.put(offset + i, e.value);
                        //buffer.WriteByte(e.value);
                        e = e._previous;
                    }

                    //buffer.position(offset + length);
                    buffer.Write(_writeBuffer, 0, Length);
                    break;
                }
            }
        }

        #region Overrides
        public override int GetHashCode()
        {
            int result = _previous is not null ? _previous.GetHashCode() : 0;
            result = 31 * result + Length;
            result = 31 * result + Value;
            result = 31 * result + FirstChar;
            return result;
        }

        /// <summary>
        /// Checks to see if the two objects are equal
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public override bool Equals(object other)
        {
            if (this == other)
            {
                return true;
            }
            if (other is null || other.GetType() != GetType())
            {
                return false;
            }

            var s = (LzwString)other;

            return FirstChar == s.FirstChar &&
                    Length == s.Length &&
                    Value == s.Value &&
                    _previous == s._previous;
        }

        public override string ToString()
        {
            var builder = new StringBuilder("ZLWString[");
            int offset = builder.Length;
            LzwString e = this;
            for (int i = Length - 1; i >= 0; i--)
            {
                builder.Insert(offset, String.Format("%2x", e.Value));
                e = e._previous;
            }
            builder.Append("]");
            return builder.ToString();
        }

        public int CompareTo(LzwString other)
        {
            if (other == this)
            {
                return 0;
            }

            if (Length != other.Length)
            {
                return other.Length - Length;
            }

            if (FirstChar != other.FirstChar)
            {
                return other.FirstChar - FirstChar;
            }

            LzwString t = this;
            LzwString o = other;

            for (int i = Length - 1; i > 0; i--)
            {
                if (t.Value != o.Value)
                {
                    return o.Value - t.Value;
                }

                t = t._previous;
                o = o._previous;
            }

            return 0;
        }

        #endregion
    }
}