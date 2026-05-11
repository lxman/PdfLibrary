using System;
using JpegCodec.Stream;

namespace JpegCodec.Segments;

// DQT (Define Quantization Table) segment, T.81 §B.2.4.1. A single DQT
// segment may carry multiple tables; ParseAll yields them in order.
internal sealed class QuantizationTable
{
    public byte TableId { get; }
    public byte Precision { get; }       // 0 = 8-bit, 1 = 16-bit
    public ushort[] Values { get; }      // 64 entries in zigzag order

    private QuantizationTable(byte tableId, byte precision, ushort[] values)
    {
        TableId = tableId;
        Precision = precision;
        Values = values;
    }

    public static QuantizationTable[] ParseAll(ReadOnlySpan<byte> payload)
    {
        var tables = new System.Collections.Generic.List<QuantizationTable>(4);
        var pos = 0;
        while (pos < payload.Length)
        {
            byte pqtq = payload[pos++];
            var precision = (byte)(pqtq >> 4);   // Pq
            var tableId = (byte)(pqtq & 0x0F);   // Tq
            int valueSize = precision == 0 ? 1 : 2;
            int needed = 64 * valueSize;
            if (pos + needed > payload.Length)
                throw new InvalidOperationException(
                    $"DQT payload truncated reading table {tableId} (precision {precision}).");

            var values = new ushort[64];
            for (var i = 0; i < 64; i++)
            {
                if (precision == 0)
                {
                    values[i] = payload[pos + i];
                }
                else
                {
                    values[i] = BigEndian.ReadUInt16(payload, pos + 2 * i);
                }
            }
            pos += needed;
            tables.Add(new QuantizationTable(tableId, precision, values));
        }
        return tables.ToArray();
    }
}
