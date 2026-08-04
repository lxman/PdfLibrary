# Font-program parse faults — recording and a corpus canary

Date: 2026-08-04
Status: design approved, plan not yet written
Motivating defect: the Type1C CFF charset bug, fixed as engine `6564363`

## Why this exists

`EmbeddedFontMetrics` swallows every font-program parse failure. Eleven `catch` blocks, of which only
two — cmap (`:361`) and the Type1 constructor (`:484`) — log anything at all. The other nine are
bare. When `Type1Table`'s constructor threw on GWG090's embedded
Type1C program, the catch at line 394 set `_cffTable = null; _isCffFont = false;` and the font
quietly took the TrueType path for months. Nothing was red. Nothing was written down. The bug was
found only because someone went looking at the bytes.

That is the defect this spec addresses. Not the CFF parser — that is already fixed — but the
property of the code that let a broken parser look healthy.

### Why "add a log line" is not the fix

`PdfLogger` is off by default and file-only:

- Every `PdfLogConfiguration` flag is an auto-property with no initializer, so all categories default
  to `false` (`Logging/PdfLogConfiguration.cs:7`). The XML doc on `LogTransforms` claims "Default:
  true"; it is wrong.
- The only production `Initialize` call in either repo is `PdfLibrary.Wpf.Viewer/App.xaml.cs:56`, and
  it sets **every** category to `false`.
- The sole sink is a Serilog file writer (`Logging/PdfLogger.cs:54`). There is no console sink, no
  in-memory sink, no event, no `ILogger` seam. Nothing can subscribe.
- No test in either repo captures `PdfLogger` output. Grep across `PdfLibrary.Tests` returns zero
  hits.

So the cmap and Type1 catches — the two that already log — are in practice exactly as silent as the
nine bare ones. Logging is not the mechanism. It is at best a courtesy on top of the mechanism.

### What the silent fallback actually degrades to

Worth stating plainly, because it is worse than "slightly wrong metrics".

Three of six production call sites never check `IsValid` and return the metrics object regardless:
`TrueTypeFont.cs:113`, `Type1Font.cs:212`, `Type0Font.cs:182`. An invalid object reports
`UnitsPerEm = 1000` (hardcoded fallback), `NumGlyphs = 0`, `Ascender`/`Descender` 0; every
`GetAdvanceWidth` returns 0, every `GetGlyphId` returns 0, every `GetGlyphOutline` returns null.

`IsCffFont == false` is the more dangerous signal, because it is silent *success* rather than silent
failure. `GlyphPathService.cs:38` sends the font down `GlyphOutlineToPath.FromTrueType`, which reads
a `glyf` table the font does not have, and produces an empty path. `CoreTextRenderer.cs:278` drops
the CFF charset lookup for Type0/CID and uses the raw `MapCidToGid` result — wrong glyphs, no error.
That is precisely the GWG090 failure mode.

## Scope

Additive observation only. **No behaviour changes.** Every fallback stays exactly as it is; the only
new thing is a record of what went wrong, and a gate that reads it.

Out of scope, deliberately:

- Fixing any of the fallbacks, or making call sites honour `IsValid`. Real problems, separate work.
- The Preflight/conformance channel — see "Rejected alternatives".
- Any Pellucid UI surface.
- Pointing the canary at the veraPDF, PDF/UA, or BFO corpora — see "Corpus scope".

## Design

### 1. The fault record

```csharp
internal enum FontProgramStage
{
    SfntDirectory, Head, MaxP, Hhea, Hmtx, Name, Cmap, RawCff, CffTable, GlyfLoca, Type1Program
}

internal readonly record struct FontProgramFault(FontProgramStage Stage, string ExceptionType);
```

`EmbeddedFontMetrics` gains `public IReadOnlyList<FontProgramFault> Faults { get; }`, empty when the
program parsed cleanly.

Each of the eleven `catch` blocks becomes:

```csharp
catch (Exception ex)
{
    RecordFault(FontProgramStage.CffTable, ex);
    // ...existing fallback, character for character unchanged...
}
```

`RecordFault` appends to the list and emits one `PdfLogger` line under `LogCategory.Text`. The
existing cmap log line stays as it is — it carries the message, which is useful when logging is
actually on.

Nine of the eleven sit in the sfnt/raw-CFF constructor (`:228`) and one in the Type1 constructor
(`:416`). The last, `LoadGlyphTables` (`:1401`), runs lazily on first outline request rather than at
construction, and records under `GlyfLoca` the same way. The `Faults` list is therefore append-only
across the object's lifetime, not frozen when the constructor returns — the canary must request an
outline before reading it if it wants `GlyfLoca` coverage.

**The exception message is deliberately not recorded.** Messages vary across .NET versions and
locales, and this repo has hard-won bit-identical cross-platform gates. `Stage` plus `ExceptionType`
identifies a regression without importing that instability into a committed baseline.

### 2. The corpus canary

Home: `PdfLibrary.Tests`. It is an engine-health check, it needs no rendering, and
`Conformance/GwgGosHarness.cs:38` already resolves the corpus (`GWG_GOS` env var, else a walk-up to a
sibling `gwg-gos`).

The walk copies `Fonts/Type0FallbackAuditTests.cs:216` — `PdfDocument.Load` → `doc.GetPage(i)` →
`page.GetResources()` → recurse into Form XObjects, with its depth cap of 12 and its
`seenFonts`/`seenRes` object-number cycle guards. That method is already 90% of this canary; it
walks fonts and parses programs, and simply never asserts.

For each font whose descriptor carries a `FontFile`, `FontFile2`, or `FontFile3`:

