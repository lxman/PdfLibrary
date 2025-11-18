using System.Collections.Generic;
using System.Collections.ObjectModel;
using FontParser.Tables.Cff.Type1;

namespace FontParser.Tables.Cff
{
    public class PrivateDictOperatorEntries : ReadOnlyDictionary<ushort, CffDictEntry?>
    {
        public PrivateDictOperatorEntries(IDictionary<ushort, CffDictEntry?> dictionary) : base(dictionary)
        {
            dictionary.Add(0x0006, new CffDictEntry("BlueValues", OperandKind.Delta, new List<double>()));
            dictionary.Add(0x0007, new CffDictEntry("OtherBlues", OperandKind.Delta, new List<double>()));
            dictionary.Add(0x0008, new CffDictEntry("FamilyBlues", OperandKind.Delta, new List<double>()));
            dictionary.Add(0x0009, new CffDictEntry("FamilyOtherBlues", OperandKind.Delta, new List<double>()));
            dictionary.Add(0x000A, new CffDictEntry("StdHW", OperandKind.Number, 0));
            dictionary.Add(0x000B, new CffDictEntry("StdVW", OperandKind.Number, 0));
            dictionary.Add(0x0C09, new CffDictEntry("BlueScale", OperandKind.Number, 0.039625));
            dictionary.Add(0x0C0A, new CffDictEntry("BlueShift", OperandKind.Number, 7));
            dictionary.Add(0x0C0B, new CffDictEntry("BlueFuzz", OperandKind.Number, 1));
            dictionary.Add(0x0C0C, new CffDictEntry("StemSnapH", OperandKind.Delta, new List<double>()));
            dictionary.Add(0x0C0D, new CffDictEntry("StemSnapV", OperandKind.Delta, new List<double>()));
            dictionary.Add(0x0C0E, new CffDictEntry("ForceBold", OperandKind.Boolean, false));
            dictionary.Add(0x0C11, new CffDictEntry("LanguageGroup", OperandKind.Number, 0));
            dictionary.Add(0x0C12, new CffDictEntry("ExpansionFactor", OperandKind.Number, 0.06));
            dictionary.Add(0x0C13, new CffDictEntry("initialRandomSeed", OperandKind.Number, 0));
            dictionary.Add(0x0013, new CffDictEntry("Subrs", OperandKind.Number, 0));
            dictionary.Add(0x0014, new CffDictEntry("defaultWidthX", OperandKind.Number, 0));
            dictionary.Add(0x0015, new CffDictEntry("nominalWidthX", OperandKind.Number, 0));
            dictionary.Add(0x0016, new CffDictEntry("vsindex", OperandKind.Number, 0));
            dictionary.Add(0x0017, new CffDictEntry("blend", OperandKind.Number, 0));
        }
    }
}