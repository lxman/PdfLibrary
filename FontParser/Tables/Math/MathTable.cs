using FontParser.Reader;

namespace FontParser.Tables.Math
{
    public class MathTable : IFontTable
    {
        public static string Tag => "MATH";

        public MathConstantsTable Constants { get; }

        public MathGlyphInfoTable GlyphInfo { get; }

        public MathVariantsTable Variants { get; }

        public MathTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            var header = new MathHeader(reader);

            reader.Seek(header.MathConstantsOffset);
            Constants = new MathConstantsTable(reader);

            reader.Seek(header.MathGlyphInfoOffset);
            GlyphInfo = new MathGlyphInfoTable(reader);

            reader.Seek(header.MathVariantsOffset);
            Variants = new MathVariantsTable(reader);
        }
    }
}