using System;
using FontParser.Reader;
using FontParser.Tables.Colr.PaintTables;

namespace FontParser.Tables.Colr
{
    public static class PaintTableFactory
    {
        public static IPaintTable CreatePaintTable(BigEndianReader reader, long offset)
        {
            reader.Seek(offset);
            byte format = reader.ReadByte();
            return format switch
            {
                1 => new PaintColrLayers(reader),
                2 => new PaintSolid(reader),
                3 => new PaintVarSolid(reader),
                4 => new PaintLinearGradient(reader),
                5 => new PaintVarLinearGradient(reader),
                6 => new PaintRadialGradient(reader),
                7 => new PaintVarRadialGradient(reader),
                8 => new PaintSweepGradient(reader),
                9 => new PaintVarSweepGradient(reader),
                10 => new PaintGlyph(reader),
                11 => new PaintColrGlyph(reader),
                12 => new PaintTransform(reader),
                13 => new PaintVarTransform(reader),
                14 => new PaintTranslate(reader),
                15 => new PaintVarTranslate(reader),
                16 => new PaintScale(reader),
                17 => new PaintVarScale(reader),
                18 => new PaintScaleAroundCenter(reader),
                19 => new PaintVarScaleAroundCenter(reader),
                20 => new PaintScaleUniform(reader),
                21 => new PaintVarScaleUniform(reader),
                22 => new PaintScaleUniformAroundCenter(reader),
                23 => new PaintVarScaleUniformAroundCenter(reader),
                24 => new PaintRotate(reader),
                25 => new PaintVarRotate(reader),
                26 => new PaintRotateAroundCenter(reader),
                27 => new PaintVarRotateAroundCenter(reader),
                28 => new PaintSkew(reader),
                29 => new PaintVarSkew(reader),
                30 => new PaintSkewAroundCenter(reader),
                31 => new PaintVarSkewAroundCenter(reader),
                32 => new PaintComposite(reader),
                _ => throw new ArgumentOutOfRangeException($"No paint table for #{format}")
            };
        }
    }
}