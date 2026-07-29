using System.Linq;
using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Decodes an image XObject to <b>native DeviceCMYK</b> samples (W×H×4 bytes, 0..255), WITHOUT the
/// lossy CMYK→sRGB conversion <see cref="PdfImageToRgba"/> applies. A CMYK compositor uses these so an
/// image whose colour resolves to DeviceCMYK paints in native ink and matches adjacent DeviceCMYK vector
/// content pixel-for-pixel (a plain RGB round-trip re-injects gamut-mapped cyan and mismatches — GWG010
/// mask swatches). Returns null when the image's colour does not resolve to native ink (DeviceRGB,
/// ICCBased, Lab, spot colours with a non-CMYK alternate) — the caller then keeps using the RGBA path.
/// Scope: 8 bits/component (plus 1-bpc DeviceGray), non-image-mask. Covers direct DeviceCMYK (honouring
/// /Decode), DeviceGray/CalGray as K-only ink (honouring /Decode), Indexed with a DeviceCMYK / ICCBased-4
/// / Separation-or-DeviceN(CMYK-alternate) base, and direct Separation/DeviceN with a DeviceCMYK alternate.
/// Alpha (transparency / SMask) is NOT produced here — the caller pairs these colour samples with the
/// alpha channel from the RGBA decode.
/// </summary>
public static class PdfImageToCmyk
{
    /// <summary>
    /// The set of CMYK plates an image's colour space MARKS, for basic overprint (ISO 32000 §8.6.7):
    /// when the image overprints, the plates it does NOT mark are preserved. Returns null for device
    /// spaces (DeviceCMYK/Gray/RGB — marks all/implied process plates → knockout) and for any space
    /// carrying a spot colorant we can't map to a process plate (also knockout). A Separation/DeviceN
    /// resolving to process colorants — directly or through an Indexed base (e.g. a Black+Cyan duotone) —
    /// returns exactly the subset {C,M,Y,K} it marks. The overprint MODE (OPM) is never consulted for
    /// images, so the caller applies this mask only via the basic overprint (op) flag.
    /// </summary>
    public static (bool C, bool M, bool Y, bool K)? PlateMaskFor(PdfImage image, PdfDocument? document)
    {
        if (image.IsImageMask) return null;                 // stencil: colour (and overprint) comes from the fill
        return ColorSpaceResolver.PlatesForColorSpaceObject(image.ColorSpaceArray, document);
    }

