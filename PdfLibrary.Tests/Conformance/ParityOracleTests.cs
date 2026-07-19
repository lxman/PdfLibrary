using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Phase-2 parity gates: they diff the live <see cref="Preflighter"/> against veraPDF's committed
/// verdicts (<see cref="ParitySnapshot"/>) at clause granularity, via <see cref="ParityComparison"/>.
///
/// Unlike <see cref="CorpusOracleTests"/> (whose oracle is the corpus FILENAME label), these are
/// anchored to the reference validator's actual verdict — the basis for the "verdict parity with
/// veraPDF" claim. They need the corpus present to run the preflighter, so they are
/// <c>[Trait("Category","Parity")]</c> and skip when it is absent (a dedicated CI job clones the
/// corpus and runs exactly this category; the committed snapshot means no JVM is needed there).
/// </summary>
[Trait("Category", "Parity")]
public class ParityOracleTests(ITestOutputHelper output)
{
    private const string Skip = "veraPDF corpus not present at ../veraPDF-corpus (Category=Parity)";

    /// <summary>
    /// Conformant-per-veraPDF files that PdfLibrary still wrongly rejects. EMPTY — the preflighter is a
    /// subset validator, so it can only under-report; a genuine false positive is a bug, not a baseline
    /// entry. Any addition here must be justified in review, never used to paper over a real FP.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownParityFalsePositives = new HashSet<string>();

    /// <summary>
    /// Clauses where PdfLibrary currently matches veraPDF on EVERY file the reference flags — a ratchet.
    /// Seeded from the phase-2 measurement; raise it (never lower without justification) as rules land.
    /// A miss here means a regression against the reference on a clause we claim full parity for.
    /// </summary>
    private static readonly IReadOnlyDictionary<ConformanceProfile, string[]> ParityFullClauses =
        new Dictionary<ConformanceProfile, string[]>
        {
            [ConformanceProfile.PdfA2b] =
                ["6.1.3", "6.1.7.1", "6.1.9", "6.2.3", "6.2.4.3", "6.2.8.3", "6.2.10", "6.2.11.3.2", "6.2.11.6", "6.3.1", "6.3.2", "6.4.1", "6.4.2", "6.5.1", "6.5.2", "6.6.2.3.1", "6.6.2.3.3"],
            [ConformanceProfile.PdfA2u] = ["6.6.4"],
            [ConformanceProfile.PdfA3b] = ["6.8"],
            [ConformanceProfile.PdfUA1] = ["7.3", "7.15", "7.21.3.2", "7.21.5", "7.21.8"],
        };

    [Fact]
    public void No_false_positives_vs_veraPDF()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable && ParitySnapshot.IsAvailable, Skip);

        HashSet<string> fps = ParityComparison.AllFiles
            .Where(f => f.IsFalsePositive)
            .Select(f => $"{f.Profile}/{f.FileName}")
            .ToHashSet();

        string[] unexpected = fps.Except(KnownParityFalsePositives).OrderBy(x => x).ToArray();
        string[] stale = KnownParityFalsePositives.Except(fps).OrderBy(x => x).ToArray();

        output.WriteLine($"checked {ParityComparison.AllFiles.Count()} files across "
            + $"{ParityComparison.All.Count} profiles — {fps.Count} false positive(s)");
        foreach (string f in unexpected) output.WriteLine($"NEW false positive (veraPDF passes, PdfLibrary rejects): {f}");
        foreach (string f in stale) output.WriteLine($"baseline entry no longer a FP — remove it: {f}");

        Assert.True(unexpected.Length == 0,
            $"{unexpected.Length} file(s) PdfLibrary rejects that veraPDF passes: {string.Join(", ", unexpected)}");
        Assert.True(stale.Length == 0,
            $"{stale.Length} stale KnownParityFalsePositives entr(y/ies): {string.Join(", ", stale)}");
    }

    [Fact]
    public void Fully_covered_clauses_match_veraPDF_exactly()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable && ParitySnapshot.IsAvailable, Skip);

        var misses = new List<string>();
        int locked = 0;

        foreach (ParityComparison.ProfileComparison pc in ParityComparison.All)
        {
            if (!ParityFullClauses.TryGetValue(pc.Profile, out string[]? clauses)) continue;

            foreach (string clause in clauses)
            {
                locked++;
                foreach (ParityComparison.FileComparison f in pc.Files)
                    if (f.VeraClauses.Contains(clause) && !f.PdfLibraryClauses.Contains(clause))
                        misses.Add($"{pc.Profile}/{clause}/{f.FileName}");
            }
        }

        output.WriteLine($"verified {locked} locked clause(s) across {ParityComparison.All.Count} profiles");
        foreach (string m in misses.OrderBy(x => x)) output.WriteLine($"MISS (veraPDF flags, PdfLibrary does not): {m}");

        Assert.True(misses.Count == 0,
            $"{misses.Count} regression(s) on a fully-covered clause: {string.Join(", ", misses.Take(20))}"
            + (misses.Count > 20 ? " …" : ""));
    }

    [Fact]
    public void Snapshot_matches_the_corpus_checkout()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable && ParitySnapshot.IsAvailable, Skip);

        var problems = new List<string>();
        foreach (ParityComparison.ProfileComparison pc in ParityComparison.All)
        {
            foreach (string n in pc.SnapshotFilesMissingFromCorpus)
                problems.Add($"{pc.Profile}: snapshot has '{n}' but the corpus does not");
            foreach (string n in pc.CorpusFilesMissingFromSnapshot)
                problems.Add($"{pc.Profile}: corpus has '{n}' but the snapshot does not");
        }

        output.WriteLine($"snapshot captured @ {ParitySnapshot.CorpusCommit}; "
            + $"{problems.Count} file-set mismatch(es)");
        foreach (string p in problems.Take(20)) output.WriteLine(p);

        Assert.True(problems.Count == 0,
            $"{problems.Count} corpus/snapshot mismatch(es) — the corpus checkout drifted from the "
            + "snapshot. Regenerate via tools/verapdf-parity/capture.sh and commit. "
            + $"First few: {string.Join("; ", problems.Take(5))}");
    }
}
