using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Functions;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Resolves PDF color spaces to device color spaces (DeviceRGB, DeviceCMYK, DeviceGray)
/// Handles ICCBased, Separation, and other color space conversions
/// </summary>
internal class ColorSpaceResolver(PdfDocument? document)
{
    private readonly IccColorConverter _iccConverter = new(document);
    private readonly ProofCmykResolver _proofResolver = new(document);

    /// <summary>
    /// Diagnostic counter for the G-12 throughput hook (Docs/colour/rendering-conformance.md):
    /// incremented once per ResolveColorSpace entry, read by PdfRenderer.ColorSpaceResolveCount.
    /// One resolver instance per PdfRenderer, so no thread-safety needed.
    /// </summary>
    internal int ResolveCallCount { get; private set; }

    /// <summary>
    /// Resolves a named color space from resources to a device color space
    /// </summary>
    /// <param name="colorSpaceName">The color space name (may be modified to device color space)</param>
    /// <param name="color">The color components (may be modified based on color space conversion)</param>
    /// <param name="colorSpaces">The ColorSpace resource dictionary</param>
    /// <param name="blackPointCompensation">When true, ICC conversions apply black-point compensation (PDF 2.0 /UseBlackPtComp).</param>
    /// <param name="renderingIntent">PDF rendering-intent name (ri / RI) selecting the ICC intent for ICC conversions; null = relative colorimetric.</param>
    public void ResolveColorSpace(ref string? colorSpaceName, ref List<double>? color, PdfDictionary? colorSpaces, bool blackPointCompensation = false, string? renderingIntent = null)
    {
        ResolveColorSpace(ref colorSpaceName, ref color, colorSpaces, blackPointCompensation, renderingIntent, out _);
    }

    /// <summary>
    /// Convenience overload for callers that only need the proof-CMYK out value with default
    /// black-point-compensation/rendering-intent behaviour.
    /// </summary>
    public void ResolveColorSpace(ref string? colorSpaceName, ref List<double>? color, PdfDictionary? colorSpaces, out double[]? proofCmyk)
    {
        ResolveColorSpace(ref colorSpaceName, ref color, colorSpaces, false, null, out proofCmyk);
    }

    /// <param name="colorSpaceName">The color space name (may be modified to device color space)</param>
    /// <param name="color">The color components (may be modified based on color space conversion)</param>
    /// <param name="colorSpaces">The ColorSpace resource dictionary</param>
    /// <param name="blackPointCompensation">When true, ICC conversions apply black-point compensation (PDF 2.0 /UseBlackPtComp).</param>
    /// <param name="renderingIntent">PDF rendering-intent name (ri / RI) selecting the ICC intent for ICC conversions; null = relative colorimetric.</param>
    /// <param name="proofCmyk">
    /// Proof-target CMYK (0..1 ×4) for the resolved colour when the source was device-independent
    /// (ICCBased N≥3 / Lab) and the proof destination resolved; null otherwise.
    /// </param>
    public void ResolveColorSpace(ref string? colorSpaceName, ref List<double>? color, PdfDictionary? colorSpaces, bool blackPointCompensation, string? renderingIntent, out double[]? proofCmyk)
    {
        proofCmyk = null;
        ResolveCallCount++;

        if (string.IsNullOrEmpty(colorSpaceName))
            return;

        // Ensure the color list exists
        color ??= [];

        // Skip device color spaces - they don't need resolution
        if (colorSpaceName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK")
        {
            return;
        }

        // Try to resolve named color space from resources
        if (colorSpaces is null)
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: No ColorSpace dict in resources for '{colorSpaceName}'");
            return;
        }

        if (!colorSpaces.TryGetValue(new PdfName(colorSpaceName), out PdfObject? csObj))
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: ColorSpace '{colorSpaceName}' not found in dict");
            return;
        }

        // Resolve indirect reference
        if (csObj is PdfIndirectReference reference && document is not null)
        {
            csObj = document.ResolveReference(reference);
        }

        switch (csObj)
        {
            // Handle single-element Pattern array: [/Pattern]
            case PdfArray { Count: 1 } singleArray when singleArray[0] is PdfName singleName && singleName.Value == "Pattern":
                colorSpaceName = "Pattern";
                PdfLogger.Log(LogCategory.Graphics, "RESOLVE: Pattern color space detected (uncolored)");
                return;
            // Handle Pattern with underlying color space: [/Pattern /DeviceRGB] or [/Pattern [/ICCBased ...]]
            case PdfArray { Count: >= 2 } patternArray when patternArray[0] is PdfName patternName && patternName.Value == "Pattern":
                colorSpaceName = "Pattern";
                if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Pattern color space detected (colored, underlying={patternArray[1]})");
                return;
        }

        // Parse color space array
        // Can be: [/ICCBased stream] or [/Separation name alternateSpace tintTransform]
        if (csObj is not PdfArray { Count: >= 2 } csArray || csArray[0] is not PdfName csType)
        {
            return;
        }

