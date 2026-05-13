using Jp2Codec.Quantization;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Quantization
{
    public sealed class SubbandLayoutTests
    {
        [Fact]
        public void EnumerateQcdOrder_NoDecomp_ReturnsSingleLlBandAtLevelZero()
        {
            SubbandDescriptor[] table = SubbandLayout.EnumerateQcdOrder(0);

            Assert.Single(table);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.LL, 0), table[0]);
        }

        [Fact]
        public void EnumerateQcdOrder_NL1_Returns_LL_HL_LH_HH_AllAtLevel1()
        {
            SubbandDescriptor[] table = SubbandLayout.EnumerateQcdOrder(1);

            Assert.Equal(4, table.Length);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.LL, 1), table[0]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HL, 1), table[1]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.LH, 1), table[2]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HH, 1), table[3]);
        }

        [Fact]
        public void EnumerateQcdOrder_NL3_OrdersDeepestLevelFirstHighestLast()
        {
            // F.3.1: NLLL, NLHL, NLLH, NLHH, (NL-1)HL, (NL-1)LH, (NL-1)HH, ..., 1HL, 1LH, 1HH.
            SubbandDescriptor[] table = SubbandLayout.EnumerateQcdOrder(3);

            Assert.Equal(10, table.Length); // 1 + 3 * 3
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.LL, 3), table[0]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HL, 3), table[1]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.LH, 3), table[2]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HH, 3), table[3]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HL, 2), table[4]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.LH, 2), table[5]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HH, 2), table[6]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HL, 1), table[7]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.LH, 1), table[8]);
            Assert.Equal(new SubbandDescriptor(SubbandOrientation.HH, 1), table[9]);
        }

        [Theory]
        [InlineData((int)SubbandOrientation.LL, 0)]
        [InlineData((int)SubbandOrientation.HL, 1)]
        [InlineData((int)SubbandOrientation.LH, 1)]
        [InlineData((int)SubbandOrientation.HH, 2)]
        public void Log2Gain_MatchesTableE1(int orientationValue, int expectedLog2Gain)
        {
            Assert.Equal(expectedLog2Gain, SubbandLayout.Log2Gain((SubbandOrientation)orientationValue));
        }
    }
}
