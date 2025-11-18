using System;
using System.Collections;

namespace FontParser.Extensions
{
    public static class ByteArrayExtensions
    {
        public static int ParseBits(this byte[] bytes, int start, int length)
        {
            var ba = new BitArray(bytes);
            var idx = 0;
            int shift = length - 1;
            // Iterate backwards through the bits and perform bitwise operations
            for (int i = start + length - 1; i >= start; i--)
            {
                idx |= (Convert.ToInt32(ba.Get(i)) << shift);
                shift--;
            }
            return idx;
        }
    }
}