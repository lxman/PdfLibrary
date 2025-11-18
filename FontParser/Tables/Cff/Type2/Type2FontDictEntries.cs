using System.Collections.Generic;
using System.Collections.ObjectModel;
using FontParser.Tables.Cff.Type1;

namespace FontParser.Tables.Cff.Type2
{
    public class Type2FontDictEntries : ReadOnlyDictionary<ushort, CffDictEntry?>
    {
        public Type2FontDictEntries(IDictionary<ushort, CffDictEntry?> dictionary) : base(dictionary)
        {
            dictionary.Add(0x0012, new CffDictEntry("PrivateDictSizeOffset", OperandKind.Array, new List<uint>()));
        }
    }
}