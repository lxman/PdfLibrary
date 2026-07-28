using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The placement table: one colorant → one slot. ISO 32000-2 Table 71 makes POSITION the channel
/// identity for a /Process component, which a name cannot carry — so these tests assert slots
/// POSITIONALLY. A permutation of the same slots has the same multiset and would pass any
/// count/sum/contains assertion (design §5.2).
/// </summary>
public class ColorantPlacementTests
{
    private static ColourantComponent Process(string name, int channel) =>
        new(name, ColourantRole.Process, null, null, channel);

    private static ColourantComponent Spot(string name) =>
        new(name, ColourantRole.Spot, null, null, null);

    private static ColourantComponent None() =>
        new("None", ColourantRole.None, null, null, null);

    // --- the table is built when every component is placeable and the count is 4 ---

    [Fact]
    public void ListedProcessNames_TakeTheirListedChannel_NotTheirName()
    {
        // The whole point: none of these three is a reserved name, and each must land on the plate its
        // /Process /Components POSITION gives it.
        ColorantPlacement? p = ColorantPlacement.Build(
            [Process("PrCyan", 0), Process("PrMagenta", 1), Process("PrYellow", 2), Process("Black", 3)], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Plate(1), p.Slots[1]);
        Assert.Equal(ColorantSlot.Plate(2), p.Slots[2]);
        Assert.Equal(ColorantSlot.Plate(3), p.Slots[3]);
        Assert.Empty(p.SpotNames);
    }

    [Fact]
    public void TranspositionIsVisible_ListedIndexBeatsCanonicalName()
    {
        // /Components [/Black /Cyan] — Black listed at 0, Cyan at 1. The canonical answer would be
        // Black→3, Cyan→0. Listed position must win, and a positional assert is the only thing that
        // can see the difference.
        ColorantPlacement? p = ColorantPlacement.Build([Process("Black", 0), Process("Cyan", 1)], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);   // Black on CYAN's plate, as listed
        Assert.Equal(ColorantSlot.Plate(1), p.Slots[1]);
    }

    [Fact]
    public void SpotsGetSequentialSlots_AndSpotNamesInThatOrder()
    {
        ColorantPlacement? p = ColorantPlacement.Build(
            [Spot("GWG Green"), Process("Cyan", 0), Spot("PANTONE 032 C")], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Spot(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Plate(0), p.Slots[1]);
        Assert.Equal(ColorantSlot.Spot(1), p.Slots[2]);
        Assert.Equal(new[] { "GWG Green", "PANTONE 032 C" }, p.SpotNames);
    }

    [Fact]
    public void NoneIsAPlacement_NotARefusal()
    {
        // /None is a colorant the printer deliberately does not run. It must NOT refuse the table.
        ColorantPlacement? p = ColorantPlacement.Build([Process("Cyan", 0), None()], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Nothing, p.Slots[1]);
    }

    // --- the table is refused, whole, in exactly these cases ---

    [Fact]
    public void AllRefusesTheTable_EvenThoughItsRoleIsSpot()
    {
        // RoleFor maps "All" => ColourantRole.Spot, so the ROLE cannot distinguish it. Only the NAME
        // can. If this test fails, /All is silently being routed to a spot slot.
        Assert.Null(ColorantPlacement.Build([Process("Cyan", 0), Spot("All")], 4));
    }

    [Fact]
    public void AProcessComponentWithNoDeterminableChannel_RefusesTheWholeTable()
    {
        // All-or-nothing: one unplaceable component and the consumer must fall back WHOLE. Pass 2b's
        // equivalent rule was found silently unpinned; this is its pin.
        Assert.Null(ColorantPlacement.Build(
            [Process("Cyan", 0), new("PlateX", ColourantRole.Process, null, null, null)], 4));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(null)]
    public void AnyChannelCountOtherThanFour_RefusesTheTable(int? count)
    {
        // A channel index is a PLATE index only under a four-channel process space. Under /DeviceGray a
        // listed name also gets index 0 — byte-identical to a /Cyan under CMYK.
        Assert.Null(ColorantPlacement.Build([Process("Ink1", 0)], count));
    }

    [Fact]
    public void NullComponents_RefusesTheTable()
    {
        Assert.Null(ColorantPlacement.Build(null, 4));
    }

    // --- through the real resolver, on real parsed PDF colour spaces ---

    private const string Tint4 = "<< /FunctionType 2 /Domain [0 1 0 1 0 1 0 1] "
        + "/C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>";

    private static PdfArray ParseCs(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    private static ColorantOrigin? OriginFor(string literal) =>
        ColorSpaceResolver.OriginForColorSpaceObject(ParseCs(literal), null, null);

    [Fact]
    public void Resolver_NChannelListedProcessNames_PlacesByListedPosition()
    {
        // /Process /Components names all four, so all four are Process and none is a reserved name.
        ColorantOrigin? o = OriginFor(
            "[/DeviceN [/PrCyan /PrMagenta /PrYellow /Black] /DeviceCMYK " + Tint4
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/PrCyan /PrMagenta /PrYellow /Black] >> >>]");

        Assert.NotNull(o);
        ColorantPlacement? p = o!.Placement;
        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Plate(1), p.Slots[1]);
        Assert.Equal(ColorantSlot.Plate(2), p.Slots[2]);
        Assert.Equal(ColorantSlot.Plate(3), p.Slots[3]);
        Assert.Empty(p.SpotNames);
    }

    [Fact]
    public void Resolver_PlainDeviceN_HasNoPlacement()
    {
        ColorantOrigin? o = OriginFor("[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint4 + "]");

        Assert.NotNull(o);
        Assert.Null(o!.Placement);
    }

    [Fact]
    public void Resolver_NChannelOverAOneChannelProcessSpace_HasNoPlacement()
    {
        // Ink1 gets ProcessChannel 0 under /DeviceGray, which is NOT the cyan plate.
        ColorantOrigin? o = OriginFor(
            "[/DeviceN [/Ink1] /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] "
            + "/C1 [1 1 1 1] /N 1 >> << /Subtype /NChannel /Process << /ColorSpace /DeviceGray "
            + "/Components [/Ink1] >> >>]");

        Assert.NotNull(o);
        Assert.NotNull(o!.Components);          // the carrier IS populated ...
        Assert.Null(o.Placement);               // ... and placement still refuses it
    }
}
