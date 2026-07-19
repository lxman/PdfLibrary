# Font-program Slice 2 — Type3 font metric (widths) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `FontProgramRule` to check font-metric consistency (ISO 19005-2 6.2.11.5 / ISO 14289-1 7.21.5) for **Type3** fonts — comparing each glyph's declared `/Widths` value against the `wx` operand of the `d0`/`d1` operator in its CharProc content stream.

**Architecture:** Type3 fonts have no embedded font *program* (glyphs are content streams), so they never reach the existing `CheckType0`/`CheckSimple` — `Check` skips them at the `GetEmbeddedMetrics() is null` guard. This slice adds a `Type3Font` dispatch branch in `Check` before that guard, routing to a new `CheckType3` that reads each glyph's `d0`/`d1` width via `PdfContentParser`. Deterministic and FP-safe: unresolvable glyph / unparseable CharProc / missing `d0`-`d1` → skip.

**Tech Stack:** C# (.NET 10), xUnit v3 (`ITestOutputHelper` is in `Xunit`, no `Xunit.Abstractions`). Engine repo `PdfLibrary` (master `753d8d0` after slice 1).

## Global Constraints

- **0 false positives, corpus-wide** — verified via the regenerated parity report before any floor bump. Any glyph whose CharProc can't be found/parsed, or has no `d0`/`d1`, is **skipped**, never guessed.
- **Scope is Type3 widths (6.2.11.5 / 7.21.5) only.** No Type3 `.notdef`/glyph-present this slice. The deferred width cases (predefined-charset CFF, symbolic TrueType, Type0-CMap) stay deferred — they are FP-traps or other-slice capabilities, confirmed by corpus probe.
- Width comparison is in **raw glyph space**: `/Widths[code]` and the `d0`/`d1` `wx` are both glyph-space values and compare directly (FontMatrix scales both equally, so it is irrelevant to consistency). Reuse the existing `WidthTolerance` (10.0).
- RM3 exemption applies (6.2.11.5 is render-mode-3-exempt) → iterate `usage.VisibleCodes`, consistent with the other width checks.
- Profile-aware via `Make(context, font, "5", …)` → `6.2.11.5` (A-2) / `7.21.5` (UA-1). PDF/X-4 excluded (rule's `AppliesToProfiles` already gates this).
- `PdfObject` is in namespace `PdfLibrary.Core`. Never `git add` the untracked `Docs/plans/2026-07-10-*.md`. Slice branch off master; merge `--no-ff`; push at end.
- Reference floor: A-2b agreement **937** in `ParityReportTests.cs` (UA-1 281). Suite ~2290 tests.

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `PdfLibrary/Conformance/Rules/FontProgramRule.cs` | Type3 dispatch + `CheckType3` + `Type3ProgramWidth` | Modify |
| `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs` | Unit tests (synthetic Type3 doc vs `d0`/`d1`) | Modify |
| `PdfLibrary.Tests/Conformance/ParityReportTests.cs` | A-2b floor ratchet | Modify (Task 3) |

## Background the implementer needs

**Ground truth (corpus probe, 2026-07-18):** of 13 files failing 6.2.11.5, exactly one clean, not-yet-agreed target is a Type3 font: `veraPDF test suite 6-2-11-5-t01-fail-c.pdf` (fails **only** 6.2.11.5). Its font: `/Subtype /Type3`, `/FontMatrix [0.001 0 0 0.001 0 0]`, `/FirstChar 97 /LastChar 98`, `/Widths [0 0]`; each CharProc begins `1000 0 0 0 750 750 d1` (so program `wx` = 1000). Declared 0 vs program 1000 → inconsistent. Closing it: A-2b 937 → **938** (measured per regen).

**Engine APIs (verified):**
- `Type3Font` (`PdfLibrary/Fonts/Type3Font.cs`, internal): `double[] FontMatrix`, `PdfStream? GetCharProc(string glyphName)` (resolves indirect), `string? GetGlyphName(int charCode)` (via `Encoding`), `int FirstChar`, `PdfDictionary FontDictionary` (via base `PdfFont`), `string BaseFont`.
- `PdfFont.Create` returns a `Type3Font` for `/Subtype /Type3`, and `GetFontObject` yields it — so `usage.Font is Type3Font` holds. Type3 fonts DO appear in `context.UsedTextGlyphs` (the collector treats them as simple one-byte-code fonts).
- `PdfContentParser.Parse(byte[])` → `List<PdfOperator>`. An unknown operator (like `d0`/`d1`) is returned as `GenericOperator(string Name, List<PdfObject> Operands)` (`PdfContentParser.cs:276`); `PdfOperator.Name` and `PdfOperator.Operands` are public. For `1000 0 0 0 750 750 d1`, `Operands[0].ToDouble()` = 1000 = `wx`.
- `PdfStream.GetDecodedData(context.Document.Decryptor)` → decoded bytes.
- Existing helpers in `FontProgramRule`: `WidthTolerance` (10.0), `Make(context, font, sub, message)`, `Name(font)`, and the `metricsReported` `HashSet<string>` built at the top of `Check`.

**Current `Check` head (post-slice-1) you are modifying:**
```csharp
    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var notdefReported = new HashSet<string>(StringComparer.Ordinal);
        var metricsReported = new HashSet<string>(StringComparer.Ordinal);
        var presentReported = new HashSet<string>(StringComparer.Ordinal);

        foreach (UsedFontCodes usage in context.UsedTextGlyphs)
        {
            PdfFont font = usage.Font;
            EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
            if (metrics is null || !metrics.IsValid)
                continue; // not embedded, or the program will not parse — nothing to compare (FP-safe)

            foreach (Finding f in font is Type0Font type0
                         ? CheckType0(context, type0, metrics, usage.Codes, usage.VisibleCodes, notdefReported, metricsReported)
                         : CheckSimple(context, font, metrics, usage.Codes, usage.VisibleCodes, metricsReported, notdefReported, presentReported))
            {
                yield return f;
            }
        }
    }
```

---

## Task 1: Type3 width check (6.2.11.5 / 7.21.5)

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/FontProgramRule.cs`
- Test: `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs`

**Interfaces:**
- Produces (private): `CheckType3(ConformanceContext, Type3Font, IReadOnlyCollection<int> visibleCodes, HashSet<string> metricsReported)`, `Type3ProgramWidth(ConformanceContext, Type3Font, int code)`.
- Consumes: `Make`, `Name`, `WidthTolerance`, `PdfContentParser.Parse`, `GenericOperator`.

- [ ] **Step 1: Write the failing tests**

Add to `PreflightSlice19Tests.cs`. These build a synthetic Type3 font whose CharProc declares a program width via `d0`, and assert an inconsistent `/Widths` fails 6.2.11.5 while a consistent one passes. Add near the metrics tests.

```csharp
    // ── Type3 font metrics (6.2.11.5 / 7.21.5) — slice 2 ──────────────────────────────────────────────

    // A one-glyph Type3 font: code 'A' → glyph "a", whose CharProc sets program width via `<progWidth> 0 d0`.
    // /Widths declares declaredWidth for code 'A'. FontMatrix is the conventional 1/1000 glyph space.
    private static PdfDocument Type3Doc(int declaredWidth, int programWidth)
    {
        var charProc = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"{programWidth} 0 d0 0 0 750 750 re f"));
        var encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("Differences")] = new PdfArray(new PdfInteger('A'), N("a")),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type3"),
            [N("FontBBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(750), new PdfInteger(750)),
            [N("FontMatrix")] = new PdfArray(new PdfReal(0.001), new PdfReal(0), new PdfReal(0), new PdfReal(0.001), new PdfReal(0), new PdfReal(0)),
            [N("FirstChar")] = new PdfInteger('A'),
            [N("LastChar")] = new PdfInteger('A'),
            [N("Widths")] = new PdfArray(new PdfInteger(declaredWidth)),
            [N("Encoding")] = Ref(4),
            [N("CharProcs")] = Ref(3),
        };
        var charProcs = new PdfDictionary { [N("a")] = Ref(5) };
        return DocWith(font, Encoding.ASCII.GetBytes("(A)"),
            (3, charProcs), (4, encoding), (5, charProc));
    }

    [Fact]
    public void Type3_consistent_width_passes()
    {
        Assert.Empty(Run(Type3Doc(declaredWidth: 1000, programWidth: 1000)));
    }

    [Fact]
    public void Type3_inconsistent_width_fails_metrics()
    {
        Finding f = Assert.Single(Run(Type3Doc(declaredWidth: 0, programWidth: 1000)));
        Assert.Equal("6.2.11.5", Clause(f));
        Assert.Contains("Type3", f.Message);
    }

    [Fact]
    public void Type3_metrics_finding_is_profile_aware_under_pdfua1()
    {
        Finding f = Assert.Single(Run(Type3Doc(declaredWidth: 0, programWidth: 1000), ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.5", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    [Fact]
    public void Type3_missing_charproc_is_skipped()
    {
        // /CharProcs has no entry for "a" → program width can't be read → no finding (FP-safe).
        PdfDocument doc = Type3Doc(declaredWidth: 0, programWidth: 1000);
        // Replace the CharProcs dict (object 3) with an empty one.
        doc.AddObject(3, 0, new PdfDictionary());
        Assert.Empty(Run(doc));
    }
```

Note: the CharProc content is `<wx> 0 d0 …path…`. `d0` takes two operands (`wx wy`); `Operands[0]` is `wx`. The trailing `re f` (a rectangle path) is ignored by the width reader.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice19 -c Debug`
Expected: the four `Type3_*` tests FAIL (`Type3_consistent_width_passes` currently passes vacuously since Type3 is skipped, but `Type3_inconsistent_width_fails_metrics` / `..._profile_aware` FAIL with 0 findings — Type3 isn't checked yet). Every pre-existing test still passes.

- [ ] **Step 3: Add the Type3 dispatch + `CheckType3` + `Type3ProgramWidth`**

In `FontProgramRule.cs`, change the `Check` loop to dispatch Type3 before the metrics guard:

```csharp
        foreach (UsedFontCodes usage in context.UsedTextGlyphs)
        {
            PdfFont font = usage.Font;

            // Type3 fonts have no embedded program (glyphs are content streams), so they never reach the
            // metrics-based checks below. Their 6.2.11.5 width comes from the CharProc's d0/d1 operator.
            if (font is Type3Font type3)
            {
                foreach (Finding f in CheckType3(context, type3, usage.VisibleCodes, metricsReported))
                    yield return f;
                continue;
            }

            EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
            if (metrics is null || !metrics.IsValid)
                continue; // not embedded, or the program will not parse — nothing to compare (FP-safe)

            foreach (Finding f in font is Type0Font type0
                         ? CheckType0(context, type0, metrics, usage.Codes, usage.VisibleCodes, notdefReported, metricsReported)
                         : CheckSimple(context, font, metrics, usage.Codes, usage.VisibleCodes, metricsReported, notdefReported, presentReported))
            {
                yield return f;
            }
        }
```

Add the two methods (place after `CheckSimple`):

```csharp
    // ── Type3 fonts — metrics only (glyphs are content streams; width from the d0/d1 operator) ─────────
    private IEnumerable<Finding> CheckType3(
        ConformanceContext context, Type3Font font, IReadOnlyCollection<int> visibleCodes,
        HashSet<string> metricsReported)
    {
        // 6.2.11.5 / 7.21.5 for Type3: the /Widths value and the CharProc's d0/d1 width (both raw glyph
        // space) must be consistent. RM3-exempt → visibleCodes. FP-safe: any code whose glyph name,
        // CharProc, or d0/d1 width can't be resolved is skipped.
        if (context.Resolve(font.FontDictionary.Get("Widths")) is not PdfArray widths)
            yield break;

        double worstDiff = 0;
        foreach (int code in visibleCodes)
        {
            int index = code - font.FirstChar;
            if (index < 0 || index >= widths.Count)
                continue; // no declared width for this code

            double? program = Type3ProgramWidth(context, font, code);
            if (program is null)
                continue; // glyph/CharProc/d0-d1 not resolvable — skip rather than guess

            double declared = widths[index].ToDouble();
            worstDiff = Math.Max(worstDiff, Math.Abs(declared - program.Value));
        }

        if (worstDiff > WidthTolerance && metricsReported.Add(font.BaseFont))
            yield return Make(context, font, "5",
                $"The Type3 font {Name(font)} declares a glyph width that differs from the CharProc's "
                + $"d0/d1 width by {worstDiff:F0} units (tolerance {WidthTolerance:F0}).");
    }

    /// <summary>
    /// The program (glyph-space) advance width of a Type3 glyph: the <c>wx</c> operand of the first
    /// <c>d0</c>/<c>d1</c> operator in the glyph's CharProc (ISO 32000-1 9.6.5.1 requires one of these as the
    /// procedure's first operator). Returns null when the code has no glyph name, no CharProc, an unparseable
    /// CharProc, or no d0/d1 — the caller then skips it (FP-safe).
    /// </summary>
    private static double? Type3ProgramWidth(ConformanceContext context, Type3Font font, int code)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName))
            return null;

        PdfStream? charProc = font.GetCharProc(glyphName);
        if (charProc is null)
            return null;

        List<PdfOperator> ops;
        try { ops = PdfContentParser.Parse(charProc.GetDecodedData(context.Document.Decryptor)); }
        catch { return null; }

        foreach (PdfOperator op in ops)
            if (op is GenericOperator { Name: "d0" or "d1" } g && g.Operands.Count >= 1)
                return g.Operands[0].ToDouble();
        return null;
    }
```

Add these two usings at the top of `FontProgramRule.cs` (verified — the file currently has neither): `using PdfLibrary.Content;` (for `PdfContentParser` and `PdfOperator`) and `using PdfLibrary.Content.Operators;` (for `GenericOperator`). `System.Collections.Generic` is already imported.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice19 -c Debug`
Expected: all `PreflightSlice19` tests PASS, including the four `Type3_*` tests.

- [ ] **Step 5: Run the full Conformance group for collateral**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~Conformance -c Debug`
Expected: PASS. Note the count for Task 2.

- [ ] **Step 6: Commit**

```bash
cd /Users/michaeljordan/RiderProjects/PdfLibrary
git checkout -b feat/font-program-slice2-type3-widths
git add PdfLibrary/Conformance/Rules/FontProgramRule.cs PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs
git commit -m "feat(conformance): Type3 font metric check via d0/d1 width (6.2.11.5)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"
```

---

## Task 2: Corpus acceptance gate — clause closure + 0 false positives

**Files:** none modified (verification only; possible resolver tightening if FP > 0).

- [ ] **Step 1: Regenerate the parity report**

Run:
```bash
SCRATCH=/private/tmp/claude-501/-Users-michaeljordan-RiderProjects-Pellucid/922bccd6-d2dc-4acd-bb50-d014106958be/scratchpad/parity-slice2.md
PARITY_REPORT="$SCRATCH" dotnet test PdfLibrary.Tests \
  --filter FullyQualifiedName~Generate_parity_report -c Release
```
Expected: PASS; report written to `$SCRATCH`.

- [ ] **Step 2: Verify 0 false positives and the clause closed**

```bash
grep -nE "false positives" "$SCRATCH" | head -1
sed -n '/## Verdict agreement/,/## Clause coverage/p' "$SCRATCH" | grep -E "A-2b|UA-1"
grep -nE "6\.2\.11\.5 " "$SCRATCH"
```
Expected: **0 false positives** across all 1316 files; PDF/A-2b agreement **938** (up from 937 — `6-2-11-5-t01-fail-c` flips); 6.2.11.5 clause coverage **7/13** (up from 6). If any false positive appears, identify the conformant Type3 file, tighten `Type3ProgramWidth`/`CheckType3` to skip that shape (return null), and re-run. FP == 0 is mandatory before the floor bump.

- [ ] **Step 3: Run the parity floor gate + full suite**

Run:
```bash
dotnet test PdfLibrary.Tests --filter FullyQualifiedName~ParityReport -c Release
dotnet test PdfLibrary.Tests -c Release
```
Expected: `ParityReport` passes against the current floor (agreement only rose); full suite green (≈ 2290 + the 4 new tests, 0 failures).

---

## Task 3: Ratchet the floor, merge, push

**Files:** Modify `PdfLibrary.Tests/Conformance/ParityReportTests.cs`.

- [ ] **Step 1: Raise the A-2b floor to the measured value**

In `ParityReportTests.cs`, set `[ConformanceProfile.PdfA2b]` to the exact new agreement from Task 2 (expected **938**) and append to its comment, e.g.:
```csharp
            [ConformanceProfile.PdfA2b] = 938,   // …existing… + Type3 font metrics (6.2.11.5 via CharProc d0/d1 → 7/13), 0 FP (font-program slice 2)
```
Leave UA-1 at 281 unless the regen shows it rose.

- [ ] **Step 2: Verify the floor holds**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~ParityReport -c Release`
Expected: PASS at the new floor.

- [ ] **Step 3: Matterhorn note (no count change)**

Type3 width is covered by the existing **31-016** (7.21.5-1, glyph width), already marked `font-program` — no matrix tick changes. Optionally add a one-line dated note in `Docs/pdfua/matterhorn-coverage.md` that 31-016 now also covers Type3 (via CharProc d0/d1). Skip if it adds noise.

- [ ] **Step 4: Commit, merge, push**

```bash
git add PdfLibrary.Tests/Conformance/ParityReportTests.cs
git commit -m "test(conformance): ratchet A-2b floor to 938 (Type3 metrics, font slice 2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"

git checkout master
git merge --no-ff feat/font-program-slice2-type3-widths -m "Merge feat/font-program-slice2-type3-widths: Type3 font metric check (6.2.11.5)"
git push origin master
```
Expected: clean merge; push succeeds. Confirm the untracked `Docs/plans/2026-07-10-*.md` were never staged.

---

## Self-review

**Spec coverage** (Slice 2, refined to Type3 after corpus probe):
- Type3 width check (6.2.11.5 / 7.21.5) via CharProc d0/d1 — Task 1. ✓
- RM3-exempt (visible codes) — `CheckType3` iterates `visibleCodes`. ✓
- FP-safe skips (no glyph / no CharProc / unparseable / no d0-d1) — `Type3ProgramWidth` returns null. ✓
- 0-FP corpus gate before floor bump — Task 2. ✓
- Deferred (documented): predefined-charset CFF, symbolic TrueType, Type0-CMap width cases — not in scope, per the probe. ✓

**Placeholder scan:** No TBD/TODO. The floor value (938) is the measured corpus result with the exact source command; the one soft instruction is verifying the `PdfLibrary.Content` / `PdfLibrary.Content.Operators` namespaces for the new usings, which the compiler resolves deterministically.

**Type consistency:** `CheckType3(ConformanceContext, Type3Font, IReadOnlyCollection<int>, HashSet<string>)` and `Type3ProgramWidth(ConformanceContext, Type3Font, int)` are named identically across Task 1's dispatch and body. `Make(context, font, "5", …)` maps to `6.2.11.5`/`7.21.5` (same as the existing width emissions). `GenericOperator.Name`/`.Operands` and `PdfOperator` match the parser's output type.
