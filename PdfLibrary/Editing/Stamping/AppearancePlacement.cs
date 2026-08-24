namespace PdfLibrary.Editing.Stamping;

/// <summary>
/// ISO 32000-1 §12.5.5, "Algorithm: Appearance streams" — computes the matrix AA that maps an
/// appearance stream's own coordinate system into an annotation's /Rect in default user space.
/// This is distinct from (and more general than) <c>FormFlattener</c>'s widget-placement one-liner,
/// which is a pure translation to /Rect's lower-left corner and assumes identity /Matrix, a /BBox
/// whose origin is (0,0), and a /BBox whose size equals /Rect's. Those assumptions do not hold for
/// annotations in general, so this type implements the full algorithm instead of reusing that one.
/// </summary>
internal static class AppearancePlacement
{
    /// <summary>
    /// Smallest transformed-box extent, in default user space points, that still yields a meaningful
    /// scale factor. The transformed box is in user space (Matrix maps the appearance's coordinate
    /// system into it), so an absolute tolerance in points is the right kind: a box narrower than a
    /// nanopoint is not a box. Sized to sit well above float noise — subtracting two nearly-equal
    /// coordinates at PDF's practical upper bound (~32767) loses about 3.6e-12 — while rejecting
    /// nothing a real document could intend.
    /// </summary>
    private const double MinExtent = 1e-9;

