# Colour Pass 2 — NChannel Per-Component Evaluation (2a′ + 2b)

**Date:** 2026-07-26
**Matrix:** `Docs/colour/rendering-conformance.md`, rows 5-3, 5-10, 5-11
**Gap:** G-4
**Predecessor:** Pass 2a, merged as `b4b9634` — the engine now *carries* per-component data. This design
makes it *correct* and makes the compositor *consume* it.
**Repos:** `C:\Users\jorda\RiderProjects\PDF` (2a′) and `C:\Users\jorda\RiderProjects\Pellucid` (2b).

---

## The clause

> **Row 5-3, ISO 32000-2 §8.6.6.5:** "For NChannel colour spaces, the components shall be evaluated
> individually; that is, only the ones not present on the output device shall use the alternate colour
> space of that component."

Today one unregistered colourant flattens *every* colourant through the whole-space alternate, including
the ones we can paint. The gate that does this appears four times in `CmykPageRenderer` — fills/strokes
(`:358`), shading (`:560`), mesh (`:755`), images (`:1157`) — the last three literally
`routeSpots = false; break;`.

## What the measurement changed

This design was drafted twice. The first version scoped Pass 2b to fills and strokes and proposed
validating the combining rule against a Ghostscript oracle. **A corpus census killed both ideas**, and
the census is recorded here because the conclusions are only as good as it is.

Method: grep is unreliable on the GWG corpus (47 of 51 patches use compressed object streams), so the
inventory was taken with the parser — walking page `/ColorSpace`, `/Shading`, `/Pattern`, and image
XObjects' own `/ColorSpace`, unwrapping `/Indexed` to its base. A first pass that walked only the
`/ColorSpace` resource dictionary reported **zero** NChannel spaces and was wrong; both real instances
hide elsewhere. That scaffold was deleted after use.

| Corpus | Files | NChannel spaces | Where they live |
|---|---|---|---|
| GWG (feeds the render-hash gate) | 51 | 1 file (GWG081), 2 spaces | an axial **shading**, and an **image** behind `/Indexed` |
| veraPDF (feeds validator parity) | 2907 | 3 test files | incl. a page `/ColorSpace` resource — a real **fill** |

Three consequences:

1. **There is no NChannel fill or stroke anywhere in the GWG corpus.** A fills/strokes-only Pass 2b
   would have produced zero digest movement and therefore no evidence at all.
2. **GWG081's renderable-with-evidence instance is the image**, which needs a per-component tint *ramp*
   — Pass 2a carries only a single per-op quad. So ramp work is required, not optional.
3. **The combining rule cannot be measured on GWG** — there is no NChannel fill there to measure. The
   proposed oracle experiment would have been theatre. It is resolved by derivation instead (below).

### The fixture that drives the design

`veraPDF test suite 6-2-4-4-t02-pass-a.pdf` exercises every part of this design at once:

```
/CS0 [/DeviceN [/Black /PrCyan /PrMagenta /PrYellow] /DeviceCMYK 14 0 R 15 0 R]
15 0 obj << /Subtype /NChannel /Colorants 19 0 R /Process 26 0 R >>
19 0 obj << /Black     [/Separation /Black     /DeviceCMYK 22 0 R]
            /PrCyan    [/Separation /PrCyan    /DeviceCMYK 23 0 R]
            /PrMagenta [/Separation /PrMagenta /DeviceCMYK 24 0 R]
            /PrYellow  [/Separation /PrYellow  /DeviceCMYK 25 0 R] >>
26 0 obj << /ColorSpace [/ICCBased 7 0 R] /Components [/PrCyan /PrMagenta /PrYellow /Black] >>
 7 0 obj << /Filter /FlateDecode /N 4 /Length 384790 >>
14 0 obj << /FunctionType 4 /Domain [0 1 0 1 0 1 0 1] /Range [0 1 0 1 0 1 0 1] >> stream {}

content:  q /CS0 cs  0.0 0.36 0.57 0.02 scn  … f  Q
```

- **Positional channel identity is load-bearing.** `/Black` is at space position 0 but process channel 3.
  Routing by position transposes the colour visibly.
- **Non-reserved process names.** `PrCyan`/`PrMagenta`/`PrYellow` match nothing by name — the harm
  finding I-1 identified, on a real file.
