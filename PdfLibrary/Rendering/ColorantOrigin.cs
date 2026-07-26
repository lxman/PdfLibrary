namespace PdfLibrary.Rendering;

/// <summary>
/// The named-colorant identity of a resolved Separation/DeviceN paint, preserved alongside the
/// flattened device colour on <see cref="PdfLibrary.Content.PdfGraphicsState"/> and
/// <see cref="ShadingDescriptor"/>. Null for device (DeviceGray/RGB/CMYK) and Pattern colours.
/// Soft-Proof SP-1: the data SP-2's N-channel compositor uses to route paint to spot plates.
///
/// <para><b>The two members below are additive by design.</b> This record is public and crosses the
/// package boundary: <c>Pellucid.Rendering.Cmyk</c> reads it in production and seven Pellucid test
/// files construct it positionally. Keeping the three-element constructor intact means Pass 2a needs no
/// lockstep engine-pack-and-repin. They are folded into the positional shape by a later contract pass,
/// once <see cref="Components"/> has fully replaced <see cref="Names"/> and <see cref="Tints"/>.</para>
/// </summary>
public sealed record ColorantOrigin(
    IReadOnlyList<string> Names,   // Separation → 1 name; DeviceN → N names, in colorant order
    IReadOnlyList<double> Tints,   // the raw tint inputs supplied to the colour operator
    string AlternateSpace)         // the tint transform's alternate space name (e.g. "DeviceCMYK", "Lab")
{
    /// <summary><c>/Attributes /Subtype</c> — "DeviceN" or "NChannel", defaulting to "DeviceN" per
    /// ISO 32000-2 Table 70. Null only when this origin was built by a caller that predates Pass 2a.
    /// A value other than "NChannel" means per-component evaluation does not apply.</summary>
    public string? Subtype { get; init; }

    /// <summary>One entry per colourant name, in order, when this is an NChannel space whose components
    /// can be evaluated individually. <b>Null</b> for every other space — a Separation, a plain DeviceN,
    /// an unrecognised subtype, or an NChannel whose process colour space this engine cannot reduce to
    /// CMYK. A null here is the signal to fall back to whole-space behaviour, which is exactly what
    /// ISO 32000-2 §8.6.6.5 requires of a non-NChannel DeviceN.
    ///
    /// <para><b>A non-null value does not by itself mean per-component routing is available.</b>
    /// Shadings and meshes resolve their origin with no per-op colour (<c>Tints</c> empty), so every
    /// component in the list gets a null <c>Tint</c> and therefore a null <c>OwnAlternateCmyk</c> — a
    /// fully populated, role-classified list whose entries carry nothing a compositor can act on
    /// individually. A consumer must check for a usable per-component alternate (or tint), not merely
    /// that <c>Components is not null</c>.</para></summary>
    public IReadOnlyList<ColourantComponent>? Components { get; init; }
}