    /// <summary>
    /// Computes AA = Matrix × A per §12.5.5:
    /// <list type="number">
    /// <item>a) The appearance's bounding box (BBox) is transformed by Matrix to produce a
    /// quadrilateral of arbitrary orientation. The transformed appearance box is the smallest
    /// upright rectangle that encompasses that quadrilateral.</item>
    /// <item>b) A matrix A scales and translates the transformed appearance box to align with the
    /// edges of the annotation's rectangle (Rect): A maps the transformed box's lower-left corner
    /// (smallest x, smallest y) to Rect's lower-left corner, and its upper-right corner (greatest x,
    /// greatest y) to Rect's upper-right corner.</item>
    /// <item>c) Matrix is concatenated with A to form AA, which maps from the appearance's own
    /// coordinate system to the annotation's rectangle in default user space.</item>
    /// </list>
    /// </summary>
    /// <param name="bbox">
    /// The appearance's /BBox as four elements [x0 y0 x1 y1], read directly from the PDF. Not
    /// required to be normalised — the corner with the smallest/greatest coordinate need not be
    /// first; all four corners of the box are derived from the two x-values and two y-values given.
    /// </param>
    /// <param name="matrix">
    /// The appearance's /Matrix as six elements [a b c d e f]. Pass [1 0 0 1 0 0] when /Matrix is
    /// absent (its default per the spec is the identity matrix).
    /// </param>
    /// <param name="rect">
    /// The annotation's /Rect as four elements [x0 y0 x1 y1], read directly from the PDF. Also not
    /// required to be normalised: a /Rect given as [x2 y2 x1 y1] (upper-right before lower-left) is
    /// handled the same as [x1 y1 x2 y2].
    /// </param>
    /// <returns>
    /// The six-element [a b c d e f] matrix AA, or <see langword="null"/> when the transformed
    /// appearance box is degenerate — narrower or shorter than <see cref="MinExtent"/> in either
    /// dimension, or not a finite number at all. In those cases the scale factor for that dimension
    /// is undefined or meaningless: dividing by an exactly-zero extent is undefined, dividing by a
    /// near-zero one returns an absurdly blown-up matrix, and a NaN extent (which every ordered
    /// comparison answers false for, so it cannot be screened by a zero test) poisons every term.
    /// Degenerate input most often means a /BBox with a zero-length side, or a /Matrix that
    /// collapses the box onto a line or a point (e.g. a zero or near-zero scale term). Callers must treat a
    /// <see langword="null"/> result as "this appearance cannot be placed" and must not substitute a
    /// fallback matrix — silently returning a garbage placement would bake a corrupted appearance
    /// onto the page instead of refusing the repair.
    /// </returns>
    internal static double[]? ComputeAA(double[] bbox, double[] matrix, double[] rect)
    {
        ArgumentNullException.ThrowIfNull(bbox);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(rect);
        if (bbox.Length < 4) throw new ArgumentException("bbox must have at least 4 elements.", nameof(bbox));
        if (matrix.Length < 6) throw new ArgumentException("matrix must have at least 6 elements.", nameof(matrix));
        if (rect.Length < 4) throw new ArgumentException("rect must have at least 4 elements.", nameof(rect));

        // a) Transform BBox's four corners by Matrix; find the smallest upright rectangle
        // (the "transformed appearance box") that encompasses the resulting quadrilateral.
        (double x, double y) c0 = TransformPoint(bbox[0], bbox[1], matrix);
        (double x, double y) c1 = TransformPoint(bbox[2], bbox[1], matrix);
        (double x, double y) c2 = TransformPoint(bbox[2], bbox[3], matrix);
        (double x, double y) c3 = TransformPoint(bbox[0], bbox[3], matrix);

        double tx0 = Min4(c0.x, c1.x, c2.x, c3.x);
        double tx1 = Max4(c0.x, c1.x, c2.x, c3.x);
        double ty0 = Min4(c0.y, c1.y, c2.y, c3.y);
        double ty1 = Max4(c0.y, c1.y, c2.y, c3.y);

        double transformedWidth = tx1 - tx0;
        double transformedHeight = ty1 - ty0;

        // Written as !(w > MinExtent) rather than (w <= MinExtent) deliberately: the negated form
        // also catches NaN, which every ordered comparison answers false for. A /Matrix carrying a
        // NaN (or an infinity, which subtracts to NaN) would otherwise pass an == 0 test, divide
        // into a NaN scale factor, and return a matrix of NaNs as if it were a real placement.
        // Both the near-zero and the not-a-number case mean the same thing to a caller — this
        // appearance cannot be placed — so both take the refusal branch.
        if (!(transformedWidth > MinExtent) || !(transformedHeight > MinExtent))
            return null;

        // b) Compute A: scale + translate the transformed appearance box onto Rect (Rect is not
        // assumed normalised — take its own min/max independently in each axis).
        double rx0 = Math.Min(rect[0], rect[2]);
        double rx1 = Math.Max(rect[0], rect[2]);
        double ry0 = Math.Min(rect[1], rect[3]);
        double ry1 = Math.Max(rect[1], rect[3]);

        double sx = (rx1 - rx0) / transformedWidth;
        double sy = (ry1 - ry0) / transformedHeight;
        double ex = rx0 - sx * tx0;
        double ey = ry0 - sy * ty0;
        double[] a = [sx, 0, 0, sy, ex, ey];

        // c) AA = Matrix × A (apply Matrix, then A).
        return Concat(matrix, a);
    }

    private static (double x, double y) TransformPoint(double x, double y, double[] m) =>
        (m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5]);

    private static double Min4(double a, double b, double c, double d) =>
        Math.Min(Math.Min(a, b), Math.Min(c, d));

    private static double Max4(double a, double b, double c, double d) =>
        Math.Max(Math.Max(a, b), Math.Max(c, d));

    /// <summary>
    /// Concatenates two PDF matrices as "apply m1, then m2" (PDF 32000-1 §8.3.4): the returned
    /// matrix R satisfies point×R == (point×m1)×m2 for every point, using the PDF row-vector
    /// convention x' = a·x + c·y + e, y' = b·x + d·y + f.
    /// </summary>
    private static double[] Concat(double[] m1, double[] m2) =>
    [
        m1[0] * m2[0] + m1[1] * m2[2],
        m1[0] * m2[1] + m1[1] * m2[3],
        m1[2] * m2[0] + m1[3] * m2[2],
        m1[2] * m2[1] + m1[3] * m2[3],
        m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
        m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
    ];
}
