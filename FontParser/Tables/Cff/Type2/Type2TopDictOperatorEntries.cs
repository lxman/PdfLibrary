using System.Collections.Generic;
using System.Collections.ObjectModel;
using FontParser.Tables.Cff.Type1;

namespace FontParser.Tables.Cff.Type2
{
    public class Type2TopDictOperatorEntries : ReadOnlyDictionary<ushort, CffDictEntry?>
    {
        public Type2TopDictOperatorEntries(Dictionary<ushort, CffDictEntry?> dictionary) : base(dictionary)
        {
            dictionary.Add(0x0011, new CffDictEntry("CharStringIndexOffset", OperandKind.Number, 0));
            dictionary.Add(0x0018, new CffDictEntry("VariationStoreOffset", OperandKind.Number, 0));
            dictionary.Add(0x0C24, new CffDictEntry("FontDictIndexOffset", OperandKind.Number, 0));
            dictionary.Add(0x0C25, new CffDictEntry("FontDictSelectOffset", OperandKind.Number, 0));
            dictionary.Add(0x0C07, new CffDictEntry("FontMatrix", OperandKind.Array, new List<double> { 0.001, 0, 0, 0.001, 0, 0 }));
        }
    }
}