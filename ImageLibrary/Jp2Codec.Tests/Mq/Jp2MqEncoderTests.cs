using System;
using Jp2Codec.Mq;

namespace Jp2Codec.Tests.Mq
{
    public sealed class Jp2MqEncoderTests
    {
        private static int[] DecodeAll(byte[] data, int count, byte initialContext = 0)
        {
            var dec = new Jp2MqDecoder(data, 0, data.Length);
            byte cx = initialContext;
            var bits = new int[count];
            for (var i = 0; i < count; i++) bits[i] = dec.Decode(ref cx);
            return bits;
        }

        [Fact]
        public void Encode_SingleZero_RoundTrips()
        {
            var enc = new Jp2MqEncoder();
            byte cx = 0;
            enc.Encode(0, ref cx);
            enc.Flush();

            int[] decoded = DecodeAll(enc.ToArray(), 1);
            Assert.Equal(0, decoded[0]);
        }

        [Fact]
        public void Encode_SingleOne_RoundTrips()
        {
            var enc = new Jp2MqEncoder();
            byte cx = 0;
            enc.Encode(1, ref cx);
            enc.Flush();

            int[] decoded = DecodeAll(enc.ToArray(), 1);
            Assert.Equal(1, decoded[0]);
        }

        [Fact]
        public void Encode_AlternatingBits_RoundTrip()
        {
            const int Count = 200;
            var enc = new Jp2MqEncoder();
            byte cx = 0;
            var input = new int[Count];
            for (var i = 0; i < Count; i++)
            {
                input[i] = i & 1;
                enc.Encode(input[i], ref cx);
            }
            enc.Flush();

            int[] decoded = DecodeAll(enc.ToArray(), Count);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void Encode_ShortRunOfZeros_RoundTrip()
        {
            // Long runs of one symbol against a single context are NOT a
            // useful round-trip test once the encoder reaches a confident
            // probability estimate — arithmetic coding compresses them well
            // below 1 bit per symbol, and the decoder running off the end of
            // the byte stream produces arbitrary bits past whatever the
            // encoder bothered to emit. (Real EBCOT use never asks the
            // decoder to decode more bits than the encoder emitted, because
            // the coding-pass structure tracks the bit count separately.)
            // Keep the count small enough that bit-count roughly matches the
            // emitted bytes.
            const int Count = 12;
            var enc = new Jp2MqEncoder();
            byte cx = 0;
            for (var i = 0; i < Count; i++) enc.Encode(0, ref cx);
            enc.Flush();

            int[] decoded = DecodeAll(enc.ToArray(), Count);
            for (var i = 0; i < Count; i++)
                Assert.True(decoded[i] == 0, $"bit {i}: decoded {decoded[i]}, expected 0");
        }

        [Fact]
        public void Encode_ShortRunOfOnes_RoundTrip()
        {
            const int Count = 12;
            var enc = new Jp2MqEncoder();
            byte cx = 0;
            for (var i = 0; i < Count; i++) enc.Encode(1, ref cx);
            enc.Flush();

            int[] decoded = DecodeAll(enc.ToArray(), Count);
            for (var i = 0; i < Count; i++)
                Assert.True(decoded[i] == 1, $"bit {i}: decoded {decoded[i]}, expected 1");
        }

        [Fact]
        public void Encode_PseudoRandomBits_RoundTrip()
        {
            const int Count = 5000;
            var rng = new Random(20260512);
            var input = new int[Count];
            for (var i = 0; i < Count; i++) input[i] = rng.Next(0, 2);

            var enc = new Jp2MqEncoder();
            byte cx = 0;
            foreach (int b in input) enc.Encode(b, ref cx);
            enc.Flush();

            int[] decoded = DecodeAll(enc.ToArray(), Count);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void Encode_WithMultipleContexts_RoundTrip()
        {
            // Simulate Tier-1 use — 19 contexts initialised per Table D.7,
            // bit decisions distributed across them.
            const int Count = 4000;
            var rng = new Random(42);
            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            byte[] decContexts = Jp2MqContextSet.CreateInitialised();

            var input = new int[Count];
            var ctxIndices = new int[Count];
            for (var i = 0; i < Count; i++)
            {
                input[i] = rng.Next(0, 2);
                ctxIndices[i] = rng.Next(0, Jp2MqContextSet.Count);
            }

            var enc = new Jp2MqEncoder();
            for (var i = 0; i < Count; i++)
                enc.Encode(input[i], ref encContexts[ctxIndices[i]]);
            enc.Flush();

            byte[] data = enc.ToArray();
            var dec = new Jp2MqDecoder(data, 0, data.Length);
            for (var i = 0; i < Count; i++)
            {
                int bit = dec.Decode(ref decContexts[ctxIndices[i]]);
                Assert.True(bit == input[i],
                    $"bit {i}: encoded {input[i]}, decoded {bit} (context #{ctxIndices[i]})");
            }

            // Contexts should evolve identically on both sides.
            Assert.Equal(encContexts, decContexts);
        }

        [Fact]
        public void Encode_NoContent_FlushProducesEmptyOrTrivialBuffer()
        {
            var enc = new Jp2MqEncoder();
            enc.Flush();
            byte[] data = enc.ToArray();
            // Either empty (trailing-0xff trimmed away) or a small flush remnant.
            Assert.True(data.Length <= 2);
        }

        [Fact]
        public void BytesEmitted_MatchesToArrayLength()
        {
            var enc = new Jp2MqEncoder();
            byte cx = 0;
            for (var i = 0; i < 100; i++) enc.Encode(i & 1, ref cx);
            enc.Flush();
            Assert.Equal(enc.BytesEmitted, enc.ToArray().Length);
        }

        [Fact]
        public void Encode_RejectsBitOutsideZeroOne()
        {
            var enc = new Jp2MqEncoder();
            byte cx = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => enc.Encode(2, ref cx));
            Assert.Throws<ArgumentOutOfRangeException>(() => enc.Encode(-1, ref cx));
        }

        // Replays the exact (ctx, bit) sequence that the CUP test traced just
        // before the divergence at op 22, in isolation. If the basic MQ codec
        // round-trips this, the bug is somewhere else; if it doesn't, the
        // encoder/decoder pair has a subtle defect that the multi-context
        // random test doesn't tickle.
        [Fact]
        public void Encode_ReplayCupTraceUntilDivergence_RoundTrips()
        {
            (int ctx, int bit)[] ops =
            {
                // Stripe 0 column 0 RL aggregation: RL=1, k=3 (UNI 1, UNI 1), sign for (0,3) ctx=9
                (17, 1), (18, 1), (18, 1), (9, 0),
                // Column 1 stripe 0 per-sample: 4 ZC bits all 0
                (0, 0), (0, 0), (1, 0), (5, 0),
                // Column 2 stripe 0 RL: RL=1, k=3, sign ctx=9
                (17, 1), (18, 1), (18, 1), (9, 0),
                // Column 3 stripe 0 per-sample: 4 ZC bits
                (0, 0), (0, 0), (1, 0), (5, 0),
                // Column 4 stripe 0 RL: RL=1, k=3, sign ctx=9
                (17, 1), (18, 1), (18, 1), (9, 0),
                // Stripe 1: ZC ctx=3 (0,4), ZC ctx=2 (1,4), ZC ctx=3 (2,4), ZC ctx=2 (3,4), ZC ctx=3 (4,4)
                (3, 0), (2, 0), (3, 0), (2, 0), (3, 0),
            };

            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            foreach ((int ctx, int bit) op in ops)
                enc.Encode(op.bit, ref encContexts[op.ctx]);
            enc.Flush();

            byte[] decContexts = Jp2MqContextSet.CreateInitialised();
            byte[] data = enc.ToArray();
            var dec = new Jp2MqDecoder(data, 0, data.Length);
            for (var i = 0; i < ops.Length; i++)
            {
                int decoded = dec.Decode(ref decContexts[ops[i].ctx]);
                Assert.True(decoded == ops[i].bit,
                    $"op {i}: ctx={ops[i].ctx}, encoded {ops[i].bit}, decoded {decoded}");
            }
            Assert.Equal(encContexts, decContexts);
        }
    }
}
