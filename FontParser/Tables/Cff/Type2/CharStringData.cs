using System.Collections.Generic;

namespace FontParser.Tables.Cff.Type2
{
    public class CharStringData
    {
        public List<List<byte>>? Subroutines { get; }

        public int? NominalWidthX { get; }

        public CharStringData(List<List<byte>>? subroutines, int? nominalWidthX)
        {
            Subroutines = subroutines;
            NominalWidthX = nominalWidthX;
        }
    }
}