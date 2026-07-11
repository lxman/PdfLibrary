using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// The heart of the parity harness: runs the <see cref="Preflighter"/> live over every corpus file
/// and joins each result with veraPDF's verdict from the <see cref="ParitySnapshot"/>, at clause
/// granularity. Computed once (needs the corpus present) and shared by the gates and the reports.
///
/// For each file it records four things: veraPDF's verdict (compliant? which clauses), PdfLibrary's verdict
/// (conforms? which clauses), and whether the file exists in both the corpus and the snapshot (the
/// completeness signal). Clause sets on both sides are the bare dotted ISO clause
/// (<see cref="ParitySnapshot.ClauseKey"/>) so they compare directly.
/// </summary>
internal static class ParityComparison
{
    /// <summary>One corpus file compared across the two validators.</summary>
    internal sealed record FileComparison(
        ConformanceProfile Profile,
        string FileName,
        bool VeraCompliant,
        IReadOnlySet<string> VeraClauses,
        bool PdfLibraryConforms,
        IReadOnlySet<string> PdfLibraryClauses)
    {
        /// <summary>veraPDF passed but PdfLibrary rejected — a false positive (the invariant we forbid).</summary>
        public bool IsFalsePositive => VeraCompliant && !PdfLibraryConforms;
    }

    /// <summary>All comparisons for one profile plus the corpus/snapshot set mismatches.</summary>
    internal sealed record ProfileComparison(
        ConformanceProfile Profile,
        IReadOnlyList<FileComparison> Files,
        IReadOnlyList<string> SnapshotFilesMissingFromCorpus,
        IReadOnlyList<string> CorpusFilesMissingFromSnapshot);

    private static readonly Lazy<IReadOnlyList<ProfileComparison>> Results = new(Compute);

    /// <summary>The comparison, computed once. Empty when the corpus or snapshot is absent.</summary>
    public static IReadOnlyList<ProfileComparison> All => Results.Value;

    /// <summary>Every file comparison across all profiles, flattened.</summary>
    public static IEnumerable<FileComparison> AllFiles => All.SelectMany(p => p.Files);

    private static IReadOnlyList<ProfileComparison> Compute()
    {
        if (!CorpusHarness.IsAvailable || !ParitySnapshot.IsAvailable)
            return [];

        var profiles = new List<ProfileComparison>();

        // Only profiles present in BOTH the snapshot and the corpus checkout.
        foreach (ConformanceProfile profile in ParitySnapshot.Profiles
                     .Where(CorpusHarness.SupportedProfiles.Contains)
                     .OrderBy(p => p))
        {
            // base name -> disk path (first wins on the unexpected event of a duplicate name).
            var diskMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string path in CorpusHarness.AllPdfPaths(profile))
                diskMap.TryAdd(Path.GetFileName(path), path);

            IReadOnlyDictionary<string, ParitySnapshot.ParityVerdict> vera = ParitySnapshot.Files(profile);

            var files = new List<FileComparison>();
            var snapshotMissingFromCorpus = new List<string>();

            foreach ((string name, ParitySnapshot.ParityVerdict verdict) in vera)
            {
                if (!diskMap.TryGetValue(name, out string? path))
                {
                    snapshotMissingFromCorpus.Add(name);
                    continue;
                }

                (bool conforms, IReadOnlySet<string> clauses) = RunPdfLibrary(path, profile);
                files.Add(new FileComparison(
                    profile, name, verdict.Compliant, verdict.FailedClauses, conforms, clauses));
            }

            List<string> corpusMissingFromSnapshot = diskMap.Keys
                .Where(n => !vera.ContainsKey(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            profiles.Add(new ProfileComparison(
                profile,
                files,
                snapshotMissingFromCorpus.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                corpusMissingFromSnapshot));
        }

        return profiles;
    }

    private static (bool Conforms, IReadOnlySet<string> Clauses) RunPdfLibrary(string path, ConformanceProfile profile)
    {
        try
        {
            PreflightResult result = Preflighter.Check(path, profile);
            var clauses = result.Errors
                .Select(e => ParitySnapshot.ClauseKey(e.Clause))
                .Where(c => c is not null)
                .Select(c => c!)
                .ToHashSet(StringComparer.Ordinal);
            return (result.Conforms, clauses);
        }
        catch
        {
            // A loader crash mirrors a rejected file: non-conformant, no clause detail.
            return (false, new HashSet<string>(StringComparer.Ordinal));
        }
    }
}
