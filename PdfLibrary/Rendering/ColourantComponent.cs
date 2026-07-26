namespace PdfLibrary.Rendering;

/// <summary>How one component of a DeviceN/NChannel colour space relates to the output device.</summary>
public enum ColourantRole
{
    /// <summary>A named spot colourant. May or may not be available on the device — that is the
    /// compositor's question, not the engine's.</summary>
    Spot,

    /// <summary>A process component: one of the four reserved names, or a name listed in
    /// <c>/Attributes /Process /Components</c>.</summary>
    Process,

    /// <summary>The reserved name <c>/None</c>. ISO 32000-2 §8.6.6.5: such components "shall never be
    /// painted on the page" when painting named colourants directly.</summary>
    None,
}

/// <summary>
/// One component of an NChannel colour space, carrying what ISO 32000-2 §8.6.6.5 needs in order to
/// evaluate that component individually: "only the ones not present on the output device shall use the
/// alternate colour space <b>of that component</b>."
///
/// <para>Populated engine-side and carried on <see cref="ColorantOrigin"/>, matching the precedent set
/// by <c>SpotImageInk</c>, <c>ShadingSpotInk</c> and <c>MeshSpotInk</c>, all of which carry names plus
/// pre-resolved process CMYK so that PDF function evaluation stays out of the compositor.</para>
/// </summary>
/// <param name="Name">The colourant name, verbatim from the names array.</param>
/// <param name="Role">Spot, Process, or None.</param>
/// <param name="Tint">This component's tint from the painting operator, or null when the operator
/// supplied no value for it — a shading resolves its origin with no per-op colour at all.</param>
/// <param name="OwnAlternateCmyk">The component's own alternate colour, evaluated at
/// <paramref name="Tint"/>, as four CMYK values — derived from <c>/Attributes /Colorants</c>, which for
/// an NChannel space defines each spot colourant as a full Separation space describing "the appearance
/// of that colorant alone". Null when there is no usable alternate: no <c>/Colorants</c> entry, an
/// entry that is not a Separation, an alternate this engine cannot reduce to CMYK, a tint transform
/// that failed, or no tint to evaluate at. A null here means the component cannot be reverted
/// individually, and the consumer must fall back rather than invent a colour.</param>
/// <param name="ProcessChannel">The zero-based index of the channel this component marks within the
/// process colour space's channel ordering, or null when that cannot be determined. ISO 32000-2
/// Table 71: <c>/Process /Components</c> names "correspond, in order, to the components of the process
/// colour space" — position <b>is</b> the channel identity, which the name alone does not carry (e.g. a
/// non-CMYK-named process plate such as <c>/PlateX</c>).
///
/// <para>Populated when: (1) the name appears in a successfully-read <c>/Components</c> array, giving
/// its index there (first index wins on a duplicate name); or (2) the name is one of the reserved
/// process names (Cyan/Magenta/Yellow/Black) and the effective process space is DeviceCMYK-shaped
/// (explicitly <c>/DeviceCMYK</c>, no <c>/Process</c> dictionary at all, or an unreadable/absent
/// <c>/Process /ColorSpace</c> — all treated as "no constraint"), giving the canonical index
/// Cyan=0/Magenta=1/Yellow=2/Black=3. Null otherwise: a Spot or None component, a reserved name under a
/// DeviceGray process space (one channel — nothing says which reserved name owns it, and guessing would
/// be the half-built mapping this plan's Scope warns is worse than not building it), or an index that
/// falls outside the process space's channel count (4 for DeviceCMYK/no-constraint, 1 for
/// DeviceGray) — treated as malformed input rather than an out-of-range channel.</para>
/// </param>
public sealed record ColourantComponent(
    string Name,
    ColourantRole Role,
    double? Tint,
    IReadOnlyList<double>? OwnAlternateCmyk,
    int? ProcessChannel = null);
