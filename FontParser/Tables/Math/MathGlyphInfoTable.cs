using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.CoverageFormat;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Math
{
    public class MathGlyphInfoTable
    {
        public MathItalicsCorrectionInfo ItalicsCorrectionInfo { get; }

        public MathTopAccentAttachment TopAccentAttachment { get; }

        public ICoverageFormat ExtendedShapeCoverage { get; }

        public MathKernInfoTable KernInfo { get; }

        public MathGlyphInfoTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort italicsCorrectionInfoOffset = reader.ReadUShort();
            ushort topAccentAttachmentOffset = reader.ReadUShort();
            ushort extendedShapeCoverageOffset = reader.ReadUShort();
            ushort kernInfoOffset = reader.ReadUShort();

            if (italicsCorrectionInfoOffset > 0)
            {
                reader.Seek(position + italicsCorrectionInfoOffset);
                ItalicsCorrectionInfo = new MathItalicsCorrectionInfo(reader);
            }

            if (topAccentAttachmentOffset > 0)
            {
                reader.Seek(position + topAccentAttachmentOffset);
                TopAccentAttachment = new MathTopAccentAttachment(reader);
            }

            if (extendedShapeCoverageOffset > 0)
            {
                reader.Seek(position + extendedShapeCoverageOffset);
                ExtendedShapeCoverage = CoverageTable.Retrieve(reader);
            }
            if (kernInfoOffset == 0) return;
            reader.Seek(position + kernInfoOffset);
            KernInfo = new MathKernInfoTable(reader);
        }
    }
}