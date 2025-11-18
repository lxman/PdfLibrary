using System.Collections.Generic;
using System.Linq;
using FontParser.Tables.Cff.Type1;

namespace FontParser.Extensions
{
    public static class CffDictEntryExtensions
    {
        public static CffDictEntry Clone(this CffDictEntry entry)
        {
            return new CffDictEntry(entry.Name, entry.OperandKind, entry.Operand);
        }

        public static List<CffDictEntry> Clone(this List<CffDictEntry> entries)
        {
            return entries.Select(entry => entry.Clone()).ToList();
        }
    }
}
