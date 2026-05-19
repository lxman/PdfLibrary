using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tests.Profile;

public class ColorSpaceTests
{
    [Fact]
    public void Channel_counts_match_spec()
    {
        Assert.Equal(3, ColorSpaceSignatures.ChannelCount(ColorSpaceSignatures.XYZ));
        Assert.Equal(3, ColorSpaceSignatures.ChannelCount(ColorSpaceSignatures.Lab));
        Assert.Equal(3, ColorSpaceSignatures.ChannelCount(ColorSpaceSignatures.RGB));
        Assert.Equal(1, ColorSpaceSignatures.ChannelCount(ColorSpaceSignatures.Gray));
        Assert.Equal(4, ColorSpaceSignatures.ChannelCount(ColorSpaceSignatures.CMYK));
        Assert.Equal(3, ColorSpaceSignatures.ChannelCount(ColorSpaceSignatures.CMY));
    }

    [Theory]
    [InlineData("2CLR", 2)]
    [InlineData("3CLR", 3)]
    [InlineData("4CLR", 4)]
    [InlineData("5CLR", 5)]
    [InlineData("6CLR", 6)]
    [InlineData("7CLR", 7)]
    [InlineData("8CLR", 8)]
    [InlineData("9CLR", 9)]
    [InlineData("ACLR", 10)]
    [InlineData("FCLR", 15)]
    public void NCLR_channel_counts(string sig, int n)
    {
        Assert.Equal(n, ColorSpaceSignatures.ChannelCount(IccSignature.FromAscii(sig)));
    }

    [Fact]
    public void Unknown_signature_returns_zero()
    {
        Assert.Equal(0, ColorSpaceSignatures.ChannelCount(IccSignature.FromAscii("zzzz")));
    }
}
