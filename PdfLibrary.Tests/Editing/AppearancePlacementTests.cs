using PdfLibrary.Editing.Stamping;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Coverage for <see cref="AppearancePlacement.ComputeAA"/> — ISO 32000-1 §12.5.5. The production
/// corpus this program targets has identity /Matrix and BBox == Rect == MediaBox on every document,
/// so it exercises none of the hard cases below. These tests are the entire safety net for the
/// algorithm; several are cross-checked by an independent hand computation (recorded in comments)
/// and/or a round-trip self-consistency check (apply AA back to BBox's corners and confirm the
/// result's bounding box is Rect), not just by trusting the implementation's own arithmetic.
/// </summary>
public class AppearancePlacementTests
{
    [Fact]
    public void Identity_CorpusShape_ProducesTranslateByZero()
    {
        // The measured production shape: /Rect == /BBox == /MediaBox, /Matrix identity.
        double[] bbox = [0, 0, 792, 612];
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [0, 0, 792, 612];

        double[]? aa = AppearancePlacement.ComputeAA(bbox, matrix, rect);

        Assert.NotNull(aa);
        Assert.Equal(new[] { 1.0, 0, 0, 1, 0, 0 }, aa!);
    }

    [Fact]
    public void Rotation_Matrix_MapsBBoxCornersExactlyOntoRect()
    {
        // BBox is 100x50. /Matrix rotates 90 degrees CCW: [cos90, sin90, -sin90, cos90, 0, 0]
        // = [0, 1, -1, 0, 0, 0]. Rect is 50x100 — the rotated box's aspect ratio, sized so the fit
        // in step (b) is a pure identity scale (sx = sy = 1).
        //
        // Independent hand computation (PDF point transform x' = a.x + c.y + e, y' = b.x + d.y + f):
        //   (0,0)   -> (0,0)
        //   (100,0) -> (0,100)
        //   (100,50)-> (-50,100)
        //   (0,50)  -> (-50,0)
        // Transformed appearance box: x in [-50,0], y in [0,100] -> width 50, height 100.
        // A maps that onto Rect [0,0,50,100]: sx=1, sy=1, ex = 0 - 1*(-50) = 50, ey = 0 - 1*0 = 0.
        // A = [1,0,0,1,50,0].
        // AA = Matrix x A (apply Matrix then A):
        //   a = 0*1 + 1*0 = 0
        //   b = 0*0 + 1*1 = 1
        //   c = -1*1 + 0*0 = -1
        //   d = -1*0 + 0*1 = 0
        //   e = 0*1 + 0*0 + 50 = 50
        //   f = 0*0 + 0*1 + 0 = 0
        // AA = [0, 1, -1, 0, 50, 0].
        double[] bbox = [0, 0, 100, 50];
        double[] matrix = [0, 1, -1, 0, 0, 0];
        double[] rect = [0, 0, 50, 100];

        double[]? aa = AppearancePlacement.ComputeAA(bbox, matrix, rect);

        Assert.NotNull(aa);
        Assert.Equal(new[] { 0.0, 1, -1, 0, 50, 0 }, aa!);

        // Round-trip cross-check: apply AA to the four BBox corners directly and confirm the
        // resulting quadrilateral's bounding box is exactly Rect — a check independent of trusting
        // ComputeAA's internal arithmetic, since it only relies on the point-transform formula.
        AssertBBoxTransformsOntoRect(bbox, aa!, rect);
    }

    [Fact]
    public void Scale_Matrix_WhenItAlreadyFitsRect_LeavesAIdentity()
    {
        // /Matrix itself scales the appearance 2x. BBox is 50x25; after the /Matrix scale the
        // transformed box is exactly 100x50, which already matches Rect — so step (b)'s A should
        // come out as the identity matrix and AA should equal Matrix unchanged.
        double[] bbox = [0, 0, 50, 25];
        double[] matrix = [2, 0, 0, 2, 0, 0];
        double[] rect = [0, 0, 100, 50];

        double[]? aa = AppearancePlacement.ComputeAA(bbox, matrix, rect);

        Assert.NotNull(aa);
        Assert.Equal(new[] { 2.0, 0, 0, 2, 0, 0 }, aa!);
        AssertBBoxTransformsOntoRect(bbox, aa!, rect);
    }

    [Fact]
    public void BBoxOriginNotAtZero_TranslatesToRectOrigin()
    {
        // BBox origin is (10,20), not (0,0). Matrix is identity, so the translation in A must
        // absorb BBox's own offset in addition to placing it at Rect's origin.
        double[] bbox = [10, 20, 110, 70]; // 100x50, origin (10,20)
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [0, 0, 100, 50];

        double[]? aa = AppearancePlacement.ComputeAA(bbox, matrix, rect);

        Assert.NotNull(aa);
        Assert.Equal(new[] { 1.0, 0, 0, 1, -10, -20 }, aa!);
        AssertBBoxTransformsOntoRect(bbox, aa!, rect);
    }

