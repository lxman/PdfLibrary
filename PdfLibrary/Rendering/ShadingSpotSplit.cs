using PdfLibrary.Document;

namespace PdfLibrary.Rendering;

/// <summary>
/// Splits a shading stop's colour-space components (aligned to its colorant Names) into a process-only
/// packed CMYK plate value (0xCCMMYYKK — process colorant names at their component value) and per-spot
/// tint bytes (Soft-Proof SP-7). Mirrors SP-6a's per-name image split: no tint transform is used — the
/// spot's alternate is applied once at display via the registry ramp.
/// </summary>
internal static class ShadingSpotSplit
{
    /// <summary>The spot-kind colorant names (per <see cref="PageColorant.Classify"/>), in order.</summary>
    public static List<string> SpotNames(IReadOnlyList<string> names)
    {
        var spots = new List<string>();
        foreach (string n in names)
            if (PageColorant.Classify(n) == ColorantKind.Spot) spots.Add(n);
        return spots;
    }

    /// <summary>Splits <paramref name="comps"/> (aligned to <paramref name="names"/>) into a packed
    /// process-only CMYK (returned) and per-spot tint bytes written to <paramref name="spotDest"/> at
    /// <paramref name="destOffset"/> + s (s in SpotNames order).</summary>
    public static uint Split(double[] comps, IReadOnlyList<string> names, byte[] spotDest, int destOffset)
    {
        double c = 0, m = 0, y = 0, k = 0;
        var s = 0;
        for (var j = 0; j < names.Count; j++)
        {
            double v = j < comps.Length ? comps[j] : 0.0;
            switch (PageColorant.Classify(names[j]))
            {
                case ColorantKind.Process:
                    switch (names[j])
                    {
                        case "Cyan": c = v; break;
                        case "Magenta": m = v; break;
                        case "Yellow": y = v; break;
                        case "Black": k = v; break;
                    }
                    break;
                case ColorantKind.All or ColorantKind.None: break;   // recognised, not a plate
                default: spotDest[destOffset + s] = B(v); s++; break;   // spot
            }
        }
        return ((uint)B(c) << 24) | ((uint)B(m) << 16) | ((uint)B(y) << 8) | B(k);
    }

    private static byte B(double v) => (byte)Math.Clamp(Math.Round(v * 255.0), 0, 255);
}
