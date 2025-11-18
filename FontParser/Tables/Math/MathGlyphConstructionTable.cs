using System.Collections.Generic;
using FontParser.Reader;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Math
{
    public class MathGlyphConstructionTable
    {
        public GlyphAssemblyTable? GlyphAssembly { get; }

        public List<MathGlyphVariantRecord> GlyphVariantRecords { get; } = new List<MathGlyphVariantRecord>();

        public MathGlyphConstructionTable(BigEndianReader reader)
        {
            long position = reader.Position;

            ushort glyphAssemblyOffset = reader.ReadUShort();

            ushort variantCount = reader.ReadUShort();

            for (var i = 0; i < variantCount; i++)
            {
                GlyphVariantRecords.Add(new MathGlyphVariantRecord(reader));
            }

            if (glyphAssemblyOffset == 0) return;
            reader.Seek(position + glyphAssemblyOffset);

            GlyphAssembly = new GlyphAssemblyTable(reader);
            reader.LogChanges = false;
        }
    }
}