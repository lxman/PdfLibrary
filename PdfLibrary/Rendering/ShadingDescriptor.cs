using System.Numerics;

namespace PdfLibrary.Rendering;

/// <summary>
/// A single tessellated mesh-shading vertex: a position in the shading's target coordinate space
/// (the same space as axial/radial <see cref="ShadingDescriptor.Coords"/>) paired with its colour in
/// both sRGB (<see cref="Rgb"/>, 0xAARRGGBB) and native DeviceCMYK (<see cref="Cmyk"/>, 0xCCMMYYKK).
/// Consumers apply the same shading→device transform they use for axial/radial coordinates.
/// </summary>
public readonly record struct MeshVertex(float X, float Y, uint Rgb, uint Cmyk);

/// <summary>Per-spot shading ink (Soft-Proof SP-7, axial/radial). Parallel to the flattened
/// <see cref="ShadingDescriptor.CmykColors"/>: per SPOT colorant a per-stop tint, plus a per-stop
/// process-only CMYK (process colorants at their tint; zero for a pure-spot shading). Null when the
/// shading carries no spot colorant. (Mesh per-vertex spot data is a separate follow-on.)</summary>
/// <param name="Names">Spot colorant names, in tint index order.</param>
/// <param name="StopTints">Per-stop, per-spot tint bytes (0..255). Stop-major, spot-minor: length is
/// <c>Stops.Length * Names.Count</c>, and index <c>stop * Names.Count + s</c> holds spot <c>s</c>'s
/// (0-based, <see cref="Names"/> order) tint at that stop — NOT spot-major.</param>
/// <param name="StopProcessCmyk">Per-stop process-only packed CMYK, length <c>Stops.Length</c>; each
/// entry is <c>0xCCMMYYKK</c> (process colorants — Cyan/Magenta/Yellow/Black — at their tint; zero
/// when the shading has no process colorant).</param>
public sealed record ShadingSpotInk(
    IReadOnlyList<string> Names,
    byte[] StopTints,
    uint[] StopProcessCmyk);

/// <summary>
/// Backend-agnostic description of a PDF shading. Axial (type 2) and radial (type 3) carry a
/// pre-sampled colour ramp (<see cref="Coords"/> + <see cref="Stops"/> + <see cref="Colors"/>);
/// mesh shadings (Coons type 6, tensor-product type 7) instead carry a pre-tessellated triangle
/// list (<see cref="MeshTriangles"/>). Produced by <see cref="ShadingBuilder"/> and consumed by
/// <see cref="IRenderTarget.PaintShading"/> (the <c>sh</c> operator) and
/// <see cref="IRenderTarget.FillPathWithShadingPattern"/> (a PatternType 2 shading pattern).
/// </summary>
public sealed class ShadingDescriptor
{
    /// <summary>2 = axial (linear), 3 = radial, 6 = Coons patch mesh, 7 = tensor-product patch mesh.</summary>
    public int ShadingType { get; init; }

    /// <summary>Axial: [x0, y0, x1, y1]. Radial: [x0, y0, r0, x1, y1, r1]. In shading space.</summary>
    public float[] Coords { get; init; } = [];

    /// <summary>Extend the shading beyond the start / end of its axis (/Extend).</summary>
    public bool ExtendStart { get; init; }
    /// <summary>Extend the shading beyond the end of its axis (/Extend[1]).</summary>
    public bool ExtendEnd { get; init; }

    /// <summary>Ascending stop positions in [0, 1], paired with <see cref="Colors"/>.</summary>
    public float[] Stops { get; init; } = [];

    /// <summary>Stop colours as packed 0xAARRGGBB, paired with <see cref="Stops"/>.</summary>
    public uint[] Colors { get; init; } = [];

    /// <summary>Native DeviceCMYK stop colours (packed 0xCCMMYYKK), paired with <see cref="Stops"/>, for a
    /// shading whose /ColorSpace resolves to CMYK (DeviceCMYK, ICCBased-4, or Separation/DeviceN with a
    /// DeviceCMYK alternate). Empty when the colour space doesn't resolve to CMYK — a CMYK compositor then
    /// falls back to converting <see cref="Colors"/> (sRGB) instead. Lets the compositor paint the gradient
    /// in native ink and honour <see cref="OverprintPlates"/>.</summary>
    public uint[] CmykColors { get; init; } = [];

    /// <summary>Per-plate CMYK overprint mask derived from the shading's Separation/DeviceN colorants
    /// (Cyan→C, Magenta→M, Yellow→Y, Black→K; null for device spaces or any spot colorant). When the shading
    /// overprints, a CMYK compositor paints only these plates and preserves the rest (ISO 32000 §8.6.6.3) —
    /// so a DeviceN[C,M] gradient over a yellow object keeps the backdrop's Y plate (GWG010 e/j).</summary>
    public (bool C, bool M, bool Y, bool K)? OverprintPlates { get; init; }

    /// <summary>Named Separation/DeviceN colorant identity of the shading's colour space (Soft-Proof
    /// SP-1), or null for device colour spaces. Additive; does not affect existing shading rendering.</summary>
    public ColorantOrigin? ColorantOrigin { get; init; }

    /// <summary>Per-spot ink for a spot axial/radial shading (Soft-Proof SP-7); null ⇒ no spot colorant
    /// (renders exactly as before via <see cref="CmykColors"/>).</summary>
    public ShadingSpotInk? SpotInk { get; init; }

    /// <summary>
    /// Tessellated triangle list for a mesh shading (type 6/7): a triangle soup where each consecutive
    /// triple of vertices forms one Gouraud-shaded triangle, positions in the shading's target coordinate
    /// space. Empty for axial/radial shadings. Each patch's bicubic surface is sampled on a grid and its
    /// four corner colours bilinearly interpolated (ISO 32000 §8.7.4.5.7–8).
    /// </summary>
    public MeshVertex[] MeshTriangles { get; init; } = [];

    /// <summary>True when a mesh shading's colour space resolves to DeviceCMYK, so <see cref="MeshVertex.Cmyk"/>
    /// carries native ink and a CMYK compositor should paint it directly instead of converting from
    /// <see cref="MeshVertex.Rgb"/>. False for RGB/Gray/Lab meshes.</summary>
    public bool MeshHasCmyk { get; init; }

    /// <summary>
    /// Pattern matrix (pattern space → the page's default user space) for a shading PATTERN.
    /// Null for the <c>sh</c> operator, whose coordinates are already in the current user space.
    /// </summary>
    public Matrix3x2? PatternMatrix { get; init; }

    /// <summary>Continuous source→sRGB sampler over the raw /Function: s∈[0,1] → 0xAARRGGBB, evaluated
    /// at any position (not a pre-sampled ramp). Non-null for axial/radial (types 2/3) whose colour
    /// space does NOT resolve to CMYK — a CMYK compositor samples this per-pixel and colour-manages the
    /// result, instead of flattening the gradient to an average. Null for mesh, for CMYK-resolving
    /// spaces (which use <see cref="CmykColors"/>), or when the function can't be evaluated. Pure /
    /// thread-safe: it captures only immutable post-build state.</summary>
    public Func<float, uint>? SampleRgbAt { get; init; }
}
