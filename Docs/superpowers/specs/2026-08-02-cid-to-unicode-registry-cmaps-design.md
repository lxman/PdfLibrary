# B-1 — CID→Unicode text extraction for registered Adobe CID collections

**Date:** 2026-08-02
**Issue:** Pellucid tracker B-1 (low priority; engine-side): a Type0 font with no `/ToUnicode` and a
real Adobe registry ordering (Japan1/Korea1/GB1/CNS1) could have its CIDs mapped to Unicode through
Adobe's published CMap resources. Today `Type0Font.DecodeCharacter` falls through to
`char.ConvertFromUtf32(charCode)` — tofu/garbage for CJK.
**Repo:** PDF engine (`PdfLibrary`) only. No Pellucid change; no rendering change; extraction only.

## Measured scope (2026-08-02, working-tree audit extension of `Type0FallbackAuditTests`, 072111d)

Corpus: 3005 files (veraPDF + GWG), 369 Type0 fonts. 51 lack `/ToUnicode`; 33 are Adobe-Identity
(unextractable by construction); the reachable population is **18 rows** (Japan1 9, Korea1 5, GB1 2,
CNS1 2 — deduplicating to ~4–6 distinct fonts across conformance-suite variants). Their Type0
`/Encoding` values — the fact that fixes the scope:

| /Encoding | rows |
|---|---|
| embedded CMap stream | 15 |
| Identity-H | 2 |
| UniJIS-UCS2-H | 1 |

Full listing: `b1-scoping-encodings.md` (session artifact; the histogram above is the durable fact).
**Zero rows use a predefined legacy encoding name** (no RKSJ/EUC/GBK-EUC/…), so bundling the
predefined *encoding* CMap sets — the cost the tracker feared — is not needed. What IS needed:
reading the **embedded encoding CMap stream** (code→CID), which the engine does not parse today.

## Goals

1. `Type0Font.DecodeCharacter` produces real Unicode for a registered-collection CID font with no
   `/ToUnicode`, when the encoding is an embedded CMap stream, Identity-H/V, or a `Uni*-UCS2-*`
   name — the entire measured population.
2. The corpus audit's 18 rows extract non-fallback text (LocalOnly assertion).
3. Zero behavior change for fonts with `/ToUnicode` (step 1 of the chain still wins) and for
   Adobe-Identity fonts (no mapping exists; unchanged fallback).
4. Rendering untouched: no draw-path, width, or glyph-selection change. (The engine renders CIDs
   through the embedded font program; this feature only affects text extraction.)

## Non-goals

- No predefined encoding-CMap bundles (RKSJ etc.) — zero measured users. If a real document ever
  needs one, the natural seam is the encoding-shape branch in `Type0Font`'s registry context (§3):
  a predefined non-Identity, non-UCS2 name would resolve there to a bundled encoding CMap parsed
  by the same `CidCMap` machinery. Not built now.
- No variable-length code support in the extractor: `PdfTextExtractor` reads Type0 codes as 2-byte
  big-endian today and continues to (all measured fonts are 2-byte CJK CMaps). The `CidCMap` parser
  records codespace ranges for validity but the extraction loop stays 2-byte. Documented limitation.
- No conformance-rule changes: `FontUnicodeMapping` stays deliberately conservative
  (benefit-of-the-doubt for registered collections is now *true* rather than merely presumed).
- No `Supplement` gating: the UCS2 tables cover the highest supplement Adobe publishes; a CID above
  a font's declared supplement simply looks up like any other (missing → fallback).

## Design

### 1. `CidCMap` — embedded encoding CMap parser (code→CID)

New `PdfLibrary/Fonts/CidCMap.cs`, a sibling of `ToUnicodeCMap` parsing the *CID* operators:

- `begincodespacerange`/`endcodespacerange` — recorded (byte-length validity), not used to vary
  the extractor's 2-byte read.
- `begincidchar`/`endcidchar` — `<code> cid` pairs.
- `begincidrange`/`endcidrange` — `<lo> <hi> cid` (CID increments across the range).
- `usecmap` — NOT followed in v1 (an embedded CMap that layers on a predefined base would need the
  base bundled; none of the measured population does this — the parser records the name and the
  caller treats an unresolved `usecmap` as "parse what's present"). Documented in the class doc.

API shape: `static CidCMap Parse(byte[] data)`; `int? MapCodeToCid(int code)`. Reuse
`ToUnicodeCMap`'s hex-token lexing idiom (same file conventions, `partial`/regex style as that
class uses). Malformed input degrades to an empty map (extraction falls through the chain), never
throws out of `Parse` — same resilience posture as `ToUnicodeCMap.Parse`.

### 2. `AdobeCidToUnicode` — bundled CID→Unicode tables

New `PdfLibrary/Fonts/AdobeCidToUnicode.cs` + four embedded resources under
`PdfLibrary/Resources/CMaps/` (gzip-compressed):

- `Adobe-Japan1-UCS2`, `Adobe-Korea1-UCS2`, `Adobe-GB1-UCS2`, `Adobe-CNS1-UCS2` from Adobe's
  `cmap-resources` (github.com/adobe-type-tools/cmap-resources, BSD-3-Clause). Their license file
  is bundled alongside as `LICENSE-Adobe-CMaps.txt` and referenced from the class doc. (Korea1 is
  the ordering the measured fonts declare; if the current Adobe repo ships it under the KR
  supersession, take the latest file that still names Adobe-Korea1 — the plan pins exact
  files/checksums at fetch time.)
