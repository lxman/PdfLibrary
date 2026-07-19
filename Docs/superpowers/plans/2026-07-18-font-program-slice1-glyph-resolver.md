# Font-program Slice 1 — simple-font glyph resolver (.notdef + glyph-present) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `FontProgramRule` to resolve a shown character code to a glyph in the embedded program for **simple** fonts (Type1/TrueType/CFF), closing veraPDF `.notdef` (6.2.11.8 / 7.21.8) and glyph-present (6.2.11.4.1 t2 / 7.21.4.1 t2) for simple fonts — today only Type0-Identity is covered.

**Architecture:** A new private tri-state resolver in `FontProgramRule` returns `Present` / `NotDef` / `Unknown` for a simple-font code, reusing the encoding→name→Unicode→cmap path (TrueType) and encoding→name→charset-GID path (CFF/Type1) already present in the width helpers. Only a *confident* `NotDef` produces a finding; `Unknown` skips silently. This is the FP-safe escape hatch that lets us attempt symbolic/built-in-encoding fonts without breaking the 0-false-positive invariant. The corpus parity harness — not the unit tests — is the real breadth/FP oracle (mirroring the existing `PreflightSlice19Tests` doc-comment).

**Tech Stack:** C# (.NET 10), xUnit. Engine repo `PdfLibrary` (origin/master `2f20ca2`). Rule at `PdfLibrary/Conformance/Rules/FontProgramRule.cs`; unit tests at `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs`; corpus oracle at `PdfLibrary.Tests/Conformance/ParityReportTests.cs`.

## Global Constraints

- **0 false positives, corpus-wide** — verified via the regenerated parity report before any floor bump. `Unknown → skip`; never guess.
- Font rules are **profile-aware**: one rule serves PDF/A-2 and PDF/UA-1 via `ConformanceClauses.For(target, sub)`. PDF/X-4 stays excluded.
- **CFF gated on `metrics.CffHasEmbeddedCharset`** — predefined-charset CFF is `Unknown` (the `Type1Table.cs` predefined-charset parser bug has rendering blast radius; do not touch it).
- **Never** route through `EmbeddedFontMetrics.GetAdvanceWidthByName` (hardcodes 500 for CFF) — not used in this slice, but the resolver must not reintroduce it.
- `PdfObject` is in namespace `PdfLibrary.Core`. `PdfName` stores 1 byte/char (Latin1).
- **Never** `git add` the untracked `Docs/plans/2026-07-10-*.md` (pre-existing, not ours).
- Work on a slice branch off `master`; merge `--no-ff`; push at the end.
- Reference values (today): A-2b agreement floor **935**, UA-1 **281** in `ParityReportTests.cs`. Suite **2,239** tests.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `PdfLibrary/Conformance/Rules/FontProgramRule.cs` | The tri-state resolver + `.notdef`/glyph-present emission for simple fonts | Modify |
| `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs` | Unit tests pinning message, profile mapping, FP-safe skips (synthetic PDFs vs the real `PublicPixel.ttf`) | Modify |
| `PdfLibrary.Tests/Conformance/ParityReportTests.cs` | Whole-file agreement floor (ratchet) | Modify (Task 4) |
| `Docs/pdfua/matterhorn-coverage.md` | Matterhorn 31-xxx coverage matrix | Modify (Task 4) |

No new files, no `Preflighter.cs` change — `FontProgramRule` is already registered (`Preflighter.cs:45`).

---

## Background the implementer needs

**Existing `FontProgramRule` shape** (read the whole file first):
- `Check` iterates `context.UsedTextGlyphs` (`UsedFontCodes { PdfFont Font; IReadOnlyCollection<int> Codes }`), gets `EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics()`, skips when `metrics is null || !metrics.IsValid`, then dispatches to `CheckType0` (Type0 composite) or `CheckSimple`.
- `CheckType0` already reports `.notdef` (sub `"8"`) for Type0-Identity fonts (a shown code whose CID→GID is 0). **Do not change Type0 behavior in this slice.**
- `CheckSimple` currently does *widths only*, for TrueType and simple CFF with an embedded charset, and early-returns unless `/Widths` is present.
- Helpers already present: `TrueTypeAdvance(font, metrics, code)` and `SimpleCffAdvance(font, metrics, code)` (both `double?`, null = skip), `Scale`, `Name`, `Make(context, font, sub, message)`.

