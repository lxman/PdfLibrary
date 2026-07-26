# Colour decision surface: typed colour-space model, per-component colourant carrier, NChannel conformance

**Date:** 2026-07-26
**Status:** Draft design — awaiting review
**Repos:** `PDF` (engine, majority of the work) and `Pellucid` (corpus gate, `InkDecider`)
**Supersedes scope of:** gap G-4 in `Docs/colour/rendering-conformance.md`
**Related:** `2026-07-16-soft-proof-sp8-unified-ink-decision-design.md` (Pellucid) — the precedent for this consolidation

---

## 1. Why this is not just "implement NChannel"

G-4 has been the one substantive violation in the colour conformance matrix since slice 1: rows 5-3
(NChannel components "shall be evaluated individually") and 5-11 (`/Subtype` not read on the render
path). The obvious plan is to read `/Attributes` and branch.

Auditing the surface that change would land on says otherwise. The colour decision path has four
structural weaknesses, and **every open gap on the board traces to one of them.** Implementing G-4
directly would add a 23rd positional colour-space read and a fourth and fifth field to a record already
documented in-code as lossy — repeating the exact pattern SP-8 was written to stop:

> One question — "which colorants does this painting op mark, and what happens to the ones it doesn't?"
> — is currently answered in **five** places, with five different rules... the scatter is actively
> growing: #3 was added this session precisely because #2 gives the wrong answer.
> — *SP-8 design, 2026-07-16*

SP-8 collapsed five overprint decision sites into one Table 148 lookup. The same disease is now visible
one layer up, on colourant resolution rather than plate masking. This spec fixes the surface first, then
lands G-4 on it.

---

## 2. Evidence

### 2.1 The four seams

**S1 — There is no parsed colour-space model.**
`PdfLibrary/Rendering/ColorSpaceResolver.cs` is 1295 lines containing **22 positional `csArray[i]`
reads**, **30 `Deref` calls**, and **four duplicated dispatch heads** — `:100`, `:610`, `:810`, `:858`
each independently re-check `is not PdfArray { Count: >= N } csArray || csArray[0] is not PdfName
csType` and then `switch (csType.Value)`. Twelve members answer twelve different questions about a
colour space, each re-parsing the raw array from scratch. Nothing is cached.

**S2 — The carrier is lossy and assumes one tint per op.**
`ColorantOrigin` (`Rendering/ColorantOrigin.cs:9`) is
`(IReadOnlyList<string> Names, IReadOnlyList<double> Tints, string AlternateSpace)`. It cannot
distinguish Separation from DeviceN — documented as deliberate leniency at `InkDecider.cs:95-100` — it
carries no `/Subtype`, and it has nowhere to put a per-component alternate. `Tints` is permitted to be
*empty*, because a shading has no single per-op tint.

**S3 — SP-8's consolidation is half-done.**
It unified fills, strokes, shadings and meshes. Images still resolve through
`PdfImageToCmyk.TryToSpotInk`, stencil masks through `RecordingRenderTarget` reading
`ResolvedFillColor`, and shading *patterns* through the pattern machinery. Three parallel routes.

**S4 — Verdicts are booleans that discard the reason.**
Availability is `registry.TryGetPlane(name) != null`, conflating four distinct facts: not a spot
(`SpotColorantRegistry.cs:55`), not discovered (`PageColorantReader.cs:17-18` — Type3 CharProcs and
annotation appearance streams are deliberately not descended), over `planeCap = 16`
(`SpotColorantRegistry.cs:57`), or genuinely absent from the device. `PaintsNothing` is a bool.
`InitialColorFor` returns `null` for both "no answer" and "no change". `RouteSpots` is a single bool
answering an N-component question, which is why `CmykPageRenderer.CompositeInk` (`:315`) must re-loop
`Names` and re-query `TryGetPlane`, recomputing what `AnyRegistered` (`InkDecider.cs:258`) already knew.

### 2.2 Every open gap maps to a seam

