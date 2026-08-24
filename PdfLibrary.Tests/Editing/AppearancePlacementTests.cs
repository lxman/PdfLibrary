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
    [Fact]
    public void NearDegenerateTransformedBox_ReturnsNull_RatherThanABlownUpScale()
    {
        // Review finding (Task 1 gate): the degeneracy test was an exact `== 0`, so a transformed
        // box that is merely NEAR zero — a /Matrix with a tiny but non-zero scale term — skipped the
        // refusal and divided by it instead, producing an enormous scale factor rather than an
        // honest "cannot be placed". 1e-12 is below MinExtent (1e-9) and above nothing: it is a real
        // double, and `1e-12 == 0` is false.
        double[] bbox = [0, 0, 100, 50];
        double[] matrix = [1e-14, 0, 0, 1, 0, 0]; // collapses width to 100 * 1e-14 = 1e-12
        double[] rect = [0, 0, 50, 100];

        Assert.Null(AppearancePlacement.ComputeAA(bbox, matrix, rect));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteMatrixTerm_ReturnsNull_NotAMatrixOfNaNs(double poison)
    {
        // An == 0 test answers false for NaN, so a malformed /Matrix used to flow straight through
        // into the division and return six NaNs dressed up as a placement. Infinities subtract to
        // NaN and take the same path. Both mean the same thing to a caller: unplaceable.
        double[] bbox = [0, 0, 100, 50];
        double[] matrix = [poison, 0, 0, 1, 0, 0];
        double[] rect = [0, 0, 50, 100];

        Assert.Null(AppearancePlacement.ComputeAA(bbox, matrix, rect));
    }

    [Fact]
    public void NonNormalisedBBox_UpperRightBeforeLowerLeft_IsHandledTheSameAsNormalised()
    {
        // The review confirmed by hand that reversed-BBox input is already handled correctly (all
        // four corners are derived, then min/max taken), but nothing tested it — and an untested
        // correct path is one edit away from being an untested wrong one. The corpus cannot see
        // this: every production BBox is [0 0 792 612].
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [10, 20, 110, 70];

        double[]? normalised = AppearancePlacement.ComputeAA([0, 0, 100, 50], matrix, rect);
        double[]? reversed = AppearancePlacement.ComputeAA([100, 50, 0, 0], matrix, rect);

        Assert.NotNull(normalised);
        Assert.NotNull(reversed);
        Assert.Equal(normalised!, reversed!);
    }

    [Fact]
    public void NullArgument_ThrowsArgumentNullException_NotNullReference()
    {
        double[] ok4 = [0, 0, 100, 50];
        double[] ok6 = [1, 0, 0, 1, 0, 0];

        Assert.Throws<ArgumentNullException>(() => AppearancePlacement.ComputeAA(null!, ok6, ok4));
        Assert.Throws<ArgumentNullException>(() => AppearancePlacement.ComputeAA(ok4, null!, ok4));
        Assert.Throws<ArgumentNullException>(() => AppearancePlacement.ComputeAA(ok4, ok6, null!));
    }

    [Theory]
    // zero-width /Rect, zero-height /Rect, and both — each with a perfectly healthy BBox/Matrix,
    // so the source-box guard above cannot catch them.
    [InlineData(50.0, 0.0, 50.0, 100.0)]
    [InlineData(0.0, 50.0, 100.0, 50.0)]
    [InlineData(10.0, 10.0, 10.0, 10.0)]
    public void DegenerateRect_ReturnsNull_RatherThanAZeroScalePlacement(
        double x0, double y0, double x1, double y1)
    {
        // Task 3's review gate, empirically demonstrated: a zero-extent /Rect does NOT divide by
        // zero — it zeroes the NUMERATOR, so sx comes out a finite 0 and every guard above passes.
        // The old code returned [0,0,0,1,50,0]: a valid-looking matrix that collapses the appearance
        // to a line. Its caller then wrote that degenerate CTM into the page and deleted the
        // annotation, reporting success.
        double[] bbox = [0, 0, 100, 100];
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [x0, y0, x1, y1];

        Assert.Null(AppearancePlacement.ComputeAA(bbox, matrix, rect));
    }

    [Fact]
    public void ARectJustAboveMinExtent_IsStillPlaced()
    {
        // The other side of the guard: it must refuse degeneracy without rejecting a legitimately
        // tiny annotation. 1e-6pt is absurd but real; MinExtent is 1e-9.
        double[] bbox = [0, 0, 100, 100];
        double[] matrix = [1, 0, 0, 1, 0, 0];
        double[] rect = [0, 0, 1e-6, 1e-6];

        double[]? aa = AppearancePlacement.ComputeAA(bbox, matrix, rect);

        Assert.NotNull(aa);
        Assert.True(aa![0] > 0, "x-scale must be positive, not collapsed");
        Assert.True(aa[3] > 0, "y-scale must be positive, not collapsed");
    }

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