**`EmbeddedFontMetrics` methods this slice uses** (all public, verified in `PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs`):
- `ushort GetGlyphId(ushort charCode)` — cmap lookup; returns 0 if absent. When the program has **no** cmap it falls back to "charCode is the GID" (a rendering heuristic) — so gate cmap use on `GetCmapSubtableCount() > 0`.
- `int GetCmapSubtableCount()` — 0 when there is no cmap table.
- `ushort GetGlyphIdByName(string glyphName)` — CFF charset / Type1 name→GID; 0 if the name is not in the program.
- `ushort NumGlyphs { get; }`, `bool IsCffFont`, `bool CffHasEmbeddedCharset`.

**`PdfFont` members used:** `font.Encoding?.GetGlyphName(int code)` (honours `/Differences`), `font.FontType == PdfFontType.TrueType`, `font.FirstChar`. `GlyphList.GetUnicode(string glyphName)` maps an AGL name to its Unicode string (null if unknown).

**Corpus fail files this slice targets** (names under `veraPDF test suite …`):
- `.notdef` (6.2.11.8): `6-2-11-8-t01-fail-a/b/c/d`, `6-2-11-4-1-t02-fail-c/d/e`, `6-1-13-t10-fail-a`. Several also fail 6.2.11.5 (they may already agree via the width check — the net gain is measured, not assumed).
- glyph-present (6.2.11.4.1 t2): `6-2-11-4-1-t02-fail-a/b/d/e`, `6-1-13-t10-fail-a`.
- UA-1 7.21.8: `7.21.8-t01-fail-a`. **UA-1 7.21.4.1 t2: 0 corpus files** — wire the clause mapping for completeness/Matterhorn, expect no UA agreement change from it.

---

