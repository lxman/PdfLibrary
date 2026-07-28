using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class ShadingSpotSplitTests
{
    [Fact]
    public void SpotNames_ReturnsOnlySpotKindNamesInOrder()
    {
        var names = new[] { "GWG Green", "Cyan", "PANTONE 032 C", "Black" };
        Assert.Equal(new[] { "GWG Green", "PANTONE 032 C" }, ShadingSpotSplit.SpotNames(names));
    }

    [Fact]
    public void Split_DeviceNSpotPlusProcess_SplitsByName()
    {
        // DeviceN [GWG Green (spot), Cyan (process)] at components (0.5, 1.0).
        var names = new[] { "GWG Green", "Cyan" };
        var spot = new byte[1];
        uint proc = ShadingSpotSplit.Split([0.5, 1.0], names, spot, 0);

        Assert.Equal(128, spot[0]);                 // GWG Green tint 0.5 → 128
        Assert.Equal(0xFF000000u, proc);            // Cyan 1.0 → C plate; M/Y/K zero (spot alternate NOT folded)
    }

    [Fact]
    public void Split_PureSeparation_ProcessAllZero()
    {
        var names = new[] { "GWG Green" };
        var spot = new byte[1];
        uint proc = ShadingSpotSplit.Split([1.0], names, spot, 0);

        Assert.Equal(255, spot[0]);
        Assert.Equal(0u, proc);                     // no process colorant → process CMYK all zero
    }

    [Fact]
    public void Split_TwoSpots_NonZeroDestOffset_LandsAtOffsetPlusIndex()
    {
        // Two spots, no process colorant, written into "stop 1" of a 3-stop*2-spot buffer (destOffset 2).
        var names = new[] { "GWG Green", "PANTONE 032 C" };
        var spot = new byte[6];   // 3 stops * 2 spots
        uint proc = ShadingSpotSplit.Split([0.5, 0.2], names, spot, destOffset: 2);

        Assert.Equal(128, spot[2]);   // GWG Green (s=0) at destOffset + 0
        Assert.Equal(51, spot[3]);    // PANTONE 032 C (s=1) at destOffset + 1
        Assert.Equal(0, spot[0]);     // untouched slots stay 0
        Assert.Equal(0, spot[1]);
        Assert.Equal(0, spot[4]);
        Assert.Equal(0, spot[5]);
        Assert.Equal(0u, proc);       // no process colorant
    }

    [Fact]
    public void Split_AllNone_ContributeNothing()
    {
        var names = new[] { "None", "GWG Green" };   // "None" must be skipped, not treated as a spot
        var spot = new byte[1];
        uint proc = ShadingSpotSplit.Split([0.0, 0.4], names, spot, 0);

        Assert.Equal(102, spot[0]);                  // GWG Green 0.4 → 102 lands at index 0 (None skipped)
        Assert.Equal(0u, proc);
    }

    // --- placement-driven split (design §4.1) ---

    private static ColourantComponent Proc(string name, int channel) =>
        new(name, ColourantRole.Process, null, null, channel);

    private static ColourantComponent Sp(string name) =>
        new(name, ColourantRole.Spot, null, null, null);

    [Fact]
    public void SplitByPlacement_ListedProcessNames_LandOnTheirListedPlates()
    {
        // THE defect in one assertion. Names order (PrCyan, PrMagenta, PrYellow, Black) carrying
        // components (0.0, 0.36, 0.57, 0.02). The name split puts NONE of the first three on a plate.
        ColorantPlacement p = ColorantPlacement.Build(
            [Proc("PrCyan", 0), Proc("PrMagenta", 1), Proc("PrYellow", 2), Proc("Black", 3)], 4)!;

        uint proc = ShadingSpotSplit.SplitByPlacement([0.0, 0.36, 0.57, 0.02], p, [], 0);

        // PER PLATE. The same four values in any other order share sum, max and multiset.
        Assert.Equal(0u, (proc >> 24) & 0xFF);     // C
        Assert.Equal(92u, (proc >> 16) & 0xFF);    // M = round(0.36*255)
        Assert.Equal(145u, (proc >> 8) & 0xFF);    // Y = round(0.57*255)
        Assert.Equal(5u, proc & 0xFF);             // K = round(0.02*255)
    }

    [Fact]
    public void SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset()
    {
        // Also the mutation target for "index vs channel": Cyan sits at INDEX 1 but CHANNEL 0.
        ColorantPlacement p = ColorantPlacement.Build(
            [Sp("GWG Green"), Proc("Cyan", 0), Sp("PANTONE 032 C")], 4)!;
        var spot = new byte[6];   // 3 stops * 2 spots

        uint proc = ShadingSpotSplit.SplitByPlacement([0.5, 1.0, 0.2], p, spot, destOffset: 2);

        Assert.Equal(128, spot[2]);                // slot 0 at offset 2
        Assert.Equal(51, spot[3]);                 // slot 1 at offset 2
        Assert.Equal(0xFF000000u, proc);           // Cyan 1.0 on the C plate, not the M plate
        Assert.Equal(0, spot[0]);                  // stop 0 untouched
    }

    [Fact]
    public void SplitByPlacement_NoneContributesNothing_ToAnyPlateOrSpot()
    {
        ColorantPlacement p = ColorantPlacement.Build(
            [Proc("Cyan", 0), new("None", ColourantRole.None, null, null, null)], 4)!;
        var spot = new byte[1];

        uint proc = ShadingSpotSplit.SplitByPlacement([0.25, 1.0], p, spot, 0);

        Assert.Equal(64u, (proc >> 24) & 0xFF);
        Assert.Equal(0u, proc & 0x00FFFFFFu);
        Assert.Equal(0, spot[0]);                  // /None's 1.0 went nowhere
    }

    // --- builder-level pin (Step 8): D and E are PREDICTED GREEN because nothing yet observes the
    // real builders calling SplitByPlacement. Both mutations must go red HERE, by assertion, or the
    // task is not complete per the brief. ---

    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    private static PdfArray NamesArr(params string[] n)
    {
        var items = new PdfObject[n.Length];
        for (var i = 0; i < n.Length; i++) items[i] = new PdfName(n[i]);
        return new PdfArray(items);
    }

    // Single-input Type-2 exponential: the shading /Function idiom used throughout ShadingSpotInkTests.
    private static PdfDictionary Type2Fn(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d[new PdfName("FunctionType")] = new PdfInteger(2);
        d[new PdfName("Domain")] = new PdfArray(new PdfReal(0), new PdfReal(1));
        d[new PdfName("C0")] = new PdfArray(Array.ConvertAll(c0, v => (PdfObject)new PdfReal(v)));
        d[new PdfName("C1")] = new PdfArray(Array.ConvertAll(c1, v => (PdfObject)new PdfReal(v)));
        d[new PdfName("N")] = new PdfReal(1);
        return d;
    }

    // The colour SPACE's own tint transform (DeviceN array element 3) — only gates BuildCmykMapper
    // non-null and feeds the (unused-by-these-assertions) CmykColors ramp; SplitByPlacement/Split read
    // the shading's raw per-stop components directly, never this transform. A constant tint (matching
    // ShadingAllProcessNChannelTests' ConstantTint) makes that unambiguous.
    private static PdfDictionary ConstantSpaceTint()
    {
        var d = new PdfDictionary();
        d[new PdfName("FunctionType")] = new PdfInteger(2);
        d[new PdfName("Domain")] = Reals(0, 1);
        d[new PdfName("C0")] = Reals(1, 1, 1, 1);
        d[new PdfName("C1")] = Reals(1, 1, 1, 1);
        d[new PdfName("N")] = new PdfReal(1);
        return d;
    }

    // /Attributes: /Subtype /NChannel, /Process << /ColorSpace /DeviceCMYK
    //              /Components [/PrCyan /PrMagenta /PrYellow /Black] >>
    private static PdfDictionary NChannelAttributes()
    {
        var process = new PdfDictionary();
        process.Add(new PdfName("ColorSpace"), new PdfName("DeviceCMYK"));
        process.Add(new PdfName("Components"), NamesArr("PrCyan", "PrMagenta", "PrYellow", "Black"));

        var attrs = new PdfDictionary();
        attrs.Add(new PdfName("Subtype"), new PdfName("NChannel"));
        attrs.Add(new PdfName("Process"), process);
        return attrs;
    }

    // DeviceN [GWG Green (a real spot), PrCyan (process BY POSITION, not by literal name)]. Under the
    // name split (PageColorant.Classify) NEITHER name is one of the four reserved literals, so it would
    // call BOTH spots. Under placement, PrCyan is /Process /Components index 0 -- the C plate -- and
    // only GWG Green is a spot. This is the disagreement Step 8 requires be pinned at the BUILDER level.
    private static PdfArray MixedNChannelSpace() => new(
        new PdfName("DeviceN"), NamesArr("GWG Green", "PrCyan"), new PdfName("DeviceCMYK"),
        ConstantSpaceTint(), NChannelAttributes());

    [Fact]
    public void ShadingBuilder_PlacementDisagreesWithNameSplit_SpotInkFollowsPlacement()
    {
        var dict = new PdfDictionary();
        dict[new PdfName("ShadingType")] = new PdfInteger(2);
        dict[new PdfName("Coords")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(1), new PdfReal(0));
        dict[new PdfName("ColorSpace")] = MixedNChannelSpace();
        // Per-component ramps, both 0 -> 1: comps[0] = GWG Green, comps[1] = PrCyan (DeviceN Names order).
        dict[new PdfName("Function")] = new PdfArray(Type2Fn([0], [1]), Type2Fn([0], [1]));

        ShadingDescriptor? sh = ShadingBuilder.Build(dict, null);

        Assert.NotNull(sh);
        Assert.NotNull(sh!.SpotInk);
        // PLACEMENT: only "GWG Green" is a spot. A name-driven fallback (mutation D forces placement to
        // null) would call PrCyan a spot too, giving Names.Count 2 -- this equality catches either shape.
        Assert.Equal(new[] { "GWG Green" }, sh.SpotInk!.Names);

        int last = sh.Stops.Length - 1;
        // At t~1 both ramps are near-full: PrCyan's component must land on the C plate, not a spot tint.
        Assert.True((sh.SpotInk.StopProcessCmyk[last] >> 24 & 0xFF) > 200);      // C plate
        Assert.Equal(0u, sh.SpotInk.StopProcessCmyk[last] & 0x00FFFFFFu);         // M/Y/K zero
        Assert.True(sh.SpotInk.StopTints[last] > 200);                           // GWG Green tint, stride 1
    }

    // --- mesh: a type-6 Coons patch over the same mixed space (Step 8's "and mesh") ---

    private static readonly (int Col, int Row)[] BoundaryOrder =
    [
        (0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2),
        (3, 1), (3, 0), (2, 0), (1, 0)
    ];

    // A single flag-0 type-6 patch, uniform corner colour (GWG Green=0.502, PrCyan=1.0 -- raw bytes
    // 128/255 round-trip exactly through B(v) = round(v*255)), so any tessellated vertex pins it.
    private static PdfStream MixedNChannelMeshPatch()
    {
        var data = new List<byte> { 0 }; // edge flag 0
        foreach ((int col, int row) in BoundaryOrder)
        {
            data.Add((byte)(col * 85));
            data.Add((byte)(row * 85));
        }
        byte[] corner = [128, 255]; // GWG Green, PrCyan -- DeviceN Names order
        for (var i = 0; i < 4; i++) data.AddRange(corner); // c00, c03, c33, c30 -- uniform

        var dict = new PdfDictionary();
        dict.Add(new PdfName("ShadingType"), new PdfInteger(6));
        dict.Add(new PdfName("ColorSpace"), MixedNChannelSpace());
        dict.Add(new PdfName("BitsPerCoordinate"), new PdfInteger(8));
        dict.Add(new PdfName("BitsPerComponent"), new PdfInteger(8));
        dict.Add(new PdfName("BitsPerFlag"), new PdfInteger(8));
        dict.Add(new PdfName("Decode"), Reals(0, 100, 0, 100, 0, 1, 0, 1));
        return new PdfStream(dict, data.ToArray());
    }

    [Fact]
    public void MeshShadingReader_PlacementDisagreesWithNameSplit_MeshSpotInkFollowsPlacement()
    {
        ShadingDescriptor? d = ShadingBuilder.Build(MixedNChannelMeshPatch(), null);

        Assert.NotNull(d);
        Assert.NotNull(d!.MeshSpotInk);
        // PLACEMENT: only "GWG Green" is a spot. The name-driven fallback (mutation E) would call
        // PrCyan a spot too (Names.Count 2) AND -- the independent ink-loss channel Task 0 measured --
        // hasProcess would read false, dropping VertexProcessCmyk to null.
        Assert.Equal(new[] { "GWG Green" }, d.MeshSpotInk!.Names);
        Assert.NotNull(d.MeshSpotInk.VertexProcessCmyk);

        uint proc = d.MeshSpotInk.VertexProcessCmyk![0];
        Assert.True((proc >> 24 & 0xFF) > 250);            // PrCyan -> C plate, ~255
        Assert.Equal(0u, proc & 0x00FFFFFFu);               // M/Y/K zero

        Assert.Equal(128, d.MeshSpotInk.VertexTints[0]);    // GWG Green tint, stride 1 (only spot name)
    }
}
