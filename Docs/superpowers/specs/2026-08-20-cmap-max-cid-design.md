# CMap-aware conformance: max CID ≤ 65535 — design

_2026-08-20. Closes PDF/A-2b clause **6.1.13 test 10**. Target: veraPDF verdict parity 976/986 →
**978/986**, and clause 6.1.13 from 13/15 to **15/15 (full)**._

## 1. What the rule is

From the veraPDF PDF/A-2B profile XML:

| Clause / test | Object | Test expression | Description |
|---|---|---|---|
| 6.1.13 t10 | **`CMapFile`** | `maximalCID <= 65535` | A conforming file shall not contain a CID value greater than 65535 |

The object matters. This is a property of the **CMap file** — the largest CID the CMap *declares* —
not a scan of CIDs actually used in content streams. That makes it far narrower than the old
"needs an embedded-CMap parser" framing suggested, and it means no content walk is required.

## 2. Why this is now cheap, and two stale comments

Two places in the engine still claim the engine cannot parse a CMap. Both predate B-1, which landed
`PdfLibrary/Fonts/CidCMap.cs`:

- `Conformance/Rules/ImplementationLimitsRule.cs` — "the CID > 65535 (needs an embedded-CMap parser)
  limit is out of scope for this slice".
- `Conformance/Rules/FontProgramRule.cs:114` — "any other CMap (predefined name or embedded stream)
  needs a CMap parser the engine lacks, so the font is skipped".

The first is what this work deletes. **The second is out of scope here but should be recorded**: it
is not merely documentation rot. `Type0Font.EncodingName` returns null when `/Encoding` is a stream,
so `IsIdentity(null)` is false and `CheckType0` skips the font entirely — losing its `.notdef` *and*
width checks for every font with an embedded CMap. Fixing that belongs to the font job, but this
work makes it possible.

### Measured against the two target files

Both corpus fixtures carry the same CMap shape. Every range has a code span of `0xFF`, comfortably
under `CidCMap`'s `MaxRangeSpan = 0xFFFF`, and the final range is:

```
begincidrange
  <0000> <00ff> 0
  …
  <3f00> <3fff> 65536      ← top CID 65536 + (0x3fff − 0x3f00) = 65791
endcidrange
```

**Max declared CID = 65791 > 65535** in both `6-1-13-t08-fail-b.pdf` and `6-1-13-t10-fail-a.pdf`.
Verified by extracting and parsing the CMap bodies directly.

### The +2, and why it is +2 rather than +1

The parity report's leverage table lists 6.1.13 as "flips alone: 1". That undercounts, because the
table's model assumes every clause in a file's blocking set must close before the verdict moves. A
whole-file miss is defined purely by **verdict** (`!VeraCompliant && PdfLibraryConforms`), so any
single clause firing closes it.

| File | veraPDF flags | Closed by this work? |
|---|---|---|
| `6-1-13-t08-fail-b.pdf` | 6.1.13/t10 | yes — sole blocker |
| `6-1-13-t10-fail-a.pdf` | 6.1.13/t10 + 6.2.11.4.1/t2 + 6.2.11.8/t1 | **yes** — reporting t10 alone makes it non-conformant |

The corpus proves the principle independently: `6-2-11-8-t01-fail-d.pdf` is a file veraPDF flags on
6.2.11.5, which the engine does **not** flag on 6.2.11.5 — yet it is not a miss, because the engine
flags 6.2.11.8 on it instead.

## 3. Design

### 3.1 A cheap max, not a materialised map

`CidCMap.Parse` materialises **every code in every range** into a `Dictionary<int,int>`. For a
CJK CMap that is tens of thousands of entries per font, and a conformance rule that only needs a
maximum should not pay it.

Add a separate static scan on `CidCMap` that computes the maximum declared CID **without
materialising anything**:

```csharp
internal static long? MaxDeclaredCid(byte[] data)
```

Returns null when the data declares no `cidchar`/`cidrange` entry at all (not a CID CMap, or
unparseable) — which the rule treats as "nothing to check", never as a violation.

