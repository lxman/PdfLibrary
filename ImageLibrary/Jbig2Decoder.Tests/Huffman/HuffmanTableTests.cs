using System.Reflection;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Tests.Huffman;

/// <summary>
/// Sanity tests for the Huffman table builder + bit reader.
///
/// Encodes a known sequence of values using the canonical codes implied by
/// each Annex B table, then verifies the decoder reads them back identically.
/// Acts as a tripwire on the canonical-Huffman code-assignment math.
/// </summary>
public class HuffmanTableTests
{
    private readonly ITestOutputHelper _out;
    public HuffmanTableTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TableB4_DecodesKnownSequence()
    {
        // Table B.4 (D), HTOOB=false, lines:
        //   "0"     value 1    (PrefLen 1, RangeLen 0)
        //   "10"    value 2    (PrefLen 2, RangeLen 0)
        //   "110"   value 3
        //   "1110"  values 4..11  (PrefLen 4, RangeLen 3)
        //   "11110" values 12..75 (PrefLen 5, RangeLen 6)
        //   "11111" values 76..   (high, RangeLen 32)
        //
        // Hand-encoded sequence: 1, 2, 3, 5, 12
        //   "0" + "10" + "110" + "1110 001" + "11110 000000"
        //   = 0 10 110 1110001 11110000000
        //   = 22 bits: 0,1,0,1,1,0,1,1,1,0,0,0,1,1,1,1,1,0,0,0,0,0,0  (24 bits with 1 pad zero)
        //   Group into bytes (MSB-first):
        //     bits[0..7]  = 0_10_110_11 = 0b01011011 = 0x5B
        //     bits[8..15] = 1_0001_111 = 0b10001111 = 0x8F
        //     bits[16..23]= 1_0_000000 = 0b10000000 = 0x80
        var input = new byte[] { 0x5B, 0x8F, 0x80 };

        Assembly asm = typeof(MqDecoder).Assembly;
        Type paramsType = asm.GetType("Jbig2Decoder.Huffman.HuffmanParams", throwOnError: true)!;
        Type standardType = asm.GetType("Jbig2Decoder.Huffman.StandardHuffmanTables", throwOnError: true)!;
        object dParams = standardType.GetField("D")!.GetValue(null)!;

        Type tableType = asm.GetType("Jbig2Decoder.Huffman.HuffmanTable", throwOnError: true)!;
        ConstructorInfo tableCtor = tableType.GetConstructors().Single();
        object table = tableCtor.Invoke([dParams]);

        Type readerType = asm.GetType("Jbig2Decoder.Huffman.HuffmanBitReader", throwOnError: true)!;
        ConstructorInfo readerCtor = readerType.GetConstructors().Single();
        object reader = readerCtor.Invoke([input, 0, input.Length]);

        MethodInfo decode = tableType.GetMethod("Decode")!;
        int[] expected = [1, 2, 3, 5, 12];
        for (var i = 0; i < expected.Length; i++)
        {
            object[] args = [reader, 0];
            var ok = (bool)decode.Invoke(table, args)!;
            var value = (int)args[1];
            Assert.True(ok, $"value {i} unexpected OOB");
            if (value != expected[i])
            {
                _out.WriteLine($"value {i}: expected {expected[i]}, got {value}");
                Assert.Fail($"Huffman Table B.4 decode mismatch at index {i}");
            }
        }
    }
}
