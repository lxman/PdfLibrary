using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Tests.Mq;

/// <summary>
/// Pin every row of T.88 Table E.1 against the spec.
/// Failures here mean the table transcription is wrong;
/// any downstream MQ decoder bug investigation should rule this out first.
/// </summary>
public class QeTableTests
{
    [Fact]
    public void Tables_HaveCorrectLength()
    {
        Assert.Equal(QeTable.Length, QeTable.Qe.Length);
        Assert.Equal(QeTable.Length, QeTable.NMPS.Length);
        Assert.Equal(QeTable.Length, QeTable.NLPS.Length);
        Assert.Equal(QeTable.Length, QeTable.Switch.Length);
    }

    // Format: index, Qe, NMPS, NLPS, Switch
    [Theory]
    [InlineData(0, 0x5601,  1,  1, true)]
    [InlineData(1, 0x3401,  2,  6, false)]
    [InlineData(2, 0x1801,  3,  9, false)]
    [InlineData(3, 0x0AC1,  4, 12, false)]
    [InlineData(4, 0x0521,  5, 29, false)]
    [InlineData(5, 0x0221, 38, 33, false)]
    [InlineData(6, 0x5601,  7,  6, true)]
    [InlineData(13, 0x1601, 29, 21, false)]
    [InlineData(14, 0x5601, 15, 14, true)]
    [InlineData(45, 0x0001, 45, 43, false)]
    [InlineData(46, 0x5601, 46, 46, false)]
    public void Row_MatchesSpec(int index, int qe, int nmps, int nlps, bool sw)
    {
        Assert.Equal(qe, QeTable.Qe[index]);
        Assert.Equal(nmps, QeTable.NMPS[index]);
        Assert.Equal(nlps, QeTable.NLPS[index]);
        Assert.Equal(sw, QeTable.Switch[index]);
    }

    [Fact]
    public void Switch_OnlySetForRows_0_6_14()
    {
        // Per Table E.1, exactly indices 0, 6, 14 carry the SWITCH bit.
        for (var i = 0; i < QeTable.Length; i++)
        {
            bool expected = i is 0 or 6 or 14;
            Assert.Equal(expected, QeTable.Switch[i]);
        }
    }
}
