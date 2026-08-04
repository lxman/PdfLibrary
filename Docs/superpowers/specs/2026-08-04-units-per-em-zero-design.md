# UnitsPerEm can be zero, and seven division sites do not guard it

Date: 2026-08-04
Status: **decided 2026-08-04 — Option A now, Option B recorded as the end state.** Ready to plan.
Found by: the font-fault probe, 2026-08-04
Depends on: `2026-08-04-minimal-sfnt-fixture-builder-design.md` (its tests need that builder)

## The defect

A `head` table of 54 zero bytes parses **successfully**. `HeadTable` throws nothing, so no fault is
recorded, `IsValid` stays `true` — and `UnitsPerEm` comes out **0**.

Observed directly (probe, since deleted):

```
head content 0x00    valid=True   upem=0    glyphs=424   faults=(none)
```

This is worse than the failures the fault-diagnostics work was built for. Those at least *threw*. This
one reports full health and hands back a value that poisons every downstream calculation.

## Blast radius

Seven unguarded production division sites, every one of them `double` arithmetic — so there is no
`DivideByZeroException` to catch. They silently yield `Infinity` (or `NaN` for `0/0`):

| Site | Expression |
|---|---|
| `PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs:996` | `fontUnitValue * fontSize / UnitsPerEm` (`ScaleToUserUnits`) |
| `PdfLibrary/Fonts/TrueTypeFont.cs:45` | `glyphWidth * 1000.0 / embeddedMetrics.UnitsPerEm` |
| `PdfLibrary/Fonts/Type1Font.cs:56` | same |
| `PdfLibrary/Fonts/Type1Font.cs:87` | same |
| `PdfLibrary/Fonts/Type1Font.cs:95` | same |
| `PdfLibrary/Builder/PdfDocumentWriter.cs:1828` | `1000.0 / metrics.UnitsPerEm` |
| `PdfLibrary/Builder/PdfDocumentWriter.cs:1882` | same |

An `Infinity` advance width propagates into text positioning and into written `/Widths` arrays. The
builder sites are the more alarming pair: they would emit `Infinity` into a **produced PDF**.

No site guards. There is no central chokepoint — the property is read directly at all seven.

## Why this is not "just add a guard at each site"

Seven sites today, and the count only grows. A guard per site is the pattern that produced this bug's
neighbours: it works until someone adds an eighth site, and nothing tells them to.

`UnitsPerEm` is *already* a fallback-bearing property. When `head` is absent or throws, the
constructor assigns 1000 (`EmbeddedFontMetrics.cs:285`, `:290`) and documents it as "Fallback
default". A zero-valued `head` is the same situation — an unusable `head` — arriving by a different
road. Treating it differently is the inconsistency, not the fix.

## The decision

This changes render output for any document containing such a font, so it is not mine to make
unilaterally. Three options:

### Option A — clamp to 1000 at construction, record a fault (recommended)

Where `UnitsPerEm` is assigned from a parsed `head`, treat a non-positive value exactly as a missing
one: fall back to 1000 and record a fault so the canary can see it.

- **Consistent** with the two existing fallback paths, which already answer "unusable head" with 1000.
- **One chokepoint.** The eighth division site inherits the guard for free.
- **Visible.** A recorded fault means the corpus canary reports the font instead of scoring it clean —
  which is exactly the property this whole line of work exists to establish.
- **Renders something plausible** rather than nothing. A font whose `head` is corrupt may still have
  perfectly good outlines; 1000 units/em is the near-universal convention for CFF and a common one
  for TrueType, so the glyphs will very likely be right.
- Risk: if the true value was 2048 and the rest of the font is intact, glyphs render at roughly half
  scale — wrong, but visibly wrong, and now accompanied by a fault row.

### Option B — mark the font invalid

Set `IsValid = false` when `UnitsPerEm <= 0`.

- Honest: we genuinely cannot scale this font.
- But three of six call sites ignore `IsValid` (`TrueTypeFont.cs:113`, `Type1Font.cs:212`,
  `Type0Font.cs:182`), so this would **not actually stop** the zero from reaching the division sites.
  It would need those call sites fixed first — which is separate, larger work that the canary was
  deliberately built to measure before anyone changes it.