- **An ICCBased CMYK process space** (`/N 4`) — the case Pass 2a deliberately deferred, which makes the
  fixture invisible today.
- **A degenerate whole-space transform** (`{}`), so the whole-space fallback renders garbage.

**What this fixture does *not* prove.** All four components appear in `/Components`, so all four are
**Process**, and Table 71's "any such definition shall be ignored if the colorant is also present in the
process dictionary" means its four `/Colorants` entries are deliberately unused. So `t02-pass-a`
exercises `ProcessChannel` routing and ICCBased acceptance — **not** spot reversion and **not** the
per-component ramps.

**Reversion has no corpus instance anywhere.** GWG081's image is `[Black(Process), GWG Green(Spot)]` and
GWG Green *is* registered, so nothing reverts there either. The reversion path is therefore covered by
synthetic fixtures plus the plane-cap invariance property test, and this design does not pretend
otherwise.

---

## Scope

**In:**
- **Pass 2a′ (engine):** accept an ICCBased CMYK/Gray `/Process /ColorSpace`; build per-component ramps
  from `/Attributes /Colorants`; teach the page colorant inventory the `/Process /Components` rule so it
  stops classifying a named process colorant as a spot.
- **Pass 2b (compositor):** per-component evaluation for **fills/strokes and images**; process
  components routed by `ProcessChannel` rather than by name; a new render-hash gate over the three
  veraPDF NChannel files.

**Out, deliberately:**
- **Shadings and meshes** — they resolve with `rawColor: null`, so there is no per-op tint. GWG081's
  NChannel shading is in this bucket. Unchanged; that is **G-7**.
- **`/CalGray` as a process space.** CIE-based, not a device space; `InkDecider.ToCmyk` already treats it
  differently from `/DeviceGray` for that reason. Stays suppressed. **New gap.**
- **ICC-based colour conversion.** `/N` is read for its channel count alone. Mapping an ICCBased CMYK
  process space's components through the profile is a colour-management question, not a plate-identity
  one.
- **Caching built tint transforms.** Still deferred; see Cost.

---

## Pass 2a′ — engine

### 1. `ProcessSpaceName` → `ProcessChannelCount`

Returning a family-name string is what forces the ICCBased rejection. Return the channel count instead —
the question the caller actually has. This also absorbs the `processIsCmyk` bool added in Pass 2a's fix
round.

```csharp
/// <returns>The number of channels in the NChannel space's process colour space, or null when this
/// engine cannot say — in which case BuildComponents suppresses the whole component list.</returns>
private static int? ProcessChannelCount(PdfDictionary process, PdfDocument? doc)
```

| `/Process /ColorSpace` | Result | Note |
|---|---|---|
| absent | `4` | "no constraint" — preserves today's `""` behaviour exactly |
| present but unreadable / odd shape | `4` | same; degrade rather than reject |
| `/DeviceCMYK` | `4` | |
| `/DeviceGray` | `1` | |
| `[/ICCBased s]` → stream with `/N 4` | `4` | **new** |
| `[/ICCBased s]` → stream with `/N 1` | `1` | **new** |
| `[/ICCBased s]` → `/N` 3, absent, or non-numeric | `null` | suppress — we genuinely do not know |
| `[/ICCBased s]` → `s` unresolvable or not a stream | `null` | suppress — preserves today exactly |
| `[/ICCBased]` with `Count < 2` | `null` | suppress |
| any other name (`/DeviceRGB`, `/Lab`, `/CalGray`, …) | `null` | suppress — unchanged |

`ProcessChannelFor`'s reserved-name rule becomes "canonical index only when the count is 4", replacing
the `processIsCmyk` bool. Every other Pass 2a rule is unchanged: listed index wins over canonical, first
index wins on a duplicate, an index at or beyond the count yields null.

**Guard — designed in, not reviewed in.** Axis A is the table above; every row is a required test. Axis B
is the recurring defect: `Deref(a[1], doc)` resolves the ICC **stream**, an object no path previously
touched here. It sits inside `BuildComponents`'s existing `try`/`catch (Exception)` — *and that is not
evidence*. Pass 2a's Task 2 deref was inside a `try` too and still needed its own test at its own level.
Required: a corrupt indirect ICC reference that **errors if the guard is removed**, built with the
in-use-xref-entry-with-an-unparseable-body technique the three existing guard tests use (a reference to a
merely non-existent object returns null without throwing and would make the test vacuous).

