# Font-program Slice 3 — CMap stream (WMode + UseCMap) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `FontDictionaryRule` to close ISO 19005-2 6.2.11.3.3 / ISO 14289-1 7.21.3.3 tests **2** (an embedded CMap's `/WMode` dictionary entry must equal the `/WMode` in the CMap stream body) and **3** (a CMap must not reference a non-predefined CMap via `/UseCMap`).

**Architecture:** `FontDictionaryRule.CheckType0` already traverses each Type0 font's `/Encoding`, detects the embedded-CMap-stream case, and owns the `PredefinedCMaps` set. This slice adds two checks in that embedded-stream branch: a minimal `/WMode <n> def` scan of the decoded CMap body (test 2), and a `/UseCMap` dictionary-entry resolution whose referenced CMap name must be predefined (test 3). No general CMap parser — just these two facts. FP-safe: any stream that can't be decoded, or a `/WMode`/`/UseCMap` that can't be resolved, is skipped.

**Tech Stack:** C# (.NET 10), xUnit v3. Engine repo `PdfLibrary` (master `cc8b163` after slice 2 + the PDF/A-3 authoring work). Rule at `PdfLibrary/Conformance/Rules/FontDictionaryRule.cs`; tests at `PdfLibrary.Tests/Conformance/PreflightSlice18Tests.cs`.

## Global Constraints

