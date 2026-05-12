using JpegCodec.Decode;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec.Tests.Decode;

public class HuffmanDecoderTests
{
    // T.81 Annex K Table K.3 — Luma DC. Symbol 0 = "00", symbol 1 = "010",
    // symbol 2 = "011", ..., symbol 5 = "110", symbol 6 = "1110", ...,
    // symbol B = "111111110".
    private static readonly byte[] LumaDcBits =
        [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] LumaDcValues =
        [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];

    private static HuffmanCanonicalTable LumaDcTable()
        => HuffmanCanonicalTable.Build(LumaDcBits, LumaDcValues);

    // Helper: pack a sequence of (bits, length) into a byte stream, MSB
    // first. Pads the final byte with 1s (T.81 §F.1.2.3 convention) — does
    // not matter for these tests but keeps the byte stream JPEG-shaped.
    private static byte[] PackBits(params (int bits, int length)[] tokens)
    {
        var output = new List<byte>();
        var bitBuffer = 0;
        var bitsInBuffer = 0;
        void Emit(byte b)
        {
            output.Add(b);
            // T.81 §F.1.2.3 byte stuffing — required to prevent the byte
            // source from misreading data as a marker.
            if (b == 0xFF) output.Add(0x00);
        }
        foreach ((int bits, int length) in tokens)
        {
            bitBuffer = (bitBuffer << length) | bits;
            bitsInBuffer += length;
            while (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                Emit((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }
        if (bitsInBuffer > 0)
        {
            // Pad with 1s on the right.
            int shift = 8 - bitsInBuffer;
            Emit((byte)(((bitBuffer << shift) | ((1 << shift) - 1)) & 0xFF));
        }
        return output.ToArray();
    }

    [Fact]
    public void DecodeSymbol_LumaDc_Symbol0()
    {
        HuffmanCanonicalTable table = LumaDcTable();
        // Code "00" (length 2) — symbol 0.
        byte[] data = PackBits((0b00, 2));

        var reader = new JpegBitReader(new JpegByteSource(data, 0));
        Assert.Equal(0x00, HuffmanDecoder.DecodeSymbol(reader, table));
    }

    [Fact]
    public void DecodeSymbol_LumaDc_AllSpecValues()
    {
        HuffmanCanonicalTable table = LumaDcTable();
        (int code, int length, int expectedSymbol)[] cases =
        [
            (0b00,          2, 0x00),
            (0b010,         3, 0x01),
            (0b011,         3, 0x02),
            (0b100,         3, 0x03),
            (0b101,         3, 0x04),
            (0b110,         3, 0x05),
            (0b1110,        4, 0x06),
            (0b11110,       5, 0x07),
            (0b111110,      6, 0x08),
            (0b1111110,     7, 0x09),
            (0b11111110,    8, 0x0A),
            (0b111111110,   9, 0x0B),
        ];

        foreach ((int code, int length, int expected) in cases)
        {
            byte[] data = PackBits((code, length));
            var reader = new JpegBitReader(new JpegByteSource(data, 0));
            Assert.Equal(expected, HuffmanDecoder.DecodeSymbol(reader, table));
        }
    }

    [Fact]
    public void DecodeSymbol_ConcatenatedStream_DecodesInSequence()
    {
        HuffmanCanonicalTable table = LumaDcTable();
        // "00" (sym 0) | "010" (sym 1) | "1110" (sym 6) | "111111110" (sym B)
        byte[] data = PackBits(
            (0b00, 2),
            (0b010, 3),
            (0b1110, 4),
            (0b111111110, 9));

        var reader = new JpegBitReader(new JpegByteSource(data, 0));
        Assert.Equal(0x00, HuffmanDecoder.DecodeSymbol(reader, table));
        Assert.Equal(0x01, HuffmanDecoder.DecodeSymbol(reader, table));
        Assert.Equal(0x06, HuffmanDecoder.DecodeSymbol(reader, table));
        Assert.Equal(0x0B, HuffmanDecoder.DecodeSymbol(reader, table));
    }

    [Fact]
    public void DecodeSymbol_ThenReceive_T81_AnnexF14_DcDifference()
    {
        // T.81 §F.1.4 worked example: a DC coefficient encoded as
        // (category SSSS, then SSSS bits). Here SSSS=4 → symbol byte 4,
        // followed by 4 bits whose value EXTENDed gives the signed DC
        // delta.
        HuffmanCanonicalTable table = LumaDcTable();
        // First emit Huffman code for SSSS=4 (which is "101"), then 4
        // bits = 0011 (representing -12 after EXTEND).
        byte[] data = PackBits(
            (0b101, 3),         // SSSS = 4 → symbol byte 4
            (0b0011, 4));       // raw value 3, Extend(3,4) = -12

        var reader = new JpegBitReader(new JpegByteSource(data, 0));

        int ssss = HuffmanDecoder.DecodeSymbol(reader, table);
        Assert.Equal(4, ssss);

        int dcDelta = reader.Receive(ssss);
        Assert.Equal(-12, dcDelta);
    }
}
