using System.Reflection;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Tests.Arith;

/// <summary>
/// Differential test against jbig2dec's <c>jbig2_arith_iaid_decode</c>.
/// Two fixtures (codeLen=8, codeLen=4) verify the symbol-ID decoder both for
/// the typical text-region length and a smaller one to exercise different
/// loop-iteration counts and context-array sizes.
/// </summary>
public class IaidDecoderTests
{
    private static readonly byte[] TestStream =
    [
        0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20, 0x00, 0x00,
        0x41, 0x0D, 0xBB, 0x86, 0xF4, 0x31, 0x7F, 0xFF, 0x88, 0xFF, 0x37, 0x47,
        0x1A, 0xDB, 0x6A, 0xDF, 0xFF, 0xAC, 0x00, 0x00
    ];

    // jbig2dec iaid_oracle ./iaid_oracle 8 — 32 IDs of 8 bits each.
    private static readonly int[] ExpectedCodeLen8 =
    [
        29, 16, 89, 26, 211, 29, 113, 65, 95, 87, 64, 29, 29, 192, 21, 22,
        21, 159, 22, 29, 53, 206, 21, 66, 21, 23, 29, 157, 206, 22, 59, 29
    ];

    // jbig2dec iaid_oracle ./iaid_oracle 4 — 32 IDs of 4 bits each.
    private static readonly int[] ExpectedCodeLen4 =
    [
        1, 10, 4, 5, 12, 2, 1, 12, 0, 1, 5, 13, 2, 10, 5, 5,
        5, 5, 1, 10, 12, 9, 13, 2, 1, 1, 5, 3, 2, 1, 4, 3
    ];

    private readonly ITestOutputHelper _out;
    public IaidDecoderTests(ITestOutputHelper output) => _out = output;

    public static TheoryData<int, int[]> Fixtures() => new()
    {
        { 8, ExpectedCodeLen8 },
        { 4, ExpectedCodeLen4 },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decode_AgainstJbig2dec_ProducesIdenticalIdSequence(int codeLength, int[] expected)
    {
        Assembly asm = typeof(MqDecoder).Assembly;
        Type mqType = asm.GetType("Jbig2Decoder.Mq.MqDecoder", throwOnError: true)!;
        ConstructorInfo mqCtor = mqType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 3);
        object mq = mqCtor.Invoke([TestStream, 0, TestStream.Length]);

        Type iaidType = asm.GetType("Jbig2Decoder.Arith.IaidDecoder", throwOnError: true)!;
        ConstructorInfo iaidCtor = iaidType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
        object iaid = iaidCtor.Invoke([mq, codeLength]);
        MethodInfo decode = iaidType.GetMethod("Decode", BindingFlags.Public | BindingFlags.Instance)!;

        for (var i = 0; i < expected.Length; i++)
        {
            var actual = (int)decode.Invoke(iaid, null)!;
            if (actual == expected[i]) continue;

            _out.WriteLine($"Divergence at decode #{i} (codeLen={codeLength}):");
            _out.WriteLine($"  oracle:  {expected[i]}");
            _out.WriteLine($"  decoder: {actual}");
            Assert.Fail($"IaidDecoder diverges from jbig2dec at decode #{i} (codeLen={codeLength})");
        }
    }
}
