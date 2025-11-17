namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// Encoding record from cmap table header
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class EncodingRecord
    {
        public static long RecordSize => 8;

        public PlatformId PlatformId { get; }

        public UnicodeEncodingId? UnicodeEncoding { get; }

        public MacintoshEncodingId? MacintoshEncoding { get; }

        public IsoEncodingId? IsoEncoding { get; }

        public WindowsEncodingId? WindowsEncoding { get; }

        internal uint Offset { get; }

        public EncodingRecord(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            PlatformId = (PlatformId)reader.ReadUShort();
            ushort platformEncodingId = reader.ReadUShort();
            switch (PlatformId)
            {
                case PlatformId.Unicode:
                    UnicodeEncoding = (UnicodeEncodingId)platformEncodingId;
                    break;

                case PlatformId.Macintosh:
                    MacintoshEncoding = (MacintoshEncodingId)platformEncodingId;
                    break;

                case PlatformId.Iso:
                    IsoEncoding = (IsoEncodingId)platformEncodingId;
                    break;

                case PlatformId.Windows:
                    WindowsEncoding = (WindowsEncodingId)platformEncodingId;
                    break;
            }
            Offset = reader.ReadUInt32();
        }
    }
}
