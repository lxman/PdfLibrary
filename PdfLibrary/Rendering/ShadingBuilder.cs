using System.Numerics;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Functions;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds a <see cref="ShadingDescriptor"/> from a PDF shading dictionary — the /Shading resource
/// painted by the <c>sh</c> operator, or the /Shading inside a PatternType 2 pattern. Only axial
/// (type 2) and radial (type 3) shadings are produced; the colour ramp is pre-sampled by evaluating
/// the shading's /Function across its /Domain. Returns null for shading types/functions we don't
/// model yet, so the caller can skip the paint cleanly.
/// </summary>
internal static class ShadingBuilder
{
    private const int StopCount = 64;

    public static ShadingDescriptor? Build(PdfObject? shadingObj, PdfDocument? document, Matrix3x2? patternMatrix = null)
    {
        if (shadingObj is PdfIndirectReference iref && document is not null)
            shadingObj = document.ResolveReference(iref);

        PdfDictionary? dict = shadingObj switch
        {
            PdfStream stream => stream.Dictionary,
            PdfDictionary d => d,
            _ => null
        };
        if (dict is null) return null;

        if (!dict.TryGetValue(new PdfName("ShadingType"), out PdfObject typeObj) || typeObj is not PdfInteger typeInt)
            return null;
        int shadingType = typeInt.Value;
        if (shadingType is not (2 or 3)) return null; // axial / radial only

        double[]? coords = GetNumbers(dict, "Coords", document);
        if (coords is null) return null;
        if (shadingType == 2 && coords.Length < 4) return null;
        if (shadingType == 3 && coords.Length < 6) return null;

        if (!dict.TryGetValue(new PdfName("Function"), out PdfObject funcObj)) return null;
        List<PdfFunction> functions = ResolveFunctions(funcObj, document);
        if (functions.Count == 0) return null; // e.g. a type 3 stitching / type 4 function we can't evaluate yet

        double[] domain = GetNumbers(dict, "Domain", document) ?? [0.0, 1.0];
        double t0 = domain.Length > 0 ? domain[0] : 0.0;
        double t1 = domain.Length > 1 ? domain[1] : 1.0;

        // The /Function outputs colour components in the shading's /ColorSpace; map those to sRGB the
        // same way fills do (Separation/DeviceN through their tint transform), so spot-colour ramps
        // don't collapse to the naive by-component-count guess.
        PdfObject? shadingCs = dict.TryGetValue(new PdfName("ColorSpace"), out PdfObject csObj) ? csObj : null;
        Func<double[], uint> toColor = BuildColorMapper(shadingCs, document);
        // Native CMYK mapper (for CMYK-resolving spaces) + the colorant→plate overprint mask, so a CMYK
        // compositor can paint the gradient in native ink and preserve non-painted plates on overprint.
        Func<double[], uint>? toCmyk = BuildCmykMapper(shadingCs, document);
        (bool C, bool M, bool Y, bool K)? overprintPlates =
            ColorSpaceResolver.PlatesForColorSpaceObject(shadingCs, document);

        bool extendStart = false, extendEnd = false;
        if (dict.TryGetValue(new PdfName("Extend"), out PdfObject extObj) && extObj is PdfArray { Count: >= 2 } extArr)
        {
            extendStart = extArr[0] is PdfBoolean { Value: true };
            extendEnd = extArr[1] is PdfBoolean { Value: true };
        }

        var stops = new float[StopCount];
        var colors = new uint[StopCount];
        uint[] cmykColors = toCmyk is null ? [] : new uint[StopCount];
        for (var i = 0; i < StopCount; i++)
        {
            double s = i / (double)(StopCount - 1);
            double t = t0 + (t1 - t0) * s;
            double[] components = EvaluateColor(functions, t);
            colors[i] = toColor(components);
            if (toCmyk is not null) cmykColors[i] = toCmyk(components);
            stops[i] = (float)s;
        }

        return new ShadingDescriptor
        {
            ShadingType = shadingType,
            Coords = Array.ConvertAll(coords, x => (float)x),
            ExtendStart = extendStart,
            ExtendEnd = extendEnd,
            Stops = stops,
            Colors = colors,
            CmykColors = cmykColors,
            OverprintPlates = overprintPlates,
            PatternMatrix = patternMatrix
        };
    }

    // Builds a "function output → 0xCCMMYYKK (native CMYK bytes)" mapper for shading colour spaces that
    // resolve to DeviceCMYK: DeviceCMYK / ICCBased-4 pass their 4 components through; Separation/DeviceN
    // with a DeviceCMYK alternate run their tint transform to CMYK. Returns null otherwise (the compositor
    // then falls back to the sRGB stops).
    private static Func<double[], uint>? BuildCmykMapper(PdfObject? csObj, PdfDocument? document)
    {
        if (csObj is PdfIndirectReference r && document is not null)
            csObj = document.ResolveReference(r);

        switch (csObj)
        {
            case PdfName { Value: "DeviceCMYK" }:
                return PackCmyk;
            case PdfArray { Count: >= 1 } arr when arr[0] is PdfName head:
                switch (head.Value)
                {
                    case "ICCBased" when IccComponents(arr, document) == 4:
                        return PackCmyk;
                    case "Separation" or "DeviceN":
                        Func<double[], (double C, double M, double Y, double K)>? tint =
                            ColorSpaceResolver.BuildTintToCmyk(arr, document, out _);
                        if (tint is not null)
                            return c => { (double cc, double mm, double yy, double kk) = tint(c); return PackCmyk([cc, mm, yy, kk]); };
                        break;
                }
                break;
        }
        return null;
    }

