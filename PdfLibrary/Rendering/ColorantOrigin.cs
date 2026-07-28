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

    /// <summary>The number of channels in this NChannel space's process colour space — 4 for
    /// <c>/DeviceCMYK</c>, for an ICCBased process space whose stream declares <c>/N 4</c>, and for the
    /// "no constraint" case (no <c>/Process</c> dictionary, or one whose <c>/ColorSpace</c> is absent,
    /// unreadable, or whose dereference THREW); 1 for <c>/DeviceGray</c> and ICCBased <c>/N 1</c>.
    /// <b>Non-null exactly when <see cref="Components"/> is non-null</b> — the two are one answer, and a
    /// suppressed component list suppresses this too.
    ///
    /// <para><b>4 can therefore mean "we never found out".</b> When dereferencing <c>/Process</c> throws,
    /// the read degrades to reserved-name-only classification and this stays at its no-constraint
    /// default rather than being suppressed — the same number <c>ProcessChannelFor</c> was already
    /// bounding indices by on that path, so this surfaces the engine's existing behaviour rather than
    /// inventing one. It is safe for a consumer precisely because that path also leaves the listed-name
    /// map null, so every component falls back to reserved-name classification, where a channel→plate
    /// mapping and a name→plate mapping agree by construction.</para>
    ///
    /// <para><b>Why a consumer needs this and cannot infer it.</b>
    /// <see cref="ColourantComponent.ProcessChannel"/> indexes the PROCESS space's channels, not the CMYK
    /// plates. Under a one-channel process space a name listed in <c>/Process /Components</c> still gets
    /// index 0 (it is in range for that space), which is byte-identical to the index a <c>/Cyan</c> gets
    /// under a four-channel space. A consumer mapping channel→plate must therefore check this is 4 before
    /// treating an index as a CMYK plate; at any other count the component is not placeable on plates and
    /// the consumer must fall back rather than guess. Mirrors the reasoning in
    /// <c>ProcessChannelFor</c>'s own one-channel rule, which refuses to guess for exactly the same
    /// reason.</para></summary>
    public int? ProcessChannelCount { get; init; }

    /// <summary>Which output colorant each component belongs to (ISO 32000-2 Table 71), or null when
    /// this space must fall back to whole-space behaviour. See <see cref="ColorantPlacement.Build"/>
    /// for the three cases that produce null.
    ///
    /// <para><b>Non-null implies <see cref="Components"/> non-null and
    /// <see cref="ProcessChannelCount"/> == 4, but NOT the converse</b> — a component list can be
    /// fully populated and still be unplaceable (an <c>/All</c>, or a Process component whose channel
    /// could not be determined). A consumer that wants to place ink checks THIS, not
    /// <c>Components is not null</c>.</para>
    ///
    /// <para>Populated for shadings and meshes too. Those resolve their origin with no per-op colour,
    /// so every <see cref="ColourantComponent.Tint"/> is null — but placement does not depend on tint.
    /// Which unit a colorant belongs on is a property of the COLOUR SPACE, not of the paint operation:
    /// the shading supplies the per-stop values, this supplies the destinations.</para></summary>
    public ColorantPlacement? Placement { get; init; }
}