It reuses the existing `CidCharRegex`, `CidRangeRegex` and `FindBlocks`. It does **not** touch
`Parse`, `_codeToCid`, or the instance path, so the font/decode behaviour that path feeds is
provably unaffected.

`internal`, not `public`. `CidCMap` is a public type, but only the conformance layer (same assembly)
needs this, and new public surface on the engine is flagged by convention.

### 3.2 Deliberate under-reports, all FP-safe

Three cases where this reports nothing, each a conscious subset of veraPDF:

1. **Ranges wider than `MaxRangeSpan`.** `Parse` skips a range whose code span exceeds `0xFFFF` as
   corrupt. `MaxDeclaredCid` applies the *same* guard, even though computing a max costs no
   allocation and could evaluate it. Rationale: a 3- or 4-byte codespace is legal in ISO 32000, but
   this engine's CID handling assumes 2 bytes throughout; flagging a shape the rest of the engine
   treats as corrupt risks a false positive on a document no corpus fixture covers. Revisit only if
   a fixture demands it.
2. **`usecmap`.** `CidCMap` records `UseCMapName` but does not follow it, so CIDs inherited from a
   base CMap are invisible. Under-report.
3. **Predefined CMaps.** Only an embedded `/Encoding` **stream** is examined. A predefined name
   (`Identity-H`, `UniJIS-UCS2-H`, …) has no embedded file to read, and none are bundled. Note
   Identity-H's maximum CID is exactly 65535, so it conforms anyway and costs nothing.

Arithmetic uses `long`, because `cidStart + (hi - lo)` on a large range can exceed `int`.

### 3.3 Reaching the CMap from a rule — use the unguarded path

There are two routes from a font to its embedded CMap bytes, and only one is usable:

- **`Type0Font._encodingCMap`** — hard-gated on `CIDSystemInfo/Registry == "Adobe"` **and** an
  Ordering the engine bundles. Fonts failing either gate never have their CMap fetched, and the
  field is not exposed anyway. **Do not use.**
- **Direct dictionary navigation**, exactly as `FontDictionaryRule.ReadCMapBodyWMode` already does:
  resolve `/Encoding`, pattern-match `PdfStream`, call `GetDecodedData(context.Document.Decryptor)`.
  No Registry/Ordering gate. **Use this.**

Enumerate via `ConformanceContext.ReferencedFonts` (`IReadOnlyList<PdfDictionary>`), filtering to
entries whose `/Subtype` is `Type0`. Note that a Type0 font's descendant CIDFont appears in that
list as its own separate flat entry; it carries no `/Encoding`, so the subtype filter excludes it
naturally.

Parse each distinct CMap stream once — dedup on the stream's object number, since several fonts can
share one CMap.

### 3.4 The `yield break` trap in `Check`

`ImplementationLimitsRule.Check` currently ends:

```csharp
if (integerReported)
    yield break;

foreach (Finding f in CheckContentStreamIntegers(context))
    yield return f;
```

That `yield break` terminates the **whole iterator**, not just the integer arm. Appending a CID
sub-check after it would be silently skipped on any document that also has an out-of-range integer —
a suppression that looks like absence of a violation. This is the same fix-one-arm-miss-its-twin
shape that has cost this repository repeatedly.

`Check` must therefore be restructured so the suppression is **scoped to the arm it belongs to**:

```csharp
if (!integerReported)
    foreach (Finding f in CheckContentStreamIntegers(context))
        yield return f;

foreach (Finding f in CheckCMapCids(context))
    yield return f;
```

This is a prerequisite of the work, not an optional tidy-up.

### 3.5 Message wording

The finding must **not** contain the word "integer". `IsIntegerFinding` distinguishes this rule's
integer findings by a `Message.Contains("integer")` substring test. It is currently applied only to
`CheckStringsAndNames` output, so a CID finding would not be tested by it today — but relying on
that is exactly the kind of latent coupling that breaks on the next edit.

