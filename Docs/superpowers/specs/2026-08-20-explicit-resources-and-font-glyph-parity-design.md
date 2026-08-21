# Explicit resources + font glyph parity — design

_2026-08-20. Closes the last 8 PDF/A-2b whole-file misses: clause 6.2.2 test 2 (3 files) and the
6.2.11 font cluster (5 files). Target: **986/986 verdict parity on all four profiles.**_

## Where we start

| Profile | Files | Agreement | Misses | False positives |
|---|--:|--:|--:|--:|
| PDF/A-2b | 986 | 978 (99%) | 8 | 0 |
| PDF/A-2u | 22 | 22 (100%) | 0 | 0 |
| PDF/A-3b | 12 | 12 (100%) | 0 | 0 |
| PDF/UA-1 | 296 | 296 (100%) | 0 | 0 |

Engine `PdfLibrary` master `e37e030`; Pellucid main `93c21fa`; CI run 32432744380 green on
build-windows / build-linux / build-macos / app-tests. Parity gate re-run live at spec time: 5/5 pass
in 3 s.

**The zero-false-positive invariant is standing and non-negotiable.** PdfLibrary is a strict subset
of veraPDF: it must never reject a file veraPDF accepts. No coverage gain is worth trading for it.

## The eight misses

Each row is a *verdict* disagreement — veraPDF says non-compliant, we say conforms.

| File | veraPDF clauses | Job |
|---|---|---|
| `6-2-2-t04-fail-d` | 6.2.2 t2 | A |
| `6-2-2-t04-fail-e` | 6.2.2 t2 | A |
| `6-2-2-t04-fail-f` | 6.2.2 t2 | A |
| `6-2-11-4-1-t02-fail-a` | 6.2.11.4.1 t2, 6.2.11.5 t1 | B |
| `6-2-11-4-1-t02-fail-b` | 6.2.11.4.1 t2, 6.2.11.5 t1 | B |
| `6-2-11-4-1-t02-fail-e` | 6.2.11.8 t1, 6.2.11.5 t1, 6.2.11.4.1 t2 | B |
| `6-2-11-8-t01-fail-a` | 6.2.11.8 t1, 6.2.11.5 t1 | B |
| `6-2-11-8-t01-fail-b` | 6.2.11.8 t1, 6.2.11.5 t1 | B |

### Any ONE clause closes a miss

`PARITY-REPORT.md`'s verdict-leverage table claims none of the font clauses "flips alone". That is a
defect in our own tooling, not a fact about the corpus.

A miss is defined at `ParityLeverage.cs:44` as `!VeraCompliant && PdfLibraryConforms`.
`PdfLibraryConforms` is true only when we emit **no error finding at all**, so flagging *any one* of
that file's clauses flips the verdict. `FlipsAlone` (`ParityLeverage.cs:57`) instead counts only
misses where the clause is the sole missed clause, which models clause-level parity — a different
and stricter goal.

Proof from the committed data, not from reasoning: `6-2-11-8-t01-fail-d` is flagged by veraPDF on
**both** 6.2.11.5 and 6.2.11.8; we flag only 6.2.11.8; it is **not** in the miss list.

Consequence for this spec: job B needs *one* working detection per file, not all three clauses.

## Verified diagnosis

Every mechanism below was confirmed by running the engine over the fixtures, not inferred. **Three of
the four font sub-gaps recorded in the previous session's handoff were wrong**, so the notes that
described them are superseded by this section.

### Job A — clause 6.2.2 test 2

veraPDF profile (`PDFA-2B.xml`), object `PDContentStream`:

```
TEST: inheritedResourceNames == ''
MESSAGE: A content stream refers to resource(s) %1 not defined in an explicitly
         associated Resources dictionary
```

The six corpus fixtures discriminate the rule exactly:

| Fixture | Direct `/Resources` | Stream references | veraPDF |
|---|---|---|---|
| `fail-d` | Type3 font: **absent** | charprocs `/CS0 cs` | fail |
| `pass-a` | Type3 font: absent | charprocs `d1`, no named refs | pass |
| `fail-e` | Form XObject: **absent** | `/CS0 cs` | fail |
| `pass-b` | Form XObject: absent | `rg` only | pass |
| `fail-f` | Page: **absent**; ancestor `Pages` node holds `/XObject <</X0…>>` | `/X0 Do` | fail |
| `pass-c` | page has `/X0`; form has `/X1`; inner form absent | `rg` only | pass |

