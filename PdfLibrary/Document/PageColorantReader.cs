using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Reads a page's distinct named Separation/DeviceN colorants (Soft-Proof SP-1) from its
/// <c>/Resources/ColorSpace</c> dictionary into public <see cref="PageColorant"/>s. Colorants declared
/// only inside XObject/Pattern sub-resources are not walked here — they are captured by the per-op
/// <see cref="ColorantOrigin"/> during rendering. All/None colorants are recognised but not emitted.
/// </summary>
internal static class PageColorantReader
{
    public static IReadOnlyList<PageColorant> Read(PdfDocument document, int pageIndex)
    {
        var result = new List<PageColorant>();
        PdfPage? page = document.GetPage(pageIndex);
        PdfDictionary? colorSpaces = page?.GetResources()?.GetColorSpaces();
        if (colorSpaces is null) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PdfName key in colorSpaces.Keys)
        {
            if (colorSpaces.TryGetValue(key, out PdfObject? defObj))
                AddColorants(defObj, document, seen, result);
        }
        return result;
    }

    private static void AddColorants(PdfObject? defObj, PdfDocument document, HashSet<string> seen,
        List<PageColorant> result, int depth = 0)
    {
        if (depth > 8) return;                                   // bounded Indexed/base unwrap
        PdfObject? resolved = Deref(defObj, document);
        if (resolved is not PdfArray arr || arr.Count == 0) return;

        // Indexed carries its colorants in the base space: [/Indexed base hival lookup] → recurse `base`.
        if (arr[0] is PdfName { Value: "Indexed" } && arr.Count >= 2)
        {
            AddColorants(arr[1], document, seen, result, depth + 1);
            return;
        }

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(resolved, null, document);
        if (origin is null) return;

        for (var i = 0; i < origin.Names.Count; i++)
        {
            string name = origin.Names[i];
            ColorantKind kind = PageColorant.Classify(name);
            if (kind is ColorantKind.All or ColorantKind.None) continue; // recognised, not a plate
            if (!seen.Add(name)) continue;

            (double[][]? ramp, (byte R, byte G, byte B) solid) =
                ColorSpaceResolver.BuildTintRamp(arr, document, i, origin.Names.Count);
            result.Add(new PageColorant(name, kind, origin.AlternateSpace, ramp, solid));
        }
    }

    private static PdfObject? Deref(PdfObject? obj, PdfDocument document) =>
        obj is PdfIndirectReference r ? document.ResolveReference(r) ?? obj : obj;
}
