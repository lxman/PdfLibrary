using System;
using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Independent second conformance oracle: the BFO PDF/A test suite (<see cref="BfoHarness"/>). Its pass
/// fixtures are a hard oracle — a rule subset can only under-report, so a flagged pass file is a real false
/// positive — and its fail fixtures back a detection floor that ratchets up as rules land. Cross-validates
/// the veraPDF-driven rules against fixtures written independently from the ISO text. LocalOnly (the suite
/// is a sibling checkout, absent on CI).
/// </summary>
[Trait("Category", "LocalOnly")]
public class BfoOracleTests(ITestOutputHelper output)
{
    /// <summary>Pass fixtures the current rule set still flags. Empty — no BFO conformant file is rejected.</summary>
    private static readonly IReadOnlySet<string> KnownPassFalsePositives = new HashSet<string>();

    /// <summary>Detection floor — a ratchet. Raise as new rules catch more BFO fail fixtures. A floor may
    /// also legitimately FALL when a fix removes detections that were false positives — lower it
    /// deliberately, name the fix in the remarks below, and verify veraPDF agrees the lost detections
    /// were never real (first case: issues 24-26, 2026-08-15).</summary>
    /// <remarks>
    /// 2026-07-25 — ratcheted 10 → 14, the measured detection over the 24 PDF/A-2 fail fixtures, after the
    /// suite was checked out locally again. Stable across three consecutive runs. The 10 remaining misses
    /// are coverage gaps shared with the veraPDF corpus (chiefly the font-program clauses), not errors:
    /// the pass-fixture oracle reports zero false positives, so the rule set stays a strict subset here
    /// exactly as it is against veraPDF.
    ///
    /// Re-verified 2026-08-15 after the issues 24-26 false-positive fixes (indirect /W array elements,
    /// StandardEncoding Annex D.2 names, zero-advance TrueType programs): the floor held at 14, unchanged
    /// — no BFO fail fixture's verdict or detection depended on any of them. (The companion veraPDF
    /// PDF_A-2b corpus did have 21 files cross fails→fixed as their spurious width findings disappeared
    /// — see Pellucid.App.Tests/oi-corpus-baseline.txt, 2026-08-15 — but each remained non-conforming
    /// for other reasons; no BFO-corpus file's verdict or detection depended on the fixed defects.)
    /// </remarks>
    private const int DetectionFloor = 14;

    [Fact]
    public void Pass_fixtures_are_not_flagged()
    {
        Assert.SkipUnless(BfoHarness.IsAvailable, "pdfa-testsuite not present at ../pdfa-testsuite");

        var unexpected = new List<string>();
        var staleBaseline = new HashSet<string>(KnownPassFalsePositives);

        foreach (BfoHarness.BfoCase test in BfoHarness.Cases().Where(c => c.ExpectPass))
        {
            List<string> rules = RuleIdsFlagging(test);
            if (rules.Count == 0)
                continue;

            staleBaseline.Remove(test.Name);
            if (!KnownPassFalsePositives.Contains(test.Name))
                unexpected.Add($"{test.Name} [{string.Join(",", rules)}]");
        }

        Assert.True(unexpected.Count == 0,
            $"{unexpected.Count} conformant BFO file(s) flagged: {string.Join("; ", unexpected)}");
        Assert.True(staleBaseline.Count == 0,
            $"{staleBaseline.Count} baseline entr(y/ies) now pass — remove: {string.Join(", ", staleBaseline)}");
    }

    [Fact]
    public void Fail_detection_does_not_regress()
    {
        Assert.SkipUnless(BfoHarness.IsAvailable, "pdfa-testsuite not present at ../pdfa-testsuite");

        int detected = 0, total = 0;
        foreach (BfoHarness.BfoCase test in BfoHarness.Cases().Where(c => !c.ExpectPass))
        {
            total++;
            if (RuleIdsFlagging(test).Count > 0)
                detected++;
        }

        output.WriteLine($"BFO: detected {detected}/{total} fail fixtures (floor {DetectionFloor})");
        Assert.True(detected >= DetectionFloor,
            $"BFO fail-fixture detection regressed: {detected} < floor {DetectionFloor}.");
    }

    /// <summary>The distinct rule ids that flag the fixture as an Error, or empty when it conforms. A load
    /// failure surfaces as the synthetic <c>document-load</c> rule (the preflight never throws out).</summary>
    private static List<string> RuleIdsFlagging(BfoHarness.BfoCase test)
    {
        try
        {
            PreflightResult result = Preflighter.Check(test.Path, test.Profile);
            return result.Conforms ? [] : result.Errors.Select(e => e.RuleId).Distinct().ToList();
        }
        catch (Exception ex)
        {
            return [$"load-error:{ex.GetType().Name}"];
        }
    }
}
