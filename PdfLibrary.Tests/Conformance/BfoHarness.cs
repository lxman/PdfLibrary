using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Locates the BFO PDF/A test suite (<c>https://github.com/bfosupport/pdfa-testsuite</c>) at the sibling
/// <c>../pdfa-testsuite</c> and turns its file names into typed cases. An independent second conformance
/// oracle alongside the veraPDF corpus: BFO authored these fixtures from the ISO text directly, so agreement
/// with our verdicts is real cross-validation. File names encode the target and expected outcome —
/// <c>pdfa{part}-{clause}-bfo-t{NN}-{pass|fail}.pdf</c> — so the part maps to a profile (pdfa2 → PDF/A-2b,
/// pdfa3 → PDF/A-3b; pdfa1 is skipped, as PDF/A-1 is not a target). Absent on CI, so consumers are
/// <c>[Trait("Category","LocalOnly")]</c>. Set <c>BFO_SUITE</c> to override the location.
/// </summary>
internal static class BfoHarness
{
    private static readonly Lazy<string?> RootLazy = new(Locate);

    public static string? Root => RootLazy.Value;

    public static bool IsAvailable => Root is not null;

    /// <summary>A BFO fixture: its path, bare name, the profile its part maps to, and whether it must conform.</summary>
    public readonly record struct BfoCase(string Path, string Name, ConformanceProfile Profile, bool ExpectPass);

    /// <summary>Every BFO fixture whose part maps to a supported profile, in stable name order.</summary>
    public static IEnumerable<BfoCase> Cases()
    {
        if (Root is null)
            yield break;

        foreach (string path in Directory.EnumerateFiles(Root, "*.pdf", SearchOption.AllDirectories)
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("pdfa", StringComparison.Ordinal) || name.Length < 5)
                continue;

            ConformanceProfile? profile = name[4] switch
            {
                '2' => ConformanceProfile.PdfA2b,
                '3' => ConformanceProfile.PdfA3b,
                _ => null, // pdfa1 (or anything else) — not a target
            };
            if (profile is null)
                continue;

            bool? expectPass = name.EndsWith("-pass", StringComparison.Ordinal) ? true
                : name.EndsWith("-fail", StringComparison.Ordinal) ? false
                : null;
            if (expectPass is null)
                continue;

            yield return new BfoCase(path, name, profile.Value, expectPass.Value);
        }
    }

    private static string? Locate()
    {
        string? env = Environment.GetEnvironmentVariable("BFO_SUITE");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "pdfa-testsuite");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
