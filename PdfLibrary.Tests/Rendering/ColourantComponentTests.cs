using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The per-component carrier that Pass 2b's NChannel routing consumes. Nothing reads it yet, so these
/// tests are its only consumer — which is deliberate: the degenerate inputs enumerated in the Pass 2a
/// plan cannot appear in the Ghent corpus (they are malformed or exotic), so a test is the only thing
/// that can exercise them before the compositor depends on them.
/// </summary>
public class ColourantComponentTests
{
    private const string Tint1 = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>";
    private const string Tint2 = "<< /FunctionType 2 /Domain [0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    private static ColorantOrigin? Origin(string literal, params double[] tints) =>
        ColorSpaceResolver.OriginForColorSpaceObject(Parse(literal), tints, null);

    /// <summary>An NChannel [/Magenta /Spot1] with the given extra attribute entries.</summary>
    private static string NChannel(string extraAttributes) =>
        "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
        + extraAttributes + " >>]";

    // --- Components are populated ONLY for NChannel ---

    [Fact]
    public void PlainDeviceN_HasNoComponents_ButStillReportsItsSubtype()
    {
        ColorantOrigin? o = Origin("[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + "]", 0.25, 0.5);

        Assert.NotNull(o);
        Assert.Equal("DeviceN", o!.Subtype);      // Table 70 default
        Assert.Null(o.Components);
    }

    [Fact]
    public void Separation_HasNoComponents()
    {
        ColorantOrigin? o = Origin("[/Separation /Spot1 /DeviceCMYK " + Tint1 + "]", 0.75);

        Assert.NotNull(o);
        Assert.Equal("DeviceN", o!.Subtype);
        Assert.Null(o.Components);
    }

    [Fact]
    public void UnrecognisedSubtype_IsNotTreatedAsNChannel()
    {
        // Table 70 says the value SHALL be DeviceN or NChannel. Anything else is not NChannel, so
        // per-component routing must not engage.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /Sideways >>]", 0.25, 0.5);

        Assert.NotNull(o);
        Assert.Equal("Sideways", o!.Subtype);
        Assert.Null(o.Components);
    }

    [Fact]
    public void NChannel_PopulatesOneComponentPerName_InOrder()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5);

        Assert.NotNull(o);
        Assert.Equal("NChannel", o!.Subtype);
        Assert.NotNull(o.Components);
        Assert.Equal(2, o.Components!.Count);
        Assert.Equal("Magenta", o.Components[0].Name);
        Assert.Equal("Spot1", o.Components[1].Name);
    }

    // --- Roles ---

    [Fact]
    public void ReservedProcessNames_AreProcess_WithoutAProcessDictionary()
    {
        // Table 71: the reserved names "shall always be considered to be process colours ... they need
        // not have entries in the process dictionary".
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Cyan /Magenta /Yellow /Black] /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1 0 1 0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>"
            + " << /Subtype /NChannel >>]", 0.1, 0.2, 0.3, 0.4);

        Assert.NotNull(o);
        Assert.All(o!.Components!, c => Assert.Equal(ColourantRole.Process, c.Role));
    }

    [Fact]
    public void NoneComponent_IsRoleNone()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Spot1 /None] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]", 0.5, 0.5);

        Assert.NotNull(o);
        Assert.Equal(ColourantRole.Spot, o!.Components![0].Role);
        Assert.Equal(ColourantRole.None, o.Components[1].Role);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }

    [Fact]
    public void OrdinaryName_IsSpot()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);   // Magenta
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);        // Spot1
    }

    [Fact]
    public void AllInADeviceN_IsTreatedAsAnOrdinarySpotName()
    {
        // 8.6.6.5 forbids /All in a DeviceN names array, but InkDecider documents a deliberate
        // leniency for it (InkDecider.cs:95-100) that Pass 2b must not regress. Classifying it as a
        // spot here keeps that arm reachable.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/All /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]", 0.5, 0.5);

        Assert.Equal(ColourantRole.Spot, o!.Components![0].Role);
    }

    // --- Tints ---

    [Fact]
    public void Tints_ArePairedPositionally()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5);

        Assert.Equal(0.25, o!.Components![0].Tint);
        Assert.Equal(0.5, o.Components[1].Tint);
    }

    [Fact]
    public void FewerTintsThanNames_LeavesTheRemainderNull()
    {
        // A shading resolves its origin with rawColor null, so Tints is empty. Such a component has no
        // per-op tint and therefore no alternate colour to compute.
        ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(Parse(NChannel("")), null, null);

        Assert.NotNull(o);
        Assert.Equal(2, o!.Components!.Count);
        Assert.Null(o.Components[0].Tint);
        Assert.Null(o.Components[1].Tint);
        Assert.Null(o.Components[0].OwnAlternateCmyk);
    }

    [Fact]
    public void MoreTintsThanNames_IgnoresTheExtras()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5, 0.9);

        Assert.Equal(2, o!.Components!.Count);
        Assert.Equal(0.5, o.Components[1].Tint);
    }

    // --- The positional constructor is untouched ---

    [Fact]
    public void PositionalConstructor_StillCompiles_AndDefaultsTheNewMembersToNull()
    {
        // Seven Pellucid test files construct ColorantOrigin this way; that must keep working.
        var o = new ColorantOrigin(["Spot1"], [1.0], "DeviceCMYK");

        Assert.Null(o.Subtype);
        Assert.Null(o.Components);
    }
}