## Task 1: Tri-state resolver + simple-font `.notdef` (6.2.11.8 / 7.21.8)

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/FontProgramRule.cs`
- Test: `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs`

**Interfaces:**
- Produces (private, consumed by Task 2):
  - `private enum SimpleGlyphResolution { Present, NotDef, Unknown }`
  - `private static SimpleGlyphResolution ResolveSimpleGlyph(PdfFont font, EmbeddedFontMetrics metrics, int code, bool isTrueType)`
- Consumes: existing `Make`, `Name`, `GlyphList.GetUnicode`, the `EmbeddedFontMetrics` members above.

- [ ] **Step 1: Write the failing tests**

Add to `PreflightSlice19Tests.cs`. The first test *characterizes the fixture* so the "absent glyph" choice is a checked invariant, not an assumption; the rest pin behavior. Insert after the existing `.notdef` region (after `Notdef_finding_is_profile_aware_under_pdfua1`, ~line 209).

```csharp
    // ── simple-font .notdef / glyph-present (slice 1) ─────────────────────────────────────────────────

    // A WinAnsi code remapped via /Differences to a glyph whose Unicode PublicPixel lacks, so the
    // program resolves it to no glyph. afii10017 = Cyrillic Capital A (U+0410); a Latin pixel font has no
    // such glyph. Guarded by Fixture_font_lacks_cyrillic so a font change can't silently invalidate it.
    private const int AbsentUnicode = 0x0410;
    private const string AbsentGlyphName = "afii10017";

    private static PdfDocument TrueTypeDocShowingAbsentGlyph()
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(32), // nonsymbolic
            [N("FontFile2")] = Ref(3),
        };
        var encoding = new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("BaseEncoding")] = N("WinAnsiEncoding"),
            [N("Differences")] = new PdfArray(new PdfInteger('A'), N(AbsentGlyphName)),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("FirstChar")] = new PdfInteger('A'),
            [N("LastChar")] = new PdfInteger('A'),
            [N("Widths")] = new PdfArray(new PdfInteger(ProgramWidth)),
            [N("Encoding")] = Ref(4),
            [N("FontDescriptor")] = Ref(2),
        };
        return DocWith(font, Encoding.ASCII.GetBytes("(A)"), (2, descriptor), (3, FontFile()), (4, encoding));
    }

    [Fact]
    public void Fixture_font_lacks_cyrillic()
    {
        // Precondition for the absent-glyph tests: PublicPixel has no glyph for U+0410.
        var metrics = new PdfLibrary.Fonts.Embedded.EmbeddedFontMetrics(FontBytes());
        Assert.True(metrics.IsValid);
        Assert.Equal(0, metrics.GetGlyphId((ushort)AbsentUnicode));
    }

    [Fact]
    public void Simple_truetype_absent_glyph_fails_notdef()
    {
        Finding f = Assert.Single(Run(TrueTypeDocShowingAbsentGlyph()));
        Assert.Equal("6.2.11.8", Clause(f));
        Assert.Contains(".notdef", f.Message);
    }

    [Fact]
    public void Simple_truetype_absent_glyph_notdef_is_profile_aware()
    {
        Finding f = Assert.Single(Run(TrueTypeDocShowingAbsentGlyph(), ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.8", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    [Fact]
    public void Simple_truetype_present_glyph_is_clean()
    {
        // 'A' with the default WinAnsi mapping resolves to a real PublicPixel glyph — no finding.
        Assert.Empty(Run(TrueTypeDoc(ProgramWidth)));
    }
```

Note: `Run` currently invokes `new FontProgramRule().Check(...)`. `EmbeddedFontMetrics` has a public `(byte[])` constructor used elsewhere in the tests — if the exact ctor differs, mirror the one `Fonts/Embedded/EmbeddedFontIntegrationTests.cs` uses.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice19 -c Debug`
Expected: `Fixture_font_lacks_cyrillic` PASSES (precondition holds); `Simple_truetype_absent_glyph_fails_notdef` and `..._is_profile_aware` FAIL (`Assert.Single` gets 0 findings — the simple-font `.notdef` path does not exist yet); `Simple_truetype_present_glyph_is_clean` PASSES.

If `Fixture_font_lacks_cyrillic` FAILS (PublicPixel unexpectedly has U+0410), pick another absent codepoint the font lacks (e.g. U+3042 Hiragana `/afii...` or U+05D0 Hebrew) and update both constants. This is the documented contingency, not a placeholder.

- [ ] **Step 3: Add the tri-state resolver and wire simple `.notdef`**

In `FontProgramRule.cs`, add the enum + resolver as private members (place after `SimpleCffAdvance`):

```csharp
    /// <summary>Confidence-tagged resolution of a simple-font code to its program glyph.</summary>
    private enum SimpleGlyphResolution { Present, NotDef, Unknown }

    /// <summary>
    /// Resolves a simple-font code to a program glyph with a confidence flag, the FP-safe way. Returns
    /// <see cref="SimpleGlyphResolution.Unknown"/> whenever the standard code→glyph path is not reproducible
    /// here (symbolic TrueType with no usable Unicode cmap, an encoding name with no Unicode, a
    /// predefined-charset CFF) so the caller emits nothing. Only a confident glyph-0 result is
    /// <see cref="SimpleGlyphResolution.NotDef"/>.
    /// </summary>
    private static SimpleGlyphResolution ResolveSimpleGlyph(
        PdfFont font, EmbeddedFontMetrics metrics, int code, bool isTrueType)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);

        if (isTrueType)
        {
            // Trustworthy only through a real cmap keyed by the encoding name's Unicode value. Without a
            // cmap (GetGlyphId would fall back to "code is the GID", a rendering heuristic) or without an
            // AGL Unicode for the name (symbolic / custom name), we cannot tell absence from a lookup gap.
            string? unicode = glyphName is null ? null : GlyphList.GetUnicode(glyphName);
            if (string.IsNullOrEmpty(unicode) || metrics.GetCmapSubtableCount() == 0)
                return SimpleGlyphResolution.Unknown;
            ushort gid = metrics.GetGlyphId((ushort)char.ConvertToUtf32(unicode, 0));
            return gid == 0 ? SimpleGlyphResolution.NotDef : SimpleGlyphResolution.Present;
        }

        // Simple CFF / Type1: code → name → charset GID. Gated (by the caller) on an embedded charset, so a
        // name absent from the charset is a genuine miss, not the predefined-charset parser bug.
        if (string.IsNullOrEmpty(glyphName))
            return SimpleGlyphResolution.Unknown;
        return metrics.GetGlyphIdByName(glyphName) == 0
            ? SimpleGlyphResolution.NotDef
            : SimpleGlyphResolution.Present;
    }
```

Then extend `CheckSimple` to emit `.notdef` before the width block. Replace the current `CheckSimple` early-return/width structure so glyph resolution runs even when `/Widths` is absent. The method becomes:

```csharp
    private IEnumerable<Finding> CheckSimple(
        ConformanceContext context, PdfFont font, EmbeddedFontMetrics metrics,
        IReadOnlyCollection<int> codes, HashSet<string> metricsReported, HashSet<string> notdefReported)
    {
        bool isTrueType = font.FontType == PdfFontType.TrueType;
        bool isSimpleCff = metrics.IsCffFont && metrics.CffHasEmbeddedCharset;
        if (!isTrueType && !isSimpleCff)
            yield break; // classic Type1 (FontFile) / Type3 / predefined-charset CFF → out of scope (FP-safe)

        // .notdef (6.2.11.8 / 7.21.8): a shown code that confidently resolves to glyph 0.
        bool notdefHit = false;
        foreach (int code in codes)
        {
            if (ResolveSimpleGlyph(font, metrics, code, isTrueType) == SimpleGlyphResolution.NotDef)
            {
                notdefHit = true;
                break;
            }
        }
        if (notdefHit && notdefReported.Add(font.BaseFont))
            yield return Make(context, font, "8",
                $"The {(isTrueType ? "TrueType" : "CFF")} font {Name(font)} renders a character code that maps "
                + "to the .notdef glyph (glyph 0), which is not present in the embedded font program.");

        // metrics (6.2.11.5 / 7.21.5): unchanged — only runs when /Widths is present.
        if (context.Resolve(font.FontDictionary.Get("Widths")) is not PdfArray widths)
            yield break;

        double worstDiff = 0;
        foreach (int code in codes)
        {
            int index = code - font.FirstChar;
            if (index < 0 || index >= widths.Count)
                continue;

            double? program = isTrueType
                ? TrueTypeAdvance(font, metrics, code)
                : SimpleCffAdvance(font, metrics, code);
            if (program is null)
                continue;

            double declared = widths[index].ToDouble();
            worstDiff = Math.Max(worstDiff, Math.Abs(declared - program.Value));
        }

        if (worstDiff > WidthTolerance && metricsReported.Add(font.BaseFont))
            yield return Make(context, font, "5",
                $"The {(isTrueType ? "TrueType" : "CFF")} font {Name(font)} declares a glyph width that differs "
                + $"from the embedded font program's advance width by {worstDiff:F0} units "
                + $"(tolerance {WidthTolerance:F0}).");
    }
```

Update the single `CheckSimple` call site in `Check` to pass the new `notdefReported` set (already constructed at the top of `Check` — reuse it, matching `CheckType0`'s signature):

```csharp
            foreach (Finding f in font is Type0Font type0
                         ? CheckType0(context, type0, metrics, usage.Codes, notdefReported, metricsReported)
                         : CheckSimple(context, font, metrics, usage.Codes, metricsReported, notdefReported))
            {
                yield return f;
            }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice19 -c Debug`
Expected: all `PreflightSlice19` tests PASS (the new `.notdef` tests plus every pre-existing one — confirm `Non_embedded_truetype_is_not_reported`, `Type0_non_identity_cmap_is_skipped`, and the width tests still pass).

- [ ] **Step 5: Commit**

```bash
cd /Users/michaeljordan/RiderProjects/PdfLibrary
git checkout -b feat/font-program-slice1-glyph-resolver
git add PdfLibrary/Conformance/Rules/FontProgramRule.cs PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs
git commit -m "feat(conformance): simple-font .notdef via tri-state glyph resolver (6.2.11.8)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"
```

---

## Task 2: Glyph-present (6.2.11.4.1 t2 / 7.21.4.1 t2) for simple fonts

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/FontProgramRule.cs`
- Test: `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs`

**Interfaces:**
- Consumes: `SimpleGlyphResolution`, `ResolveSimpleGlyph` (Task 1); `Make`, `Name`.
- Produces: nothing new consumed later.

**Design note:** veraPDF distinguishes `.notdef` (6.2.11.8 — glyph named `.notdef`) from glyph-present (6.2.11.4.1 t2 — the referenced glyph is absent from the program). For simple fonts both manifest as "the code resolves to no real glyph"; emitting *both* clauses on the same offending code is correct (veraPDF flags both, and detecting either makes the file agree). To keep the checks independent and each separately reviewable, emit 6.2.11.4.1 t2 from the same `NotDef` resolution, deduplicated on its own set. **Do not** emit it for Type0 here — Type0 glyph-present is a later slice (CIDToGIDMap→out-of-range), out of scope now.

- [ ] **Step 1: Write the failing test**

Add to `PreflightSlice19Tests.cs`, after the Task-1 tests:

```csharp
    [Fact]
    public void Simple_truetype_absent_glyph_also_fails_embedding_glyph_present()
    {
        // The same absent glyph violates 6.2.11.4.1 test 2 (embedded font must define all rendered glyphs).
        Finding[] fs = Run(TrueTypeDocShowingAbsentGlyph());
        Assert.Contains(fs, f => Clause(f) == "6.2.11.4.1");
        Assert.Contains(fs, f => Clause(f) == "6.2.11.8");
    }

    [Fact]
    public void Simple_present_glyph_emits_no_glyph_present_finding()
    {
        Assert.DoesNotContain(Run(TrueTypeDoc(ProgramWidth)), f => Clause(f) == "6.2.11.4.1");
    }
```

Update `Simple_truetype_absent_glyph_fails_notdef` (Task 1) — it used `Assert.Single`, which now breaks because two findings are emitted. Change it to assert on the `.notdef` finding specifically:

```csharp
    [Fact]
    public void Simple_truetype_absent_glyph_fails_notdef()
    {
        Finding f = Assert.Single(Run(TrueTypeDocShowingAbsentGlyph()), x => Clause(x) == "6.2.11.8");
        Assert.Contains(".notdef", f.Message);
    }
```

(Also update `Simple_truetype_absent_glyph_notdef_is_profile_aware` the same way: `Assert.Single(Run(..., PdfUA1), x => Clause(x) == "7.21.8")`.)

- [ ] **Step 2: Run to verify the new test fails**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice19 -c Debug`
Expected: `Simple_truetype_absent_glyph_also_fails_embedding_glyph_present` FAILS (no 6.2.11.4.1 finding yet); the adjusted `.notdef` tests PASS; `Simple_present_glyph_emits_no_glyph_present_finding` PASSES.

- [ ] **Step 3: Emit 6.2.11.4.1 t2 from the same resolution**

In `CheckSimple`, extend the `.notdef` block to also emit glyph-present. Add a `presentReported` set threaded from `Check` (construct `var presentReported = new HashSet<string>(StringComparer.Ordinal);` at the top of `Check` and pass it in). Replace the `.notdef` emission block:

```csharp
        bool notdefHit = false;
        foreach (int code in codes)
        {
            if (ResolveSimpleGlyph(font, metrics, code, isTrueType) == SimpleGlyphResolution.NotDef)
            {
                notdefHit = true;
                break;
            }
        }
        if (notdefHit)
        {
            string kind = isTrueType ? "TrueType" : "CFF";
            if (notdefReported.Add(font.BaseFont))
                yield return Make(context, font, "8",
                    $"The {kind} font {Name(font)} renders a character code that maps to the .notdef glyph "
                    + "(glyph 0), which is not present in the embedded font program.");
            if (presentReported.Add(font.BaseFont))
                yield return Make(context, font, "4.1",
                    $"The {kind} font {Name(font)} renders a glyph that is not present in the embedded font "
                    + "program.");
        }
```

Update the `Check` dispatch call to pass `presentReported` to `CheckSimple` and adjust the method signature accordingly.

- [ ] **Step 4: Run to verify all pass**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice19 -c Debug`
Expected: all `PreflightSlice19` tests PASS.

- [ ] **Step 5: Run the full conformance test group to catch collateral**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~Conformance -c Debug`
Expected: PASS (no regression in sibling conformance tests). Note the count for comparison in Task 3.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Conformance/Rules/FontProgramRule.cs PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs
git commit -m "feat(conformance): simple-font glyph-present check (6.2.11.4.1 t2)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"
```

---

## Task 3: Corpus acceptance gate — clause closure + 0 false positives

This is the real oracle. The unit tests pin behavior; the corpus decides breadth and FP. **No floor may move until FP == 0 here.**

**Files:** none modified in this task (verification + possible resolver tightening only).

- [ ] **Step 1: Regenerate the parity report**

Run:
```bash
SCRATCH=/private/tmp/claude-501/-Users-michaeljordan-RiderProjects-Pellucid/922bccd6-d2dc-4acd-bb50-d014106958be/scratchpad/parity-slice1.md
PARITY_REPORT="$SCRATCH" dotnet test PdfLibrary.Tests \
  --filter FullyQualifiedName~Generate_parity_report -c Release
```
Expected: PASS; the report is written to `$SCRATCH`.

- [ ] **Step 2: Verify 0 false positives**

The report lists, per profile, files where PdfLibrary reports a failure veraPDF does **not** (false positives). Inspect:
```bash
grep -nA40 -i "false positive\|falsePositive\|FP" "$SCRATCH" | head -80
```
Expected: **zero** false-positive files for every profile. If any appear:
1. Identify the offending file and the clause (it will be `6.2.11.8` or `6.2.11.4.1`).
2. That file is a *conformant* font the resolver mis-read as absent — a resolution gap masquerading as absence. Tighten `ResolveSimpleGlyph` to return `Unknown` for that shape (e.g. require the nonsymbolic flag, or a specific cmap `(3,1)` subtable, or exclude the encoding form involved) — never relax the FP guard for a detection.
3. Re-run Step 1–2. Repeat until FP == 0. Partial closure is acceptable; a false positive is not.

- [ ] **Step 3: Record clause closure (net agreement gain)**

From the report, note the new agreement counts vs the floors (A-2b 935, UA-1 281) and which of the target files flipped:
```bash
grep -nE "PdfA2b|PdfA2u|PdfA3b|PdfUA1" "$SCRATCH" | grep -i "agree" | head
```
Expected: A-2b agreement ≥ 935 (some of the `.notdef`/glyph-present files flip here; the rest flip in the widths slice because they also fail 6.2.11.5). UA-1 ≥ 281 (the single `7.21.8-t01-fail-a` may already be covered by the Type0 path; if the UA count is unchanged that is fine). Record the exact new numbers for Task 4's floor values.

- [ ] **Step 4: Run the parity floor + full suite**

Run:
```bash
dotnet test PdfLibrary.Tests --filter FullyQualifiedName~ParityReport -c Release
dotnet test PdfLibrary.Tests -c Release
```
Expected: `ParityReport` agreement test PASSES against the *current* floors (it should, since agreement only rose); full suite green (≈ 2,239 passing, +the new unit tests, 0 failures).

---

## Task 4: Ratchet the floor, update Matterhorn, merge

**Files:**
- Modify: `PdfLibrary.Tests/Conformance/ParityReportTests.cs`
- Modify: `Docs/pdfua/matterhorn-coverage.md`

- [ ] **Step 1: Raise the A-2b (and, if it moved, UA-1) agreement floor**

In `ParityReportTests.cs`, set `[ConformanceProfile.PdfA2b]` to the exact new agreement from Task 3 Step 3, and append a note to its comment. Example (use the measured number, not this literal):

```csharp
            [ConformanceProfile.PdfA2b] = <measured>,   // …existing note… + simple-font .notdef/glyph-present (6.2.11.8, 6.2.11.4.1 t2) via tri-state resolver, 0 FP
```
If UA-1 rose, update `[ConformanceProfile.PdfUA1]` likewise; if it did not, leave it at 281.

- [ ] **Step 2: Verify the floor holds**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~ParityReport -c Release`
Expected: PASS at the new floor.

- [ ] **Step 3: Update the Matterhorn coverage matrix**

In `Docs/pdfua/matterhorn-coverage.md`, mark the 31-xxx conditions this capability now covers. The simple-font glyph resolver closes the **.notdef reference** and **glyph-present** conditions among 31-025/026/027 (verify each row's exact text against the matrix before ticking — only mark a condition whose check is genuinely now machine-verified; leave the cmap-WMode / ToUnicode / CharSet rows for their own slices). Add a one-line dated note referencing this slice.

- [ ] **Step 4: Commit, merge, push**

```bash
git add PdfLibrary.Tests/Conformance/ParityReportTests.cs Docs/pdfua/matterhorn-coverage.md
git commit -m "test(conformance): ratchet A-2b agreement floor; Matterhorn 31-xxx notes (font slice 1)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"

git checkout master
git merge --no-ff feat/font-program-slice1-glyph-resolver -m "Merge feat/font-program-slice1-glyph-resolver: simple-font .notdef + glyph-present (6.2.11.8 / 6.2.11.4.1 t2)"
git push origin master
```
Expected: fast, clean merge; push succeeds. Confirm the untracked `Docs/plans/2026-07-10-*.md` were never staged (`git status` still shows them as `??`).

---

## Self-review

**Spec coverage** (against `2026-07-18-font-program-conformance-design.md`, Slice 1):
- Tri-state resolver (`Present`/`NotDef`/`Unknown`, `Unknown→skip`) — Task 1 Step 3. ✓
- `.notdef` for simple fonts (6.2.11.8 / 7.21.8) — Task 1. ✓
- glyph-present (6.2.11.4.1 t2 / 7.21.4.1 t2) — Task 2. ✓ (UA t2 has 0 corpus files — documented, wired via the shared profile mapping.)
- CFF gated on embedded charset; predefined-charset CFF → Unknown — Task 1 Step 3 (the `isSimpleCff` gate + resolver comment). ✓
- 0-FP corpus gate before floor bump — Task 3. ✓
- Matterhorn matrix updated per slice — Task 4 Step 3. ✓
- Per-slice flow (RED→GREEN→regen→verify→floor→matrix→merge→push) — Tasks 1–4. ✓
- Never `git add` `Docs/plans/*.md` — Global Constraints + Task 4 Step 4. ✓

**Placeholder scan:** No `TBD`/`TODO`/"add error handling". The only measured-at-runtime value is the new floor number (Task 4 Step 1) — that is data the corpus produces, with the exact source command (Task 3 Step 3), not a placeholder. The absent-glyph fixture has a concrete value (U+0410 / `afii10017`) plus a self-checking precondition test and a named contingency.

**Type consistency:** `SimpleGlyphResolution` (enum) and `ResolveSimpleGlyph(PdfFont, EmbeddedFontMetrics, int, bool)` are named identically across Tasks 1–2. `Make(context, font, sub, message)` sub strings `"8"`, `"4.1"`, `"5"` map through the existing `ConformanceClauses.For` to `6.2.11.8` / `6.2.11.4.1` / `6.2.11.5` (A-2) and `7.21.*` (UA-1), matching `Clause(f)` assertions. `CheckSimple` signature gains `notdefReported` and `presentReported` (both `HashSet<string>`), constructed once in `Check` and shared, consistent with the existing `metricsReported`/`notdefReported` pattern used by `CheckType0`.