    public static byte[]? TryToCmyk(PdfImage image, PdfDocument? document, out int width, out int height)
    {
        width = image.Width;
        height = image.Height;
        if (width <= 0 || height <= 0) return null;
        if (image.IsImageMask) return null;                 // stencil: colour comes from the fill, not the image

        // DeviceGray/CalGray separate to the black plate only (1/8/16 bpc all handled); every other space
        // is 8bpc-only below. ICCBased-gray is deliberately excluded (image.ColorSpace == "ICCBased"): its
        // samples carry a source profile and must go through the managed RGBA path, not raw ink. Grey is
        // handled at every depth so an 8-bit reference tile and a 16-bit test tile of the same image
        // separate to the SAME K path — the only residual is sample precision, not a colour-path split
        // (GWG183 overlays an 8-bit cross on a 16-bit photo).
        bool isGray = image.ColorSpace is "DeviceGray" or "CalGray";
        // 8-bit: every space. 16-bit: every space too — grey unpacks its own depth below; other spaces
        // down-convert to high bytes after decode (see below) so a 16-bit DeviceCMYK image builds a native
        // ink plane instead of the lossy CMYK→RGB→CMYK round-trip (GWG181). 1-bit: grey only. Anything else
        // (sub-byte non-grey) stays unsupported and falls to the managed RGBA path.
        if (image.BitsPerComponent is not (8 or 16) && !(isGray && image.BitsPerComponent == 1)) return null;

        byte[] data;
        try { data = image.GetDecodedData(); }
        catch { return null; }

        int px = width * height;

        // --- DeviceGray / CalGray: 1 gray sample/pixel → K-only native ink (C=M=Y=0, K = 1 − gray) ---
        // A grey image on a CMYK proof prints on the black plate alone, so image-grey then matches an
        // adjacent DeviceCMYK(0,0,0,k) box pixel-for-pixel instead of drifting through an RGB→CMYK gamut
        // round-trip (GWG173: the JBIG2 "X" vanishes when its solid-black tile lands on the same K as the
        // 0 0 0 1 k box behind it). Honours /Decode; unsupported grey depths fell through the guard above.
        if (isGray)
            return GrayToKPlane(data, width, height, image.BitsPerComponent, image.DecodeArray);

        // JPXDecode images reach us already colour-resolved by the JP2 filter — the palette (pclr) is
        // expanded and the colr box applied, so a CMYK JP2 is 4 interleaved ink bytes/pixel. A 4-channel
        // JPX decode is DeviceCMYK ink (JpxDecodeFilter), so take those bytes as the native plane directly.
        // The file may still declare a degenerate [/Indexed /DeviceCMYK 0 lookup] wrapper around the JP2's
        // own single-entry palette; running the Indexed branch below would treat the already-expanded CMYK
        // samples as palette indices and paint the GWG170 striped "X". Grey/RGB JPX carries no native CMYK
        // and stays on the RGBA path (ToRgba applies the source ICC for RGB).
        if (image.Filters.Contains("JPXDecode"))
            return data.Length >= px * 4 ? (data.Length == px * 4 ? data : data[..(px * 4)]) : null;

        // 16-bit non-grey samples: down-convert each big-endian sample to its high byte so the 8-bit ink
        // paths below build a native CMYK plane. Grey unpacked its own depth above; JPX carries its own and
        // returned above. The low byte is sub-perceptual and /Decode is linear, matching PdfImageToRgba.
        if (image.BitsPerComponent == 16)
        {
            var packed = new byte[data.Length / 2];
            for (int i = 0, j = 0; j < packed.Length; i += 2, j++) packed[j] = data[i];
            data = packed;
        }

        PdfArray? cs = image.ColorSpaceArray;

        // --- Indexed: 1 index byte/pixel → per-entry CMYK from the raw lookup ---
        if (cs is { Count: >= 4 } && cs[0] is PdfName { Value: "Indexed" })
        {
            if (data.Length < px) return null;
            PdfObject baseObj = Deref(cs[1], document);
            byte[]? lookup = ResolveLookup(cs[3], document);
            if (lookup is null) return null;

            Func<int, (double C, double M, double Y, double K)>? entry =
                BuildIndexedEntryToCmyk(baseObj, lookup, document);
            if (entry is null) return null;

            var outIdx = new byte[px * 4];
            for (var i = 0; i < px; i++)
            {
                (double c, double m, double y, double k) = entry(data[i]);
                int o = i * 4;
                outIdx[o] = B(c); outIdx[o + 1] = B(m); outIdx[o + 2] = B(y); outIdx[o + 3] = B(k);
            }
            return outIdx;
        }

        // --- Direct DeviceCMYK: 4 sample bytes/pixel ---
        if (ResolvesToDirectCmyk(image))
        {
            if (data.Length < px * 4) return null;
            double[]? dec = image.DecodeArray;
            bool applyDecode = dec is { Length: >= 8 } &&
                (dec[0] != 0 || dec[1] != 1 || dec[2] != 0 || dec[3] != 1 ||
                 dec[4] != 0 || dec[5] != 1 || dec[6] != 0 || dec[7] != 1);

            var outCmyk = new byte[px * 4];
            for (var i = 0; i < px * 4; i++)
            {
                byte s = data[i];
                if (!applyDecode) { outCmyk[i] = s; continue; }
                int comp = i & 3;                            // 0..3 → which /Decode pair
                double dmin = dec![comp * 2], dmax = dec[comp * 2 + 1];
                double v = dmin + s / 255.0 * (dmax - dmin);
                outCmyk[i] = B(v);
            }
            return outCmyk;
        }

        // --- Direct Separation/DeviceN with a DeviceCMYK alternate: N colorant bytes/pixel → tint → CMYK ---
        if (cs is { Count: >= 4 } && cs[0] is PdfName { Value: "Separation" or "DeviceN" })
        {
            // G-14: all-reserved colourants (± /None) — samples go straight to their plates;
            // the alternate + tint transform are ignored (§8.6.6.4 first clause; the fill/stroke
            // sibling is InkDecider's reserved-direct arm, the shading sibling is
            // ShadingBuilder.PackByReservedName). Indexed images over such a base are NOT routed
            // here (the Indexed branch above already returned) — recorded in the matrix.
            string[] rNames = SeparationNames(cs, document);
            if (rNames.Length >= 1 && ColorSpaceResolver.AllReservedProcessOrNone(rNames))
            {
                int rInC = rNames.Length;
                if (data.Length < px * rInC) return null;
                double[]? rDec = image.DecodeArray;
                bool rApplyDecode = rDec is not null && rDec.Length >= rInC * 2;
                var plateOf = new int[rInC];
                for (var c = 0; c < rInC; c++)
                    plateOf[c] = ColorSpaceResolver.ReservedChannelOf(rNames[c]) ?? -1;   // /None → −1

                var outR = new byte[px * 4];
                for (var i = 0; i < px; i++)
                {
                    int src = i * rInC, po = i * 4;
                    for (var c = 0; c < rInC; c++)
                    {
                        if (plateOf[c] < 0) continue;
                        double s = data[src + c] / 255.0;
                        if (rApplyDecode) s = rDec![2 * c] + s * (rDec[2 * c + 1] - rDec[2 * c]);
                        outR[po + plateOf[c]] = B(s);
                    }
                }
                return outR;
            }

            Func<double[], (double C, double M, double Y, double K)>? tint =
                ColorSpaceResolver.BuildTintToCmyk(cs, document, out int inC);
            if (tint is null || inC < 1) return null;
            if (data.Length < px * inC) return null;

            // Honour a non-identity /Decode (e.g. [1 0] to invert a spot ramp) per colorant before the
            // tint transform — exactly as the DeviceCMYK branch above and the RGBA path do. bd5823c fixed
            // this in PdfImageToRgba only; the CMYK-proof path landed here unfixed, so GWG 6.0/6.1 (and
            // GWG061) reference image b — a grayscale Separation JPEG with /Decode [1 0] — rendered as a
            // colour negative. Default [0 1 …] is a no-op.
            double[]? dec = image.DecodeArray;
            bool applyDecode = dec is not null && dec.Length >= inC * 2;

            var outSep = new byte[px * 4];
            var colorants = new double[inC];
            for (var i = 0; i < px; i++)
            {
                int src = i * inC;
                for (var c = 0; c < inC; c++)
                {
                    double s = data[src + c] / 255.0;
                    colorants[c] = applyDecode ? dec![2 * c] + s * (dec[2 * c + 1] - dec[2 * c]) : s;
                }
                (double cc, double mm, double yy, double kk) = tint(colorants);
                int o = i * 4;
                outSep[o] = B(cc); outSep[o + 1] = B(mm); outSep[o + 2] = B(yy); outSep[o + 3] = B(kk);
            }
            return outSep;
        }

        return null;
    }