| Gap | Seam | Why |
|---|---|---|
| G-4 NChannel | S1 + S2 + S4 | needs `/Attributes`; no per-component slot; `RouteSpots` is binary |
| G-7 `/All` shadings | S2 | empty `Tints` falls through to the flattened path |
| G-8 `/None` shading patterns | S3 | pattern route never consults `PaintsNothing` |
| G-9 `/All` images + stencils diverge | S3 | three routes, three answers for the same tint |
| G-10 `/None` mode-4 clip | S4 | bool conflates "paints nothing" with "do nothing" |
| G-11 Pattern initial colour | S4 | `null` overloaded |
| G-12 `cs`/`CS` 2× cost | S1 | no parsed model ⇒ no cache; `PdfFunction.Create` re-parses type-4 streams per call |
| G-13 stencil routing untested | S3 | |

Eight gaps, four causes.

### 2.3 The real failure mode of G-4 is worse than recorded

The matrix describes G-4 as an all-or-nothing flatten that "drags every colourant through the
alternate." That is only the *no-colourant-registered* case. `InkDecider.Decide` (`:120`) branches on
`AnyRegistered` — **any**, not all:

| Component set | Today | ISO 32000-2 §8.6.6.5 |
|---|---|---|
| all colourants have planes | route all direct | correct |
| none have planes | flatten via document tint transform | defensible — all revert, and the whole-space transform is the best available information |
| **mixed** | route the registered ones, **silently drop the rest** | route the available ones; unavailable ones use *their own* alternate |

The drop is confirmed by reading: `CompositeInk` composites only names with a plane (`:362`), and
`ProcessContribution` (`InkDecider.cs:268`) matches only the four literal strings `Cyan`, `Magenta`,
`Yellow`, `Black`. An unregistered spot hits neither branch and contributes nothing. That is ink
missing from the page.

**This punches through row 5-2, currently scored ✅.** For plain DeviceN the spec requires the *whole
space* to revert when any component is unavailable. The routed arm does not revert. Row 5-2's test
(`SeparationAlternateSpaceTests.DeviceN_RevertsToAlternate_PassingEveryTintToTheTransform`) is an engine
test against `ColorSpaceResolver`, which always flattens — it never reaches `InkDecider`. Every registry
in `InkDeciderTests` is fully populated; **no partial-registration test exists anywhere.**

The mixed case is reachable in production via two documented routes: the 16-plane cap, and the
undiscovered-colourant paths in `PageColorantReader`.

### 2.4 "Evaluated individually" is well-defined — ISO 32000-2 says which alternate

The 32000-1 wording is ambiguous; the 32000-2 wording is not:

> For NChannel colour spaces, the components shall be evaluated individually; that is, only the ones not
> present on the output device shall use the alternate colour space **of that component**.

Per-component evaluation is *not* running the n-in/m-out document transform per component — that
function is documented as describing "the appearance of its colorants **in combination**". Each
component has its own alternate:

- **Spot components** — `/Attributes /Colorants /<name>` is required for NChannel to be a full
  `/Separation` space for that colourant, with its own alternate and its own 1-in tint transform.
  Table 70: "the alternate colour space and tint transformation function of a Separation colour space
  describe the appearance of **that colorant alone**."
- **Process components** — `/Attributes /Process /ColorSpace` + `/Components`; at most one process
  colour space per NChannel space. Reserved names `Cyan`/`Magenta`/`Yellow`/`Black` are always process
  and need no `/Process` entry. For a non-CMYK process space, values are in **natural** form (additive
  for RGB), not subtractive.

### 2.5 The fold rule is measured, not invented

GWG081 (`2-SPOT/Patches/GWG081_DeviceN-Support_5c_X1a.pdf`) is the corpus's only NChannel file:

