using System.Numerics;

namespace PdfLibrary.Builder.Page;

/// <summary>
/// A path-drawing content element (the data behind <see cref="PdfPathBuilder"/>):
/// a sequence of segments plus fill/stroke and graphics-state settings.
/// </summary>
public class PdfPathContent : PdfContentElement
{
    /// <summary>The ordered path segments (move / line / curve / close).</summary>
    public List<PdfPathSegment> Segments { get; } = [];
    /// <summary>Fill colour, or null to leave the path unfilled.</summary>
    public PdfColor? FillColor { get; set; }
    /// <summary>Stroke colour, or null to leave the path unstroked.</summary>
    public PdfColor? StrokeColor { get; set; }
    /// <summary>Stroke width in points.</summary>
    public double LineWidth { get; set; } = 1;
    /// <summary>Fill rule used for self-intersecting paths.</summary>
    public PdfFillRule FillRule { get; set; } = PdfFillRule.NonZeroWinding;
    /// <summary>Cap style for open stroke ends.</summary>
    public PdfLineCap LineCap { get; set; } = PdfLineCap.Butt;
    /// <summary>Join style between stroke segments.</summary>
    public PdfLineJoin LineJoin { get; set; } = PdfLineJoin.Miter;
    /// <summary>Miter limit for miter line joins.</summary>
    public double MiterLimit { get; set; } = 10;
    /// <summary>Dash array in points, or null for a solid line.</summary>
    public double[]? DashPattern { get; set; }
    /// <summary>Dash phase (offset into the dash pattern), in points.</summary>
    public double DashPhase { get; set; }
    /// <summary>If true, the path defines a clipping region instead of being painted.</summary>
    public bool IsClippingPath { get; set; }
    /// <summary>Fill opacity (0 = transparent, 1 = opaque).</summary>
    public double FillOpacity { get; set; } = 1.0;
    /// <summary>Stroke opacity (0 = transparent, 1 = opaque).</summary>
    public double StrokeOpacity { get; set; } = 1.0;

    /// <summary>Optional affine transform applied to the path.</summary>
    public Matrix3x2? Transform { get; set; }
    /// <summary>Enables overprint for fills (PDF op).</summary>
    public bool FillOverprint { get; set; }
    /// <summary>Enables overprint for strokes (PDF OP).</summary>
    public bool StrokeOverprint { get; set; }
    /// <summary>Overprint mode (PDF OPM): 0 or 1.</summary>
    public int OverprintMode { get; set; }
    /// <summary>Blend mode name (PDF BM), e.g. "Multiply"; null means Normal.</summary>
    public string? BlendMode { get; set; }
}
