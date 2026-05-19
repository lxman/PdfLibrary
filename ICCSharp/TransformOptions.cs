using ICCSharp.Eval;
using ICCSharp.Profile;

namespace ICCSharp;

/// <summary>
/// Options controlling an <see cref="IccTransform"/>. Defaults are chosen to match lcms2's
/// out-of-the-box behavior so cross-CMM diffs stay small.
/// </summary>
public sealed class TransformOptions
{
    /// <summary>Rendering intent to honor when selecting source/destination LUT tags. Defaults to relative colorimetric.</summary>
    public RenderingIntent Intent { get; set; } = RenderingIntent.RelativeColorimetric;

    /// <summary>
    /// Whether to apply Adobe-style Black Point Compensation between the two pipelines. Defaults
    /// to <c>false</c> (the spec-default; lcms2's default is also off unless `cmsFLAGS_BLACKPOINTCOMPENSATION`
    /// is set).
    /// </summary>
    public bool BlackPointCompensation { get; set; } = false;

    /// <summary>
    /// Chromatic adaptation method. Reserved for future use — current implementation does not yet
    /// apply CAT between source and destination PCS (both are assumed to be D50, the ICC norm).
    /// </summary>
    public CatMethod ChromaticAdaptation { get; set; } = CatMethod.Bradford;
}
