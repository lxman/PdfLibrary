# Non-embedded font substitution: metadata-driven resolution

**Date:** 2026-08-04
**Repo:** PdfLibrary (engine)
**Status:** design, awaiting review
**Scope:** SLICE 1 of 2 — the metadata index and the resolution ladder over *installed* fonts.

## Slicing

Split deliberately, because the two halves have different risk profiles and the first carries all of
the measured benefit.

- **Slice 1 (this spec)** — `FontMetadataIndex`, the base-35 alias table, and ladder steps 1–3 over
  installed fonts. Pure code, no new binaries, no licence question. Delivers the whole measured
  outcome: Windows and Linux move to `C059-Italic`, macOS is unchanged.
- **Slice 2 (separate spec)** — bundled Liberation as a guaranteed floor, plus the symbolic guard that
  exists only to keep symbolic fonts away from that floor. Adds ~4 MB of binaries and a licence file.

The symbolic guard belongs with slice 2, not here: it protects against falling back to a Latin
substitute that has none of the required glyphs, and in slice 1 there is no such fallback to protect
against. Slice 1 must therefore introduce **no Latin floor of any kind** — failing the ladder returns
null, exactly as today.

## Goal

Resolve a font that the renderer cannot use from the PDF itself to the best available face on the
machine, by matching the font's own metadata rather than guessing at filenames.

## Why

`SubstituteFontResolver` currently resolves through `Standard14Fonts.SubstituteFileBaseNames`, an
ordered list of hardcoded **file base names** (`timesi`, `LiberationSerif-Italic`, …). Three
consequences, all measured (see Evidence):

1. **Only fonts whose filename happens to match are reachable.** On the Windows dev box 755 faces are
   installed and roughly forty filename strings can ever be matched. A perfectly apt face named
   anything else is invisible.
2. **The style is discarded at the last hop.** The un-styled generic (`"Times"`, `"Helvetica"`,
   `"Courier"`) appears in all four style arrays of each family, so on a machine holding only the
   generic file every style collapses to regular. This shipped as a real defect: macOS rendered the
   Ghent GWG 9.0 panel's Type1 row upright against the page's own embedded italic reference. Fixed
   for collections in `6afbe7a` (face selection); the list itself is still wrong.
3. **The right typeface is passed over.** `NewCenturySchlbk-Italic` resolves to a Times italic on
   every platform even where `C059-Italic` — the actual New Century Schoolbook italic — is installed.

## Evidence

All figures measured 2026-08-04 on the three CI machines, with throwaway probes (since deleted).

**Installed fonts**

| Box | files | faces | faces with a PostScript name |
|---|---|---|---|
| Windows | 732 | 755 | 755 (100%) |
| Linux (llmbox) | 395 | 421 | 421 (100%) |
| macOS (macmini) | 371 | 788 | 788 (100%) |

macOS packs 788 faces into 371 files — collections are the norm there, which is why the face-selection
defect surfaced on that box and nowhere else.

**Index build cost** (Windows, 732 files / 471 MB, 24 cores, warm cache, Debug)

| Strategy | cost |
|---|---|
| directory enumeration only (today) | 4 ms |
| header-only seek + read, serial | **42 ms** |
| full file read + `SfntFont` parse | 591 ms |
| header-only, `Parallel.ForEach` | 11 ms |

Reading whole files costs 14× more than reading the three tables we need. At 42 ms serial the index
is affordable eagerly, once per process.

**Persistent cache: rejected on measurement.** Validating a cached index (enumerate + compare size and
mtime) costs ~9–10 ms, against a 42 ms rebuild — and on Windows the enumeration's `FIND_DATA` already
carries size and mtime, so there are no per-file `stat` calls to avoid. A cache saves ~30 ms at best
and nothing against the parallel build, in exchange for a writable location (imposed on every consumer
of a NuGet package), an invalidation surface, schema versioning, and cross-process concurrency. Its
failure mode is a silently wrong substitution. Not worth it.

