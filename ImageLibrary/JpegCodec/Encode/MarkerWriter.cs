using System;
using System.Collections.Generic;
using JpegCodec.Stream;

namespace JpegCodec.Encode;

internal static class MarkerWriter
{
    public static void WriteMarker(List<byte> output, JpegMarker marker)
    {
        output.Add(0xFF);
        output.Add((byte)marker);
    }

    public static void WriteSegment(List<byte> output, JpegMarker marker, ReadOnlySpan<byte> payload)
    {
        WriteMarker(output, marker);
        int length = payload.Length + 2;
        output.Add((byte)(length >> 8));
        output.Add((byte)length);
        for (var i = 0; i < payload.Length; i++) output.Add(payload[i]);
    }

    public static void WriteJfif(List<byte> output)
    {
        // JFIF 1.02, density units = 0, density = 1x1, no thumbnail.
        var payload = new byte[]
        {
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
            0x01, 0x02,             // major/minor version
            0x00,                   // units (0 = no units, aspect only)
            0x00, 0x01, 0x00, 0x01, // X/Y density
            0x00, 0x00,             // thumbnail width/height
        };
        WriteSegment(output, JpegMarker.App0, payload);
    }

    public static void WriteAdobeApp14(List<byte> output, byte colorTransform)
    {
        var payload = new byte[]
        {
            (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e',
            0x00, 0x64,         // DCTEncodeVersion = 100
            0x00, 0x00,         // APP14Flags0
            0x00, 0x00,         // APP14Flags1
            colorTransform,
        };
        WriteSegment(output, JpegMarker.App14, payload);
    }

    public static void WriteDqt(List<byte> output, byte tableId, ReadOnlySpan<ushort> values)
    {
        if (values.Length != 64) throw new ArgumentException("DQT values must be 64.", nameof(values));
        var precision16 = false;
        for (var i = 0; i < 64; i++) if (values[i] > 255) { precision16 = true; break; }

        var pqtq = (byte)((precision16 ? 1 : 0) << 4 | (tableId & 0x0F));
        int bodySize = 1 + 64 * (precision16 ? 2 : 1);
        var payload = new byte[bodySize];
        payload[0] = pqtq;
        if (precision16)
        {
            for (var i = 0; i < 64; i++)
            {
                payload[1 + 2 * i] = (byte)(values[i] >> 8);
                payload[2 + 2 * i] = (byte)values[i];
            }
        }
        else
        {
            for (var i = 0; i < 64; i++) payload[1 + i] = (byte)values[i];
        }
        WriteSegment(output, JpegMarker.Dqt, payload);
    }

    public static void WriteDht(List<byte> output, byte tableClass, byte tableId, byte[] bits, byte[] values)
    {
        if (bits.Length != 16) throw new ArgumentException("BITS must have 16 entries.", nameof(bits));
        var payload = new byte[1 + 16 + values.Length];
        payload[0] = (byte)((tableClass << 4) | (tableId & 0x0F));
        Array.Copy(bits, 0, payload, 1, 16);
        Array.Copy(values, 0, payload, 17, values.Length);
        WriteSegment(output, JpegMarker.Dht, payload);
    }

    public static void WriteSof0(List<byte> output,
        int width, int height, int precision, FrameComponentSpec[] components)
    {
        var payload = new byte[6 + 3 * components.Length];
        payload[0] = (byte)precision;
        payload[1] = (byte)(height >> 8);
        payload[2] = (byte)height;
        payload[3] = (byte)(width >> 8);
        payload[4] = (byte)width;
        payload[5] = (byte)components.Length;
        for (var i = 0; i < components.Length; i++)
        {
            payload[6 + 3 * i] = components[i].Id;
            payload[7 + 3 * i] = (byte)((components[i].HSampling << 4) | components[i].VSampling);
            payload[8 + 3 * i] = components[i].QuantTableId;
        }
        WriteSegment(output, JpegMarker.Sof0, payload);
    }

    public static void WriteSos(List<byte> output, ScanComponentSpec[] components,
        byte ss, byte se, byte ah, byte al)
    {
        var payload = new byte[1 + 2 * components.Length + 3];
        payload[0] = (byte)components.Length;
        for (var i = 0; i < components.Length; i++)
        {
            payload[1 + 2 * i] = components[i].Id;
            payload[2 + 2 * i] = (byte)((components[i].DcTableId << 4) | (components[i].AcTableId & 0x0F));
        }
        int tail = 1 + 2 * components.Length;
        payload[tail] = ss;
        payload[tail + 1] = se;
        payload[tail + 2] = (byte)((ah << 4) | (al & 0x0F));
        WriteSegment(output, JpegMarker.Sos, payload);
    }

    public static void WriteDri(List<byte> output, ushort interval)
    {
        var payload = new byte[] { (byte)(interval >> 8), (byte)interval };
        WriteSegment(output, JpegMarker.Dri, payload);
    }

    internal readonly struct FrameComponentSpec
    {
        public byte Id { get; }
        public byte HSampling { get; }
        public byte VSampling { get; }
        public byte QuantTableId { get; }
        public FrameComponentSpec(byte id, byte hSampling, byte vSampling, byte quantTableId)
        {
            Id = id; HSampling = hSampling; VSampling = vSampling; QuantTableId = quantTableId;
        }
    }

    internal readonly struct ScanComponentSpec
    {
        public byte Id { get; }
        public byte DcTableId { get; }
        public byte AcTableId { get; }
        public ScanComponentSpec(byte id, byte dcTableId, byte acTableId)
        {
            Id = id; DcTableId = dcTableId; AcTableId = acTableId;
        }
    }
}