```
54 0 obj  [/DeviceN [/GWG#20Green /Cyan] /DeviceCMYK 50 0 R 53 0 R]
53 0 obj  <</Colorants 51 0 R /Process 52 0 R /Subtype/NChannel>>
51 0 obj  <</GWG#20Green 14 0 R>>
52 0 obj  <</ColorSpace/DeviceCMYK/Components[/Cyan/Magenta/Yellow/Black]>>
14 0 obj  [/Separation/GWG#20Green/DeviceCMYK <</C0[0 0 0 0]/C1[0.5 0 1 0]/FunctionType 2/N 1>>]
```

Its whole-space type-4 transform (obj 50), decompressed and evaluated:

```
green  cyan  ->  whole-space CMYK        |  /Colorants ramp (0.5t, 0, t, 0)
 0.25  0.00  ->  (0.1250, 0, 0.2500, 0)  |  (0.1250, 0, 0.2500, 0)   max|d| = 0.000000
 0.50  0.00  ->  (0.2500, 0, 0.5000, 0)  |  (0.2500, 0, 0.5000, 0)   max|d| = 0.000000
 1.00  0.00  ->  (0.5000, 0, 1.0000, 0)  |  (0.5000, 0, 1.0000, 0)   max|d| = 0.000000

green=0.50 cyan=0.50: whole=(0.6250, 0, 0.5000, 0)  fold=(0.6250, 0, 0.5000, 0)  max|d|=0.000000
green=0.75 cyan=0.25: whole=(0.5312, 0, 0.7500, 0)  fold=(0.5312, 0, 0.7500, 0)  max|d|=0.000000
```

Two consequences.

1. **The combination rule is the subtractive screen fold** `1 − (1−a)(1−b)`. The decompressed function
   is literally a chain of those over per-colourant coefficient vectors. Per-component-then-fold
   reproduces the whole-space answer exactly. This is how Illustrator/InDesign emit DeviceN transforms,
   so the rule is empirically grounded rather than chosen.
2. **`/Colorants`-derived ramps change nothing on this file.** `BuildTintRamp`'s existing sweep (one
   input, others held at 0) already reproduces `/Colorants/<name>` bit-for-bit, because the transform is
   separable. A `/Colorants` ramp only differs on a deliberately non-separable transform, which
   well-formed producers do not emit — so its test must be synthetic to be non-vacuous.

### 2.6 The corpus gate cannot witness any of this

Verified by inflating every Flate stream in all 51 patches, so compressed object streams are accounted
for:

```
total patch PDFs: 51
_X4.pdf (in the gate): 30
DeviceN-bearing patches: 12   of which INSIDE the X4 gate: 0
NChannel-bearing patches inside the X4 gate: 0
```

`GwgCorpus.DiscoverX4()` enumerates only `*_X4.pdf`. All twelve DeviceN patches — GWG010, GWG020,
GWG030, GWG060, GWG061, GWG080, GWG081, GWG082, GWG190, GWG191, GWG192, GWG230 — are `_x1a`, `_x3` or
`_X1`, and fall outside it.

What the two scoreboards actually assert:

- `GwgX4RenderScoreboardTests` — a **floor**: no exception, coverage in (0.5%, 99.9%). Catches crashes,
  blank pages and slab blow-ups. Does not look at colour. The per-fixture table goes to
  `_out.WriteLine`, which does not appear in default `dotnet test` output.
- `GwgX4FidelityScoreboardTests` — ΔE2000, but only on **flat DeviceCMYK swatches** it can locate. Spot
  and DeviceN output is never measured.

The standing rule "corpus gate mandatory for anything touching `InkDecider` or the colour operators"
has therefore been resting on a gate structurally blind to the arm of `InkDecider` this work concerns.
**Fixing the gate is a prerequisite, not a nicety.**

### 2.7 Blast radius: inverted from the intuitive reading

`ColorSpaceResolver` is `internal class` (`:14`). `PdfLibrary.csproj:62-70` grants `InternalsVisibleTo`
to `Rendering.SkiaSharp`, `Rendering.Wpf.Tests`, `Tests`, `Integration`. **Pellucid is not on the list**
and consumes the engine as the compiled `Lxman.PdfLibrary` package. Exhaustive grep confirms **zero
call sites of any of the twelve members anywhere in Pellucid.** The `public static` keywords on
`OverprintPlatesFor`, `PaintsNothing`, `OriginFor` and others are decorative — the containing class's
`internal` is the gate.

