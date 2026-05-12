using System.Reflection;
using Jbig2Decoder.Mq;
using Xunit.Abstractions;

namespace Jbig2Decoder.Tests.Arith;

/// <summary>
/// Differential test against jbig2dec's <c>jbig2_arith_int_decode</c>.
///
/// Same 32-byte canonical stream as the MQ-layer differential test. We run 32
/// successive integer decodes through a single shared IAx context (512 bytes)
/// and assert each (status, value) pair matches the captured oracle output.
///
/// Status: <c>true</c> = normal value (in <see cref="OracleEntry.Value"/>);
///         <c>false</c> = OOB (value undefined).
/// </summary>
public class IntegerDecoderTests
{
    private static readonly byte[] TestStream =
    [
        0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20, 0x00, 0x00,
        0x41, 0x0D, 0xBB, 0x86, 0xF4, 0x31, 0x7F, 0xFF, 0x88, 0xFF, 0x37, 0x47,
        0x1A, 0xDB, 0x6A, 0xDF, 0xFF, 0xAC, 0x00, 0x00
    ];

    public readonly record struct OracleEntry(bool Ok, int Value);

    // Captured from int_oracle on llmbox (gcc -DHAVE_CONFIG_H + libjbig2dec.a).
    // Each row mirrors `jbig2_arith_int_decode` returning rc=0 (normal) → Ok=true
    // or rc=1 (OOB) → Ok=false.
    private static readonly OracleEntry[] Expected =
    [
        new(true,   1), new(true,  -2), new(true,   5), new(true,   8),
        new(false,  0), new(true,   1), new(true,  12), new(true,  -2),
        new(true,  12), new(true,   8), new(true,  -2), new(true,   1),
        new(true,  15), new(false,  0), new(true,   8), new(true,  -3),
        new(false,  0), new(true,  -2), new(true,  -2), new(true,   0),
        new(false,  0), new(false,  0), new(true,  14), new(true,  -2),
        new(false,  0), new(true,   1), new(true,  -1), new(true,  -2),
        new(false,  0), new(false,  0), new(true,  -2), new(true,  -2)
    ];

    private readonly ITestOutputHelper _out;
    public IntegerDecoderTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Decode_AgainstJbig2dec_ProducesIdenticalIntegerSequence()
    {
        Assembly asm = typeof(MqDecoder).Assembly;
        Type mqType = asm.GetType("Jbig2Decoder.Mq.MqDecoder", throwOnError: true)!;
        ConstructorInfo mqCtor = mqType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 3);
        object mq = mqCtor.Invoke([TestStream, 0, TestStream.Length]);

        Type intType = asm.GetType("Jbig2Decoder.Arith.IntegerDecoder", throwOnError: true)!;
        ConstructorInfo intCtor = intType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
        object intDecoder = intCtor.Invoke([mq, "INT"]);
        MethodInfo decode = intType.GetMethod("Decode", BindingFlags.Public | BindingFlags.Instance)!;

        for (var i = 0; i < Expected.Length; i++)
        {
            object[] args = [0];
            var ok = (bool)decode.Invoke(intDecoder, args)!;
            var value = (int)args[0];

            OracleEntry exp = Expected[i];
            if (ok == exp.Ok && (!ok || value == exp.Value)) continue;

            _out.WriteLine($"Divergence at decode #{i}:");
            _out.WriteLine($"  oracle:  ok={exp.Ok}, value={(exp.Ok ? exp.Value.ToString() : "(OOB)")}");
            _out.WriteLine($"  decoder: ok={ok}, value={(ok ? value.ToString() : "(OOB)")}");
            Assert.Fail($"IntegerDecoder diverges from jbig2dec at decode #{i}");
        }
    }
}