`fail-e` vs `pass-b` is the sharpest constraint: byte-for-byte the same structure, differing only in
whether the stream *names* a resource. So device-colour operators (`rg`/`g`/`k`, inline-image
abbreviations) are not resource references and must never trigger the rule.

Engine state: **no rule reads `/Resources` for conformance today.** Clause 6.2.2 test 1 is covered by
`ContentStreamOperatorRule`, which is an operator whitelist and unrelated.

### Job B — the font cluster

Live diagnostic over all five fixtures plus the two already-passing controls:

| Fixture | Font | Shown codes | Actual blocker |
|---|---|---|---|
| `8-t01-fail-a` | Type1 + Type1C | `[0]` | encoding glyph name for code 0 is **`null`**; the `.notdef` predicate tests `== ".notdef"` only |
| `8-t01-fail-b` | TrueType | `[0]` | same null-name cause |
| `t02-fail-a` | Type1 + Type1C | `Hello World.` | `period` → `gidByName=0` (absent) but **`derived=True`** → `ResolveSimpleGlyph` returns `Unknown` |
| `t02-fail-b` | MMType1 + Type1C | `#` | `numbersign` → `gidByName=0`, **`derived=True`** → `Unknown` |
| `t02-fail-e` | Type0 / CIDFontType2, Identity-H | 8 CIDs | trailing `(#)` byte is **silently dropped**; every collected CID is genuinely present |

Corrections to the prior handoff, each measured:

1. **`isSimpleCff` is not the blocker.** All three CFFs carry an embedded custom charset (Top DICT
   charset offsets 186, 295, 233 — all > 2), so `CffHasEmbeddedCharset` is `True` and the
   `CheckSimple` gate at `FontProgramRule.cs:183` passes for every one of them. The handoff's claim
   that `8-t01-fail-a`'s "861-byte CFF has none" is false.
2. **Type1/MMType1 are not "gated out entirely".** The gate is expressed on the *program*
   (`metrics.IsCffFont`), not the subtype; a `/Type1` with `/FontFile3` reaches the checks.
3. **The `.notdef` cheap win moves nothing on its own.** Moving the check above the gate was the
   proposed win, but the gate already passes for both `.notdef` fixtures. The real cause is the null
   glyph name. (Reordering is still defensible on principle, but it admits classic Type1 `/FontFile`
   fonts to the check — new FP surface for zero verdict gain. **Deferred.**)
4. **Glyph-count (`GID >= NumGlyphs`) closes nothing by itself.** `t02-fail-e`'s collected CIDs are
   all < `NumGlyphs` (4998). Only the dropped trailing byte distinguishes it from `fail-d`, which we
   already catch.

#### The `derived` provenance bug

`ResolveSimpleGlyph` (`FontProgramRule.cs:356`) refuses to call a glyph absent when its name was
*derived* — this engine's reverse-AGL reconstruction rather than something the document asserts. That
gate is correct and was added deliberately (issues 27–28, round 2).

The bug is the classification feeding it. Two sibling base-encoding tables disagree:

- `CreateStandardEncoding` assigns codes 32–126 via `SetCharacterName` → **not** derived
  (`PdfFontEncoding.cs:347-349`).
- `CreateWinAnsiEncoding` assigns the same range via `SetUnicode(i, …)` → **every name marked
  derived** (`PdfFontEncoding.cs:397-400`).

A name that comes from the Annex-D WinAnsiEncoding table, named by the document's own `/Encoding`, is
asserted by the document. Calling it a guess is simply wrong, and it is what suppresses both `t02`
detections. The names WinAnsi produces are already correct — only the provenance flag is wrong.

#### The dropped trailing byte

`ToUnicodeUsageCollector.cs:115-127` — the collector that actually feeds `ConformanceContext.UsedTextGlyphs`
(`ConformanceContext.cs:431`), **not** `GlyphUsageCollector`, which has the same shape at `:239-245` but
feeds the subsetting path — walks Identity-H text two bytes at a time and skips an odd trailing byte as
"not a complete code". That is exactly backwards for a preflighter —
a string whose final code is incomplete cannot map to any glyph, which is the defect. We will not
fabricate a padded CID; we will record that an unmappable code was shown.