So the 1295-line file is *cheap*: ~25 external production call sites across seven files
(`PdfRenderer`, `ShadingBuilder`, `MeshShadingReader`, `PdfImageToRgba`, `PdfImageToCmyk`, `PdfImage`,
`PageColorantReader`) plus ~10 test files, all single-repo, all freely re-signaturable.

The expensive surface is two things, neither of them the resolver:

1. **`ColorantOrigin`** — genuinely `public`, crosses the package boundary. Read by name in
   `InkDecider.cs` and `CmykPageRenderer.cs`, and **constructed directly in seven Pellucid test files**
   (they cannot obtain one from the resolver, which is invisible to them).
2. **`PdfGraphicsState.ResolvedFillColor` / `ResolvedFillColorSpace`** — `Docs/RendererSpi.md` §4
   (lines 139-181) formalises the `(string name, List<double> components)` shape as versioned public
   renderer SPI, consumed by the documented helper `PdfColorToRgb.ToRgb`.

### 2.8 The Skia path is not a constraint

Neither `PdfLibrary.Rendering.SkiaSharp` (sunset, test-only) nor `Pellucid.Rendering.Skia` (live) does
any colour-space parsing of its own — no positional `csArray[…]` reads, no `/DeviceN`/`/Separation`/
`/Indexed` literal matching. Neither calls `ColorSpaceResolver` directly; the only transitive reach is
the shared `PdfImageToRgba`, which calls `BuildTintToRgb` (`:813`) and `LabWhitePoint`/`LabRange`
(`:754-755`).

`ColorantOrigin` reaches the Skia layer's doorstep — `PdfGraphicsState.ResolvedFillColorantOrigin`,
`ShadingDescriptor.ColorantOrigin` are populated on every command regardless of consumer — and is
**never dereferenced** by Skia code. Reshaping it is a compile-level concern there at most.

Per-component evaluation is additionally a **no-op on the Skia/RGB path**: no planes exist, so no
colourant is available, so every component reverts — and "all revert" is precisely the case the
whole-space tint transform is designed for. G-4 is a CMYK-path change, like the four ⚠️ rows.

---

## 3. Scope

### In

- **S1** — a typed, cached colour-space model, internal to `PdfLibrary`, replacing positional parsing.
- **S2** — `ColorantOrigin` extended *additively* to carry `/Subtype` and per-component data.
- **S4, narrow slice** — availability becomes a three-way disposition instead of a bool, so the ink
  decision is per-component and `CompositeInk` stops re-deriving it.
- **G-4 proper** — per-component routing, `/Process` component mapping, `/Colorants`-derived ramps.
  Closes rows 5-3 and 5-11; repairs row 5-2's hidden hole.
- **Corpus gate** — extend discovery to all 51 patches; add render-hash baselines.

### Out

- **S3** — funnelling images, stencil masks, shading patterns and text through one resolution point.
  This is what G-8, G-9 and G-13 need. It is a separate pass of comparable size, and G-4 does not
  depend on it.
- **G-10, G-11** — the remaining S4 work (`PaintsNothing` scope, Pattern initial colour).
- **`/MixingHints`** — rows 5-13/5-14/5-15 remain class L. Nothing reads the key today and this design
  does not change that.
- **Collapsing `PdfColorToRgb.ToRgb` and the sunset `ColorConverter.ConvertColor` duplication.** Real
  (they are hand-maintained near-duplicates of the same name→RGB switch, including the same
  guess-by-component-count fallback) but confined to a test-only backend. Logged, not fixed here.

---

## 4. Architecture

### 4.1 S1 — typed cached colour-space model

A discriminated model parsed once per colour-space object and cached by object number:

