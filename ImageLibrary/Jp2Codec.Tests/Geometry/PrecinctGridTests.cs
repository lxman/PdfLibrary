using Jp2Codec.Geometry;

namespace Jp2Codec.Tests.Geometry;

public class PrecinctGridTests
{
    [Fact]
    public void Build_SinglePrecinctCoversCanvas()
    {
        // 16x16 canvas, PPx/PPy = 15 (32768). One giant precinct.
        var canvas = new CanvasRect(0, 0, 16, 16);
        var g = PrecinctGrid.Build(resolution: 2, ppx: 15, ppy: 15, canvas);
        Assert.Equal(1, g.NumPrecinctsWide);
        Assert.Equal(1, g.NumPrecinctsHigh);
        Assert.Equal(canvas, g.PrecinctRectOnResolution(0, 0));
    }

    [Fact]
    public void Build_MultiPrecinctGridCountsAndExtents()
    {
        // 16x16 canvas, PPx=PPy=2 (precinct = 4x4). 4x4 grid.
        var canvas = new CanvasRect(0, 0, 16, 16);
        var g = PrecinctGrid.Build(resolution: 1, ppx: 2, ppy: 2, canvas);
        Assert.Equal(4, g.NumPrecinctsWide);
        Assert.Equal(4, g.NumPrecinctsHigh);
        Assert.Equal(new CanvasRect(0, 0, 4, 4), g.PrecinctRectOnResolution(0, 0));
        Assert.Equal(new CanvasRect(4, 4, 8, 8), g.PrecinctRectOnResolution(1, 1));
        Assert.Equal(new CanvasRect(12, 12, 16, 16), g.PrecinctRectOnResolution(3, 3));
    }

    [Fact]
    public void Build_OffsetCanvas_HonoursFloorAnchoring()
    {
        // Canvas [3, 5) × [4, 6) is below precinct stride 4. Expectation:
        // floor(3/4)=0, ceil(5/4)=2 → 2 precincts wide.
        // floor(4/4)=1, ceil(6/4)=2 → 1 precinct high.
        var canvas = new CanvasRect(3, 4, 5, 6);
        var g = PrecinctGrid.Build(resolution: 1, ppx: 2, ppy: 2, canvas);
        Assert.Equal(2, g.NumPrecinctsWide);
        Assert.Equal(1, g.NumPrecinctsHigh);
        // First precinct on canvas: bounded by [0..4) × [4..8) anchored on
        // the multiple-of-stride grid, clipped to canvas → [3, 4) × [4, 6).
        Assert.Equal(new CanvasRect(3, 4, 4, 6), g.PrecinctRectOnResolution(0, 0));
        // Second precinct (x=1, y=0): [4..8) × [4..8), clipped to canvas → [4, 5) × [4, 6).
        Assert.Equal(new CanvasRect(4, 4, 5, 6), g.PrecinctRectOnResolution(1, 0));
    }

    [Fact]
    public void PrecinctRectOnSubband_Resolution0_PassesThroughOnLL()
    {
        // Resolution 0 (LL only): subband canvas == resolution canvas.
        var resCanvas = new CanvasRect(0, 0, 4, 4);
        var g = PrecinctGrid.Build(resolution: 0, ppx: 1, ppy: 1, resCanvas);
        var subCanvas = new CanvasRect(0, 0, 4, 4);
        Assert.Equal(new CanvasRect(0, 0, 2, 2), g.PrecinctRectOnSubband(0, 0, subCanvas));
        Assert.Equal(new CanvasRect(2, 0, 4, 2), g.PrecinctRectOnSubband(1, 0, subCanvas));
    }

    [Fact]
    public void PrecinctRectOnSubband_Resolution1_HalvesCoordinates()
    {
        // Resolution 1 with PPx=PPy=2 → precinct is 4x4 on resolution canvas.
        // Subband (HL/LH/HH) sits at half-scale; precinct [0,4)×[0,4) → [0,2)×[0,2).
        var resCanvas = new CanvasRect(0, 0, 8, 8);
        var g = PrecinctGrid.Build(resolution: 1, ppx: 2, ppy: 2, resCanvas);
        var hlCanvas = new CanvasRect(0, 0, 4, 4); // subband at half scale
        Assert.Equal(new CanvasRect(0, 0, 2, 2), g.PrecinctRectOnSubband(0, 0, hlCanvas));
        Assert.Equal(new CanvasRect(2, 0, 4, 2), g.PrecinctRectOnSubband(1, 0, hlCanvas));
        Assert.Equal(new CanvasRect(2, 2, 4, 4), g.PrecinctRectOnSubband(1, 1, hlCanvas));
    }

