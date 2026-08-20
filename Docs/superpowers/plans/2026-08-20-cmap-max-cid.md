# CMap Max-CID Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect PDF/A-2b clause 6.1.13 test 10 — no CID greater than 65535 — lifting veraPDF verdict parity from 976/986 to 978/986 and clause 6.1.13 to full parity.

**Architecture:** `CidCMap` gains a cheap static scan that reports the largest CID a CMap *declares*, without materialising the code→CID dictionary its existing `Parse` builds. `ImplementationLimitsRule` gains a sixth sub-check that walks `ConformanceContext.ReferencedFonts`, reads each Type0 font's embedded `/Encoding` CMap stream by direct dictionary navigation, and compares. A prerequisite restructure scopes an existing `yield break` so the new sub-check cannot be silently skipped.

**Tech Stack:** C#, .NET 10, xUnit. Engine repo `PdfLibrary` only, plus a Pellucid engine pin and oi-corpus rebaseline in the final task.

**Spec:** `Docs/superpowers/specs/2026-08-20-cmap-max-cid-design.md`

## Global Constraints

- **Zero false positives across all 1316 corpus files.** A standing invariant of the parity harness, never traded for coverage. If any arm produces a finding veraPDF does not, narrow the arm — never weaken the expectation.
- **`CidCMap.Parse` and its instance path must be untouched.** The font/decode path it feeds must behave identically. The new scan is additive and separate.
- Clause strings come from `ConformanceClauses.For(context.Target, "6.1.13")` — never a hardcoded ISO string.
- **The finding message must not contain the word "integer"** — `IsIntegerFinding` distinguishes this rule's integer findings by a `Message.Contains("integer")` substring test.
- Reach the CMap by **direct dictionary navigation**, never via `Type0Font`'s parsed CMap: that path is gated on `CIDSystemInfo/Registry == "Adobe"` plus a bundled Ordering, so a font failing either never has its CMap fetched.
- Detection only. No remediation wiring; no Pellucid changes beyond the engine pin and oi-corpus rebaseline in Task 3.
- Every guard test must be **probed**: delete the guard, confirm the test fails, restore.
- Work on branch `feat/parity-6113-t10-max-cid`, already created off `master` at `c5416fb`.

---

### Task 1: `CidCMap.MaxDeclaredCid`

**Files:**
- Modify: `PdfLibrary/Fonts/CidCMap.cs` (add one static method; touch nothing else)
- Test: `PdfLibrary.Tests/Fonts/CidCMapMaxCidTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static long? MaxDeclaredCid(byte[] data)` on `PdfLibrary.Fonts.CidCMap`. Returns the largest CID the data declares via `cidchar`/`cidrange`, or `null` when it declares none (not a CID CMap, or unparseable). Never throws.

`internal`, not `public`: `CidCMap` is a public type but only the conformance layer needs this, and new public engine surface is flagged by convention here.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/CidCMapMaxCidTests.cs`:

```csharp
using System.Text;
using PdfLibrary.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// ISO 19005-2 clause 6.1.13 test 10 bounds the largest CID a CMap DECLARES at 65535 (veraPDF
/// object <c>CMapFile</c>, property <c>maximalCID</c>). These pin
/// <see cref="CidCMap.MaxDeclaredCid"/>, the cheap scan that answers it without materialising the
/// code→CID dictionary <see cref="CidCMap.Parse"/> builds.
/// </summary>
public class CidCMapMaxCidTests
{
    private static long? Max(string cmap) => CidCMap.MaxDeclaredCid(Encoding.ASCII.GetBytes(cmap));

    [Fact]
    public void A_range_reports_its_top_CID_not_its_start()
    {
        // Exactly the shape both corpus fixtures carry: <3f00> <3fff> 65536 → 65536 + 0xFF = 65791.
        Assert.Equal(65791, Max("1 begincidrange\n<3f00> <3fff> 65536\nendcidrange"));
    }

    [Fact]
    public void The_maximum_is_taken_across_every_range()
    {
        Assert.Equal(65791, Max(
            "3 begincidrange\n"
            + "<0000> <00ff> 0\n"
            + "<3f00> <3fff> 65536\n"
            + "<2100> <21ff> 8448\n"
            + "endcidrange"));
    }

    [Fact]
    public void A_cidchar_entry_counts_too()
    {
        Assert.Equal(70000, Max("1 begincidchar\n<0041> 70000\nendcidchar"));
    }