    // DeviceGray/CalGray → K-only CMYK plane (W×H×4). Unpacks 1-, 8-, or 16-bpc samples to an 8-bit grey
    // (16-bpc keeps the big-endian high byte, matching PdfImageToRgba's down-convert; the low byte is
    // sub-perceptual), maps each through the /Decode interval, and separates grey g to (0,0,0,1−g).
    // Returns null when the decoded data is short (caller then keeps the RGBA path).
    private static byte[]? GrayToKPlane(byte[] data, int width, int height, int bpc, double[]? decode)
    {
        int px = width * height;
        // After the 16→8 high-byte reduction the sample range is 0..255, so /Decode is applied on that
        // 8-bit scale for every depth (1-bpc uses its own 0..1 range).
        int maxVal = bpc == 1 ? 1 : 255;
        double dMin = decode is { Length: >= 2 } ? decode[0] : 0.0;
        double dMax = decode is { Length: >= 2 } ? decode[1] : 1.0;

        var outGray = new byte[px * 4];
        int stride = (width * bpc + 7) / 8;
        if (data.Length < stride * height) return null;

        for (var y = 0; y < height; y++)
        {
            int rowBase = y * stride;
            for (var x = 0; x < width; x++)
            {
                int sample = bpc switch
                {
                    8 => data[rowBase + x],
                    16 => data[rowBase + x * 2],                      // big-endian high byte
                    _ => (data[rowBase + (x >> 3)] >> (7 - (x & 7))) & 1,
                };
                double g = dMin + (double)sample / maxVal * (dMax - dMin);
                int o = (y * width + x) * 4;
                // C=M=Y=0 already (fresh array); set K = 1 − grey.
                outGray[o + 3] = B(1.0 - g);
            }
        }
        return outGray;
    }

    // Per-palette-entry CMYK for an Indexed base. Handles DeviceCMYK (4 raw bytes) and Separation/DeviceN
    // with a CMYK alternate (tint transform). An ICCBased base is deliberately NOT handled: its palette
    // samples are in the source profile's space, not raw output CMYK, so it must go through the
    // source-profile-managed RGBA path (returning null here routes the whole image there — GWG130).
    private static Func<int, (double C, double M, double Y, double K)>? BuildIndexedEntryToCmyk(
        PdfObject baseObj, byte[] lookup, PdfDocument? document)
    {
        switch (baseObj)
        {
            case PdfName { Value: "DeviceCMYK" }:
                return e => Read4(lookup, e * 4);

            case PdfArray { Count: >= 4 } sep when sep[0] is PdfName { Value: "Separation" or "DeviceN" }:
            {
                Func<double[], (double C, double M, double Y, double K)>? tint =
                    ColorSpaceResolver.BuildTintToCmyk(sep, document, out int inC);
                if (tint is null || inC < 1) return null;
                return e =>
                {
                    var colorants = new double[inC];
                    int src = e * inC;
                    for (var c = 0; c < inC; c++) colorants[c] = src + c < lookup.Length ? lookup[src + c] / 255.0 : 0;
                    return tint(colorants);
                };
            }

            default:
                return null;
        }
    }

