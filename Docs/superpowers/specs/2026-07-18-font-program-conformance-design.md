# Font-program conformance program — design

_2026-07-18. Engine: `PdfLibrary` (origin/master `2f20ca2`, public 2.5.0). Closes the remaining veraPDF
font clauses (6.2.11.x / 7.21.x) and, per capability, ~12 Matterhorn 31-xxx conditions. Multi-slice TDD
program, leverage-ordered._

## Goal & invariant

Close the font-program detection gaps that dominate both the veraPDF parity tail and the uncovered
Matterhorn font conditions. These gaps all need the engine to **inspect the embedded font program** (glyph
resolution, advance widths, CMap streams), which the current rules deliberately avoid for simple fonts.

**Hard invariant — 0 false positives, corpus-wide.** PdfLibrary is a strict subset of veraPDF: every
current disagreement is an unimplemented clause, never a wrong rejection. Every slice must preserve
`FP == 0` across all 1,316 corpus files before its floor bump. When resolution is not confident, the rule
**skips and emits nothing** — it never guesses. Partial closure of a clause is acceptable and expected.

## Ground truth (established during brainstorming)

Sources: veraPDF verdict snapshot (`PdfLibrary.Tests/Conformance/parity/verapdf-verdicts.json`,
core/model 1.30.2, corpus `49de56c`) and the veraPDF profile rule definitions
(`~/RiderProjects/veraPDF-validation-profiles`). Gap = files veraPDF fails that PdfLibrary currently misses.

| Gap (A-2b files) | Clause / test | What it checks | Capability | Status |
|---|---|---|---|---|
| 5 | 6.2.11.8 t1 (+ UA 7.21.8) | shown code must not resolve to `.notdef` (glyph 0) | simple-font code→GID | **not done** (Type0-Identity only today) |
| 5 | 6.2.11.4.1 t2 (+ UA 7.21.4.1 t2) | embedded font must define every glyph referenced for rendering (`isGlyphPresent`) | simple-font code→GID | **not done** |
| 7 | 6.2.11.5 t1 (+ UA 7.21.5) | declared width == program advance (±1 unit) | advance for classic Type1 / predefined-charset CFF | **partial + FP-trap** |
| 4 | 6.2.11.3.3 t2/t3 (+ UA 7.21.3.3) | embedded CMap `/WMode` == stream body; CMap must not `usecmap` a non-predefined CMap | CMap-stream tokenizer | **no parser exists** |
| 1 | 6.2.11.3.1 t1 (+ UA 7.21.3.1) | CIDSystemInfo compat for a *predefined* (non-Identity) CMap name | predefined-CMap → (Registry, Ordering) table | **not done** (embedded-CMap only today) |
| 1 (UA) | 7.21.7 t1 | every used code has a ToUnicode/Unicode value present | `FontUnicodeMapping.ToUnicodeValue` | **exists — reuse** (t2 values already done) |

Structural insight: `.notdef`, glyph-present, and widths all need **one shared capability — a trustworthy
simple-font code→GID resolver**. That is exactly the capability the current code refuses to attempt
(symbolic cmaps, built-in encodings, the WinAnsi 0x80–0x9F remap band make naive replication FP-prone).
The CMap-parse / predefined-CMap-CSI / ToUnicode-presence clauses are independent and low-risk.

## The linchpin — tri-state simple-font glyph resolver

New engine capability, mirroring veraPDF's `Glyph.isGlyphPresent` / `Glyph.name`. Resolves a shown
character code to a glyph in the embedded program for **simple** fonts (Type1 `FontFile`, TrueType
`FontFile2`, CFF/Type1C `FontFile3`). Returns a **tri-state**:

- **Present** — code resolves to a real glyph (GID > 0, in range).
- **NotDef** — code definitively resolves to glyph 0.
- **Unknown** — resolution is not confident (unhandled symbolic cmap, missing table, predefined-charset
  CFF) → the rule **skips**, emits nothing.

