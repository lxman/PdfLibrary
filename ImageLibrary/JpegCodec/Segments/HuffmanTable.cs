using System;
using System.Collections.Generic;

namespace JpegCodec.Segments;

// DHT (Define Huffman Table) segment, T.81 §B.2.4.2. A single DHT segment
// may carry multiple tables.
internal sealed class HuffmanTable
{
    public byte TableId { get; }
    public byte Class { get; }           // 0 = DC, 1 = AC
    public byte[] Bits { get; }          // BITS[1..16], i.e. count of codes per length
    public byte[] Values { get; }        // HUFFVAL, length = sum(Bits)

    public HuffmanTable(byte tableClass, byte tableId, byte[] bits, byte[] values)
    {
        Class = tableClass;
        TableId = tableId;
        Bits = bits;
        Values = values;
    }

    public static HuffmanTable[] ParseAll(ReadOnlySpan<byte> payload)
    {
        var tables = new List<HuffmanTable>(4);
        var pos = 0;
        while (pos < payload.Length)
        {
            if (pos + 17 > payload.Length)
                throw new InvalidOperationException("DHT payload truncated reading header.");
            byte tcth = payload[pos++];
            var tableClass = (byte)(tcth >> 4);   // Tc: 0 DC, 1 AC
            var tableId = (byte)(tcth & 0x0F);    // Th
            if (tableClass > 1)
                throw new InvalidOperationException($"Invalid Huffman table class {tableClass}.");

            var bits = new byte[16];
            var sum = 0;
            for (var i = 0; i < 16; i++)
            {
                bits[i] = payload[pos + i];
                sum += bits[i];
            }
            pos += 16;
            if (sum > 256)
                throw new InvalidOperationException(
                    $"Huffman table has {sum} entries; max 256.");
            if (pos + sum > payload.Length)
                throw new InvalidOperationException(
                    $"DHT payload truncated reading {sum} HUFFVAL entries.");

            var values = new byte[sum];
            for (var i = 0; i < sum; i++)
                values[i] = payload[pos + i];
            pos += sum;

            tables.Add(new HuffmanTable(tableClass, tableId, bits, values));
        }
        return tables.ToArray();
    }
}