- These are CMaps in the same syntax (`begincidrange` with UTF-16BE *codes* mapping code→CID —
  note the direction: the UCS2 CMaps map **Unicode→CID**; the table must be **inverted** at load
  into CID→Unicode. Collisions (two Unicode points mapping to one CID) keep the first — matching
  the stable ordering of the source file — and the class doc says so.)
- Loaded lazily, once per ordering, behind `Lazy<T>`; parsed with the same `CidCMap` machinery
  (the file syntax is identical; only the interpretation of "code" differs).
- API: `static string? Lookup(string ordering, int cid)` returning a UTF-16 string (surrogate
  pairs possible) or null.

Size note: the four files gzip to roughly 100–300 KB total; acceptable for the package. The plan
records the exact measured sizes.

### 3. Wiring in `Type0Font`

`DecodeCharacter`'s chain gains one step after ToUnicode, before the embedded-glyph-name fallback:

```
1. ToUnicode lookup                        (unchanged, still wins)
2. NEW registry mapping, when descendant CIDSystemInfo is Registry "Adobe" +
   Ordering ∈ {Japan1, Korea1, GB1, CNS1}:
     a. Encoding name Uni*-UCS2-H/V  → the code IS UCS-2: return it directly
        (chars 0xD800-0xDFFF and unmapped-plane values excluded → fall through)
     b. Encoding Identity-H/V        → cid = code
        Encoding = embedded CMap stream → cid = CidCMap.MapCodeToCid(code)
        then AdobeCidToUnicode.Lookup(ordering, cid)
3. Embedded-font glyph-name fallback       (unchanged)
4. char.ConvertFromUtf32(charCode)         (unchanged)
```

- `Type0Font` lazily builds its registry context on first use: descendant `CIDSystemInfo`
  (Registry/Ordering strings), the `/Encoding` shape (name vs stream), and the parsed `CidCMap`
  for the stream case (decoded stream bytes → `CidCMap.Parse`). All null-safe: any missing piece
  disables step 2 silently.
- Existing behavior notes preserved: the logging style of the current glyph-name fallback
  (`PdfLogger.Log(LogCategory.Text, ...)`) is mirrored with a one-time-per-font log when the
  registry path activates.

### 4. What deliberately does not change

- `FontUnicodeMapping.HasReliableUnicode` — unchanged (its Type0 arm already returns true for
  registered collections).
- `PdfTextExtractor`'s 2-byte Type0 loop and the advance-width path — unchanged.
- Rendering (`CoreTextRenderer` etc.) — unchanged.

## Testing & acceptance

1. **`CidCMap` unit tests** — synthetic CMap streams: cidchar, cidrange (incrementing), multiple
   codespace ranges, malformed input → empty map, `usecmap` present → parses local operators and
   ignores the base.
2. **`AdobeCidToUnicode` unit tests** — table loads once per ordering; spot-check one known
   CID↔Unicode pair per ordering taken from the shipped file itself at fetch time (the plan pins
   the four (ordering, cid, unicode) triples with their source lines, so the test asserts against
   the bundled data's own ground truth, not a hand-remembered value); unknown ordering → null;
   out-of-range CID → null; inversion collision policy (first wins) pinned on a constructed case.
3. **`Type0Font` integration tests** — synthetic Type0 dictionaries (the test project already
   builds font dictionaries): (a) embedded-CMap-stream encoding + Japan1 ordering + no ToUnicode →
   decodes through the new path; (b) Identity-H + Korea1; (c) UniJIS-UCS2-H returns the code
   directly; (d) ToUnicode PRESENT → new path never consulted (pin: give the ToUnicode a mapping
   that disagrees with the registry table and assert ToUnicode's answer); (e) Adobe-Identity →
   unchanged fallback.
4. **Corpus audit (LocalOnly)** — extend `Type0FallbackAuditTests` with an assertion-bearing test:
   every one of the 18 registered-ordering/no-ToUnicode rows decodes at least one sampled code to
   something other than the raw-code fallback, and the 33 Adobe-Identity rows are byte-identical to
   before. (The existing census test stays a census.)
5. **Full engine suite** green; conformance counts (PdfA/UA agreement floors) unchanged — this
   feature adds no rule behavior.
6. **Pellucid side:** nothing required. No repin needed for Pellucid correctness (extraction flows
   through the engine API); the next routine repin picks it up.

## Risks

- **UCS2-file direction inversion** is the one subtle step (Unicode→CID source inverted to
  CID→Unicode) — mis-reading it produces systematically wrong text that still LOOKS like CJK. The
  per-ordering spot-check triples (from the files' own lines) pin the direction.
- **License hygiene**: BSD-3-Clause requires retaining Adobe's notice — the bundled license file +
  class-doc reference satisfy it; the plan adds it to the package's third-party-notices if the
  engine ships one.
- **Embedded-CMap variability** in the wild exceeds the corpus (usecmap layering, 1-byte or mixed
  codespaces). Mitigated by the fall-through design: any gap degrades to today's behavior, never
  worse.
