namespace PdfLibrary.Wpf.Viewer.Logic;

/// <summary>Vertical continuous-scroll geometry: cumulative page offsets, the visible index range for a
/// given scroll position, and the focused page (under the viewport's vertical center). Pure — no WPF —
/// so the viewer's virtualization + focused-page form routing are unit-testable off Windows.</summary>
public sealed class PageLayout
{
    private readonly double[] _offsets;   // top of each page
    private readonly IReadOnlyList<double> _heights;
    private readonly double _gap;

    public PageLayout(IReadOnlyList<double> pageHeights, double gap)
    {
        _heights = pageHeights;
        _gap = gap;
        _offsets = new double[pageHeights.Count];
        double y = 0;
        for (int i = 0; i < pageHeights.Count; i++) { _offsets[i] = y; y += pageHeights[i] + gap; }
        TotalHeight = pageHeights.Count == 0 ? 0 : y - gap;   // no trailing gap
    }

    public double TotalHeight { get; }
    public double OffsetOf(int index) => _offsets[index];

    public (int first, int last) VisibleRange(double scrollTop, double viewportHeight)
    {
        if (_heights.Count == 0) return (0, -1);
        double bottom = scrollTop + viewportHeight;
        // Default first to the last page (not 0): if scrollTop is past every page's end, the search
        // loop below never breaks, and first must land on the last page, not the first.
        int first = _heights.Count - 1, last = _heights.Count - 1;
        for (int i = 0; i < _heights.Count; i++)
        {
            double top = _offsets[i], end = top + _heights[i];
            if (end >= scrollTop) { first = i; break; }
        }
        for (int i = first; i < _heights.Count; i++)
        {
            if (_offsets[i] > bottom) { last = i - 1; break; }
            last = i;
        }
        return (Math.Clamp(first, 0, _heights.Count - 1), Math.Clamp(last, 0, _heights.Count - 1));
    }

    public int FocusedPage(double scrollTop, double viewportHeight)
    {
        if (_heights.Count == 0) return 0;
        double center = scrollTop + viewportHeight / 2;
        for (int i = 0; i < _heights.Count; i++)
            if (center < _offsets[i] + _heights[i]) return i;
        return _heights.Count - 1;
    }
}
