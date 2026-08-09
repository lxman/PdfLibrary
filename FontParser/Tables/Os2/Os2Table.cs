using FontParser.Reader;

namespace FontParser.Tables.Os2
{
    /// <summary>
    /// The OS/2 and Windows Metrics table.
    ///
    /// <para>Parsed for three consumers, all in font-descriptor construction: <c>sCapHeight</c> is
    /// the only real source for <c>/CapHeight</c>, <c>usWeightClass</c> is the fallback basis for
    /// <c>/StemV</c>, and <c>fsType</c> is the font's own statement of whether embedding it is
    /// licensed at all.</para>
    ///
    /// <para>Version matters: <c>sxHeight</c> and <c>sCapHeight</c> were added in version 2, so a
    /// version-0 or version-1 table simply does not contain them and reading those offsets would
    /// run past the table into whatever follows. Both are reported as 0, which callers must treat
    /// as "absent" rather than "zero".</para>
    /// </summary>
    public class Os2Table : IFontTable
    {
        public static string Tag => "OS/2";

        public ushort Version { get; }
        public ushort UsWeightClass { get; }
        public ushort FsType { get; }
        public short STypoAscender { get; }
        public short STypoDescender { get; }
        public short STypoLineGap { get; }
        public short SxHeight { get; }
        public short SCapHeight { get; }

        /// <summary>
        /// True when the font declares Restricted License Embedding (fsType bit 1). Such a font
        /// must not be embedded in a document.
        ///
        /// <para>Only bit 1 is a prohibition. Preview-and-print (bit 2) and editable (bit 3) both
        /// permit the embedding this library performs. The spec calls the bits mutually exclusive
        /// and real files violate that, so bit 1 is tested alone rather than by equality.</para>
        /// </summary>
        public bool EmbeddingRestricted => (FsType & 0x0002) != 0;

        public Os2Table(byte[] data)
        {
            // Every field beyond the first is guarded by BytesRemaining: a truncated OS/2 table
            // occurs in the wild and must degrade to "absent" (0) rather than throw inside a
            // font-loading path.
            using var reader = new BigEndianReader(data);

            Version = reader.BytesRemaining >= 2 ? reader.ReadUShort() : (ushort)0;

            reader.Seek(4);
            UsWeightClass = reader.BytesRemaining >= 2 ? reader.ReadUShort() : (ushort)0;

            reader.Seek(8);
            FsType = reader.BytesRemaining >= 2 ? reader.ReadUShort() : (ushort)0;

            reader.Seek(68);
            STypoAscender = reader.BytesRemaining >= 2 ? reader.ReadShort() : (short)0;

            reader.Seek(70);
            STypoDescender = reader.BytesRemaining >= 2 ? reader.ReadShort() : (short)0;

            reader.Seek(74);
            STypoLineGap = reader.BytesRemaining >= 2 ? reader.ReadShort() : (short)0;

            if (Version < 2) return; // sxHeight/sCapHeight do not exist before version 2

            reader.Seek(86);
            SxHeight = reader.BytesRemaining >= 2 ? reader.ReadShort() : (short)0;

            reader.Seek(88);
            SCapHeight = reader.BytesRemaining >= 2 ? reader.ReadShort() : (short)0;
        }
    }
}
