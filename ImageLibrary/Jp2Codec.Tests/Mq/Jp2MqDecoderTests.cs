using Jp2Codec.Mq;

namespace Jp2Codec.Tests.Mq
{
    /// <summary>
    /// Targeted tests for the J2K MQ decoder mechanics: initial register state
    /// after INITDEC for the three byte cases (regular, 0xFF + stuff, 0xFF +
    /// marker), and the renormalisation invariant during normal decoding.
    /// Higher-level decoding correctness is exercised once Tier-1 is in place
    /// and we can compare entire codeblocks against reference output.
    /// </summary>
    public sealed class Jp2MqDecoderTests
    {
        [Fact]
        public void InitDec_PlainBytes_LoadsExpectedRegisters()
        {
            // First byte 0x12, second byte 0x34. INITDEC: C = (0x12 << 16) + (0x34 << 8) = 0x00123400.
            // After C <<= 7 → 0x091A0000. CT = 8 - 7 = 1. BP advanced past byte 0.
            var d = new Jp2MqDecoder(new byte[] { 0x12, 0x34, 0x00, 0x00 }, 0, 4);

            Assert.Equal(0x8000u, d.A);
            Assert.Equal(0x091A0000u, d.C);
            Assert.Equal(1, d.CT);
            Assert.Equal(1, d.BP);
        }

        [Fact]
        public void InitDec_FfFollowedByStuffByte_UsesStuffPath()
        {
            // First byte 0xFF, next byte 0x00 (≤ 0x8F → stuff bit case).
            // INITDEC: C = (0xFF << 16) = 0x00FF0000. BYTEIN stuff path:
            //   BP++, C += 0x00 << 9 = 0; CT = 7. Then C <<= 7 → 0x7F800000, CT -= 7 → 0.
            var d = new Jp2MqDecoder(new byte[] { 0xFF, 0x00, 0x00 }, 0, 3);

            Assert.Equal(0x8000u, d.A);
            Assert.Equal(0x7F800000u, d.C);
            Assert.Equal(0, d.CT);
            Assert.Equal(1, d.BP);
        }

        [Fact]
        public void InitDec_FfFollowedByMarker_FeedsVirtualOnes()
        {
            // First byte 0xFF, peek byte 0xAC (> 0x8F → marker case).
            // INITDEC: C = (0xFF << 16) = 0x00FF0000. BYTEIN marker path adds 0xFF00,
            //   keeps BP on the 0xFF. C = 0x00FFFF00; CT = 8. Then C <<= 7 → 0x7FFF8000, CT = 1.
            var d = new Jp2MqDecoder(new byte[] { 0xFF, 0xAC, 0x00 }, 0, 3);

            Assert.Equal(0x8000u, d.A);
            Assert.Equal(0x7FFF8000u, d.C);
            Assert.Equal(1, d.CT);
            Assert.Equal(0, d.BP);
        }

        [Fact]
        public void InitDec_SingleByteBuffer_DoesNotThrow()
        {
            // BP starts at 0, ByteIn() advances to 1 == bpEnd. Past the end
            // BYTEIN feeds a virtual 0xFF sentinel byte (Annex C.3.4 fallback)
            // so the running interval keeps advancing rather than stalling on
            // uninitialised memory.
            // Manual: C = (0x12 << 16) | (0xFF << 8) = 0x0012FF00; << 7 = 0x097F8000.
            var d = new Jp2MqDecoder(new byte[] { 0x12 }, 0, 1);

            Assert.Equal(0x8000u, d.A);
            Assert.Equal(0x097F8000u, d.C);
            Assert.Equal(1, d.CT);
        }

        [Fact]
        public void Decode_KeepsAOverHalfRange_PerInvariant()
        {
            // Decode 20 decisions from a fresh context against a non-trivial stream
            // and check that the interval register A stays renormalised to ≥ 0x8000
            // after every call (Annex C.3.2 invariant).
            var d = new Jp2MqDecoder(
                new byte[] { 0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA4, 0x49, 0x00, 0x00 }, 0, 9);
            byte cx = 0;
            for (var i = 0; i < 20; i++)
            {
                int bit = d.Decode(ref cx);
                Assert.InRange(bit, 0, 1);
                Assert.True(d.A >= 0x8000u, $"call {i}: A=0x{d.A:X4} fell below 0x8000");
            }
        }

        [Fact]
        public void Decode_ContextByteUpdates_InPlace()
        {
            // After decoding several bits, the context byte must end up with a
            // valid Qe-table index (≤ 46) and a 0/1 MPS bit.
            var d = new Jp2MqDecoder(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, 4);
            byte cx = 0;
            for (var i = 0; i < 50; i++) d.Decode(ref cx);

            int index = cx & 0x7F;
            int mps = (cx >> 7) & 0x01;
            Assert.True(index <= 46, $"index {index} > 46");
            Assert.InRange(mps, 0, 1);
        }
    }
}
