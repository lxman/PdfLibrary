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
        if (string.IsNullOrEmpty(colorSpaceName))
            return;

        // Ensure the color list exists
        color ??= [];

        // DIAGNOSTIC: Log every color space resolution attempt
        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE START: colorSpaceName='{colorSpaceName}', color=[{string.Join(", ", color.Select(c => c.ToString("F3")))}]");

        // Skip device color spaces - they don't need resolution
        if (colorSpaceName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK")
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: '{colorSpaceName}' is device color space, skipping");
            return;
        }

        // Try to resolve named color space from resources
        if (colorSpaces is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: No ColorSpace dict in resources for '{colorSpaceName}'");
            return;
        }

        // DIAGNOSTIC: Log what's in the ColorSpace dictionary
        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: ColorSpace dict has {colorSpaces.Keys.Count} entries: [{string.Join(", ", colorSpaces.Keys.Take(10).Select(k => k.Value))}]");

        if (!colorSpaces.TryGetValue(new PdfName(colorSpaceName), out PdfObject? csObj))
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: ColorSpace '{colorSpaceName}' not found in dict");
            return;
        }

        // DIAGNOSTIC: Log successful lookup
        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Found '{colorSpaceName}' in dict, type={csObj?.GetType().Name ?? "NULL"}");

        // Resolve indirect reference
        if (csObj is PdfIndirectReference reference && document is not null)
        {
            csObj = document.ResolveReference(reference);
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Resolved indirect reference for '{colorSpaceName}', type={csObj?.GetType().Name ?? "NULL"}");
        }

        // DIAGNOSTIC: Log PdfArray contents
        if (csObj is PdfArray debugArray)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: PdfArray Count={debugArray.Count}");
            if (debugArray.Count > 0)
            {
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: PdfArray[0] type={debugArray[0]?.GetType().Name ?? "NULL"}, value={debugArray[0]}");
            }
            if (debugArray.Count > 1)
            {
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: PdfArray[1] type={debugArray[1]?.GetType().Name ?? "NULL"}");
            }
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
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Pattern color space detected (colored, underlying={patternArray[1]})");
                return;
        }

        // Parse color space array
        // Can be: [/ICCBased stream] or [/Separation name alternateSpace tintTransform]
        if (csObj is not PdfArray { Count: >= 2 } csArray || csArray[0] is not PdfName csType)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Early return - condition failed. csObj is PdfArray: {csObj is PdfArray}, count check: {(csObj as PdfArray)?.Count >= 2}, [0] is PdfName: {(csObj as PdfArray)?[0] is PdfName}");
            return;
        }

        // DIAGNOSTIC: Log what we're switching on
        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Entering switch with csType.Value='{csType.Value}', csArray.Count={csArray.Count}");

        switch (csType.Value)
        {
            case "ICCBased" when csArray.Count >= 2:
                ResolveICCBased(csArray, ref colorSpaceName, ref color, blackPointCompensation, renderingIntent);
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
                ResolveLab(csArray, ref colorSpaceName, ref color);
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
    private void ResolveICCBased(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color, bool blackPointCompensation = false, string? renderingIntent = null)
    {
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

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Separation: colorant='{colorantName}', altSpace='{altSpace}', tintTransform={tintTransform?.GetType().Name ?? "NULL"}, color.Count={color.Count}");

        if (color.Count == 1)
        {
            double tint = color[0];

            // Try to use the tint transform function
            if (tintTransform is not null)
            {
                double[] result = tintTransform.Evaluate([tint]);
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Separation '{colorantName}' -> tint={tint:F3} -> function result=[{string.Join(", ", result.Select(r => r.ToString("F3")))}]");
                ApplyTintTransformResult(result, altSpace, alternateObj, ref colorSpaceName, ref color);
            }
            else
            {
                // Fallback: simple heuristic when tint transform not available
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Separation '{colorantName}' -> using fallback (no tint transform), tint={tint:F3}");
                ApplySeparationFallback(colorantName, altSpace, tint, ref colorSpaceName, ref color);
            }
        }

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Separation END: colorSpaceName='{colorSpaceName}', color=[{string.Join(", ", color?.Select(c => c.ToString("F3")) ?? [])}]");
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
        if (baseArray.Count < 4 || baseArray[0] is not PdfName { Value: "Separation" or "DeviceN" } head)
            return null;

        if (head.Value == "Separation")
            inputComponents = 1;
        else if (Deref(baseArray[1], document) is PdfArray names)
            inputComponents = names.Count;   // DeviceN: one input per colorant name
        else
            return null;
        if (inputComponents < 1) return null;

        PdfObject altObj = Deref(baseArray[2], document);
        string altSpace = altObj switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty
        };
        if (altSpace.Length == 0) return null;
        PdfArray? labArray = altSpace == "Lab" ? altObj as PdfArray : null;

        PdfFunction? tint = PdfFunction.Create(Deref(baseArray[3], document), document);
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
        if (baseArray.Count < 4 || baseArray[0] is not PdfName { Value: "Separation" or "DeviceN" } head)
            return null;

        if (head.Value == "Separation")
            inputComponents = 1;
        else if (Deref(baseArray[1], document) is PdfArray names)
            inputComponents = names.Count;
        else
            return null;
        if (inputComponents < 1) return null;

        PdfObject altObj = Deref(baseArray[2], document);
        string altSpace = altObj switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty
        };
        if (altSpace != "DeviceCMYK") return null;   // only a CMYK alternate yields native CMYK

        PdfFunction? tint = PdfFunction.Create(Deref(baseArray[3], document), document);
        if (tint is null) return null;

        return colorants =>
        {
            double[] r = tint.Evaluate(colorants);
            static double C01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
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
        if (baseArray.Count < 4 || colorantIndex < 0 || colorantIndex >= inputCount)
            return (null, (0, 0, 0));

        PdfFunction? tint = PdfFunction.Create(Deref(baseArray[3], doc), doc);
        if (tint is null) return (null, (0, 0, 0));

        var ramp = new double[samples][];
        var input = new double[inputCount];
        for (var s = 0; s < samples; s++)
        {
            double t = samples == 1 ? 0.0 : (double)s / (samples - 1);
            Array.Clear(input);
            input[colorantIndex] = t;
            ramp[s] = tint.Evaluate((double[])input.Clone());
        }

        // Representative solid via the existing sRGB evaluator (Lab-aware), at tint = 1.
        (byte R, byte G, byte B) solid = (0, 0, 0);
        Func<double[], (byte R, byte G, byte B)>? toRgb = BuildTintToRgb(baseArray, doc, out int _);
        if (toRgb is not null)
        {
            Array.Clear(input);
            input[colorantIndex] = 1.0;
            solid = toRgb((double[])input.Clone());
        }
        return (ramp, solid);
    }

    private static PdfObject Deref(PdfObject obj, PdfDocument? document) =>
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
        if (csObj is not PdfArray { Count: >= 2 } csArray || csArray[0] is not PdfName csType)
            return null;

        // Gather colorant names from Separation (single name) or DeviceN (names array).
        List<string> colorants;
        switch (csType.Value)
        {
            case "Separation":
                if (Deref(csArray[1], doc) is not PdfName sepName)
                    return null;
                colorants = [sepName.Value];
                break;
            case "DeviceN":
                if (Deref(csArray[1], doc) is not PdfArray namesArr)
                    return null;
                colorants = new List<string>(namesArr.Count);
                foreach (PdfObject nameObj in namesArr)
                {
                    if (Deref(nameObj, doc) is not PdfName n)
                        return null;
                    colorants.Add(n.Value);
                }
                break;
            case "Indexed":
                // An Indexed image's samples are palette indices into its base space; the plates it marks
                // are the base space's plates (e.g. an Indexed[/DeviceN[Black Cyan]] duotone marks K + C).
                return PlatesForColorSpaceObject(csArray[1], doc);
            default:
                return null;
        }

        if (colorants.Count == 0)
            return null;

        bool c = false, m = false, y = false, k = false;
        foreach (string name in colorants)
        {
            switch (name)
            {
                case "Cyan": c = true; break;
                case "Magenta": m = true; break;
                case "Yellow": y = true; break;
                case "Black": k = true; break;
                case "All": c = m = y = k = true; break;
                case "None": break;   // marks no colorant (ISO 32000 §8.6.6.4) — skip; used as DeviceN padding
                default:
                    // A real spot colorant isn't a CMYK plate → fall back to OPM behaviour.
                    return null;
            }
        }

        return (c, m, y, k);
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
        if (csObj is null) return null;
        csObj = Deref(csObj, doc);
        if (csObj is not PdfArray { Count: >= 4 } csArray || csArray[0] is not PdfName csType) return null;

        List<string> names;
        switch (csType.Value)
        {
            case "Separation":
                if (Deref(csArray[1], doc) is not PdfName sepName) return null;
                names = [sepName.Value];
                break;
            case "DeviceN":
                if (Deref(csArray[1], doc) is not PdfArray namesArr) return null;
                names = new List<string>(namesArr.Count);
                foreach (PdfObject nameObj in namesArr)
                {
                    if (Deref(nameObj, doc) is not PdfName n) return null;
                    names.Add(n.Value);
                }
                break;
            default:
                return null;
        }
        if (names.Count == 0) return null;

        PdfObject altObj = Deref(csArray[2], doc);
        string altSpace = altObj switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty
        };

        double[] tints = rawColor is null ? [] : [.. rawColor];
        return new ColorantOrigin(names, tints, altSpace);
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
        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed LOOKUP OBJECT: type={lookupObj?.GetType().Name ?? "NULL"}, value={(lookupObj is PdfString s ? $"PdfString len={s.Bytes.Length}" : lookupObj?.ToString() ?? "null")}");

        if (lookupObj is PdfIndirectReference lookupRef && document is not null)
            lookupObj = document.ResolveReference(lookupRef);

        byte[]? paletteData = lookupObj switch
        {
            PdfString lookupString => lookupString.Bytes,
            PdfStream lookupStream => lookupStream.GetDecodedData(document?.Decryptor),
            _ => null
        };

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed PALETTE DATA: paletteData={(paletteData is not null ? $"len={paletteData.Length}" : "NULL")}");

        if (paletteData is null)
        {
            PdfLogger.Log(LogCategory.Graphics, "RESOLVE Indexed: No palette data found");
            return;
        }

        // DIAGNOSTIC: Log palette data extraction for debugging
        if (paletteData.Length >= 10)
        {
            string bytesHex = string.Join(" ", paletteData.Take(20).Select(b => b.ToString("X2")));
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed PALETTE BYTES: first 20 bytes=[{bytesHex}]");
        }

        // Validate palette index
        if (paletteIndex < 0 || paletteIndex > hival)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed: Palette index {paletteIndex} out of range [0, {hival}]");
            paletteIndex = Math.Clamp(paletteIndex, 0, hival);
        }

        // Extract color components from the palette
        // It is stored as consecutive bytes: [C1_0, C2_0, C3_0, C1_1, C2_1, C3_1, ...]
        int bytesPerEntry = baseComponents;
        int byteOffset = paletteIndex * bytesPerEntry;

        if (byteOffset + bytesPerEntry > paletteData.Length)
        {
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

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed: index={paletteIndex}, base={baseColorSpace}, components={baseComponents}, palette color=[{string.Join(", ", paletteColor.Select(c => c.ToString("F3")))}]");

        // Now we have the color from the palette, resolve the base color space if needed
        color = paletteColor;

        // If the base color space is ICCBased or another complex type, resolve it recursively
        if (baseObj is PdfArray baseArray2 and [PdfName { Value: "ICCBased" }, _, ..])
        {
            ResolveICCBased(baseArray2, ref baseColorSpace, ref color);
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

        PdfLogger.Log(LogCategory.Graphics,
            $"RESOLVE Indexed END: colorSpaceName='{colorSpaceName}', color=[{string.Join(", ", color.Select(c => c.ToString("F3")))}]");
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
    private void ResolveLab(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];

        // Lab color space requires 3 components: L, a, b
        if (color.Count != 3)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Lab: Expected 3 components (L,a,b), got {color.Count}");
            return;
        }

        double L = color[0];
        double a = color[1];
        double b = color[2];

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Lab: L={L:F2}, a={a:F2}, b={b:F2}");

        // Convert Lab to RGB using the existing LabToRgb helper
        double[] rgb = LabToRgb(L, a, b, csArray);

        color = [rgb[0], rgb[1], rgb[2]];
        colorSpaceName = "DeviceRGB";

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Lab END: R={rgb[0]:F3}, G={rgb[1]:F3}, B={rgb[2]:F3}");
    }

    /// <summary>
    /// Resolves a CalRGB color space: [/CalRGB &lt;&lt; /WhitePoint [...] /Gamma [...] /Matrix [...] &gt;&gt;]
    /// For now, we simply pass through to DeviceRGB
    /// </summary>
    private void ResolveCalRgb(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];

        // CalRGB requires 3 components: R, G, B
        if (color.Count != 3)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE CalRGB: Expected 3 components, got {color.Count}");
            return;
        }

        // Calibrate through the CalRGB gamma + matrix to sRGB; fall back to a raw DeviceRGB
        // interpretation only if the dictionary can't be parsed.
        CalRgbConverter? converter = CalRgbConverter.FromCalRgbArray(csArray, document);
        if (converter is not null)
        {
            double[] rgb = converter.ToSrgb(color[0], color[1], color[2]);
            PdfLogger.Log(LogCategory.Graphics,
                $"RESOLVE CalRGB: ({color[0]:F3},{color[1]:F3},{color[2]:F3}) calibrated to sRGB ({rgb[0]:F3},{rgb[1]:F3},{rgb[2]:F3})");
            color = [rgb[0], rgb[1], rgb[2]];
        }

        colorSpaceName = "DeviceRGB";
    }

    /// <summary>
    /// Resolves a CalGray color space: [/CalGray &lt;&lt; /WhitePoint [...] /Gamma [...] &gt;&gt;]
    /// For now, we simply pass through to DeviceGray
    /// </summary>
    private void ResolveCalGray(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
    {
        color ??= [];

        // CalGray requires 1 component: Gray value
        if (color.Count != 1)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE CalGray: Expected 1 component, got {color.Count}");
            return;
        }

        // Calibrate through the CalGray gamma to sRGB (a neutral grey); fall back to raw
        // DeviceGray only if the dictionary can't be parsed.
        CalGrayConverter? converter = CalGrayConverter.FromCalGrayArray(csArray, document);
        if (converter is not null)
        {
            double[] rgb = converter.ToSrgb(color[0]);
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
    private static double[] LabToRgb(double L, double a, double b, PdfArray? labArray)
    {
        // Get white point from Lab color space definition (default to D65)
        double xn = 0.95047, yn = 1.0, zn = 1.08883;  // D65 white point

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

                // Validate WhitePoint values - if they seem incorrect, fall back to D65
                // D65 should have approximately: xn≈0.95, yn=1.0, zn≈1.09
                if (xn < 0.90 || xn > 1.00 || yn < 0.95 || yn > 1.05 || zn < 1.00 || zn > 1.20)
                {
                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Invalid Lab WhitePoint [{xn:F6}, {yn:F6}, {zn:F6}] detected, using D65 instead");
                    xn = 0.95047;
                    yn = 1.0;
                    zn = 1.08883;
                }
                else
                {
                    PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Using PDF WhitePoint [{xn:F6}, {yn:F6}, {zn:F6}]");
                }
            }
        }

        // Convert Lab → sRGB respecting the PDF-specified WhitePoint. Falls back to the D65 values
        // initialised at the top of this method if WhitePoint is missing.
        return LabToSrgb.Convert(L, a, b, new ICCSharp.IO.XyzNumber(xn, yn, zn));
    }
}
