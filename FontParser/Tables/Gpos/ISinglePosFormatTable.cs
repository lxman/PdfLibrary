using FontParser.Tables.Common.CoverageFormat;

namespace FontParser.Tables.Gpos
{
    public interface ISinglePosFormatTable
    {
        ushort PosFormat { get; }

        ICoverageFormat Coverage { get; }

        ValueFormat ValueFormat { get; }
    }
}