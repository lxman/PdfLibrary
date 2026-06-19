using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.TtTables.Glyf
{
    public class SimpleGlyph : IGlyphSpec
    {
        public List<SimpleGlyphCoordinate> Coordinates { get; } = new();

        public List<ushort> EndPtsOfContours { get; private set; }

        public List<byte> Instructions { get; private set; }

        public SimpleGlyph(
            BigEndianReader reader,
            GlyphHeader glyphHeader)
        {
            EndPtsOfContours = reader.ReadUShortArray(Convert.ToUInt32(glyphHeader.NumberOfContours)).ToList();
            ushort instructionLength = reader.ReadUShort();
            Instructions = reader.ReadBytes(instructionLength).ToList();

            int numberOfPoints = EndPtsOfContours[glyphHeader.NumberOfContours - 1] + 1;
            SimpleGlyphFlags[]? flags = ArrayPool<SimpleGlyphFlags>.Shared.Rent(numberOfPoints);
            for (var i = 0; i < numberOfPoints; i++)
            {
                flags[i] = (SimpleGlyphFlags)reader.ReadByte();
                if (!flags[i].HasRepeat()) continue;
                byte repeat = reader.ReadByte();
                for (var j = 0; j < repeat; j++)
                {
                    i++;
                    if (i >= numberOfPoints)
                    {
                        break;
                    }
                    flags[i] = flags[i - 1];
                }
            }

            short[]? xCoordinates = ArrayPool<short>.Shared.Rent(numberOfPoints);
            for (var i = 0; i < numberOfPoints; i++)
            {
                if (flags[i].HasXShortVector())
                {
                    if (flags[i].HasXIsSameOrPositiveXShortVector())
                    {
                        xCoordinates[i] = Convert.ToInt16(reader.ReadByte() + (i > 0 ? xCoordinates[i - 1] : 0));
                    }
                    else
                    {
                        xCoordinates[i] = Convert.ToInt16(-reader.ReadByte() + (i > 0 ? xCoordinates[i - 1] : 0));
                    }
                }
                else if (flags[i].HasXIsSameOrPositiveXShortVector())
                {
                    xCoordinates[i] = Convert.ToInt16(i > 0 ? xCoordinates[i - 1] : 0);
                }
                else
                {
                    xCoordinates[i] = Convert.ToInt16(reader.ReadShort() + (i > 0 ? xCoordinates[i - 1] : 0));
                }
            }

            short[]? yCoordinates = ArrayPool<short>.Shared.Rent(numberOfPoints);
            for (var i = 0; i < numberOfPoints; i++)
            {
                if (flags[i].HasYShortVector())
                {
                    if (flags[i].HasYIsSameOrPositiveYShortVector())
                    {
                        yCoordinates[i] = Convert.ToInt16(reader.ReadByte() + (i > 0 ? yCoordinates[i - 1] : 0));
                    }
                    else
                    {
                        yCoordinates[i] = Convert.ToInt16(-reader.ReadByte() + (i > 0 ? yCoordinates[i - 1] : 0));
                    }
                }
                else if (flags[i].HasYIsSameOrPositiveYShortVector())
                {
                    yCoordinates[i] = Convert.ToInt16(i > 0 ? yCoordinates[i - 1] : 0);
                }
                else
                {
                    yCoordinates[i] = Convert.ToInt16(reader.ReadShort() + (i > 0 ? yCoordinates[i - 1] : 0));
                }
            }

            for (var i = 0; i < numberOfPoints; i++)
            {
                Coordinates.Add(new SimpleGlyphCoordinate(new Point(xCoordinates[i], yCoordinates[i]), flags[i].HasOnCurve()));
            }
            ArrayPool<short>.Shared.Return(xCoordinates);
            ArrayPool<short>.Shared.Return(yCoordinates);
            ArrayPool<SimpleGlyphFlags>.Shared.Return(flags);
        }
    }
}