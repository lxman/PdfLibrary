using System.Numerics;

namespace PdfLibrary.Rendering;

/// <summary>
/// Backend-agnostic description of a PDF axial (type 2) or radial (type 3) shading, with its
/// colour ramp pre-sampled into stops. Produced by <see cref="ShadingBuilder"/> and consumed by
/// <see cref="IRenderTarget.PaintShading"/> (the <c>sh</c> operator) and
/// <see cref="IRenderTarget.FillPathWithShadingPattern"/> (a PatternType 2 shading pattern).
/// </summary>
public sealed class ShadingDescriptor
{
    /// <summary>2 = axial (linear), 3 = radial.</summary>
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

    /// <summary>
    /// Pattern matrix (pattern space → the page's default user space) for a shading PATTERN.
    /// Null for the <c>sh</c> operator, whose coordinates are already in the current user space.
    /// </summary>
    public Matrix3x2? PatternMatrix { get; init; }
}