    // Only DEVICE CMYK samples are native output ink. An ICCBased-4 image carries a source ICC profile
    // that must be transformed to the output space — its samples are NOT raw output CMYK (treating them so
    // ignores the source profile, e.g. GWG130's red X). Such images fall through to the source-profile-managed
    // RGBA path (PdfImageToRgba). DeviceCMYK has no source profile, so its samples ARE output ink.
    private static bool ResolvesToDirectCmyk(PdfImage image) => image.ColorSpace == "DeviceCMYK";

    private static (double, double, double, double) Read4(byte[] lut, int o) =>
        o + 3 < lut.Length ? (lut[o] / 255.0, lut[o + 1] / 255.0, lut[o + 2] / 255.0, lut[o + 3] / 255.0)
                           : (0, 0, 0, 0);

    private static byte[]? ResolveLookup(PdfObject lookupObj, PdfDocument? document)
    {
        lookupObj = Deref(lookupObj, document);
        return lookupObj switch
        {
            PdfString s => s.Bytes,
            PdfStream st => st.GetDecodedData(document?.Decryptor),
            _ => null,
        };
    }

    private static PdfObject Deref(PdfObject obj, PdfDocument? document) =>
        obj is PdfIndirectReference r && document is not null ? document.ResolveReference(r) ?? obj : obj;

    private static byte B(double v) => (byte)Math.Round((v < 0 ? 0 : v > 1 ? 1 : v) * 255.0);

