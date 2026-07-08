using System.Numerics;

namespace PdfLibrary.Rendering;

/// <summary>
/// A single tessellated mesh-shading vertex: a position in the shading's target coordinate space
/// (the same space as axial/radial <see cref="ShadingDescriptor.Coords"/>) paired with its colour in
/// both sRGB (<see cref="Rgb"/>, 0xAARRGGBB) and native DeviceCMYK (<see cref="Cmyk"/>, 0xCCMMYYKK).
/// Consumers apply the same shading→device transform they use for axial/radial coordinates.
/// </summary>
public readonly record struct MeshVertex(float X, float Y, uint Rgb, uint Cmyk);

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
}