**Two constraints that belong in the code as comments**, because a well-meaning edit would undo both:

> Read `PdfStream.Dictionary` only — never `.Data` or `GetDecodedData()`. The conformance fixture's
> profile is 384 KB of Flate and this runs on every colour-setting operator.

Verified safe: `Dictionary` is a plain `{ get; }`, separate from `Data`/`GetDecodedData`; and
`PdfDocument.GetObject` calls `AddObject(...)` after an on-demand load, so the stream object is parsed
once per document and later operators hit the cache.

### 2. Per-component ramps from `/Colorants`

`BuildTintRamp` answers "what does component *i* look like alone?" by zeroing the others and evaluating
the **whole-space** transform. For an NChannel space the file states the answer outright: Table 71 defines
`/Attributes /Colorants /<name>` as a Separation describing "the appearance of that colorant alone".

```
For an NChannel space, build component i's ramp from its /Colorants Separation, sampled 0..1
over 256 steps. Fall back to today's isolated-component evaluation when there is no usable entry.
```

This is the ramp-shaped twin of Pass 2a's `OwnAlternateCmyk`, and it makes the plane-cap invariance below
real, because both paths then read the same function. It flows to the compositor through the existing
`PageColorant.TintRamp` → `SpotColorantRegistry` pipeline: **page-level, built once, no per-operator
cost.**

Fixture effect: `t02-pass-a`'s whole-space transform is `{}`, so today every component's ramp is garbage;
after this change all four come from their own Separations.

**Recorded fragility.** `PageColorantReader` dedupes by name (`if (!seen.Add(name)) continue;`) and
GWG Green appears in several spaces in GWG081, so which space supplies its ramp today depends on
resource-walk order. Harmless while every space agrees; this change is what can make them disagree. Not
fixed here — recorded so the ramp difference is attributed correctly if a digest moves.

---

## Pass 2b — compositor

### Per-component rules

| Component | Action |
|---|---|
| `Role == None` | Discard. No `/Colorants` lookup (row 5-7). |
| `Role == Process`, `ProcessChannel` known | Paint its tint on that plate. |
| `Role == Spot`, plane registered | Route to the plane (existing mechanism). |
| `Role == Spot`, no plane, `OwnAlternateCmyk` present | Add its alternate into the process buffer. |
| `Role == Process`, `ProcessChannel` null | **Unplaceable** → whole-space fallback. |
| `Role == Spot`, no plane, `OwnAlternateCmyk` null | **Unplaceable** → whole-space fallback. |

### The governing principle

> **Per-component evaluation is attempted only if every component can be placed.** One unplaceable
> component falls the whole operation back to today's whole-space flatten.

Borrowed verbatim from SP-6c's posture — *"degrades to the status quo rather than losing ink"*. Never
silently drop a component: that is the I-1 harm, and dropping is strictly worse than the status quo.

### Combining — derived, not chosen

`SpotDisplayCombiner` already defines how spot ink meets process ink, shipped and validated in SP-2:

```
combined_CMYK = clamp(process + Σ_enabled ramp_s(spot_tint))
```

