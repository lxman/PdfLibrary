using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Phase-1 tests for the parity harness plumbing: the <see cref="ParitySnapshot"/> loader and the
/// <see cref="ParitySnapshot.ClauseKey"/> normalizer that reconciles a <see cref="Finding.Clause"/>
/// with veraPDF's dotted clause. These are CI-safe — the snapshot is committed and copied next to
/// the test binary, so no corpus and no JVM are needed (the gates that DO need the corpus come in
/// phase 2 under <c>[Trait("Category","Parity")]</c>).
/// </summary>
public class ParitySnapshotTests
{
    // ---- clause normalization: round-trips through the real clause producer -----------------------

    [Theory]
    [InlineData(ConformanceProfile.PdfA2b, "6.1.10")]
    [InlineData(ConformanceProfile.PdfA2u, "6.2.11.7.2")]
    [InlineData(ConformanceProfile.PdfA3b, "6.8")]
    [InlineData(ConformanceProfile.PdfUA1, "7.1")]
    public void ClauseKey_recovers_the_dotted_clause_from_a_finding_clause(ConformanceProfile profile, string clause)
    {
        // ConformanceClauses.For is exactly what a rule stamps onto Finding.Clause.
        string findingClause = ConformanceClauses.For(profile, clause);

        Assert.NotEqual(clause, findingClause);                       // it really did add an ISO prefix
        Assert.Equal(clause, ParitySnapshot.ClauseKey(findingClause)); // …which we strip back off
    }

    [Theory]
    [InlineData("—")]              // the placeholder on a rule that could not be evaluated
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no digits here")]
    public void ClauseKey_returns_null_when_there_is_no_clause_number(string? findingClause)
    {
        Assert.Null(ParitySnapshot.ClauseKey(findingClause));
    }

    // ---- snapshot loads and matches the committed capture ----------------------------------------

    [Fact]
    public void Snapshot_loads_with_provenance_and_all_four_profiles()
    {
        Assert.True(ParitySnapshot.IsAvailable, "verapdf-verdicts.json was not found next to the test binary");
        Assert.Equal("49de56c", ParitySnapshot.CorpusCommit);
        Assert.Equal("1.30.2", ParitySnapshot.VerapdfVersions["core"]);

        Assert.Equal(
            new[] { ConformanceProfile.PdfA2b, ConformanceProfile.PdfA2u, ConformanceProfile.PdfA3b, ConformanceProfile.PdfUA1 }
                .OrderBy(p => p),
            ParitySnapshot.Profiles.OrderBy(p => p));

        // PDF/X-4 is deliberately absent — veraPDF does not validate PDF/X.
        Assert.DoesNotContain(ConformanceProfile.PdfX4, ParitySnapshot.Profiles);
    }

    [Theory]
    // file count, non-compliant count — tracks the committed snapshot (regenerate via capture.sh → update).
    [InlineData(ConformanceProfile.PdfA2b, 986, 609)]
    [InlineData(ConformanceProfile.PdfA2u, 22, 10)]
    [InlineData(ConformanceProfile.PdfA3b, 12, 5)]
    [InlineData(ConformanceProfile.PdfUA1, 296, 155)]
    public void Snapshot_file_and_noncompliant_counts_match_the_capture(ConformanceProfile profile, int files, int nonCompliant)
    {
        var verdicts = ParitySnapshot.Files(profile);
        Assert.Equal(files, verdicts.Count);
        Assert.Equal(nonCompliant, verdicts.Values.Count(v => !v.Compliant));
    }

    [Fact]
    public void A_known_fail_fixture_carries_its_clause_and_a_pass_fixture_is_clean()
    {
        ParitySnapshot.ParityVerdict? fail =
            ParitySnapshot.Get(ConformanceProfile.PdfA2b, "veraPDF test suite 6-1-10-t01-fail-a.pdf");
        Assert.NotNull(fail);
        Assert.False(fail!.Compliant);
        Assert.Contains("6.1.10", fail.FailedClauses);

        ParitySnapshot.ParityVerdict? pass =
            ParitySnapshot.Get(ConformanceProfile.PdfA2b, "veraPDF test suite 6-1-12-t01-pass-a.pdf");
        Assert.NotNull(pass);
        Assert.True(pass!.Compliant);
        Assert.Empty(pass.FailedClauses);
    }

    [Fact]
    public void Every_noncompliant_file_has_at_least_one_failed_clause()
    {
        // The capture found zero parse-gaps; guard that invariant so a future recapture that loses
        // clause detail (a non-compliant verdict with no clauses) is caught here rather than silently
        // weakening the gates.
        string[] offenders = ParitySnapshot.Profiles
            .SelectMany(p => ParitySnapshot.Files(p).Select(kv => (p, kv.Key, kv.Value)))
            .Where(t => !t.Value.Compliant && t.Value.FailedClauses.Count == 0)
            .Select(t => $"{t.p}/{t.Key}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"{offenders.Length} non-compliant file(s) carry no clause: {string.Join(", ", offenders)}");
    }
}