- Where it does take effect, the font stops rendering entirely. A worse outcome than half-scale glyphs
  for a document whose only defect is a corrupt `head`.

### Option C — guard each division site

Reject. Seven sites, no enforcement, and it leaves the poisoned value in circulation for the next
reader to trip over.

## Decision (2026-08-04)

**Option A now. Option B is the recorded end state.**

A is the smallest change, is consistent with behaviour the file already documents, routes the problem
into the diagnostic channel just built for it, and does not depend on the unfixed `IsValid` call sites.

B is the more honest answer and is where this should end up — but it is inert until the three call
sites that ignore `IsValid` are fixed, because until then a `false` there does not stop the zero
reaching the seven division sites. That sequencing is deliberate, not timidity: the corpus canary
exists precisely to measure the consequences of those call sites *before* anyone changes their
behaviour.

### The gate for moving to B

B becomes actionable when all three of `TrueTypeFont.cs:113`, `Type1Font.cs:212`, and
`Type0Font.cs:182` honour `IsValid`. That is its own spec, and it should be written only after the
canary has run against a wider corpus than GWG — the whole point of measuring first is to know how
many real documents an `IsValid` gate would stop rendering.

Until then, A's clamp is containment, and the `Head:UnitsPerEmZero` fault row is the measurement.
When B lands, the clamp is removed in the same change; the fault row stays.

## Design

### Representing a non-throwing fault

`FontProgramFault` currently carries `(FontProgramStage Stage, string ExceptionType)`. A zero
`UnitsPerEm` is a fault with no exception, so `ExceptionType` is the wrong field name for it.

Rename that field `Detail` (**decided 2026-08-04**). The baseline's wire format is unchanged — still
`Stage:Detail`, still `Head:ArgumentException` for a throwing fault — and the new case reads
`Head:UnitsPerEmZero`.

**Now is the cheap moment for this rename:** the committed baseline body is currently empty, so no
committed row churns. That stops being true the first time a real fault gets pinned.

The rename is sequenced **first** in the plan, ahead of the clamp. Renaming a field that four test
files already reference is mechanical; doing it before the clamp lands keeps the clamp's diff about
the clamp.

### The change

At each of the two sites that assign `UnitsPerEm` from a parsed `head` table, and at the CFF
`FontMatrix`-derived site (`EmbeddedFontMetrics.cs:270`, which computes
`(ushort)Math.Round(1.0 / fontMatrix[0])` and can round to 0 for a large enough `FontMatrix[0]`):

```csharp
if (UnitsPerEm == 0)
{
    RecordFault(FontProgramStage.Head, "UnitsPerEmZero");   // or RawCff/CffTable for the FontMatrix site
    UnitsPerEm = 1000;
}
```

`RecordFault` gains a `(FontProgramStage, string detail)` overload beside the existing
`(FontProgramStage, Exception)` one.

The `FontMatrix` site is included because the probe did not test it and the same arithmetic hazard is
visible by inspection — a design should not leave a known-shaped hole next to the one it is closing.

### Tests

All CI-runnable, using the `MinimalSfnt` builder from
`2026-08-04-minimal-sfnt-fixture-builder-design.md` — **this spec depends on that one landing first**:

1. `head` of 54 zero bytes → `UnitsPerEm == 1000`, `Faults` contains `Head:UnitsPerEmZero`.
2. The same font's `ScaleToUserUnits` returns a finite number — the actual point, asserted directly
   rather than inferred from the property.
3. A CFF whose `FontMatrix[0]` is large enough to round the reciprocal to 0 → clamped, fault recorded.
4. A healthy font → `UnitsPerEm` unchanged, no fault. The guard must be scoped, not blanket.

### Corpus

The canary must be re-run and its baseline re-pinned if any GWG program trips the new fault. Current
state is 136 programs, 0 faulting, so a new row would be real news and must be reported, not absorbed.

## Not in scope

- Fixing the three call sites that ignore `IsValid`. Measure first.
- Guarding the seven division sites individually — the clamp makes them moot.
- Any other `head` field that parses to an implausible value. `UnitsPerEm` is singled out because it is
  a divisor; the rest are not.
