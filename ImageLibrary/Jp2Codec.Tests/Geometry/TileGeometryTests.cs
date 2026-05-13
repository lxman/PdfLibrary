using Jp2Codec.Codestream.Segments;
using Jp2Codec.Geometry;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Geometry;

public class TileGeometryTests
{
    // Construct a SIZ with whole-image-fits-in-one-tile geometry, no offset,
    // identity subsampling for every component.
    private static SizSegment MakeSimpleSiz(int width, int height, int components = 1, byte xrsiz = 1, byte yrsiz = 1)
    {
        var comps = new SizComponent[components];
        for (var i = 0; i < components; i++)
            comps[i] = new SizComponent(8, false, xrsiz, yrsiz);
        return new SizSegment(
            capabilities: 0,
            referenceGridWidth: (uint)width,
            referenceGridHeight: (uint)height,
            imageHorizontalOffset: 0u,
            imageVerticalOffset: 0u,
            tileWidth: (uint)width,
            tileHeight: (uint)height,
            tileHorizontalOffset: 0u,
            tileVerticalOffset: 0u,
            components: comps);
    }

    [Fact]
    public void TileRect_SingleTile_FullImage()
    {
        SizSegment siz = MakeSimpleSiz(16, 16);
        CanvasRect r = TileGeometry.TileRectOnReferenceGrid(siz, 0, 0);
        Assert.Equal(new CanvasRect(0, 0, 16, 16), r);
    }

    [Fact]
    public void TileRect_WithOffsetTileGrid_ClipsToImageOrigin()
    {
        // Reference grid 1024x576; tile 192x108 anchored at (224, 12).
        // First tile rect: tx0 = max(224, 0) = 224, but wait — XOsiz is the image
        // origin offset. We want the tile to start at the IMAGE origin where
        // it intersects. Spec D.7: tx0(u,v) = max(XTOsiz + u·XTsiz, XOsiz).
        // With u=0: tx0 = max(224, XOsiz). With XOsiz=224, tx0=224, tx1=416.
        var siz = new SizSegment(
            capabilities: 0,
            referenceGridWidth: 1024u,
            referenceGridHeight: 576u,
            imageHorizontalOffset: 224u,
            imageVerticalOffset: 12u,
            tileWidth: 192u,
            tileHeight: 108u,
            tileHorizontalOffset: 224u,
            tileVerticalOffset: 12u,
            components: new[] { new SizComponent(8, false, 1, 1) });

            // First tile (u=0, v=0): tx0 = max(224 + 0*192, 224) = 224.
            // tx1 = min(224 + 1*192, 1024) = 416. ty0 = 12, ty1 = 120.
            CanvasRect r0 = TileGeometry.TileRectOnReferenceGrid(siz, 0, 0);
            Assert.Equal(new CanvasRect(224, 12, 416, 120), r0);

            // Bottom-right corner tile (u=4, v=5): tx0=max(224+4*192,224)=992
            // tx1=min(224+5*192,1024)=1024, ty0=max(12+5*108,12)=552, ty1=min(12+6*108,576)=576.
            CanvasRect r45 = TileGeometry.TileRectOnReferenceGrid(siz, 4, 5);
            Assert.Equal(new CanvasRect(992, 552, 1024, 576), r45);
    }

    [Fact]
    public void TileComponentRect_NoSubsampling_PassThrough()
    {
        SizSegment siz = MakeSimpleSiz(16, 16);
        CanvasRect tile = TileGeometry.TileRectOnReferenceGrid(siz, 0, 0);
        CanvasRect c = TileGeometry.TileComponentRect(siz, tile, 0);
        Assert.Equal(tile, c);
    }

    [Fact]
    public void TileComponentRect_With2xSubsampling_HalfDimensions()
    {
        // Component has 2x2 subsampling. Tile is 16x16 on reference grid.
        // tcx0 = ceil(0/2) = 0, tcx1 = ceil(16/2) = 8 → 8x8 component.
        SizSegment siz = MakeSimpleSiz(16, 16, components: 1, xrsiz: 2, yrsiz: 2);
        CanvasRect tile = TileGeometry.TileRectOnReferenceGrid(siz, 0, 0);
        CanvasRect c = TileGeometry.TileComponentRect(siz, tile, 0);
        Assert.Equal(new CanvasRect(0, 0, 8, 8), c);
    }

