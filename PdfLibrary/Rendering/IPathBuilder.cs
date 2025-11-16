namespace PdfLibrary.Rendering;

/// <summary>
/// Interface for building vector paths from PDF path construction operators
/// Platform implementations will convert this to their native path format
/// </summary>
public interface IPathBuilder
{
    /// <summary>
    /// Begin a new subpath at the specified coordinates (m operator)
    /// </summary>
    void MoveTo(double x, double y);

    /// <summary>
    /// Append a straight line segment to the current point (l operator)
    /// </summary>
    void LineTo(double x, double y);

    /// <summary>
    /// Append a cubic Bézier curve (c operator)
    /// </summary>
    void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3);

    /// <summary>
    /// Append a rectangle (re operator)
    /// </summary>
    void Rectangle(double x, double y, double width, double height);

    /// <summary>
    /// Close the current subpath (h operator)
    /// </summary>
    void ClosePath();

    /// <summary>
    /// Clear the path (prepare for new path)
    /// </summary>
    void Clear();

    /// <summary>
    /// Check if the path is empty
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Clone the current path
    /// </summary>
    IPathBuilder Clone();
}
