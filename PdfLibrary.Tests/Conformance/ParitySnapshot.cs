using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Loads the committed veraPDF verdict snapshot — the reference "answer key" that the parity
/// gates diff the <see cref="Preflighter"/> against. The snapshot
/// (<c>Conformance/parity/verapdf-verdicts.json</c>, regenerated offline by
/// <c>tools/verapdf-parity/capture.sh</c>) is copied next to the test binary, so this loads with
/// NO corpus and NO JVM present — unlike <see cref="CorpusHarness"/>, which needs the corpus PDFs.
///
/// It carries veraPDF's per-file verdict (compliant?) and the set of ISO clauses veraPDF flagged,
/// keyed by profile then by file name. The join key to <see cref="CorpusHarness.CorpusCase"/> is the
/// file's base name (unique across the corpus). Clause is the join granularity: veraPDF emits dotted
/// clauses (<c>6.1.10</c>); a <see cref="Finding"/> carries the same number inside <see cref="Finding.Clause"/>
/// (<c>"ISO 19005-2:2011, 6.1.10"</c>) — reconcile the two with <see cref="ClauseKey"/>.
/// </summary>
internal static class ParitySnapshot
{
    /// <summary>veraPDF's verdict for one file: whether it is compliant and which clauses it failed.</summary>
    internal sealed record ParityVerdict(
        bool Compliant,
        IReadOnlySet<string> FailedClauses,
        IReadOnlyList<FailedRule> FailedRules);

    /// <summary>A single failed veraPDF rule: dotted ISO clause plus its test number (kept for diagnostics).</summary>
    internal readonly record struct FailedRule(string Clause, int Test);

    // ---- JSON shape (mirrors tools/verapdf-parity/MrrToVerdicts output) --------------------------
    private sealed record SnapshotDto(int SchemaVersion, GeneratedFromDto? GeneratedFrom, Dictionary<string, ProfileDto>? Profiles);
    private sealed record GeneratedFromDto(Dictionary<string, string>? VerapdfVersions, string? CorpusCommit, string? CaptureDateUtc);
    private sealed record ProfileDto(string? Flavour, Dictionary<string, FileDto>? Files);
    private sealed record FileDto(bool Compliant, List<RuleDto>? FailedRules);
    private sealed record RuleDto(string? Clause, int TestNumber);

    private sealed record LoadedSnapshot(
        string CorpusCommit,
        IReadOnlyDictionary<string, string> VerapdfVersions,
        IReadOnlyDictionary<ConformanceProfile, IReadOnlyDictionary<string, ParityVerdict>> Profiles);

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<LoadedSnapshot?> Loaded = new(Load);

    /// <summary>True when the snapshot file is present and parsed.</summary>
    public static bool IsAvailable => Loaded.Value is not null;

    /// <summary>The corpus git commit the snapshot was captured against (provenance), or null.</summary>
    public static string? CorpusCommit => Loaded.Value?.CorpusCommit;

    /// <summary>The veraPDF component versions that produced the snapshot (id → version).</summary>
    public static IReadOnlyDictionary<string, string> VerapdfVersions =>
        Loaded.Value?.VerapdfVersions ?? new Dictionary<string, string>();

    /// <summary>The profiles present in the snapshot.</summary>
    public static IReadOnlyCollection<ConformanceProfile> Profiles =>
        Loaded.Value?.Profiles.Keys.ToArray() ?? [];

    /// <summary>Every file's verdict for a profile (file name → verdict). Empty if absent.</summary>
    public static IReadOnlyDictionary<string, ParityVerdict> Files(ConformanceProfile profile) =>
        Loaded.Value is { } s && s.Profiles.TryGetValue(profile, out IReadOnlyDictionary<string, ParityVerdict>? files)
            ? files
            : new Dictionary<string, ParityVerdict>();

    /// <summary>veraPDF's verdict for one file under a profile, or null when the snapshot has no entry.</summary>
    public static ParityVerdict? Get(ConformanceProfile profile, string fileName) =>
        Files(profile).TryGetValue(fileName, out ParityVerdict? v) ? v : null;

    // Trailing dotted ISO clause, e.g. "6.1.10" out of "ISO 19005-2:2011, 6.1.10" or "7.1" out of
    // "ISO 14289-1:2014, 7.1". The clause always terminates the string, so anchoring at the end is safe.
    private static readonly Regex TrailingClause = new(@"(\d+(?:\.\d+)*)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Reduces a <see cref="Finding.Clause"/> to the bare dotted ISO clause used as the parity join key,
    /// stripping the ISO document prefix. Returns null for a clause carrying no number (e.g. the
    /// <c>"—"</c> placeholder on a rule that could not be evaluated).
    /// </summary>
    public static string? ClauseKey(string? findingClause)
    {
        if (string.IsNullOrWhiteSpace(findingClause)) return null;
        Match m = TrailingClause.Match(findingClause);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static LoadedSnapshot? Load()
    {
        string? path = Locate();
        if (path is null) return null;

        SnapshotDto? dto = JsonSerializer.Deserialize<SnapshotDto>(File.ReadAllText(path), JsonOpts);
        if (dto?.Profiles is null) return null;

        var profiles = new Dictionary<ConformanceProfile, IReadOnlyDictionary<string, ParityVerdict>>();
        foreach ((string key, ProfileDto profile) in dto.Profiles)
        {
            if (!Enum.TryParse(key, out ConformanceProfile p) || profile.Files is null)
                continue;

            var files = new Dictionary<string, ParityVerdict>(StringComparer.Ordinal);
            foreach ((string name, FileDto file) in profile.Files)
            {
                List<FailedRule> rules = (file.FailedRules ?? [])
                    .Where(r => !string.IsNullOrEmpty(r.Clause))
                    .Select(r => new FailedRule(r.Clause!, r.TestNumber))
                    .ToList();
                var clauses = rules.Select(r => r.Clause).ToHashSet(StringComparer.Ordinal);
                files[name] = new ParityVerdict(file.Compliant, clauses, rules);
            }

            profiles[p] = files;
        }

        return new LoadedSnapshot(
            dto.GeneratedFrom?.CorpusCommit ?? "unknown",
            dto.GeneratedFrom?.VerapdfVersions ?? new Dictionary<string, string>(),
            profiles);
    }

    private static string? Locate()
    {
        // Primary: copied next to the test binary (see PdfLibrary.Tests.csproj CopyToOutputDirectory).
        string beside = Path.Combine(AppContext.BaseDirectory, "Conformance", "parity", "verapdf-verdicts.json");
        if (File.Exists(beside)) return beside;

        // Fallback: walk up to the source tree (covers an un-copied run).
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName,
                "PdfLibrary.Tests", "Conformance", "parity", "verapdf-verdicts.json");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
