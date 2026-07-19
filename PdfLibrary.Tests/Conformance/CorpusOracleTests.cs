using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 10 — the veraPDF corpus oracle. Drives the <see cref="Preflighter"/> over the whole external
/// corpus (~1000 fixtures across PDF/A-2b/2u/3b) and asserts three invariants that hold regardless of how
/// many rules are implemented:
/// <list type="number">
///   <item><b>No unexpected false positives</b> — a conformant (<c>-pass-</c>) fixture must not be rejected.
///     A subset of rules can only under-report, never over-report, so this is a hard invariant. The one
///     exception is a documented baseline (<see cref="KnownPassFalsePositives"/>).</item>
///   <item><b>Fully-covered clauses stay fully covered</b> — every <c>-fail-</c> fixture in a clause whose
///     rules are complete must be caught (regression guard).</item>
///   <item><b>Detection does not regress</b> — total detections per profile stay at or above the current
///     floor. Each new rule slice ratchets these numbers up.</item>
/// </list>
/// The corpus is a sibling checkout that is absent on CI and fresh clones, so this is
/// <c>[Trait("Category","LocalOnly")]</c> and skips when the corpus is missing. The whole corpus is
/// evaluated once (<see cref="AllResults"/>) and shared across the facts.
/// </summary>
[Trait("Category", "LocalOnly")]
public class CorpusOracleTests(ITestOutputHelper output)
{
    /// <summary>Every corpus fixture paired with its preflight outcome — computed once, shared by all facts.</summary>
    private static readonly Lazy<IReadOnlyList<(CorpusHarness.CorpusCase Case, CorpusHarness.Evaluation Eval)>> AllResults =
        new(() => CorpusHarness.SupportedProfiles
            .SelectMany(CorpusHarness.Enumerate)
            .Select(c => (Case: c, Eval: CorpusHarness.Evaluate(c)))
            .ToList());

    /// <summary>
    /// Conformant fixtures the current rule set still wrongly rejects. Empty since the FontEmbeddingRule
    /// moved onto the rendering resource-tree walk (<see cref="ConformanceContext.ReferencedFonts"/>), which
    /// removed its over-report of present-but-unreferenced non-embedded fonts. Kept as the extension point:
    /// any future false positive gets documented here rather than silently tolerated.
    /// </summary>
    private static readonly IReadOnlySet<string> KnownPassFalsePositives = new HashSet<string>();

    /// <summary>
    /// Clauses whose implemented rules currently catch every corpus <c>-fail-</c> fixture. Expand this as
    /// rule slices complete a clause; a miss here is a real regression.
    /// </summary>
    private static readonly IReadOnlyDictionary<ConformanceProfile, string[]> FullyCoveredClauses =
        new Dictionary<ConformanceProfile, string[]>
        {
            // 6.3.1/6.3.2/6.4.x/6.5.x added by slice 7 (annotations, forms, actions).
            // NOTE: 6.3.3 is intentionally NOT listed — AnnotationAppearanceRule implements only tests 1-2;
            // its test-3/-4 fixtures (widget-button appearance streams) are caught today only because
            // FontEmbeddingRule over-reports their fonts, so asserting full 6.3.3 coverage here would be a
            // false invariant that breaks when that over-report is fixed. Add 6.3.3 once t3/t4 are implemented.
            [ConformanceProfile.PdfA2b] =
                ["6.1.3", "6.3.1", "6.3.2", "6.4.1", "6.4.2", "6.5.1", "6.5.2"],
            [ConformanceProfile.PdfA2u] = ["6.6.4"],
            [ConformanceProfile.PdfA3b] = ["6.8"], // slice 8 embedded-file rule catches all 3b 6.8 fixtures
        };

