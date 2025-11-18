namespace FontParser.Tables.Cff
{
    public interface ICffHeader
    {
        byte MajorVersion { get; }

        byte MinorVersion { get; }

        byte HeaderSize { get; }
    }
}