    // SP-6a: per-spot image ink. Splits a Separation/DeviceN (or Indexed-over-those) image's per-pixel
    // colorant tints — process colorants → the ProcessCmyk plane, spot colorants → their own tint plane —
    // so SP-2's combiner routes the spot to its plane and applies the spot's alternate ONCE (registry
    // ramp), never double-counting. Returns null when the image has no spot colorant (the existing
    // flattened Cmyk / RGBA path is used unchanged). Scope mirrors TryToCmyk: 8/16-bpc, non-image-mask,
    // DeviceCMYK-alternate spaces. Honours /Decode per colorant, matching TryToCmyk.
    //
    // Pass 2b-engine: the split is by per-component ROLE and CHANNEL (ISO 32000-2 §8.6.6.5, ColorantOrigin
    // .Components) when the space is NChannel over a FOUR-channel process space, and by colorant NAME
    // otherwise — a Separation and a plain DeviceN carry no per-component roles, so the name split is
    // still their whole answer. The preference is WHOLE-IMAGE either way: one component the per-component
    // rule cannot place falls the entire image back to the name split rather than producing a partial
    // split, which would silently drop a colorant and be strictly worse than the status quo.
    //
    // THERE IS A SECOND FALLBACK, and it fires when every component IS placeable: a split producing NO spot
    // colorant is refused (see SplitByPlacement's closing guard), so an ALL-PROCESS NChannel image takes the
    // name split too. That is a deliberate trade — better on colour, worse on overprint — and the overprint
    // side is the one with teeth. Read that guard before assuming an all-process NChannel image is
    // per-component-evaluated; it is not.
    public static SpotImageInk? TryToSpotInk(PdfImage image, PdfDocument? document, out int width, out int height)
    {
        width = image.Width; height = image.Height;
        if (width <= 0 || height <= 0 || image.IsImageMask) return null;
        if (image.BitsPerComponent is not (8 or 16)) return null;

        PdfArray? cs = image.ColorSpaceArray;
        if (cs is not { Count: >= 2 }) return null;

        // Resolve the colorant space (direct, or an Indexed base) → the Separation/DeviceN array + palette lookup.
        bool indexed = cs[0] is PdfName { Value: "Indexed" };
        if (indexed && cs.Count < 4) return null;
        byte[]? lookup = indexed ? ResolveLookup(cs[3], document) : null;
        if (indexed && lookup is null) return null;
        PdfObject sepObj = indexed ? Deref(cs[1], document) : cs;
        if (sepObj is not PdfArray sep || sep.Count < 4 ||
            sep[0] is not PdfName { Value: "Separation" or "DeviceN" }) return null;

        // SP-6c: the alternate is deliberately NOT consulted here. ISO 32000-1 §8.6.6.5 conditions reversion
        // on colorant AVAILABILITY — "Reversion shall occur only if at least one colour component (other than
        // None) is specified and is not available on the device" — never on what the alternate happens to be.
        // No split below DECIDES anything from the alternate's colour, and none evaluates the tint transform,
        // so a Lab/ICCBased alternate was never grounds to bail: it is the fallback for colorants we cannot
        // paint, not a trigger. Availability is decided downstream (the compositor's all-or-nothing routing
        // falls back to the flatten path for an unregistered spot), and `spotNames.Count == 0` below still
        // hands purely-process images to the CMYK/RGBA path.
        //
        // CORRECTED (Pass 2b-engine): "never READS the alternate" is no longer true, and the distinction is
        // load-bearing. ComponentSplit below calls OriginForColorSpaceObject, which reads
        // `space.AlternateSpaceName` to construct the ColorantOrigin — and that triggers
        // SpotColorSpace.EnsureAlternate's UNGUARDED Deref of colour-space array element 2. So this method
        // now DEREFERENCES the alternate even though it still never consults its colour. That dereference is
        // the whole reason ComponentSplit wraps at its call site, and the reason
        // CorruptAlternateReference_fallsBackToTheNameSplit_ratherThanThrowing exists. **Do not read the
        // sentence above as licence to remove that catch as dead defensive code** — removing it makes that
        // test throw an unhandled PdfParseException.
        //
        // This intentionally diverges from TryToCmyk, which SP-6a's final review had scope-matched to this
        // method. TryToCmyk genuinely NEEDS a DeviceCMYK alternate because it converts THROUGH it; this method
        // does not. Motivating case: GWG080's /Indexed /DeviceN [/Black /PANTONE 265 C /None ×3] with a /Lab
        // alternate, which flattened through Lab so Black and PANTONE were never painted as separations.
        string[] names = SeparationNames(sep, document);
        int inC = names.Length;
        if (inC == 0) return null;

        // Map each colorant → a process plate (0..3) or a spot-plane index. Prefer the per-component
        // answer (ISO 32000-2 §8.6.6.5) when the origin carries a placement table (NChannel over a
        // four-channel process space, no /All, every component placeable — ColorantPlacement.Build's
        // nullability rule); otherwise the colorant-NAME split, unchanged, which is what a Separation or
        // a plain DeviceN gets and is why the 50 non-NChannel GWG patches cannot move. Bail if no spot
        // colorant.
        int[] plate;
        int[] spotOf;
        List<string> spotNames;
        if (ComponentSplit(sepObj, document, inC) is { } split)
        {
            (plate, spotOf, spotNames) = split;
        }
        else
        {
            plate = new int[inC];
            spotOf = new int[inC];
            spotNames = [];
            for (var c = 0; c < inC; c++)
                if (PageColorant.Classify(names[c]) == ColorantKind.Spot)
                { plate[c] = -1; spotOf[c] = spotNames.Count; spotNames.Add(names[c]); }
                else { plate[c] = ProcessPlate(names[c]); spotOf[c] = -1; }
        }
        if (spotNames.Count == 0) return null;

        byte[] data;
        try { data = image.GetDecodedData(); } catch { return null; }
        if (image.BitsPerComponent == 16) data = HighBytes(data);   // match TryToCmyk's 16→8 high-byte reduction

        int px = width * height, spotN = spotNames.Count;
        if (indexed ? data.Length < px : data.Length < px * inC) return null;

        double[]? dec = image.DecodeArray;
        bool applyDecode = !indexed && dec is not null && dec.Length >= inC * 2;

        var process = new byte[px * 4];
        var planes = new byte[px * spotN];
        var colorants = new double[inC];
        for (var i = 0; i < px; i++)
        {
            if (indexed)
            {
                int e = data[i];
                for (var c = 0; c < inC; c++)
                { int s = e * inC + c; colorants[c] = s < lookup!.Length ? lookup[s] / 255.0 : 0; }
            }
            else
            {
                int src = i * inC;
                for (var c = 0; c < inC; c++)
                {
                    double s = data[src + c] / 255.0;
                    colorants[c] = applyDecode ? dec![2 * c] + s * (dec[2 * c + 1] - dec[2 * c]) : s;
                }
            }
            int po = i * 4;
            for (var c = 0; c < inC; c++)
            {
                byte v = B(colorants[c]);
                if (plate[c] >= 0) process[po + plate[c]] = v;
                else if (spotOf[c] >= 0) planes[i * spotN + spotOf[c]] = v;
                // else: All/None colorant — contributes nothing to the split (SP-6a spec).
            }
        }
        return new SpotImageInk(spotNames, planes, process);
    }