## Design

### Job A — `ExplicitResourcesRule`

New rule, `RuleId = "explicit-resources"`, `AppliesToProfiles = AllPdfA`, registered in
`Preflighter.Rules`. Clause string `ConformanceClauses.For(target, "6.2.2")`.

**Predicate.** For each content stream, report every resource *name* it references that is absent
from the stream's **directly associated** `/Resources` dictionary **and** resolvable through that
stream's inheritance fallback.

Requiring resolvability through inheritance is deliberate: it matches veraPDF's property name
(`inheritedResourceNames`), gives identical verdicts on all six fixtures, and is strictly the
lower-FP choice on the other 980 files — a name that resolves nowhere is a different defect and is
left alone.

**Scopes and their two dictionaries.**

| Scope | Direct `/Resources` | Inheritance fallback |
|---|---|---|
| Page content stream | the page object's own `/Resources` key | nearest `/Resources` up the full `/Parent` chain |
| Form XObject | the form stream's own `/Resources` | the invoking scope's effective resources |
| Type3 glyph procedure | the Type3 **font dict**'s `/Resources` | the invoking scope's effective resources |

The `/Parent`-chain walk reuses the shape already proven at
`ReferencedFontWalker.cs:119-133` (`EffectiveResources`, cycle-guarded). Note `PdfPage.GetResources()`
inherits only one level and reads an *injected* `_parentNode`, so it is unsuitable here.

**Categories tracked.** `Tf`→`/Font`, `Do`→`/XObject`, `cs`/`CS`→`/ColorSpace`,
`scn`/`SCN` trailing name operand→`/Pattern`, `sh`→`/Shading`, `gs`→`/ExtGState`.

Not resource references, and must not trigger: `rg`/`g`/`k`/`sc` and friends, and the device names
`DeviceGray`/`DeviceRGB`/`DeviceCMYK`/`Pattern` as operands of `cs`/`CS`.

**Deferred** — `/Properties` (`BDC`/`DP`) and inline-image `/CS` named colour spaces. Neither is needed
by any fixture, both are pure FP surface, and the inline-image path would mean planning against
`InlineImageOperator`'s dictionary shape without having verified it.

**Traversal.** Model on `DeviceColourAnalysis`, which already walks page → form → Type3 glyph with
per-scope resources. Carry a `(direct, inherited)` pair down instead of a single dictionary. Depth cap
and cycle guard mirroring `ContentWalk.cs:24,78`. Tiling patterns and annotation appearance streams
are **deferred** for the same reason as `/Properties`.

**Finding.** One finding per offending stream, listing the names. Populate `PageIndex` and
`ObjectNumber` — unlike `InlineImageRule`, which cannot because `ContentWalk` drops provenance. Our
walk owns provenance, so it must carry it.

### Job B — three targeted fixes

**B1 — base-encoding name provenance.** Rebuild `CreateWinAnsiEncoding` to assign names via
`SetCharacterName` from an explicit Annex-D table, exactly as `CreateStandardEncoding` already does.
Apply the same treatment to the other `SetUnicode`-built base tables. The resulting names are
unchanged; only `IsDerivedName` changes. Closes `t02-fail-a` and `t02-fail-b` via 6.2.11.4.1.

*Risk:* more codes become trustworthy, so the CFF arm can now call more glyphs absent — the exact FP
class the derived gate protects. The existing `GetGlyphIdByCffEncoding` fallback
(`FontProgramRule.cs:367`) remains the safety net for producers that point a standard name at a
subset lacking it. Validated by the parity gate and `WidthFalsePositiveCorpusTests`.

**B2 — an undefined code is `.notdef`.** Per ISO 32000-1 §9.6.6, a simple-font code with no entry in
the effective encoding renders `.notdef`. Extend the 6.2.11.8 predicate: a shown code is a `.notdef`
reference when **all** of the following hold.

1. The effective encoding yields **no glyph name** for the code (`GetGlyphName(code) is null`).
2. The font is **nonsymbolic** — neither `/Flags` bit 3 nor `HasSymbolCmapEncoding()`. A symbolic font
   legitimately drives its own built-in encoding and routinely has null names; it must be exempt.
