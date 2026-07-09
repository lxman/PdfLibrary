using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Locates and enumerates the external veraPDF conformance corpus (a sibling <c>../veraPDF-corpus</c>
/// checkout, ~74 MB, deliberately NOT vendored — see
/// <c>Docs/plans/2026-07-06-conformance-preflight-read-api-audit.md</c>). Every corpus file name encodes
/// its ISO clause, test number, and expected verdict, e.g. <c>"veraPDF test suite 6-1-13-t09-fail-e.pdf"</c>.
/// Slice 10 (the corpus harness) uses this to drive the <see cref="Preflighter"/> against real fixtures.
///
/// The corpus is absent on CI and on fresh clones, so tests that consume it are
/// <c>[Trait("Category","LocalOnly")]</c> and skip via <see cref="IsAvailable"/>. Set the
/// <c>VERAPDF_CORPUS</c> environment variable to point at a checkout in a non-default location.
/// </summary>
internal static class CorpusHarness
{
    /// <summary>One corpus fixture: its parsed clause/test identity and the verdict its name asserts.</summary>
    internal sealed record CorpusCase(
        string Path,
        string Clause,               // dotted ISO clause, e.g. "6.1.13"
        int Test,                    // the tNN number
        char Variant,                // the trailing a/b/c… disambiguator
        bool ExpectedPass,           // filename says -pass- (true) or -fail- (false)
        ConformanceProfile Profile)
    {
        /// <summary>Clause + test, e.g. <c>"6.1.13-t9"</c> — the stable veraPDF rule identifier.</summary>
        public string RuleKey => $"{Clause}-t{Test}";

        public string FileName => System.IO.Path.GetFileName(Path);

        public override string ToString() => FileName;
    }

    /// <summary>Outcome of running the preflighter over one fixture, tolerant of loader failures.</summary>
    internal readonly record struct Evaluation(bool Conforms, IReadOnlyList<string> RuleIds, string? LoadError)
    {
        /// <summary>True when the file was rejected outright by the loader (encryption, malformed structure…).</summary>
        public bool LoadFailed => LoadError is not null;
    }

    // veraPDF filename tail: "…{clause dashed}-t{NN}-{pass|fail}-{letter}.pdf".
    private static readonly Regex NamePattern = new(
        @"(?<clause>\d+(?:-\d+)*)-t(?<test>\d+)-(?<result>pass|fail)-(?<variant>[a-z])\.pdf$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<ConformanceProfile, string> ProfileFolders =
        new Dictionary<ConformanceProfile, string>
        {
            [ConformanceProfile.PdfA2b] = "PDF_A-2b",
            [ConformanceProfile.PdfA2u] = "PDF_A-2u",
            [ConformanceProfile.PdfA3b] = "PDF_A-3b",
            [ConformanceProfile.PdfUA1] = "PDF_UA-1",
        };

    private static readonly Lazy<string?> RootLazy = new(Locate);

    /// <summary>The corpus root directory, or null when it is not present on this machine.</summary>
    public static string? Root => RootLazy.Value;

    /// <summary>True when the corpus is available to drive tests.</summary>
    public static bool IsAvailable => Root is not null;

    /// <summary>The supported PDF/A profiles that actually have a corpus folder present.</summary>
    public static IEnumerable<ConformanceProfile> SupportedProfiles =>
        Root is null
            ? []
            : ProfileFolders.Where(kv => Directory.Exists(System.IO.Path.Combine(Root, kv.Value)))
                            .Select(kv => kv.Key);

    /// <summary>Every parseable fixture under the given profile's corpus folder, in stable path order.</summary>
    public static IEnumerable<CorpusCase> Enumerate(ConformanceProfile profile)
    {
        if (Root is null || !ProfileFolders.TryGetValue(profile, out string? folder))
            yield break;

        string dir = System.IO.Path.Combine(Root, folder);
        if (!Directory.Exists(dir))
            yield break;

        foreach (string path in Directory.EnumerateFiles(dir, "*.pdf", SearchOption.AllDirectories)
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            if (TryParseFileName(System.IO.Path.GetFileName(path), out string clause, out int test,
                                 out char variant, out bool expectedPass))
            {
                yield return new CorpusCase(path, clause, test, variant, expectedPass, profile);
            }
        }
    }

    /// <summary>
    /// Parses a veraPDF corpus file name (e.g. <c>"veraPDF test suite 6-1-13-t09-fail-e.pdf"</c>) into its
    /// clause / test / variant / expected-verdict parts. Pure and filesystem-free so it is unit-testable
    /// without the corpus present. Returns false for a name that does not carry the veraPDF pattern.
    /// </summary>
    public static bool TryParseFileName(string fileName, out string clause, out int test, out char variant, out bool expectedPass)
    {
        clause = string.Empty;
        test = 0;
        variant = '\0';
        expectedPass = false;

        Match m = NamePattern.Match(fileName);
        if (!m.Success)
            return false;

        clause = m.Groups["clause"].Value.Replace('-', '.');
        test = int.Parse(m.Groups["test"].Value);
        variant = m.Groups["variant"].Value[0];
        expectedPass = m.Groups["result"].Value.Equals("pass", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>
    /// Runs the preflighter over one fixture. A file the loader rejects (an encrypted or structurally
    /// broken PDF) is reported as non-conformant with a synthetic <c>"load-error"</c> rule id, mirroring
    /// how a real validator treats an unreadable file — rather than letting the exception escape.
    /// </summary>
    public static Evaluation Evaluate(CorpusCase c)
    {
        try
        {
            PreflightResult result = Preflighter.Check(c.Path, c.Profile);
            return new Evaluation(
                result.Conforms,
                result.Errors.Select(e => e.RuleId).Distinct().ToArray(),
                LoadError: null);
        }
        catch (Exception ex)
        {
            return new Evaluation(Conforms: false, RuleIds: ["load-error"], LoadError: $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? Locate()
    {
        string? env = Environment.GetEnvironmentVariable("VERAPDF_CORPUS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        // Walk up from the test binaries looking for a sibling "veraPDF-corpus" checkout. The corpus
        // sits alongside the repo (…/RiderProjects/veraPDF-corpus), which is an ancestor of bin/.
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "veraPDF-corpus");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
