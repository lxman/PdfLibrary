using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Functions;
using PdfLibrary.Structure;
using Wacton.Unicolour;

namespace PdfLibrary.Rendering;

/// <summary>
/// Resolves PDF color spaces to device color spaces (DeviceRGB, DeviceCMYK, DeviceGray)
/// Handles ICCBased, Separation, and other color space conversions
/// </summary>
internal class ColorSpaceResolver(PdfDocument? document)
{
    /// <summary>
    /// Resolves a named color space from resources to a device color space
    /// </summary>
    /// <param name="colorSpaceName">The color space name (may be modified to device color space)</param>
    /// <param name="color">The color components (may be modified based on color space conversion)</param>
    /// <param name="colorSpaces">The ColorSpace resource dictionary</param>
    public void ResolveColorSpace(ref string? colorSpaceName, ref List<double>? color, PdfDictionary? colorSpaces)
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
                ResolveICCBased(csArray, ref colorSpaceName, ref color);
                break;

            case "Separation" when csArray.Count >= 4:
                ResolveSeparation(csArray, ref colorSpaceName, ref color);
                break;

            case "Indexed" when csArray.Count >= 4:
                ResolveIndexed(csArray, ref colorSpaceName, ref color);
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
    private void ResolveICCBased(PdfArray csArray, ref string? colorSpaceName, ref List<double>? color)
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

        // Get /Alternate color space
        string? alternateSpace = null;
        if (streamDict.TryGetValue(new PdfName("Alternate"), out PdfObject altObj))
        {
            if (altObj is PdfName altName)
            {
                alternateSpace = altName.Value;
            }
        }

        // For now, use the alternate color space directly with the color values
        // This is a simplification - ideally we'd process the ICC profile
        if (alternateSpace is not null)
        {
            colorSpaceName = alternateSpace;
        }
        else
        {
            // No alternate specified, infer from component count
            colorSpaceName = numComponents switch
            {
                1 => "DeviceGray",
                3 => "DeviceRGB",
                4 => "DeviceCMYK",
                _ => "DeviceGray"
            };
        }

        // Initialize default colors if the component count doesn't match
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

                switch (result.Length)
                {
                    case 4:
                    {
                        // CMYK output
                        color = [result[0], result[1], result[2], result[3]];
                        colorSpaceName = "DeviceCMYK";
                        break;
                    }
                    case >= 3:
                    {
                        // Check if this is Lab color space
                        if (altSpace == "Lab" && result.Length == 3)
                        {
                            // Convert Lab to RGB
                            double[] rgb = LabToRgb(result[0], result[1], result[2], alternateObj as PdfArray);
                            color = [rgb[0], rgb[1], rgb[2]];
                            colorSpaceName = "DeviceRGB";
                            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Lab -> RGB conversion: L={result[0]:F2}, a={result[1]:F2}, b={result[2]:F2} -> R={rgb[0]:F3}, G={rgb[1]:F3}, B={rgb[2]:F3}");
                        }
                        else
                        {
                            // RGB output from tint transform
                            color = [result[0], result[1], result[2]];
                            colorSpaceName = altSpace is "CalRGB" ? "DeviceRGB" : altSpace;
                            if (colorSpaceName != "DeviceRGB" && colorSpaceName != "DeviceCMYK" && colorSpaceName != "DeviceGray")
                                colorSpaceName = "DeviceRGB";
                        }
                        break;
                    }
                    case 1:
                        // Grayscale output
                        color = [result[0]];
                        colorSpaceName = "DeviceGray";
                        break;
                    default:
                        // Unexpected component count
                        break;
                }
            }
            else
            {
                // Fallback: simple heuristic when tint transform not available
                PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: Separation '{colorantName}' -> using fallback (no tint transform), tint={tint:F3}");
                ApplySeparationFallback(colorantName, altSpace, tint, ref colorSpaceName, ref color);
            }
        }

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Separation END: colorSpaceName='{colorSpaceName}', color=[{string.Join(", ", color.Select(c => c.ToString("F3")))}]");
    }

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

        PdfLogger.Log(LogCategory.Graphics, $"RESOLVE Indexed PALETTE DATA: paletteData={(paletteData != null ? $"len={paletteData.Length}" : "NULL")}");

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

        // Use Unicolour for Lab→RGB conversion
        // Note: Using default D65 white point from Unicolour for now
        // TODO: Investigate how to properly configure custom white points in Unicolour if needed

        // Create Lab color and convert to RGB (Unicolour expects Lab in range: L=0-100, a/b=-128 to 127)
        var unicolour = new Unicolour(ColourSpace.Lab, L, a, b);
        var rgb = unicolour.Rgb;

        // RGB values are already in 0.0-1.0 range and clamped by Unicolour
        return [rgb.R, rgb.G, rgb.B];
    }
}
