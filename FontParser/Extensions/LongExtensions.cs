using System;

namespace FontParser.Extensions
{
    public static class LongExtensions
    {
        public static DateTime ToDateTime(this long value)
        {
            value = value & 0x00000000FFFFFFFF;
            var dateTime = new DateTime(1904, 1, 1);
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).AddSeconds(value);
        }
    }
}