**Resolution comparison** — the only font in the 51-fixture GWG corpus that reaches substitution:

| Box | today (filename-first) | proposed (metadata) |
|---|---|---|
| Windows | `timesi.ttf#0` Times New Roman Italic | **`C059-Italic.otf#0`** (step 2) |
| Linux | `LiberationSerif-Italic.ttf#0` | **`C059-Italic.otf#0`** (step 2) |
| macOS | `Times.ttc#2` Times Italic (`ps=Times-Italic`) | `Times.ttc#2` (step 3) |

Churn is **one fixture on every platform**, every change neutral-or-better, no platform regressed.
Windows and Linux converge on the same face, so cross-platform divergence on that panel shrinks.

## Architecture

### `FontMetadataIndex` (new, internal)

Supersedes `FontDirectoryIndex` as the resolver's lookup structure (see compatibility, below). Built
once per process inside the
existing `SystemFontLocator.Default` singleton — that singleton already exists because rebuilding
per-`PdfRenderer` was once 86% of page-record time (Type3 fonts construct a sub-renderer per glyph).

Per face — **every** face, `.ttc` collections enumerated individually — it records:

| field | source |
|---|---|
| `PostScriptName` | `name` ID 6 |
| `Families` | `name` IDs 1 and 16, **all** language records |
| `EnglishFamily` | ID 16/1 at Windows `langID 0x409` or Mac `langID 0`; else first seen |
| `Subfamily` | `name` IDs 2 and 17, English preferred |
| `Italic` | `head.macStyle` bit 1, or subfamily contains Italic/Oblique |
| `Bold` | `head.macStyle` bit 0, or subfamily contains Bold |
| `Path`, `FaceIndex` | for `EmbeddedFontMetrics(bytes, faceIndex)` |

Reads only the sfnt header, table directory, `name` and `head` — kilobytes per file, never the whole
font. Malformed or unreadable files are skipped, matching today's best-effort behaviour.

`OS/2` is deliberately **not** read. The ladder scores on italic and bold only, so `usWeightClass` and
`usWidthClass` would be recorded and never consulted; and slice 2's symbolic guard keys off the PDF's
own `/Flags`, not the font's. Adding them later is a one-line change if a weight-aware score is ever
wanted.

**PostScript name is the primary key.** It is ASCII by specification, has no language variants, is
unique per face, and is exactly what a PDF's `/BaseFont` is derived from. Measured present on 100% of
1,964 faces across all three boxes.

**All localized family records are indexed as aliases.** A document naming a font by its localized
family (e.g. `ヒラギノ明朝 ProN`) must still resolve. English is used only to canonicalise for
reporting and to break ties deterministically — never to filter. Discarding non-English records would
make CJK families unmatchable.

### Resolution ladder

`SubstituteFontResolver.Load`, in order. First hit wins.

1. **PostScript name exact match** against `/BaseFont` with the `ABCDEF+` subset tag stripped.
2. **Aliased family match** + style score. The alias table is the PostScript base-35 set taken from
   Ghostscript's `Fontmap.GS` — `NewCenturySchlbk→C059`, `Palatino→P052`, `Bookman→URW Bookman`,
   `AvantGarde→URW Gothic`, `ZapfChancery→Z003`, `Symbol→StandardSymbolsPS`, `ZapfDingbats→D050000L`,
   plus the standard-14 families to their Nimbus / Liberation / Tinos-Arimo-Cousine equivalents.
3. **Synthetic standard-14 name** from the existing `Classify` + `SyntheticStd14Name`, matched by
   PostScript name then aliased family. This is what keeps macOS on `Times.ttc#2` today.
4. Failing all three, return `null` — the run is not drawn, exactly as today. *(Slice 2 inserts the
   bundled-Liberation floor here, with the symbolic guard in front of it.)*

Style scoring throughout: `+1` italic match, `+1` bold match, ties break on lowest face index so an
indistinguishable collection resolves as it does today.

### `ISystemFontProvider` compatibility

