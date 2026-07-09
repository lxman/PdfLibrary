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
        if (image.BitsPerComponent != 8 && !(isGray && image.BitsPerComponent is 1 or 16)) return null;

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
            Func<double[], (double C, double M, double Y, double K)>? tint =
                ColorSpaceResolver.BuildTintToCmyk(cs, document, out int inC);
            if (tint is null || inC < 1) return null;
            if (data.Length < px * inC) return null;

            var outSep = new byte[px * 4];
            var colorants = new double[inC];
            for (var i = 0; i < px; i++)
            {
                int src = i * inC;
                for (var c = 0; c < inC; c++) colorants[c] = data[src + c] / 255.0;
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
}
