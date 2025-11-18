using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.WOFF2
{
    public class TransformedGlyfTable
    {
        public ushort Reserved { get; }

        public ushort OptionFlags { get; }

        public ushort GlyphCount { get; }

        public ushort IndexFormat { get; }

        public short[] NContourStream { get; }

        public ushort[] NPointsStream { get; }

        public byte[] FlagStream { get; }

        public byte[] GlyphStream { get; }

        public byte[] CompositeStream { get; }

        public byte[] BBoxBitmapStream { get; }

        public short[] BBoxStream { get; }

        public byte[] InstructionStream { get; }

        public byte[] OverlapSimpleBitmap { get; }

        public TransformedGlyfTable(BigEndianReader reader)
        {
            Reserved = reader.ReadUShort();
            OptionFlags = reader.ReadUShort();
            GlyphCount = reader.ReadUShort();
            IndexFormat = reader.ReadUShort();
            uint nContourStreamSize = reader.ReadUInt32();
            uint nPointsStreamSize = reader.ReadUInt32();
            uint flagStreamSize = reader.ReadUInt32();
            uint glyphStreamSize = reader.ReadUInt32();
            uint compositeStreamSize = reader.ReadUInt32();
            uint bboxBitmapStreamSize = Convert.ToUInt32(System.Math.Floor(Convert.ToDouble(GlyphCount + 31) / 32)) * 4;
            uint bboxStreamSize = reader.ReadUInt32();
            uint instructionStreamSize = reader.ReadUInt32();

            long nContourStreamOffset = reader.Position;

            long nPointsStreamOffset = nContourStreamOffset + nContourStreamSize;
            long flagStreamOffset = nPointsStreamOffset + nPointsStreamSize;
            long glyphStreamOffset = flagStreamOffset + flagStreamSize;
            long compositeStreamOffset = glyphStreamOffset + glyphStreamSize;
            long bboxBitmapStreamOffset = compositeStreamOffset + compositeStreamSize;
            long bboxStreamOffset = bboxBitmapStreamOffset + bboxBitmapStreamSize;
            long instructionStreamOffset = bboxStreamOffset + (bboxStreamSize - bboxBitmapStreamSize);

            NContourStream = reader.ReadShortArray(nContourStreamSize / 2);
            int numContours = NContourStream.Where(nContours => nContours >= 0).Aggregate(0, (current, nContours) => current + nContours);
            reader.Seek(nPointsStreamOffset);
            var nPoints = new List<ushort>();
            var index = 0;
            while (index++ < numContours)
            {
                nPoints.Add(reader.Read255UInt16());
            }
            NPointsStream = nPoints.ToArray();
            reader.Seek(flagStreamOffset);
            FlagStream = reader.ReadBytes(flagStreamSize);
            reader.Seek(glyphStreamOffset);
            GlyphStream = reader.ReadBytes(glyphStreamSize);
            reader.Seek(compositeStreamOffset);
            CompositeStream = reader.ReadBytes(compositeStreamSize);
            reader.Seek(bboxBitmapStreamOffset);
            BBoxBitmapStream = reader.ReadBytes(bboxBitmapStreamSize);
            reader.Seek(bboxStreamOffset);
            BBoxStream = reader.ReadShortArray(bboxStreamSize);
            reader.Seek(instructionStreamOffset);
            InstructionStream = reader.ReadBytes(instructionStreamSize);
            OverlapSimpleBitmap = reader.ReadBytes(bboxBitmapStreamSize);
        }
    }
}