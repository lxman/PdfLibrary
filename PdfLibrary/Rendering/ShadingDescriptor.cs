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
    /// Pattern matrix (pattern space → the page's default user space) for a shading PATTERN.
    /// Null for the <c>sh</c> operator, whose coordinates are already in the current user space.
    /// </summary>
    public Matrix3x2? PatternMatrix { get; init; }
}