    private static uint PackCmyk(double[] c) =>
        ((uint)Clamp255(c.Length > 0 ? c[0] : 0) << 24) | ((uint)Clamp255(c.Length > 1 ? c[1] : 0) << 16) |
        ((uint)Clamp255(c.Length > 2 ? c[2] : 0) << 8) | (uint)Clamp255(c.Length > 3 ? c[3] : 0);

    // A shading's /Function is either a single n-output function, or an array of n single-output
    // functions (one per colour component).
    private static List<PdfFunction> ResolveFunctions(PdfObject? funcObj, PdfDocument? document)
    {
        if (funcObj is PdfIndirectReference r && document is not null)
            funcObj = document.ResolveReference(r);

        var list = new List<PdfFunction>();
        if (funcObj is PdfArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var f = PdfFunction.Create(arr[i], document);
                if (f is not null) list.Add(f);
            }
        }
        else
        {
            var f = PdfFunction.Create(funcObj, document);
            if (f is not null) list.Add(f);
        }
        return list;
    }

    private static double[] EvaluateColor(List<PdfFunction> functions, double t)
    {
        if (functions.Count == 1)
            return functions[0].Evaluate([t]);

        var outv = new double[functions.Count];
        for (var i = 0; i < functions.Count; i++)
        {
            double[] r = functions[i].Evaluate([t]);
            outv[i] = r.Length > 0 ? r[0] : 0.0;
        }
        return outv;
    }

    // Builds a "function output → 0xFFRRGGBB" mapper for the shading's /ColorSpace. Device/Cal/Lab
    // resolve by name through PdfColorToRgb (matching fills); ICCBased maps by its /N; Separation and
    // DeviceN run their tint transform via ColorSpaceResolver.BuildTintToRgb. Anything unrecognised
    // falls back to the by-component-count guess.
    private static Func<double[], uint> BuildColorMapper(PdfObject? csObj, PdfDocument? document)
    {
        if (csObj is PdfIndirectReference r && document is not null)
            csObj = document.ResolveReference(r);

        switch (csObj)
        {
            case PdfName name:
                return c => Pack(PdfColorToRgb.ToRgb(c, name.Value));

            case PdfArray { Count: >= 1 } arr when arr[0] is PdfName head:
                switch (head.Value)
                {
                    case "ICCBased":
                        string icc = IccComponents(arr, document) switch
                        {
                            1 => "DeviceGray",
                            4 => "DeviceCMYK",
                            _ => "DeviceRGB"
                        };
                        return c => Pack(PdfColorToRgb.ToRgb(c, icc));
                    case "CalGray" or "CalRGB" or "Lab":
                        return c => Pack(PdfColorToRgb.ToRgb(c, head.Value));
                    case "Separation" or "DeviceN":
                        Func<double[], (byte R, byte G, byte B)>? tint =
                            ColorSpaceResolver.BuildTintToRgb(arr, document, out _);
                        if (tint is not null) return c => Pack(tint(c));
                        break;
                }
                break;
        }

        return ToArgbByCount;
    }

    // Component count of an [/ICCBased stream] alternate, read from the stream's /N (defaults to 3).
    private static int IccComponents(PdfArray arr, PdfDocument? document)
    {
        if (arr.Count < 2) return 3;
        PdfObject obj = arr[1];
        if (obj is PdfIndirectReference r && document is not null) obj = document.ResolveReference(r) ?? obj;
        if (obj is PdfStream s && s.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj) && nObj is PdfInteger n)
            return n.Value;
        return 3;
    }

    // Fallback for an unknown/absent colour space: infer the model from the component count —
    // 1 = gray, 4 = CMYK, otherwise the first three components as RGB.
    private static uint ToArgbByCount(double[] c)
    {
        double r, g, b;
        switch (c.Length)
        {
            case 1:
                r = g = b = c[0];
                break;
            case 4:
                double k = c[3];
                r = (1.0 - c[0]) * (1.0 - k);
                g = (1.0 - c[1]) * (1.0 - k);
                b = (1.0 - c[2]) * (1.0 - k);
                break;
            default: // 3 (or anything else): first three components as RGB
                r = c.Length > 0 ? c[0] : 0.0;
                g = c.Length > 1 ? c[1] : 0.0;
                b = c.Length > 2 ? c[2] : 0.0;
                break;
        }
        return 0xFF000000u | ((uint)Clamp255(r) << 16) | ((uint)Clamp255(g) << 8) | (uint)Clamp255(b);
    }

    private static uint Pack((byte R, byte G, byte B) c) =>
        0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static int Clamp255(double v) => v <= 0.0 ? 0 : v >= 1.0 ? 255 : (int)Math.Round(v * 255.0);

    private static double[]? GetNumbers(PdfDictionary dict, string key, PdfDocument? document)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject? obj)) return null;
        if (obj is PdfIndirectReference r && document is not null) obj = document.ResolveReference(r);
        if (obj is not PdfArray arr) return null;
        var nums = new double[arr.Count];
        for (var i = 0; i < arr.Count; i++) nums[i] = arr[i].ToDouble();
        return nums;
    }
}