    /// <summary>Detection floor per profile — a ratchet. Raise these as new slices land.</summary>
    private static readonly IReadOnlyDictionary<ConformanceProfile, int> DetectionFloor =
        new Dictionary<ConformanceProfile, int>
        {
            // Re-measured at slice 27: the PDF/A-2b floor had gone stale (it sat at 134 while shared PDF/A
            // rule slices — fonts, XMP, colour, annotations — raised A2b detection to 522 without ratcheting
            // it; those slices only bumped the PdfUA1 floor). Ratcheted to the true measured detection.
            // Slice 27 — subset CharSet/CIDSet (font-subset-coverage, 6.2.11.4.2) adds +2 (522 → 524),
            // measured by toggling the rule off/on: it is the sole rule that catches 6-2-11-4-2-t01-fail-a
            // (Type1 /CharSet) and 6-2-11-4-2-t02-fail-a (CIDFontType2 /CIDSet).
            // Slice 29 — CIDFontType0 advance-width (font-program 6.2.11.5 extended from CIDFontType2-only to
            // both Type0 descendant kinds, on the back of the CFF defaultWidthX parser fix) adds +1
            // (524 → 525): it is the sole rule that catches 6-2-11-5-t01-fail-e (Type0/CIDFontType0/CFF),
            // the corpus's only CIDFontType0 width-fail fixture. fail-f (CIDFontType2) was already caught.
            // Slice 30 — simple-CFF advance-width (font-program 6.2.11.5 extended from TrueType-only to simple
            // CFF/Type1C, resolving name→charset-GID→CharString advance) adds +2 (525 → 527): it is the sole
            // rule that catches 6-2-11-5-t01-fail-a (WinAnsi) and -fail-b (custom /Differences), the corpus's
            // two simple-CFF width-fail fixtures. fail-c is Type3 (out of scope); the only UA fail fixture
            // (7.21.5-t01-fail-a) is TrueType, so PdfUA1 is unchanged.
            // Slice 31 — extended graphics state (graphics-state, 6.2.5) adds +7 (527 → 534): TR (t01-a), HTP
            // (t01-b), TR2≠Default (t02-a), HalftoneType∉{1,5} (t03-a), Type-5 TransferFunction for a
            // non-primary colourant (t03-b), HalftoneName (t04-a), non-standard RI (t05-a). The TransferFunction
            // check uses veraPDF's CMYK-only "primary" set (rule 6.2.5-6): RGB/Gray components require one.
            // Slice 32 — rendering intents (rendering-intent, 6.2.6) adds +3 (534 → 537). The RI value check
            // moved here from graphics-state (veraPDF scopes it to CosRenderingIntent, clause 6.2.6, not 6.2.5)
            // and widened to every intent site — so beyond 6-2-5-t05-fail-a (ExtGState /RI, was already caught,
            // now at 6.2.6) it newly catches 6-2-6-t01-fail-a (inline image /Intent), 6-2-2-t02-fail-a (the ri
            // operator) and 6-2-8-1-t03-fail-a (image XObject /Intent) — all "Custom" values.
            // Slice 33 — Separation/DeviceN for PDF/A (6.2.4.4) adds +7 (537 → 544): the PDF/X-4 spot-colour
            // rules (pdfx-separation-consistency, pdfx-nchannel-colorants) were widened to PDF/A, catching the
            // 4 t03 same-name-Separation-inconsistency fixtures and the 3 t02 DeviceN-/Colorants fixtures. t01
            // (device alternate space without an output intent) was already caught by device-colour.
            // Clause 6.2.2 (content-stream operators): +3 (544 → 547). ContentStreamOperatorRule catches
            // 6-2-2-t01-fail-a/b/c (an undefined operator in page / Do-reached content). Conservative floor —
            // true measured PdfA2b detection is 568/609.
            [ConformanceProfile.PdfA2b] = 547,
            [ConformanceProfile.PdfA2u] = 6,
            [ConformanceProfile.PdfA3b] = 5,   // slice 8: embedded files (all 3b fail fixtures)
            // Ratcheted to the current detection when the CP14 headings rule (ua-headings, clause 7.4) landed:
            // it adds +7 (112 → 119) by catching every 7.4 heading fail fixture — the numbered-sequence
            // (7.4.2-t01-fail-a/-b), one-<H>-per-node (7.4.4-t01-fail-a) and no-mixing (7.4.4-t02/-t03-fail)
            // cases. (The prior floor of 82 had gone stale; earlier UA slices already reached 112 without
            // ratcheting it.)
            //
            // Slice 23 — structural bucket adds +8 (119 → 127), measured by toggling the slice's rules off/on:
            //   7.1  Suspects            (ua-suspects)          +1
            //   7.9  Note IDs            (ua-note-id)           +3
            //   7.10 optional-content    (optional-content→UA)  +3
            //   7.20 reference XObjects  (ua-reference-xobject) +1
            //
            // Slice 24 (B1) — natural language for non-structure-element text (ua-content-lang, clause 7.2
            // t24 annotation /Contents, t25 form-field /TU, t33 XMP lang-alt x-default): +0 at the fixture
            // level. The rule correctly flags all three fail fixtures (7.2-t24/t25/t33-fail-a), but each was
            // already counted as detected — the veraPDF test-builder fixtures also carry an outline with no
            // catalog /Lang, which ua-object-lang already catches — so the per-fixture floor is unchanged.
            //
            // Slice B2 — Form XObject MCID reuse (ua-xobject-mcid, clause 7.20 t2 / Matterhorn 30-002): +1
            // (127 → 128), measured by toggling the rule off/on. It is the sole rule that catches
            // 7.20-t02-fail-a (a tagged Form XObject drawn three times); no other rule fires on that fixture.
            //
            // Slice 27 — subset CharSet/CIDSet completeness (font-subset-coverage, clause 7.21.4.2): +3
            // (128 → 131), measured by toggling the rule off/on. It is the sole rule that catches
            // 7.21.4.2-t01-fail-a, 7.21.4.2-t01-fail-b (subset Type1 /CharSet) and 7.21.4.2-t02-fail-a
            // (subset CIDFontType2 /CIDSet).
            //
            // Slice 28 (font F2-t2) — invalid /ToUnicode values for PDF/UA-1 (pdfa2u-tounicode-values
            // widened to PdfUA1, clause 7.21.7 test 2): +3 (131 → 134), measured by stashing the rule
            // change off/on. It is the sole rule that catches 7.21.7-t02-fail-a (U+0000), -t02-fail-b
            // (U+FFFE) and -t02-fail-c (U+FEFF); t01-fail-a stays ua-text-unicode's (7.21.7 test 1).
            // The UA forbidden set {U+0000, U+FFFE, U+FEFF} is a strict subset of the A-2u set, so PdfA2u
            // detection is unchanged (7, floor 6).
            //
            // Slice 29 — CIDFontType0 advance-width (font-program 7.21.5 ≡ 6.2.11.5): +0 for PdfUA1. The
            // clause's only UA fail fixture (7.21.5-t01-fail-a) is TrueType/Type1, not CIDFontType0, so the
            // widened check adds no UA detection even though it adds +1 to PdfA2b (6-2-11-5-t01-fail-e).
            // Cluster 5 (pdfuaid-prefix, clause 5 t3/t4/t5): +3 (134 → 137). Sole rule catching 5-t03-fail-a
            // (part), 5-t04-fail-a (amd) and 5-t05-fail-a (corr) — the AIIM namespace bound to a non-"pdfuaid"
            // prefix. Read per-property via XmlReader (XLinq collapses multiple prefixes on one URI).
            // Cluster 7.1 (role-map, t6/t7): +2 (137 → 139). UaRoleMapRule catches 7.1-t06-fail-a (circular
            // LI→LI) and 7.1-t07-fail-a (standard type Document remapped to Book).
            // Cluster 7.16 (encryption /P): +1 (139 → 140). UaEncryptionRule catches 7.16-t01-fail-a (an
            // encrypted file whose /P clears bit 512, the accessibility-extraction permission).
            // Cluster 7.18.6.2 (media clip): +3 (140 → 143). UaMediaClipRule catches 7.18.6.2-t01-fail-a
            // (no /CT), -t02-fail-a (no /Alt) and -t02-fail-b (/Alt default text empty).
            // Cluster 7.5 (table headers): +3 (143 → 146). UaTableHeaderRule catches 7.5-t01-fail-a/-fail-b
            // (TD with no resolvable header) and 7.5-t02-fail-a (TD /Headers references an undefined id).
            // Conservative floor — true measured PdfUA1 detection is now 155/155 (full; margin absorbs
            // corpus-load flakiness).
            [ConformanceProfile.PdfUA1] = 146,
        };