```
ResolvedSpace
  ├─ DeviceSpace(name)
  ├─ CieSpace(kind, whitePoint, range, iccStream)
  ├─ IndexedSpace(Base: ResolvedSpace, HiVal, Lookup)
  ├─ SeparationSpace(Name, Alternate: ResolvedSpace, TintTransform)
  ├─ DeviceNSpace(Names, Alternate, TintTransform, Attributes?)
  └─ PatternSpace(Underlying?)

DeviceNAttributes(Subtype, Colorants: IReadOnlyDictionary<string, SeparationSpace>,
                  Process: ProcessInfo?, MixingHintsRaw)
ProcessInfo(ColorSpace: ResolvedSpace, Components: IReadOnlyList<string>)
```

The twelve existing members become queries over this model rather than re-parses. `/Subtype`,
`/Colorants` and `/Process` are read **here and only here**.

Caching is keyed on the colour-space object's object number, with a fallback to no caching for direct
(non-indirect) objects. The cache holds the parsed `PdfFunction` too, which is what makes G-12's second
`ResolveColorSpace` pass cheap.

**Invariant preserved:** `ResolveColorSpace` still collapses to the documented
`(string colorSpaceName, List<double> color)` shape on the way out. The typed model is internal;
`RendererSpi.md` does not change.

### 4.2 S2 — additive carrier

`ColorantOrigin` keeps its positional constructor and gains init-only optional members:

```csharp
public sealed record ColorantOrigin(
    IReadOnlyList<string> Names,
    IReadOnlyList<double> Tints,
    string AlternateSpace)
{
    public string? Subtype { get; init; }                        // "DeviceN" | "NChannel" | null
    public IReadOnlyList<ColourantComponent>? Components { get; init; }
}

public sealed record ColourantComponent(
    string Name,
    ColourantRole Role,          // Spot | Process | None
    double? Tint,                // null for shadings — no single per-op tint
    IReadOnlyList<double>? OwnAlternateCmyk);   // from /Colorants, or /Process mapping
```

`new ColorantOrigin(names, tints, alt)` keeps compiling, so both Pellucid production consumers and all
seven Pellucid test files are untouched. No lockstep engine-pack-and-repin is needed mid-refactor.

`Components` is populated engine-side, matching the existing precedent — `SpotImageInk`,
`ShadingSpotInk` and `MeshSpotInk` all carry names plus pre-resolved process CMYK. This keeps PDF
function evaluation out of the compositor.

**Rationale for additive over replacement:** `ColorantOrigin`'s three-field shape is a live cross-repo
contract (§2.7). Expanding now and contracting once Pass 2's consumers have migrated costs one extra
pass and removes the only cross-package break in the plan.

### 4.3 S4 narrow — per-component disposition

`InkDecider` gains a per-component answer instead of one `RouteSpots` bool:

```csharp
public enum ComponentDisposition { DirectToPlane, DirectToProcess, ViaAlternate, Discarded }
```

- `DirectToPlane` — spot with a registry plane; composite its raw tint to that plane (today's routed
  behaviour).
- `DirectToProcess` — a process component, whether named by a reserved name or mapped through
  `/Process /Components`; contributes to the process CMYK.
- `ViaAlternate` — no plane available; contribute `OwnAlternateCmyk` folded into the process CMYK by
  the subtractive screen rule of §2.5. **This is the case that is silently dropped today.**
- `Discarded` — `/None`.

