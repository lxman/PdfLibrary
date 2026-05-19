using ICCSharp.Profile;

namespace ICCSharp.Tests.Profile;

public class ProfileVersionTests
{
    [Theory]
    [InlineData(0x04300000u, 4, 3, 0)]
    [InlineData(0x02400000u, 2, 4, 0)]
    [InlineData(0x02100000u, 2, 1, 0)]
    [InlineData(0x04400000u, 4, 4, 0)]
    [InlineData(0x05230000u, 5, 2, 3)]
    public void FromRaw_decodes_bcd_major_minor_bugfix(uint raw, byte major, byte minor, byte bug)
    {
        ProfileVersion v = ProfileVersion.FromRaw(raw);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(bug, v.BugFix);
    }

    [Fact]
    public void Reserved_low_word_is_preserved()
    {
        ProfileVersion v = ProfileVersion.FromRaw(0x04300042u);
        Assert.Equal((ushort)0x0042, v.Reserved);
    }

    [Theory]
    [InlineData(0x04300000u)]
    [InlineData(0x02400000u)]
    [InlineData(0x05230000u)]
    [InlineData(0x04300042u)]
    public void ToRaw_is_inverse_of_FromRaw(uint raw)
    {
        ProfileVersion v = ProfileVersion.FromRaw(raw);
        Assert.Equal(raw, v.ToRaw());
    }

    [Fact]
    public void Comparison_orders_by_major_then_minor_then_bugfix()
    {
        ProfileVersion v23 = new(2, 3, 0);
        ProfileVersion v24 = new(2, 4, 0);
        ProfileVersion v240 = new(2, 4, 0);
        ProfileVersion v241 = new(2, 4, 1);
        ProfileVersion v30 = new(3, 0, 0);

        Assert.True(v23 < v24);
        Assert.True(v24 < v241);
        Assert.True(v241 < v30);
        Assert.True(v24 == v240);
    }

    [Fact]
    public void ToString_renders_major_minor_bugfix()
    {
        Assert.Equal("4.3.0", new ProfileVersion(4, 3, 0).ToString());
        Assert.Equal("2.4.0", new ProfileVersion(2, 4, 0).ToString());
    }
}
