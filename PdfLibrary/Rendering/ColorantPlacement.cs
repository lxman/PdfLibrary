namespace PdfLibrary.Rendering;

/// <summary>Which kind of destination a colorant is placed on.</summary>
public enum ColorantSlotKind
{
    /// <summary><c>/None</c> — a colorant that is deliberately never run. A placement, not a failure.</summary>
    Nothing,

    /// <summary>A process plate, identified by its index in the four-channel process space.</summary>
    Plate,

    /// <summary>A spot colorant, identified by its index into
    /// <see cref="ColorantPlacement.SpotNames"/>. Whether a UNIT exists for it is the compositor's
    /// question — see <see cref="ColorantPlacement"/>.</summary>
    Spot,
}

/// <summary>Where one colorant is placed. <paramref name="Index"/> is the plate index for
/// <see cref="ColorantSlotKind.Plate"/>, the spot index for <see cref="ColorantSlotKind.Spot"/>, and
/// meaningless (0) for <see cref="ColorantSlotKind.Nothing"/>.</summary>
public readonly record struct ColorantSlot(ColorantSlotKind Kind, int Index)
{
    public static ColorantSlot Nothing => new(ColorantSlotKind.Nothing, 0);
    public static ColorantSlot Plate(int plateIndex) => new(ColorantSlotKind.Plate, plateIndex);
    public static ColorantSlot Spot(int spotIndex) => new(ColorantSlotKind.Spot, spotIndex);
}

/// <summary>
/// Which output colorant each component of an NChannel space belongs to — computed once, where
/// <c>/Process</c> is read, instead of re-derived by every consumer.
///
/// <para><b>The physical statement.</b> A press has UNITS; each carries one ink and one plate.
/// ISO 32000-2 Table 71's <c>/Process /Components</c> says which named colorant IS which unit, by
/// POSITION — a name cannot carry that (consider a plate named <c>/PlateX</c>). The alternate colour
/// space and tint transform are the recipe for simulating an ink you have no unit for. §8.6.6.5 then
/// reduces to one instruction: <i>run the real ink where you have the unit, simulate only where you
/// don't.</i> This table is the first half of that answer.</para>
///
/// <para><b>The boundary this type does not cross.</b> It says "spot slot 2". It never says "spot slot
/// 2 has a unit" — registration is a registry fact and the registry is compositor-side. The carrier
/// answers <i>which colorant is this</i>; the compositor answers <i>do we have that unit</i>.</para>
/// </summary>
/// <param name="Slots">One slot per component, aligned index-for-index with
/// <see cref="ColorantOrigin.Names"/> and <see cref="ColorantOrigin.Components"/>.</param>
/// <param name="SpotNames">The spot colorant names, in slot order. Empty when every component is
/// Process or None.</param>
public sealed record ColorantPlacement(
    IReadOnlyList<ColorantSlot> Slots,
    IReadOnlyList<string> SpotNames)
{
    /// <summary>
    /// Builds the table, or returns null meaning "fall back to whole-space behaviour".
    ///
    /// <para><b>Null in exactly three cases</b>, and this single nullability rule is why consumers do
    /// not each re-implement them:</para>
    /// <list type="number">
    /// <item><b><paramref name="components"/> is null.</b> Not just "not an NChannel space" — see
    /// <see cref="ColorantOrigin.Components"/> for the full case list, which also includes a genuine
    /// NChannel space whose <c>/Process /ColorSpace</c> this engine cannot reduce to plates (e.g.
    /// <c>/Lab</c>, <c>/DeviceRGB</c>, <c>/CalGray</c>, or an ICCBased stream whose <c>/N</c> is neither
    /// 4 nor 1 — see <c>ColorSpaceResolver.BuildComponents</c>). Treating null here as proof the space
    /// is not NChannel would make a consumer skip an NChannel-only branch for exactly the space it is
    /// meant to catch.</item>
    /// <item><b><paramref name="processChannelCount"/> is not 4.</b> A channel index is a PLATE index
    /// only under a four-channel process space; under <c>/DeviceGray</c> a listed name also gets index
    /// 0, byte-identical to a <c>/Cyan</c> under CMYK. See
    /// <see cref="ColorantOrigin.ProcessChannelCount"/>.</item>
    /// <item><b>Any component is unplaceable</b> — a Process component whose channel could not be
    /// determined, or an <c>/All</c>. All-or-nothing: one unplaceable colorant and the whole space
    /// falls back, because a partly-placed space would put some ink on the right unit and silently
    /// drop the rest.</item>
    /// </list>
    ///
    /// <para><b><c>/All</c> is detected by NAME, not by role.</b>
    /// <c>ColorSpaceResolver.RoleFor</c> maps <c>"All"</c> to <see cref="ColourantRole.Spot"/>, so the
    /// role cannot distinguish it and a role-only check would silently route <c>/All</c> to a spot
    /// slot. <c>/All</c> means "every colorant on the device at once", which is a different rule from
    /// placement and is out of scope here.</para>
    ///
    /// <para><b>Dereferences nothing.</b> Every value read is already materialised on
    /// <see cref="ColourantComponent"/> or is the count computed beside it, so this adds no resolution
    /// site and needs no <c>try</c>. That is deliberate: the dominant defect class in this programme is
    /// a new member access resolving a PDF object the previous code never touched and throwing out of a
    /// path that used to succeed.</para>
    /// </summary>
    public static ColorantPlacement? Build(
        IReadOnlyList<ColourantComponent>? components, int? processChannelCount)
    {
        if (components is null || processChannelCount != 4) return null;

        var slots = new ColorantSlot[components.Count];
        var spotNames = new List<string>();

        for (var i = 0; i < components.Count; i++)
        {
            ColourantComponent c = components[i];

            // Name, not Role — RoleFor collapses /All into Spot.
            if (c.Name == "All") return null;

            switch (c.Role)
            {
                case ColourantRole.None:
                    slots[i] = ColorantSlot.Nothing;
                    break;

                case ColourantRole.Process:
                    // Bounded by construction: ProcessChannelFor returns an index only when it is
                    // < channelCount, and channelCount IS processChannelCount, which is 4 here.
                    if (c.ProcessChannel is not { } channel) return null;
                    slots[i] = ColorantSlot.Plate(channel);
                    break;

                default:
                    slots[i] = ColorantSlot.Spot(spotNames.Count);
                    spotNames.Add(c.Name);
                    break;
            }
        }

        return new ColorantPlacement(slots, spotNames);
    }
}