    [Fact]
    public void CodeBlockGrid_Build_SingleBlockCoversPrecinct()
    {
        // Code-block xcb=ycb=6 (64x64), PPx=PPy=15 (precinct big). Precinct
        // slice 8x8 → one code-block 8x8.
        var subCanvas = new CanvasRect(0, 0, 8, 8);
        var slice = new CanvasRect(0, 0, 8, 8);
        var g = CodeBlockGrid.Build(
            xcb: 6, ycb: 6, ppxOnSubband: 15, ppyOnSubband: 15,
            subCanvas, slice);
        Assert.Equal(6, g.CodeBlockWidthExponent);
        Assert.Equal(6, g.CodeBlockHeightExponent);
        Assert.Equal(1, g.CodeBlockColumns);
        Assert.Equal(1, g.CodeBlockRows);
        CanvasRect r = g.CodeBlockRectOnSubband(0, 0);
        Assert.Equal(new CanvasRect(0, 0, 8, 8), r);
    }

    [Fact]
    public void CodeBlockGrid_Build_ClampsToPrecinctExponent()
    {
        // xcb=ycb=5 (32x32), but PPx on subband = 2 (precinct=4x4). xcb' = ycb' = 2 → 4x4 blocks.
        // Subband canvas 16x16, precinct slice 16x16 → 4x4 grid of 4x4 blocks.
        var subCanvas = new CanvasRect(0, 0, 16, 16);
        var slice = new CanvasRect(0, 0, 16, 16);
        var g = CodeBlockGrid.Build(
            xcb: 5, ycb: 5, ppxOnSubband: 2, ppyOnSubband: 2,
            subCanvas, slice);
        Assert.Equal(2, g.CodeBlockWidthExponent);
        Assert.Equal(2, g.CodeBlockHeightExponent);
        Assert.Equal(4, g.CodeBlockColumns);
        Assert.Equal(4, g.CodeBlockRows);
    }

    [Fact]
    public void CodeBlockGrid_Build_EmptySliceProducesZeroGrid()
    {
        var sub = new CanvasRect(0, 0, 16, 16);
        var empty = new CanvasRect(5, 5, 5, 5);
        var g = CodeBlockGrid.Build(
            xcb: 6, ycb: 6, ppxOnSubband: 15, ppyOnSubband: 15,
            sub, empty);
        Assert.Equal(0, g.CodeBlockColumns);
        Assert.Equal(0, g.CodeBlockRows);
    }

    [Fact]
    public void CodeBlockGrid_OffsetPrecinctAndSubband_BlockRectsAreClipped()
    {
        // Subband [3..11) × [5..13) = 8x8. xcb=ycb=2 (4x4 blocks).
        // First block anchor: floor(3/4)*4 = 0; last block anchor: ceil(11/4)*4 = 12.
        // So blocks span x = 0..12 step 4 → 3 columns (each 4 wide). Same for rows.
        // Block (0, 0) on subband: raw [0..4) × [4..8) clipped to slice = [3..4) × [5..8).
        // Block (1, 0): raw [4..8) × [4..8) clipped → [4..8) × [5..8).
        var subCanvas = new CanvasRect(3, 5, 11, 13);
        var g = CodeBlockGrid.Build(
            xcb: 2, ycb: 2, ppxOnSubband: 15, ppyOnSubband: 15,
            subCanvas, subCanvas);
        Assert.Equal(3, g.CodeBlockColumns);
        Assert.Equal(3, g.CodeBlockRows);
        Assert.Equal(new CanvasRect(3, 5, 4, 8), g.CodeBlockRectOnSubband(0, 0));
        Assert.Equal(new CanvasRect(4, 5, 8, 8), g.CodeBlockRectOnSubband(1, 0));
        Assert.Equal(new CanvasRect(8, 5, 11, 8), g.CodeBlockRectOnSubband(2, 0));
        // Bottom-right block clips against both x1 and y1.
        Assert.Equal(new CanvasRect(8, 12, 11, 13), g.CodeBlockRectOnSubband(2, 2));
    }
}
