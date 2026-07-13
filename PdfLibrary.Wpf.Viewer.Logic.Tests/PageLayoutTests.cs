using PdfLibrary.Wpf.Viewer.Logic;
using Xunit;

public class PageLayoutTests
{
    // 3 pages, height 100 each, gap 10 → offsets 0, 110, 220; total 320.
    private static PageLayout Three() => new(new double[] { 100, 100, 100 }, gap: 10);

    [Fact] public void TotalHeight_SumsPagesAndGaps() => Assert.Equal(320, Three().TotalHeight);

    [Fact] public void OffsetOf_AccountsForGaps()
    {
        var l = Three();
        Assert.Equal(0, l.OffsetOf(0));
        Assert.Equal(110, l.OffsetOf(1));
        Assert.Equal(220, l.OffsetOf(2));
    }

    [Fact] public void VisibleRange_TopViewport_ShowsFirstPages()
    {
        var (first, last) = Three().VisibleRange(scrollTop: 0, viewportHeight: 150);
        Assert.Equal(0, first);
        Assert.Equal(1, last);   // page 1 starts at 110 < 150
    }

    [Fact] public void VisibleRange_Clamps()
    {
        var (first, last) = Three().VisibleRange(scrollTop: 1000, viewportHeight: 150);
        Assert.Equal(2, first);
        Assert.Equal(2, last);
    }

    [Fact] public void FocusedPage_IsPageUnderViewportCenter()
    {
        // scrollTop 60, viewport 100 → center at y=110 → page 1 (spans 110..210).
        Assert.Equal(1, Three().FocusedPage(scrollTop: 60, viewportHeight: 100));
    }
}
