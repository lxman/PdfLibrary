using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Site 1 of the G-7 migration (design §3): PdfImageToCmyk's split consumes ColorantOrigin.Placement
/// instead of re-deriving slots from Role/ProcessChannel. Every assertion is positional — a
/// transposition has the same multiset/sum/max, so aggregate assertions are decorative (§5.2).
/// </summary>
public class PdfImageToCmykPlacementTests
{
    private static ColourantComponent Proc(string name, int channel, double? tint = null) =>
        new(name, ColourantRole.Process, tint, null, channel);

    private static ColourantComponent Sp(string name, double? tint = null) =>
        new(name, ColourantRole.Spot, tint, null, null);

    // B(0.42)=107, B(0.11)=28, B(0.99)=252 — Math.Round(v*255).

    [Fact]
    public void StencilInk_PlacementAlone_IsConsumed()
    {
        // Placement set, Components null. Before the migration the gate reads Components and falls to
        // the name split, which classifies PrCyan as a spot; after, the placement puts it on plate 0.
        var origin = new ColorantOrigin(["PrCyan", "Spot1"], [0.42, 0.11], "DeviceCMYK")
        {
            Placement = ColorantPlacement.Build([Proc("PrCyan", 0), Sp("Spot1")], 4),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "Spot1" }, ink!.Names);
        Assert.Equal(107, ink.ProcessCmyk[0]);   // PrCyan 0.42 on ITS plate, C
        Assert.Equal(0, ink.ProcessCmyk[1]);
        Assert.Equal(0, ink.ProcessCmyk[2]);
        Assert.Equal(0, ink.ProcessCmyk[3]);
        Assert.Equal(28, ink.TintPlanes[0]);     // Spot1 0.11 on plane 0
    }

    [Fact]
    public void StencilInk_Transposition_SlotIndexBeatsComponentChannel()
    {
        // Components says channel 0; the placement says Plate(1). Incoherent in production (Build is
        // the only producer) — constructed to pin WHICH source the site reads. Also the mutation
        // target for `plate[c] = slot.Index` -> `plate[c] = c`.
        var origin = new ColorantOrigin(["PrCyan", "Spot1"], [0.42, 0.11], "DeviceCMYK")
        {
            Components = [Proc("PrCyan", 0, 0.42), Sp("Spot1", 0.11)],
            ProcessChannelCount = 4,
            Placement = new ColorantPlacement([ColorantSlot.Plate(1), ColorantSlot.Spot(0)], ["Spot1"]),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(0, ink!.ProcessCmyk[0]);
        Assert.Equal(107, ink.ProcessCmyk[1]);   // slot.Index 1, NOT component channel 0
    }

    [Fact]
    public void StencilInk_NoPlacement_FallsBackToTheNameSplit()
    {
        // A plain DeviceN shape: no Components, no Placement. The name split must be byte-identical
        // to today — this is the fallback arm the 50 non-NChannel GWG patches ride.
        var origin = new ColorantOrigin(["Cyan", "Spot1"], [0.42, 0.11], "DeviceCMYK");

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "Spot1" }, ink!.Names);
        Assert.Equal(107, ink.ProcessCmyk[0]);
        Assert.Equal(28, ink.TintPlanes[0]);
    }

    [Fact]
    public void StencilInk_AllProcessPlacement_RefusesToTheNameSplit()
    {
        // R3, site 1's side: a no-spot split is REFUSED (the I-1 category-flip guard) and the whole
        // op takes the name split — which calls the non-reserved names spots. That is today's
        // recorded GAP, preserved bit-for-bit. Mutation target: dropping SplitByPlacement's
        // SpotNames.Count guard makes the placement path return a no-spot split, the caller's
        // `spotNames.Count == 0` fires, and this returns null instead.
        var origin = new ColorantOrigin(["PrCyan", "PrMagenta"], [0.42, 0.11], "DeviceCMYK")
        {
            Placement = ColorantPlacement.Build([Proc("PrCyan", 0), Proc("PrMagenta", 1)], 4),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "PrCyan", "PrMagenta" }, ink!.Names);   // the name split's answer
        Assert.Equal(28, ink.TintPlanes[1]);
        Assert.Equal(0, ink.ProcessCmyk[0]);
    }

    [Fact]
    public void StencilInk_SpotOrder_IsSlotOrder_ThroughBuild()
    {
        // The production shape: Build emits spot slots in component order. Pinned positionally
        // because a spot-order swap is silent plane corruption (the adjacent-stop lesson).
        var origin = new ColorantOrigin(["Spot1", "PrCyan", "Spot2"], [0.11, 0.42, 0.99], "DeviceCMYK")
        {
            Placement = ColorantPlacement.Build([Sp("Spot1"), Proc("PrCyan", 0), Sp("Spot2")], 4),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "Spot1", "Spot2" }, ink!.Names);
        Assert.Equal(28, ink.TintPlanes[0]);
        Assert.Equal(252, ink.TintPlanes[1]);
        Assert.Equal(107, ink.ProcessCmyk[0]);
    }

    [Fact]
    public void StencilInk_SpotPlane_IsTheSlotIndex_NotArrivalOrder()
    {
        // Hand-built placement with NON-sequential spot indexes — Build never makes one, so this is
        // the only fixture that can see a `spotOf[c] = <arrival counter>` mutation: sequential
        // re-counting assigns SpotA plane 0, but its slot says plane 1.
        var origin = new ColorantOrigin(["SpotA", "PrCyan", "SpotB"], [0.11, 0.42, 0.99], "DeviceCMYK")
        {
            Placement = new ColorantPlacement(
                [ColorantSlot.Spot(1), ColorantSlot.Plate(0), ColorantSlot.Spot(0)],
                ["SpotB", "SpotA"]),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "SpotB", "SpotA" }, ink!.Names);
        Assert.Equal(252, ink.TintPlanes[0]);    // SpotB (0.99) at ITS slot, 0
        Assert.Equal(28, ink.TintPlanes[1]);     // SpotA (0.11) at ITS slot, 1
        Assert.Equal(107, ink.ProcessCmyk[0]);
    }
}