    // SP-6d: a stencil's ink, synthesized from the FILL.
    //
    // ISO 32000-1 §8.9.6.2 "Stencil Masking": an image mask's samples "designate places on the page that
    // should either be marked with the current colour or masked out (not marked at all). Areas that are
    // masked out retain their former contents. The effect is like applying paint in the current colour
    // through a cut-out stencil", and "The image dictionary shall not contain a ColorSpace entry".
    //
    // So a stencil provably has NO INK OF ITS OWN — TryToSpotInk returning null for it (above) is correct,
    // not an omission. Its ink is the current fill, which means the fill is also where its SPOT identity
    // lives. Without this, RecordingRenderTarget bakes only the fill's ALTERNATE into RGBA, the compositor
    // sees Spots == null, derives ProcessOther (Table 148 row 2 = "paint source on every process colorant")
    // and paints the alternate on all four plates — ERASING the backdrop the stencil was overprinting.
    // GWG020's "mask - i": GWG Green at 1% tint over a CMYK green, rendered as a white ✗ where Ghostscript
    // and Adobe both show uniform green.
    //
    // The planes are CONSTANT across the image on purpose: the stencil's shape already lives in the RGBA
    // alpha (masked-out ⇒ alpha 0 ⇒ never composited), which is exactly §8.9.6.2's "retain their former
    // contents". Cost is W*H*(N+4) bytes; the design spec's Option D is the escape hatch if that ever bites.
    //
    // The split is SP-6a's, deliberately identical (spot ⇒ its own plane; Cyan/Magenta/Yellow/Black ⇒
    // their plate at their own tint; All/None ⇒ nothing), so a stencil and an image of the same colorants
    // decide the same way. Pass 2b-engine preserved that invariant by changing BOTH together: this method
    // and TryToSpotInk now prefer the same per-component split (SplitByPlacement, gated on the origin
    // carrying a placement table — NChannel over a four-channel process space, no /All, every component
    // placeable — ColorantPlacement.Build's nullability rule) and fall back whole to the same name split
    // otherwise. That
    // shared decision is WHY both were changed in one step rather than the image path alone. The stencil
    // half of it is pinned by RecordingRenderTargetSpotTests' three NChannel-fill fixtures — added in the
    // Task 2 fix round, because until then this branch was reachable only from production and forcing it
    // off left the whole suite green.
    //
    // The shared fallback has TWO triggers, not one — see TryToSpotInk's note. Besides an unplaceable
    // component, a split yielding NO spot colorant is refused, so an ALL-PROCESS NChannel fill takes the
    // name split here too. That matters more for a stencil than for an image: returning null leaves
    // Spots == null, which is exactly the ProcessOther/backdrop-erasure failure the paragraph above
    // describes SP-6d closing. The guard is what keeps this method from re-opening it.
    internal static SpotImageInk? StencilInkFromFill(ColorantOrigin origin, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        int inC = origin.Names.Count;
        if (inC == 0) return null;

        // The same per-component preference as TryToSpotInk, so a stencil and an image of the same
        // colorants still decide the same way (SP-6d's stated invariant), applied when the origin
        // carries a placement table (NChannel over a four-channel process space, no /All, every
        // component placeable — ColorantPlacement.Build's nullability rule). No try/catch here and none
        // needed: Placement is a materialised value already built and guarded upstream, not a lazy
        // handle onto the document.
        int[] plate;
        int[] spotOf;
        List<string> spotNames;
        if (origin is { Placement: { } placement } && placement.Slots.Count == inC
            && SplitByPlacement(placement) is { } split)
        {
            (plate, spotOf, spotNames) = split;
        }
        else
        {
            plate = new int[inC];
            spotOf = new int[inC];
            spotNames = [];
            for (var c = 0; c < inC; c++)
                if (PageColorant.Classify(origin.Names[c]) == ColorantKind.Spot)
                { plate[c] = -1; spotOf[c] = spotNames.Count; spotNames.Add(origin.Names[c]); }
                else { plate[c] = ProcessPlate(origin.Names[c]); spotOf[c] = -1; }
        }
        if (spotNames.Count == 0)
        {
            // G-14: an ALL-RESERVED fill (± /None) is no longer "the RGBA path is fine" — that path
            // paints the fill's resolved ALTERNATE, and for a reserved-name separation the alternate
            // is ignored (§8.6.6.4 first clause). Return process-only ink: the plates directly from
            // the tints, no spot names, no planes. The compositor routes it with a zero-length spot
            // loop (CmykPageRenderer's image gate accepts empty Names for exactly this shape). Any
            // other process-only fill (e.g. a DeviceN of non-reserved process names without a
            // placement) still declines to the RGBA path, unchanged.
            if (!ColorSpaceResolver.AllReservedProcessOrNone(origin.Names)) return null;
            var cellR = new byte[4];
            for (var c = 0; c < inC; c++)
                if (plate[c] >= 0)
                    cellR[plate[c]] = B(c < origin.Tints.Count ? origin.Tints[c] : 0.0);
            int pxR = width * height;
            var processR = new byte[pxR * 4];
            for (var i = 0; i < pxR; i++)
            {
                int po = i * 4;
                processR[po] = cellR[0]; processR[po + 1] = cellR[1];
                processR[po + 2] = cellR[2]; processR[po + 3] = cellR[3];
            }
            return new SpotImageInk([], [], processR);
        }

        // One pixel's worth of ink, then replicated: the value is constant by construction.
        int spotN = spotNames.Count;
        var cell = new byte[4];
        var spotCell = new byte[spotN];
        for (var c = 0; c < inC; c++)
        {
            // Tints is allowed to be SHORTER than Names (see InkDecider.ProcessContribution's note); a
            // missing tint reads as 0 rather than throwing.
            byte v = B(c < origin.Tints.Count ? origin.Tints[c] : 0.0);
            if (plate[c] >= 0) cell[plate[c]] = v;
            else if (spotOf[c] >= 0) spotCell[spotOf[c]] = v;
            // else: All/None colorant — contributes nothing to the split (SP-6a).
        }

        int px = width * height;
        var process = new byte[px * 4];
        var planes = new byte[px * spotN];
        for (var i = 0; i < px; i++)
        {
            int po = i * 4;
            process[po] = cell[0]; process[po + 1] = cell[1];
            process[po + 2] = cell[2]; process[po + 3] = cell[3];
            int so = i * spotN;
            for (var s = 0; s < spotN; s++) planes[so + s] = spotCell[s];
        }
        return new SpotImageInk(spotNames, planes, process);
    }