        switch (csType.Value)
        {
            case "ICCBased" when csArray.Count >= 2:
                ResolveICCBased(csArray, ref colorSpaceName, ref color, out proofCmyk, blackPointCompensation, renderingIntent);
                break;

            case "Separation" when csArray.Count >= 4:
                ResolveSeparation(csArray, ref colorSpaceName, ref color);
                break;

            case "DeviceN" when csArray.Count >= 4:
                ResolveDeviceN(csArray, ref colorSpaceName, ref color);
                break;

            case "Indexed" when csArray.Count >= 4:
                ResolveIndexed(csArray, ref colorSpaceName, ref color);
                break;

            case "Lab" when csArray.Count >= 2:
                ResolveLab(csArray, ref colorSpaceName, ref color, out proofCmyk);
                break;

            case "CalRGB" when csArray.Count >= 2:
                ResolveCalRgb(csArray, ref colorSpaceName, ref color);
                break;

            case "CalGray" when csArray.Count >= 2:
                ResolveCalGray(csArray, ref colorSpaceName, ref color);
                break;

            case "Pattern":
                // Pattern color space - don't resolve to device colors
                colorSpaceName = "Pattern";
                PdfLogger.Log(LogCategory.Graphics, "RESOLVE: Pattern color space detected");
                break;
        }
    }

    /// <summary>
    /// Resolves an ICCBased color space: [/ICCBased stream]
    /// </summary>
    private void ResolveICCBased(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color, out double[]? proofCmyk, bool blackPointCompensation = false, string? renderingIntent = null)
    {
        proofCmyk = null;
        color ??= [];

        // Get the ICC profile stream
        PdfObject? streamObj = csArray[1];
        if (streamObj is PdfIndirectReference streamRef && document is not null)
            streamObj = document.ResolveReference(streamRef);

        if (streamObj is not PdfStream iccStream) return;

        // Get stream dictionary to find alternate color space and number of components
        PdfDictionary streamDict = iccStream.Dictionary;

        // Get /N (number of components): 1=Gray, 3=RGB, 4=CMYK
        var numComponents = 1;
        if (streamDict.TryGetValue(new PdfName("N"), out PdfObject nObj) && nObj is PdfInteger nNum)
        {
            numComponents = nNum.Value;
        }

        if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: ICCBased '{colorSpaceName}': N={numComponents}, current color has {color.Count} components, color=[{string.Join(", ", color.Select(c => c.ToString("F2")))}]");

        // Get /Alternate color space (fallback when the ICC transform can't be built).
        string? alternateSpace = null;
        if (streamDict.TryGetValue(new PdfName("Alternate"), out PdfObject altObj))
        {
            if (altObj is PdfName altName)
            {
                alternateSpace = altName.Value;
            }
        }

        // Initialize default colors if the component count doesn't match so that downstream
        // calls have a well-formed tuple to work with (whether or not the ICC path succeeds).
        if (color.Count != numComponents)
        {
            color = numComponents switch
            {
                1 => [0.0],
                3 => [0.0, 0.0, 0.0],
                4 => [0.0, 0.0, 0.0, 1.0],
                _ => [0.0]
            };
        }

        // Try to transform the color through the embedded ICC profile to device-sRGB. If that
        // works, we replace the color tuple with the sRGB equivalent and treat the result as
        // DeviceRGB. On failure we fall back to the /Alternate path.
        double[]? srgb = _iccConverter.TryConvertToSrgb(iccStream, color, blackPointCompensation, renderingIntent);
        if (srgb is not null)
        {
            proofCmyk = _proofResolver.TryIccToProofCmyk(iccStream, color);
            color = [srgb[0], srgb[1], srgb[2]];
            colorSpaceName = "DeviceRGB";
            return;
        }

        if (alternateSpace is not null)
        {
            colorSpaceName = alternateSpace;
        }
        else
        {
            // No alternate specified, infer from component count.
            colorSpaceName = numComponents switch
            {
                1 => "DeviceGray",
                3 => "DeviceRGB",
                4 => "DeviceCMYK",
                _ => "DeviceGray"
            };
        }
    }

    /// <summary>
    /// Resolves a Separation color space: [/Separation name alternateSpace tintTransform]
    /// </summary>
    private void ResolveSeparation(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];

        // Get the colorant name (index 1)
        string colorantName = csArray[1] is PdfName cn ? cn.Value : "Unknown";

        // ISO 32000-2 §8.6.6.4: for the reserved names /All and /None, "PDF processors shall ignore the
        // alternateSpace and tintTransform parameters" — so both are handled before either is read.
        //
        // /All applies the tint to every available colourant at once. On an additive device (our RGB
        // display path) the clause requires the subtractive tint to "be complemented by subtracting from
        // 1 before applying to all available colourants": the colourants are R, G and B, so tint t paints
        // the neutral 1 − t — tint 1 (maximum colourant) is black, tint 0 is white.
        //
        // /None resolves to no colour at all: painting is suppressed upstream by PdfGraphicsState's
        // FillPaintsNothing/StrokePaintsNothing, so whatever value sits here is never marked on the page.
        // It is deliberately left at the caller's value rather than forced to white — a white fill still
        // marks the page, and would hide a suppression regression behind a white-on-white coincidence.
        switch (colorantName)
        {
            case "All":
            {
                double allTint = color.Count == 1 ? Math.Clamp(color[0], 0.0, 1.0) : 1.0;
                double v = 1.0 - allTint;
                color = [v, v, v];
                colorSpaceName = "DeviceRGB";
                if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                    PdfLogger.Log(LogCategory.Graphics,
                        $"RESOLVE Separation /All: tint={allTint:F3} -> complement {v:F3} on all colourants");
                return;
            }
            case "None":
                PdfLogger.Log(LogCategory.Graphics, "RESOLVE Separation /None: paints nothing (§8.6.6.4)");
                return;
        }

        // Get the alternate color space (index 2) - can be PdfName or PdfArray
        PdfObject? alternateObj = csArray[2];
        if (alternateObj is PdfIndirectReference altRef && document is not null)
            alternateObj = document.ResolveReference(altRef);

        string altSpace;

        switch (alternateObj)
        {
            case PdfName altName:
                altSpace = altName.Value;
                break;
            case PdfArray altArray when altArray.Count >= 1 && altArray[0] is PdfName altArrayType:
                // Handle array-based alternate spaces like [/CalRGB <<...>>], [/CalGray <<...>>], [/Lab <<...>>]
                altSpace = altArrayType.Value;
                break;
            default:
                // Unknown alternate space format
                return;
        }

        // Get the tint transform function (index 3)
        PdfObject tintTransformObj = csArray[3];
        PdfFunction? tintTransform = document is not null
            ? PdfFunction.Create(tintTransformObj, document)
            : null;

        if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Separation: colorant='{colorantName}', altSpace='{altSpace}', tintTransform={tintTransform?.GetType().Name ?? "NULL"}, color.Count={color.Count}");

        if (color.Count == 1)
        {
            double tint = color[0];

            // Try to use the tint transform function
            if (tintTransform is not null)
            {
                double[] result = tintTransform.Evaluate([tint]);
                if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Separation '{colorantName}' -> tint={tint:F3} -> function result=[{string.Join(", ", result.Select(r => r.ToString("F3")))}]");
                ApplyTintTransformResult(result, altSpace, alternateObj, ref colorSpaceName, ref color);
            }
            else
            {
                // Fallback: simple heuristic when tint transform not available
                if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Separation '{colorantName}' -> using fallback (no tint transform), tint={tint:F3}");
                ApplySeparationFallback(colorantName, altSpace, tint, ref colorSpaceName, ref color);
            }
        }
    }

    /// <summary>
    /// Resolves a DeviceN color space: <c>[/DeviceN [names] alternateSpace tintTransform]</c> — the
    /// N-colorant generalisation of Separation. Evaluates the tint transform over all N tint components
    /// into the alternate space, then flattens to a device colour (same result handling as Separation).
    /// Without this, a DeviceN fill reached the renderer unresolved (its resource name + raw tints), and
    /// the CMYK compositor mis-mapped it (e.g. a 2-tint value read as no ink → white). GWG190/191/192.
    /// </summary>
    private void ResolveDeviceN(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];
        if (color.Count == 0) return;

        PdfObject? alternateObj = csArray[2];
        if (alternateObj is PdfIndirectReference altRef && document is not null)
            alternateObj = document.ResolveReference(altRef);

        string altSpace = alternateObj switch
        {
            PdfName altName => altName.Value,
            PdfArray { Count: >= 1 } altArray when altArray[0] is PdfName altType => altType.Value,
            _ => string.Empty,
        };
        if (altSpace.Length == 0) return;

        PdfFunction? tintTransform = document is not null ? PdfFunction.Create(csArray[3], document) : null;
        if (tintTransform is null) return;   // DeviceN requires a tint transform to flatten to the alternate

        double[] result = tintTransform.Evaluate(color.ToArray());
        if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE DeviceN: altSpace='{altSpace}', tints={color.Count} -> result=[{string.Join(", ", result.Select(r => r.ToString("F3")))}]");
        ApplyTintTransformResult(result, altSpace, alternateObj, ref colorSpaceName, ref color);
    }

    /// <summary>
    /// Flattens a Separation/DeviceN tint-transform result (in the alternate space) to a device colour +
    /// name: 4 comps → DeviceCMYK, Lab-alternate 3 comps → DeviceRGB (via the alternate white point),
    /// other 3 comps → DeviceRGB, 1 comp → DeviceGray. Shared by ResolveSeparation and ResolveDeviceN.
    /// </summary>
    private void ApplyTintTransformResult(double[] result, string altSpace, PdfObject? alternateObj,
        ref string? colorSpaceName, ref List<double>? color)
    {
        switch (result.Length)
        {
            case 4:
                color = [result[0], result[1], result[2], result[3]];
                colorSpaceName = "DeviceCMYK";
                break;
            case >= 3:
                if (altSpace == "Lab" && result.Length == 3)
                {
                    double[] rgb = LabToRgb(result[0], result[1], result[2], alternateObj as PdfArray);
                    color = [rgb[0], rgb[1], rgb[2]];
                    colorSpaceName = "DeviceRGB";
                }
                else
                {
                    color = [result[0], result[1], result[2]];
                    colorSpaceName = altSpace is "CalRGB" ? "DeviceRGB" : altSpace;
                    if (colorSpaceName != "DeviceRGB" && colorSpaceName != "DeviceCMYK" && colorSpaceName != "DeviceGray")
                        colorSpaceName = "DeviceRGB";
                }
                break;
            case 1:
                color = [result[0]];
                colorSpaceName = "DeviceGray";
                break;
        }
    }

    /// <summary>
    /// Builds a colorant-tuple → sRGB mapping for a <c>[/Separation …]</c> or <c>[/DeviceN …]</c>
    /// color space by evaluating its tint transform into the alternate space and converting that to
    /// RGB (Lab honours the alternate's white point). Returns null (and <paramref name="inputComponents"/>
    /// = 0) when the space can't be modelled. Shared by the indexed-image palette path so
    /// DeviceN/Separation images resolve the same way fills do.
    /// </summary>
    internal static Func<double[], (byte R, byte G, byte B)>? BuildTintToRgb(
        PdfArray baseArray, PdfDocument? document, out int inputComponents)
    {
        inputComponents = 0;
        // Pre-Pass-1 this member required Count >= 4 BEFORE touching element 1. minimumElements: 4
        // enforces that arity gate inside TryParse itself, ahead of any dereference, so a short array
        // (e.g. a two-element [/Separation 7 0 R] whose element 1 is a corrupt indirect object) is
        // rejected for free rather than throwing PdfParseException out of this render path.
        if (!SpotColorSpace.TryParse(baseArray, document, out SpotColorSpace? space, minimumElements: 4))
            return null;

        // §8.6.6.4 row 4-10: for /All and /None the alternateSpace and tintTransform SHALL be ignored.
        // Handled before either is read, exactly as ResolveSeparation does for fills — otherwise an /All
        // IMAGE paints a different colour from an identical /All FILL.
        if (PaintsNothing(baseArray, document)) return null;   // /None: build no evaluator at all
        if (space.Family == "Separation" && space.Names[0] == "All")
        {
            // Additive device: the subtractive tint is complemented before being applied to R, G and B,
            // so tint t is the neutral 1 − t.
            inputComponents = 1;
            return t =>
            {
                byte g = Clamp255(1.0 - (t.Length > 0 ? t[0] : 0.0));
                return (g, g, g);
            };
        }

        // Names.Count is the colorant count for both families; the names themselves need not resolve.
        inputComponents = space.Names.Count;
        if (inputComponents < 1) return null;

        string altSpace = space.AlternateSpaceName;
        if (altSpace.Length == 0) return null;
        PdfArray? labArray = altSpace == "Lab" ? space.AlternateObject as PdfArray : null;

        PdfFunction? tint = PdfFunction.Create(space.TintTransformObject, document);
        if (tint is null) return null;

        return colorants =>
        {
            double[] result = tint.Evaluate(colorants);
            if (altSpace == "Lab" && result.Length >= 3)
            {
                double[] rgb = LabToRgb(result[0], result[1], result[2], labArray);
                return (Clamp255(rgb[0]), Clamp255(rgb[1]), Clamp255(rgb[2]));
            }
            return PdfColorToRgb.ToRgb(result, altSpace);
        };
    }

    /// <summary>
    /// Builds a colorant→native-CMYK evaluator for a <c>[/Separation …]</c> or <c>[/DeviceN …]</c>
    /// space whose alternate is <c>DeviceCMYK</c>. Unlike <see cref="BuildTintToRgb"/> it stops at the
    /// tint transform's CMYK output (no CMYK→sRGB step), so a CMYK compositor can paint the spot colour
    /// in native ink. Returns null when the alternate is not DeviceCMYK (the caller then falls back to
    /// the RGB path). <paramref name="inputComponents"/> is the colorant count (1 for Separation).
    /// </summary>
    internal static Func<double[], (double C, double M, double Y, double K)>? BuildTintToCmyk(
        PdfArray baseArray, PdfDocument? document, out int inputComponents)
    {
        inputComponents = 0;
        // Pre-Pass-1 this member required Count >= 4 BEFORE touching element 1, as in BuildTintToRgb.
        // minimumElements: 4 enforces that arity gate inside TryParse itself, ahead of any dereference,
        // so a short array is rejected for free rather than throwing PdfParseException out of this
        // render path.
        if (!SpotColorSpace.TryParse(baseArray, document, out SpotColorSpace? space, minimumElements: 4))
            return null;

        // §8.6.6.4 row 4-10, as in BuildTintToRgb. Placed BEFORE the alternate-space gate below, because
        // the clause says the alternate space is ignored for these names — an /All space is convertible
        // here whatever its alternate happens to be.
        if (PaintsNothing(baseArray, document)) return null;   // /None: build no evaluator at all
        if (space.Family == "Separation" && space.Names[0] == "All")
        {
            // Subtractive device: the tint applies DIRECTLY to every colourant, uncomplemented.
            inputComponents = 1;
            return t =>
            {
                double v = Math.Clamp(t.Length > 0 ? t[0] : 0.0, 0.0, 1.0);
                return (v, v, v, v);
            };
        }

        inputComponents = space.Names.Count;
        if (inputComponents < 1) return null;

        string altSpace = space.AlternateSpaceName;
        // A DeviceGray alternate is just as convertible as a DeviceCMYK one: PDF 32000-1 §10.3.3 makes
        // DeviceGray a DEVICE space that separates onto the black plate alone (k = 1 − gray), which is
        // exactly the rule the FILL path already applies (Pellucid's InkDecider.ToCmyk). Rejecting it sent
        // a [/Separation /Black /DeviceGray] image down the managed RGBA path, where RGB(g,g,g) was
        // ICC-converted into a RICH gray (ink on all four plates) that no longer matched an adjacent
        // DeviceCMYK(0,0,0,k) box — GWG230 c/d, which draw the same X twice (once as a fill, once as an
        // image) and so rendered HALF an X: the fill half correct, the image half rich.
        //
        // CalGray is deliberately NOT included: it is CIE-based, not a device space, so it keeps colour
        // management and must still take the RGBA path (same split InkDecider.ToCmyk makes for fills).
        var grayAlt = altSpace == "DeviceGray";
        if (altSpace != "DeviceCMYK" && !grayAlt) return null;

        PdfFunction? tint = PdfFunction.Create(space.TintTransformObject, document);
        if (tint is null) return null;

        return colorants =>
        {
            double[] r = tint.Evaluate(colorants);
            static double C01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
            // DeviceGray alternate: one output component (gray) → K-only, per §10.3.3.
            if (grayAlt) return (0, 0, 0, C01(1.0 - (r.Length > 0 ? r[0] : 0)));
            return (C01(r.Length > 0 ? r[0] : 0), C01(r.Length > 1 ? r[1] : 0),
                    C01(r.Length > 2 ? r[2] : 0), C01(r.Length > 3 ? r[3] : 0));
        };
    }

    /// <summary>
    /// Builds a 256-entry tint ramp (tint 0..1 → alternate-space colour) for colorant
    /// <paramref name="colorantIndex"/> of a <c>[/Separation …]</c> or <c>[/DeviceN …]</c> array, sweeping
    /// that colorant's input and holding the others at 0 (the per-plate separations approximation). Also
    /// returns a representative sRGB solid at tint = 1 for UI. <c>Ramp</c> is null when there is no usable
    /// tint transform. Soft-Proof SP-1.
    /// </summary>
    internal static (double[][]? Ramp, (byte R, byte G, byte B) Solid) BuildTintRamp(
        PdfArray baseArray, PdfDocument? doc, int colorantIndex, int inputCount, int samples = 256)
    {
        if (colorantIndex < 0 || colorantIndex >= inputCount)
            return (null, (0, 0, 0));
        // Pre-Pass-1 this member required Count >= 4 (baseArray.Count < 4) BEFORE touching element 1.
        // minimumElements: 4 enforces that arity gate inside TryParse itself, ahead of any dereference,
        // so a short array is rejected for free (a null ramp) rather than throwing PdfParseException.
        if (!SpotColorSpace.TryParse(baseArray, doc, out SpotColorSpace? space, minimumElements: 4))
            return (null, (0, 0, 0));

        // ISO 32000-2 Table 71: for an NChannel space, /Attributes /Colorants /<name> is a full
        // Separation describing "the appearance of that colorant alone" — authoritative for this
        // component. Zeroing the other inputs of the whole-space transform (below) only approximates it,
        // and coincides just when that transform is separable. Falls through to the approximation
        // whenever the entry is absent or unusable, which keeps every non-NChannel space — and every
        // NChannel space without usable /Colorants — byte-identical to before.
        if (space.IsNChannel && OwnColorantRamp(space, colorantIndex, doc, samples) is { } ownRamp)
            return ownRamp;

        PdfFunction? tint = PdfFunction.Create(space.TintTransformObject, doc);
        if (tint is null) return (null, (0, 0, 0));

        var ramp = new double[samples][];
        var input = new double[inputCount];
        (byte R, byte G, byte B) solid = (0, 0, 0);
        try
        {
            for (var s = 0; s < samples; s++)
            {
                double t = samples == 1 ? 0.0 : (double)s / (samples - 1);
                Array.Clear(input);
                input[colorantIndex] = t;
                ramp[s] = tint.Evaluate((double[])input.Clone());
            }

            // Representative solid via the existing sRGB evaluator (Lab-aware), at tint = 1.
            Func<double[], (byte R, byte G, byte B)>? toRgb = BuildTintToRgb(baseArray, doc, out int _);
            if (toRgb is not null)
            {
                Array.Clear(input);
                input[colorantIndex] = 1.0;
                solid = toRgb((double[])input.Clone());
            }
        }
        catch (Exception ex)
        {
            // Spec: a missing/invalid tint transform still lists the colorant (Name + Kind) with a null
            // ramp — must never throw into the render path. PdfFunction.Create can succeed yet Evaluate
            // still throw at runtime on a malformed Type-0/Type-4 function body (e.g. a Domain that
            // doesn't match the colour space's actual input count); treat that identically to "no usable
            // tint transform" rather than letting the page-inventory call (GetPageColorants) fail.
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics,
                    $"BuildTintRamp: tint transform threw during ramp/solid evaluation for colorant index {colorantIndex}, falling back to null ramp: {ex}");
            return (null, (0, 0, 0));
        }
        return (ramp, solid);
    }

    /// <summary>
    /// The ramp and representative solid for one NChannel component taken from its own
    /// <c>/Attributes /Colorants /&lt;name&gt;</c> Separation, or null when there is no usable entry.
    ///
    /// <para>Null is the "use the whole-space approximation instead" signal, not an error: a missing
    /// dictionary, a missing entry, a process colorant (Table 71: a <c>/Colorants</c> definition "shall
    /// be ignored if the colorant is also present in the process dictionary" — the four reserved names
    /// always qualify, with or without a <c>/Process</c> dictionary at all), an entry that is not a
    /// Separation, an alternate this engine cannot reduce, or a tint transform that throws at evaluation
    /// time all land here. Everything is wrapped because this runs from
    /// <c>PdfDocument.GetPageColorants</c>, whose contract is that a malformed colour space still lists
    /// its colorants rather than failing the call — the same reason <see cref="BuildTintRamp"/>'s own
    /// evaluation loop is wrapped.</para>
    ///
    /// <para>Dereferencing the entry (and, via <see cref="IsProcessColorant"/>, <c>/Process
    /// /Components</c>) resolves objects no path previously touched here, which is why the try covers the
    /// whole body and not merely the ramp evaluation.</para>
    ///
    /// <para><b>Review finding (Important 2):</b> <see cref="PdfLibrary.Document.PageColorant.TintRamp"/>
    /// is documented, and used by its consumers, as "alternate-space colour" for the SPACE's OWN
    /// alternate — <see cref="PdfLibrary.Document.PageColorant.AlternateSpace"/> is
    /// <c>origin.AlternateSpace</c> (the space's alternate name), threaded through unchanged by
    /// <c>PageColorantReader</c>. This method always emits 4-component DeviceCMYK, taken from the
    /// <c>/Colorants</c> ENTRY's own alternate via <see cref="BuildTintToCmyk"/> — which need not match
    /// the SPACE's alternate at all. Emitting CMYK under an <c>AlternateSpace</c> label that still says
    /// "Lab" (or "DeviceRGB", or a 3-component ICCBased profile) would silently change both the colour
    /// space AND the component count the label promises, and could do so per-component within one
    /// page's colorant list. Gating on the space's own alternate already being DeviceCMYK keeps this
    /// path's output in the same family the label already advertises; widening to other space alternates
    /// is a deliberate, separately-gated future change, not a side effect of this one.</para>
    ///
    /// <para><b>Minor 1 (whole-branch review):</b> the gate previously also admitted <c>DeviceGray</c>,
    /// on the theory that Gray is "in the same family" as CMYK. It is not, in both respects the Lab
    /// exclusion above protects: <c>AlternateSpace</c> would still say <c>DeviceGray</c> while the ramp
    /// silently became 4-component CMYK, and the component count would go 1 → 4 — exactly the
    /// AlternateSpace/component-count mismatch this method exists to avoid. Narrowed to
    /// <c>DeviceCMYK</c> only; a <c>DeviceGray</c>-alternate NChannel space keeps the isolated
    /// whole-space evaluation even with a usable <c>/Colorants</c> entry (see
    /// <c>NChannelWithADeviceGrayAlternate_KeepsTheIsolatedEvaluation_EvenWithAUsableColorantsEntry</c>).
    /// Widening back to Gray is a deliberate, separately-gated future change, the same reasoning that
    /// produced the Lab exclusion.</para>
    /// </summary>
    private static (double[][] Ramp, (byte R, byte G, byte B) Solid)? OwnColorantRamp(
        SpotColorSpace space, int colorantIndex, PdfDocument? doc, int samples)
    {
        try
        {
            // See the Important-2/Minor-1 remarks above: restrict to spaces whose own alternate is
            // already DeviceCMYK (checked before colorantIndex/name, since it depends only on the
            // space, not the component). DeviceGray was excluded by Minor 1: this method always emits
            // 4-component CMYK, which would silently promote a 1-component Gray ramp under a label that
            // still says DeviceGray.
            if (space.AlternateSpaceName is not "DeviceCMYK") return null;

            if (colorantIndex < 0 || colorantIndex >= space.Names.Count) return null;
            if (space.Names[colorantIndex] is not { } name) return null;

            // BuildTintRamp is called directly by PageColorantReader with just an index — unlike
            // OwnAlternateFor's caller, there is no already-built ColourantComponent/Role to consult
            // here, so the process-vs-spot question is re-derived. Without this gate, an NChannel file
            // that (legally, if unusually) carries a /Colorants entry keyed by a reserved or listed
            // process name would take its ramp from that entry instead of falling back — exactly the
            // shape ProcessComponent_StillUsesTheIsolatedEvaluation pins.
            if (IsProcessColorant(space, name, doc)) return null;

            if (space.Colorants is not { } colorants) return null;
            if (!colorants.TryGetValue(new PdfName(name), out PdfObject? entryObj)) return null;
            if (Deref(entryObj, doc) is not PdfArray entry) return null;

            // Reuse the whole-space builders on the Separation itself: a /Colorants value IS a colour
            // space array, so the same arity and alternate-space rules apply to it unchanged.
            Func<double[], (double C, double M, double Y, double K)>? toCmyk =
                BuildTintToCmyk(entry, doc, out int inputs);
            if (toCmyk is null || inputs != 1) return null;

            var ramp = new double[samples][];
            for (var s = 0; s < samples; s++)
            {
                double t = samples == 1 ? 0.0 : (double)s / (samples - 1);
                (double c, double m, double y, double k) = toCmyk([t]);
                ramp[s] = [c, m, y, k];
            }

            // The solid comes from the SAME source as the ramp, so a swatch can never disagree with the
            // plate it represents.
            (byte R, byte G, byte B) solid = (0, 0, 0);
            Func<double[], (byte R, byte G, byte B)>? toRgb = BuildTintToRgb(entry, doc, out int _);
            if (toRgb is not null) solid = toRgb([1.0]);

            return (ramp, solid);
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, () =>
                $"OwnColorantRamp: /Colorants entry for component {colorantIndex} threw; falling back to "
                + $"the whole-space isolated evaluation: {ex}");
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="name"/> is a process colorant per ISO 32000-2 Table 71: the four
    /// reserved names (Cyan/Magenta/Yellow/Black) "shall always be considered to be process colours …
    /// they need not have entries in the process dictionary", and any other name listed in
    /// <c>/Process /Components</c> is a process colorant too — in both cases its <c>/Colorants</c>
    /// definition, if any, "shall be ignored".
    ///
    /// <para>This mirrors <see cref="RoleFor"/>'s reserved-name/listed-name rule but answers only the
    /// yes/no question <see cref="OwnColorantRamp"/> needs — it does not compute a channel index or
    /// consult <see cref="ProcessChannelCount"/>, so it does not duplicate <see cref="BuildComponents"/>'s
    /// channel-count plumbing for a question that has no use for it.</para>
    ///
    /// <para>Deliberately has no try/catch of its own: every call site is inside
    /// <see cref="OwnColorantRamp"/>'s own try, so a corrupt <c>/Process /Components</c> dereference is
    /// already caught there — adding a second catch here would only duplicate that guard.</para>
    ///
    /// <para><b>Two deliberate divergences from <see cref="RoleFor"/> (review finding, Minor 3; each is
    /// defensible on its own but the disagreement itself needed to be written down):</b></para>
    /// <para>(a) No <c>/None</c> arm. <see cref="RoleFor"/> tests <c>/None</c> FIRST and unconditionally
    /// (§8.6.6.5's discard rule must survive even a malformed process dictionary that lists <c>/None</c>).
    /// This method never sees a <c>/None</c> component today, because <c>PageColorantReader.AddColorants</c>
    /// filters <c>ColorantKind.All</c>/<c>None</c> out before calling <see cref="BuildTintRamp"/> at all —
    /// but <see cref="BuildTintRamp"/> is <c>internal static</c> and nothing at that boundary states the
    /// invariant, so a future caller that skips the filter would see <c>/None</c> fall through to the
    /// listed-in-/Process/Components check below rather than being forced to <c>true</c> the way
    /// <see cref="RoleFor"/> forces it to <see cref="ColourantRole.None"/>.</para>
    /// <para>(b) No <c>/Process /ColorSpace</c> check. A name listed in <c>/Process /Components</c> is
    /// treated as process here regardless of whether the process colour space is one this engine can
    /// reduce to plates — <see cref="BuildComponents"/>, by contrast, suppresses the ENTIRE component list
    /// (via <see cref="ProcessChannelCount"/> returning null) when the process space is non-CMYK/Gray
    /// (e.g. <c>/DeviceRGB</c>), so a name that would classify Process there never reaches a Role check at
    /// all in that case. This method only needs "should a /Colorants entry be ignored for this name", which
    /// Table 71 answers from list membership alone — the process space's reducibility is a different
    /// question this method has no reason to ask.</para>
    /// </summary>
    private static bool IsProcessColorant(SpotColorSpace space, string name, PdfDocument? doc)
    {
        if (IsReservedProcessName(name)) return true;
        if (space.Process is not { } process) return false;
        if (!process.TryGetValue(new PdfName("Components"), out PdfObject? compsObj)) return false;
        if (Deref(compsObj, doc) is not PdfArray comps) return false;

        foreach (PdfObject c in comps)
            if (Deref(c, doc) is PdfName cn && cn.Value == name)
                return true;
        return false;
    }

    /// <summary>
    /// Resolves an indirect reference against <paramref name="document"/>, returning the object itself
    /// when it is direct, when there is no document, or when the reference does not resolve.
    /// </summary>
    /// <remarks>
    /// Null-in/null-out: several callers index a PDF array defensively (<c>arr.Count >= 2 ? arr[1] : null</c>)
    /// and then pattern-match the result, so accepting null here is the shape the call sites already want.
    /// <see cref="NotNullIfNotNullAttribute"/> keeps that from leaking a nullable return to the ~29 sites
    /// that pass a value the compiler already knows is non-null — without it, this signature change would
    /// have to be paid for in null-forgiving operators, which is the opposite of the point.
    /// </remarks>
    [return: NotNullIfNotNull(nameof(obj))]
    internal static PdfObject? Deref(PdfObject? obj, PdfDocument? document) =>
        obj is PdfIndirectReference r && document is not null ? document.ResolveReference(r) ?? obj : obj;

    /// <summary>
    /// Computes the per-plate CMYK overprint mask for a colour-space resource name. For a
    /// <c>[/Separation name …]</c> or <c>[/DeviceN [names] …]</c> space, each colorant name maps to a
    /// device plate (Cyan→C, Magenta→M, Yellow→Y, Black→K, All→all four); the mask is the union of the
    /// mapped plates. Returns null when the space is a device space, when the resource can't be resolved,
    /// or when ANY colorant is a spot colour (an unmapped name) — in those cases the caller keeps the
    /// existing OPM-based overprint behaviour. Per ISO 32000 §8.6.6.3, an overprinting Separation/DeviceN
    /// colour preserves the plates it does not paint REGARDLESS of the overprint mode.
    /// </summary>
    /// <param name="csName">The colour-space resource name recorded on the graphics state (e.g. "CS0").</param>
    /// <param name="colorSpaces">The page/resource /ColorSpace dictionary mapping names to definitions.</param>
    /// <param name="doc">Document used to resolve indirect references (may be null).</param>
    public static (bool C, bool M, bool Y, bool K)? OverprintPlatesFor(
        string? csName, PdfDictionary? colorSpaces, PdfDocument? doc)
    {
        if (string.IsNullOrEmpty(csName))
            return null;

        // Device spaces (and Pattern) never carry a colorant-derived plate mask — OPM logic applies.
        if (csName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "Pattern")
            return null;

        if (colorSpaces is null || !colorSpaces.TryGetValue(new PdfName(csName), out PdfObject? csObj))
            return null;

        return PlatesForColorSpaceObject(csObj, doc);
    }

    /// <summary>
    /// Per-plate CMYK overprint mask for a colour-space DEFINITION object (not a resource name) — a
    /// <c>[/Separation name …]</c> or <c>[/DeviceN [names] …]</c> array. Each colorant maps to a device
    /// plate (Cyan→C, Magenta→M, Yellow→Y, Black→K, All→all four); the mask is the union. Returns null for
    /// device spaces, unresolvable objects, or any spot colorant. Used by <see cref="OverprintPlatesFor"/>
    /// (resource-name path) and by shadings, whose /ColorSpace is a direct object, not a resource name.
    /// </summary>
    public static (bool C, bool M, bool Y, bool K)? PlatesForColorSpaceObject(PdfObject? csObj, PdfDocument? doc)
    {
        if (csObj is null) return null;
        csObj = Deref(csObj, doc);

        // GetObject does not chase indirect-reference chains, so a still-unresolved reference here is
        // exactly what the pre-migration single-Deref lookup would already have failed on. Returning
        // early restores that single-level resolution — without this, SpotColorSpace.TryParse's own
        // internal Deref call would newly resolve a second link in the chain, which this pass forbids.
        if (csObj is PdfIndirectReference) return null;

        // An Indexed image's samples are palette indices into its base space; the plates it marks are
        // the base space's plates (e.g. Indexed[/DeviceN[Black Cyan]] duotone marks K + C).
        if (csObj is PdfArray { Count: >= 2 } indexedArr && indexedArr[0] is PdfName { Value: "Indexed" })
            return PlatesForColorSpaceObject(indexedArr[1], doc);

        if (!SpotColorSpace.TryParse(csObj, doc, out SpotColorSpace? space)) return null;

        // Any unresolvable colorant name means we cannot know the plate set — fall back to OPM.
        if (!space!.AllNamesResolved || space.Names.Count == 0) return null;

        bool c = false, m = false, y = false, k = false;
        foreach (string? name in space.Names)
        {
            switch (name)
            {
                case "Cyan": c = true; break;
                case "Magenta": m = true; break;
                case "Yellow": y = true; break;
                case "Black": k = true; break;
                case "All": c = m = y = k = true; break;
                case "None": break;   // marks no colorant (ISO 32000 §8.6.6.4) — DeviceN padding
                default:
                    // A real spot colorant isn't a CMYK plate → fall back to OPM behaviour.
                    return null;
            }
        }

        return (c, m, y, k);
    }

    /// <summary>
    /// The initial colour a space takes when it becomes current via <c>cs</c>/<c>CS</c>
    /// (ISO 32000-2 §8.6.8, Table 73). Returns null when no component vector applies — Pattern, whose
    /// initial "colour" is a pattern object that paints nothing rather than a set of components, and
    /// any space that cannot be identified, where leaving the current colour alone is safer than
    /// guessing.
    ///
    /// <para>
    /// The values are NOT uniform across families, which is the trap that sank an earlier attempt at
    /// this: DeviceCMYK is <c>[0 0 0 1]</c> (black via K — all-zeros would be white), while Separation
    /// and DeviceN initialise to 1.0 because their tints are subtractive. Lab and ICCBased initialise
    /// to zero "unless that falls outside the intervals specified by the space's Range entry, in which
    /// case the nearest valid value shall be substituted".
    /// </para>
    /// </summary>
    public static List<double>? InitialColorFor(string? csName, PdfObject? csObj, PdfDocument? doc)
    {
        // The four families nameable without parameters resolve by name alone, and always identify
        // those spaces directly — they never refer to a ColorSpace resource.
        switch (csName)
        {
            case "DeviceGray": return [0.0];
            case "DeviceRGB": return [0.0, 0.0, 0.0];
            case "DeviceCMYK": return [0.0, 0.0, 0.0, 1.0];
            case "Pattern": return null;
        }

        if (csObj is null) return null;
        csObj = Deref(csObj, doc);

        if (csObj is PdfName aliasName)
            return InitialColorFor(aliasName.Value, null, doc);

        if (csObj is not PdfArray { Count: >= 1 } arr || arr[0] is not PdfName family)
            return null;

        switch (family.Value)
        {
            case "DeviceGray" or "CalGray": return [0.0];
            case "DeviceRGB" or "CalRGB": return [0.0, 0.0, 0.0];
            case "DeviceCMYK": return [0.0, 0.0, 0.0, 1.0];
            case "Indexed": return [0.0];
            case "Pattern": return null;

            case "Separation": return [1.0];

            case "DeviceN":
            {
                if (Deref(arr.Count >= 2 ? arr[1] : null, doc) is not PdfArray { Count: > 0 } names)
                    return null;
                var tints = new List<double>(names.Count);
                for (var i = 0; i < names.Count; i++) tints.Add(1.0);
                return tints;
            }

            case "Lab":
            {
                // L is always in [0,100] so 0 is valid; a and b are clamped to /Range (default
                // [-100 100 -100 100]).
                double[] range = LabRangeOrDefault(arr, doc);
                return [0.0, Clamp(0.0, range[0], range[1]), Clamp(0.0, range[2], range[3])];
            }

            case "ICCBased":
            {
                if (Deref(arr.Count >= 2 ? arr[1] : null, doc) is not PdfStream icc) return null;

                // /N is required by ISO 32000-2 Table 66 — a stream without it is a malformed file, not
                // one we should guess a channel count for. (ResolveICCBased separately defaults absent
                // /N to 1 when actually resolving a colour value, which is a different concern: it needs
                // *some* count to keep the colour tuple well-formed even for malformed input. Guessing
                // here would only disagree with that default and pick an initial colour with a component
                // count the resolve step wouldn't consistently use anyway.) Returning null means "leave
                // the current colour alone" — the same conservative fallback already used for Pattern and
                // for unidentifiable spaces.
                if (!icc.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj)
                    || Deref(nObj, doc) is not PdfInteger nInt) return null;
                int n = nInt.Value;
                if (n < 1) return null;

                var comps = new List<double>(n);
                PdfArray? iccRange = icc.Dictionary.TryGetValue(new PdfName("Range"), out PdfObject rObj)
                    ? Deref(rObj, doc) as PdfArray
                    : null;
                for (var i = 0; i < n; i++)
                {
                    double lo = iccRange is not null && iccRange.Count > 2 * i ? iccRange[2 * i].ToDouble() : 0.0;
                    double hi = iccRange is not null && iccRange.Count > 2 * i + 1 ? iccRange[2 * i + 1].ToDouble() : 1.0;
                    comps.Add(Clamp(0.0, lo, hi));
                }
                return comps;
            }

            default: return null;
        }
    }

    /// <summary>Lab <c>/Range</c> as [aMin aMax bMin bMax], defaulting per ISO 32000-2 Table 65.</summary>
    private static double[] LabRangeOrDefault(PdfArray labArray, PdfDocument? doc)
    {
        if (labArray.Count >= 2 && Deref(labArray[1], doc) is PdfDictionary d
            && d.TryGetValue(new PdfName("Range"), out PdfObject rObj)
            && Deref(rObj, doc) is PdfArray { Count: >= 4 } r)
            return [r[0].ToDouble(), r[1].ToDouble(), r[2].ToDouble(), r[3].ToDouble()];
        return [-100.0, 100.0, -100.0, 100.0];
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>
    /// True when a colour space marks nothing on the page, so painting operators using it shall be
    /// suppressed entirely (ISO 32000-2 §8.6.6.4 and §8.6.6.5).
    ///
    /// <para>
    /// Two cases qualify. A <c>[/Separation /None …]</c> space "shall not produce any visible output […]
    /// shall have no effect on the current page", on all devices. A DeviceN space whose component names
    /// are <i>all</i> <c>/None</c> "shall always discard its output […] it shall never revert to the
    /// alternate colour space" (§8.6.6.5) — so it too paints nothing, and notably must not be flattened
    /// through its tint transform on the way.
    /// </para>
    ///
    /// <para>
    /// This is deliberately a separate signal from the resolved colour rather than a colour value.
    /// Resolving <c>/None</c> to white would still mark the page, which is only invisible when the
    /// backdrop happens to be white.
    /// </para>
    /// </summary>
    public static bool PaintsNothing(string? csName, PdfDictionary? colorSpaces, PdfDocument? doc)
    {
        if (string.IsNullOrEmpty(csName)) return false;
        if (csName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "Pattern") return false;
        if (colorSpaces is null || !colorSpaces.TryGetValue(new PdfName(csName), out PdfObject? csObj))
            return false;
        return PaintsNothing(csObj, doc);
    }

    /// <summary>
    /// <see cref="PaintsNothing(string?,PdfDictionary?,PdfDocument?)"/> for a colour-space DEFINITION
    /// object rather than a resource name. Images and shadings carry their colour space as a direct
    /// object, so they cannot use the name-based form; both share this one body so the rule for the
    /// reserved names lives in exactly one place.
    /// </summary>
    public static bool PaintsNothing(PdfObject? csObj, PdfDocument? doc)
    {
        if (csObj is null) return false;
        csObj = Deref(csObj, doc);

        // GetObject does not chase indirect-reference chains, so a still-unresolved reference here is
        // exactly what the pre-migration single-Deref lookup would already have failed on. Returning
        // early restores that single-level resolution — without this, SpotColorSpace.TryParse's own
        // internal Deref call would newly resolve a second link in the chain, which this pass forbids.
        if (csObj is PdfIndirectReference) return false;

        // Indexed paints through its BASE space, so it marks nothing exactly when the base marks
        // nothing. SpotColorSpace does not model Indexed, so this recursion stays here.
        if (csObj is PdfArray { Count: >= 2 } indexedArr && indexedArr[0] is PdfName { Value: "Indexed" })
            return PaintsNothing(indexedArr[1], doc);

        if (!SpotColorSpace.TryParse(csObj, doc, out SpotColorSpace? space)) return false;

        // Separation: the single colorant is /None.
        // DeviceN: EVERY component is /None. A name that did not resolve is not "None", so it makes
        // the answer false — matching the pre-Pass-1 behaviour exactly.
        if (space!.Names.Count == 0) return false;
        for (var i = 0; i < space.Names.Count; i++)
            if (space.Names[i] != "None")
                return false;
        return true;
    }

    /// <summary>
    /// The named-colorant identity for a colour-space RESOURCE NAME, or null for device/Pattern spaces or
    /// spaces that aren't Separation/DeviceN. Mirrors <see cref="OverprintPlatesFor"/>: called at the same
    /// PdfRenderer site that sets FillOverprintPlates, using the graphics state's raw (pre-resolution)
    /// colour-space name and colour components. Soft-Proof SP-1.
    /// </summary>
    public static ColorantOrigin? OriginFor(
        string? csName, IReadOnlyList<double>? rawColor, PdfDictionary? colorSpaces, PdfDocument? doc)
    {
        if (string.IsNullOrEmpty(csName)) return null;
        if (csName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK" or "Pattern") return null;
        if (colorSpaces is null || !colorSpaces.TryGetValue(new PdfName(csName), out PdfObject? csObj))
            return null;
        return OriginForColorSpaceObject(csObj, rawColor, doc);
    }

    /// <summary>Named-colorant identity for a colour-space DEFINITION object (not a resource name) — used by
    /// shadings, whose /ColorSpace is a direct object. Null unless the object is Separation/DeviceN.</summary>
    public static ColorantOrigin? OriginForColorSpaceObject(
        PdfObject? csObj, IReadOnlyList<double>? rawColor, PdfDocument? doc)
    {
        // Pre-Pass-1 this member required Count >= 4, unlike PaintsNothing/PlatesForColorSpaceObject.
        // minimumElements: 4 enforces that arity gate inside TryParse itself, ahead of any dereference,
        // so a short array (e.g. a two-element [/Separation name]) yields no origin for free rather
        // than throwing PdfParseException out of a malformed tint-transform reference it never needed
        // to touch.
        if (!SpotColorSpace.TryParse(csObj, doc, out SpotColorSpace? space, minimumElements: 4))
            return null;

        if (!space.AllNamesResolved || space.Names.Count == 0) return null;

        var names = new List<string>(space.Names.Count);
        foreach (string? n in space.Names) names.Add(n!);

        double[] tints = rawColor is null ? [] : [.. rawColor];
        IReadOnlyList<ColourantComponent>? components =
            BuildComponents(space, tints, doc, out int? processChannelCount);
        return new ColorantOrigin(names, tints, space.AlternateSpaceName)
        {
            Subtype = space.Subtype,
            Components = components,
            ProcessChannelCount = processChannelCount,
            Placement = ColorantPlacement.Build(components, processChannelCount),
        };
    }

    /// <summary>
    /// Builds the per-component carrier for an NChannel space (ISO 32000-2 §8.6.6.5: "the components
    /// shall be evaluated individually"). Returns null for every space that is not NChannel — a
    /// Separation, a plain DeviceN, or an unrecognised subtype — because for those the whole space
    /// reverts together and there is nothing per-component to say.
    ///
    /// <para><paramref name="tints"/> may be SHORTER than the name list, including empty: a shading
    /// resolves its origin with no per-op colour. Those components get a null tint.</para>
    ///
    /// <para><paramref name="processChannelCount"/> surfaces the effective process-space channel count
    /// alongside the list, non-null exactly when the return value is non-null. It adds no resolution
    /// site: the value is the one the guarded <c>/Process</c> read below already computed and the
    /// component loop is already bounded by. See <see cref="ColorantOrigin.ProcessChannelCount"/> for why
    /// a consumer cannot infer it from <see cref="ColourantComponent.ProcessChannel"/> alone.</para>
    /// </summary>
    private static IReadOnlyList<ColourantComponent>? BuildComponents(
        SpotColorSpace space, IReadOnlyList<double> tints, PdfDocument? doc, out int? processChannelCount)
    {
        processChannelCount = null;
        if (!space.IsNChannel) return null;

        // ISO 32000-2 Table 72: /Process names the process colour space and its component names. That
        // space may be any device or CIE-based space, but reducing a non-CMYK/non-Gray one to plates is
        // a colour conversion this method has no converter for. Rather than classify such components as
        // Process and leave the consumer unable to say which plate they mark — which maps them onto
        // NONE, worse than the status quo — suppress the whole component list and let the space fall
        // back to its document tint transform. Recorded as a gap; see the Pass 2a plan.
        //
        // /DeviceCMYK, /DeviceGray, an absent /ColorSpace ("no constraint"), and an ICCBased space whose
        // stream declares /N 4 or /N 1 (ISO 32000-1 EXAMPLE 5 — the canonical calibrated-CMYK NChannel,
        // and what Illustrator/InDesign emit for /ColorSpace [/ICCBased n 0 R]) are accepted; anything
        // else — /DeviceRGB, /Lab, /CalGray, an ICCBased stream of some other channel count, etc. — stays
        // suppressed. See ProcessChannelCount for exactly which shapes are which.
        //
        // /ColorSpace and /Components can themselves be indirect references (GWG081 uses both), so
        // reading them dereferences objects that EnsureAttributes's own try/catch does not cover — it
        // only guards the /Attributes dictionary and its immediate Subtype/Colorants/Process values, not
        // what THOSE values point to. A corrupt target here would otherwise throw PdfParseException out
        // of OriginForColorSpaceObject, which PdfRenderer calls on every colour-setting operator with no
        // try/catch above it. Guarded the same way and for the same reason as EnsureAttributes: fall
        // back to reserved-name-only classification (processChannels stays null) rather than fail a
        // render that used to succeed. This is deliberately distinct from the non-CMYK/non-Gray case
        // above/below, which is a successful read that legitimately suppresses the whole list — a throw
        // must NOT take that branch, or a corrupt process dictionary would silently drop an otherwise-
        // good NChannel space.
        //
        // processChannels maps a /Components name to its POSITIONAL index (Table 71: "correspond, in
        // order, to the components of the process colour space" — position IS the channel identity).
        // channelCount is the effective process space's channel count (4 for DeviceCMYK/no-constraint/
        // ICCBased-N4, 1 for DeviceGray/ICCBased-N1): reserved names get their canonical channel only
        // when it is 4, never when it is 1, where one channel cannot be attributed to a specific
        // reserved name.
        Dictionary<string, int>? processChannels = null;
        // Starts at the no-constraint default and is only ever LOWERED, by a successful DeviceGray or
        // /N 1 read. That is why the catch below does not restore it: if the lowering read already
        // succeeded, 1 is correct; if the throw preceded it, 4 is already correct. Restoring 4 here would
        // re-enable the canonical CMYK guess for a space known to have one channel.
        var channelCount = 4;
        if (space.Process is { } process)
        {
            try
            {
                if (ProcessChannelCount(process, doc) is not { } count) return null;
                channelCount = count;

                if (process.TryGetValue(new PdfName("Components"), out PdfObject? compsObj)
                    && Deref(compsObj, doc) is PdfArray comps)
                {
                    // Each element can itself be an indirect reference (GWG081 uses this shape for the
                    // whole array; a well-formed file may do it per-element too) — asymmetric with
                    // SpotColorSpace.TryParse's DeviceN names-array handling (:216), which derefs every
                    // element for exactly this reason. Skipping the deref here would misclassify an
                    // indirect process component as Spot.
                    processChannels = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (var ci = 0; ci < comps.Count; ci++)
                    {
                        if (Deref(comps[ci], doc) is not PdfName cn) continue;
                        // First index wins on a duplicate name — Table 71 is positional, and the first
                        // occurrence is the one the array's own order assigns the name to.
                        if (!processChannels.ContainsKey(cn.Value)) processChannels[cn.Value] = ci;
                    }
                }
            }
            catch (Exception ex)
            {
                // channelCount is deliberately NOT reset here. It starts at 4 (the "no constraint"
                // default) and is only ever LOWERED, at the successful read above, once the process
                // space is confirmed one-channel (DeviceGray or ICCBased /N 1). If that read already
                // succeeded before this throw (e.g. a corrupt /Components element under an otherwise-
                // valid one-channel /ColorSpace), channelCount is correctly 1 already; resetting it to 4
                // here would let a reserved name get a canonical index for what is actually a one-channel
                // space — exactly the guess ProcessChannelFor's one-channel rule forbids, and an index
                // the range guard would otherwise reject. The field's initial value already supplies the
                // correct default on every path where the throw precedes the lowering read, so there is
                // nothing to restore here.
                processChannels = null;
                PdfLogger.Log(LogCategory.Graphics, () =>
                    $"BuildComponents: /Process dereferencing threw, falling back to reserved-name classification: {ex}");
            }
        }

        // Surfaced alongside the list, from the value ProcessChannelFor was already bounded by. Set HERE
        // rather than at each successful read: every early return above is a suppression, and a
        // suppressed list must carry a suppressed count (see ColorantOrigin.ProcessChannelCount). No new
        // dereference — `channelCount` is already resolved by the guarded read above, and its catch
        // deliberately leaves it lowered-or-default, so this is the same number the loop below uses.
        processChannelCount = channelCount;

        var components = new List<ColourantComponent>(space.Names.Count);
        for (var i = 0; i < space.Names.Count; i++)
        {
            string name = space.Names[i]!;   // callers gate on AllNamesResolved before reaching here
            double? tint = i < tints.Count ? tints[i] : null;
            ColourantRole role = RoleFor(name, processChannels);
            int? processChannel = ProcessChannelFor(name, role, processChannels, channelCount);
            components.Add(new ColourantComponent(
                name, role, tint, OwnAlternateFor(space, name, role, tint, doc), processChannel));
        }
        return components;
    }

    /// <summary>The number of channels in an NChannel space's process colour space, or null when this
    /// engine cannot say — in which case <see cref="BuildComponents"/> suppresses the whole component
    /// list and the space falls back to its document tint transform.
    ///
    /// <para>An absent or unreadable <c>/ColorSpace</c> returns the no-constraint default rather than
    /// null: a malformed process dictionary should degrade to reserved-name classification, not suppress
    /// an otherwise usable space.</para>
    ///
    /// <para><c>[/ICCBased s]</c> is accepted by reading the stream's <c>/N</c> — 4 is CMYK-shaped, 1 is
    /// Gray-shaped, anything else is not reducible to plates here. ISO 32000-1 EXAMPLE 5 is exactly this
    /// shape and is what Illustrator and InDesign emit. Only the channel COUNT is read: the profile is
    /// not validated and is not used for conversion, which is a colour-management question rather than a
    /// plate-identity one. Read the stream's DICTIONARY only — never Data/GetDecodedData; a real profile
    /// is hundreds of kilobytes of Flate and this runs on every colour-setting operator.</para>
    ///
    /// <para>Dereferencing the ICC stream resolves an object no path previously touched here. The call
    /// site in <see cref="BuildComponents"/> wraps this in try/catch for exactly that reason;
    /// <c>CorruptIccBasedStreamReference_DegradesToReservedNamesOnly_RatherThanThrowing</c> pins it.</para>
    /// </summary>
    private static int? ProcessChannelCount(PdfDictionary process, PdfDocument? doc)
    {
        const int noConstraint = 4;

        if (!process.TryGetValue(new PdfName("ColorSpace"), out PdfObject? csObj)) return noConstraint;

        PdfObject resolved = Deref(csObj, doc);
        string family = resolved switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty,
        };

        switch (family)
        {
            case "": return noConstraint;          // unreadable shape — degrade, do not reject
            case "DeviceCMYK": return 4;
            case "DeviceGray": return 1;

            case "ICCBased":
                if (resolved is not PdfArray { Count: >= 2 } iccArray) return null;
                if (Deref(iccArray[1], doc) is not PdfStream icc) return null;
                // Matches the established idiom at the ICCBased arm of InitialColorFor: /N may itself be
                // an indirect reference, and only a PdfInteger counts.
                if (!icc.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj)
                    || Deref(nObj, doc) is not PdfInteger nInt) return null;
                return nInt.Value is 4 or 1 ? nInt.Value : null;

            // /DeviceRGB, /Lab, /CalGray, /Indexed, /Pattern, anything else. /CalGray is deliberately NOT
            // treated as Gray: it is CIE-based rather than a device space, and InkDecider.ToCmyk already
            // distinguishes it from /DeviceGray for that reason. Recorded as a gap.
            default: return null;
        }
    }

    /// <summary>
    /// Single source of truth for ISO 32000-2 Table 71's four reserved process-colourant names and their
    /// canonical channel order under a four-channel process space (Cyan=0/Magenta=1/Yellow=2/Black=3).
    ///
    /// <para>Review finding (Minor 4): <see cref="RoleFor"/>, <see cref="ProcessChannelFor"/> and
    /// <see cref="IsProcessColorant"/> each independently listed these four names. All three now consult
    /// this one map (via <see cref="IsReservedProcessName"/> for the yes/no question, or directly for the
    /// canonical index) instead of repeating the literal list — behaviour is unchanged.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> ReservedProcessChannels =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Cyan"] = 0,
            ["Magenta"] = 1,
            ["Yellow"] = 2,
            ["Black"] = 3,
        };

    /// <summary>True for the four reserved process-colourant names (Cyan/Magenta/Yellow/Black) — see
    /// <see cref="ReservedProcessChannels"/> for the single list they're drawn from.</summary>
    private static bool IsReservedProcessName(string name) => ReservedProcessChannels.ContainsKey(name);

    /// <summary>G-14 predicate — the ONE definition of "all-reserved" on the engine side (the
    /// compositor's copy is InkDecider.AllReservedProcessOrNone, kept in step by the cross-repo
    /// pins): every name reserved-process or /None, at least one reserved-process. /All fails it.
    /// True means ISO 32000-2 §8.6.6.4's first clause applies on a CMYK device — every colourant
    /// is available, nothing may be simulated, tints go straight to plates.</summary>
    internal static bool AllReservedProcessOrNone(IReadOnlyList<string> names)
    {
        var anyProcess = false;
        foreach (string n in names)
        {
            if (n == "None") continue;
            if (!IsReservedProcessName(n)) return false;
            anyProcess = true;
        }
        return anyProcess;
    }

    /// <summary>The CMYK plate index of a reserved process name (Cyan 0 … Black 3); null for any
    /// other name, including /None and /All. The lookup is <see cref="ReservedProcessChannels"/> —
    /// the same single list every reserved-name decision draws from.</summary>
    internal static int? ReservedChannelOf(string name) =>
        ReservedProcessChannels.TryGetValue(name, out int ch) ? ch : null;

    /// <summary>Element-1 colourant names of a Separation/DeviceN array: a single name for
    /// Separation, the /Names array for DeviceN. Empty on any other shape.</summary>
    internal static string[] ColorantNamesOf(PdfArray sepOrDeviceN, PdfDocument? document)
    {
        PdfObject el = sepOrDeviceN.Count > 1 ? sepOrDeviceN[1] : PdfNull.Instance;
        if (el is PdfIndirectReference r && document is not null)
            el = document.ResolveReference(r) ?? el;
        return el switch
        {
            PdfName one => [one.Value],
            PdfArray arr => [.. arr.Select(x =>
                x is PdfIndirectReference xr && document is not null
                    ? document.ResolveReference(xr) ?? x : x)
                .OfType<PdfName>().Select(p => p.Value)],
            _ => [],
        };
    }

    /// <summary>Element-1 colourant COUNT of a Separation/DeviceN array, as the array DECLARES it —
    /// unlike <see cref="ColorantNamesOf"/>, which silently drops any non-<see cref="PdfName"/> element
    /// via <c>OfType&lt;PdfName&gt;</c>. Separation is always 1 (a single name, never an array); DeviceN
    /// is the /Names array's raw <c>Count</c>, non-name elements included. −1 on any other shape (no
    /// count to declare).</summary>
    /// <remarks>
    /// Whole-branch final review, minor finding 3: a malformed DeviceN whose /Names array mixes a
    /// non-<see cref="PdfName"/> element in with real names (e.g. <c>[/Cyan 5]</c>) makes
    /// <c>ColorantNamesOf(...).Length</c> LIE about the space's declared colourant count — the dropped
    /// element silently shifts every following sample's stride. A caller that walks per-pixel samples at
    /// stride <c>ColorantNamesOf(...).Length</c> must first confirm that count against THIS one; on a
    /// mismatch the space is malformed and the caller should decline (fall through to its existing path)
    /// rather than walk samples at the wrong stride.
    /// </remarks>
    internal static int DeclaredColourantCount(PdfArray sepOrDeviceN, PdfDocument? document)
    {
        PdfObject el = sepOrDeviceN.Count > 1 ? sepOrDeviceN[1] : PdfNull.Instance;
        if (el is PdfIndirectReference r && document is not null)
            el = document.ResolveReference(r) ?? el;
        return el switch
        {
            PdfName => 1,
            PdfArray arr => arr.Count,
            _ => -1,
        };
    }

    /// <summary>
    /// Classifies one colourant name. ISO 32000-2 Table 71: the reserved names Cyan, Magenta, Yellow
    /// and Black "shall always be considered to be process colours … they need not have entries in the
    /// process dictionary", and any name listed in the process dictionary is a process component whose
    /// /Colorants definition, if any, "shall be ignored".
    ///
    /// <para><c>/None</c> is tested FIRST and unconditionally: §8.6.6.5 requires those components to be
    /// discarded when painting named colourants directly, and a malformed process dictionary listing
    /// /None must not override that. Classifying /None as anything else would send it down a paint
    /// path.</para>
    ///
    /// <para><b>Minor 2 (whole-branch review):</b> <c>/All</c> is likewise tested unconditionally,
    /// alongside <c>/None</c>. <c>ColourantRole</c> has no <c>All</c> member —
    /// <see cref="ColourantRole.Spot"/> stands in for it, and <c>PageColorantReader.KindFor</c> recovers
    /// the distinction by falling back to
    /// <c>PageColorant.Classify</c> whenever the role is Spot. Without this arm, a malformed-but-real
    /// <c>/Process /Components</c> array that (illegally) lists <c>/All</c> would hit the
    /// <c>processChannels.ContainsKey</c> arm below and classify Process — silently absorbing the
    /// reserved "paint every plate" name into the process set, where <c>KindFor</c> can no longer recover
    /// the All/Spot distinction it depends on this method to preserve. <c>/All</c> must never be treated
    /// as process, regardless of what a malformed process dictionary lists.</para>
    /// </summary>
    private static ColourantRole RoleFor(string name, Dictionary<string, int>? processChannels) => name switch
    {
        "None" => ColourantRole.None,
        "All" => ColourantRole.Spot,
        _ when IsReservedProcessName(name) => ColourantRole.Process,
        _ when processChannels is not null && processChannels.ContainsKey(name) => ColourantRole.Process,
        _ => ColourantRole.Spot,
    };

    /// <summary>
    /// The zero-based channel index this component marks within the process colour space's channel
    /// ordering (ISO 32000-2 Table 71: <c>/Process /Components</c> names "correspond, in order" to the
    /// process space's components — position IS the channel identity, which the name alone does not
    /// carry for a non-reserved process plate such as <c>/PlateX</c>).
    ///
    /// <para>Null for every non-Process role. For a Process component: its positional index in
    /// <paramref name="processChannels"/> when listed there (bounded by <paramref name="channelCount"/> —
    /// an index at or beyond it is malformed input, not a channel); otherwise, for one of the four
    /// reserved names, the canonical index from <see cref="ReservedProcessChannels"/> — but ONLY when
    /// <paramref name="channelCount"/> is 4. Under a one-channel process space, nothing in the spec says
    /// which reserved name owns it, so guessing would be exactly the half-built mapping this plan's Scope
    /// warns is worse than not building it — the result is null.</para>
    /// </summary>
    private static int? ProcessChannelFor(
        string name, ColourantRole role, Dictionary<string, int>? processChannels, int channelCount)
    {
        if (role != ColourantRole.Process) return null;

        if (processChannels is not null && processChannels.TryGetValue(name, out int listedIndex))
            return listedIndex < channelCount ? listedIndex : null;

        // Canonical reserved channels exist only for a four-channel process space. Under a one-channel
        // space nothing in the spec says which reserved name owns the single channel, and guessing would
        // be the half-built mapping this plan's Scope warns is worse than not building it.
        if (channelCount != 4) return null;
        return ReservedProcessChannels.TryGetValue(name, out int canonicalIndex) ? canonicalIndex : null;
    }

    /// <summary>
    /// The component's own alternate colour as CMYK, from <c>/Attributes /Colorants /&lt;name&gt;</c> —
    /// which ISO 32000-2 Table 71 defines as a full Separation space describing "the appearance of that
    /// colorant alone", i.e. exactly the "alternate colour space of that component" §8.6.6.5 calls for.
    ///
    /// <para>Null whenever that cannot be produced: a non-spot role (process components take the process
    /// space, /None is never painted), no tint to evaluate at, no /Colorants dictionary, no entry for
    /// this name, an entry that is not a Separation, or an alternate this engine cannot reduce to CMYK.
    /// Null is a meaningful answer meaning "this component cannot be reverted individually" — the
    /// consumer falls back rather than inventing a colour.</para>
    ///
    /// <para>The evaluation is wrapped because <see cref="BuildTintToCmyk"/> has no internal catch — only
    /// <see cref="BuildTintRamp"/> does — and a Type 0 or Type 4 function can build successfully and
    /// still throw at evaluation time on a malformed body. This method runs from
    /// <see cref="OriginForColorSpaceObject"/>, which <c>PdfRenderer</c> calls on every colour-setting
    /// operator, so a throw here would take down the render of an otherwise fine page.</para>
    /// </summary>
    private static IReadOnlyList<double>? OwnAlternateFor(
        SpotColorSpace space, string name, ColourantRole role, double? tint, PdfDocument? doc)
    {
        if (role != ColourantRole.Spot || tint is not { } t) return null;
        if (space.Colorants is not { } colorants) return null;
        if (!colorants.TryGetValue(new PdfName(name), out PdfObject? entryObj)) return null;

        try
        {
            // doc is load-bearing: GWG081's /Colorants entry is `14 0 R`, and a Separation's tint
            // transform is normally an indirect stream object. Passing null here would leave both
            // unresolved and silently yield no alternate on every real NChannel file.
            //
            // This Deref must live INSIDE the try, not above it: it is a live dereference of an object
            // no engine path previously touched (the /Colorants dictionary itself is already covered by
            // EnsureAttributes's guard, but not what its entries point to), and PdfDocument.GetObject
            // wraps a corrupt on-demand object's parse failure in PdfParseException. This runs from
            // OriginForColorSpaceObject, which PdfRenderer calls on every colour-setting operator with
            // no try/catch above it — the same lesson /Process's contents needed one level up.
            if (Deref(entryObj, doc) is not PdfArray entry) return null;

            Func<double[], (double C, double M, double Y, double K)>? toCmyk =
                BuildTintToCmyk(entry, doc, out int inputs);
            // Table 71 requires a /Colorants value to be a full Separation space: exactly one input.
            // BuildTintToCmyk accepts DeviceN too (inputComponents = space.Names.Count), whose delegate
            // would otherwise evaluate a multi-input tint transform on the one-element array below,
            // silently producing a WRONG alternate rather than the null the design calls for.
            if (toCmyk is null || inputs != 1) return null;

            (double c, double m, double y, double k) = toCmyk([t]);
            return [c, m, y, k];
        }
        catch (Exception ex)
        {
            // Lazy overload: Log(category, string) checks IsCategoryEnabled AFTER the caller has already
            // formatted the string, so a plain interpolation pays a full Exception.ToString() stack-trace
            // format even when Graphics logging is disabled. A fresh SpotColorSpace is parsed per colour
            // operator, so this runs on every colour-setting operator when an NChannel space is in play.
            PdfLogger.Log(LogCategory.Graphics, () =>
                $"OwnAlternateFor: /Colorants entry for '{name}' threw during evaluation; "
                + $"treating the component as having no individual alternate: {ex}");
            return null;
        }
    }

    private static byte Clamp255(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>
    /// Resolves an Indexed color space: [/Indexed base hival lookup]
    /// </summary>
    private void ResolveIndexed(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];

        // Indexed color space requires a single component (the palette index)
        if (color.Count != 1)
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed: Expected 1 component (palette index), got {color.Count}");
            return;
        }

        var paletteIndex = (int)color[0];

        // Get the base color space (index 1) - can be PdfName or PdfArray
        PdfObject? baseObj = csArray[1];
        if (baseObj is PdfIndirectReference baseRef && document is not null)
            baseObj = document.ResolveReference(baseRef);

        // Extract base color space name and number of components
        string? baseColorSpace = null;
        var baseComponents = 3; // Default to RGB

        switch (baseObj)
        {
            case PdfName baseName:
                baseColorSpace = baseName.Value;
                baseComponents = baseColorSpace switch
                {
                    "DeviceGray" => 1,
                    "DeviceRGB" => 3,
                    "DeviceCMYK" => 4,
                    _ => 3
                };
                break;
            case PdfArray { Count: >= 1 } baseArray when baseArray[0] is PdfName baseTypeName:
            {
                // Handle array-based color spaces like [/ICCBased stream]
                if (baseTypeName.Value == "ICCBased" && baseArray.Count >= 2)
                {
                    // Get ICC stream to determine component count
                    PdfObject? iccStreamObj = baseArray[1];
                    if (iccStreamObj is PdfIndirectReference iccRef && document is not null)
                        iccStreamObj = document.ResolveReference(iccRef);

                    if (iccStreamObj is PdfStream iccStream)
                    {
                        if (iccStream.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj) && nObj is PdfInteger nInt)
                        {
                            baseComponents = nInt.Value;
                        }
                    }

                    baseColorSpace = baseTypeName.Value;
                }

                break;
            }
        }

        // Get hival (index 2) - maximum palette index
        var hival = 255;
        if (csArray[2] is PdfInteger hivalInt)
        {
            hival = hivalInt.Value;
        }

        // Get lookup table (index 3) - can be string or stream
        PdfObject? lookupObj = csArray[3];

        if (lookupObj is PdfIndirectReference lookupRef && document is not null)
            lookupObj = document.ResolveReference(lookupRef);

        byte[]? paletteData = lookupObj switch
        {
            PdfString lookupString => lookupString.Bytes,
            PdfStream lookupStream => lookupStream.GetDecodedData(document?.Decryptor),
            _ => null
        };

        if (paletteData is null)
        {
            PdfLogger.Log(LogCategory.Graphics, "RESOLVE Indexed: No palette data found");
            return;
        }

        // Validate palette index
        if (paletteIndex < 0 || paletteIndex > hival)
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed: Palette index {paletteIndex} out of range [0, {hival}]");
            paletteIndex = Math.Clamp(paletteIndex, 0, hival);
        }

        // Extract color components from the palette
        // It is stored as consecutive bytes: [C1_0, C2_0, C3_0, C1_1, C2_1, C3_1, ...]
        int bytesPerEntry = baseComponents;
        int byteOffset = paletteIndex * bytesPerEntry;

        if (byteOffset + bytesPerEntry > paletteData.Length)
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed: Palette index {paletteIndex} * {bytesPerEntry} = {byteOffset} exceeds palette size {paletteData.Length}");
            return;
        }

        // Extract color components and normalize to 0.0-1.0 range
        var paletteColor = new List<double>();
        for (var i = 0; i < baseComponents; i++)
        {
            byte componentByte = paletteData[byteOffset + i];
            double normalizedValue = componentByte / 255.0;
            paletteColor.Add(normalizedValue);
        }

        if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed: index={paletteIndex}, base={baseColorSpace}, components={baseComponents}, palette color=[{string.Join(", ", paletteColor.Select(c => c.ToString("F3")))}]");

        // Now we have the color from the palette, resolve the base color space if needed
        color = paletteColor;

        // If the base color space is ICCBased or another complex type, resolve it recursively
        if (baseObj is PdfArray baseArray2 and [PdfName { Value: "ICCBased" }, _, ..])
        {
            ResolveICCBased(baseArray2, ref baseColorSpace, ref color, out _);
        }

        // Set the resolved color space
        if (color is null) return;
        colorSpaceName = baseColorSpace switch
        {
            "ICCBased" => color.Count switch
            {
                1 => "DeviceGray",
                3 => "DeviceRGB",
                4 => "DeviceCMYK",
                _ => "DeviceRGB"
            },
            _ => baseColorSpace ?? "DeviceRGB"
        };
    }

    /// <summary>
    /// Applies fallback heuristics when a tint transform function is not available
    /// </summary>
    private static void ApplySeparationFallback(string colorantName, string altSpace, double tint, ref string? colorSpaceName, ref List<double>? color)
    {
        // Handle special colorant names
        if (colorantName == "Black" || colorantName == "All" || colorantName == "None")
        {
            // For Black/All separations: tint=1 means black, tint=0 means white
            double value = 1.0 - tint;
            color = [value, value, value];
            colorSpaceName = "DeviceRGB";
        }
        else switch (altSpace)
        {
            case "DeviceRGB" or "CalRGB":
            {
                double value = 1.0 - tint;
                color = [value, value, value];
                colorSpaceName = "DeviceRGB";
                break;
            }
            case "DeviceGray" or "CalGray":
            {
                double value = 1.0 - tint;
                color = [value];
                colorSpaceName = "DeviceGray";
                break;
            }
            case "DeviceCMYK":
                color = [0.0, 0.0, 0.0, tint];
                colorSpaceName = "DeviceCMYK";
                break;
            default:
            {
                double value = 1.0 - tint;
                color = [value, value, value];
                colorSpaceName = "DeviceRGB";
                break;
            }
        }
    }

    /// <summary>
    /// Resolves a Lab color space: [/Lab &lt;&lt; /WhitePoint [...] /Range [...] &gt;&gt;]
    /// </summary>
    private void ResolveLab(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color, out double[]? proofCmyk)
    {
        proofCmyk = null;
        color ??= [];

        // Lab color space requires 3 components: L, a, b
        if (color.Count != 3)
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Lab: Expected 3 components (L,a,b), got {color.Count}");
            return;
        }

        double L = color[0];
        double a = color[1];
        double b = color[2];

        if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Lab: L={L:F2}, a={a:F2}, b={b:F2}");

        // Convert Lab to RGB using the existing LabToRgb helper
        double[] rgb = LabToRgb(L, a, b, csArray);

        proofCmyk = _proofResolver.TryLabToProofCmyk(L, a, b);
        color = [rgb[0], rgb[1], rgb[2]];
        colorSpaceName = "DeviceRGB";
    }

    /// <summary>
    /// Resolves a CalRGB color space: [/CalRGB &lt;&lt; /WhitePoint [...] /Gamma [...] /Matrix [...] &gt;&gt;]
    /// <para>
    /// The components ARE calibrated: <see cref="CalRgbConverter"/> applies the dictionary's
    /// <c>/Gamma</c> and <c>/Matrix</c> through the CIE XYZ pipeline and returns sRGB, which replaces
    /// <paramref name="color"/> in place. Only when the dictionary cannot be parsed
    /// (<c>FromCalRgbArray</c> returns null) do the raw components survive and get reinterpreted as
    /// DeviceRGB. Either way <paramref name="colorSpaceName"/> ends as <c>DeviceRGB</c> — that
    /// rename is the space substitution, NOT evidence that calibration was skipped.
    /// </para>
    /// </summary>
    private void ResolveCalRgb(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];

        // CalRGB requires 3 components: R, G, B
        if (color.Count != 3)
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE CalRGB: Expected 3 components, got {color.Count}");
            return;
        }

        // Calibrate through the CalRGB gamma + matrix to sRGB; fall back to a raw DeviceRGB
        // interpretation only if the dictionary can't be parsed.
        CalRgbConverter? converter = CalRgbConverter.FromCalRgbArray(csArray, document);
        if (converter is not null)
        {
            double[] rgb = converter.ToSrgb(color[0], color[1], color[2]);
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics,
                    $"RESOLVE CalRGB: ({color[0]:F3},{color[1]:F3},{color[2]:F3}) calibrated to sRGB ({rgb[0]:F3},{rgb[1]:F3},{rgb[2]:F3})");
            color = [rgb[0], rgb[1], rgb[2]];
        }

        colorSpaceName = "DeviceRGB";
    }

    /// <summary>
    /// Resolves a CalGray color space: [/CalGray &lt;&lt; /WhitePoint [...] /Gamma [...] &gt;&gt;]
    /// <para>
    /// The component IS calibrated: <see cref="CalGrayConverter"/> applies the dictionary's
    /// <c>/Gamma</c> and <c>/WhitePoint</c> and returns sRGB. Note the two exits differ in BOTH
    /// component count and space: on success the single grey is replaced by three sRGB components
    /// and <paramref name="colorSpaceName"/> becomes <c>DeviceRGB</c> (the calibrated white point
    /// need not be neutral in sRGB, so one channel cannot carry the result); only the
    /// unparseable-dictionary fallback leaves the lone raw component and names it
    /// <c>DeviceGray</c>.
    /// </para>
    /// </summary>
    private void ResolveCalGray(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];

        // CalGray requires 1 component: Gray value
        if (color.Count != 1)
        {
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE CalGray: Expected 1 component, got {color.Count}");
            return;
        }

        // Calibrate through the CalGray gamma to sRGB (a neutral grey); fall back to raw
        // DeviceGray only if the dictionary can't be parsed.
        CalGrayConverter? converter = CalGrayConverter.FromCalGrayArray(csArray, document);
        if (converter is not null)
        {
            double[] rgb = converter.ToSrgb(color[0]);
            if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE CalGray: {color[0]:F3} calibrated to sRGB ({rgb[0]:F3})");
            color = [rgb[0], rgb[1], rgb[2]];
            colorSpaceName = "DeviceRGB";
            return;
        }

        colorSpaceName = "DeviceGray";
    }

    /// <summary>
    /// Converts CIE Lab color values to RGB using Unicolour library
    /// </summary>
    /// <param name="L">L* value (0-100)</param>
    /// <param name="a">a* value (-128 to 127)</param>
    /// <param name="b">b* value (-128 to 127)</param>
    /// <param name="labArray">Optional Lab color space array with WhitePoint</param>
    /// <returns>RGB values in 0.0-1.0 range</returns>
    private static double[] LabToRgb(double L, double a, double b, PdfArray? labArray) =>
        LabToSrgb.Convert(L, a, b, LabWhitePoint(labArray));

    /// <summary>
    /// The reference white of a Lab colour space array, as declared by its <c>/WhitePoint</c>
    /// (ISO 32000-1 §8.6.5.4). Shared by the fill path and the image path so both interpret a Lab
    /// space identically.
    /// </summary>
    internal static ICCSharp.IO.XyzNumber LabWhitePoint(PdfArray? labArray)
    {
        // Default to D50 — the ICC PCS white, and the overwhelmingly common choice in print-oriented
        // PDFs — for the malformed case where /WhitePoint is missing or unusable.
        double xn = 0.9642, yn = 1.0, zn = 0.8249;  // D50 white point

        if (labArray is { Count: >= 2 } && labArray[1] is PdfDictionary labDict)
        {
            if (labDict.TryGetValue(new PdfName("WhitePoint"), out PdfObject? wpObj) && wpObj is PdfArray wpArray && wpArray.Count >= 3)
            {
                xn = wpArray[0] switch
                {
                    PdfInteger intVal => intVal.Value,
                    PdfReal realVal => realVal.Value,
                    _ => xn
                };
                yn = wpArray[1] switch
                {
                    PdfInteger intVal => intVal.Value,
                    PdfReal realVal => realVal.Value,
                    _ => yn
                };
                zn = wpArray[2] switch
                {
                    PdfInteger intVal => intVal.Value,
                    PdfReal realVal => realVal.Value,
                    _ => zn
                };

                // Reject only genuinely unusable values — a non-finite or non-positive Y divides by
                // zero in the Lab→XYZ step, and a negative X or Z is physically meaningless. Every
                // real illuminant is honoured as declared.
                //
                // This deliberately does NOT range-check against a particular illuminant. An earlier
                // version required zn >= 1.00, which D50 (0.8249) always fails, so the declared white
                // was silently replaced by D65. That was correct when this method ended in a
                // hard-coded D65 XYZ→sRGB matrix with no chromatic adaptation, but LabToSrgb now
                // Bradford-adapts from an arbitrary reference white — so the check rejected precisely
                // the case the converter exists to handle. Neutrals cancel the error, which is why it
                // went unnoticed; saturated colours drift badly (a mid blue by ~37/255).
                if (!IsUsableWhitePoint(xn, yn, zn))
                {
                    if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Unusable Lab WhitePoint [{xn:F6}, {yn:F6}, {zn:F6}], using D50 instead");
                    xn = 0.9642;
                    yn = 1.0;
                    zn = 0.8249;
                }
                else
                {
                    if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
                        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Using PDF WhitePoint [{xn:F6}, {yn:F6}, {zn:F6}]");
                }
            }
        }

        return new ICCSharp.IO.XyzNumber(xn, yn, zn);
    }

    /// <summary>
    /// The <c>/Range</c> of a Lab colour space — the limits of its a* and b* components — defaulting
    /// to <c>[-100 100 -100 100]</c> per ISO 32000-1 §8.6.5.4 Table 66 when absent or malformed.
    /// </summary>
    /// <remarks>
    /// This drives the default <c>/Decode</c> for a Lab IMAGE (§8.9.5.2: <c>[0 100 amin amax bmin
    /// bmax]</c>), so a wrong range silently rescales every a/b sample rather than failing outright.
    /// </remarks>
    internal static (double AMin, double AMax, double BMin, double BMax) LabRange(PdfArray? labArray)
    {
        if (labArray is { Count: >= 2 } && labArray[1] is PdfDictionary labDict
            && labDict.TryGetValue(new PdfName("Range"), out PdfObject? rangeObj)
            && rangeObj is PdfArray { Count: >= 4 } r)
        {
            double[] v = new double[4];
            for (var i = 0; i < 4; i++)
            {
                switch (r[i])
                {
                    case PdfInteger n: v[i] = n.Value; break;
                    case PdfReal n: v[i] = n.Value; break;
                    default: return (-100, 100, -100, 100);   // non-numeric entry — fall back whole
                }
            }
            // A reversed or degenerate interval would invert or flatten the axis; treat as malformed.
            if (v[1] > v[0] && v[3] > v[2] && double.IsFinite(v[0]) && double.IsFinite(v[1])
                && double.IsFinite(v[2]) && double.IsFinite(v[3]))
            {
                return (v[0], v[1], v[2], v[3]);
            }
        }

        return (-100, 100, -100, 100);
    }

    /// <summary>
    /// Whether a Lab <c>/WhitePoint</c> can be used as a reference white. Y must be strictly positive
    /// (it is the divisor in the Lab→XYZ step) and X and Z non-negative; all three must be finite.
    /// </summary>
    private static bool IsUsableWhitePoint(double x, double y, double z) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z)
        && y > 0.0 && x >= 0.0 && z >= 0.0;
}