    [Fact]
    public void Pass_fixtures_are_not_flagged_beyond_the_known_baseline()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable, "veraPDF corpus not present at ../veraPDF-corpus");

        HashSet<string> actual = AllResults.Value
            .Where(r => r.Case.ExpectedPass && !r.Eval.Conforms)
            .Select(r => r.Case.FileName)
            .ToHashSet();

        string[] unexpected = actual.Except(KnownPassFalsePositives).OrderBy(x => x).ToArray();
        string[] stale = KnownPassFalsePositives.Except(actual).OrderBy(x => x).ToArray();

        foreach (string f in unexpected) output.WriteLine($"NEW false positive (a conformant file was rejected): {f}");
        foreach (string f in stale) output.WriteLine($"baseline entry now passes — delete it from KnownPassFalsePositives: {f}");

        Assert.True(unexpected.Length == 0,
            $"{unexpected.Length} conformant fixture(s) newly rejected: {string.Join(", ", unexpected)}");
        Assert.True(stale.Length == 0,
            $"{stale.Length} baseline entr(y/ies) no longer needed — remove: {string.Join(", ", stale)}");
    }

    [Fact]
    public void Fully_covered_clauses_detect_every_fail_fixture()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable, "veraPDF corpus not present at ../veraPDF-corpus");

        string[] misses = AllResults.Value
            .Where(r => !r.Case.ExpectedPass
                        && r.Eval.Conforms
                        && FullyCoveredClauses.TryGetValue(r.Case.Profile, out string[]? clauses)
                        && clauses.Contains(r.Case.Clause))
            .Select(r => $"{r.Case.Profile}/{r.Case.FileName}")
            .OrderBy(x => x)
            .ToArray();

        Assert.True(misses.Length == 0,
            $"a fully-covered clause missed {misses.Length} fixture(s): {string.Join(", ", misses)}");
    }

    [Fact]
    public void Detection_coverage_does_not_regress()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable, "veraPDF corpus not present at ../veraPDF-corpus");

        foreach (ConformanceProfile profile in CorpusHarness.SupportedProfiles)
        {
            List<(CorpusHarness.CorpusCase Case, CorpusHarness.Evaluation Eval)> fails =
                AllResults.Value.Where(r => r.Case.Profile == profile && !r.Case.ExpectedPass).ToList();
            int detected = fails.Count(r => !r.Eval.Conforms);
            int floor = DetectionFloor.GetValueOrDefault(profile);

            output.WriteLine($"{profile}: detected {detected}/{fails.Count} fail fixtures (floor {floor})");

            Assert.True(detected >= floor,
                $"{profile} fail-fixture detection regressed: {detected} < floor {floor}. "
                + "If a rule was intentionally removed, lower the floor; otherwise this is a regression.");
        }
    }
}
