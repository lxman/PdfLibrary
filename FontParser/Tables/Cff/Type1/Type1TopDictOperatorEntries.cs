using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FontParser.Tables.Cff.Type1
{
    public class Type1TopDictOperatorEntries : ReadOnlyDictionary<ushort, CffDictEntry?>
    {
        public Type1TopDictOperatorEntries(IDictionary<ushort, CffDictEntry?> dictionary) : base(dictionary)
        {
            dictionary.Add(0x0000, new CffDictEntry("version", OperandKind.Number, 0));
            dictionary.Add(0x0001, new CffDictEntry("Notice", OperandKind.StringId, 0));
            dictionary.Add(0x0002, new CffDictEntry("FullName", OperandKind.StringId, 0));
            dictionary.Add(0x0003, new CffDictEntry("FamilyName", OperandKind.StringId, 0));
            dictionary.Add(0x0004, new CffDictEntry("Weight", OperandKind.StringId, 0));
            dictionary.Add(0x0005, new CffDictEntry("FontBBox", OperandKind.Array, new List<int> { 0, 0, 0, 0 }));
            dictionary.Add(0x000D, new CffDictEntry("UniqueID", OperandKind.Number, 0));
            dictionary.Add(0x000E, new CffDictEntry("XUID", OperandKind.Array, new object()));
            dictionary.Add(0x000F, new CffDictEntry("charset", OperandKind.Number, 0));
            dictionary.Add(0x0010, new CffDictEntry("Encoding", OperandKind.Number, 0));
            dictionary.Add(0x0011, new CffDictEntry("CharStrings", OperandKind.Number, 0));
            dictionary.Add(0x0012, new CffDictEntry("Private", OperandKind.NumberNumber, 0));
            dictionary.Add(0x0C00, new CffDictEntry("Copyright", OperandKind.StringId, 0));
            dictionary.Add(0x0C01, new CffDictEntry("isFixedPitch", OperandKind.Boolean, false));
            dictionary.Add(0x0C02, new CffDictEntry("ItalicAngle", OperandKind.Number, 0));
            dictionary.Add(0x0C03, new CffDictEntry("UnderlinePosition", OperandKind.Number, -100));
            dictionary.Add(0x0C04, new CffDictEntry("UnderlineThickness", OperandKind.Number, 50));
            dictionary.Add(0x0C05, new CffDictEntry("PaintType", OperandKind.Number, 0));
            dictionary.Add(0x0C06, new CffDictEntry("CharstringType", OperandKind.Number, 2));
            dictionary.Add(0x0C07, new CffDictEntry("FontMatrix", OperandKind.Array, new List<double> { 0.001, 0, 0, 0.001, 0, 0 }));
            dictionary.Add(0x0C08, new CffDictEntry("StrokeWidth", OperandKind.Number, 0));
            dictionary.Add(0x0C14, new CffDictEntry("SyntheticBase", OperandKind.Number, 0));
            dictionary.Add(0x0C15, new CffDictEntry("PostScript", OperandKind.StringId, 0));
            dictionary.Add(0x0C16, new CffDictEntry("BaseFontName", OperandKind.StringId, 0));
            dictionary.Add(0x0C17, new CffDictEntry("BaseFontBlend", OperandKind.Delta, new object()));
            dictionary.Add(0x0C1E, new CffDictEntry("ROS", OperandKind.SidSidNumber, new object()));
            dictionary.Add(0x0C1F, new CffDictEntry("CIDFontVersion", OperandKind.Number, 0));
            dictionary.Add(0x0C20, new CffDictEntry("CIDFontRevision", OperandKind.Number, 0));
            dictionary.Add(0x0C21, new CffDictEntry("CIDFontType", OperandKind.Number, 0));
            dictionary.Add(0x0C22, new CffDictEntry("CIDCount", OperandKind.Number, 8720));
            dictionary.Add(0x0C23, new CffDictEntry("UIDBase", OperandKind.Number, 0));
            dictionary.Add(0x0C24, new CffDictEntry("FDArray", OperandKind.Number, 0));
            dictionary.Add(0x0C25, new CffDictEntry("FDSelect", OperandKind.Number, 0));
            dictionary.Add(0x0C26, new CffDictEntry("FontName", OperandKind.StringId, 0));
        }
    }
}