`ISystemFontProvider` is **public**, and `GetFontData(string) → byte[]?` cannot express "face 2 of this
collection". Rather than break implementers:

- Add `FontMatch? Resolve(FontRequest)` as a **default-implemented** interface method, where
  `FontRequest` carries the parsed `/BaseFont` plus style, and `FontMatch` carries bytes and face
  index. The default implementation delegates to `GetFontData` and face 0, so existing implementers
  keep compiling and keep working.
- `SystemFontLocator` overrides it with the metadata ladder.
- `GetFontData`, `GetAvailableFontFamilies`, `IsFontAvailable` and `FindFirstAvailable` remain, backed
  by the new index. Note their documented contract already says these take and return **file base
  names**; that stays true, since the index still knows each face's path.

This keeps the change additive on the public surface. `FontDirectoryIndex` becomes an implementation
detail of the metadata index rather than a separate structure.

## Error handling

- A malformed or unreadable font file is skipped at index time; the index is best-effort.
- A face that fails to parse at resolution time is skipped and the ladder continues.
- Failing every step returns `null`, and the run is not drawn — unchanged from today.
- No exception escapes the resolver.

## Testing

- **Unit, per ladder step:** synthetic in-memory fonts with controlled `name` / `head` tables, as
  `SubstituteFontFaceSelectionTests` already does for collections.
- **Localized names:** a face whose only family record is non-English must be matchable by that name,
  and its `EnglishFamily` must fall back deterministically.
- **No new floor:** a request that matches nothing must still return null. This is the regression test
  that keeps slice 1 honest — it is the assertion slice 2 will deliberately invert.
- **Style scoring:** italic request against a Regular/Bold-only collection keeps face 0.
- **Gates:** GWG render-hash and Ghent scoreboard on all three platforms. Exactly one fixture
  (GWG090) and one panel (p3/s0 [9.0]) are expected to move; the crop must be viewed and re-triaged
  per `render-verify`, and any other movement blocks.

## Out of scope

- **Bundled fonts and the symbolic guard — slice 2.** Liberation Serif / Sans / Mono, 4 styles each,
  ~4 MB as `EmbeddedResource` alongside the existing ICC profile and CMaps, under **SIL OFL 1.1**
  (chosen over URW base-35's AGPL-plus-font-exception because this is a public repo shipping a NuGet
  package, and OFL needs no legal review). Liberation covers the standard-14 Latin cases but has no
  Symbol or ZapfDingbats glyphs, which is exactly why the guard ships alongside it rather than before.
- **The Type1C parse failure.** GWG090 embeds `NewCenturySchlbk-Italic` as Type1C; the engine cannot
  parse it (`EmbeddedFontMetrics.IsValid == false`) and silently substitutes. This is why that fixture
  reaches substitution at all. It is a **separate and arguably higher-value defect** — a document with
  a valid embedded font renders in the wrong typeface with no diagnostic — and is tracked on its own.
- **Synthetic oblique.** Unnecessary once a real bundled italic is always reachable; a designed italic
  beats a sheared roman.
- **DirectWrite / CoreText / fontconfig.** Three P/Invoke surfaces in a library whose value is being
  pure C# and dependency-free. `System.Drawing`'s font classes are Windows-only since .NET 6, expose
  family names rather than font bytes, and snapshot at construction — unusable on all three counts.
- **A persistent on-disk cache.** Rejected on measurement, above.

## Risks

- **Baseline movement beyond the predicted one fixture.** Mitigated by the gates; anything unexpected
  blocks until explained.
- **A machine whose fonts differ from the three measured.** The ladder degrades through steps rather
  than failing, so the worst case is a less apt but correctly styled face — or, where the machine has
  nothing at all, the same null the engine returns today. Slice 2's floor removes that last case.
- **Index cost on cold cache / slow disk.** 42 ms is warm and Debug. `Parallel.ForEach` takes it to
  11 ms and is a one-line change if a cold measurement ever justifies it — measure before applying,
  since parallel random reads can degrade on spinning disks.
