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

    /// <summary>
    /// Like <see cref="Parse"/> but keeps the document alive and returns it too, so
    /// <see cref="ColorSpaceResolver.Deref"/> actually resolves indirect references instead of
    /// short-circuiting on a null document. Same idiom as
    /// <c>SpotColorSpaceTests.ParseWithDoc</c> / <c>ColorSpaceResolverCharacterizationTests.ParseWithResources</c>
    /// — the caller disposes the document via <c>using (doc)</c>.
    /// </summary>
    private static (PdfArray Array, PdfDocument Doc) ParseWithDoc(
        string pdfArrayLiteral, params string[] extraObjects)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f",
            withFont: false, extraResources: "", extraObjects: extraObjects);
        PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return ((PdfArray)colorSpaces[new PdfName("Cs0")]!, doc);
    }

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

    [Fact]
    public void AllListedInProcessComponents_StaysRoleSpot_NotAbsorbedIntoProcess()
    {
        // Minor 2 (whole-branch review): RoleFor tests "None" first and unconditionally but had no
        // /All counterpart, so a malformed-but-real /Process /Components array that lists /All hit the
        // processChannels.ContainsKey arm and was classified Process -- losing the distinction
        // PageColorant needs to skip /All as "paint every plate" rather than route it to a channel.
        // /All is reserved and must never be absorbed into the process set, no matter what a malformed
        // /Process dictionary lists. The sibling test above (AllInADeviceN_IsTreatedAsAnOrdinarySpotName)
        // does not catch this: its fixture has no /Process dictionary at all, so processChannels is null
        // there and the ContainsKey arm never fires.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/All /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components [/All] >> >>]",
            0.5, 0.5);

        Assert.NotNull(o);
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

    [Fact]
    public void PartiallyShorterTintList_PairsInRangeAndLeavesTheRestNull()
    {
        // FewerTintsThanNames_LeavesTheRemainderNull only covers Tints.Count == 0, and
        // Tints_ArePairedPositionally only covers the exact-length case. Neither shape has SOME indices
        // in range and SOME out of range in the same call, which is the only shape where an off-by-one
        // in the pairing loop would actually index out of bounds rather than merely returning the wrong
        // (but still in-range) value.
        ColorantOrigin? o = Origin(NChannel(""), 0.25);

        Assert.Equal(2, o!.Components!.Count);
        Assert.Equal(0.25, o.Components[0].Tint);
        Assert.Null(o.Components[1].Tint);
    }

    // --- A corrupt /Attributes must not throw out of the render path ---

    [Fact]
    public void CorruptAttributesReference_DegradesToDeviceNDefaults_RatherThanThrowing()
    {
        // ColorSpaceResolver.cs:846 (Subtype = space.Subtype) is the first engine read of
        // SpotColorSpace.Subtype anywhere, which triggers EnsureAttributes. Before Pass 2a nothing ever
        // dereferenced element 4 (/Attributes), so a corrupt /Attributes object could not previously
        // reach this path. OriginForColorSpaceObject is called from PdfRenderer on every colour-setting
        // operator with no try/catch above it, so this must degrade rather than throw.
        //
        // Object 5 here is an in-use xref entry (so GetObject does not merely return null the way a
        // reference to a non-existent object number would) whose body is a lone "]" — a genuinely
        // unparseable token in object-value position, the same shape PdfParserTests pins as
        // Parser_ThrowsOnInvalidTokenInObjectContext. PdfDocument.GetObject's on-demand load path wraps
        // whatever PdfParser.ReadObject() throws into PdfParseException.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " 5 0 R]", "]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o);
            Assert.Equal("DeviceN", o!.Subtype);
            Assert.Null(o.Components);
        }
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

    // --- /Process /Components mapping ---

    private const string CmykProcess =
        "/Process << /ColorSpace /DeviceCMYK /Components [/PlateX /Magenta /Yellow /Black] >>";

    [Fact]
    public void ProcessComponents_MapNonReservedNamesToProcessRole()
    {
        // The GWG081 shape: a CMYK process dictionary alongside a spot. PlateX is listed in
        // /Components and is not one of the four reserved names, so it must classify Process via the
        // processNames arm specifically — not the reserved-name arm, which an earlier version of this
        // test (names [/Spot1 /Cyan]) accidentally exercised instead, passing even with processNames
        // ignored entirely.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/PlateX /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel " + CmykProcess + " >>]", 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }

    [Fact]
    public void NameListedInProcessComponents_IsProcess_EvenIfNotReserved()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/Ink1 /Magenta /Yellow /Black] >> >>]", 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }

    [Fact]
    public void ProcessEntryWins_OverAColorantsEntryForTheSameName()
    {
        // Table 71: "Any such definition shall be ignored if the colorant is also present in the
        // process dictionary."
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/Ink1 /Magenta /Yellow /Black] >> "
            + "/Colorants << /Ink1 [/Separation /Ink1 /DeviceCMYK " + Tint1 + "] >> >>]", 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);
    }

    [Fact]
    public void NoneStaysNone_EvenIfListedInProcessComponents()
    {
        // /None's discard rule is unconditional; a malformed process dictionary must not override it.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/None /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/None /Magenta /Yellow /Black] >> >>]", 0.5, 0.5);

        Assert.Equal(ColourantRole.None, o!.Components![0].Role);
    }

    [Fact]
    public void NonCmykProcessSpace_SuppressesComponentsEntirely()
    {
        // EXAMPLE 7's shape. Mapping RGB process components onto plates is a colour conversion this
        // engine has no converter for here, and classifying them as Process without being able to say
        // which plate they mark would map them onto none. Falling back is the safe answer.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/ProcessRed /ProcessGreen /ProcessBlue /Red] /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1 0 1 0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>"
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceRGB "
            + "/Components [/ProcessRed /ProcessGreen /ProcessBlue] >> >>]", 0.1, 0.2, 0.3, 0.4);

        Assert.NotNull(o);
        Assert.Equal("NChannel", o!.Subtype);
        Assert.Null(o.Components);
    }

    [Fact]
    public void DeviceGrayProcessSpace_IsAccepted()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceGray /Components [/Ink1] >> >>]",
            0.25, 0.5);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
    }

    [Fact]
    public void MalformedProcessDictionary_DegradesToReservedNamesOnly()
    {
        // /Components missing. Reserved names must still classify; the rest stay spots.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK >> >>]", 0.25, 0.5);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }

    [Fact]
    public void ProcessWithNoColorSpace_IsTreatedAsNoConstraint()
    {
        // The `case "":` arm in ProcessChannelCount: an absent /ColorSpace is "no constraint", not a
        // rejection — a malformed process dictionary should still let its /Components list classify
        // names, rather than being suppressed the way a non-CMYK /ColorSpace is.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /Components [/Ink1 /Magenta /Yellow /Black] >> >>]",
            0.25, 0.5);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
    }

    // --- Corrupt indirect references inside /Process must not throw ---

    [Fact]
    public void CorruptProcessColorSpaceReference_DegradesToReservedNamesOnly_RatherThanThrowing()
    {
        // /Process /ColorSpace as an indirect reference to a corrupt object. ProcessChannelCount's
        // Deref(csObj, doc) sits OUTSIDE EnsureAttributes's try/catch — that guard only covers the
        // /Attributes dictionary and its immediate Subtype/Colorants/Process values, not what /Process
        // itself points to. Without its own guard, this throws PdfParseException out of
        // OriginForColorSpaceObject, which PdfRenderer calls on every colour-setting operator with no
        // try/catch above it — a page that rendered fine before this task would start failing.
        //
        // Object 5 here is an in-use xref entry (so GetObject does not merely return null the way a
        // reference to a non-existent object number would) whose body is a lone "]" — the same
        // genuinely-unparseable-target technique CorruptAttributesReference_DegradesToDeviceNDefaults_
        // RatherThanThrowing uses one level up.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace 5 0 R >> >>]", "]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o);
            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);   // Magenta: reserved name
            Assert.Equal(ColourantRole.Spot, o.Components[1].Role);       // Spot1: no processNames survives the throw
        }
    }

    [Fact]
    public void ProcessComponentsAsIndirectReference_IsResolvedThroughTheDocument()
    {
        // GWG081's real shape: /Process is itself 52 0 R, and /Components can independently be an
        // indirect reference to the names array. Deref is a no-op when doc is null, so this test fails
        // if BuildComponents were ever called with doc: null instead of the OriginForColorSpaceObject
        // parameter — the exact silent-failure shape a plan correction called out for Task 2, since
        // every other new test in this file uses the null-document Origin(...) helper with direct
        // arrays and would stay green regardless.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components 5 0 R >> >>]",
            "[/Ink1 /Magenta /Yellow /Black]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);   // Ink1, via the indirect array
            Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
        }
    }

    // --- ProcessChannel: positional channel identity within the process colour space ---

    [Fact]
    public void NonReservedNameInProcessComponents_GetsItsPositionalIndex()
    {
        // Table 71: /Components names "correspond, in order, to the components of the process colour
        // space". PlateX is /Components[0], so it must carry channel 0 — not merely classify Process.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/PlateX /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel " + CmykProcess + " >>]", 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);
        Assert.Equal(0, o.Components[0].ProcessChannel);
    }

    [Fact]
    public void ReservedNameAbsentFromProcessComponents_GetsCanonicalIndex()
    {
        // /Components is present but doesn't list Magenta. Table 71: the reserved names "need not have
        // entries in the process dictionary" — Magenta still gets its canonical channel (1) because the
        // effective process space is DeviceCMYK.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK >> >>]", 0.25, 0.5);

        Assert.Equal(1, o!.Components![0].ProcessChannel);
    }

    [Fact]
    public void ReservedNames_GetCanonicalIndices_WithNoProcessDictionaryAtAll()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Cyan /Magenta /Yellow /Black] /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1 0 1 0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>"
            + " << /Subtype /NChannel >>]", 0.1, 0.2, 0.3, 0.4);

        Assert.Equal(0, o!.Components![0].ProcessChannel);
        Assert.Equal(1, o.Components[1].ProcessChannel);
        Assert.Equal(2, o.Components[2].ProcessChannel);
        Assert.Equal(3, o.Components[3].ProcessChannel);
    }

    [Fact]
    public void ReservedNameUnderDeviceGrayProcessSpace_GetsNullProcessChannel()
    {
        // A DeviceGray process space has ONE channel and nothing in the spec says which reserved name
        // owns it. Guessing would be exactly the half-built mapping the plan's Scope warns is worse
        // than not building it at all.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Magenta] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceGray /Components [/Ink1] >> >>]",
            0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![1].Role);   // Magenta still classifies Process
        Assert.Null(o.Components[1].ProcessChannel);
        Assert.Equal(0, o.Components[0].ProcessChannel);                // Ink1 IS listed, index 0 of 1
    }

    [Fact]
    public void SpotComponent_HasNullProcessChannel()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5);

        Assert.Equal(ColourantRole.Spot, o!.Components![1].Role);   // Spot1
        Assert.Null(o.Components[1].ProcessChannel);
    }

    [Fact]
    public void NoneComponent_HasNullProcessChannel()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Spot1 /None] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]", 0.5, 0.5);

        Assert.Equal(ColourantRole.None, o!.Components![1].Role);
        Assert.Null(o.Components[1].ProcessChannel);
    }

    [Fact]
    public void ProcessComponentsArrayLongerThanChannelCount_YieldsNullForTheOutOfRangeEntry()
    {
        // Five /Components entries under DeviceCMYK, which has 4 channels. The fifth is malformed
        // input, not a fifth channel — it must classify Process (it IS listed) but carry no channel.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Cyan /Magenta /Yellow /Black /Extra] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/Cyan /Magenta /Yellow /Black /Extra] >> >>]", 0.1, 0.2, 0.3, 0.4, 0.5);

        Assert.Equal(0, o!.Components![0].ProcessChannel);
        Assert.Equal(1, o.Components[1].ProcessChannel);
        Assert.Equal(2, o.Components[2].ProcessChannel);
        Assert.Equal(3, o.Components[3].ProcessChannel);
        Assert.Equal(ColourantRole.Process, o.Components[4].Role);   // Extra IS listed in /Components
        Assert.Null(o.Components[4].ProcessChannel);                 // but index 4 >= channel count 4
    }

    [Fact]
    public void DuplicateNameInProcessComponents_TakesTheFirstIndex()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/PlateX] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/PlateX /Magenta /PlateX /Black] >> >>]", 0.25);

        Assert.Equal(0, o!.Components![0].ProcessChannel);   // first occurrence of PlateX, not index 2
    }

    [Fact]
    public void ReservedNameUnderDeviceGray_StaysNull_EvenWhenACorruptComponentsElementThrows()
    {
        // Re-review finding: ProcessChannelCount reads /ColorSpace /DeviceGray successfully at :906
        // (channelCount correctly lowered to 1 at :907), but then a corrupt /Components element
        // throws while the loop is dereferencing it. The catch at :927-942 must NOT reset channelCount
        // back to 4 — doing so would hand Magenta the canonical index 1 for what is actually a
        // ONE-channel space, exactly the guess ProcessChannelFor's one-channel rule forbids, and an
        // index the range guard would otherwise have rejected outright.
        //
        // Object 5 is an in-use xref entry whose body is a lone "]" — a genuinely corrupt target, same
        // technique as CorruptIndirectProcessComponentsElement_DegradesToReservedNamesOnly_RatherThanThrowing.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceGray /Components [5 0 R] >> >>]",
            "]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o);
            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);   // Magenta: reserved name still applies
            Assert.Null(o.Components[0].ProcessChannel);                 // NOT 1 — the space has one channel
        }
    }

    [Fact]
    public void NoneListedInProcessComponents_HasNullProcessChannel()
    {
        // RoleFor tests /None first and unconditionally, so a malformed process dictionary listing
        // /None still classifies it None rather than Process — but ProcessChannelFor must independently
        // respect that: without its own role != Process guard, /None's LISTED index (0 here) would leak
        // through as a channel for a component that is never painted at all.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/None /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/None /Magenta /Yellow /Black] >> >>]", 0.5, 0.5);

        Assert.Equal(ColourantRole.None, o!.Components![0].Role);
        Assert.Null(o.Components[0].ProcessChannel);
    }

    [Fact]
    public void ListedIndexWinsOverCanonicalIndex_ForAReservedNameOutOfCanonicalPosition()
    {
        // Every other fixture that lists a reserved name in /Components happens to put it at its
        // canonical position, so a regression that consulted the canonical switch BEFORE processChannels
        // would stay green everywhere else. Here Magenta is listed at position 0 (canonical 1) and Cyan
        // at position 1 (canonical 0) — the LISTED index must win.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Cyan] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/Magenta /Cyan] >> >>]", 0.25, 0.5);

        Assert.Equal(0, o!.Components![0].ProcessChannel);   // Magenta: listed position 0, not canonical 1
        Assert.Equal(1, o.Components[1].ProcessChannel);     // Cyan: listed position 1, not canonical 0
    }

    // --- /Process /Components elements that are indirect references (M-2) ---

    [Fact]
    public void IndirectProcessComponentsElement_ResolvesToProcessRole_WithItsIndex()
    {
        // Asymmetric with SpotColorSpace.TryParse's DeviceN names-array handling, which derefs every
        // element for exactly this reason (SpotColorSpace.cs:216). An indirect /Components element must
        // not be misclassified as Spot, and must still carry its positional channel.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/PlateX /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [5 0 R /Magenta /Yellow /Black] >> >>]",
            "/PlateX");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.Equal(ColourantRole.Process, o!.Components![0].Role);
            Assert.Equal(0, o.Components[0].ProcessChannel);
            Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
        }
    }

    [Fact]
    public void CorruptIndirectProcessComponentsElement_DegradesToReservedNamesOnly_RatherThanThrowing()
    {
        // Object 5 here is an in-use xref entry whose body is a lone "]" — a genuinely corrupt target,
        // same technique as CorruptProcessColorSpaceReference_DegradesToReservedNamesOnly_RatherThanThrowing
        // one level up. A merely non-existent object would return null without throwing and make this
        // test vacuous.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [5 0 R /Magenta /Yellow /Black] >> >>]",
            "]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o);
            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);   // Magenta: reserved name still applies
            Assert.Equal(1, o.Components[0].ProcessChannel);              // canonical index; processChannels fell back to null
            Assert.Equal(ColourantRole.Spot, o.Components[1].Role);       // no processNames survives the throw
        }
    }

    // --- OwnAlternateCmyk from /Colorants ---

    private const string SpotColorants =
        "/Colorants << /Spot1 [/Separation /Spot1 /DeviceCMYK "
        + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>] >>";

    [Fact]
    public void SpotComponent_GetsItsOwnAlternateEvaluatedAtItsTint()
    {
        // C1 is [0.5 0 1 0] with N 1, so at tint 1 the component's own alternate is exactly that —
        // independently derivable from the function, not copied from a debugger.
        ColorantOrigin? o = Origin(NChannel(SpotColorants), 0.25, 1.0);

        IReadOnlyList<double>? alt = o!.Components![1].OwnAlternateCmyk;
        Assert.NotNull(alt);
        Assert.Equal(4, alt!.Count);
        Assert.Equal(0.5, alt[0], 3);
        Assert.Equal(0.0, alt[1], 3);
        Assert.Equal(1.0, alt[2], 3);
        Assert.Equal(0.0, alt[3], 3);
    }

    [Fact]
    public void SpotComponent_AlternateTracksTheTint()
    {
        ColorantOrigin? o = Origin(NChannel(SpotColorants), 0.25, 0.5);

        IReadOnlyList<double>? alt = o!.Components![1].OwnAlternateCmyk;
        Assert.NotNull(alt);
        Assert.Equal(0.25, alt![0], 3);   // 0.5 * 0.5
        Assert.Equal(0.5, alt[2], 3);     // 1.0 * 0.5
    }

    // The role gate itself — not an absent /Colorants entry — is what must suppress the process
    // component's alternate: Magenta gets an entry under its OWN name that would evaluate to a
    // non-null alternate if the `role != Spot` check in OwnAlternateFor were ever deleted.
    private const string SpotAndProcessColorants =
        "/Colorants << /Spot1 [/Separation /Spot1 /DeviceCMYK "
        + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>] "
        + "/Magenta [/Separation /Magenta /DeviceCMYK "
        + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>] >>";

    [Fact]
    public void ProcessComponent_HasNoOwnAlternate()
    {
        ColorantOrigin? o = Origin(NChannel(SpotAndProcessColorants), 0.25, 1.0);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);   // Magenta
        Assert.Null(o.Components[0].OwnAlternateCmyk);
    }

    [Fact]
    public void NChannelWithoutColorants_LeavesSpotAlternatesNull()
    {
        // The spec requires /Colorants for an NChannel space with spot colourants, but files lie.
        // A null alternate means "cannot revert this component individually", which is the signal
        // Pass 2b falls back on.
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 1.0);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Spot, o.Components![1].Role);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }

    [Fact]
    public void ColorantsEntryThatIsNotASeparation_LeavesTheAlternateNull()
    {
        ColorantOrigin? o = Origin(
            NChannel("/Colorants << /Spot1 /DeviceRGB >>"), 0.25, 1.0);

        Assert.Null(o!.Components![1].OwnAlternateCmyk);
    }

    [Fact]
    public void ColorantsEntryWithANonCmykAlternate_LeavesTheAlternateNull()
    {
        // BuildTintToCmyk accepts only DeviceCMYK and DeviceGray alternates; anything else is not
        // reducible to plates here.
        ColorantOrigin? o = Origin(
            NChannel("/Colorants << /Spot1 [/Separation /Spot1 /DeviceRGB "
                     + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>] >>"),
            0.25, 1.0);

        Assert.Null(o!.Components![1].OwnAlternateCmyk);
    }

    [Fact]
    public void ColorantsEntryThatIsMultiInput_LeavesTheAlternateNull()
    {
        // Table 71 requires /Colorants to be a full Separation space: exactly one input. Supplying a
        // DeviceN there (two names, two inputs) would otherwise have its multi-input tint transform
        // evaluated on a one-element array, silently producing a WRONG alternate rather than the null
        // this design specifies for anything that isn't a true single-input Separation.
        ColorantOrigin? o = Origin(
            NChannel("/Colorants << /Spot1 [/DeviceN [/Spot1 /Spot2] /DeviceCMYK " + Tint2 + "] >>"),
            0.25, 1.0);

        Assert.Null(o!.Components![1].OwnAlternateCmyk);
    }

    [Fact]
    public void SpotComponentWithNoTint_HasNoAlternate()
    {
        // Shadings resolve with no per-op colour, so there is no point to evaluate the alternate at.
        ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(
            Parse(NChannel(SpotColorants)), null, null);

        Assert.NotNull(o!.Components);
        Assert.Null(o.Components![1].Tint);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }

    [Fact]
    public void IndirectColorantsEntry_IsResolved()
    {
        // THE REAL-WORLD SHAPE, not an edge case. GWG081 — the corpus's only NChannel file — has
        // /Colorants 51 0 R whose value is << /GWG#20Green 14 0 R >>, and that Separation's own tint
        // transform is ALSO an indirect object. Both indirections have to resolve through doc
        // independently: object 5 is the Colorants entry (a Separation array whose tint transform is
        // itself 6 0 R, not inline), object 6 is that tint transform. A fixture with only the entry
        // indirect (and the tint transform inline) would stay green even if doc were dropped from the
        // BuildTintToCmyk call specifically — this shape closes that hole too. Uses the same
        // ParseWithDoc helper as the /Process indirection tests rather than duplicating its
        // Build/Load/GetColorSpaces sequence inline.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Colorants << /Spot1 5 0 R >> >>]",
            "[/Separation /Spot1 /DeviceCMYK 6 0 R]",
            "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 1.0], doc);

            IReadOnlyList<double>? alt = o!.Components![1].OwnAlternateCmyk;
            Assert.NotNull(alt);
            Assert.Equal(0.5, alt![0], 3);
            Assert.Equal(1.0, alt[2], 3);
        }
    }

    [Fact]
    public void NoneComponent_NeverLooksUpAColorantsEntry()
    {
        // Row 5-7: /None components are discarded when painting directly. Evaluating an alternate for
        // one would be meaningless work on a path that must never paint. The /Colorants entry keyed
        // "/None" here is deliberately an ORDINARY Separation (named /Decoy, not /None) that WOULD
        // evaluate to a non-null alternate if OwnAlternateFor's role gate were ever removed — a
        // "/None" Separation there would return null via BuildTintToCmyk's own PaintsNothing check
        // regardless of the gate, which is why the earlier version of this fixture didn't actually
        // prove the gate does anything.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Spot1 /None] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Colorants << /None [/Separation /Decoy /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>] >> >>]",
            0.5, 1.0);

        Assert.Equal(ColourantRole.None, o!.Components![1].Role);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }

    [Fact]
    public void CorruptColorantsEntryReference_DegradesToNullAlternate_RatherThanThrowing()
    {
        // /Colorants /Spot1 as an indirect reference to a corrupt object. The Deref of the entry must
        // sit INSIDE OwnAlternateFor's own try/catch (a review finding moved it there — it originally
        // sat above the try, matching the plan's Step 3 snippet verbatim). Without that placement, a
        // corrupt /Colorants entry throws PdfParseException out of OriginForColorSpaceObject, which
        // PdfRenderer calls on every colour-setting operator with no try/catch above it — a page that
        // rendered fine before Task 3 would start failing. This test errors (rather than merely
        // failing an assertion) if the guard is removed, because nothing above OwnAlternateFor's own
        // try/catch would catch the escaping exception.
        //
        // Object 5 here is an in-use xref entry (so GetObject does not merely return null the way a
        // reference to a non-existent object number would) whose body is a lone "]" — the same
        // genuinely-unparseable-target technique
        // CorruptAttributesReference_DegradesToDeviceNDefaults_RatherThanThrowing and
        // CorruptProcessColorSpaceReference_DegradesToReservedNamesOnly_RatherThanThrowing use one and
        // two levels up.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Colorants << /Spot1 5 0 R >> >>]", "]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 1.0], doc);

            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Spot, o.Components![1].Role);
            Assert.Null(o.Components[1].OwnAlternateCmyk);
        }
    }

    // --- Degenerate-input table rows with no test (M-1) ---

    [Fact]
    public void DuplicateNamesInNamesArray_YieldOnePerPosition_WithIndependentAlternates()
    {
        // Currently a natural consequence of the positional loop, but OwnAlternateFor is already
        // name-keyed: a "build a name→alternate map once" optimisation would silently collapse
        // duplicates with the suite green, because both positions would get the SAME cached alternate
        // even though their tints (and therefore their true alternates) differ.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Spot1 /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + SpotColorants + " >>]", 0.25, 1.0);

        Assert.Equal(2, o!.Components!.Count);
        Assert.Equal("Spot1", o.Components[0].Name);
        Assert.Equal("Spot1", o.Components[1].Name);
        Assert.Equal(0.25, o.Components[0].Tint);
        Assert.Equal(1.0, o.Components[1].Tint);

        IReadOnlyList<double>? alt0 = o.Components[0].OwnAlternateCmyk;
        IReadOnlyList<double>? alt1 = o.Components[1].OwnAlternateCmyk;
        Assert.NotNull(alt0);
        Assert.NotNull(alt1);
        Assert.Equal(0.125, alt0![0], 3);   // C1[0]=0.5 * tint 0.25
        Assert.Equal(0.5, alt1![0], 3);     // C1[0]=0.5 * tint 1.0
        Assert.NotEqual(alt0[0], alt1[0]);
    }

    [Fact]
    public void ProcessComponentsPresentButNotAnArray_DegradesToReservedNamesOnly()
    {
        // MalformedProcessDictionary_DegradesToReservedNamesOnly covers /Components MISSING. This
        // covers the other half of the table row: /Components present but the WRONG TYPE.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components /NotAnArray >> >>]", 0.25, 0.5);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }

    // --- ICCBased process spaces (Pass 2a') ---

    /// <summary>An [/ICCBased s] process space whose stream declares /N 4 is CMYK-shaped: components
    /// classify normally and reserved names get their canonical channels. ISO 32000-1 EXAMPLE 5.</summary>
    [Fact]
    public void IccBasedCmykProcessSpace_IsAcceptedAsFourChannel()
    {
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 4 /Length 0 >> stream\nendstream");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);
            // Magenta is listed at /Components[0]; the listed index wins over the canonical index
            // (see ListedIndexWinsOverCanonicalIndex_ForAReservedNameOutOfCanonicalPosition above), so
            // its channel is 0, not the canonical Magenta=1.
            Assert.Equal(0, o.Components[0].ProcessChannel);
            Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
        }
    }

    [Fact]
    public void IccBasedGrayProcessSpace_IsAcceptedAsOneChannel()
    {
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Ink1] >> >>]",
            "<< /N 1 /Length 0 >> stream\nendstream");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);
            Assert.Equal(0, o.Components[0].ProcessChannel);
        }
    }

    /// <summary>THE GRAY-HALF PIN. IccBasedGrayProcessSpace_IsAcceptedAsOneChannel above uses Ink1,
    /// which is LISTED in /Components — so it is answered by <c>processChannels</c> before
    /// <c>channelCount</c> is ever consulted (0 &lt; 1 and 0 &lt; 4 are both true, so the channel count
    /// cannot affect that test's answer). This test uses Magenta — reserved, but UNLISTED — so
    /// <c>ProcessChannelFor</c> must fall through to the <c>channelCount != 4</c> guard for real: under a
    /// genuinely one-channel ICCBased process space (<c>/N 1</c>), nothing in the spec says which
    /// reserved name owns the single channel, so Magenta must get null, not the canonical CMYK index
    /// 1. Mutating <c>ProcessChannelCount</c>'s <c>/N</c> switch from
    /// <c>nInt.Value is 4 or 1 ? nInt.Value : null</c> to <c>... ? 4 : null</c> (i.e. Gray silently
    /// reporting 4 channels) leaves every other ICCBased test green and only THIS one catches it.</summary>
    [Fact]
    public void ReservedNameUnderIccBasedGrayProcessSpace_GetsNullProcessChannel()
    {
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Ink1] >> >>]",
            "<< /N 1 /Length 0 >> stream\nendstream");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);   // Magenta: reserved name
            Assert.Null(o.Components[0].ProcessChannel);                  // one channel: no canonical index
        }
    }

    [Fact]
    public void IccBasedThreeChannelProcessSpace_SuppressesTheComponentList()
    {
        // /N 3 is not reducible to plates here — the same answer /DeviceRGB gets.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 3 /Length 0 >> stream\nendstream");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.Null(o!.Components);
        }
    }

    [Fact]
    public void IccBasedWithoutN_SuppressesTheComponentList()
    {
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /Length 0 >> stream\nendstream");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.Null(o!.Components);
        }
    }

    [Fact]
    public void IccBasedWithNoDocumentToResolveAgainst_SuppressesTheComponentList()
    {
        // NOT "s is a missing object" (Origin always passes doc: null, so Deref(iccArray[1], doc) at
        // ColorSpaceResolver.cs:560-561 returns the indirect reference UNCHANGED rather than resolving
        // it against a document — object 99 is never actually looked up). This still lands on the same
        // `is not PdfStream icc` clause (a PdfIndirectReference is not a PdfStream), so it isn't vacuous,
        // but it exercises "no document to resolve against", not "s doesn't resolve to a stream". See
        // IccBasedStreamReferenceResolvesToNonStream_SuppressesTheComponentList below for that half.
        ColorantOrigin? o = Origin(
            NChannel("/Process << /ColorSpace [/ICCBased 99 0 R] /Components [/Magenta] >>"), 0.25, 1.0);

        Assert.Null(o!.Components);
    }

    [Fact]
    public void IccBasedStreamReferenceResolvesToNonStream_SuppressesTheComponentList()
    {
        // The genuinely-missing half of "s unresolvable or not a stream": a real document resolves
        // object 5, and object 5 is an ordinary DICTIONARY (no `stream` keyword) that carries a real,
        // readable /N 4 — so if the `is not PdfStream icc` gate were ever weakened to also accept a
        // plain dictionary, this fixture would flip to non-null. That is deliberately different from
        // IccBasedWithNoDocumentToResolveAgainst_SuppressesTheComponentList above, whose object has no
        // /N at all and so would stay null under that same weakening — it wouldn't catch the mutation.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 4 >>");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.Null(o!.Components);
        }
    }

    [Fact]
    public void IccBasedArrayWithNoStreamElement_SuppressesTheComponentList()
    {
        ColorantOrigin? o = Origin(
            NChannel("/Process << /ColorSpace [/ICCBased] /Components [/Magenta] >>"), 0.25, 1.0);

        Assert.Null(o!.Components);
    }

    /// <summary>THE AXIS-B TEST. Reading /N dereferences the ICC stream — an object no path previously
    /// touched here. A corrupt target must degrade, not throw out of OriginForColorSpaceObject, which
    /// PdfRenderer calls on every colour-setting operator with no try/catch above it.
    ///
    /// <para>A reference to a merely NON-EXISTENT object returns null without throwing, so the fixture
    /// uses a genuinely corrupt target: ColourConformancePage.Build writes an in-use xref entry for every
    /// extraObject, so an object body of a lone ']' reaches the on-demand parser and throws.</para></summary>
    [Fact]
    public void CorruptIccBasedStreamReference_DegradesToReservedNamesOnly_RatherThanThrowing()
    {
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            // Degrades to reserved-name classification: the list survives, Magenta is still Process.
            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);
            Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
        }
    }

    /// <summary>Distinct from CorruptIccBasedStreamReference_… above: THERE, object 5 (the ICC stream
    /// itself) is the corrupt target, caught at ColorSpaceResolver.cs:999's
    /// <c>Deref(iccArray[1], doc) is not PdfStream</c>. HERE object 5 is a well-formed stream and its
    /// <c>/N</c> is itself an indirect reference to a corrupt object, so the corrupt target is one level
    /// deeper — at the <c>Deref(nObj, doc)</c> call inside the <c>/N</c> check
    /// (ColorSpaceResolver.cs:1003). Both throws are caught by the same try/catch in
    /// <see cref="ColorSpaceResolver"/>'s <c>BuildComponents</c> (mutation-verified in the Task 1 fix
    /// report), but IndirectN_IsResolved below only pins the happy path for this specific deref.</summary>
    [Fact]
    public void CorruptIndirectNReference_DegradesToReservedNamesOnly_RatherThanThrowing()
    {
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 6 0 R /Length 0 >> stream\nendstream",
            "]");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);   // Magenta: reserved name
            Assert.Equal(ColourantRole.Spot, o.Components[1].Role);       // no processNames survives the throw
        }
    }

    [Fact]
    public void IndirectN_IsResolved()
    {
        // /N may itself be an indirect reference; ColorSpaceResolver.cs:714's established idiom derefs it.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 6 0 R /Length 0 >> stream\nendstream",
            "4");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.NotNull(o!.Components);
            Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        }
    }

    // --- Table rows 2, 5 (CalGray) and 9 (/N wrong type) that the brief's eight tests above don't
    // individually exercise — completeness check against Task 1's degenerate-input table.

    [Fact]
    public void ProcessColorSpaceThatIsNeitherNameNorArray_IsTreatedAsNoConstraint()
    {
        // Table row 2: /ColorSpace present but resolving to neither a PdfName nor a PdfArray with a
        // PdfName head. ProcessChannelCount's family-resolution switch falls through to "" (the same
        // "unreadable shape" arm an absent key hits), which is the no-constraint default — not present
        // in IccBasedWithoutN_SuppressesTheComponentList/etc., which all exercise ICCBased-shaped inputs.
        // No /Components here, so this also confirms the canonical-index arm still fires under the
        // no-constraint default rather than being accidentally gated off by the new switch.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace 42 >> >>]", 0.25, 0.5);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        Assert.Equal(1, o.Components[0].ProcessChannel);   // canonical Magenta index under channelCount 4
    }

    [Fact]
    public void CalGrayProcessSpace_SuppressesTheComponentList()
    {
        // Table row 5's CalGray sub-case specifically (NonCmykProcessSpace_SuppressesComponentsEntirely
        // only covers /DeviceRGB). /CalGray is deliberately NOT treated as Gray: it is CIE-based rather
        // than a device space, matching InkDecider.ToCmyk's existing DeviceGray/CalGray distinction.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /CalGray >> >>]", 0.25, 0.5);

        Assert.NotNull(o!.Subtype);
        Assert.Null(o.Components);
    }

    [Fact]
    public void IccBasedWithNonIntegerN_SuppressesTheComponentList()
    {
        // Table row 9's other half: IccBasedWithoutN_SuppressesTheComponentList covers the KEY being
        // absent; this covers /N being PRESENT but not a PdfInteger even after Deref, which is a
        // different branch of the same `is not PdfInteger nInt` check.
        (PdfArray arr, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N /NotANumber /Length 0 >> stream\nendstream");
        using (doc)
        {
            ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(arr, [0.25, 0.5], doc);

            Assert.Null(o!.Components);
        }
    }
}
