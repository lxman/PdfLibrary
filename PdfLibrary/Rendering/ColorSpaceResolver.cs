using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Functions;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Resolves PDF color spaces to device color spaces (DeviceRGB, DeviceCMYK, DeviceGray)
/// Handles ICCBased, Separation, and other color space conversions
/// </summary>
public class ColorSpaceResolver(PdfDocument? document)
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

        // Skip device color spaces - they don't need resolution
        if (colorSpaceName is "DeviceGray" or "DeviceRGB" or "DeviceCMYK")
            return;

        // Try to resolve named color space from resources
        if (colorSpaces is null)
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: No ColorSpace dict in resources for '{colorSpaceName}'");
            return;
        }

        if (!colorSpaces.TryGetValue(new PdfName(colorSpaceName), out PdfObject? csObj))
        {
            PdfLogger.Log(LogCategory.Graphics, $"RESOLVE: ColorSpace '{colorSpaceName}' not found in dict (has {colorSpaces.Keys.Count} entries: [{string.Join(", ", colorSpaces.Keys.Take(10).Select(k => k.Value))}])");
            return;
        }

        // Resolve indirect reference
        if (csObj is PdfIndirectReference reference && document is not null)
            csObj = document.ResolveReference(reference);

        // Parse color space array
        // Can be: [/ICCBased stream] or [/Separation name alternateSpace tintTransform]
        if (csObj is PdfArray { Count: >= 2 } csArray)
        {
            if (csArray[0] is not PdfName csType)
                return;

            switch (csType.Value)
            {
                case "ICCBased" when csArray.Count >= 2:
                    ResolveICCBased(csArray, ref colorSpaceName, ref color);
                    break;

                case "Separation" when csArray.Count >= 4:
                    ResolveSeparation(csArray, ref colorSpaceName, ref color);
                    break;
            }
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

        // Initialize default colors if component count doesn't match
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
        PdfObject alternateObj = csArray[2];
        if (alternateObj is PdfIndirectReference altRef && document is not null)
            alternateObj = document.ResolveReference(altRef);

        string altSpace;

        if (alternateObj is PdfName altName)
        {
            altSpace = altName.Value;
        }
        else if (alternateObj is PdfArray altArray && altArray.Count >= 1 && altArray[0] is PdfName altArrayType)
        {
            // Handle array-based alternate spaces like [/CalRGB <<...>>], [/CalGray <<...>>], [/Lab <<...>>]
            altSpace = altArrayType.Value;
        }
        else
        {
            // Unknown alternate space format
            return;
        }

        // Get the tint transform function (index 3)
        PdfObject? tintTransformObj = csArray[3];
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

                if (result.Length >= 3)
                {
                    // RGB output from tint transform
                    color = [result[0], result[1], result[2]];
                    colorSpaceName = altSpace is "CalRGB" ? "DeviceRGB" : altSpace;
                    if (colorSpaceName != "DeviceRGB" && colorSpaceName != "DeviceCMYK" && colorSpaceName != "DeviceGray")
                        colorSpaceName = "DeviceRGB";
                }
                else if (result.Length == 1)
                {
                    // Grayscale output
                    color = [result[0]];
                    colorSpaceName = "DeviceGray";
                }
                else if (result.Length == 4)
                {
                    // CMYK output
                    color = [result[0], result[1], result[2], result[3]];
                    colorSpaceName = "DeviceCMYK";
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
        else if (altSpace is "DeviceRGB" or "CalRGB")
        {
            double value = 1.0 - tint;
            color = [value, value, value];
            colorSpaceName = "DeviceRGB";
        }
        else if (altSpace is "DeviceGray" or "CalGray")
        {
            double value = 1.0 - tint;
            color = [value];
            colorSpaceName = "DeviceGray";
        }
        else if (altSpace == "DeviceCMYK")
        {
            color = [0.0, 0.0, 0.0, tint];
            colorSpaceName = "DeviceCMYK";
        }
        else
        {
            double value = 1.0 - tint;
            color = [value, value, value];
            colorSpaceName = "DeviceRGB";
        }
    }
}
