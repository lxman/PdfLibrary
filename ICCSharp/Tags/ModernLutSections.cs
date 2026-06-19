using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tags;

/// <summary>
/// Parser shared by lutAToBType ('mAB ') and lutBToAType ('mBA '). Both share the same on-disk
/// layout (i, o, five offsets, then any subset of {A curves, CLUT, M curves, matrix, B curves}),
/// but interpret the pipeline in opposite directions.
/// </summary>
internal static class ModernLutSections
{
    public sealed class Parsed
    {
        public int InputChannels;
        public int OutputChannels;
        public IReadOnlyList<TagElement>? ACurves;
        public LutClutData? Clut;
        public IReadOnlyList<TagElement>? MCurves;
        public double[]? Matrix; // 12 = 3×3 + 3-offset
        public IReadOnlyList<TagElement> BCurves = Array.Empty<TagElement>();
    }

    /// <summary>
    /// Parses a tag whose data is <paramref name="tagData"/> (full slice including the 8-byte type header).
    /// <paramref name="aCurveCount"/> and <paramref name="bCurveCount"/> are determined by the caller
    /// (different mappings for mAB vs mBA). M curves, when present, are always 3.
    /// </summary>
    public static Parsed Parse(ReadOnlyMemory<byte> tagData, int aCurveCount, int bCurveCount)
    {
        if (tagData.Length < 32)
            throw new IccParseException($"Modern LUT tag {tagData.Length} bytes; need at least 32 for header.");

        IccBinaryReader reader = new(tagData);
        reader.Skip(8); // type sig + reserved (caller has already inspected the sig)

        int inputChannels = reader.ReadUInt8();
        int outputChannels = reader.ReadUInt8();
        reader.Skip(2); // reserved

        uint offBCurves = reader.ReadUInt32();
        uint offMatrix  = reader.ReadUInt32();
        uint offMCurves = reader.ReadUInt32();
        uint offClut    = reader.ReadUInt32();
        uint offACurves = reader.ReadUInt32();

        Parsed result = new()
        {
            InputChannels = inputChannels,
            OutputChannels = outputChannels,
        };

        // B curves: required.
        if (offBCurves == 0)
            throw new IccParseException("Modern LUT tag missing required B curves offset.");
        result.BCurves = ReadCurves(tagData, (int)offBCurves, bCurveCount);

        if (offACurves != 0)
            result.ACurves = ReadCurves(tagData, (int)offACurves, aCurveCount);
        if (offMCurves != 0)
            result.MCurves = ReadCurves(tagData, (int)offMCurves, 3);
        if (offMatrix != 0)
            result.Matrix = ReadMatrix(tagData, (int)offMatrix);
        if (offClut != 0)
            result.Clut = ReadClut(tagData, (int)offClut, inputChannels, outputChannels);

        return result;
    }

    private static IReadOnlyList<TagElement> ReadCurves(ReadOnlyMemory<byte> tagData, int offset, int count)
    {
        var curves = new TagElement[count];
        int cursor = offset;
        for (var i = 0; i < count; i++)
        {
            (TagElement el, int size) = ReadOneCurve(tagData, cursor);
            curves[i] = el;
            cursor += size;
            // Pad to next 4-byte boundary.
            int padded = (cursor + 3) & ~3;
            cursor = padded;
        }
        return curves;
    }

    private static (TagElement, int) ReadOneCurve(ReadOnlyMemory<byte> tagData, int offset)
    {
        if (offset < 0 || offset + 12 > tagData.Length)
            throw new IccParseException(
                $"Curve element offset {offset} (+12) past end of tag ({tagData.Length}).");

        ReadOnlySpan<byte> head = tagData.Span.Slice(offset, 4);
        uint sigBits =
            ((uint)head[0] << 24) | ((uint)head[1] << 16) | ((uint)head[2] << 8) | head[3];
        IccSignature sig = new(sigBits);

        int size;
        if (sig == TagTypeSignatures.Curve)
        {
            uint count = BinaryPrimitives.ReadUInt32BigEndian(tagData.Span.Slice(offset + 8, 4));
            size = checked(12 + 2 * (int)count);
        }
        else if (sig == TagTypeSignatures.ParametricCurve)
        {
            ushort fnType = BinaryPrimitives.ReadUInt16BigEndian(tagData.Span.Slice(offset + 8, 2));
            int paramCount = ParametricCurveTagElement.RequiredParameterCount(fnType);
            if (paramCount < 0)
                throw new IccParseException($"Embedded parametric curve has unknown function type {fnType}.");
            size = 12 + 4 * paramCount;
        }
        else
        {
            throw new IccParseException(
                $"Expected 'curv' or 'para' inside modern LUT curve slot; got '{sig}'.");
        }

        if (offset + size > tagData.Length)
            throw new IccParseException(
                $"Curve element at offset {offset} (size {size}) overruns tag ({tagData.Length}).");

        return (TagElementReader.Parse(tagData.Slice(offset, size)), size);
    }

    private static double[] ReadMatrix(ReadOnlyMemory<byte> tagData, int offset)
    {
        if (offset < 0 || offset + 48 > tagData.Length)
            throw new IccParseException($"Matrix at offset {offset} (+48) past end of tag ({tagData.Length}).");
        IccBinaryReader r = new(tagData)
        {
            Position = offset
        };
        var m = new double[12];
        for (var i = 0; i < 12; i++) m[i] = r.ReadS15Fixed16();
        return m;
    }

    private static LutClutData ReadClut(
        ReadOnlyMemory<byte> tagData, int offset, int inputChannels, int outputChannels)
    {
        if (offset < 0 || offset + 20 > tagData.Length)
            throw new IccParseException($"CLUT at offset {offset} (+20) past end of tag ({tagData.Length}).");

        IccBinaryReader r = new(tagData)
        {
            Position = offset
        };

        byte[] gridAll = r.ReadBytes(16).ToArray();
        int precision = r.ReadUInt8();
        r.Skip(3); // reserved

        if (precision != 1 && precision != 2)
            throw new IccParseException($"CLUT precision must be 1 or 2; got {precision}.");

        var gridPoints = new byte[inputChannels];
        Array.Copy(gridAll, 0, gridPoints, 0, inputChannels);

        long totalEntries = 1;
        for (var i = 0; i < inputChannels; i++)
        {
            if (gridPoints[i] < 2)
                throw new IccParseException($"CLUT grid dimension {i} must be ≥ 2; got {gridPoints[i]}.");
            totalEntries = checked(totalEntries * gridPoints[i]);
        }
        long totalValues = checked(totalEntries * outputChannels);
        long totalBytes = totalValues * precision;
        if (offset + 20 + totalBytes > tagData.Length)
            throw new IccParseException(
                $"CLUT body needs {totalBytes} bytes at offset {offset + 20} but tag is {tagData.Length} bytes.");
        if (totalValues > int.MaxValue)
            throw new IccParseException($"CLUT entry count {totalValues} exceeds int.MaxValue.");

        var values = new double[(int)totalValues];
        if (precision == 1)
        {
            ReadOnlySpan<byte> b = r.ReadBytes((int)totalBytes);
            for (var i = 0; i < values.Length; i++) values[i] = b[i] / 255.0;
        }
        else
        {
            for (var i = 0; i < values.Length; i++)
            {
                ushort v = r.ReadUInt16();
                values[i] = v / 65535.0;
            }
        }
        return new LutClutData(gridPoints, precision, values, outputChannels);
    }
}
