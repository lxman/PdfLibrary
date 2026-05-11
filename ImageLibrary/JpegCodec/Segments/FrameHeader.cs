using System;
using JpegCodec.Stream;

namespace JpegCodec.Segments;

// SOF (Start Of Frame) segment, T.81 §B.2.2.
internal sealed class FrameHeader
{
    public JpegMarker Marker { get; }
    public byte Precision { get; }
    public ushort Height { get; }
    public ushort Width { get; }
    public byte NumberOfComponents { get; }
    public FrameComponent[] Components { get; }

    private FrameHeader(
        JpegMarker marker,
        byte precision,
        ushort height,
        ushort width,
        byte numberOfComponents,
        FrameComponent[] components)
    {
        Marker = marker;
        Precision = precision;
        Height = height;
        Width = width;
        NumberOfComponents = numberOfComponents;
        Components = components;
    }

    public static FrameHeader Parse(JpegMarker marker, ReadOnlySpan<byte> payload)
    {
        // Layout (T.81 §B.2.2):
        //   1 byte   Precision (P)
        //   2 bytes  Number of Lines (Y) — image height
        //   2 bytes  Number of Samples Per Line (X) — image width
        //   1 byte   Number of Components in Frame (Nf)
        //   Per component (3 bytes each):
        //     1 byte  Component identifier (Ci)
        //     1 byte  Sampling factors (Hi << 4 | Vi)
        //     1 byte  Quantization table destination selector (Tqi)
        const int FixedSize = 6;
        if (payload.Length < FixedSize)
            throw new InvalidOperationException(
                $"SOF payload too short ({payload.Length}); need at least {FixedSize}.");

        byte precision = payload[0];
        ushort height = BigEndian.ReadUInt16(payload, 1);
        ushort width = BigEndian.ReadUInt16(payload, 3);
        byte nf = payload[5];

        if (payload.Length != FixedSize + 3 * nf)
            throw new InvalidOperationException(
                $"SOF payload size {payload.Length} mismatched for Nf={nf}.");

        var components = new FrameComponent[nf];
        for (var i = 0; i < nf; i++)
        {
            int off = FixedSize + 3 * i;
            byte id = payload[off];
            byte sampling = payload[off + 1];
            byte tq = payload[off + 2];
            components[i] = new FrameComponent(
                identifier: id,
                horizontalSampling: (byte)(sampling >> 4),
                verticalSampling: (byte)(sampling & 0x0F),
                quantizationTableId: tq);
        }

        return new FrameHeader(marker, precision, height, width, nf, components);
    }
}

internal readonly struct FrameComponent
{
    public byte Identifier { get; }
    public byte HorizontalSampling { get; }
    public byte VerticalSampling { get; }
    public byte QuantizationTableId { get; }

    public FrameComponent(byte identifier, byte horizontalSampling, byte verticalSampling, byte quantizationTableId)
    {
        Identifier = identifier;
        HorizontalSampling = horizontalSampling;
        VerticalSampling = verticalSampling;
        QuantizationTableId = quantizationTableId;
    }
}
