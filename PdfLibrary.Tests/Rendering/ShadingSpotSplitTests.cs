using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class ShadingSpotSplitTests
{
    [Fact]
    public void SpotNames_ReturnsOnlySpotKindNamesInOrder()
    {
        var names = new[] { "GWG Green", "Cyan", "PANTONE 032 C", "Black" };
        Assert.Equal(new[] { "GWG Green", "PANTONE 032 C" }, ShadingSpotSplit.SpotNames(names));
    }

    [Fact]
    public void Split_DeviceNSpotPlusProcess_SplitsByName()
    {
        // DeviceN [GWG Green (spot), Cyan (process)] at components (0.5, 1.0).
        var names = new[] { "GWG Green", "Cyan" };
        var spot = new byte[1];
        uint proc = ShadingSpotSplit.Split([0.5, 1.0], names, spot, 0);

        Assert.Equal(128, spot[0]);                 // GWG Green tint 0.5 → 128
        Assert.Equal(0xFF000000u, proc);            // Cyan 1.0 → C plate; M/Y/K zero (spot alternate NOT folded)
    }

    [Fact]
    public void Split_PureSeparation_ProcessAllZero()
    {
        var names = new[] { "GWG Green" };
        var spot = new byte[1];
        uint proc = ShadingSpotSplit.Split([1.0], names, spot, 0);

        Assert.Equal(255, spot[0]);
        Assert.Equal(0u, proc);                     // no process colorant → process CMYK all zero
    }

    [Fact]
    public void Split_TwoSpots_NonZeroDestOffset_LandsAtOffsetPlusIndex()
    {
        // Two spots, no process colorant, written into "stop 1" of a 3-stop*2-spot buffer (destOffset 2).
        var names = new[] { "GWG Green", "PANTONE 032 C" };
        var spot = new byte[6];   // 3 stops * 2 spots
        uint proc = ShadingSpotSplit.Split([0.5, 0.2], names, spot, destOffset: 2);

        Assert.Equal(128, spot[2]);   // GWG Green (s=0) at destOffset + 0
        Assert.Equal(51, spot[3]);    // PANTONE 032 C (s=1) at destOffset + 1
        Assert.Equal(0, spot[0]);     // untouched slots stay 0
        Assert.Equal(0, spot[1]);
        Assert.Equal(0, spot[4]);
        Assert.Equal(0, spot[5]);
        Assert.Equal(0u, proc);       // no process colorant
    }

    [Fact]
    public void Split_AllNone_ContributeNothing()
    {
        var names = new[] { "None", "GWG Green" };   // "None" must be skipped, not treated as a spot
        var spot = new byte[1];
        uint proc = ShadingSpotSplit.Split([0.0, 0.4], names, spot, 0);

        Assert.Equal(102, spot[0]);                  // GWG Green 0.4 → 102 lands at index 0 (None skipped)
        Assert.Equal(0u, proc);
    }
}
