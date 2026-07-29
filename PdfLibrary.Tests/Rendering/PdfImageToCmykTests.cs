using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// <see cref="PdfImageToCmyk"/> recovers native DeviceCMYK samples (no lossy CMYK→sRGB round-trip) for
/// images whose colour resolves to DeviceCMYK, so a CMYK compositor can paint them in native ink.
/// </summary>
public class PdfImageToCmykTests
{
    private static PdfImage Image(PdfObject colorSpace, byte[] data, int w, int h, int bpc = 8)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(w),
            [new PdfName("Height")] = new PdfInteger(h),
            [new PdfName("ColorSpace")] = colorSpace,
            [new PdfName("BitsPerComponent")] = new PdfInteger(bpc),
        };
        return new PdfImage(new PdfStream(dict, data));
    }

    [Fact]
    public void Direct_DeviceCMYK_returns_native_samples()
    {
        // Two pixels: 50% magenta (0,128,0,0), pure cyan (255,0,0,0).
        byte[] data = [0, 128, 0, 0, 255, 0, 0, 0];
        PdfImage img = Image(new PdfName("DeviceCMYK"), data, 2, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(data, cmyk);
    }

    [Fact]
    public void Direct_DeviceCMYK_16bit_returns_downconverted_native_samples()
    {
        // 16-bit big-endian DeviceCMYK (8 bytes/pixel). A 16-bit CMYK image must still yield a native ink
        // plane (down-converted to each sample's high byte) instead of being rejected and forced through the
        // lossy CMYK→RGB→CMYK round-trip (GWG181: colour offset vs a native-CMYK RIP).
        byte[] data =
        [
            0x00, 0x00, 0x80, 0x80, 0x00, 0x00, 0x00, 0x00,   // pixel 0 → high bytes (0, 0x80, 0, 0)
            0xFF, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00    // pixel 1 → high bytes (0xFF, 0, 0, 0)
        ];
        PdfImage img = Image(new PdfName("DeviceCMYK"), data, 2, 1, bpc: 16);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 0x00, 0x80, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00 }, cmyk);
    }

    [Fact]
    public void Indexed_DeviceCMYK_looks_up_native_palette()
    {
        // Palette: entry0 = 50% magenta, entry1 = pure cyan. Pixels index [1,0,1].
        byte[] palette = [0, 128, 0, 0, 255, 0, 0, 0];
        var cs = new PdfArray(new PdfName("Indexed"), new PdfName("DeviceCMYK"),
            new PdfInteger(1), new PdfString(palette));
        byte[] indices = [1, 0, 1];
        PdfImage img = Image(cs, indices, 3, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(3, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 255, 0, 0, 0, /* cyan */  0, 128, 0, 0, /* magenta */  255, 0, 0, 0 }, cmyk);
    }

    [Fact]
    public void DeviceRGB_image_returns_null()
    {
        PdfImage img = Image(new PdfName("DeviceRGB"), new byte[2 * 1 * 3], 2, 1);
        Assert.Null(PdfImageToCmyk.TryToCmyk(img, null, out _, out _));
    }

    // GWG170: a 236×236 CMYK JPEG 2000 patch (single-entry pclr, colr=CMYK) wrapped in the degenerate
    // [/Indexed /DeviceCMYK 0 <00ffff00>] colour space many writers emit. The JP2 filter already expands
    // the palette to 4 interleaved CMYK bytes/pixel; treating those as palette indices paints a striped
    // "X". TryToCmyk must instead hand the CMYK bytes back as native ink (uniform 0,255,255,0 = red).
    private const string Gwg170CmykJp2Base64 =
        "AAAADGpQICANCocKAAAAHGZ0eXBqcDIgAAAAAGpwMiBqcHhianB4IAAAAB5ycmVxAfj4AAUAAYAABUAADCAAEhAANwgAAAAAAFhqcDJoAAAAFm" +
        "loZHIAAADsAAAA7AABBwcBAAAAAA9jb2xyAQIBAAAADAAAABNwY2xyAAEEBwcHBwD//wAAAAAYY21hcAAAAQAAAAEBAAABAgAAAQMAAAC7anAy" +
        "Y/9P/1EAKQAAAAAA7AAAAOwAAAAAAAAAAAAAAOwAAADsAAAAAAAAAAAAAQcBAf9SAAwAAQABAAUDAwAB/1wAE0BASEhQSEhQSEhQSEhQSEhQ/5" +
        "AACgAAAAAAFgAG/5PfgCgRUFSjb/+QAAoAAAAAAA8BBv+TgP+QAAoAAAAAAA8CBv+TgP+QAAoAAAAAAA8DBv+TgP+QAAoAAAAAAA8EBv+TgP+Q" +
        "AAoAAAAAAA8FBv+TgP/Z";

    [Fact]
    public void Jpx_CMYK_indexed_wrapper_yields_native_cmyk_not_striped_indices()
    {
        byte[] jp2 = System.Convert.FromBase64String(Gwg170CmykJp2Base64);
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(236),
            [new PdfName("Height")] = new PdfInteger(236),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Filter")] = new PdfName("JPXDecode"),
            [new PdfName("ColorSpace")] = new PdfArray(new PdfName("Indexed"), new PdfName("DeviceCMYK"),
                new PdfInteger(0), new PdfString(new byte[] { 0, 255, 255, 0 })),
        };
        var img = new PdfImage(new PdfStream(dict, jp2));

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(236, w); Assert.Equal(236, h);
        Assert.Equal(236 * 236 * 4, cmyk!.Length);
        // Every pixel is the single palette entry CMYK(0,255,255,0); no (0,0,0,0) index-miss stripes.
        for (var i = 0; i < w * h; i++)
        {
            Assert.Equal(0, cmyk[i * 4]);
            Assert.Equal(255, cmyk[i * 4 + 1]);
            Assert.Equal(255, cmyk[i * 4 + 2]);
            Assert.Equal(0, cmyk[i * 4 + 3]);
        }
    }

    [Fact]
    public void Direct_DeviceCMYK_honours_inverting_decode()
    {
        byte[] data = [0, 128, 0, 0];  // one pixel
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Decode")] = new PdfArray(
                new PdfReal(1), new PdfReal(0), new PdfReal(1), new PdfReal(0),
                new PdfReal(1), new PdfReal(0), new PdfReal(1), new PdfReal(0)),
        };
        var img = new PdfImage(new PdfStream(dict, data));

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        Assert.NotNull(cmyk);
        Assert.Equal(new byte[] { 255, 127, 255, 255 }, cmyk); // inverted: 255-s
    }

    [Fact]
    public void DeviceGray_8bpc_separates_to_K_only_ink()
    {
        // black, mid, white → K-only ink (C=M=Y=0, K = 1 − grey). A grey image then lands on the same
        // black plate as an adjacent DeviceCMYK(0,0,0,k) box instead of an RGB→CMYK round-trip (GWG173).
        byte[] data = [0, 128, 255];
        PdfImage img = Image(new PdfName("DeviceGray"), data, 3, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(3, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 127,  0, 0, 0, 0 }, cmyk);
    }

    [Fact]
    public void DeviceGray_1bpc_unpacks_to_K_only_ink()
    {
        // 8 pixels in one byte, MSB-first: 1 0 1 0 0 0 0 0 → white,black,white,black×5 (bit 1 = white).
        byte[] data = [0b1010_0000];
        PdfImage img = Image(new PdfName("DeviceGray"), data, 8, 1, bpc: 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(8, w); Assert.Equal(1, h);
        byte[] expectedK = [0, 255, 0, 255, 255, 255, 255, 255];
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(0, cmyk![i * 4]);          // C
            Assert.Equal(0, cmyk[i * 4 + 1]);       // M
            Assert.Equal(0, cmyk[i * 4 + 2]);       // Y
            Assert.Equal(expectedK[i], cmyk[i * 4 + 3]); // K
        }
    }

    [Fact]
    public void DeviceGray_16bpc_keeps_high_byte_as_K_only_ink()
    {
        // Two 16-bit big-endian samples: 0x0000 (black) and 0xFFFF (white). High byte drives K, so
        // K = 255 then 0 — same K path an 8-bit tile of the same image takes (GWG183 8-bit vs 16-bit).
        byte[] data = [0x00, 0x00, 0xFF, 0xFF];
        PdfImage img = Image(new PdfName("DeviceGray"), data, 2, 1, bpc: 16);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 0 }, cmyk);
    }

    [Fact]
    public void DeviceGray_honours_inverting_decode()
    {
        byte[] data = [0]; // black sample; [1 0] decode flips it to white → K = 0
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Decode")] = new PdfArray(new PdfReal(1), new PdfReal(0)),
        };
        var img = new PdfImage(new PdfStream(dict, data));

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        Assert.NotNull(cmyk);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, cmyk);
    }

    // A minimal Separation/DeviceN colour space. The tint-function slot is still never evaluated by
    // TryToSpotInk — the per-component path resolves the space with rawColor: null, so every component's
    // Tint is null and OwnAlternateFor returns at its first line without building a transform, and the
    // name-split fallback reads only names — so a bare name placeholder suffices.
    private static PdfArray Separation(string name) =>
        new(new PdfName("Separation"), new PdfName(name), new PdfName("DeviceCMYK"), new PdfName("Identity"));
    private static PdfArray DeviceN(params string[] names) =>
        new(new PdfName("DeviceN"), new PdfArray(names.Select(n => (PdfObject)new PdfName(n)).ToArray()),
            new PdfName("DeviceCMYK"), new PdfName("Identity"));

    // An NChannel space: [/DeviceN [names] /DeviceCMYK <tint fn> << /Subtype /NChannel /Process <<…>> >>].
    private static PdfArray NChannel(PdfDictionary? process, params string[] names)
    {
        var attrs = new PdfDictionary { [new PdfName("Subtype")] = new PdfName("NChannel") };
        if (process is not null) attrs[new PdfName("Process")] = process;
        return new PdfArray(
            new PdfName("DeviceN"),
            new PdfArray(names.Select(n => (PdfObject)new PdfName(n)).ToArray()),
            new PdfName("DeviceCMYK"), new PdfName("Identity"), attrs);
    }

    private static PdfDictionary Process(string colorSpace, params string[] components) =>
        new()
        {
            [new PdfName("ColorSpace")] = new PdfName(colorSpace),
            [new PdfName("Components")] =
                new PdfArray(components.Select(n => (PdfObject)new PdfName(n)).ToArray()),
        };

    /// <summary>Same idiom as <c>NChannelRampTests.ParseWithDoc</c> (:50-59) — a private per-file copy, the
    /// established convention in this test project (five existing files each carry their own) rather than a
    /// shared helper. Keeps the document alive so indirect references actually resolve; the caller disposes
    /// via <c>using (doc)</c>.</summary>
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

    [Fact]
    public void Separation_spot_image_splits_to_spot_plane_only()
    {
        byte[] data = [255, 128];                               // 2 px: tint 1.0, tint ~0.5
        PdfImage img = Image(Separation("GWG Green"), data, 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out int w, out int h);

        Assert.NotNull(ink);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 255, 128 }, ink.TintPlanes);  // one plane = the raw per-pixel tints
        Assert.All(ink.ProcessCmyk, b => Assert.Equal((byte)0, b)); // pure spot → no process ink
    }

    [Fact]
    public void DeviceN_black_plus_spot_splits_process_to_K_and_spot_to_plane()
    {
        // DeviceN [Black, GWG Green], 2 bytes/pixel. Pixel 0 = (Black 1.0, Green 0.5); pixel 1 = (Black 0, Green 1.0).
        byte[] data = [255, 128, 0, 255];
        PdfImage img = Image(DeviceN("Black", "GWG Green"), data, 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);          // only the spot colorant gets a plane
        Assert.Equal(new byte[] { 128, 255 }, ink.TintPlanes);   // green tints per pixel
        // ProcessCmyk: C M Y K per pixel — Black → K plate, rest 0.
        Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 0 }, ink.ProcessCmyk);
    }

    [Fact]
    public void Indexed_over_Separation_splits_from_palette()
    {
        // Indexed base = Separation "GWG Green"; palette entries 0..1 = tints (0.5, 1.0). Pixels [1,0].
        byte[] palette = [128, 255];
        var cs = new PdfArray(new PdfName("Indexed"), Separation("GWG Green"),
            new PdfInteger(1), new PdfString(palette));
        PdfImage img = Image(cs, [1, 0], 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 255, 128 }, ink.TintPlanes);   // entry1 then entry0
        Assert.All(ink.ProcessCmyk, b => Assert.Equal((byte)0, b));
    }

    // SP-6c deliberately un-matches the two scopes, which SP-6a's final review (Finding 1) had deliberately
    // matched. They answer different questions:
    //   TryToCmyk    NEEDS the alternate — it converts through it, so a non-DeviceCMYK alternate is a real
    //                blocker and it still returns null (unchanged).
    //   TryToSpotInk NEVER reads the alternate — it splits by colorant NAME. §8.6.6.5 conditions reversion on
    //                colorant AVAILABILITY, not on what the alternate is, so the alternate was never grounds
    //                to bail. The alternate is the fallback for colorants we cannot paint, not a trigger.
    // The divergence is safe: when a spot is unregistered, the compositor's all-or-nothing routing falls back
    // to the flatten path, finds Cmyk == null, and lands on RGBA — exactly the old behaviour.
    [Fact]
    public void NonCmyk_alternate_spot_image_still_splits_but_yields_no_cmyk_plane()
    {
        byte[] data = [255, 128]; // 2 px
        var cs = new PdfArray(new PdfName("Separation"), new PdfName("GWG Green"),
            new PdfName("DeviceRGB"), new PdfName("Identity"));
        PdfImage img = Image(cs, data, 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);
        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 255, 128 }, ink.TintPlanes);

        // TryToCmyk still bails: it would have to convert through the alternate, and this one isn't CMYK.
        Assert.Null(PdfImageToCmyk.TryToCmyk(img, null, out _, out _));
    }

    // A Lab colour space array. TryToSpotInk only reads the alternate's NAME (to decide nothing, after
    // SP-6c) — the dictionary is here so the fixture matches GWG080's real shape rather than a stub.
    private static PdfArray Lab() =>
        new(new PdfName("Lab"), new PdfDictionary
        {
            [new PdfName("WhitePoint")] = new PdfArray(new PdfReal(0.9642), new PdfReal(1.0), new PdfReal(0.82491)),
            [new PdfName("Range")] = new PdfArray(new PdfReal(-128), new PdfReal(127), new PdfReal(-128), new PdfReal(127)),
        });

    private static PdfArray DeviceNWithAlt(PdfObject alternate, params string[] names) =>
        new(new PdfName("DeviceN"), new PdfArray(names.Select(n => (PdfObject)new PdfName(n)).ToArray()),
            alternate, new PdfName("Identity"));

    // SP-6c, the motivating fixture. GWG080's Duotone turtle is /Indexed over
    // /DeviceN [/Black /PANTONE 265 C /None /None /None] with a /Lab alternate. Before SP-6c the altSpace
    // guard bailed, the image flattened through Lab (Lab→RGB→CMYK), and Black + PANTONE were never painted
    // as separations at all — gs paints them (Black 87%, PANTONE 92%), which is the fixture's whole point
    // ("should appear only in Black and Pantone 265C separations").
    [Fact]
    public void DeviceN_spot_image_with_a_Lab_alternate_splits_by_name()
    {
        // 5 components/pixel. px0 = (Black 1.0, PANTONE 0.5); px1 = (Black 0, PANTONE 1.0).
        // The three /None channels carry GWG080's Lab fallback values and must contribute nothing.
        byte[] data = [255, 128, 40, 50, 60,   0, 255, 40, 50, 60];
        PdfImage img = Image(DeviceNWithAlt(Lab(), "Black", "PANTONE 265 C", "None", "None", "None"), data, 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out int w, out int h);

        Assert.NotNull(ink);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(new[] { "PANTONE 265 C" }, ink!.Names);      // only the spot colorant gets a plane
        Assert.Equal(new byte[] { 128, 255 }, ink.TintPlanes);
        // Black → K plate; /None contributes nothing, so C/M/Y stay 0 despite the 40/50/60 channels.
        Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 0 }, ink.ProcessCmyk);
    }

    // The non-goal SP-6c must NOT widen into: a purely-process DeviceN has no spot to route, so the
    // CMYK/RGBA path still owns it whatever the alternate. This is GWG230's shape.
    [Fact]
    public void DeviceN_without_a_spot_returns_null_whatever_the_alternate()
    {
        PdfImage img = Image(DeviceNWithAlt(new PdfName("DeviceGray"), "Black"), [255, 0], 2, 1);
        Assert.Null(PdfImageToCmyk.TryToSpotInk(img, null, out _, out _));
    }

    // GWG081 Duotone shape: an Indexed image over a DeviceN[Black, GWG Green] base (2 colorants/entry).
    // Locks in the motivating fixture's split in-suite (previously only checked by a deleted probe).
    [Fact]
    public void Indexed_over_DeviceN_black_plus_spot_splits_from_palette()
    {
        // Palette entries are 2 bytes each: entry0 = (Black 1.0, Green 0.5), entry1 = (Black 0, Green 1.0).
        byte[] palette = [255, 128, 0, 255];
        var cs = new PdfArray(new PdfName("Indexed"), DeviceN("Black", "GWG Green"),
            new PdfInteger(1), new PdfString(palette));
        PdfImage img = Image(cs, [1, 0], 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 255, 128 }, ink.TintPlanes);              // green tints, pixel order [1,0]
        Assert.Equal(new byte[] { 0, 0, 0, 0,  0, 0, 0, 255 }, ink.ProcessCmyk); // K: pixel0=idx1→K0, pixel1=idx0→K255
    }

    [Fact]
    public void No_spot_images_return_null()
    {
        Assert.Null(PdfImageToCmyk.TryToSpotInk(Image(new PdfName("DeviceCMYK"), new byte[8], 2, 1), null, out _, out _));
        Assert.Null(PdfImageToCmyk.TryToSpotInk(Image(new PdfName("DeviceRGB"), new byte[6], 2, 1), null, out _, out _));
        Assert.Null(PdfImageToCmyk.TryToSpotInk(Image(DeviceN("Cyan", "Black"), new byte[4], 2, 1), null, out _, out _));
    }

    // "All"/"None" are spec-legal DeviceN colorant names (PageColorant.Classify maps them to
    // ColorantKind.All/None — neither Spot nor Process), and per the SP-6a design they contribute
    // NOTHING to the split when they sit alongside a genuine spot colorant. Before the fix, the
    // per-pixel write loop treated any non-process colorant as spot and indexed planes[-1] for
    // "All"/"None", throwing IndexOutOfRangeException at pixel 0.
    [Fact]
    public void DeviceN_all_or_none_alongside_spot_contributes_nothing_no_crash()
    {
        // DeviceN [All, GWG Green], 2 bytes/pixel. Pixel 0 = (All 1.0, Green 0.5); pixel 1 = (All 0, Green 1.0).
        byte[] data = [255, 128, 0, 255];
        PdfImage img = Image(DeviceN("All", "GWG Green"), data, 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);          // only the spot colorant gets a plane
        Assert.Equal(new byte[] { 128, 255 }, ink.TintPlanes);    // green tints per pixel
        Assert.All(ink.ProcessCmyk, b => Assert.Equal((byte)0, b)); // "All" contributes nothing to the split
    }

    // ---- A DeviceGray alternate separates K-only (GWG230 c/d) ----

    private static PdfArray Reals(params double[] v) =>
        new(v.Select(x => (PdfObject)new PdfReal(x)).ToArray());

    // Type 2 exponential, N=1: a real, evaluable tint transform t -> C0 + t*(C1-C0).
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

    [Fact]
    public void Separation_with_a_DeviceGray_alternate_separates_K_only()
    {
        // GWG230 patch c, exactly: [/Separation /Black /DeviceGray {t -> gray}], tint 1 -> gray 0 (black),
        // tint 0 -> gray 1 (white). PDF 32000-1 §10.3.3: DeviceGray is a DEVICE space and separates onto the
        // black plate alone — k = 1 - gray. Before this, BuildTintToCmyk bailed on any non-DeviceCMYK
        // alternate, so the image reverted to RGBA and the compositor ICC-converted RGB(g,g,g) into a RICH
        // gray (ink on all four plates) that no longer matched an adjacent DeviceCMYK(0,0,0,k) box.
        PdfArray cs = new(new PdfName("Separation"), new PdfName("Black"), new PdfName("DeviceGray"),
            Type2([1.0], [0.0]));
        // 2 px: tint 0.0 (-> gray 1.0 -> K 0) and tint 1.0 (-> gray 0.0 -> K 255)
        PdfImage img = Image(cs, [0, 255], 2, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        // K-only on BOTH pixels: no ink on C/M/Y at any tint.
        Assert.Equal(new byte[] { 0, 0, 0, 0, /**/ 0, 0, 0, 255 }, cmyk);
    }

    [Fact]
    public void DeviceN_with_a_DeviceGray_alternate_separates_K_only()
    {
        // GWG230 patch d: [/DeviceN [/Black] /DeviceGray {t -> gray}].
        PdfArray cs = new(new PdfName("DeviceN"), new PdfArray(new PdfName("Black")),
            new PdfName("DeviceGray"), Type2([1.0], [0.0]));
        PdfImage img = Image(cs, [0, 255], 2, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        Assert.NotNull(cmyk);
        Assert.Equal(new byte[] { 0, 0, 0, 0, /**/ 0, 0, 0, 255 }, cmyk);
    }

    // Deliberately retires the old Separation_with_a_CalGray_alternate_still_reverts pin. What that pin
    // protected was TryToCmyk's SCOPE half: before G-14, only a DeviceCMYK/DeviceGray alternate was
    // converted to ink here — a CIE-based (CalGray/Lab/ICCBased) alternate deferred to the managed RGBA
    // path, same as InkDecider.ToCmyk for fills. G-14 retires that for ALL-RESERVED colourant names
    // specifically (§8.6.6.4 first clause): the alternate space + tint transform are a simulation recipe
    // for when the colourant is UNAVAILABLE, but a reserved name (/Black, /Cyan, /Magenta, /Yellow, /All)
    // is always available on a CMYK device — so its tint IS the ink percentage, and the alternate (of
    // whatever flavour, device or CIE) is ignored unconditionally. /Black over CalGray now routes
    // directly to the K plate instead of deferring.
    [Fact]
    public void Separation_Black_CalGrayAlternate_RoutesDirectly_G14()
    {
        PdfArray cs = new(new PdfName("Separation"), new PdfName("Black"),
            new PdfArray(new PdfName("CalGray"), new PdfDictionary()), Type2([1.0], [0.0]));
        // 2 px: tint 0.0 and tint 1.0 — sampled straight onto K, alternate/tint transform ignored.
        PdfImage img = Image(cs, [0, 255], 2, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(new byte[] { 0, 0, 0, 0, /**/ 0, 0, 0, 255 }, cmyk);
    }

    // The scope half of the retired pin, still live: a NON-reserved Separation name (a genuine spot) with
    // a CalGray alternate is NOT all-reserved, so G-14's direct route does not fire, and this still defers
    // to the managed RGBA/CalGray path — TryToCmyk returns null exactly as before.
    [Fact]
    public void Separation_SpotName_CalGrayAlternate_StillReverts()
    {
        PdfArray cs = new(new PdfName("Separation"), new PdfName("SpotX"),
            new PdfArray(new PdfName("CalGray"), new PdfDictionary()), Type2([1.0], [0.0]));
        PdfImage img = Image(cs, [0, 255], 2, 1);

        Assert.Null(PdfImageToCmyk.TryToCmyk(img, null, out _, out _));
    }

    // ---- Pass 2b-engine: an NChannel image's colorants split by ROLE and CHANNEL, not by name ----

    // THE DEFECT THIS TASK CLOSES. PrCyan is a non-reserved name listed in /Process /Components, so it is a
    // PROCESS colorant on channel 0. The name split calls it Spot (Classify sees an unreserved name) and
    // hands it a spot plane the registry will never hold, dropping the whole image to the whole-space
    // flatten. Per-component: its tint lands on the cyan plate and it is not a spot at all.
    [Fact]
    public void NChannel_nonReservedProcessName_paints_its_channel_not_a_spot_plane()
    {
        // NChannel [PrCyan, GWG Green] over DeviceCMYK with /Components [PrCyan].
        // 2 px: (PrCyan 1.0, Green 0.5) then (PrCyan 0, Green 1.0).
        byte[] data = [255, 128, 0, 255];
        PdfImage img = Image(NChannel(Process("DeviceCMYK", "PrCyan"), "PrCyan", "GWG Green"), data, 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);          // PrCyan is NOT a spot
        Assert.Equal(new byte[] { 128, 255 }, ink.TintPlanes);
        // PrCyan → process channel 0 = the CYAN plate, at its own per-pixel tint.
        Assert.Equal(new byte[] { 255, 0, 0, 0,  0, 0, 0, 0 }, ink.ProcessCmyk);
    }

    // Table 71 makes POSITION the channel identity, so a reserved name listed at a non-canonical index takes
    // the listed one. Routing by name instead would transpose the colour visibly — the same failure
    // veraPDF t02-pass-a is built to catch.
    [Fact]
    public void NChannel_listed_index_beats_the_reserved_name_canonical_index()
    {
        // /Components [/Black /Cyan] ⇒ Black is process channel 0 (cyan plate), Cyan is channel 1 (magenta).
        // 1 px, 3 colorants: Black 1.0, Cyan 0.0, GWG Green ~0.5.
        PdfImage img = Image(NChannel(Process("DeviceCMYK", "Black", "Cyan"), "Black", "Cyan", "GWG Green"),
            [255, 0, 128], 1, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 128 }, ink.TintPlanes);
        // Black's 255 on channel 0, Cyan's 0 on channel 1 — NOT K=255 as the name split would give.
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, ink.ProcessCmyk);
    }

    // A one-channel process space hands a listed name index 0, which is NOT the cyan plate. There is no
    // mapping from a gray channel to plates here, so the per-component split must refuse ENTIRELY and the
    // name split must handle it — Ink1 is unreserved, so it stays a spot exactly as before this task.
    [Fact]
    public void NChannel_over_a_gray_process_space_falls_back_to_the_name_split()
    {
        PdfImage img = Image(NChannel(Process("DeviceGray", "Ink1"), "Ink1", "GWG Green"),
            [255, 64, 128, 32], 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "Ink1", "GWG Green" }, ink!.Names);   // BOTH spots — the name split's answer
        Assert.All(ink.ProcessCmyk, b => Assert.Equal((byte)0, b));
    }

    // The governing principle: one unplaceable component falls the WHOLE op back, never a half-split. /All
    // means "every available colourant" and SpotImageInk cannot express that, so its presence disqualifies
    // the per-component split — and the name split's existing All arm (contributes nothing) takes over.
    [Fact]
    public void NChannel_containing_All_falls_back_to_the_name_split_whole()
    {
        PdfImage img = Image(NChannel(Process("DeviceCMYK", "Cyan"), "Cyan", "All", "GWG Green"),
            [255, 255, 128], 1, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);           // All contributes nothing, as today
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, ink.ProcessCmyk);// Cyan → plate 0 via the NAME split
    }

    // §8.6.6.5: /None components "shall never be painted on the page".
    //
    // PLAN DEFECT (Task 2, Step 2): the plan classifies this test as a regression anchor that "must already
    // pass", on the stated grounds that it gives the "same answer under both rules". It does NOT — its
    // PrCyan channel is the very shape the task fixes, so under the name split PrCyan is a Spot, Names
    // comes back ["PrCyan", "GWG Green"] and ProcessCmyk is all zero. Observed failing at Step 2 alongside
    // the two the plan predicted. What it genuinely pins is that /None contributes to NEITHER output on the
    // per-component path, so a future edit there cannot silently start painting it.
    [Fact]
    public void NChannel_None_component_contributes_nothing()
    {
        PdfImage img = Image(NChannel(Process("DeviceCMYK", "PrCyan"), "PrCyan", "None", "GWG Green"),
            [255, 255, 128], 1, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, ink.ProcessCmyk);  // None's 255 landed nowhere
    }

    // THE ALL-OR-NOTHING RULE, pinned. Added at Step 8 because Mutation B (ColorantPlacement.Build's
    // `if (c.ProcessChannel is not { } channel) return null;` arm changed to silently dropping the
    // component) left the WHOLE suite green: none of the plan's seven fixtures reaches that arm, so the
    // rule the task calls governing was unpinned.
    //
    // PrCyan is listed in /Components at index 4, which is at the four-channel process space's channel
    // count, so ProcessChannelFor rejects it as out of range and returns null while RoleFor still calls it
    // Process — a Process component with no determinable channel, the one shape that is neither placeable
    // nor droppable. The whole image must fall back to the name split, where PrCyan is an ordinary spot.
    // Under Mutation B it is dropped instead and Names comes back ["GWG Green"].
    [Fact]
    public void NChannel_processComponent_withNoDeterminableChannel_fallsBackWhole()
    {
        PdfImage img = Image(
            NChannel(Process("DeviceCMYK", "A", "B", "C", "D", "PrCyan"), "PrCyan", "GWG Green"),
            [255, 128], 1, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "PrCyan", "GWG Green" }, ink!.Names);   // the NAME split's answer, whole
        Assert.Equal(new byte[] { 255, 128 }, ink.TintPlanes);
        Assert.All(ink.ProcessCmyk, b => Assert.Equal((byte)0, b));
    }

    // THE OVERPRINT CATEGORY MUST NOT FLIP. An NChannel image whose components are ALL Process produces a
    // valid per-component split with an EMPTY spot list. Placing those components on their plates would be
    // more correct on colour and would leave spotNames empty, so TryToSpotInk's `spotNames.Count == 0`
    // would return null where it previously returned a two-plane SpotImageInk — and downstream that is not
    // a colour change: CmykPageRenderer picks InkSourceCategory.SeparationDeviceN iff Spots is non-null
    // (OverprintPlates is null for non-reserved names, so the category alone decides), so null Spots means
    // ProcessOther means "paint source on every process plate" means KNOCKOUT, and an overprinting image
    // erases a backdrop it used to preserve under Table 148 row 3.
    //
    // So the per-component split refuses a spotless split and the NAME split answers whole: PrCyan/PrMagenta
    // are unreserved, Classify calls them Spot, and this is byte-for-byte what the file did before Task 2.
    // The colour is wrong the same way it was already wrong; only the overprint category is protected.
    // Removing the `spotNames.Count == 0 return null` guard in THIS file (R3 stays verbatim at the per-
    // component call sites; ColorantPlacement.Build does not own this guard) must make this test fail on
    // Assert.NotNull.
    [Fact]
    public void NChannel_allProcess_image_keeps_the_name_splits_answer()
    {
        // 1 px, 2 colorants both listed in /Process /Components: PrCyan 1.0, PrMagenta ~0.5.
        PdfImage img = Image(NChannel(Process("DeviceCMYK", "PrCyan", "PrMagenta"), "PrCyan", "PrMagenta"),
            [255, 128], 1, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "PrCyan", "PrMagenta" }, ink!.Names);   // the NAME split's answer, whole
        Assert.Equal(new byte[] { 255, 128 }, ink.TintPlanes);
        Assert.All(ink.ProcessCmyk, b => Assert.Equal((byte)0, b));  // nothing on any plate
    }

    // The 50 unaffected GWG patches depend on this: a plain DeviceN carries Components == null, so the name
    // split is untouched and the output is byte-identical to before this task.
    [Fact]
    public void PlainDeviceN_is_unaffected_by_the_perComponent_split()
    {
        byte[] data = [255, 128, 0, 255];
        PdfImage img = Image(DeviceN("Black", "GWG Green"), data, 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 128, 255 }, ink.TintPlanes);
        Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 0 }, ink.ProcessCmyk);
    }

    // AXIS B GUARD. A corrupt indirect reference somewhere under the colour space must degrade to the name
    // split rather than throw out of TryToSpotInk.
    //
    // PLAN DEFECT (Task 2, Step 1): the plan's fixture puts the corrupt reference at /Attributes /Process
    // (`/Process 5 0 R`). That target is ALREADY guarded — SpotColorSpace.EnsureAttributes wraps the deref
    // of /Attributes and of its immediate /Subtype, /Colorants and /Process values, and on a throw resets
    // Subtype to "DeviceN". Nothing would ever reach ComponentSplit's catch, so the plan's fixture is
    // vacuous and Mutation C would not go red (verified empirically: with the catch removed, the
    // /Process 5 0 R shape still passes).
    //
    // The ALTERNATE (element 2) is the genuinely unguarded target on this path. SpotColorSpace.EnsureAlternate
    // derefs it with no try of its own — deliberately, because pre-Pass-1 callers already dereferenced it —
    // and OriginForColorSpaceObject reads AlternateSpaceName to construct the ColorantOrigin. TryToSpotInk
    // itself never touches element 2 (SP-6c: the alternate is not consulted), so this reference is resolved
    // for the FIRST time by the new per-component call, which is exactly what a call-site wrap has to cover.
    // Object 5's xref entry is IN USE but its body is a lone ']', so GetObject's on-demand path wraps and
    // RETHROWS PdfParseException; a merely NON-EXISTENT object returns null without throwing and would make
    // this test vacuous. Same technique and same object number as
    // NChannelRampTests.CorruptProcessComponentsReference_FallsBackToTheIsolatedEvaluation_RatherThanThrowing.
    [Fact]
    public void CorruptAlternateReference_fallsBackToTheNameSplit_ratherThanThrowing()
    {
        // /Components [/Black /Cyan] would put Black on channel 0 and Cyan on channel 1 if the resolve had
        // succeeded, so the assertion below distinguishes the NAME split's answer from the per-component
        // one rather than merely observing that something came back.
        (PdfArray space, PdfDocument doc) = ParseWithDoc(
            "[/DeviceN [/Black /Cyan /GWGGreen] 5 0 R "
            + "<< /FunctionType 2 /Domain [0 1 0 1 0 1] /C0 [0 0 0 0] /C1 [1 0 0 0] /N 1 "
            + "/Range [0 1 0 1 0 1 0 1] >> "
            + "<< /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components [/Black /Cyan] >> >>]",
            "]");
        using (doc)
        {
            PdfImage img = Image(space, [255, 0, 128], 1, 1);

            SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, doc, out _, out _);

            // Degrades to the NAME split: Black ⇒ the K plate (NOT channel 0), Cyan ⇒ plate 0 at tint 0,
            // GWGGreen ⇒ a spot plane.
            Assert.NotNull(ink);
            Assert.Equal(new[] { "GWGGreen" }, ink!.Names);
            Assert.Equal(new byte[] { 128 }, ink.TintPlanes);
            Assert.Equal(new byte[] { 0, 0, 0, 255 }, ink.ProcessCmyk);
        }
    }

    // --- G-14: all-reserved Separation/DeviceN image samples go straight to their plates ---

    private static PdfDictionary LyingMagentaTint()
    {
        // t → (0, t, 0, 0): a WRONG alternate for a /Cyan image. Direct routing must ignore it.
        var d = new PdfDictionary
        {
            [new PdfName("FunctionType")] = new PdfInteger(2),
            [new PdfName("Domain")] = new PdfArray(new PdfReal(0), new PdfReal(1)),
            [new PdfName("C0")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(0), new PdfReal(0)),
            [new PdfName("C1")] = new PdfArray(new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(0)),
            [new PdfName("N")] = new PdfReal(1),
        };
        return d;
    }

    [Fact]
    public void G14_ReservedSeparationImage_SamplesGoToItsPlate()
    {
        var cs = new PdfArray(new PdfName("Separation"), new PdfName("Cyan"),
            new PdfName("DeviceCMYK"), LyingMagentaTint());
        // Two pixels: tint 0.7 (178), tint 0 (0).
        PdfImage img = Image(cs, [178, 0], 2, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(178, cmyk![0]);  // C ← the sample, directly
        Assert.Equal(0, cmyk[1]);     // M — the lying alternate is ignored
        Assert.Equal(0, cmyk[2]);
        Assert.Equal(0, cmyk[3]);
        Assert.Equal(0, cmyk[4]); Assert.Equal(0, cmyk[5]); Assert.Equal(0, cmyk[6]); Assert.Equal(0, cmyk[7]);
    }

    [Fact]
    public void G14_ReservedDeviceNImage_PacksByName_HonoursDecode()
    {
        // [Black, Cyan] with /Decode [1 0  0 1]: Black's samples invert, Cyan's pass through.
        var tint = LyingMagentaTint();   // any transform — it must be IGNORED
        var cs = new PdfArray(new PdfName("DeviceN"),
            new PdfArray(new PdfName("Black"), new PdfName("Cyan")),
            new PdfName("DeviceCMYK"), tint);
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = cs,
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Decode")] = new PdfArray(
                new PdfReal(1), new PdfReal(0), new PdfReal(0), new PdfReal(1)),
        };
        var img = new PdfImage(new PdfStream(dict, [51, 102]));   // 0.2, 0.4

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        Assert.NotNull(cmyk);
        Assert.Equal(102, cmyk![0]);  // Cyan (names[1]) → C, decode identity: 0.4
        Assert.Equal(0, cmyk[1]);
        Assert.Equal(0, cmyk[2]);
        Assert.Equal(204, cmyk[3]);   // Black (names[0]) → K, decode [1 0]: 1−0.2 = 0.8
    }

    // Whole-branch final review, minor finding 3: a malformed DeviceN whose /Names array mixes a
    // non-PdfName element (here, an integer) in with real reserved names must NOT take the G-14 direct
    // route — SeparationNames drops the non-name element, silently understating the declared colourant
    // count and walking the sample data at the wrong stride. The guard requires the extracted name count
    // to equal the array's DECLARED count before taking the route; on a mismatch it falls through to the
    // pre-G-14 behaviour.
    [Fact]
    public void G14_MalformedDeviceNNamesArray_DoesNotTakeTheDirectRoute()
    {
        var cs = new PdfArray(new PdfName("DeviceN"),
            new PdfArray(new PdfName("Cyan"), new PdfInteger(5)),   // declared count 2, one non-name
            new PdfName("DeviceCMYK"), LyingMagentaTint());
        // Declared stride is 2 (the array's raw length), regardless of the dropped element.
        PdfImage img = Image(cs, [178, 0], 1, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        // Measured: the malformed space is refused by the direct route's count guard. Whatever the
        // fallback path ultimately does with it (decline outright, or run the tint transform on the
        // declared 2-component stride) is NOT this test's concern — what matters is that C did NOT
        // receive the raw sample 178/255 = 0.7 directly on its own plate, which is what the (buggy)
        // direct route would have produced for [/Cyan, <non-name>] read as ["Cyan"] alone.
        if (cmyk is not null)
            Assert.NotEqual(178, cmyk[0]);
    }

    [Fact]
    public void G14_StencilInk_AllReservedFill_ReturnsProcessOnlyInk()
    {
        // A stencil whose FILL is [/Separation /Cyan] tint 0.7 (unregistered). Pre-G-14 the
        // no-spot guard returned null and the stencil painted the fill's resolved ALTERNATE.
        var origin = new ColorantOrigin(["Cyan"], [0.7], "DeviceCMYK");

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 2, 2);

        Assert.NotNull(ink);
        Assert.Empty(ink!.Names);                 // no spot to route
        Assert.Empty(ink.TintPlanes);
        Assert.Equal(2 * 2 * 4, ink.ProcessCmyk.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(178, ink.ProcessCmyk[i * 4]);      // C = 0.7, directly
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 1]);    // M/Y/K untouched
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 2]);
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 3]);
        }
    }

    [Fact]
    public void G14_StencilInk_MixedUnregisteredFill_StillDeclines()
    {
        // NEGATIVE CONTROL, measured red-first: with names ["Cyan","PANTONE-X"], the name split
        // classifies PANTONE-X as spot (PageColorant.Classify), so spotNames.Count == 1 != 0 —
        // the guard never fires and the method proceeds down the EXISTING per-spot build below
        // it, not the new all-reserved branch. The point pinned here is only that the new branch
        // does not hijack this case: the spot ("PANTONE-X") still gets its own plane, unaffected
        // by G-14.
        var origin = new ColorantOrigin(["Cyan", "PANTONE-X"], [0.5, 0.5], "DeviceCMYK");

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 2, 2);

        Assert.NotNull(ink);
        Assert.Single(ink!.Names);
        Assert.Equal("PANTONE-X", ink.Names[0]);
        Assert.Equal(2 * 2, ink.TintPlanes.Length);
        Assert.All(ink.TintPlanes, b => Assert.Equal(128, b));   // 0.5 → round-to-even 128
        Assert.Equal(2 * 2 * 4, ink.ProcessCmyk.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(128, ink.ProcessCmyk[i * 4]);      // Cyan plate, 0.5 → 128
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 1]);
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 2]);
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 3]);
        }
    }
}
