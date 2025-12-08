namespace PdfLibrary.Builder.Annotation;

/// <summary>
/// Four points defining a quadrilateral region (used for text markup annotations)
/// Points are specified in order: bottom-left, bottom-right, top-left, top-right
/// </summary>
public readonly struct PdfQuadPoints
{
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }
    public double X3 { get; }
    public double Y3 { get; }
    public double X4 { get; }
    public double Y4 { get; }

    public PdfQuadPoints(double x1, double y1, double x2, double y2,
        double x3, double y3, double x4, double y4)
    {
        X1 = x1; Y1 = y1;
        X2 = x2; Y2 = y2;
        X3 = x3; Y3 = y3;
        X4 = x4; Y4 = y4;
    }

    /// <summary>
    /// Create quad points from a rectangle
    /// </summary>
    public static PdfQuadPoints FromRect(PdfRect rect)
    {
        return new PdfQuadPoints(
            rect.Left, rect.Bottom,   // bottom-left
            rect.Right, rect.Bottom,  // bottom-right
            rect.Left, rect.Top,      // top-left
            rect.Right, rect.Top      // top-right
        );
    }
}