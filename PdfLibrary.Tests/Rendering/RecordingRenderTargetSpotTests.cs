using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// <see cref="RecordingRenderTarget.DrawImage"/> attaches per-spot image ink (Soft-Proof SP-6a): a
/// recorded <see cref="ImageCommand"/> for a Separation/DeviceN spot image carries
/// <see cref="ImageCommand.Spots"/>; a DeviceCMYK image (no spot colorant) leaves it null (unchanged
/// behaviour, same as every existing recording test that never touches an image).
/// </summary>
public class RecordingRenderTargetSpotTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // Type 2 exponential (N=1): linear ramp C0..C1. This MUST be a real, evaluable tint function —
    // DrawImage decodes the image via PdfImageToRgba.ToRgba first (to get Rgba/Alpha), and that path
    // evaluates the Separation tint transform. TryToSpotInk's own unit tests get away with a bare
    // "Identity" name placeholder because TryToSpotInk splits by colorant NAME and never evaluates the
    // function — but ToRgba does, so a placeholder here would make DrawImage decode nothing at all.
    private static PdfDictionary Type2(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(c0));
        d.Add(new PdfName("C1"), Reals(c1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    // [/Separation /GWG Green /DeviceCMYK {t -> [0 0.5t t 0]}] — a spot colorant, not Process.
    private static PdfArray SeparationGwgGreen() => new(
        new PdfName("Separation"), new PdfName("GWG Green"), new PdfName("DeviceCMYK"),
        Type2([0, 0, 0, 0], [0, 0.5, 1, 0]));

    private static PdfImage SpotImage(byte[] data, int w, int h)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(w),
            [new PdfName("Height")] = new PdfInteger(h),
            [new PdfName("ColorSpace")] = SeparationGwgGreen(),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, data));
    }

    private static PdfImage DeviceCmykImage(byte[] data, int w, int h)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(w),
            [new PdfName("Height")] = new PdfInteger(h),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, data));
    }

    [Fact]
    public void DrawImage_records_Spots_for_a_separation_spot_image()
    {
        const int w = 2, h = 1;
        byte[] data = [255, 128];   // 2 px: tint 1.0, tint ~0.5
        PdfImage img = SpotImage(data, w, h);
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(img, new PdfGraphicsState());

        PageDrawList list = rec.TakeSnapshot();
        var spotImageCommand = Assert.IsType<ImageCommand>(Assert.Single(list.Commands));

        // A recorded ImageCommand for a Separation-spot image carries Spots.
        // (Names order == the colour space's spot colorants; TintPlanes/ProcessCmyk sized Width*Height.)
        Assert.NotNull(spotImageCommand.Spots);
        Assert.Equal(new[] { "GWG Green" }, spotImageCommand.Spots!.Names);
        Assert.Equal(w * h, spotImageCommand.Spots.TintPlanes.Length);       // 1 spot plane
        Assert.Equal(w * h * 4, spotImageCommand.Spots.ProcessCmyk.Length);
    }

    [Fact]
    public void DrawImage_leaves_Spots_null_for_a_DeviceCMYK_image()
    {
        byte[] data = [0, 128, 0, 0, 255, 0, 0, 0]; // 2 px direct CMYK, no spot colorant
        PdfImage img = DeviceCmykImage(data, w: 2, h: 1);
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(img, new PdfGraphicsState());

        PageDrawList list = rec.TakeSnapshot();
        var deviceCmykImageCommand = Assert.IsType<ImageCommand>(Assert.Single(list.Commands));

        Assert.Null(deviceCmykImageCommand.Spots);
    }

    // ---- SP-6d: a stencil takes its ink from the FILL, including its spot ----

    // A 2x1 stencil: /ImageMask true, 1 bpc. One row of 2 samples packed into one byte (0x80 = 1,0).
    // A stencil has NO ColorSpace entry — ISO 32000-1 §8.9.6.2 forbids one ("The image dictionary shall
    // not contain a ColorSpace entry"); its samples are "marked with the current colour". So it has no ink
    // of its own, and the fill is where its spot identity lives. That is the whole of SP-6d.
    private static PdfImage StencilImage()
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(2),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ImageMask")] = PdfBoolean.True,
            [new PdfName("BitsPerComponent")] = new PdfInteger(1),
        };
        return new PdfImage(new PdfStream(dict, [0x80]));
    }

    // The graphics state GWG020's "mask - i" actually presents, measured by SpotOracle --stencil-audit:
    //   663x663 OP=True OPM=1 fillCS=DeviceCMYK fill=[0.005,0,0.010,0] origin=GWG Green tints=[0.010]
    // i.e. GWG Green at 1% tint, its DeviceCMYK ALTERNATE already flattened into ResolvedFillColor.
    private static PdfGraphicsState SpotFillState() => new()
    {
        ResolvedFillColorSpace = "DeviceCMYK",
        ResolvedFillColor = [0.005, 0.0, 0.010, 0.0],
        ResolvedFillColorantOrigin = new ColorantOrigin(["GWG Green"], [0.010], "DeviceCMYK"),
        FillOverprint = true,
        OverprintMode = 1,
    };

    [Fact]
    public void DrawImage_takes_a_stencils_ink_from_the_FILL_including_its_spot()
    {
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(StencilImage(), SpotFillState());

        var cmd = Assert.IsType<ImageCommand>(Assert.Single(rec.TakeSnapshot().Commands));
        // The stencil's ink is the fill's — INCLUDING the spot identity that DrawImage otherwise bakes
        // away into the alternate's RGB via `maskColor`.
        Assert.NotNull(cmd.Spots);
        Assert.Equal(new[] { "GWG Green" }, cmd.Spots!.Names);
        // Constant across the image: the stencil's SHAPE lives in the RGBA alpha, not in the planes.
        Assert.Equal(2 * 1, cmd.Spots.TintPlanes.Length);
        Assert.Equal(new byte[] { 3, 3 }, cmd.Spots.TintPlanes);          // B(0.010) = round(2.55) = 3
        // GWG Green names no process colorant ⇒ nothing on C/M/Y/K. This is what stops the compositor
        // painting the alternate's [0.005,0,0.010,0] on all four plates and erasing the green backdrop.
        Assert.Equal(new byte[8], cmd.Spots.ProcessCmyk);
    }

    [Fact]
    public void DrawImage_leaves_Spots_null_for_a_stencil_with_a_PROCESS_fill()
    {
        var rec = new RecordingRenderTarget(document: null);
        // A DeviceGray fill carries no ColorantOrigin — 74 of the corpus's 76 stencils are exactly this
        // (glyph stencils). Spec non-goal 1: today's RGBA path is fine for them; leave them alone.
        var state = new PdfGraphicsState
        {
            ResolvedFillColorSpace = "DeviceGray",
            ResolvedFillColor = [0.0],
            ResolvedFillColorantOrigin = null,
        };

        rec.DrawImage(StencilImage(), state);

        var cmd = Assert.IsType<ImageCommand>(Assert.Single(rec.TakeSnapshot().Commands));
        Assert.Null(cmd.Spots);
    }

    [Fact]
    public void DrawImage_splits_a_mixed_DeviceN_stencils_process_colorants_from_its_spot()
    {
        var rec = new RecordingRenderTarget(document: null);
        var state = new PdfGraphicsState
        {
            ResolvedFillColorSpace = "DeviceCMYK",
            ResolvedFillColor = [0.0, 0.0, 0.0, 0.5],
            // DeviceN [/Black /GWG Green] at tints 0.5 / 0.25 — the SP-4 mixed shape, for a stencil.
            ResolvedFillColorantOrigin =
                new ColorantOrigin(["Black", "GWG Green"], [0.5, 0.25], "DeviceCMYK"),
            FillOverprint = true,
        };

        rec.DrawImage(StencilImage(), state);

        var cmd = Assert.IsType<ImageCommand>(Assert.Single(rec.TakeSnapshot().Commands));
        Assert.NotNull(cmd.Spots);
        // Only the SPOT rides a plane; the named PROCESS colorant paints its own tint directly, exactly as
        // SP-6a splits a mixed-DeviceN image (§8.6.6.4: a named process colorant maps to the device colorant).
        Assert.Equal(new[] { "GWG Green" }, cmd.Spots!.Names);
        Assert.Equal(new byte[] { 64, 64 }, cmd.Spots.TintPlanes);        // B(0.25) = 64
        Assert.Equal(new byte[] { 0, 0, 0, 128, 0, 0, 0, 128 }, cmd.Spots.ProcessCmyk);   // B(0.5) = 128 on K
    }

    // ---- Pass 2b-engine: the stencil path's PER-COMPONENT branch, pinned ----
    //
    // Every test above builds its ColorantOrigin positionally, so Components is null and all of them take
    // StencilInkFromFill's NAME-split fallback. The per-component branch added in Pass 2b-engine was
    // therefore entirely unpinned: forcing it off left the whole suite green, and no GWG corpus patch
    // reaches it either (Task 0's M2 found no NChannel stencil fill). These three fixtures are the only
    // thing that makes the branch, its four-channel gate, and its spotless-split refusal observable.

    /// <summary>An NChannel fill origin: the per-component carrier ColorSpaceResolver builds for
    /// <c>/Subtype /NChannel</c>, constructed directly because a stencil's origin arrives already built —
    /// StencilInkFromFill adds no resolution site. Placement is built the same way
    /// ColorSpaceResolver.OriginForColorSpaceObject builds it (from Components and
    /// ProcessChannelCount, via ColorantPlacement.Build) — a real per-component origin always carries
    /// both together, so a fixture that omitted Placement would not be a synthetic stand-in for
    /// production data, it would be a shape production never produces.</summary>
    private static ColorantOrigin NChannelFill(int processChannelCount, params ColourantComponent[] comps) =>
        new([.. comps.Select(c => c.Name)], [.. comps.Select(c => c.Tint ?? 0.0)], "DeviceCMYK")
        {
            Subtype = "NChannel",
            Components = comps,
            ProcessChannelCount = processChannelCount,
            Placement = ColorantPlacement.Build(comps, processChannelCount),
        };

    private static PdfGraphicsState StencilFillState(ColorantOrigin origin) => new()
    {
        ResolvedFillColorSpace = "DeviceCMYK",
        ResolvedFillColor = [0.0, 0.0, 0.0, 0.0],
        ResolvedFillColorantOrigin = origin,
        FillOverprint = true,
    };

    // THE BRANCH ITSELF. PrCyan is a non-reserved name listed in /Process /Components, so it is a PROCESS
    // component on channel 0. The name split calls it Spot and hands it a plane the registry never holds;
    // per-component its tint lands on the CYAN plate and only GWG Green is a spot. Disabling the branch
    // makes Names come back ["PrCyan", "GWG Green"] with an all-zero ProcessCmyk.
    [Fact]
    public void DrawImage_splits_a_stencils_NChannel_fill_by_COMPONENT_not_by_name()
    {
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(StencilImage(), StencilFillState(NChannelFill(4,
            new ColourantComponent("PrCyan", ColourantRole.Process, 0.5, null, 0),
            new ColourantComponent("GWG Green", ColourantRole.Spot, 0.25, null, null))));

        var cmd = Assert.IsType<ImageCommand>(Assert.Single(rec.TakeSnapshot().Commands));
        Assert.NotNull(cmd.Spots);
        Assert.Equal(new[] { "GWG Green" }, cmd.Spots!.Names);             // PrCyan is NOT a spot
        Assert.Equal(new byte[] { 64, 64 }, cmd.Spots.TintPlanes);         // B(0.25) = 64
        // PrCyan → process channel 0 = the CYAN plate at B(0.5) = 128, on both stencil pixels.
        Assert.Equal(new byte[] { 128, 0, 0, 0, 128, 0, 0, 0 }, cmd.Spots.ProcessCmyk);
    }

    // THE FOUR-CHANNEL GATE. ProcessChannel indexes the PROCESS space's channels, not the plates: under a
    // one-channel process space a listed name ALSO gets index 0, and painting it on cyan would be a colour
    // error the name split never made. Ink1 must stay an ordinary spot. Dropping the ProcessChannelCount: 4
    // gate at the stencil call site puts Ink1's 128 on the cyan plate and drops it from Names.
    [Fact]
    public void DrawImage_stencil_over_a_ONE_channel_process_space_falls_back_to_the_name_split()
    {
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(StencilImage(), StencilFillState(NChannelFill(1,
            new ColourantComponent("Ink1", ColourantRole.Process, 0.5, null, 0),
            new ColourantComponent("GWG Green", ColourantRole.Spot, 0.25, null, null))));

        var cmd = Assert.IsType<ImageCommand>(Assert.Single(rec.TakeSnapshot().Commands));
        Assert.NotNull(cmd.Spots);
        Assert.Equal(new[] { "Ink1", "GWG Green" }, cmd.Spots!.Names);     // BOTH spots — the name split
        Assert.Equal(new byte[] { 128, 64, 128, 64 }, cmd.Spots.TintPlanes);
        Assert.Equal(new byte[8], cmd.Spots.ProcessCmyk);                  // nothing on any plate
    }

    // THE SPOTLESS-SPLIT REFUSAL, from the stencil side. An all-process NChannel fill splits validly with
    // an EMPTY spot list, and StencilInkFromFill would then return null on `spotNames.Count == 0` — which
    // is not a colour change downstream but a CATEGORY change: cmd.Spots null means ProcessOther means
    // knockout, so an overprinting stencil erases the backdrop it used to preserve. That is precisely the
    // GWG020 failure SP-6d closed, re-opened from the other direction. SplitByComponents refuses instead,
    // and the name split's answer (both unreserved names are Spot) is what this pins — today's behaviour,
    // unchanged. Removing the `spotNames.Count > 0` guard makes this fail on Assert.NotNull.
    [Fact]
    public void DrawImage_stencil_whose_NChannel_fill_is_ALL_process_keeps_the_name_splits_answer()
    {
        var rec = new RecordingRenderTarget(document: null);

        rec.DrawImage(StencilImage(), StencilFillState(NChannelFill(4,
            new ColourantComponent("PrCyan", ColourantRole.Process, 0.5, null, 0),
            new ColourantComponent("PrMagenta", ColourantRole.Process, 0.25, null, 1))));

        var cmd = Assert.IsType<ImageCommand>(Assert.Single(rec.TakeSnapshot().Commands));
        Assert.NotNull(cmd.Spots);
        Assert.Equal(new[] { "PrCyan", "PrMagenta" }, cmd.Spots!.Names);
        Assert.Equal(new byte[] { 128, 64, 128, 64 }, cmd.Spots.TintPlanes);
        Assert.Equal(new byte[8], cmd.Spots.ProcessCmyk);
    }
}
