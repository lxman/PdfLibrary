using System.Numerics;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Represents a PDF Tiling Pattern (Type 1 pattern)
/// ISO 32000-1:2008 section 8.7.4
/// </summary>
public class PdfTilingPattern
{
    /// <summary>
    /// Pattern type: 1 = tiling pattern
    /// </summary>
    public int PatternType { get; init; } = 1;

    /// <summary>
    /// Paint type:
    /// 1 = Colored tiling pattern (pattern specifies its own colors)
    /// 2 = Uncolored tiling pattern (uses current color space/color)
    /// </summary>
    public int PaintType { get; init; } = 1;

    /// <summary>
    /// Tiling type:
    /// 1 = Constant spacing
    /// 2 = No distortion (spacing may vary)
    /// 3 = Constant spacing and faster tiling
    /// </summary>
    public int TilingType { get; init; } = 1;

    /// <summary>
    /// Bounding box of the pattern cell in pattern space
    /// </summary>
    public PdfRectangle BBox { get; init; } = new(0, 0, 1, 1);

    /// <summary>
    /// Horizontal spacing between pattern cells
    /// </summary>
    public double XStep { get; init; } = 1;

    /// <summary>
    /// Vertical spacing between pattern cells
    /// </summary>
    public double YStep { get; init; } = 1;

    /// <summary>
    /// Pattern matrix - transforms pattern space to user space
    /// </summary>
    public Matrix3x2 Matrix { get; init; } = Matrix3x2.Identity;

    /// <summary>
    /// The pattern's content stream (internal use only)
    /// </summary>
    internal PdfStream? ContentStream { get; init; }

    /// <summary>
    /// Resources dictionary for the pattern (internal use only)
    /// </summary>
    internal PdfResources? Resources { get; init; }

    /// <summary>
    /// Creates a PdfTilingPattern from a pattern dictionary
    /// </summary>
    /// <param name="dict">The pattern dictionary</param>
    /// <param name="document">The PDF document for resolving references</param>
    /// <param name="contentStream">Optional content stream for the pattern</param>
    internal static PdfTilingPattern? FromDictionary(PdfDictionary dict, PdfDocument document, PdfStream? contentStream = null)
    {
        // Check pattern type
        if (!dict.TryGetValue(new PdfName("PatternType"), out var ptObj) || ptObj is not PdfInteger pt || pt.Value != 1)
            return null; // Not a tiling pattern

        int paintType = 1;
        int tilingType = 1;
        PdfRectangle bbox = new(0, 0, 1, 1);
        double xStep = 1;
        double yStep = 1;
        Matrix3x2 matrix = Matrix3x2.Identity;
        PdfResources? resources = null;

        // Paint type (required)
        if (dict.TryGetValue(new PdfName("PaintType"), out var paintObj) && paintObj is PdfInteger paintInt)
            paintType = paintInt.Value;

        // Tiling type (required)
        if (dict.TryGetValue(new PdfName("TilingType"), out var tilingObj) && tilingObj is PdfInteger tilingInt)
            tilingType = tilingInt.Value;

        // BBox (required)
        if (dict.TryGetValue(new PdfName("BBox"), out var bboxObj) && bboxObj is PdfArray bboxArray && bboxArray.Count >= 4)
        {
            bbox = new PdfRectangle(
                bboxArray[0].ToDouble(),
                bboxArray[1].ToDouble(),
                bboxArray[2].ToDouble(),
                bboxArray[3].ToDouble());
        }

        // XStep (required)
        if (dict.TryGetValue(new PdfName("XStep"), out var xstepObj))
            xStep = xstepObj.ToDouble();

        // YStep (required)
        if (dict.TryGetValue(new PdfName("YStep"), out var ystepObj))
            yStep = ystepObj.ToDouble();

        // Matrix (optional, defaults to identity)
        if (dict.TryGetValue(new PdfName("Matrix"), out var matrixObj) && matrixObj is PdfArray m && m.Count >= 6)
        {
            matrix = new Matrix3x2(
                (float)m[0].ToDouble(),
                (float)m[1].ToDouble(),
                (float)m[2].ToDouble(),
                (float)m[3].ToDouble(),
                (float)m[4].ToDouble(),
                (float)m[5].ToDouble());
        }

        // Resources (optional)
        if (dict.TryGetValue(new PdfName("Resources"), out var resObj))
        {
            var resolvedRes = resObj;
            if (resObj is PdfIndirectReference resRef)
                resolvedRes = document.ResolveReference(resRef);
            if (resolvedRes is PdfDictionary resDict)
                resources = new PdfResources(resDict, document);
        }

        return new PdfTilingPattern
        {
            PatternType = 1,
            PaintType = paintType,
            TilingType = tilingType,
            BBox = bbox,
            XStep = xStep,
            YStep = yStep,
            Matrix = matrix,
            Resources = resources,
            ContentStream = contentStream
        };
    }
}