    // Separation → the single colorant name; DeviceN → the /Names array.
    private static string[] SeparationNames(PdfArray sep, PdfDocument? document) =>
        Deref(sep[1], document) switch
        {
            PdfName one => [one.Value],
            PdfArray arr => arr.Select(x => Deref(x, document)).OfType<PdfName>().Select(p => p.Value).ToArray(),
            _ => [],
        };

    private static int ProcessPlate(string name) =>
        name switch { "Cyan" => 0, "Magenta" => 1, "Yellow" => 2, "Black" => 3, _ => -1 };

    // ISO 32000-2 §8.6.6.5, for images: "the components shall be evaluated individually". The name split
    // in TryToSpotInk/StencilInkFromFill is right for a Separation or a plain DeviceN — neither carries
    // per-component roles — but for an NChannel space it misroutes two shapes the name cannot see:
    //   * a NON-RESERVED process colorant (e.g. /PrCyan listed in /Process /Components) — Classify calls
    //     it Spot, it is handed a plane the registry never holds, and the whole image drops to the
    //     whole-space flatten with its tint on neither a plate nor a plane;
    //   * a reserved name listed at a NON-CANONICAL index — Table 71 makes position the channel
    //     identity, so /Components [/Black /Cyan] puts Black on channel 0, and routing by name would
    //     transpose the colour.
    //
    // G-7 Plan 4: the slot assignment comes from ColorantPlacement — computed once, where /Process is
    // read — rather than being re-derived here from Role/ProcessChannel. The /All and
    // unplaceable-component refusals live on ColorantPlacement.Build now (they make the table null, and
    // a null table never reaches this method); the one refusal that is SITE-LOCAL, not a placement
    // fact, is the no-spot guard below.
    private static (int[] Plate, int[] SpotOf, List<string> SpotNames)? SplitByPlacement(
        ColorantPlacement placement)
    {
        IReadOnlyList<ColorantSlot> slots = placement.Slots;
        var plate = new int[slots.Count];       // process → 0..3 ; otherwise -1
        var spotOf = new int[slots.Count];      // spot-plane index ; otherwise -1
        for (var c = 0; c < slots.Count; c++)
        {
            ColorantSlot slot = slots[c];
            (plate[c], spotOf[c]) = slot.Kind switch
            {
                ColorantSlotKind.Plate => (slot.Index, -1),
                ColorantSlotKind.Spot => (-1, slot.Index),
                // §8.6.6.5: /None "shall never be painted on the page". Contributes nothing to either
                // output — the same answer the name split's All/None arm gives.
                _ => (-1, -1),
            };
        }

        // NO SPOT IN THE SPLIT ⇒ REFUSE, and let the caller's name split have the whole op.
        // TryToSpotInk and StencilInkFromFill exist to separate spot ink from process ink; a split with
        // no spot in it has nothing for either to do. This is not a limitation of the per-component rule
        // — it routes the op to exactly where it went before Pass 2b-engine, and the two answers are NOT
        // interchangeable downstream.
        //
        // The shape: an NChannel space whose components are ALL Process, e.g. /Components
        // [/PrCyan /PrMagenta] with both names listed and neither reserved. Placing them on their plates
        // leaves SpotNames empty, both callers' `if (spotNames.Count == 0) return null;` fires, and
        // ImageCommand.Spots stays null. Pellucid's CmykPageRenderer (:1119) picks
        // InkSourceCategory.SeparationDeviceN iff Spots is non-null — OverprintPlates is null here,
        // because PlatesForColorSpaceObject yields nothing for a non-reserved colorant name, so the
        // category alone decides. Null Spots ⇒ ProcessOther ⇒ InkDecider's "paint source on every process
        // plate" (:201-205) ⇒ KNOCKOUT. Under overprint that erases a backdrop this op used to preserve
        // via Table 148 row 3's nonzero-markedness proxy (InkDecider:149) — the GWG020-class failure this
        // programme treats as a defect everywhere else.
        //
        // The name split instead calls those non-reserved names Spot, hands them spot planes the registry
        // will not hold, and the compositor flattens — but Spots stays non-null and the op stays in row 3.
        // Per-component placement would be MORE correct on colour and LESS correct on overprint, and the
        // overprint regression is the one with teeth. Colour-only is the conservative direction here.
        //
        // GAP, recorded deliberately: an all-process NChannel op is not per-component-evaluated.
        //
        // Placed in THIS method, not in ComponentSplit, because this is the single point both callers
        // share: StencilInkFromFill calls SplitByPlacement directly and would otherwise flip the same
        // category for a stencil — the exact inversion of the GWG020 backdrop erasure SP-6d closed.
        return placement.SpotNames.Count > 0
            ? (plate, spotOf, [.. placement.SpotNames]) : null;
    }