Wording: name the CMap's maximum and the limit, e.g.
*"A CMap declares CID 65791, exceeding the maximum permitted CID value of 65535."*

Clause via `ConformanceClauses.For(context.Target, "6.1.13")` — never a hardcoded ISO string.

At most one finding per document, matching every other sub-check in this rule.

## 4. Scope boundaries

**In scope:** detection of clause 6.1.13 test 10, the `MaxDeclaredCid` scan, the `Check`
restructure, and deleting the stale "needs an embedded-CMap parser" claim from this rule's doc
comment.

**Explicitly out of scope:**

- Widening `FontProgramRule.CheckType0`'s Identity-CMap gate (§2). This work makes it feasible;
  doing it changes font findings and carries its own false-positive risk. It belongs to the font
  job.
- Following `usecmap`, bundling predefined CMaps, or admitting wide-codespace ranges (§3.2).
- Any remediation. Detection only; no Pellucid changes beyond the engine pin and the oi-corpus
  rebaseline.

## 5. Testing

**Corpus-free unit tests** — the pattern used by `XrefTableSpacingRuleTests` and
`HexStringFormatRuleTests`, so they run in CI, which has no corpus.

- `MaxDeclaredCid` directly: a `cidrange` whose top CID exceeds 65535; one exactly at 65535
  (conforming, boundary); a `cidchar` above the limit; a mixed CMap where the maximum comes from a
  `cidchar` rather than a `cidrange`; data with no CID operators at all → null; malformed data →
  null rather than throw.
- The `MaxRangeSpan` under-report, asserted as **deliberate**: a range wider than `0xFFFF` whose top
  CID exceeds the limit returns the maximum ignoring that range. A test that pins a deliberate
  under-report is what stops a later reader "fixing" it into a false positive.
- The rule: a Type0 font whose `/Encoding` is a stream with an over-limit CMap produces exactly one
  finding on clause 6.1.13; a conforming CMap produces none; a font with a predefined `/Encoding`
  name produces none; a document with no Type0 font produces none.
- **The `Check` restructure**, explicitly: a document carrying **both** an out-of-range integer and
  an over-limit CID must report both. This is the test that pins §3.4 — without it the regression is
  invisible.
- Message wording: assert the finding does not contain the word "integer" (§3.5).

**Guard probing.** Every guard test is probed by deleting the guard and confirming the test fails.

**Parity gate** (`Category=Parity`, needs the corpus):

- PDF/A-2b verdict parity **976/986 → 978/986**.
- **Zero false positives across all 1316 files** — the standing invariant, never traded.
- Clause 6.1.13 from 13/15 to **15/15 (full)**.
- A-2u 22/22, A-3b 12/12, UA-1 296/296 unchanged.
- The verdict-leverage section should lose 6.1.13 entirely, leaving only 6.2.2 and the font trio.

**Re-baseline `oi-corpus`** in the Pellucid repo: hand-edit the data line, never
`PELLUCID_OI_CORPUS_REGEN=1` (it destroys the file's decomposition history). Expect `conforms` to
fall by 2 and `fails` to rise by 2, with `fixed` and `needsDecision` flat — that flatness is the tell
that this is detection gained rather than repairs lost.

**Render baselines** must not move: nothing on this path touches parsing or rendering.

## 6. Definition of done

1. PDF/A-2b parity is 978/986 with 0 false positives across 1316 files.
2. Clause 6.1.13 is at full parity (15/15).
3. Both target files are individually verified as caught — `6-1-13-t08-fail-b.pdf` and
   `6-1-13-t10-fail-a.pdf` — not merely inferred from the aggregate count.
4. A document with both an out-of-range integer and an over-limit CID reports both findings (§3.4).
5. `ImplementationLimitsRule`'s doc comment no longer claims test 10 needs a CMap parser the engine
   lacks.
6. `CidCMap.Parse` and its instance path are untouched; the font/decode behaviour they feed is
   unchanged.
7. `oi-corpus-baseline.txt` is hand-updated and the Pellucid gate is green.
