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
public sealed record ColourantComponent(
    string Name,
    ColourantRole Role,
    double? Tint,
    IReadOnlyList<double>? OwnAlternateCmyk);
