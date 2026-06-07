using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    /// <summary>
    /// Driver-level coverage of the SEGSYM (segmentation symbols) code-block
    /// style flag (ISO/IEC 15444-1 D.5.4 / Table A-19 bit 5). The four bits
    /// {1, 0, 1, 0} are coded against the uniform context (18) after every
    /// CUP pass; the decoder verifies them and throws if any bit disagrees.
    /// </summary>
    public sealed class Tier1SegSymTests
    {
        private const int W = 4, H = 4;

        [Fact]
        public void CupOnlyPass_CorrectSegSym_Decodes()
        {
            byte[] data = EncodeCupWithRlSkip(emitSegSym: true);

            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, segSym: true);
            dec.RunPasses(new Jp2MqDecoder(data, 0, data.Length), passCount: 1);

            Assert.Equal(1, dec.PassesCompleted);
            // No coefficients became significant — the segsym tail is the
            // only thing beyond the four RL-skip zero bits.
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Assert.Equal(0, dec.State.GetFlags(x, y));
        }

        [Fact]
        public void CupOnlyPass_WrongSegSymBit_Throws()
        {
            // Replace the third expected bit (1) with 0 so the tail reads
            // 1, 0, 0, 0 — mismatch should fire at bit index 2.
            byte[] data = EncodeCupWithSegSymOverride(1, 0, 0, 0);

            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, segSym: true);
            var mq = new Jp2MqDecoder(data, 0, data.Length);

            Assert.Throws<InvalidDataException>(() => dec.RunPasses(mq, 1));
        }

        [Fact]
        public void CupOnlyPass_AllZeroSegSym_Throws()
        {
            // Hard-fault: all four tail bits are 0. Mismatch at bit 0.
            byte[] data = EncodeCupWithSegSymOverride(0, 0, 0, 0);

            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, segSym: true);
            var mq = new Jp2MqDecoder(data, 0, data.Length);

            Assert.Throws<InvalidDataException>(() => dec.RunPasses(mq, 1));
        }

        [Fact]
        public void DefaultStyle_IgnoresSegSymBytesEntirely()
        {
            // Encode a corrupt segsym tail. With segSym OFF the driver must
            // not touch those bytes — the CUP completes after the four RL
            // bits and the trailing bytes simply go unread.
            byte[] data = EncodeCupWithSegSymOverride(0, 1, 1, 0);

            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, segSym: false);
            dec.RunPasses(new Jp2MqDecoder(data, 0, data.Length), passCount: 1);

            Assert.Equal(1, dec.PassesCompleted);
        }

        [Fact]
        public void FourPassRoundTrip_SegSymOnEveryCup_Decodes()
        {
            // CUP at firstBp=5, SPP+MRP at 4, CUP at 4 — two CUPs, two
            // segsym tails. Verify the driver consumes them in stride.
            const int FirstBp = 5;
            const int Passes = 4;

            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();

            // Pass 0 (CUP @ bp 5): 4 RL-skip + segsym
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            WriteSegSym(enc, encContexts, 1, 0, 1, 0);

            // Pass 1 (SPP @ bp 4): nothing to encode (no candidates).
            // Pass 2 (MRP @ bp 4): nothing (no significant coefs).

            // Pass 3 (CUP @ bp 4): 4 RL-skip + segsym
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            WriteSegSym(enc, encContexts, 1, 0, 1, 0);

            enc.Flush();
            byte[] data = enc.ToArray();

            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, segSym: true);
            dec.RunPasses(new Jp2MqDecoder(data, 0, data.Length), Passes);

            Assert.Equal(Passes, dec.PassesCompleted);
        }

        [Fact]
        public void FourPassRoundTrip_SegSymMissingOnSecondCup_Throws()
        {
            // First CUP gets a correct segsym tail, second CUP gets {0,0,0,0}.
            // The driver should accept pass 0 and fail on pass 3.
            const int FirstBp = 5;
            const int Passes = 4;

            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();

            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            WriteSegSym(enc, encContexts, 1, 0, 1, 0);

            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            WriteSegSym(enc, encContexts, 0, 0, 0, 0); // wrong

            enc.Flush();
            byte[] data = enc.ToArray();

            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, segSym: true);
            var mq = new Jp2MqDecoder(data, 0, data.Length);

            Assert.Throws<InvalidDataException>(() => dec.RunPasses(mq, Passes));
        }

        // ---- Encoding helpers --------------------------------------------

        private static byte[] EncodeCupWithRlSkip(bool emitSegSym)
        {
            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            if (emitSegSym) WriteSegSym(enc, encContexts, 1, 0, 1, 0);
            enc.Flush();
            return enc.ToArray();
        }

        private static byte[] EncodeCupWithSegSymOverride(int b0, int b1, int b2, int b3)
        {
            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            WriteSegSym(enc, encContexts, b0, b1, b2, b3);
            enc.Flush();
            return enc.ToArray();
        }

        private static void WriteSegSym(
            Jp2MqEncoder enc, byte[] contexts,
            int b0, int b1, int b2, int b3)
        {
            enc.Encode(b0, ref contexts[Jp2MqContextSet.Uniform]);
            enc.Encode(b1, ref contexts[Jp2MqContextSet.Uniform]);
            enc.Encode(b2, ref contexts[Jp2MqContextSet.Uniform]);
            enc.Encode(b3, ref contexts[Jp2MqContextSet.Uniform]);
        }
    }
}