    [Fact]
    public void ResolutionRect_ZeroDecompositionLevels_PassThrough()
    {
        var tc = new CanvasRect(0, 0, 16, 16);
        CanvasRect r = TileGeometry.ResolutionRect(tc, numDecompositionLevels: 0, resolution: 0);
        Assert.Equal(tc, r);
    }

    [Fact]
    public void ResolutionRect_NL2_AllResolutions()
    {
        // 16x16 component, NL=2:
        // r=0 (LL_2): ceil(0..16 / 4) = 0..4 → 4x4.
        // r=1: ceil(0..16 / 2) = 0..8 → 8x8.
        // r=2 (full): ceil(0..16 / 1) = 0..16 → 16x16.
        var tc = new CanvasRect(0, 0, 16, 16);
        Assert.Equal(new CanvasRect(0, 0, 4, 4),
            TileGeometry.ResolutionRect(tc, 2, 0));
        Assert.Equal(new CanvasRect(0, 0, 8, 8),
            TileGeometry.ResolutionRect(tc, 2, 1));
        Assert.Equal(new CanvasRect(0, 0, 16, 16),
            TileGeometry.ResolutionRect(tc, 2, 2));
    }

    [Fact]
    public void SubbandRect_LevelOne_LLHLLHHHCoordinates()
    {
        // Tile-component 8x8 starting at origin, decomposition level n_b=1.
        // shift = 2^(1-1) = 1.
        // LL: ceil((0..8)/2) = 0..4.
        // HL: ceil((0-1)/2)=0, ceil((8-1)/2)=4 → x in [0, 4); y as LL.
        // LH: x like LL, y like HL (shifted vertically).
        // HH: both shifted.
        var tc = new CanvasRect(0, 0, 8, 8);
        Assert.Equal(new CanvasRect(0, 0, 4, 4),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.LL));
        Assert.Equal(new CanvasRect(0, 0, 4, 4),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.HL));
        Assert.Equal(new CanvasRect(0, 0, 4, 4),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.LH));
        Assert.Equal(new CanvasRect(0, 0, 4, 4),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.HH));
    }

    [Fact]
    public void SubbandRect_NonZeroOrigin_ShiftAffectsHL()
    {
        // Tile-component 6x8 at origin (3, 5), decomposition level n_b=1.
        // LL: x = ceil(3/2)..ceil(9/2) = 2..5 (width 3). y = ceil(5/2)..ceil(13/2)=3..7 (height 4).
        // HL: x = ceil(2/2)..ceil(8/2) = 1..4 (width 3). y = LL = 3..7.
        // LH: x = LL = 2..5. y = ceil(4/2)..ceil(12/2) = 2..6 (height 4).
        // HH: x = HL = 1..4. y = LH = 2..6.
        var tc = new CanvasRect(3, 5, 9, 13);
        Assert.Equal(new CanvasRect(2, 3, 5, 7),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.LL));
        Assert.Equal(new CanvasRect(1, 3, 4, 7),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.HL));
        Assert.Equal(new CanvasRect(2, 2, 5, 6),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.LH));
        Assert.Equal(new CanvasRect(1, 2, 4, 6),
            TileGeometry.SubbandRect(tc, 1, SubbandOrientation.HH));
    }

    [Fact]
    public void SubbandRect_AndResolutionRectAgreeOnLLAtDeepestLevel()
    {
        // For NL=3, resolution 0 == LL at decomposition level 3.
        var tc = new CanvasRect(0, 0, 32, 32);
        const int nl = 3;
        CanvasRect ll = TileGeometry.SubbandRect(tc, nl, SubbandOrientation.LL);
        CanvasRect res0 = TileGeometry.ResolutionRect(tc, nl, 0);
        Assert.Equal(res0, ll);
    }
}
