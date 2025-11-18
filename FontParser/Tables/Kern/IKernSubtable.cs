namespace FontParser.Tables.Kern
{
    public interface IKernSubtable
    {
        ushort Version { get; }

        KernCoverage Coverage { get; }
    }
}