    [Fact]
    public void BBoxSizeDiffersFromRectSize_ScalesNonUniformly()
    {
        // BBox is a 100x100 square; Rect is 50x25 — A must scale x and y by different factors
        // (0.5 and 0.25) to make the box fit Rect's edges.
        double[] bbox = [0, 0, 100, 100];
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [0, 0, 50, 25];

        double[]? aa = AppearancePlacement.ComputeAA(bbox, matrix, rect);

        Assert.NotNull(aa);
        Assert.Equal(new[] { 0.5, 0, 0, 0.25, 0, 0 }, aa!);
        AssertBBoxTransformsOntoRect(bbox, aa!, rect);
    }

    [Fact]
    public void NonNormalisedRect_UpperRightBeforeLowerLeft_IsHandledTheSameAsNormalised()
    {
        // /Rect is not guaranteed normalised: [x2 y2 x1 y1] (upper-right corner listed first)
        // must produce the same AA as the normalised [x1 y1 x2 y2] form.
        double[] bbox = [0, 0, 792, 612];
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] normalisedRect = [0, 0, 792, 612];
        double[] flippedRect = [792, 612, 0, 0];

        double[]? aaNormalised = AppearancePlacement.ComputeAA(bbox, matrix, normalisedRect);
        double[]? aaFlipped = AppearancePlacement.ComputeAA(bbox, matrix, flippedRect);

        Assert.NotNull(aaNormalised);
        Assert.NotNull(aaFlipped);
        Assert.Equal(aaNormalised!, aaFlipped!);
    }

    [Fact]
    public void DegenerateZeroWidthBBox_ReturnsNull()
    {
        double[] bbox = [10, 10, 10, 50]; // zero width
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [0, 0, 100, 50];

        Assert.Null(AppearancePlacement.ComputeAA(bbox, matrix, rect));
    }

    [Fact]
    public void DegenerateZeroHeightBBox_ReturnsNull()
    {
        double[] bbox = [10, 10, 60, 10]; // zero height
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [0, 0, 100, 50];

        Assert.Null(AppearancePlacement.ComputeAA(bbox, matrix, rect));
    }

    [Fact]
    public void DegenerateMatrixCollapsesBBoxToALine_ReturnsNull()
    {
        // A well-formed, non-degenerate BBox can still collapse after /Matrix is applied — e.g. a
        // /Matrix with a zero x-scale term flattens every point onto the y-axis.
        double[] bbox = [0, 0, 100, 50];
        double[] matrix = [0, 0, 0, 1, 0, 0]; // a=0 collapses the transformed box's width to 0
        double[] rect = [0, 0, 100, 50];

        Assert.Null(AppearancePlacement.ComputeAA(bbox, matrix, rect));
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Applies <paramref name="aa"/> to all four corners of <paramref name="bbox"/> and asserts the
    /// resulting quadrilateral's upright bounding box equals <paramref name="rect"/> (normalised).
    /// This is the property AA is defined to guarantee, checked independently of ComputeAA's own
    /// internal arithmetic — it only relies on the PDF point-transform formula.
    /// </summary>
    private static void AssertBBoxTransformsOntoRect(double[] bbox, double[] aa, double[] rect)
    {
        (double x, double y) P(double x, double y) =>
            (aa[0] * x + aa[2] * y + aa[4], aa[1] * x + aa[3] * y + aa[5]);

        (double x, double y) p0 = P(bbox[0], bbox[1]);
        (double x, double y) p1 = P(bbox[2], bbox[1]);
        (double x, double y) p2 = P(bbox[2], bbox[3]);
        (double x, double y) p3 = P(bbox[0], bbox[3]);

        double minX = Math.Min(Math.Min(p0.x, p1.x), Math.Min(p2.x, p3.x));
        double maxX = Math.Max(Math.Max(p0.x, p1.x), Math.Max(p2.x, p3.x));
        double minY = Math.Min(Math.Min(p0.y, p1.y), Math.Min(p2.y, p3.y));
        double maxY = Math.Max(Math.Max(p0.y, p1.y), Math.Max(p2.y, p3.y));

        double rx0 = Math.Min(rect[0], rect[2]);
        double rx1 = Math.Max(rect[0], rect[2]);
        double ry0 = Math.Min(rect[1], rect[3]);
        double ry1 = Math.Max(rect[1], rect[3]);

        Assert.Equal(rx0, minX, 6);
        Assert.Equal(rx1, maxX, 6);
        Assert.Equal(ry0, minY, 6);
        Assert.Equal(ry1, maxY, 6);
    }
}
