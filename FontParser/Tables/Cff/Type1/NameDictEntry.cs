using System.Collections.Generic;

namespace FontParser.Tables.Cff.Type1
{
    public class NameDictEntry
    {
        public string Name { get; }

        public List<CffDictEntry> Private { get; }

        public List<List<byte>> LocalSubroutines { get; } = new List<List<byte>>();

        public NameDictEntry(string name, List<CffDictEntry> privateDict)
        {
            Name = name;
            Private = privateDict;
        }
    }
}