**Classification order is load-bearing and must be `/None` first.** A `/None` component has no registry
plane, so a naive availability test would classify it `ViaAlternate` and paint it — inverting row 5-7
("when painting the named device colourants directly, colour components corresponding to None
colourants shall be discarded") and introducing a regression this design exists to prevent. The
disposition is therefore resolved as: `/None` → `Discarded`; else process (reserved name or
`/Process /Components` entry) → `DirectToProcess`; else plane available → `DirectToPlane`; else
→ `ViaAlternate`.

Row 5-9 (an all-`/None` space never reverts) is unaffected: `ColorSpaceResolver.PaintsNothing` short-
circuits such a space upstream, before any disposition is computed. Per-component routing must not
introduce a path that reaches the alternate for an all-`/None` space — this is worth an explicit
regression test, since the new `ViaAlternate` arm is exactly the kind of route that could.

Rows 5-6 and 5-7 remain ⚠️: `Discarded` preserves today's behaviour rather than adding the direct
pixel-level assertions those rows need, which depend on the soft-proof harness.

`InkDecision` carries the per-component dispositions, so `CompositeInk` consumes them rather than
re-looping `Names` and re-querying `TryGetPlane`.

**Plain-DeviceN divergence:** when `Subtype` is absent or `DeviceN` and *any* component would be
`ViaAlternate`, the whole space reverts through the document tint transform — row 5-2's actual
requirement. Per-component routing applies only when `Subtype` is `NChannel`. This is the branch that
repairs 5-2's hole.

### 4.4 Corpus gate

- `GwgCorpus.DiscoverAll()` alongside the existing `DiscoverX4()`; the X-4 floors keep using the latter.
- A render-hash scoreboard over all 51 patches: per fixture, a stable hash of the composited CMYK
  buffer and each spot plane. Baselines committed in-repo.
- Hash, not quantised statistics: a behaviour-neutral pass should fail on *any* difference. If AA or
  machine variance proves this flaky, downgrade to per-plane quantised means — but start strict and
  relax on evidence. These tests are already `LocalOnly` with `SkipWhen`, so flakiness costs one
  developer's console, not CI.

---

## 5. Pass structure and gates

| Pass | Work | Gate |
|---|---|---|
| **0** | `DiscoverAll`; render-hash scoreboard; baselines committed | Baselines exist; all 12 DeviceN patches visible; existing X-4 floors still green |
| **1** | S1 typed cached model; S2 additive carrier reading `/Subtype`, `/Colorants`, `/Process`. **Nothing consumes the new fields.** | Hashes **byte-identical**; 2493 engine + 1268 Pellucid green |
| **2** | G-4 — per-component dispositions, `/Process` mapping, `/Colorants` ramps | Every hash change is on a DeviceN-bearing patch, **and each one is individually explained**; no non-DeviceN patch moves; rows 5-3, 5-11 close; 5-2 repaired |
| **3** | Contract — drop `ColorantOrigin.Names`/`Tints` once `Components` has fully replaced them | Compile + green |

Pass 2's gate is the one this whole exercise buys: "no non-DeviceN patch moved, and here is why each
DeviceN one did" is a precise, falsifiable claim about blast radius that is currently impossible to
make.

Note the gate is deliberately **not** "exactly twelve hashes change." Several of the twelve — GWG030
(gray/black overprint), GWG230 (four grays), GWG010 — merely *contain* a DeviceN space without
exercising the mixed or NChannel paths, so their hashes should stay put. Asserting a count would
convert a correct no-op into a failure. The assertion is directional: non-DeviceN patches must not
move, and every patch that does move must have a stated reason.

Passes 0 and 1 are separated deliberately. Landing S1 while the gate cannot see DeviceN would mean
asserting behaviour-neutrality on evidence that structurally excludes the affected files.

**Each pass gets its own implementation plan.** They are independently shippable, have different blast
radii and different repos in play (Pass 0 is Pellucid-only; Pass 1 is engine-only; Pass 2 spans both;
Pass 3 spans both), and Pass 2's design depends on what Pass 1 actually lands. Writing one plan across
all four would force guesses about Pass 2 interfaces before Pass 1 exists. Plan Pass 0 and Pass 1 now;
plan Pass 2 after Pass 1 merges.

---

## 6. Test strategy

### 6.1 Standing rules carried forward

- A ✅ requires a test **seen to fail**. If it pins already-correct behaviour, mutation-verify then
  revert.
- Backdrop and prior colour in pixel tests must differ from the correct answer **and** from plausible
  wrong answers. Three vacuous tests were caught this way in the previous pass.
- Painted-output claims are asserted on rendered **pixels**, not resolver return values.

### 6.2 Fixtures that must be built

The corpus cannot witness Pass 2's core behaviour — the mixed case never arises in it, and GWG081 is
separable. Three synthetic fixtures are required, and each must be shown to fail before its fix:

1. **Partial-registry NChannel** — an NChannel space naming one registered spot and one unregistered
   spot. Today the unregistered one is dropped; the test asserts its `/Colorants` alternate is folded
   into the process plates. Drives the row 5-3 spot half.
2. **Partial-registry plain DeviceN** — the same shape with `/Subtype` absent. Today it routes and
   drops; the test asserts the whole space reverts through the document transform. This is the row 5-2
   repair, and it must fail against current code.
3. **Non-separable NChannel transform** — a `/Colorants` entry that provably disagrees with the
   whole-space sweep. Without this, the `/Colorants`-ramp change is unfalsifiable (§2.5). The fixture's
   construction should record the measured divergence so a future reader does not have to re-derive it.

A fourth is needed for `/Process` mapping: an NChannel with non-reserved process names
(`/ProcessRed`, `/ProcessGreen`, `/ProcessBlue` per the spec's EXAMPLE 7) mapped via
`/Process /Components`. Today those match none of the four literal names and are dropped.

### 6.3 Pre-existing coverage debt found during the census

Relevant because Pass 1 refactors these members and would be gated on integration coverage alone:

- `LabRange` — **zero** test coverage of any kind.
- `BuildTintToCmyk`, `InitialColorFor`, `PaintsNothing(string,…)`, `OriginForColorSpaceObject` — no
  direct tests; exercised only through callers.

Pass 1 should add direct tests for these before moving them, not after.

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| Render hashes flaky across AA/machine | Tests are `LocalOnly`/`SkipWhen`; documented fallback to quantised per-plane statistics |
| S1 cache returns a stale space for a mutated document | Key on object number; no caching for direct objects; invalidate per document, not globally |
| Pass 1 changes throughput, not just correctness | G-12 notes `cs`/`CS` currently costs ~2×; the cache likely *reduces* it. Gate is byte-identical **output**, not identical work. Measure rather than claim. |
| `ColorantOrigin` additive growth perpetuates the lossy shape | Pass 3 contracts it once consumers migrate; scheduled, not aspirational |
| Fixture 3 encodes our own fold rule as the oracle | The oracle is the `/Colorants` Separation evaluated independently, not our fold — the fold is what is under test |
| Corpus hashes churn for unrelated engine changes | Accepted: that is the gate working. Baselines are regenerated deliberately, with the diff reviewed |

---

## 8. Conformance matrix impact

On completion of Pass 2:

- **5-3** ❌ → ✅ — per-component evaluation, spot and process halves, pinned by fixtures 1 and 4.
- **5-11** ❌ → ✅ — `/Subtype` read on the render path and dispatched on.
- **5-2** ✅ → ✅ *(repaired)* — the note must record that the previous ✅ covered the engine path only,
  and that the routed CMYK path did not revert. Fixture 2 closes it. **This is a correction to a
  previously claimed row and must be called out explicitly in the matrix, not silently upgraded.**
- **G-4** — closed.
- **New gap G-14** — `PdfColorToRgb.ToRgb` / `ColorConverter.ConvertColor` duplication (§3, Out).
- **New gap G-15** — corpus gate covered 30 of 51 patches and zero DeviceN files until Pass 0; record
  the measurement so the blind spot is not re-introduced.
- **G-12** — expected to close or shrink as a side effect of S1's cache; verify by measurement before
  claiming it.

Rows 5-6, 5-7 and 5-10 remain ⚠️ — they need the soft-proof harness, which is out of scope here.

---

## 9. Open questions

None blocking. Two judgement calls made in-spec and flagged for review:

1. **Additive vs replacement for `ColorantOrigin`** (§4.2) — chosen additive to avoid a cross-package
   break mid-refactor, at the cost of a fourth pass.
2. **Hash vs quantised statistics for the gate** (§4.4) — chosen hash, with a documented downgrade path.