    // Resolves the space's per-component carrier for TryToSpotInk. Null whenever the per-component split
    // does not apply, which the caller reads as "use the name split".
    //
    // GATED ON A FOUR-CHANNEL PROCESS SPACE. ColourantComponent.ProcessChannel indexes the PROCESS
    // space's channels, not the plates: under a /DeviceGray process space a listed name also gets index
    // 0, and painting it on cyan would be a colour error the name split never made. Four channels or
    // fall back — the conservative direction, matching ProcessChannelFor's own refusal to guess.
    //
    // WRAPPED AT THIS CALL SITE, not per level. What each layer actually dereferences, traced rather than
    // assumed:
    //   * SpotColorSpace.TryParse (:196-221) derefs EAGERLY only the space object itself and element 1 —
    //     for DeviceN, every element of the names array. It never touches element 2 or element 3;
    //     minimumElements: 4 is an ARITY check ahead of any dereference, not a dereference of its own.
    //   * The alternate (element 2) is LAZY, behind EnsureAlternate, which has no try of its own.
    //     OriginForColorSpaceObject triggers it by reading AlternateSpaceName to build the ColorantOrigin
    //     (ColorSpaceResolver.cs:1003) — a resolution this file's TryToSpotInk path had never performed.
    //   * The tint transform (element 3) is never dereferenced on this path AT ALL: nothing in
    //     OriginForColorSpaceObject or BuildComponents reads TintTransformObject.
    //   * BuildComponents derefs /Attributes, /Process /ColorSpace (possibly an ICC stream) and every
    //     /Components element; it guards its own /Process subtree, and EnsureAttributes guards
    //     /Attributes.
    // The LAZINESS is what justifies wrapping here rather than at a named level: resolution happens when
    // a property is read, not when TryParse returns, so the depth reachable from this one call is
    // unbounded — and PdfDocument.GetObject wraps a corrupt on-demand object's parse failure in
    // PdfParseException. A call-site wrap covers arbitrary DEPTH rather than exactly the level it names —
    // the design rule that took Pass 2a-prime from four review rounds to one. Returning null is always
    // safe here: the caller keeps the flatten/RGBA path it used before this method existed.
    //
    // This catch is DEFENCE-IN-DEPTH FOR A PUBLIC METHOD, not the thing standing between the pipeline and
    // a crash. On the real render path a corrupt alternate throws one call EARLIER, unguarded:
    // RecordingRenderTarget.cs:139 calls TryToCmyk before :149's TryToSpotInk, and TryToCmyk (:141-144)
    // hands the same array to ColorSpaceResolver.BuildTintToCmyk, which reads space.AlternateSpaceName
    // (ColorSpaceResolver.cs:475) and so triggers the same EnsureAlternate deref with no catch anywhere
    // above it. That is PRE-EXISTING and deliberately not fixed here — out of scope for Pass 2b-engine,
    // recorded as a gap. What this catch does buy is that TryToSpotInk, which is public and reachable
    // without TryToCmyk having run first, degrades rather than throwing.
    private static (int[] Plate, int[] SpotOf, List<string> SpotNames)? ComponentSplit(
        PdfObject spaceObj, PdfDocument? document, int nameCount)
    {
        try
        {
            if (ColorSpaceResolver.OriginForColorSpaceObject(spaceObj, rawColor: null, document)
                is not { Placement: { } placement }) return null;
            // Slots is index-aligned with the names array by construction; a disagreement means the
            // table is not describing this space. Refuse rather than index across them.
            return placement.Slots.Count == nameCount ? SplitByPlacement(placement) : null;
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, () =>
                $"TryToSpotInk: per-component split threw, falling back to the colorant-name split: {ex}");
            return null;
        }
    }

    // 16-bit big-endian samples → their high byte (sub-perceptual low byte dropped), matching TryToCmyk.
    private static byte[] HighBytes(byte[] data)
    { var p = new byte[data.Length / 2]; for (int i = 0, j = 0; j < p.Length; i += 2, j++) p[j] = data[i]; return p; }
}
