# MinimalSfnt — a synthetic sfnt fixture builder for per-stage fault tests

Date: 2026-08-04
Status: design, plan not yet written
Predecessor: `2026-08-04-font-program-fault-diagnostics-design.md` (landed on `feat/font-program-fault-diagnostics`)

## Why this exists

The fault-diagnostics work left a coverage gap it named openly: of the eleven stages in
`FontProgramStage`, only `RawCff` and `SfntDirectory` got per-stage tests. `Head`, `MaxP`, `Hhea`,
`Name`, `Cmap`, `GlyfLoca`, `CffTable`, and `Type1Program` were untested — not because the mechanism
differs, but because reaching them needs a *structurally valid sfnt with one bad table*, and
`MinimalCff` builds bare CFF only.

That gap was accepted on a stated argument: hand-rolling an sfnt builder blind risks a fixture that
is wrong in a way that makes a test pass for the wrong reason, which is worse than no test.

That argument no longer holds, because the fixture can now be *validated against reality*.

## What the probe established

A throwaway probe (run 2026-08-04, deleted the same session) corrupted a real TrueType font —
`Alef-Regular.ttf`, from the user's local font collection — one table at a time, then compared the
result against a hand-built synthetic sfnt. Findings:

| Shape | Result |
|---|---|
| Real font, `head` length → 4 | `Head:ArgumentException`, `UnitsPerEm` → 1000, `IsValid` false |
| Synthetic sfnt, `head` length 4 | `Head:ArgumentException`, `UnitsPerEm` → 1000, `IsValid` false |
| Real font, `cmap` filled `0xFF` | `Cmap:ArgumentException` |
| Synthetic sfnt, `cmap` 64 × `0xFF` | `Cmap:ArgumentException` |
| Synthetic, `maxp` length 4 | `MaxP:ArgumentException` |
| Synthetic, `hhea` length 4 | `Hhea:ArgumentException` |
| Synthetic, `name` length 4 | `Name:ArgumentException` |
| Real font, `head` filled `0xFF` | `GlyfLoca:ArgumentException` (corrupt `indexToLocFormat`) |

**The synthetic and the real font agree on stage and exception type.** That is the validation the
earlier decision was waiting for. The real font is the oracle; it does not become a fixture, so no
font binary is vendored and the Slice 2 licensing decision stays untouched.

Two non-obvious negatives, worth recording so nobody re-derives them:

- A **4-byte `cmap`** records nothing. `cmap` needs `0xFF`-filled content to throw, not a short
  length. The two tables fail differently and the fixture API must not assume otherwise.
- A **4-byte `head`** throws, but a **54-byte all-zero `head`** does not — it parses "successfully"
  into `UnitsPerEm = 0`. That is a separate defect, specified in
  `2026-08-04-units-per-em-zero-design.md`.

## Design

### The builder

`PdfLibrary.Tests/Fonts/Embedded/MinimalSfnt.cs`, `internal static`, alongside the tests that use it.

Not shared into `FontParser.Tests` via a `<Compile Include>` the way `MinimalCff` is. `MinimalCff`
earned that because parser-level and metrics-level charset tests needed the same fixtures; nothing in
`FontParser.Tests` needs this one today. Add the link if and when a second consumer appears.

```csharp
internal static byte[] Build(params (string Tag, byte[] Data)[] tables);
```

Emits: a 12-byte sfnt header (`sfntVersion` `0x00010000`, `numTables`, and zeroed
`searchRange`/`entrySelector`/`rangeShift` — the reader does not consult them), then one 16-byte
directory record per table sorted by tag ordinal, then the payloads back to back. Checksums are
written as zero; nothing validates them.

Deliberately **not** a valid-font builder. It builds *directories over payloads*. A caller wanting a
program that parses cleanly should use a real font, not this — and no test needs that, because
"parses cleanly" is already covered by `MinimalCff` and by the corpus canary.

Two named helpers for the shapes the probe found, so the intent is legible at the call site rather
than encoded in a magic byte count:

```csharp
/// A table too short for its reader — the shape that throws for head/maxp/hhea/name.
internal static byte[] TooShort() => new byte[4];

/// A table of the right size but garbage content — the shape cmap needs, since a short cmap
/// returns cleanly instead of throwing.
internal static byte[] Garbage(int length);
```

### The tests

`PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs`, extending the existing class. One test
per newly-reachable stage, each asserting **two** things — the stage is recorded, *and* the documented
fallback is unchanged. The second assertion is the one that matters: the whole premise of the
diagnostics work is that recording changed no behaviour.

Stages covered: `Head`, `MaxP`, `Hhea`, `Name`, `Cmap`, `GlyfLoca`.

Combined with the existing `RawCff` and `SfntDirectory`, that is **8 of 11**.

### Left uncovered, with reasons

- **`CffTable`** — needs an sfnt carrying a corrupt `CFF ` table. Reachable by composing `MinimalSfnt`
  with a truncated `MinimalCff` payload, and worth doing: this is the stage that hid the Type1C bug.
  Included in scope. That makes it **9 of 11**.
- **`Type1Program`** — a different constructor entirely (`length1`/`length2`/`length3`), unreachable
  from an sfnt. Needs a malformed PostScript Type1 program. Out of scope; its constructor already logs
  and its fallback is already asserted by `FaultsIsNeverNull`.
- **`SfntDirectory`** — already covered.

Final coverage: **10 of 11 stages**, `Type1Program` the sole gap.

## Rejected alternatives

**Vendor a font binary as a fixture.** Would work, and Alef and Amiri are both OFL. Rejected because
it is unnecessary — the synthetic fixture is proven equivalent — and because vendoring font files is
the decision explicitly reserved for Slice 2. A test-coverage improvement should not be the thing that
quietly sets that precedent.

**Point the tests at a font on the developer's machine.** Makes them `LocalOnly` and
machine-dependent, which defeats the purpose: these need to run on CI, where the corpus canary cannot.

**Extend `MinimalCff` instead of a new file.** Different container format, different concerns. The CFF
builder is already 200 lines carrying real offset arithmetic; sfnt directory construction has nothing
in common with it.

## Verification

- All new tests run on CI — no `LocalOnly` trait. That is the point: the canary is local-only, so
  per-stage coverage is the part of this work CI can actually enforce.
- The corpus canary must remain green and its baseline unchanged; this work touches test fixtures
  only, never engine code.
