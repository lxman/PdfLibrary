using System;
using System.Collections.Generic;
using System.Linq;

namespace FontParser.Extensions
{
    public static class EnumExtensions
    {
        public static IEnumerable<T> GetFlags<T>(this T e) where T : Enum
        {
            return Enum.GetValues(e.GetType()).Cast<T>().Where(value => e.HasFlag(value));
        }
    }
}