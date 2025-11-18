namespace FontParser.Tables.Proprietary.Aat.Kerx.Subtables
{
    public interface IKerxSubtable
    {
        uint Length { get; }

        KerxCoverage Coverage { get; }

        uint TupleCount { get; }
    }
}