Only `NotDef` / definitively-absent yields a finding. `Unknown` skips — the FP-safe escape hatch that lets
us attempt the dangerous cases without regressing the 0-FP invariant.

**Resolution per font type (ISO 32000-1 §9.6.6):**
- **TrueType, non-symbolic:** `/Encoding` (+ `/Differences`) → glyph name → Unicode (Adobe Glyph List) →
  `(3,1)` cmap; fall back to `(1,0)`. Reuses the `TrueTypeAdvance` path already in `FontProgramRule`.
- **TrueType, symbolic:** built-in cmap — `(3,0)` (with the F0xx range fallback) or `(1,0)`. Where the
  engine cannot reproduce the symbolic lookup confidently → **Unknown**.
- **Type1 / CFF:** `/Encoding` (+ `/Differences`) or built-in encoding → glyph name → charset GID via
  `EmbeddedFontMetrics.GetGlyphIdByName`. CFF gated on an **embedded charset** (subset fonts); predefined-
  charset CFF → **Unknown** (see traps).

**Known traps (load-bearing — from memory, verified):**
- **CFF predefined charset** — `Type1Table.cs` (~L169–187) mis-parses predefined-charset operands `0/1/2`
  as byte offsets, so `GetGlyphIdByName` misresolves on full (non-subset) CFF. "Fixing" it has **rendering
  blast radius**. Mitigation: gate CFF resolution on `CffHasEmbeddedCharset` (already the pattern in
  `FontProgramRule.CheckSimple`); predefined-charset CFF → Unknown.
- **CFF advance** — `EmbeddedFontMetrics.GetAdvanceWidthByName` hardcodes 500 for CFF. Never route widths
  through it; use name → GID → `GetAdvanceWidth(gid)` (already the pattern in `SimpleCffAdvance`).
- **6.2.11.5 floor history** — the width check was dropped on CIDFontType0/CFF once for a CFF-advance FP on
  the conformant `PDFUA-Ref-2-08` file. Re-verify FP=0 on that file every width slice.

## Slices (leverage-ordered — resolver first)

Ordering chosen by the user: biggest-leverage first (the resolver unlocks the most files), accepting more
upfront regression risk, mitigated by the tri-state `Unknown→skip` and the mandatory FP=0 gate.

### Slice 1 — simple-font resolver + `.notdef` + glyph-present
Build the resolver; prove it on the **binary** checks (safest surface — a conservative resolver just
skips, never over-reports magnitude). Extends `FontProgramRule`.
- Clauses: **6.2.11.8** t1 (.notdef simple), **6.2.11.4.1** t2 / **7.21.4.1** t2 (glyph-present), **7.21.8**.
- ~11 A-2b + UA files. Matterhorn: 31-x conditions gated on glyph presence / symbolic cmap.
- Deferred within slice: any font type the resolver can't hit `Unknown`-safe confidence on stays skipped.

### Slice 2 — font-metric widths
Reuse the resolver; add classic-Type1 `FontFile` advance and, where safe, predefined-charset-CFF advance
(6.2.11.5 / 7.21.5). Highest FP magnitude → done after the resolver is trusted.
- 7 A-2b files. Sub-cases deferrable if 0-FP can't be held (classic Type1, predefined-charset CFF).

### Slice 3 — CMap-stream parser
New minimal tokenizer (a `Conformance` helper) reading `/WMode` and `usecmap` from the embedded CMap
stream body. No mapping/codespace logic — only these two facts.
- Clauses: **6.2.11.3.3** t2 (WMode match) / t3 (usecmap ref) + UA **7.21.3.3** mirror. ~7 files.
- Extends `FontDictionaryRule` (or a sibling CMap rule). Matterhorn: 31-x WMode/chaining conditions.

### Slice 4 — predefined-CMap CIDSystemInfo
Add a predefined-CMap → (Registry, Ordering[, Supplement]) table; extend `FontDictionaryRule`'s
6.2.11.3.1 check to the predefined-name case (today it only fires for embedded-CMap streams).
- Clause: **6.2.11.3.1** t1 (+ UA **7.21.3.1**). 1 A-2b file.