- **0 false positives, corpus-wide** — verified via the regenerated parity report before any floor bump. Test 2 fires ONLY when BOTH the dict `/WMode` and a body `/WMode` are present and differ; test 3 ONLY when a `/UseCMap` resolves to a CMap whose name is not predefined. Anything unresolvable → skip, never guess.
- Reuse the existing `PredefinedCMaps` set (Table 118 names) in `FontDictionaryRule` — do not duplicate it.
- Profile-aware via `Make(context, font, "3.3", …)` → `6.2.11.3.3` (A-2) / `7.21.3.3` (UA-1). PDF/X-4 excluded (rule's `AppliesToProfiles` already gates it).
- `PdfObject` is in namespace `PdfLibrary.Core`. Never `git add` the untracked `Docs/plans/2026-07-10-*.md`. Slice branch off master; **fetch + rebase onto origin/master before pushing** (origin advances from other machines — slice 2 hit this); merge `--no-ff`; push at end.
- Reference floor: A-2b agreement **938**, UA-1 **281** in `ParityReportTests.cs`. Suite ~2294.

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `PdfLibrary/Conformance/Rules/FontDictionaryRule.cs` | t2 (WMode) + t3 (UseCMap) checks + two helpers + regex | Modify |
| `PdfLibrary.Tests/Conformance/PreflightSlice18Tests.cs` | Unit tests (synthetic Type0 + embedded CMap) | Modify |
| `PdfLibrary.Tests/Conformance/ParityReportTests.cs` | A-2b + UA-1 floor ratchet | Modify (Task 3) |
| `PdfLibrary/Docs/pdfua/matterhorn-coverage.md` | Tick 31-007 / 31-008 | Modify (Task 3) |

## Background the implementer needs

**Ground truth (corpus probe, 2026-07-19):** the not-yet-agreed gap files (each fails ONLY the CMap test named):
- test 2 (WMode): A-2b `6-2-11-3-3-t02-fail-a/b`; UA-1 `7.21.3.3-t02-fail-a/b`. Their embedded CMap has a `/WMode` dict entry that differs from the `/WMode <n> def` in the stream body (e.g. dict `/WMode 1`, body `/WMode 0 def`).
- test 3 (UseCMap): A-2b `6-2-11-3-3-t03-fail-a/b`. Their embedded CMap dict has `/UseCMap <ref>` → another embedded CMap stream whose `/CMapName` (e.g. `/UMBSME+AdobeGothicStd-Bold+0`) is NOT predefined. (UA `7.21.3.3-t03-fail-a` also fails test 1, which is already detected, so it is already agreed — no UA test-3 gain.)
- Expected agreement: A-2b **938 → 942** (+4), UA-1 **281 → 283** (+2). Measured per regen.

**Existing `FontDictionaryRule.CheckType0` (the method you extend):** after the Group-4 test-1 block (which fires only when `/Encoding` is NOT a stream) and before `if (cidFont is null) yield break;`, insert the new embedded-stream checks. The existing embedded-CMap CIDSystemInfo block already pattern-matches `encoding is PdfStream cmap` — your new block uses the same `encoding is PdfStream …` shape. `Make(context, offender, sub, message)`, `BaseFont(font)`, and the `PredefinedCMaps` `HashSet<string>` are all present in the file.

**Test builders in `PreflightSlice18Tests.cs` (reuse):** `DocWithFont(PdfDictionary font)`, `Type0Font(PdfObject encoding, PdfDictionary cidFont)`, `CidFont(string subtype, …)`, `Run(doc, profile)` → `new FontDictionaryRule().Check(...)`, `Clause(f)` → `ParitySnapshot.ClauseKey`. A `PdfStream` is `new(PdfDictionary dict, byte[] body)`; with no `/Filter`, `GetDecodedData` returns the body verbatim.

**Usings:** `FontDictionaryRule.cs` already has `using System.Text.RegularExpressions;`. Add `using System.Text;` (for `Encoding.Latin1`). `PdfStream`/`PdfName`/`PdfInteger` are already in scope via `PdfLibrary.Core.Primitives`.

---

## Task 1: WMode (test 2) + UseCMap (test 3) checks

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/FontDictionaryRule.cs`
- Test: `PdfLibrary.Tests/Conformance/PreflightSlice18Tests.cs`

**Interfaces:**
- Produces (private): `ReadCMapBodyWMode(ConformanceContext, PdfStream)`, `ReferencedCMapName(ConformanceContext, PdfObject)`, `CMapWMode` (compiled Regex).
- Consumes: `Make`, `BaseFont`, `PredefinedCMaps`.

- [ ] **Step 1: Write the failing tests**

Add to `PreflightSlice18Tests.cs` (after the existing Group-4 test-1 tests). Add two builders + six tests:

```csharp
    // ── Group 4 tests 2 & 3 — CMap WMode / UseCMap (6.2.11.3.3 / 7.21.3.3) — slice 3 ──────────────────

    // An embedded CMap stream: dict /WMode = dictWMode; body contains "/WMode <bodyWMode> def".
    private static PdfStream CMapWithWMode(int dictWMode, int bodyWMode)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName("Custom-CMap"),
            [new PdfName("WMode")] = new PdfInteger(dictWMode),
        };
        return new PdfStream(dict, Encoding.ASCII.GetBytes($"begincmap /WMode {bodyWMode} def endcmap"));
    }

    // An embedded CMap stream whose /UseCMap references another CMap (a stream named referencedName,
    // or a predefined name when referencedName matches one).
    private static PdfStream CMapUsing(PdfObject useCMap)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName("Custom-CMap"),
            [new PdfName("UseCMap")] = useCMap,
        };
        return new PdfStream(dict, Encoding.ASCII.GetBytes("begincmap endcmap"));
    }

    private static PdfStream NamedCMap(string cmapName) =>
        new(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("CMap"),
            [new PdfName("CMapName")] = new PdfName(cmapName),
        }, Encoding.ASCII.GetBytes("begincmap endcmap"));

    [Fact]
    public void Type0_cmap_wmode_mismatch_fails()
    {
        PdfDictionary font = Type0Font(CMapWithWMode(dictWMode: 1, bodyWMode: 0), CidFont("CIDFontType0"));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.3", Clause(f));
        Assert.Contains("WMode", f.Message);
    }

    [Fact]
    public void Type0_cmap_wmode_consistent_passes()
    {
        PdfDictionary font = Type0Font(CMapWithWMode(dictWMode: 0, bodyWMode: 0), CidFont("CIDFontType0"));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_cmap_wmode_mismatch_is_profile_aware()
    {
        PdfDictionary font = Type0Font(CMapWithWMode(dictWMode: 1, bodyWMode: 0), CidFont("CIDFontType0"));
        Finding f = Assert.Single(Run(DocWithFont(font), ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.3.3", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    [Fact]
    public void Type0_cmap_uses_nonpredefined_cmap_fails()
    {
        PdfDictionary font = Type0Font(CMapUsing(NamedCMap("Custom-Other")), CidFont("CIDFontType0"));
        Finding f = Assert.Single(Run(DocWithFont(font)));
        Assert.Equal("6.2.11.3.3", Clause(f));
        Assert.Contains("UseCMap", f.Message);
    }

    [Fact]
    public void Type0_cmap_uses_predefined_cmap_passes()
    {
        // /UseCMap /Identity-H (a predefined name) is allowed.
        PdfDictionary font = Type0Font(CMapUsing(new PdfName("Identity-H")), CidFont("CIDFontType0"));
        Assert.Empty(Run(DocWithFont(font)));
    }

    [Fact]
    public void Type0_cmap_without_wmode_or_usecmap_is_clean()
    {
        // A bare embedded CMap (no dict /WMode, no /UseCMap) triggers neither check.
        var cmap = new PdfStream(new PdfDictionary { [new PdfName("Type")] = new PdfName("CMap") },
            Encoding.ASCII.GetBytes("begincmap endcmap"));
        Assert.Empty(Run(DocWithFont(Type0Font(cmap, CidFont("CIDFontType0")))));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice18 -c Debug`
Expected: `Type0_cmap_wmode_mismatch_fails`, `..._is_profile_aware`, and `Type0_cmap_uses_nonpredefined_cmap_fails` FAIL (0 findings — the checks don't exist). The three "passes/clean" tests already pass (nothing fires yet). All pre-existing PreflightSlice18 tests still pass.

- [ ] **Step 3: Implement the two checks + helpers**

In `FontDictionaryRule.cs`, add `using System.Text;` at the top. Add the regex field near the other static regexes:

```csharp
    // The integer /WMode declared in a CMap stream body: "/WMode <n> def".
    private static readonly Regex CMapWMode = new(@"/WMode\s+(-?\d+)\s+def", RegexOptions.Compiled);
```

In `CheckType0`, insert this block immediately after the Group-4 test-1 block (the `if (encoding is not PdfStream)` block) and before `if (cidFont is null) yield break;`:

```csharp
        // Group 4 tests 2 & 3 (6.2.11.3.3 / 7.21.3.3) — embedded CMap stream consistency. Independent of the
        // descendant CIDFont, so checked here before the cidFont guard.
        if (encoding is PdfStream encodingCMap)
        {
            // test 2 — the /WMode dictionary entry must equal the /WMode in the CMap stream body.
            if (context.Resolve(encodingCMap.Dictionary.Get("WMode")) is PdfInteger dictWMode
                && ReadCMapBodyWMode(context, encodingCMap) is { } bodyWMode
                && dictWMode.LongValue != bodyWMode)
            {
                yield return Make(context, font, "3.3",
                    $"The Type0 font {BaseFont(font)} embeds a CMap whose /WMode dictionary entry "
                    + $"({dictWMode.LongValue}) differs from the /WMode in the CMap stream ({bodyWMode}).");
            }

            // test 3 — a CMap must not reference a non-predefined CMap (via /UseCMap).
            if (encodingCMap.Dictionary.Get("UseCMap") is { } useCMapRaw
                && ReferencedCMapName(context, useCMapRaw) is { } referencedName
                && !PredefinedCMaps.Contains(referencedName))
            {
                yield return Make(context, font, "3.3",
                    $"The Type0 font {BaseFont(font)} embeds a CMap that references non-predefined CMap "
                    + $"/{referencedName} via /UseCMap.");
            }
        }
```

Add the two helpers near `StringValue`:

```csharp
    /// <summary>The integer /WMode declared in the CMap stream body ("/WMode &lt;n&gt; def"), or null when
    /// absent or the stream cannot be decoded. FP-safe: the caller compares it only when the dictionary also
    /// carries a /WMode.</summary>
    private static long? ReadCMapBodyWMode(ConformanceContext context, PdfStream cmap)
    {
        byte[] data;
        try { data = cmap.GetDecodedData(context.Document.Decryptor); }
        catch { return null; }

        Match m = CMapWMode.Match(Encoding.Latin1.GetString(data));
        return m.Success && long.TryParse(m.Groups[1].Value, out long w) ? w : null;
    }

    /// <summary>The name of the CMap referenced by a /UseCMap value: a predefined-name reference directly, or
    /// the /CMapName of an embedded CMap stream. Null when it cannot be resolved to a name.</summary>
    private static string? ReferencedCMapName(ConformanceContext context, PdfObject useCMap) =>
        context.Resolve(useCMap) switch
        {
            PdfName name => name.Value,
            PdfStream stream => (context.Resolve(stream.Dictionary.Get("CMapName")) as PdfName)?.Value,
            _ => null,
        };
```

Update the class-doc list item 4 (currently "6.2.11.3.3 / 7.21.3.3, test 1 only") to say tests 1–3, and remove the "CMap WMode/usecmap ... a later slice and out of scope" note in the doc header.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PreflightSlice18 -c Debug`
Expected: all `PreflightSlice18` tests PASS (the six new ones plus every pre-existing one — in particular the existing 3.3-test-1 and 3.1 CIDSystemInfo tests must be unaffected).

- [ ] **Step 5: Run the full Conformance group**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~Conformance -c Debug`
Expected: PASS. Note the count for Task 2.

- [ ] **Step 6: Commit**

```bash
cd /Users/michaeljordan/RiderProjects/PdfLibrary
git checkout -b feat/font-program-slice3-cmap-stream
git add PdfLibrary/Conformance/Rules/FontDictionaryRule.cs PdfLibrary.Tests/Conformance/PreflightSlice18Tests.cs
git commit -m "feat(conformance): CMap WMode + UseCMap consistency (6.2.11.3.3 t2/t3)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"
```

---

## Task 2: Corpus acceptance gate — clause closure + 0 false positives

**Files:** none modified (verification only).

- [ ] **Step 1: Regenerate the parity report**

Run:
```bash
SCRATCH=/private/tmp/claude-501/-Users-michaeljordan-RiderProjects-Pellucid/922bccd6-d2dc-4acd-bb50-d014106958be/scratchpad/parity-slice3.md
PARITY_REPORT="$SCRATCH" dotnet test PdfLibrary.Tests \
  --filter FullyQualifiedName~Generate_parity_report -c Release
```
Expected: PASS; report at `$SCRATCH`.

- [ ] **Step 2: Verify 0 FP and the clause closed**

```bash
grep -nE "false positives" "$SCRATCH" | head -1
sed -n '/## Verdict agreement/,/## Clause coverage/p' "$SCRATCH" | grep -E "A-2b|UA-1"
grep -nE "6\.2\.11\.3\.3 |7\.21\.3\.3 " "$SCRATCH"
```
Expected: **0 false positives** across all 1316 files; PDF/A-2b agreement **942** (t02-a/b + t03-a/b flip), PDF/UA-1 **283** (t02-a/b flip); 6.2.11.3.3 clause coverage rises from 1/5 toward 5/5. If any false positive appears, identify the conformant file, tighten the WMode/UseCMap check to skip that shape, and re-run. FP == 0 is mandatory before the floor bump.

- [ ] **Step 3: Parity floor gate + full suite**

Run:
```bash
dotnet test PdfLibrary.Tests --filter FullyQualifiedName~ParityReport -c Release
dotnet test PdfLibrary.Tests -c Release
```
Expected: `ParityReport` passes at the current floor (agreement rose); full suite green (≈ 2294 + 6 new tests, 0 failures).

---

## Task 3: Ratchet floors, tick Matterhorn, merge, push

**Files:** Modify `ParityReportTests.cs`, `Docs/pdfua/matterhorn-coverage.md`.

- [ ] **Step 1: Raise the floors to the measured values**

In `ParityReportTests.cs`, set `[ConformanceProfile.PdfA2b]` to the exact new agreement (expected **942**) and `[ConformanceProfile.PdfUA1]` to the new value (expected **283**), appending to each comment, e.g.:
```csharp
            [ConformanceProfile.PdfA2b] = 942,   // …existing… + CMap WMode/UseCMap (6.2.11.3.3 t2/t3, +4), 0 FP (font-program slice 3)
            …
            [ConformanceProfile.PdfUA1] = 283,   // …existing… + CMap WMode (7.21.3.3 t2, +2)
```
Use the measured numbers if they differ.

- [ ] **Step 2: Verify the floors hold**

Run: `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~ParityReport -c Release`
Expected: PASS at the new floors.

- [ ] **Step 3: Tick Matterhorn 31-007 and 31-008**

In `Docs/pdfua/matterhorn-coverage.md`: set **31-007** (7.21.3.3-1, WMode) and **31-008** (7.21.3.3-2, CMap reference) from `—` to `font-dictionary`. Bump the CP 31 summary count (18 → 20) and the header total (62 → 64). Add a dated note. Verify each row's exact text matches (WMode / references-another-CMap) before ticking.

- [ ] **Step 4: Commit, sync, merge, push**

```bash
git add PdfLibrary.Tests/Conformance/ParityReportTests.cs Docs/pdfua/matterhorn-coverage.md
git commit -m "test(conformance): ratchet floors (A-2b 942, UA-1 283); Matterhorn 31-007/008 (CMap slice)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016jS9peQ4oGzDzdbTLYF18P"

git fetch origin
git checkout master
git merge --no-ff feat/font-program-slice3-cmap-stream -m "Merge feat/font-program-slice3-cmap-stream: CMap WMode + UseCMap consistency (6.2.11.3.3 t2/t3)"
# If origin/master advanced: git rebase --rebase-merges --onto origin/master <old-merge-base> (see slice-2 note), re-run the floor gate, then push.
git push origin master
```
Expected: clean push (or rebase-then-push if origin advanced). Confirm the untracked `Docs/plans/2026-07-10-*.md` were never staged.

---

## Self-review

**Spec coverage** (Slice 3):
- test 2 WMode dict-vs-body — Task 1 (`ReadCMapBodyWMode` + compare). ✓
- test 3 UseCMap non-predefined reference — Task 1 (`ReferencedCMapName` + `PredefinedCMaps`). ✓
- Minimal, no general CMap parser — a `/WMode <n> def` regex + a `/UseCMap` dict resolution. ✓
- FP-safe skips (undecodable stream, absent `/WMode`/`/UseCMap`, unresolvable reference) — both helpers return null; the compare requires both sides present. ✓
- 0-FP corpus gate before floor bump — Task 2. ✓
- Matterhorn 31-007 / 31-008 ticked — Task 3 Step 3. ✓
- Reuse `PredefinedCMaps`, profile-aware `Make(...,"3.3",...)` — Task 1. ✓

**Placeholder scan:** No TBD/TODO. Floor values (942 / 283) are the measured corpus result with the exact source command; the Matterhorn tick is gated on verifying each row's text.

**Type consistency:** `ReadCMapBodyWMode(ConformanceContext, PdfStream) → long?` and `ReferencedCMapName(ConformanceContext, PdfObject) → string?` are named/typed identically across the call site and definitions. `Make(context, font, "3.3", …)` matches the existing 3.3-test-1 emission (same clause key). `CMapWMode` regex captures group 1 = the integer.
