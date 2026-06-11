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

        // Validate dimensions and sampling factors against an untrusted SOF before any
        // downstream raster allocation. Sampling factors are 4-bit nibbles from the wire;
        // T.81 §A.1.1 limits them to 1..4, so 0 (divide-by-zero in MCU sizing) or 5..15
        // (an oversized MCU grid) are malformed. The raster-size cap rejects a hostile SOF
        // whose width*height*components would force a multi-gigabyte allocation.
        if (width == 0 || height == 0)
            throw new InvalidOperationException($"SOF declares a zero dimension ({width}x{height}).");

        foreach (FrameComponent fc in components)
        {
            if (fc.HorizontalSampling is < 1 or > 4 || fc.VerticalSampling is < 1 or > 4)
                throw new InvalidOperationException(
                    $"SOF sampling factors out of range (H={fc.HorizontalSampling}, V={fc.VerticalSampling}); must be 1..4.");
        }

        const long MaxRasterBytes = 512L << 20; // 512 MiB of 8-bit samples (~178 MP RGB)
        long rasterBytes = (long)width * height * nf;
        if (rasterBytes > MaxRasterBytes)
            throw new InvalidOperationException(
                $"JPEG raster {width}x{height}x{nf} ({rasterBytes} bytes) exceeds the {MaxRasterBytes}-byte limit.");

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
