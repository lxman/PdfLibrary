using System.Reflection;
using System.Text;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Tests.Mq;

/// <summary>
/// Differential test against jbig2dec's <c>test_arith.c</c> reference vector.
///
/// jbig2dec ships a 32-byte canonical MQ test stream and runs 256 successive
/// decoding decisions through it with a single evolving context (initial Qe
/// index = 0, MPS = 0). We replicate that exactly and assert bit-for-bit
/// equality against the trace captured from
/// <c>jbig2_arith.c</c> compiled with <c>-DTEST -DJBIG2_DEBUG_ARITH</c>.
///
/// jbig2dec consumes the stream a 32-bit word at a time while we consume it
/// byte-at-a-time, so the intermediate A/C/CT registers diverge — only the
/// decoded bit sequence is comparable. That sequence is the only thing that
/// matters at this layer.
/// </summary>
public class MqDifferentialTests
{
    private const int BitCount = 256;

    // jbig2dec/jbig2_arith.c, static const byte test_stream[].
    private static readonly byte[] TestStream =
    [
        0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20, 0x00, 0x00,
        0x41, 0x0D, 0xBB, 0x86, 0xF4, 0x31, 0x7F, 0xFF, 0x88, 0xFF, 0x37, 0x47,
        0x1A, 0xDB, 0x6A, 0xDF, 0xFF, 0xAC, 0x00, 0x00
    ];

    // 256 bits packed MSB-first into 32 bytes (decoded sequence; bit 0 = MSB of byte 0).
    // Captured from jbig2dec test_arith compiled with -DTEST -DJBIG2_DEBUG_ARITH.
    private static readonly byte[] ExpectedBitsPacked =
    [
        0x00, 0x02, 0x00, 0x51, 0x00, 0x00, 0x00, 0xC0,
        0x03, 0x52, 0x87, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA,
        0x82, 0xC0, 0x20, 0x00, 0xFC, 0xD7, 0x9E, 0xF6,
        0xBF, 0x7F, 0xED, 0x90, 0x4F, 0x46, 0xA3, 0xBF
    ];

    private static int ExpectedBitAt(int i) => (ExpectedBitsPacked[i >> 3] >> (7 - (i & 7))) & 1;

    private readonly ITestOutputHelper _out;
    public MqDifferentialTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ExpectedBitFixtureIsTheRightSize()
    {
        Assert.Equal(BitCount / 8, ExpectedBitsPacked.Length);
    }

    [Fact]
    public void Decode_AgainstJbig2dec_ProducesIdenticalBitSequence()
    {
        Type t = typeof(QeTable).Assembly.GetType("Jbig2Decoder.Mq.MqDecoder", throwOnError: true)!;
        ConstructorInfo ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 3);
        object decoder = ctor.Invoke([TestStream, 0, TestStream.Length]);

        MethodInfo decode = t.GetMethod("Decode", BindingFlags.Public | BindingFlags.Instance)!;

        // jbig2dec uses a single packed byte (low 7 bits = Qe index, top bit = MPS),
        // initialised to 0 (index 0, MPS 0). Persisted across all 256 calls.
        byte cx = 0;

        for (var i = 0; i < BitCount; i++)
        {
            object[] args = [cx];
            var bit = (int)decode.Invoke(decoder, args)!;
            cx = (byte)args[0];

            int expected = ExpectedBitAt(i);
            if (bit == expected) continue;

            // First-divergence diagnostic: dump the bit index, decoded vs expected,
            // and the surrounding bits so the failure is attribution-friendly.
            int windowStart = Math.Max(0, i - 8);
            int windowEnd = Math.Min(BitCount, i + 8);
            var window = new StringBuilder();
            for (int j = windowStart; j < windowEnd; j++)
                window.Append(ExpectedBitAt(j));
            _out.WriteLine($"First divergence at bit {i}: decoder={bit}, oracle={expected}");
            _out.WriteLine($"  oracle  bits [{windowStart}..{windowEnd}): {window}");
            _out.WriteLine($"  current cx = 0x{cx:X2} (Qe index = {cx & 0x7F}, MPS = {cx >> 7})");
            Assert.Fail($"MQ decoder diverges from jbig2dec at bit {i}");
        }
    }
}