3. **CFF arm only** — the program itself offers no glyph for that raw code through its own built-in
   encoding: `GetGlyphIdByCffEncoding(code) == 0`.

**Corrected 2026-08-20, on measurement.** This condition originally applied to both arms, using
`GetGlyphId((ushort)code) == 0` for TrueType. Task 7's mandatory verification step proved that wrong
before any code was written: on `8-t01-fail-b` (`DYOKPS+ArialMT`) `GetGlyphId(0)` returns **1**.
`EmbeddedFontMetrics.GetGlyphId` performs no AGL translation — it passes the raw value to
`CmapTable.GetGlyphId`, which walks every subtable in platform-preference order and returns the first
non-zero hit, including from the last-resort Mac/Symbol/ISO tier. A subsetting artifact in one of
those fallback tables answers glyph 1 for raw value 0.

The asymmetry between the arms is real and principled, not a workaround. A CFF program carries a
built-in **encoding** that genuinely maps raw codes to glyphs, so probing it by raw code asks a
meaningful question. A nonsymbolic TrueType font has no such thing: ISO 32000-1 §9.6.6.4 routes code →
glyph **name** → Unicode → cmap, so with no name there is no lookup to perform and a raw-code cmap
probe is a rendering heuristic answering a different question. `ResolveSimpleGlyph`'s existing TrueType
branch already works this way — it converts the name to a Unicode value before consulting the cmap and
never probes by raw code.

So for a **nonsymbolic TrueType** font, conditions 1 and 2 alone are sufficient: an unnamed code has no
standard mapping and is `.notdef`. Closes `8-t01-fail-a` (CFF arm) and `8-t01-fail-b` (TrueType arm).

Because this removes the program-side net on the TrueType arm, the corpus-wide zero-false-positive
assertion over all 1316 files is the sole empirical guard. If it trips, back the TrueType arm out
rather than widening it.

**B3 — incomplete final composite code.** When an Identity-H/V string has an odd byte count, record
that the font showed an unmappable code and treat it as a `.notdef` reference (6.2.11.8). No CID is
fabricated. Closes `t02-fail-e`.

### Tooling fixes

**T1 — `ParityLeverage` semantics.** `FlipsAlone` must equal `AppearsInMisses`; `MinimumPayingSet`
collapses to the clause itself. Rewrite the class doc and `ParityReport`'s prose accordingly, and fix
the class comment's claim that the font cluster "only paid all three together".

**T2 — the ratchet is stale.** `AgreementFloor[PdfA2b]` is **972** (`ParityReportTests.cs:30`) while
actual agreement is **978**: the two most recent landings never raised it, leaving six points of gain
unprotected. Raise to 978 as a first, separate step so the existing gain is locked before any new
code lands; raise again at the end of the batch.

**T3 — stale `t04` comments.** `ContentStreamOperatorRule.cs:23-27` and `ParityReportTests.cs:100`
both describe the `6-2-2-t04-*` files as run-together-operator fixtures. They are the Resources
fixtures for test 2. This is the filename-vs-test-number trap the repo already warns about at
`ParityReportTests.cs:57-59`.

**T4 — full-clause ratchet.** Add `6.1.6` and `6.1.13` (already at full parity, never added) to
`ParityFullClauses[PdfA2b]`, plus `6.2.2` and any font clause that reaches full.

## Deferred, with issues to file

None of these blocks a miss; each is real and each adds FP surface for zero verdict gain.

1. **Type0 fonts with an embedded-CMap `/Encoding` are skipped entirely.** `Type0Font.EncodingName`
   returns null for a stream *and* for an indirect reference, so `CheckType0`'s Identity gate drops
   the font — losing its `.notdef` and width checks. `CidCMap` already exists and is usable; the
   comment at `FontProgramRule.cs:113-114` claiming the engine lacks a CMap parser is stale.
   Measured: **no corpus fixture uses a stream `/Encoding`**, so this closes nothing here.
2. **`GID >= NumGlyphs` is never checked** for Identity `/CIDToGIDMap`. The predicate already exists
   at `SubsetProgramGlyphs.cs:30-55` and is used by 6.2.11.4.2 but not by `FontProgramRule`.
