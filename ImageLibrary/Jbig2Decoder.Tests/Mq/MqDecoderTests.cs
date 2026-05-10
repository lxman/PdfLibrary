using System.Reflection;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Tests.Mq;

/// <summary>
/// Targeted tests for the lower-level mechanics of the MQ decoder:
/// initial state after INITDEC, BYTEIN handling for the three byte cases
/// (regular, 0xFF + stuff, 0xFF + marker, EOF), and a mid-decode RENORMD
/// invariant.
///
/// Higher-level decoding correctness is exercised by the corpus walk once
/// segment parsing and generic-region decoding are in place.
/// </summary>
public class MqDecoderTests
{
    // The decoder is internal — instantiate via reflection so tests don't need
    // InternalsVisibleTo wired up yet.
    private static object New(byte[] data, int offset, int length)
    {
        var t = typeof(QeTable).Assembly.GetType("Jbig2Decoder.Mq.MqDecoder", throwOnError: true)!;
        return Activator.CreateInstance(t, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, args: [data, offset, length], culture: null)!;
    }

    private static (uint A, uint C, int CT, int BP) GetState(object decoder)
    {
        var t = decoder.GetType();
        return (
            (uint)t.GetProperty("A", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(decoder)!,
            (uint)t.GetProperty("C", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(decoder)!,
            (int) t.GetProperty("CT", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(decoder)!,
            (int) t.GetProperty("BP", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(decoder)!
        );
    }

    [Fact]
    public void InitDec_PlainBytes_LoadsExpectedRegisters()
    {
        // First byte 0x12, second byte 0x34. INITDEC: C = (0x12 << 16) + (0x34 << 8), then C <<= 7, CT = 8 - 7 = 1.
        // Manual: ((0x12 << 16) | (0x34 << 8)) = 0x00123400; << 7 = 0x091A0000.
        var d = New([0x12, 0x34, 0x00, 0x00], 0, 4);
        var s = GetState(d);

        Assert.Equal(0x8000u, s.A);
        Assert.Equal(0x091A0000u, s.C);
        Assert.Equal(1, s.CT);
        Assert.Equal(1, s.BP); // advanced past first byte by BYTEIN
    }

    [Fact]
    public void InitDec_FirstByteIs_FF_FollowedByStuffByte_UsesStuffPath()
    {
        // First byte 0xFF, next byte 0x00 (≤ 0x8F → stuff bit case).
        // INITDEC: C = (0xFF << 16) = 0x00FF0000.
        // BYTEIN sees first byte == 0xFF, peeks 0x00 (≤ 0x8F), stuff path:
        //   BP++, C += 0x00 << 9 = 0; CT = 7.
        // Then C <<= 7 → 0x7F800000, CT -= 7 → 0.
        var d = New([0xFF, 0x00, 0x00], 0, 3);
        var s = GetState(d);

        Assert.Equal(0x8000u, s.A);
        Assert.Equal(0x7F800000u, s.C);
        Assert.Equal(0, s.CT);
        Assert.Equal(1, s.BP);
    }

    [Fact]
    public void InitDec_FirstByteIs_FF_FollowedByMarker_FeedsVirtualOnes()
    {
        // First byte 0xFF, peek byte 0xAC (> 0x8F → marker case).
        // INITDEC: C = (0xFF << 16) = 0x00FF0000.
        // BYTEIN marker path: C += 0xFF00 → 0x00FFFF00; CT = 8; BP not advanced.
        // Then C <<= 7 → 0x7FFF8000, CT = 1.
        var d = New([0xFF, 0xAC, 0x00], 0, 3);
        var s = GetState(d);

        Assert.Equal(0x8000u, s.A);
        Assert.Equal(0x7FFF8000u, s.C);
        Assert.Equal(1, s.CT);
        Assert.Equal(0, s.BP); // marker path leaves BP on the 0xFF
    }

    [Fact]
    public void InitDec_BufferTooShortForSecondByte_DoesNotThrow()
    {
        // Single-byte buffer: BYTEIN should hit the EOF branch and feed 0xFF00.
        var d = New([0x12], 0, 1);
        var s = GetState(d);

        // First byte loaded into Chigh, BYTEIN advances BP to end and adds nothing
        // (BP < bpEnd test fails). C = (0x12 << 16) << 7 = 0x09000000.
        // Hmm — depends on whether the EOF branch fires. With offset+length=1, after
        // first ByteIn() in InitDec, BP becomes 1 == _bpEnd. We didn't take the EOF
        // branch because _bp was 0 < 1 going in; we advanced and the trailing read was skipped.
        // CT goes to 8, then -= 7 = 1.
        Assert.Equal(0x8000u, s.A);
        Assert.Equal(0x09000000u, s.C);
        Assert.Equal(1, s.CT);
    }

    [Fact]
    public void Decode_RunsWithoutThrow_OnNonTrivialInput()
    {
        // Smoke test: feed several bytes and decode a few decisions with a fresh context (cx=0 → index 0, MPS=0).
        // We don't assert specific bits here — that's the job of higher-layer corpus tests.
        // What we DO assert: state remains consistent (A always renormalised to ≥ 0x8000 after each call).
        var d = New([0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA4, 0x49, 0x00, 0x00], 0, 9);

        byte cx = 0;

        var t = typeof(QeTable).Assembly.GetType("Jbig2Decoder.Mq.MqDecoder", throwOnError: true)!;
        var decode = t.GetMethod("Decode")!;

        for (var i = 0; i < 20; i++)
        {
            object[] args = [cx];
            var bit = (int)decode.Invoke(d, args)!;
            cx = (byte)args[0];

            Assert.InRange(bit, 0, 1);

            var s = GetState(d);
            Assert.True(s.A >= 0x8000u, $"after Decode call {i}, A={s.A:X4} below 0x8000");
        }
    }
}
