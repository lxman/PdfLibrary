using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;

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

    // A minimal Separation/DeviceN colour space. The tint-function slot is never evaluated by TryToSpotInk
    // (it splits by colorant NAME), so a bare name placeholder suffices.
    private static PdfArray Separation(string name) =>
        new(new PdfName("Separation"), new PdfName(name), new PdfName("DeviceCMYK"), new PdfName("Identity"));
    private static PdfArray DeviceN(params string[] names) =>
        new(new PdfName("DeviceN"), new PdfArray(names.Select(n => (PdfObject)new PdfName(n)).ToArray()),
            new PdfName("DeviceCMYK"), new PdfName("Identity"));

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
}