- `GetEmbeddedMetrics()` returns null → emit a synthetic `MetricsNull` row. This catches the *other*
  swallow, `TrueTypeFont.cs:116`'s `catch { return null; }`, which destroys the object before any
  fault list can be read.
- Otherwise request one glyph outline — `GetGlyphOutline(0)` — before reading `Faults`, so the lazy
  `GlyfLoca` stage has run. Without this the canary is blind to `loca`/`glyf` failures. Then emit one
  row per entry in `metrics.Faults`.

A font that parses cleanly emits nothing.

### 3. Baseline, not zero-tolerance

TSV, matching `gwg-render-hash-baseline.txt`:

```
# <n> font programs examined across <m> files. Regenerate with PDFLIBRARY_FONT_FAULT_REGEN=1 and review the diff.
1-CMYK/GWG010_CMYK_OP_x3.pdf	ABCDEF+SomeFont	CffTable:IndexOutOfRangeException
```

Key is `Category/File`, then the `/BaseFont` name, then `Stage:ExceptionType`. Rows are sorted and
deduplicated so the file is deterministic.

`Compare(expected, actual)` is a pure function emitting `NEW` / `CHANGED` / `MISSING`, unit-tested on
its own the way `GwgRenderHashCompareTests` tests its counterpart. Regen is
`PDFLIBRARY_FONT_FAULT_REGEN=1`, which rewrites the file **and still fails the test** — the
`GwgRenderHashGateTests.cs:136` trick, so a regeneration can never be mistaken for a passing run.
The baseline path resolves by walking up to the `.csproj` directory, so regen writes source and not
`bin/`.

Expected state on landing: `6564363` fixed GWG090, so the baseline is likely empty or close to it.
An empty baseline is a healthy canary — every future fault appears as a `NEW` row.

### 4. Two guards

**Baseline non-empty, zero files discovered → fail, do not skip.** Read the baseline before deciding
to skip, and skip only when both it and the discovered set are empty
(`GwgRenderHashGateTests.cs:101` established this).

**`programsExamined > 0` → assert.** This is the canary-of-the-canary, and with a likely-empty
baseline it is the guard doing the real work. If the resource walk itself regresses and finds no
fonts, an empty result would otherwise read as a clean pass. The examined count also goes into the
baseline header comment, so a coverage collapse shows up in the regen diff rather than passing
silently.

### 5. Regression test

The test that gives the whole exercise its point: hand-build a CFF whose Top DICT omits the charset
operator — the exact Type1C shape from `6564363` — and assert `Faults` contains `CffTable` and
`IsCffFont` is false. Run against the pre-`6564363` parser, that test goes red.

`FontParser.Tests/Cff/MinimalCff.cs` is already shared into the test project via a `<Compile
Include>` at `PdfLibrary.Tests.csproj:47`, so the scaffolding exists.

Plus targeted unit tests feeding deliberately-malformed bytes for a representative subset of the
other stages (truncated sfnt directory, corrupt `head`, corrupt `cmap`), asserting the expected
`Stage` appears and — importantly — that the existing fallback value is unchanged.

## Corpus scope

GWG only: 51 files under `gwg-gos/Ghent_PDF_Output_Suite_V50_Patches/*/Categories/*/Patches/*.pdf`.

Not veraPDF. That corpus is 2907 files, a large fraction of them *deliberately* malformed — a
baseline over it would be enormous, would churn, and would drown the signal this canary exists to
carry. Extending the root list later is a one-line change if it ever earns its place.

## CI

The canary carries `[Trait("Category", "LocalOnly")]` and will **not** run in CI, because no PDF
corpus is committed to either repo (`PdfLibrary/.gitignore:6` ignores `*.pdf` globally; CI runs
`--filter Category!=LocalOnly`). This is the same status as `GwgRenderHashGate` and
`GhentScoreboard`, which are the gates that have in practice been catching regressions. The unit
tests — `Compare()`, the malformed-program fault tests, the CFF charset regression — are ordinary
tests and do run in CI.

## Rejected alternatives

**A Preflight finding.** `Conformance/Rules/FontProgramRule.cs:93` skips unparseable programs with an
explicit FP-safe `continue`, and that is correct: Preflight reports on the *document*, and "our
parser choked" is a statement about the engine. Routing engine defects through it would make
Preflight lie about documents it was handed. Rejected.

**An app-facing engine-health surface in Pellucid.** Premature. Nothing consumes it, and it would put
an engine-defect channel into a product UI. If the canary shows this class of failure is common in
the wild, revisit then.

**Throwing instead of falling back.** A PDF renderer must survive broken fonts; a document with one
unparseable font should still render its other 40. Rejected outright.

**Zero-tolerance assert instead of a baseline.** Works today at 51 files, breaks the first time
anyone points it at a corpus with intentionally-broken fonts, and offers no way to record a known,
accepted failure. The baseline costs one file and buys that.

## Known adjacent issues, not addressed here

Recorded so they are not rediscovered as new:

- `PdfLogConfiguration.LogTransforms`' doc comment claims "Default: true" with no initializer backing
  it. One-line doc fix in an unrelated subsystem; left alone.
- `Type1Table` reads the Encoding format byte positionally rather than seeking the Top DICT Encoding
  offset, so `Type1Table.Encoding` is parsed from unrelated bytes. Harmless today — nothing
  downstream trusts it.
- Ladder step 1 has a TOCTOU: it can return null if the chosen sibling font file becomes unreadable
  between indexing and load.
- The three call sites that ignore `IsValid` (`TrueTypeFont.cs:113`, `Type1Font.cs:212`,
  `Type0Font.cs:182`). The canary will make their consequences visible, which is the right order of
  operations — measure before changing behaviour.
