using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Locates the Ghent PDF Output Suite (GOS) checkout at the sibling <c>../gwg-gos</c> and enumerates its
/// PDF/X-4 files (file-name suffix <c>_X4</c>/<c>_x4</c>; the <c>_x3</c>/<c>_x1a</c> patches target older
/// PDF/X flavours and are excluded). These are VALID PDF/X-4 files — a <b>pass-oracle only</b>: GOS ships
/// no deliberately-broken fixtures, and no public PDF/X-4 fail corpus exists (ISO 15930-7 is paywalled),
/// so slice-9 fail detection is covered by synthetic tests instead. Absent on CI, so consumers are
/// <c>[Trait("Category","LocalOnly")]</c>. Set <c>GWG_GOS</c> to override the location.
/// </summary>
internal static class GwgGosHarness
{
    private static readonly Lazy<string?> RootLazy = new(Locate);

    public static string? Root => RootLazy.Value;

    public static bool IsAvailable => Root is not null;

    /// <summary>Every PDF/X-4 file under the GOS checkout, in stable path order.</summary>
    public static IEnumerable<string> PdfX4Files()
    {
        if (Root is null)
            yield break;

        foreach (string path in Directory.EnumerateFiles(Root, "*.pdf", SearchOption.AllDirectories)
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (name.Contains("_X4") || name.Contains("_x4"))
                yield return path;
        }
    }

    private static string? Locate()
    {
        string? env = Environment.GetEnvironmentVariable("GWG_GOS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "gwg-gos");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