**Additive with clamp.** A reverted spot's alternate CMYK is exactly what `registry.SpotToCmyk(plane,
tint)` would have contributed had the colorant had a plane; reverting folds it in earlier. Same
arithmetic, different stage — so this is not a new rule, and needed no oracle experiment to pick.

It yields a testable property:

> **Plane-cap invariance** — a spot component renders to the same combined CMYK whether it rides a plane
> or reverts to its alternate. Crossing the 16-plane cap changes memory, not pixels.

### `PageColorant.Classify` disagrees with `ColourantRole` — and must not

`PageColorant.Classify` is **name-only**: `Cyan`/`Magenta`/`Yellow`/`Black` → Process, everything else →
Spot. `BuildComponents` classifies by name **and** `/Process /Components`. On `t02-pass-a` the two
disagree about the same colorant:

| Colorant | `PageColorant.Classify` (page inventory) | `ColourantRole` (per-op) |
|---|---|---|
| `PrCyan`, `PrMagenta`, `PrYellow` | **Spot** → each gets a plane in `SpotColorantRegistry` | **Process** → channels 0, 1, 2 |

Left alone, Pass 2b would route `PrCyan` to plate C *and* the registry would still hold a spot plane for
it — three of the sixteen planes consumed by colorants that are not spots, and a colorant liable to be
painted twice. `AnyRegistered` would also report true for a space with no spots in it at all.

**Pass 2b must reconcile these.** The page inventory has to learn the same `/Components` rule the per-op
path already knows, so a name listed in a process dictionary is not registered as a spot plane. Note
`Classify` is `internal` and the all-`/None` slice explicitly listed "making `PageColorant.Classify`
public" as out of scope — so this is engine-side work in Pass 2a′, not a compositor-side patch.

### The paint mask widens

If a reverted spot's alternate puts ink on cyan, plate C is now marked and must be painted, or the ink is
computed and then masked away under overprint. The mask becomes the union of the process components'
channels and the plates the reverted alternates actually touch.

---

## Verification

**CORRECTED (whole-branch review, post-merge):** the GWG/veraPDF gates below prove *corpus* silence
only — they are not evidence that Pass 2a′ has no live consumer, and they must not be read that way.
`PageColorant.Kind`/`TintRamp` already feed Pellucid's shipped `SpotColorantRegistry`/`CorpusRenderHash`
before this design's compositor half (Pass 2b) exists. The corpus stays silent because none of its 51
GWG fixtures happens to exercise the three input shapes Pass 2a′ classifies or ramps differently — see
Gaps for the shapes and why a future moved digest outside GWG081 is this surfacing on new input, not
necessarily a defect.

**Ghostscript is not the oracle.** The project's standing constraint says so, and diff-vs-gs can rise
precisely when Pellucid improves. Evidence is derived from the files.

**Sources of truth, strongest first:**

1. **Derived from the file.** `t02-pass-a` fills `0.0 0.36 0.57 0.02 scn` over
   `[Black PrCyan PrMagenta PrYellow]` with `/Components [PrCyan PrMagenta PrYellow Black]`. All four are
   Process, so each tint goes **directly** to its plate (§8.6.6.4 — "a named process colorant maps to the
   device colorant"; the routed arm already does this and does not consult `/Colorants`). Expected:
   **C=0.36, M=0.57, Y=0.02, K=0.0**. Derived from the file, in the same discipline as Pass 2a deriving
   `0.5` from `C1 [0.5 0 1 0]`. Transposition is caught here: route by space position instead of channel
   and Black's 0.0 lands on cyan while 0.36 lands on magenta.
2. **Plane-cap invariance as a property test.** Render with the spot registered, then with the cap forced
   low so it must revert; combined CMYK must match. No external oracle; pins the combining rule directly.
3. **gs `tiffsep` as cross-check only** — a sanity reference, never the arbiter. Divergence is
   investigated, not chased.

**Gates.** Both mirror the GWG gate's proven shape, including its vacuous-pass guards and its walk-up
corpus discovery — so neither corpus is copied into a repo and provenance stays where it is.

```
GwgRenderHashGateTests        51 fixtures — existing baseline
NChannelRenderHashGateTests    3 fixtures — new baseline
```

**Falsifiable predictions, stated before the work:**

- **ICCBased acceptance moves nothing in GWG.** GWG081's process dictionaries are
  `<</ColorSpace/DeviceCMYK/Components[/Cyan/Magenta/Yellow/Black]>>` (read from the bytes). Zero GWG
  digests may move from that change.
- **The ramp change may move GWG081 alone.** Whether object 14 differs from zeroing Cyan in the
  whole-space transform is a **measurement, not an assumption** — compute both and compare before
  regenerating any baseline. All 50 other files must not move.
- **A moved digest outside GWG081 is a defect**, not an expected result.

**Task 0, before any gate machinery:** confirm all three veraPDF files load and render in Pellucid at
all. If one does not, the gate around it is moot and scope shrinks to what does. This risk is not
retired.

---

## Delivery — CORRECTED (Pass 2b planning, 2026-07-26): THREE plans, not two

**The section below is preserved as written, and its premise about Pass 2b is wrong.** It scopes Pass 2b
to "the compositor", one plan, in the Pellucid repo. Reading the code this design names showed two things
that are not so:

1. **The image path has no `ColorantOrigin`.** This design cites `CmykPageRenderer.cs:1157` as the images'
   all-or-nothing gate. It is — but what it gates is `registry.TryGetPlane(ink.Names[k])` over a
   `SpotImageInk`, which carries *spot names + tint planes + a pre-split `ProcessCmyk` plane*. The
   `ColourantComponent` list never reaches that site. The colorant→plate/plane decision for images is made
   **engine-side**, in `PdfImageToCmyk.TryToSpotInk` and `StencilInkFromFill`, both splitting by
   `PageColorant.Classify` + `ProcessPlate` — the same literal reserved-name switch this design flags at
   `InkDecider.ProcessContribution`. So the images half of Pass 2b is an **engine** change.
2. **`ProcessChannel` is not safe to consume alone.** It indexes the *process colour space's* channels.
   Under a `/DeviceGray` process space a name listed in `/Process /Components` also gets index **0** —
   measured directly, byte-identical to what `/Cyan` gets under a four-channel space. A consumer mapping
   channel 0 → the cyan plate would paint a gray colorant on cyan. Nothing carried today distinguishes
   them, so `ColorantOrigin` needed a `ProcessChannelCount`.

**Actual delivery:** Pass 2a′ (engine, merged `0c0f3db`) → **Pass 2b-engine** (`ProcessChannelCount` +
the image/stencil per-component split; plan
`Docs/superpowers/plans/2026-07-26-colour-pass2b-engine-nchannel-image-split.md`) → **Pass 2b-compositor**
(per-component fills/strokes + `NChannelRenderHashGateTests`), written after 2b-engine merges because its
measurements are that plan's inputs.

**Two further corrections to this design's own claims**, both measured:

- **"GWG081's renderable-with-evidence instance is the image"** (Scope, consequence 2) does **not** hold
  for Pass 2b. GWG081's image colorants split *identically* under the name rule and the per-component
  rule, so it yields no evidence for the image change either. Combined with Pass 2a′'s Task 0 measuring
  the ramp difference at 5.55e-17, GWG081 has now supplied **zero** evidence across both passes.
- **There is no NChannel fill, stroke *or stencil* anywhere in GWG** — zero NChannel spaces appear in any
  page `/ColorSpace` resource across all 51 fixtures. Stronger than this design's census recorded, and it
  means `StencilInkFromFill`'s half is covered by synthetic fixtures alone.

**Per-component rules table, addendum:** consuming `ProcessChannel` as a CMYK plate index requires
`ProcessChannelCount == 4`. At any other count the component is not placeable and the op falls back whole.
And an all-process NChannel op is deliberately *not* per-component-evaluated — see the conformance
matrix's G-4 note for why the overprint category, not the colour, decides that.

## Delivery — two plans, not one *(original text, superseded above)*

This design spawns **two implementation plans**, matching the repo boundary and giving each its own gate:

1. **Pass 2a′ (engine).** `ProcessChannelCount`, per-component ramps, and the `Classify` reconciliation.
   Its gate: the GWG baseline moves **only** on GWG081, and only for a reason measured in advance.
2. **Pass 2b (compositor).** Per-component evaluation for fills/strokes and images, plus
   `NChannelRenderHashGateTests`. Its gate: the three veraPDF digests land on values derived from the
   files, and GWG stays put.

Splitting matches Pass 2a's shape — engine work landing behind a "*corpus* output unchanged (or
explained)" gate before any **new** consumer exists — which is what made Pass 2a's regressions cheap to
localise. **CORRECTED (whole-branch review, post-merge):** unlike Pass 2a (which touched
`ColorantOrigin`, genuinely unconsumed at the time), Pass 2a′'s `Kind`/`TintRamp` changes reach the
already-shipped `SpotColorantRegistry`/`CorpusRenderHash` immediately on merge — the gate proves the GWG
corpus doesn't move, not that nothing downstream does. Pass 2a′ ships and merges before Pass 2b is
planned in detail, because 2a′'s measurements (does object 14 differ from the isolated evaluation? do
the three files even render?) are inputs to 2b's plan.

## Cost

`ProcessChannelCount` adds one cached dictionary lookup per colour-setting operator on NChannel spaces
only. The ramp work is page-level and adds nothing per operator.

Pass 2a's corrected cost note still stands and is unchanged by this design: `PdfRenderer.OnColorChanged`
recomputes both fill and stroke origins on every colour operator regardless of side; `PdfFunction.Create`
has no cache; `BuildTintToCmyk` re-parses the `/Colorants` entry twice per component per operator. **No
cache is added here** — that decision stands.

---

## Gaps

**Closing:** G-4 for fills/strokes and images. Rows 5-3 and 5-11 close for those paths; row 5-10's
warning narrows.

**Remaining or opened:**
- **G-7** — shadings and meshes have no per-op tint. GWG081's NChannel shading sits here.
- **New** — `/CalGray` as an NChannel process space stays suppressed.
- **New** — ICCBased process spaces are accepted for *plate identity* only; no colour conversion through
  the profile.
- **New** — `OwnColorantRamp`'s gate is narrowed to a `DeviceCMYK` own-alternate only (Minor 1,
  whole-branch review); a `DeviceGray`-alternate NChannel space keeps the isolated whole-space evaluation
  even with a usable `/Colorants` entry, rather than being promoted from a 1-component ramp to 4. This is
  the conservative direction (mirrors the existing Lab exclusion's reasoning) — widening it back to
  include Gray is a deliberate, separately-gated future change, not something this design signs off on.
- **G-12** — no caching of built tint transforms.
- **New (whole-branch review, post-merge correction) — three input classes move a live consumer that the
  GWG/veraPDF corpora do not exercise, so the corpus render-hash gate is silent about them by absence,
  not by proof of no effect:**
  1. **ICCBased process space + non-reserved process names** (e.g. `/PrCyan`). Before Pass 2a′:
     `PageColorant.Classify` said Spot → `SpotColorantRegistry.Build` gave it a plane →
     `AnyRegistered` true → the routed arm painted it (imperfectly — by name, not by channel). After:
     `KindFor` says Process → zero planes → `AnyRegistered` false → flattened. **Interim
     routed→flattened window:** until Pass 2b lands `ProcessChannel`-based routing, `InkDecider.
     ProcessContribution` still switches on the literal reserved names `Cyan`/`Magenta`/`Yellow`/`Black`,
     so a non-reserved process colorant's tint reaches **neither** a plate **nor** a plane in this
     window — it survives only inside the flattened whole-space alternate. This is expected and
     temporary, not a regression to chase before Pass 2b ships.
  2. **NChannel with a non-separable whole-space tint transform and a usable `/Colorants` entry** — the
     per-component ramp (Task 2) now differs from the whole-space approximation by construction, so
     `SpotColorantRegistry.BuildCmykRamp` bakes different plane ink for that colorant.
  3. **NChannel with a `DeviceGray` alternate and a usable `/Colorants` entry** *before this correction's
     Minor-1 narrowing* — the ramp would go from 1 component to 4, flipping
     `SpotColorantRegistry.BuildCmykRamp` from its solid-scaled branch to its true per-tint branch. Minor
     1 (above) closes this specific instance by excluding Gray from `OwnColorantRamp`'s gate; recorded
     here because the same shape (own-alternate CMYK-family gating) could reopen it if the gate is ever
     widened without a matching test.

  None of the three has a corpus instance today — measured, not assumed, the same discipline Task 0 used
  for the GWG census. A future moved render-hash digest matching one of these shapes is this change
  surfacing on new input, not a mystery regression; see `Docs/colour/rendering-conformance.md`, G-4.

## Success criteria

- Row 5-3 closes for fills/strokes and images; row 5-11 closes for those paths; the matrix records the
  shading exclusion **and** the fact that reversion has no corpus instance, rather than a clean tick.
- `t02-pass-a` renders C=0.36, M=0.57, Y=0.02, K=0.0 — derived from the file, not from a debugger — and
  `PrCyan`/`PrMagenta`/`PrYellow` no longer occupy spot planes.
- Plane-cap invariance holds as a committed property test.
- Every new guard has been observed to fail — by mutation — when its guard is removed.
- GWG gate: no digest moves except possibly GWG081, and any GWG081 movement is explained by a measured
  ramp difference before any baseline is regenerated.
- Both consumers repinned to the new engine build; no repo left measuring a stale package.
