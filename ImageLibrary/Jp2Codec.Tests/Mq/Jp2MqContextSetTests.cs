using Jp2Codec.Mq;

namespace Jp2Codec.Tests.Mq
{
    public sealed class Jp2MqContextSetTests
    {
        [Fact]
        public void CreateInitialised_Returns19Entries()
        {
            byte[] ctx = Jp2MqContextSet.CreateInitialised();
            Assert.Equal(19, ctx.Length);
        }

        [Fact]
        public void CreateInitialised_RunLengthContextStartsAtIndex3()
        {
            byte[] ctx = Jp2MqContextSet.CreateInitialised();
            // Bits 0..6 = Qe index (3), bit 7 = MPS (0). So byte value = 3.
            Assert.Equal(3, ctx[Jp2MqContextSet.RunLength]);
        }

        [Fact]
        public void CreateInitialised_UniformContextStartsAtIndex46()
        {
            byte[] ctx = Jp2MqContextSet.CreateInitialised();
            Assert.Equal(46, ctx[Jp2MqContextSet.Uniform]);
        }

        [Fact]
        public void CreateInitialised_ZcZeroNeighboursContextStartsAtIndex4()
        {
            // Per Table D.7 / Table C-6: the "all zero neighbours" zero-coding
            // context (ZC[0]) starts at Qe-state 4, not 0. Every other ZC, SC,
            // MR context starts at 0.
            byte[] ctx = Jp2MqContextSet.CreateInitialised();
            Assert.Equal(4, ctx[Jp2MqContextSet.ZeroCoding + 0]);
        }

        [Fact]
        public void CreateInitialised_RemainingZcSignMrContextsStartAtZero()
        {
            byte[] ctx = Jp2MqContextSet.CreateInitialised();
            // ZeroCoding 1..8, SignCoding 9..13, MagnitudeRefinement 14..16.
            for (var i = Jp2MqContextSet.ZeroCoding + 1; i <= Jp2MqContextSet.MagnitudeRefinement + 2; i++)
                Assert.Equal(0, ctx[i]);
        }

        [Fact]
        public void ResetInPlace_RestoresInitialState_AfterMutation()
        {
            byte[] ctx = Jp2MqContextSet.CreateInitialised();
            // Simulate decoder mutation across all contexts.
            for (var i = 0; i < ctx.Length; i++) ctx[i] = 0xFF;
            Jp2MqContextSet.ResetInPlace(ctx);

            Assert.Equal(4, ctx[Jp2MqContextSet.ZeroCoding + 0]);
            Assert.Equal(0, ctx[Jp2MqContextSet.ZeroCoding + 1]);
            Assert.Equal(0, ctx[Jp2MqContextSet.SignCoding]);
            Assert.Equal(0, ctx[Jp2MqContextSet.MagnitudeRefinement]);
            Assert.Equal(3, ctx[Jp2MqContextSet.RunLength]);
            Assert.Equal(46, ctx[Jp2MqContextSet.Uniform]);
        }
    }
}