3. **Predefined-charset CFF** (ISOAdobe/Expert/ExpertSubset) stays out of scope.
4. **`.notdef` check ordering** relative to the program-kind gate (see correction 3 above).

## Non-goals

- **`WidthTolerance` stays at 10.** The profile's tolerance is 1, but 10 is deliberate and measured:
  conformant files round-trip within ~1 unit and genuine failures diverge by 41+. All seven genuine
  width files are already caught. The six files counted as "6.2.11.5 misses" fail that clause in
  veraPDF only as a *consequence* of a missing glyph — veraPDF reads the `.notdef` advance as the
  program width — so they are closed by B1/B2/B3, not by narrowing the tolerance.
- No remediation recipes. These rules are detection-only; `pellucid fix` gains nothing here.

## Test strategy

**Per-task cadence — this is binding on implementers.** Measured costs: full `PdfLibrary.Tests`
suite ~8 min, `tools/gate.sh` ~40 min, parity gate **3 s**, app build + corpus scan ~35 s.

- **Per task:** targeted `--filter` for the code you touched, **plus the parity gate**, plus a
  corpus scan where behaviour could reach real files. Do **not** run the full suite per task.
- **Per branch:** one full `PdfLibrary.Tests` run before the whole-branch review.
- **Per batch:** one `gate.sh`, one oi-corpus rebaseline, one engine pin, one push.

The parity gate over 1316 files is a *stronger* false-positive check than the unit suite and costs
three seconds. Run it after every task.

```powershell
$env:VERAPDF_CORPUS = "C:\Users\jorda\RiderProjects\veraPDF-corpus"
$env:PARITY_REPORT  = "$PWD\PdfLibrary.Tests\Conformance\parity\PARITY-REPORT.md"   # MUST be absolute
dotnet test PdfLibrary.Tests --filter 'Category=Parity' -c Release
```

**Unit tests.** Hand-built in-memory `PdfDocument`s, following
`ContentStreamOperatorRuleTests.cs` (rule under test instantiated directly against a fresh
`ConformanceContext`). Job A needs a grandparent-`/Parent` case — model it on
`ReferencedFontWalkerTests.cs:56`. Job B extends `PreflightSlice19Tests`.

**Guard tests.** Every fixture-premise assumption gets its own test, as
`Fixture_font_lacks_ff_ligature` already does — a fixture that silently stops exercising its premise
is how a green suite hides a dead code path.

**Corpus oracles.** Each closed file gets a `LocalOnly` assertion that we now flag it, and the three
`6-2-2-t04-pass-*` files get explicit no-false-positive assertions.

## Acceptance criteria

1. PDF/A-2b verdict agreement **986/986**; A-2u 22/22, A-3b 12/12, UA-1 296/296 unchanged.
2. **Zero false positives across all 1316 files.** Non-negotiable; a violation reverts the task.
3. `AgreementFloor[PdfA2b]` raised to 986 with each step's justification recorded inline.
4. Clause **6.2.2 at full parity** (6/6) and added to `ParityFullClauses`.

   The font clauses are **not** expected to reach full clause parity, and the plan must not assert
   they will. Verdict parity and clause coverage are different measures: our fixes add `.notdef` and
   glyph-present detections, not width detections, so 6.2.11.5 stays at 7/13 — veraPDF flags that
   clause on the remaining files only as a *consequence* of the missing glyph (it reads the `.notdef`
   advance as the program width), which is not a defect we reproduce. Expected after this work:
   6.2.11.4.1 8/11, 6.2.11.8 6/8, 6.2.11.5 7/13. Record the measured numbers; add a font clause to
   `ParityFullClauses` only if it actually reaches 100%.

   **Issue 32** (6.2.11.4.1 not a locked parity clause) therefore closes only if that clause measures
   full. On the projection above it will not — reassess against the measurement rather than closing
   it on plan.
5. `PARITY-REPORT.md` regenerated; its leverage prose reflects T1's corrected semantics.
6. Full `PdfLibrary.Tests` suite green; `gate.sh` green once for the batch; CI green on
   build-windows / build-linux / build-macos / app-tests (`package-path` is a permanent tripwire).
7. Deferred items 1–4 filed as tracker issues.