### Slice 5 — ToUnicode presence
Reuse `FontUnicodeMapping.ToUnicodeValue` (returns null when no mapping). Fire when a used code has no
ToUnicode/Unicode value and no alternate mechanism (predefined CMap, etc.).
- Clause: UA **7.21.7** t1 (values t2 already covered by `Pdfa2uToUnicodeValuesRule`). 1 UA file.

## Per-slice TDD flow (unchanged — proven 5× this session)

1. Probe the veraPDF profile + the snapshot to identify the exact failing files/testNumbers for the clause.
2. **RED** — write the failing conformance test (fixtures from the corpus fail files).
3. **GREEN** — implement in `PdfLibrary/Conformance/Rules/` (or a new `Conformance` helper); register in
   `Preflighter.cs`.
4. Regenerate the parity report:
   `PARITY_REPORT=<scratch> dotnet test PdfLibrary.Tests --filter FullyQualifiedName~Generate_parity_report -c Release`.
5. Verify the clause is closed **AND `FP == 0`** corpus-wide (inspect the regenerated report).
6. Bump the floor in `PdfLibrary.Tests/Conformance/ParityReportTests.cs` (A-2b 935, UA-1 281 today).
7. **Update `Docs/pdfua/matterhorn-coverage.md`** — mark the 31-xxx conditions the new capability closes.
8. Commit; `git merge --no-ff` the slice branch to master; push.

**Never** `git add` the untracked `Docs/plans/*.md` (pre-existing, not ours).

## Conventions & pitfalls (engine-specific)

- `PdfObject` lives in namespace `PdfLibrary.Core` (not `.Primitives`) — `using PdfLibrary.Core;`.
- `PdfName` stores 1 byte/char (Latin1).
- Font rules are profile-aware: shared A-2 / UA-1 sub-numbers → one rule serves both via
  `ConformanceClauses.For(target, sub)`. PDF/X-4 is excluded from all font-program clauses (ISO 15930-7
  carries none).
- Used codes come from `ConformanceContext.UsedTextGlyphs` (rendering-mode-3 text is still counted — a
  documented over-approximation, but veraPDF exempts RM3, so presence/width checks must honour
  `renderingMode == 3` as a skip where the model exposes it).
- `CffGlyphOutline` is an alias for `GlyphOutline` — no type-mismatch when threading widths.

## Out of scope / deferred (never-guess boundary)

- Predefined-charset CFF **name resolution** (rendering blast radius) — skipped as Unknown.
- Any font type/case where the resolver cannot reach `Unknown`-safe confidence.
- Inline-image and content-stream-operator clauses (6.2.8 t4, 6.2.2) — a different program.
- Fixing `Type1Table.cs` predefined-charset parsing — Tier-2 engine work, not this program.

## Success criteria

- Slices 1–5 merged to master, pushed, each with `FP == 0` verified before its floor bump.
- A-2b agreement rises from 935. **Upper bound: ~22 distinct corpus files** fail *only* a targeted font
  clause (2 more already agree via an already-detected clause — `6-1-13-t10-fail-a` via 6.1.13,
  `6-2-11-3-3-t01-fail-a` via 6.2.11.3.3 t1). The **net gain is lower**: some of those 22 are already
  caught by the partially-implemented clauses (6.2.11.5 6/13, 6.2.11.8 3/8, 6.2.11.3.1 3/4), so the true
  per-clause file gaps are 6.2.11.5 → 7, 6.2.11.8 → 5, 6.2.11.4.1 t2 → 5, 6.2.11.3.3 t2/t3 → 4,
  6.2.11.3.1 → 1, minus file overlaps and principled deferrals. The real figure is **measured per slice**
  from the regenerated parity report, not predicted here. UA-1 rises from 281 on the mirrored clauses.
- `matterhorn-coverage.md` reflects the 31-xxx conditions closed per capability.
- No regression in the 2,239-test suite; no new false positive on any corpus file.