    [Fact]
    public void The_maximum_can_come_from_a_cidchar_rather_than_a_range()
    {
        Assert.Equal(99999, Max(
            "1 begincidrange\n<0000> <00ff> 0\nendcidrange\n"
            + "1 begincidchar\n<0041> 99999\nendcidchar"));
    }

    [Theory]
    [InlineData("1 begincidrange\n<0000> <ffff> 0\nendcidrange", 65535)]   // exactly at the limit
    [InlineData("1 begincidchar\n<0041> 65535\nendcidchar", 65535)]
    public void A_conforming_CMap_reports_its_maximum_without_exceeding_the_limit(string cmap, long expected)
    {
        Assert.Equal(expected, Max(cmap));
    }

    [Fact]
    public void Data_declaring_no_CIDs_reports_null()
    {
        Assert.Null(Max("/CIDInit /ProcSet findresource begin\nend"));
        Assert.Null(Max(string.Empty));
    }

    [Fact]
    public void Malformed_data_reports_null_rather_than_throwing()
    {
        Assert.Null(Max("begincidrange <zz> <qq> notanumber endcidrange"));
    }

    [Fact]
    public void A_range_wider_than_the_span_guard_is_deliberately_ignored()
    {
        // DELIBERATE UNDER-REPORT, not an oversight. Parse skips a range whose CODE span exceeds
        // MaxRangeSpan (0xFFFF) as corrupt; this scan applies the same guard so the two agree on
        // what a legitimate range is. A wider codespace is legal in ISO 32000, but this engine's
        // CID handling assumes two bytes throughout, and flagging a shape the rest of the engine
        // treats as corrupt would risk a false positive on a document no corpus fixture covers.
        // If this test ever "fails" because someone widened the scan, that is the decision being
        // reversed — reverse it deliberately, not incidentally.
        Assert.Null(Max("1 begincidrange\n<000000> <ffffff> 1\nendcidrange"));

        // ...and a wide range does not suppress a legitimate one beside it.
        Assert.Equal(70000, Max(
            "2 begincidrange\n<000000> <ffffff> 1\n<0000> <00ff> 70000\nendcidrange"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CidCMapMaxCidTests"`
Expected: FAIL to compile — `'CidCMap' does not contain a definition for 'MaxDeclaredCid'`.

- [ ] **Step 3: Add the scan**

In `PdfLibrary/Fonts/CidCMap.cs`, add this method immediately after the existing `Parse` method (locate `Parse` by its signature `public static CidCMap Parse(byte[] data)`; insert after its closing brace, before the `[GeneratedRegex]` declarations):

```csharp
    /// <summary>
    /// The largest CID this data DECLARES, or null when it declares none — the quantity ISO 19005-2
    /// clause 6.1.13 test 10 bounds at 65535 (veraPDF object <c>CMapFile</c>, <c>maximalCID</c>).
    ///
    /// <para>A separate scan from <see cref="Parse"/> on purpose. Parse materialises every code in
    /// every range into <c>_codeToCid</c> — tens of thousands of entries for a CJK CMap — and a
    /// caller that needs only a maximum should not pay that. This reads the same operators, keeps
    /// no map, and leaves Parse and the decode path it feeds untouched.</para>
    ///
    /// <para>Returns <see cref="long"/> because a range's top CID (<c>cidStart + (hi - lo)</c>) can
    /// exceed <see cref="int"/>. Ranges wider than <see cref="MaxRangeSpan"/> are skipped, matching
    /// Parse's notion of a legitimate 2-byte range — a deliberate under-report, since a wider
    /// codespace is legal in ISO 32000 but this engine's CID handling assumes two bytes throughout.
    /// Never throws: malformed input degrades to whatever was read first, like Parse.</para>
    /// </summary>
    internal static long? MaxDeclaredCid(byte[] data)
    {
        long? max = null;

        try
        {
            string content = Encoding.ASCII.GetString(data);

            foreach (string block in FindBlocks(content, "begincidchar", "endcidchar"))
            foreach (Match match in CidCharRegex().Matches(block))
            {
                if (!long.TryParse(match.Groups[2].Value, out long cid)) continue;
                max = max is null ? cid : Math.Max(max.Value, cid);
            }

            foreach (string block in FindBlocks(content, "begincidrange", "endcidrange"))
            foreach (Match match in CidRangeRegex().Matches(block))
            {
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int lo) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out int hi) ||
                    !long.TryParse(match.Groups[3].Value, out long cidStart))
                    continue;
                if (hi < lo || hi - lo > MaxRangeSpan) continue;

                long top = cidStart + (hi - lo);
                max = max is null ? top : Math.Max(max.Value, top);
            }
        }
        catch
        {
            // Same posture as Parse: degrade to whatever was read before the fault, never throw.
        }

        return max;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CidCMapMaxCidTests"`
Expected: PASS, all tests.

- [ ] **Step 5: Probe the span guard**

Temporarily delete `if (hi < lo || hi - lo > MaxRangeSpan) continue;` from `MaxDeclaredCid` and re-run.
Expected: `A_range_wider_than_the_span_guard_is_deliberately_ignored` FAILS (the wide range now yields 16777216). Restore the line and confirm green. Report the observed failure.

- [ ] **Step 6: Confirm `Parse` is untouched**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CidCMapTests"`
Expected: PASS — the pre-existing `CidCMap` tests are unchanged and unaffected.

Then run `git diff PdfLibrary/Fonts/CidCMap.cs` and confirm the diff is **purely additive**: no line inside `Parse`, `ParseCidChar`, `ParseCidRange` or `FindBlocks` is modified. Report the diff's insertion/deletion counts.

- [ ] **Step 7: Run the full suite for regressions**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add PdfLibrary/Fonts/CidCMap.cs PdfLibrary.Tests/Fonts/CidCMapMaxCidTests.cs
git commit -m "feat(fonts): report the largest CID a CMap declares

ISO 19005-2 6.1.13 test 10 bounds it at 65535. A separate scan from
Parse, which materialises every code in every range into a dictionary —
a caller needing only a maximum should not pay that, and Parse's decode
path stays untouched. Nothing consumes it yet."
```

---

### Task 2: The 6.1.13 test 10 sub-check

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/ImplementationLimitsRule.cs` — class doc comment, `Check`, plus a new sub-check and constant
- Test: `PdfLibrary.Tests/Conformance/ImplementationLimitsCidTests.cs` (create)

**Interfaces:**
- Consumes: `CidCMap.MaxDeclaredCid(byte[]) → long?` from Task 1.
- Produces: additional `Finding`s on clause `6.1.13` from the existing rule id `"implementation-limits"`. No new rule id, no `Preflighter.cs` change.

**Two traps this task must not fall into, both already diagnosed:**

1. **`Check` ends with a bare `yield break`.** It reads `if (integerReported) yield break;` — which terminates the **whole iterator**, not just the integer arm. A sub-check appended after it would be silently skipped on any document that also has an out-of-range integer. Scoping that is a prerequisite, not a tidy-up, and it has its own test.
2. **The message must not contain the word "integer".** `IsIntegerFinding` is a `Message.Contains("integer")` substring test. It is applied only to `CheckStringsAndNames` output today, so a CID finding would not be tested by it — but relying on that is exactly the latent coupling that breaks on the next edit.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Conformance/ImplementationLimitsCidTests.cs`:

```csharp
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// ISO 19005-2 clause 6.1.13 test 10 (<see cref="ImplementationLimitsRule"/>): no CID above 65535.
/// veraPDF's object is <c>CMapFile</c> with <c>maximalCID</c>, so this is a property of what the
/// embedded CMap DECLARES, not a scan of CIDs used in content.
///
/// <para>Corpus fixtures "veraPDF test suite 6-1-13-t08-fail-b.pdf" and "…-t10-fail-a.pdf" are the
/// end-to-end proof — both carry <c>&lt;3f00&gt; &lt;3fff&gt; 65536</c>, a top CID of 65791, in a
/// Type0 font whose /Encoding is a /CMap stream. These pin the logic without the corpus, which the
/// <c>Category=Parity</c> tests skip when it is absent.</para>
/// </summary>
public class ImplementationLimitsCidTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static Finding[] CidFindings(PdfDocument doc) =>
        [.. new ImplementationLimitsRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => f.Message.Contains("CID", System.StringComparison.Ordinal))];

    private const string OverLimitCMap =
        "/CIDInit /ProcSet findresource begin\n1 begincidrange\n<3f00> <3fff> 65536\nendcidrange\nend";

    private const string ConformingCMap =
        "/CIDInit /ProcSet findresource begin\n1 begincidrange\n<0000> <00ff> 0\nendcidrange\nend";

    /// <summary>A one-page document with a Type0 font whose /Encoding is the given CMap — as a
    /// stream when <paramref name="cmapBody"/> is given, otherwise the predefined name Identity-H.
    /// <paramref name="pageContent"/> lets a caller add an unrelated violation.</summary>
    private static PdfDocument Doc(string? cmapBody, string pageContent = "BT ET")
    {
        var doc = new PdfDocument();

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("Test-Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(7)),
        };
        if (cmapBody is null)
            font[N("Encoding")] = N("Identity-H");
        else
        {
            doc.AddObject(6, 0, new PdfStream(
                new PdfDictionary { [N("Type")] = N("CMap") }, Encoding.ASCII.GetBytes(cmapBody)));
            font[N("Encoding")] = Ref(6);
        }

        doc.AddObject(7, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"), [N("Subtype")] = N("CIDFontType0"), [N("BaseFont")] = N("Test"),
        });
        doc.AddObject(5, 0, font);

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(5) },
            },
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void An_over_limit_CID_in_an_embedded_CMap_is_flagged()
    {
        Finding f = Assert.Single(CidFindings(Doc(OverLimitCMap)));

        Assert.Equal("implementation-limits", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("6.1.13", f.Clause, System.StringComparison.Ordinal);
        Assert.Contains("65791", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_conforming_CMap_is_accepted()
    {
        Assert.Empty(CidFindings(Doc(ConformingCMap)));
    }

    [Fact]
    public void A_predefined_encoding_name_is_not_examined()
    {
        // Identity-H has no embedded file to read, and its maximum CID is exactly 65535 anyway.
        Assert.Empty(CidFindings(Doc(cmapBody: null)));
    }

    [Fact]
    public void The_message_avoids_the_word_integer()
    {
        // IsIntegerFinding distinguishes this rule's integer findings by a substring test on the
        // message. A CID finding containing "integer" would be misclassified by it.
        Finding f = Assert.Single(CidFindings(Doc(OverLimitCMap)));
        Assert.DoesNotContain("integer", f.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_out_of_range_integer_does_not_suppress_the_CID_finding()
    {
        // THE REGRESSION TEST for Check's scoping. Before this task, Check ended with a bare
        // `yield break` on the integer arm, which terminates the WHOLE iterator — so a document
        // carrying both violations would report the integer one and silently drop this one.
        Finding[] all = [.. new ImplementationLimitsRule()
            .Check(new ConformanceContext(Doc(OverLimitCMap, "BT 0 2157483648 Td ET"),
                                          ConformanceProfile.PdfA2b))];

        Assert.Contains(all, f => f.Message.Contains("integer", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(all, f => f.Message.Contains("CID", System.StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~ImplementationLimitsCidTests"`
Expected: FAIL — the CID assertions find no findings (`Assert.Single` gets an empty collection).

- [ ] **Step 3: Add the limit constant and the sub-check**

In `PdfLibrary/Conformance/Rules/ImplementationLimitsRule.cs`, add the using for the CMap scanner at the top of the file, after `using PdfLibrary.Document;`:

```csharp
using PdfLibrary.Fonts;
```

Add the constant next to the other limits (locate them by `private const int MaxNameBytes = 127;` and insert after):

```csharp
    private const long MaxCid = 65535;
```

Add the sub-check as a new method, placed after `CheckContentStreamIntegers` and its `IntegersIn` helper:

```csharp
    // ── Sub-check 6 — a CID above 65535 in an embedded CMap (6.1.13 test 10) ────────────────────
    private IEnumerable<Finding> CheckCMapCids(ConformanceContext context)
    {
        var seenCMaps = new HashSet<int>();

        foreach (PdfDictionary font in context.ReferencedFonts)
        {
            // Only a Type0 font carries /Encoding. Its descendant CIDFont appears in
            // ReferencedFonts as its own separate flat entry and has none, so this filter
            // excludes it naturally.
            if (context.ResolveName(font.Get("Subtype")) != "Type0")
                continue;

            // Direct dictionary navigation, NOT Type0Font's parsed CMap: that path is gated on
            // CIDSystemInfo/Registry == "Adobe" plus a bundled Ordering, so a font failing either
            // never has its CMap fetched at all. Same route FontDictionaryRule already uses.
            if (context.Resolve(font.Get("Encoding")) is not PdfStream cmap)
                continue; // a predefined CMap name has no embedded file to read

            if (cmap.IsIndirect && !seenCMaps.Add(cmap.ObjectNumber))
                continue; // several fonts can share one CMap; scan it once

            byte[] data;
            try { data = cmap.GetDecodedData(context.Document.Decryptor); }
            catch { continue; } // an undecodable stream is a different clause's concern

            if (CidCMap.MaxDeclaredCid(data) is not { } max || max <= MaxCid)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.1.13"),
                Message = $"A CMap declares CID {max}, exceeding the maximum permitted "
                        + $"CID value of {MaxCid}.",
            };
            yield break; // one finding is enough to mark the document non-conformant
        }
    }
```

- [ ] **Step 4: Scope the `yield break` and wire the sub-check in**

Replace the tail of `Check` — currently:

```csharp
        if (integerReported)
            yield break;

        foreach (Finding f in CheckContentStreamIntegers(context))
            yield return f;
    }
```

with:

```csharp
        // Scoped to the integer arm ONLY. This was a bare `yield break`, which ends the WHOLE
        // iterator — so every sub-check below it was silently skipped on any document that also
        // carried an out-of-range integer, a suppression that reads exactly like absence.
        if (!integerReported)
            foreach (Finding f in CheckContentStreamIntegers(context))
                yield return f;

        foreach (Finding f in CheckCMapCids(context))
            yield return f;
    }
```

- [ ] **Step 5: Update the class doc comment**

Two places still say the CID limit is out of scope. First, replace this (near the top of the comment):

```
/// PDF/A implementation limits (ISO 19005-2, 6.1.13; the referenced limits are ISO 32000-1 Annex C).
/// Three tractable sub-checks — the CID &gt; 65535 (needs an embedded-CMap parser) limit is out of
/// scope for this slice:
```

with:

```
/// PDF/A implementation limits (ISO 19005-2, 6.1.13; the referenced limits are ISO 32000-1 Annex C).
/// Three tractable sub-checks:
```

Then replace the closing sentence of the fifth-sub-check paragraph:

```
/// never moved. The q/Q-nesting (test 8) and CID (test 10) limits remain out of scope.
```

with:

```
/// never moved.
///
/// A sixth sub-check covers the CID limit (test 10). veraPDF's object is <c>CMapFile</c> with
/// <c>maximalCID</c>, so this is a property of what an embedded <c>/Encoding</c> CMap DECLARES,
/// not a scan of CIDs used in content — <see cref="CidCMap.MaxDeclaredCid"/> answers it without
/// materialising the code→CID map. Only embedded CMap STREAMS are examined: a predefined name
/// carries no file to read, and Identity-H's maximum CID is exactly 65535 in any case. The
/// q/Q-nesting (test 8) limit remains out of scope.
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~ImplementationLimitsCidTests"`
Expected: PASS, all five tests.

- [ ] **Step 7: Probe both guards**

First, the scoping fix. Temporarily restore the bare `yield break` (`if (integerReported) yield break;` with the CID loop after it) and re-run.
Expected: `An_out_of_range_integer_does_not_suppress_the_CID_finding` FAILS while the other four still pass. Restore.

Second, the limit comparison. Temporarily change `max <= MaxCid` to `max <= long.MaxValue` and re-run.
Expected: `An_over_limit_CID_in_an_embedded_CMap_is_flagged`, `The_message_avoids_the_word_integer` and `An_out_of_range_integer_does_not_suppress_the_CID_finding` FAIL. Restore.

Report the actual observed output of both probes.

- [ ] **Step 8: Run the full suite for regressions**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`
Expected: PASS. A rule that now walks every document's referenced fonts runs against every existing conformance fixture — any NEW failure is a false positive in this sub-check. Investigate and report it; do not suppress it or weaken the unrelated test.

- [ ] **Step 9: Commit**

```bash
git add PdfLibrary/Conformance/Rules/ImplementationLimitsRule.cs PdfLibrary.Tests/Conformance/ImplementationLimitsCidTests.cs
git commit -m "feat(conformance): detect a CID above 65535 (6.1.13 t10)

Walks referenced Type0 fonts and reads each embedded /Encoding CMap by
direct dictionary navigation — not Type0Font's parsed CMap, which is
gated on Registry == Adobe plus a bundled Ordering.

Also scopes Check's integer suppression. It was a bare yield break,
which ends the whole iterator, so this sub-check would have been
silently skipped on any document that also had an out-of-range integer."
```

---

### Task 3: Verify parity, re-baseline, and pin

**Files:**
- Modify: `PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md` (regenerated)
- Modify: `Pellucid.App.Tests/oi-corpus-baseline.txt` in the **Pellucid** repo (hand-edited)
- Modify: Pellucid `ci/dependencies.json` (engine pin)

**Interfaces:**
- Consumes: everything from Tasks 1-2.
- Produces: the verified numbers this plan exists to deliver.

No new code. This is the measurement that decides whether the work landed, and where a false positive would surface. Report what you observe, including if it disappoints.

- [ ] **Step 1: Confirm the corpus is present**

Run: `ls ../veraPDF-corpus/PDF_A-2b`
Expected: the clause folders listed. If absent, the `Category=Parity` tests SKIP and this task cannot be completed — say so rather than reporting success.

- [ ] **Step 2: Run the parity gate**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category=Parity"`
Expected: PASS. Read the **SKIP count**, not just the PASS count — a skipped parity leg proves nothing. Report both.

- [ ] **Step 3: Regenerate the parity report**

`PARITY_REPORT` must be an **absolute** path: `dotnet test` runs with its working directory set to the test output folder, not the repo root, so a relative path throws `DirectoryNotFoundException`.

```bash
PARITY_REPORT="$(pwd)/PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md" \
  dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj \
  --filter "FullyQualifiedName~ParityReportTests.Generate_parity_report"
```

- [ ] **Step 4: Check the numbers against the definition of done**

Read the regenerated `PARITY-REPORT.md` and confirm every one of these:

- PDF/A-2b agreement is **978/986**, up from 976/986, with whole-file misses down from 10 to **8**.
- **PdfLibrary FP is 0 on every profile** — the standing invariant. Non-zero means this sub-check over-reports; fix the rule, never the expectation.
- Clause 6.1.13 shows **15/15, full**.
- A-2u 22/22, A-3b 12/12, UA-1 296/296 all unchanged.
- The verdict-leverage section no longer lists 6.1.13 at all, leaving only 6.2.2 and the three font clauses.

- [ ] **Step 5: Verify the two target files individually**

Aggregate counts can be right for the wrong reason. Confirm each target file is caught by the mechanism intended for it — copy both out and scan them, or assert from the report's miss list that neither appears any longer:

- `veraPDF test suite 6-1-13-t08-fail-b.pdf` — was blocked by 6.1.13 alone.
- `veraPDF test suite 6-1-13-t10-fail-a.pdf` — was blocked by 6.1.13 + 6.2.11.4.1 + 6.2.11.8; reporting the CID alone is what closes it.

- [ ] **Step 6: Commit the engine work**

```bash
git add PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "test(parity): A-2b 976/986 -> 978/986, clause 6.1.13 to full

Zero false positives across all 1316 files. The remaining eight misses
are 6.2.2 (three) and the font clauses (five)."
```

- [ ] **Step 7: Report, do not merge or push**

Report the parity numbers and the branch head SHA. **Do not merge to master, do not push, and do not touch the Pellucid repo** — the controller handles the merge, the engine pin, the oi-corpus rebaseline and the cross-repo sequencing, because the two repos must move as an atomic unit and the engine must be pushed first.

For the controller's reference, the oi-corpus expectation is: `conforms` falls by 2 and `fails` rises by 2, with `fixed` and `needsDecision` **flat** — that flatness is the tell that this is detection gained rather than repairs lost. Hand-edit the data line; `PELLUCID_OI_CORPUS_REGEN=1` destroys the file's decomposition history.

---

## Self-Review

**Spec coverage.** Every section maps to a task: §3.1 the cheap scan → Task 1; §3.2 the three under-reports → Task 1 (span guard tested explicitly; `usecmap` and predefined CMaps are properties of what the scan reads, covered by the predefined-name test in Task 2); §3.3 the unguarded navigation path → Task 2 Step 3; §3.4 the `yield break` trap → Task 2 Steps 4 and 7; §3.5 message wording → Task 2 Steps 3 and 6; §5 verification and §6 definition of done → Task 3. §4's out-of-scope items are respected: no `FontProgramRule` change appears anywhere in this plan.

**Type consistency.** `MaxDeclaredCid(byte[]) → long?` is defined in Task 1 and consumed in Task 2 with that exact name and nullable-`long` handling (`is not { } max`). `MaxCid` is a `long` constant so the comparison `max <= MaxCid` needs no cast. `context.ReferencedFonts` is `IReadOnlyList<PdfDictionary>`, `context.ResolveName(PdfObject?)` returns `string?`, and `context.Resolve(PdfObject?)` returns `PdfObject?` — all matching the engine's current signatures.

**Known risk, left to measurement.** `ReferencedFonts` enumerates fonts present in resource dictionaries regardless of whether content actually uses them, so this sub-check could in principle flag a font veraPDF's object model never instantiates a `CMapFile` for. Task 3 Step 4's zero-false-positive check over 1316 files is the arbiter, and the instruction there is explicit: narrow the arm, never adjust the expectation.
