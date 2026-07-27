# Colour Pass 2b-compositor — NChannel per-component evaluation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax
> for tracking.

**Goal:** Make the compositor evaluate an NChannel space's components individually (ISO 32000-2 §8.6.6.5)
for fills and strokes — routing process components by `ProcessChannel` instead of by name, and reverting
unregistered spots through their own alternate — closing the window Pass 2a′ recorded and Pass 2b-engine
half-closed.

**Architecture:** One new branch in `InkDecider.Decide`, ahead of the existing routed arm, gated on an
NChannel origin over a four-channel process space. It returns the process ink, the paint mask, and a new
per-component `SpotRoutes` list; `CmykPageRenderer.CompositeInk` gains a matching branch that paints those
routes instead of the name-derived loop. All-or-nothing: one unplaceable component falls the whole op back
to today's behaviour.

**Tech Stack:** C# / .NET 10, xUnit (`Pellucid.Rendering.Avalonia.Tests`), local NuGet engine pin.

---

## Repo, base, and the engine this depends on

- **Repo:** `C:\Users\jorda\RiderProjects\Pellucid`. Base: `main` @ `b0b2447`, clean apart from a
  pre-existing untracked `website/` — **leave that alone, it is not ours**.
- Branch: `colour/pass2b-compositor-nchannel-per-component`.
- **Engine pin already in place:** `2.5.1-dev20260727160451` (built from PDF master `fef2e7b`, Pass
  2b-engine merged). It carries `ColorantOrigin.ProcessChannelCount`, which this plan consumes.
  `Directory.Build.props.local` also pins Skia `0.1.1-dev20260717153208` — **if you re-pack the engine for
  any reason, that line is silently deleted and must be restored by hand** (eight occurrences on record).
- Entering baselines: Pellucid **1278 passing / 0 failing / 78 skipped**; engine 2643/0.
  The 78 skipped are `Pellucid.Print.Cups.Tests` (39 × net8 + 39 × net10), which is **not** in the default
  `dotnet test` set — run it by full path if you need to see it.

## What Pass 2b-engine already did, so this plan does not redo it

`PdfImageToCmyk.TryToSpotInk`/`StencilInkFromFill` now split an NChannel space's colorants by role and
channel. **Images and stencils therefore need no compositor change at all** — a non-reserved process
colorant already arrives on the correct plate inside `SpotImageInk.ProcessCmyk`, and only genuine spots
reach `SpotImageInk.Names`. The design listed "images" in Pass 2b's scope; that half is done, engine-side.
**Do not add an image path here.** What remains is fills and strokes.

## Scope

**In:** fills and strokes, via `InkDecider.Decide` + `CmykPageRenderer.CompositeInk`.

**Out, and staying out:**
- **Shadings and meshes** — they resolve with `rawColor: null`, so every component's `Tint` is null and
  there is no per-op tint to place. Unchanged. **G-7.**
- **NChannel over a one-channel process space** — `ProcessChannel` indexes the *process* space, so channel
  0 there is not the cyan plate. Falls back whole. Gap opened by Pass 2b-engine, unchanged here.
- **Other-spot knockout on the per-component path** — the existing routed arm does not knock out the spot
  planes an op does not name, and this branch matches it. Deferral, not a decision.
- **ICC colour conversion through an ICCBased process space.** `/N` is a channel count, nothing more.

## Task 0 result — measured 2026-07-27, before any production line was written

All four predictions **HELD**; no stop condition fired.

- **M1** — all three veraPDF files render through `CmykPageRenderer` without throwing, 1 page each,
  1224 × 1584 at scale 2.0. `SpotCount`: `t02-pass-a` **0**, `t03-fail-c` 4 (`Red, Green, Blue, Gray`),
  `t03-fail-d` 3 (`Red, Green, Blue`). All three record the same 7-command draw list.
- **M2 — what `t02-pass-a` paints today: `C=0, M=0.36, Y=0.57, K=0.02`** on 18,400 pixels (first at
  x=120, y=144); every other pixel is `(0,0,0,0)`; zero non-zero spot samples.
  **The mechanism is sharper than this plan originally assumed and it changes how the fix must be tested.**
  Object 14 is `/FunctionType 4` with body `{}` over `/Domain [0 1 ×4] /Range [0 1 ×4]` — an **identity
  4-in/4-out pass-through**, not a transform that returns junk. So today's output is the tint vector taken
  verbatim in **names order** `[Black, PrCyan, PrMagenta, PrYellow]`. **The entire defect is a channel
  permutation.**
- **M3 (load-bearing)** — the carrier survives the recorder onto `PdfGraphicsState`, field for field:
  `Subtype=NChannel`, `ProcessChannelCount=4`, components `Black`/Process/0.0/**ch 3**,
  `PrCyan`/Process/0.36/**ch 0**, `PrMagenta`/Process/0.57/**ch 1**, `PrYellow`/Process/0.02/**ch 2**,
  all four `OwnAlternateCmyk` **null** (object 19 *does* define all four as full `/Separation` spaces —
  Table 71 suppression confirmed at the recorder as well as the resolver). **Task 1 can work as designed.**
- **M4** — NChannel in GWG page `/ColorSpace` resources: **zero**, measured two independent ways.
  A draw-list scan of all 51 fixtures found 18,503 fill/stroke colorant origins, subtype histogram
  `DeviceN × 18,503`. A raw-byte + inflated-stream grep (1,021 streams inflated) found **2** occurrences
  of `NChannel` in the whole corpus, both in GWG081: obj 54, referenced only by a `/ShadingType 2`; and
  obj 62, referenced only through `/Indexed` by an image XObject. **No GWG fill or stroke can reach the
  new branch.**

### ⚠️ The permutation hazard — read before writing ANY value assertion

Before: `(C, M, Y, K) = (0, 0.36, 0.57, 0.02)`. After: `(0.36, 0.57, 0.02, 0)`.

**The multiset is identical. The sum is identical (0.95). The max is identical. Total ink is identical.**

So every one of these assertions **passes before and after** and proves nothing:

- "total ink is 0.95" / "sum of plates" / "average ink"
- "some plate equals 0.36"
- any ΔE or sRGB tolerance against a flat swatch, if the tolerance is loose enough to admit a permutation
- `Assert.Contains(0.36f, plates)`

**Every value assertion must be positional — all four plates, each named.** And every mutation must be
observed against *that* assertion. This is the "a mutation is only as good as the fixture it runs against"
hazard from Global Constraints, arriving in the plan's own driving fixture.

### Two more Task 0 corrections

- **`t02-pass-a` has THREE NChannel `FillCommand`s, not one** (draw list `Save, Fill, Restore, Save, Fill,
  Fill, Restore`). Command [1] carries the tints above; commands [4] and [5] carry the **same origin with
  all tints 0**, so they enter the new branch and paint `(0,0,0,0)` both before and after. Wherever this
  plan says "the `FillCommand`", read "the first of three".
- **Two of the three gate fixtures can never reach the new branch.** `t03-fail-c` and `t03-fail-d` are
  plain `DeviceN` — `Components=null`, `ProcessChannelCount=null` at every fill — and `t03-fail-d` puts no
  ink on the process plates at all. They are **regression ballast and a must-not-throw check, not evidence
  for the feature.** "The veraPDF gate passed" means *one* fixture exercised the branch plus two
  invariance checks. Do not write it as three.

## What this plan can and cannot prove — read before writing a success claim

- **`t02-pass-a` is the only real evidence, and it proves ONE thing: process routing by channel.** All four
  of its components are Process (measured twice — Pass 2b-engine Task 0 M3 at the resolver, this plan's
  Task 0 M3 at the recorder: channels `[3, 0, 1, 2]`, every `OwnAlternateCmyk` null because Table 71 says
  a process colorant's `/Colorants` entry is ignored). It exercises **no** spot, and therefore **no
  reversion**. And what it proves is specifically that the four tints stop being **permuted** — its tint
  transform is an identity pass-through, so the whole visible defect is channel order.
- **The other two gate fixtures prove nothing about the branch.** `t03-fail-c` and `t03-fail-d` are plain
  `DeviceN` with `Components == null`, measured. They are a must-not-throw check and a regression baseline.
- **Spot reversion has no corpus instance anywhere** — not in GWG, not in veraPDF. It is covered by
  synthetic fixtures plus the plane-cap invariance property test, and **nothing else**. Say exactly that;
  do not let "the veraPDF gate passes" imply reversion is validated.
- **A render-hash digest is not "derived from the file".** The design's phrase "the three veraPDF digests
  land on values derived from the files" cannot be satisfied by a hash — a hash is a regression baseline,
  not evidence of correctness. This plan therefore requires a **separate value-assertion test** on
  `t02-pass-a`'s plates (Task 3), and treats the gate as the regression net around it. **Do not report the
  gate as the evidence for the expected plate values.**
- **Ghostscript is not the oracle.** `gs tiffsep` may be used as a sanity cross-check; divergence is
  investigated, never chased.

## Global Constraints

- **GWG render-hash gate: 51/51, zero differences.** Predicted structurally, not hoped for: GWG has
  **zero NChannel spaces in any page `/ColorSpace` resource** (measured, Pass 2b-engine Task 0 M2), so no
  GWG fill or stroke can reach the new branch. Its only two NChannel spaces are a shading (out of scope)
  and an image (engine-side, already landed with zero movement). **A moved GWG digest is a defect. Do not
  regenerate that baseline.**
- **Every new guard must be OBSERVED to fail by mutation, with the failure mode reported** — assertion vs
  crash. They are not interchangeable. Re-running an implementer's claim is not verification; re-running
  the mutation is.
- **A prescribed mutation is only as good as the fixture it runs against.** Twice in Pass 2b-engine a
  mutation was written against a target that could not observe it. For every mutation below, name **which
  assertion in which fixture changes value**. If you cannot, the mutation is decorative — fix it.
- **`InkDecision` is additive only.** New members go last with defaults; ~20 existing construction sites
  across production and tests use the positional shape.
- **Check what a null/empty return CHANGES, not just what it costs.** Pass 2b-engine's one real regression
  was invisible from inside the changed method: the value was right and the damage was three files away in
  a branch keyed on `is not null`. See the note in Task 1 Step 1 about why this branch does *not* have that
  hazard — and verify that claim rather than inheriting it.

### Commands

```powershell
# From C:\Users\jorda\RiderProjects\Pellucid
dotnet test                                     # full suite (excludes Cups)
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~<Name>
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~GwgRenderHashGateTests --logger "console;verbosity=detailed"
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~NChannelRenderHashGateTests --logger "console;verbosity=detailed"
```

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `Pellucid.Rendering.Cmyk/InkDecider.cs` | Table 148, as one derivation | **Modify** — `InkDecision.SpotRoutes`; the per-component branch |
| `Pellucid.Rendering.Cmyk/CmykPageRenderer.cs:315-378` | `CompositeInk` | **Modify** — consume `SpotRoutes` |
| `Pellucid.Rendering.Avalonia.Tests/Cmyk/InkDeciderTests.cs` | Table 148 test matrix | **Modify** — Task 1 tests |
| `Pellucid.Rendering.Avalonia.Tests/Cmyk/NChannelPerComponentRenderTests.cs` | render-level + plane-cap invariance | **Create** — Task 2 |
| `Pellucid.Rendering.Avalonia.Tests/Cmyk/VeraPdfNChannelCorpus.cs` | walk-up discovery | **Create** — Task 3 |
| `Pellucid.Rendering.Avalonia.Tests/Cmyk/NChannelRenderHashGateTests.cs` | the new gate | **Create** — Task 3 |
| `Pellucid.Rendering.Avalonia.Tests/Cmyk/nchannel-render-hash-baseline.txt` | its baseline | **Create** — Task 3 |
| `Pellucid.Rendering.Avalonia.Tests/Cmyk/CorpusRenderHash.cs` | digest helper | **Modify** — string overloads only |
| `Docs/colour/rendering-conformance.md` *(PDF repo)* | the matrix | **Modify** — Task 4 |

---

### Task 0: Measure before building

**No commits. No production changes.** Scaffold goes in the session scratchpad, **outside both repos**, and
is deleted in the same turn. `git status` clean in both at the end (Pellucid's `website/` excepted).

Pass 2b-engine's Task 0 found four plan defects before an implementer hit them, and this plan makes several
claims that are currently predictions.

- [ ] **Step 1: M1 — do the three veraPDF files render through the CMYK compositor at all?**

Engine-side loading was confirmed in Pass 2a′, but **rendering through `CmykPageRenderer` was not**. For
each of the three files, run `CorpusRenderHash.Digest`'s exact pipeline (`PdfDocument.Load` →
`GetPage(0)` → `GetGeometry(2.0)` → `RecordingRenderTarget.Record` → `SpotColorantRegistry.Build` →
`RenderToBuffer`) and report: page count, pixel dimensions, `registry.SpotCount`, `registry.PlaneNames`,
and whether it threw (and with what).

Files (walk up from the test binary to `veraPDF-corpus`; it sits at
`C:\Users\jorda\RiderProjects\veraPDF-corpus`, a sibling of Pellucid):
```
PDF_A-2b/6.2 Graphics/6.2.4.4 Separation and DeviceN colour spaces/veraPDF test suite 6-2-4-4-t02-pass-a.pdf
PDF_A-2b/6.2 Graphics/6.2.4.4 Separation and DeviceN colour spaces/veraPDF test suite 6-2-4-4-t03-fail-c.pdf
PDF_A-2b/6.2 Graphics/6.2.4.4 Separation and DeviceN colour spaces/veraPDF test suite 6-2-4-4-t03-fail-d.pdf
```
**Note there is also a `PDF_A-4` tree containing files with the same `6-2-4-4` numbering.** Pin the
`PDF_A-2b` path exactly; do not glob across both.

**Prediction:** all three render without throwing; `t02-pass-a` has `SpotCount == 0` (all four components
are Process, so `PageColorantReader.KindFor` gives no spot planes after Pass 2a′). **If `t02-pass-a` has a
non-zero `SpotCount`, stop** — that contradicts Pass 2a′ Task 3 and this plan's premise.

- [ ] **Step 2: M2 — what does `t02-pass-a` paint TODAY, before any change?**

Sample the process plates at a pixel inside the filled area (the content is
`q /CS0 cs 0.0 0.36 0.57 0.02 scn … f Q`, so the fill covers a region — find a covered pixel by scanning
for any non-zero plate, and report its coordinates). Report the four plate values.

**Prediction:** garbage, because the whole-space tint transform is the degenerate `{}` (object 14,
`/FunctionType 4` with an empty body). The point of recording it is that Task 3's value test then has a
measured *before* to contrast with, so "the plates changed to 0.36/0.57/0.02/0" is a demonstrated
improvement rather than an assertion. **If today's values already happen to be C=0.36 M=0.57 Y=0.02 K=0,
stop and re-plan** — the fixture would prove nothing.

- [ ] **Step 3: M3 — confirm the origin the compositor actually receives**

`RecordingRenderTarget` builds the draw list; the fill's `PdfGraphicsState.ResolvedFillColorantOrigin` is
what `CompositeInk` sees. Walk the recorded `PageDrawList` for `t02-pass-a`, find the `FillCommand`, and
print its `ResolvedFillColorantOrigin`: `Names`, `Tints`, `AlternateSpace`, `Subtype`,
`ProcessChannelCount`, and each component's `(Name, Role, Tint, ProcessChannel, OwnAlternateCmyk)`.

**Prediction (from Pass 2b-engine M3, measured at the resolver):** `Subtype` NChannel,
`ProcessChannelCount` **4**, components `Black`/Process/0.0/**ch 3**, `PrCyan`/Process/0.36/**ch 0**,
`PrMagenta`/Process/0.57/**ch 1**, `PrYellow`/Process/0.02/**ch 2**, all four `OwnAlternateCmyk` null.

**This is the load-bearing measurement of the whole plan.** M3 in the engine pass measured the *resolver*;
this measures what survives the *recorder* onto the graphics state. If `Components` or
`ProcessChannelCount` is null here, the carrier does not reach the compositor and **Task 1 cannot work as
designed — stop and re-plan.**

- [ ] **Step 4: M4 — is there any NChannel fill or stroke in GWG after all?**

Pass 2b-engine measured zero NChannel spaces in page `/ColorSpace` resources. That measurement is this
plan's entire basis for "no GWG digest can move". **Re-confirm it independently** (walk all 51 fixtures'
page `/ColorSpace` resources, plus Form-XObject and tiling-pattern resources, for
`/Attributes /Subtype /NChannel`). Report the count.

**If it is not zero, Task 4's gate criterion changes** from "zero digests move" to "these named files may
move, for this measured reason".

- [ ] **Step 5: Report and clean up**

Return every value in your report as a number. **Do not write the ledger** — Task 4 does that, and this
task must leave a clean tree. Delete the scaffold; confirm both repos clean.

---

### Task 1: The per-component decision

**Files:**
- Modify: `Pellucid.Rendering.Cmyk/InkDecider.cs`
- Test: `Pellucid.Rendering.Avalonia.Tests/Cmyk/InkDeciderTests.cs`

**Interfaces:**
- Consumes: `ColorantOrigin.{Components, ProcessChannelCount}`, `ColourantComponent.{Name, Role, Tint,
  OwnAlternateCmyk, ProcessChannel}`, `SpotColorantRegistry.TryGetPlane`.
- Produces: `public IReadOnlyList<SpotRoute>? SpotRoutes` on `InkDecision`, where
  `public readonly record struct SpotRoute(int Plane, float Tint)`. Non-null **only** on the
  per-component branch. Task 2 consumes it.

- [ ] **Step 1: Understand the placement before writing anything**

Three facts that decide where this branch goes. Verify each in the source; do not take them on trust.

1. **It must come AFTER the `/All` arm** (`InkDecider.cs:101-108`). An NChannel space whose single
   component is the reserved `/All` has `RoleFor` → Spot and therefore a non-null `Components`, so a branch
   placed first would swallow `/All` and lose "paint every available colourant".
2. **It must come BEFORE the routed arm** (`:120-127`). That arm is gated on
   `AnyRegistered(origin, registry)` — true only when some name has a plane. `t02-pass-a` has **no spots at
   all**, so `AnyRegistered` is false and the routed arm never fires for it. Placing the new branch after
   would leave the driving fixture on the flatten path.
3. **This branch has NO category-flip hazard, unlike Pass 2b-engine's I-1.** `CompositeInk` derives
   `InkSourceCategory` from `op.Origin is not null` (`CmykPageRenderer.cs:332-336`), and `origin` is
   non-null on both the new path and the fallback. Nothing downstream keys on the *decision's* shape the
   way `img.Spots is not null` keyed on the engine's return. **Confirm this by reading `CompositeInk`**;
   if some other consumer does key on it, say so — that changes the design.

- [ ] **Step 2: Write the failing tests**

Add to `InkDeciderTests.cs`, using its existing `Conv` field and construction idiom
(`new ColorantOrigin([names], [tints], "alt") { Subtype = …, Components = […], ProcessChannelCount = … }`,
`SpotColorantRegistry.Build([new PageColorant(name, kind, alt, ramp, solid)], Conv)`).

Add one local helper first, so the fixtures stay readable:

```csharp
    private static ColorantOrigin NChannel(
        IReadOnlyList<ColourantComponent> comps, int? channelCount = 4) =>
        new([.. comps.Select(c => c.Name)], [.. comps.Select(c => c.Tint ?? 0.0)], "DeviceCMYK")
        {
            Subtype = "NChannel",
            Components = comps,
            ProcessChannelCount = channelCount,
        };
```

```csharp
// THE DRIVING FIXTURE, derived from veraPDF 6-2-4-4-t02-pass-a: /CS0 names [Black PrCyan PrMagenta
// PrYellow] with /Process /Components [PrCyan PrMagenta PrYellow Black], filled `0.0 0.36 0.57 0.02 scn`.
// POSITION IS THE CHANNEL IDENTITY (Table 71): Black sits at space position 0 but process channel 3.
// Route by position instead and Black's 0.0 lands on cyan while 0.36 lands on magenta — this test is
// what catches that transposition.
[Fact]
public void NChannel_process_components_paint_their_own_channels_not_their_positions()
{
    ColorantOrigin origin = NChannel([
        new ColourantComponent("Black",     ColourantRole.Process, 0.0,  null, 3),
        new ColourantComponent("PrCyan",    ColourantRole.Process, 0.36, null, 0),
        new ColourantComponent("PrMagenta", ColourantRole.Process, 0.57, null, 1),
        new ColourantComponent("PrYellow",  ColourantRole.Process, 0.02, null, 2),
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.0, 0.36, 0.57, 0.02], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.Equal(0.36f, d.C, 3);
    Assert.Equal(0.57f, d.M, 3);
    Assert.Equal(0.02f, d.Y, 3);
    Assert.Equal(0.00f, d.K, 3);
    Assert.NotNull(d.SpotRoutes);            // per-component branch was taken …
    Assert.Empty(d.SpotRoutes!);             // … and this space has no spot at all
    Assert.False(d.RouteSpots);              // the NAME-derived routed arm must NOT have fired
}

// A registered spot rides its plane; the process component still paints its own channel.
[Fact]
public void NChannel_registered_spot_is_routed_while_process_paints_its_channel()
{
    var registry = SpotColorantRegistry.Build(
        [new PageColorant("GWG Green", ColorantKind.Spot, "DeviceCMYK", null, (0, 0, 0))], Conv);
    ColorantOrigin origin = NChannel([
        new ColourantComponent("PrCyan",    ColourantRole.Process, 0.4, null, 0),
        new ColourantComponent("GWG Green", ColourantRole.Spot,    0.6, [0.5, 0, 1, 0], null),
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.4, 0.6], "DeviceN", origin, overprint: false, overprintMode: 0, Conv, null, registry);

    Assert.Equal(0.4f, d.C, 3);
    // The spot rode its PLANE, so its alternate must NOT also be folded into the process buffer —
    // that would double-count the ink once SpotDisplayCombiner adds the plane back.
    Assert.Equal(0f, d.M, 3);
    Assert.Equal(0f, d.Y, 3);
    SpotRoute route = Assert.Single(d.SpotRoutes!);
    Assert.Equal(0, route.Plane);
    Assert.Equal(0.6f, route.Tint, 3);
}

// REVERSION — §8.6.6.5's actual subject. No plane for the spot, so its OWN alternate (Table 71: the
// /Colorants Separation describing "the appearance of that colorant alone") folds into the process
// buffer. Combining is ADDITIVE WITH CLAMP, matching SpotDisplayCombiner's shipped SP-2 formula
// clamp(process + Σ ramp_s(tint)) — the same arithmetic at an earlier stage, not a new rule.
[Fact]
public void NChannel_unregistered_spot_reverts_through_its_own_alternate()
{
    ColorantOrigin origin = NChannel([
        new ColourantComponent("PrCyan", ColourantRole.Process, 0.4, null, 0),
        new ColourantComponent("Spot1",  ColourantRole.Spot,    0.5, [0.25, 0.5, 0, 0], null),
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.4, 0.5], "DeviceN", origin, overprint: false, overprintMode: 0, Conv,
        null, registry: null);

    Assert.Equal(0.65f, d.C, 3);            // 0.4 process + 0.25 reverted
    Assert.Equal(0.5f,  d.M, 3);            // 0 + 0.5
    Assert.Equal(0f,    d.Y, 3);
    Assert.Empty(d.SpotRoutes!);            // nothing to route — it reverted
}

[Fact]
public void NChannel_reverted_alternate_is_clamped_not_wrapped()
{
    ColorantOrigin origin = NChannel([
        new ColourantComponent("PrCyan", ColourantRole.Process, 0.8, null, 0),
        new ColourantComponent("Spot1",  ColourantRole.Spot,    1.0, [0.9, 0, 0, 0], null),
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.8, 1.0], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.Equal(1.0f, d.C, 3);             // 0.8 + 0.9 clamped to 1, not 1.7 and not 0.7
}

// §8.6.6.5: /None components "shall never be painted on the page", and row 5-7 adds that they are
// DISCARDED when painting named colourants directly — no /Colorants lookup for them.
[Fact]
public void NChannel_None_component_is_discarded_not_reverted()
{
    ColorantOrigin origin = NChannel([
        new ColourantComponent("PrCyan", ColourantRole.Process, 0.4, null, 0),
        new ColourantComponent("None",   ColourantRole.None,    1.0, [1, 1, 1, 1], null),
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.4, 1.0], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.Equal(0.4f, d.C, 3);
    Assert.Equal(0f, d.M, 3); Assert.Equal(0f, d.Y, 3); Assert.Equal(0f, d.K, 3);
}

// THE GOVERNING PRINCIPLE. A Process component with no determinable channel is unplaceable, so the
// WHOLE op falls back — here to the flattened path, since no name is registered. Never a partial
// placement: silently dropping a component is strictly worse than the status quo.
[Fact]
public void NChannel_unplaceable_process_component_falls_the_whole_op_back()
{
    ColorantOrigin origin = NChannel([
        new ColourantComponent("PrCyan", ColourantRole.Process, 0.4, null, 0),
        new ColourantComponent("PlateX", ColourantRole.Process, 0.9, null, null),   // no channel
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.4, 0.9], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.Null(d.SpotRoutes);              // per-component branch declined …
    Assert.False(d.RouteSpots);             // … and nothing is registered, so this is the flatten arm
}

// The same principle for a spot that can neither ride a plane nor revert.
[Fact]
public void NChannel_spot_with_no_plane_and_no_alternate_falls_the_whole_op_back()
{
    ColorantOrigin origin = NChannel([
        new ColourantComponent("PrCyan", ColourantRole.Process, 0.4, null, 0),
        new ColourantComponent("Spot1",  ColourantRole.Spot,    0.5, null, null),   // no alternate
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.4, 0.5], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.Null(d.SpotRoutes);
}

// A one-channel process space hands a LISTED name index 0, which is NOT the cyan plate. Refuse
// entirely — see ColorantOrigin.ProcessChannelCount's doc for why the count is the only thing that
// distinguishes this from /Cyan under four channels.
[Fact]
public void NChannel_over_a_one_channel_process_space_falls_the_whole_op_back()
{
    ColorantOrigin origin = NChannel(
        [new ColourantComponent("Ink1", ColourantRole.Process, 0.5, null, 0)], channelCount: 1);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.5], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.Null(d.SpotRoutes);
}

// A plain DeviceN and a Separation carry Components == null. This is what keeps every existing
// Separation/DeviceN behaviour — and all 51 GWG digests — exactly where they were.
[Fact]
public void PlainDeviceN_never_takes_the_perComponent_branch()
{
    var origin = new ColorantOrigin(["Black", "GWG Green"], [1.0, 0.5], "DeviceCMYK");

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [1.0, 0.5], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.Null(d.SpotRoutes);
}

// ORDER GUARD: /All must still win. An NChannel space whose one component is the reserved /All has a
// non-null Components (RoleFor maps /All to Spot), so a per-component branch placed FIRST would
// swallow it and lose "paint every available colourant".
[Fact]
public void NChannel_All_still_takes_the_All_arm_not_the_perComponent_branch()
{
    ColorantOrigin origin = NChannel(
        [new ColourantComponent("All", ColourantRole.Spot, 0.75, [0.75, 0.75, 0.75, 0.75], null)]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.75], "DeviceN", origin, overprint: false, overprintMode: 0, Conv, null, TwoSpotRegistry());

    Assert.True(d.AllColourants);
    Assert.Null(d.SpotRoutes);
}

// Under OVERPRINT only the plates this op actually marks are painted; the rest keep the backdrop.
// "Marks" = the union of the process components' channels and the plates a REVERTED alternate puts ink
// on — the mask must WIDEN to cover reverted ink, or that ink is computed and then masked away.
[Fact]
public void NChannel_overprint_paints_only_the_marked_plates_including_reverted_ink()
{
    ColorantOrigin origin = NChannel([
        new ColourantComponent("PrCyan", ColourantRole.Process, 0.4, null, 0),
        new ColourantComponent("Spot1",  ColourantRole.Spot,    0.5, [0, 0.5, 0, 0], null),
    ]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.4, 0.5], "DeviceN", origin, overprint: true, overprintMode: 0, Conv);

    Assert.True(d.PaintC);      // the process component's channel
    Assert.True(d.PaintM);      // the reverted alternate's ink — this is the one that gets forgotten
    Assert.False(d.PaintY);
    Assert.False(d.PaintK);
}

// Knockout paints every process plate, so unmarked ones are wiped to paper — matching
// ProcessContribution's `if (!overprint) pc = pm = py = pk = true;`.
[Fact]
public void NChannel_knockout_paints_every_process_plate()
{
    ColorantOrigin origin = NChannel(
        [new ColourantComponent("PrCyan", ColourantRole.Process, 0.4, null, 0)]);

    InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
        [0.4], "DeviceN", origin, overprint: false, overprintMode: 0, Conv);

    Assert.True(d is { PaintC: true, PaintM: true, PaintY: true, PaintK: true });
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```powershell
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~InkDeciderTests
```

Expected first failure: **compile error** — `InkDecision` has no `SpotRoutes`, and `SpotRoute` does not
exist. That is correct.

**After the type exists but before the branch does**, re-run and record which of the thirteen fail. The
four "falls the whole op back" / "never takes the branch" / "All still wins" tests assert `SpotRoutes` is
**null**, so they will pass trivially at that point — they are regression anchors, not new behaviour.
**Say so in your report; do not count them as proof the branch works.**

- [ ] **Step 4: Add the carrier to `InkDecision`**

In `InkDecider.cs`, above `InkDecision`:

```csharp
/// <summary>One registered spot component of an NChannel space that rides its own ink plane: the plane
/// index and the tint to paint on it. Produced only by the per-component branch, which resolves planes
/// through <see cref="SpotColorantRegistry.TryGetPlane"/> per COMPONENT — unlike the name-derived routed
/// arm, whose caller re-walks <c>Origin.Names</c>. That difference is the point: a component's Role
/// decides whether it may ride a plane at all, so a colorant the page inventory happens to know by name
/// cannot be routed here if this space calls it Process.</summary>
public readonly record struct SpotRoute(int Plane, float Tint);
```

and as the **last** parameter of `InkDecision` (after `AllColourants`, so every existing positional
construction site is untouched):

```csharp
    // Non-null ONLY on the per-component branch (ISO 32000-2 §8.6.6.5). An EMPTY list is meaningful and
    // is not the same as null: it means per-component evaluation succeeded and this space has no spot to
    // route (every component is Process, or reverted into the process buffer above). Null means the
    // branch declined and one of the older arms produced this decision. A consumer must test for null,
    // not for emptiness.
    IReadOnlyList<SpotRoute>? SpotRoutes = null);
```

- [ ] **Step 5: Add the branch**

Insert in `Decide`, **after** the `/All` arm and **before** the routed arm:

```csharp
        // ISO 32000-2 §8.6.6.5: "For NChannel colour spaces, the components shall be evaluated
        // individually; that is, only the ones not present on the output device shall use the alternate
        // colour space of that component." Placed after the /All arm (an NChannel /All has a non-null
        // Components and must still take that arm) and before the routed arm (which is gated on
        // AnyRegistered, false for an all-process NChannel space — the conformance fixture's shape).
        //
        // Gated on ProcessChannelCount == 4 because ProcessChannel indexes the PROCESS space's channels,
        // not the plates: under a one-channel process space a listed name also gets index 0. See
        // ColorantOrigin.ProcessChannelCount.
        if (category == InkSourceCategory.SeparationDeviceN
            && origin is { Components: { } components, ProcessChannelCount: 4 }
            && TryPerComponent(components, registry, overprint, out InkDecision perComponent))
            return perComponent;
```

and the helper:

```csharp
    /// <summary>
    /// Per-component evaluation for an NChannel space. Returns false — leaving the caller on its existing
    /// arms — when ANY component cannot be placed.
    /// </summary>
    /// <remarks>
    /// <para><b>All or nothing.</b> One unplaceable component falls the whole operation back to today's
    /// behaviour rather than being dropped. Borrowed from SP-6c's posture: degrade to the status quo
    /// rather than lose ink. Dropping a component is strictly worse than not evaluating individually.</para>
    /// <para><b>Combining is additive with clamp</b>, which is DERIVED rather than chosen:
    /// <see cref="SpotDisplayCombiner"/> already folds a spot plane in as
    /// <c>clamp(process + Σ ramp_s(tint))</c> (SP-2, shipped and validated). A reverted spot's alternate is
    /// exactly what <c>registry.SpotToCmyk</c> would have contributed had it owned a plane; reverting folds
    /// it in earlier. Same arithmetic, different stage. That equivalence is the plane-cap invariance
    /// property test.</para>
    /// <para><b>The paint mask widens.</b> Under overprint it is the union of the process components'
    /// channels and the plates a reverted alternate actually marks — otherwise reverted ink is computed
    /// and then masked away. Under knockout every process plate is painted, matching
    /// <see cref="ProcessContribution"/>.</para>
    /// </remarks>
    private static bool TryPerComponent(
        IReadOnlyList<ColourantComponent> components, SpotColorantRegistry? registry, bool overprint,
        out InkDecision decision)
    {
        decision = default;
        Span<float> cmyk = stackalloc float[4];
        Span<bool> marked = stackalloc bool[4];
        // Allocated unconditionally rather than lazily: an NChannel op is rare, and `routes ?? []` in the
        // construction below needs a target type that the named-argument position does not reliably
        // supply. One small allocation beats a cast dance at the return site.
        List<SpotRoute> routes = [];

        for (var i = 0; i < components.Count; i++)
        {
            ColourantComponent c = components[i];
            switch (c.Role)
            {
                // Row 5-7: /None components are DISCARDED when painting named colourants directly. No
                // /Colorants lookup for them — a malformed file may define one, and it must be ignored.
                case ColourantRole.None:
                    continue;

                // §8.6.6.4: a named process colorant maps to the device colorant. Table 71: its own
                // /Colorants entry "shall be ignored", which is why nothing here consults it. A null tint
                // (a shading resolves its origin with no per-op colour) is unplaceable, not zero.
                case ColourantRole.Process when c is { ProcessChannel: >= 0 and <= 3, Tint: { } pt }:
                    cmyk[c.ProcessChannel!.Value] += (float)pt;
                    marked[c.ProcessChannel.Value] = true;
                    continue;

                case ColourantRole.Spot when registry?.TryGetPlane(c.Name) is { } plane
                                             && c.Tint is { } st:
                    routes.Add(new SpotRoute(plane, (float)st));
                    continue;

                // No plane: revert through this component's OWN alternate (Table 71 — the /Colorants
                // Separation describing "the appearance of that colorant alone"), which the engine has
                // already evaluated at this component's tint.
                case ColourantRole.Spot when c.OwnAlternateCmyk is { Count: >= 4 } alt:
                    for (var p = 0; p < 4; p++)
                    {
                        var v = (float)alt[p];
                        if (v == 0f) continue;
                        cmyk[p] += v;
                        marked[p] = true;
                    }
                    continue;

                // Unplaceable: a Process component with no channel or no tint, or a Spot with neither a
                // plane nor a usable alternate. Fall back whole.
                default:
                    return false;
            }
        }

        for (var p = 0; p < 4; p++) cmyk[p] = Clamp(cmyk[p]);

        bool pc = marked[0], pm = marked[1], py = marked[2], pk = marked[3];
        if (!overprint) pc = pm = py = pk = true;

        decision = new InkDecision(cmyk[0], cmyk[1], cmyk[2], cmyk[3], pc, pm, py, pk,
            RouteSpots: false, KnockoutOtherSpots: false, SpotRoutes: routes);
        return true;
    }
```

> **`Clamp` already exists** as `InkDecider.Clamp(double)` (`:337`). It takes a `double`; a `float`
> argument widens implicitly, and the result is `float`. Confirm rather than assume.
>
> **`KnockoutOtherSpots: false` is deliberate and matches the routed arm.** The routed arm passes
> `knockoutOtherSpots` but `CompositeInk` returns before ever using it, so other-spot knockout is already
> a deferral on every routed path. Setting it false here makes that explicit rather than relying on the
> caller to keep ignoring it. **If Task 2's `CompositeInk` branch does not return before the knockout
> call, this is wrong — check.**

- [ ] **Step 6: Run the tests to verify they pass**

```powershell
dotnet test Pellucid.Rendering.Avalonia.Tests
```

Expected: 500 + 13 = **513 passing** in that assembly, 0 failing; full suite **1291 / 0**.

- [ ] **Step 7: Mutation-verify — five mutations, each naming its own observable**

For each: apply, run, record **which test and which assertion changed value, and the failure mode**
(assertion vs crash), then revert.

| # | Mutation | Must go red |
|---|---|---|
| 1 | `cmyk[c.ProcessChannel!.Value]` → `cmyk[i]` (route by position, not channel) | `NChannel_process_components_paint_their_own_channels_not_their_positions` — C becomes 0.0 and M becomes 0.36 |
| 2 | Drop `ProcessChannelCount: 4` from the branch gate | `NChannel_over_a_one_channel_process_space_falls_the_whole_op_back` — `SpotRoutes` becomes non-null |
| 3 | `default: return false;` → `continue;` (drop the component silently) | `NChannel_unplaceable_process_component_falls_the_whole_op_back` **and** `NChannel_spot_with_no_plane_and_no_alternate_falls_the_whole_op_back` |
| 4 | In the revert arm, delete `marked[p] = true;` | `NChannel_overprint_paints_only_the_marked_plates_including_reverted_ink` — `PaintM` becomes false |
| 5 | Move the branch **above** the `/All` arm | `NChannel_All_still_takes_the_All_arm_not_the_perComponent_branch` — `AllColourants` becomes false |

**If any leaves the suite green, that guard is unpinned** — add the fixture that observes it before
proceeding. Do not report a green mutation as acceptable.

- [ ] **Step 8: Commit**

```bash
git add Pellucid.Rendering.Cmyk/InkDecider.cs Pellucid.Rendering.Avalonia.Tests/Cmyk/InkDeciderTests.cs
git commit -m "feat(colour): evaluate NChannel components individually in InkDecider

ISO 32000-2 8.6.6.5 requires an NChannel space's components to be evaluated
individually; today one unregistered colourant flattens every colourant through
the whole-space alternate. Adds a branch that routes process components by their
/Process /Components POSITION (Table 71 makes position the channel identity, so
a name cannot carry it), routes registered spots to their planes, and reverts
unregistered spots through their own /Colorants alternate.

All or nothing: one unplaceable component falls the whole op back to today's
behaviour rather than dropping a colorant. Combining is additive with clamp,
derived from SpotDisplayCombiner's shipped SP-2 formula rather than chosen.

Placed after the /All arm (an NChannel /All has a non-null Components and must
still paint every colourant) and before the routed arm (gated on AnyRegistered,
which is false for an all-process NChannel space - the conformance fixture's
exact shape).

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Composite it, and pin the combining rule by invariance

**Files:**
- Modify: `Pellucid.Rendering.Cmyk/CmykPageRenderer.cs` — `CompositeInk`, around `:358`
- Create: `Pellucid.Rendering.Avalonia.Tests/Cmyk/NChannelPerComponentRenderTests.cs`

**Interfaces:**
- Consumes: `InkDecision.SpotRoutes`, `SpotRoute` (Task 1).
- Produces: no new API. Rendered output for NChannel fills/strokes.

- [ ] **Step 1: Write the failing tests**

Create the file. Model the page/fill construction on `CmykPageRendererTests` (`:16-43`) — a
`BeginPageArgs`, a rect `List<PathSegment>`, a `PageDrawList`, and a `PdfGraphicsState`. The state needs
`ResolvedFillColorantOrigin` set, which `CmykFill` there does not do, so write a local `NChannelFill`
helper. Render via the internal `CmykPageRenderer.RenderToBuffer(list, buf, Conv, spots: …, registry: …)`
and read plates with `buf.PlatesCopy()` (`CorpusRenderHash.cs:66` uses exactly this pair).

```csharp
using System.Numerics;
using Pellucid.Rendering.Cmyk;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.Icc;
using Xunit;

namespace Pellucid.Rendering.Avalonia.Tests.Cmyk;

public class NChannelPerComponentRenderTests
{
    private static readonly DeviceCmykConverter Conv =
        new(ICCSharp.Profile.IccProfile.Parse(System.IO.File.ReadAllBytes(TestProfile.Path)));

    private const int W = 20, H = 20;

    private static List<PathSegment> FullPage() =>
    [
        new MoveToSegment(0, 0), new LineToSegment(W, 0), new LineToSegment(W, H),
        new LineToSegment(0, H), new ClosePathSegment(),
    ];

    private static PdfGraphicsState FillState(ColorantOrigin origin, bool overprint = false) => new()
    {
        ResolvedFillColorSpace = "DeviceN",
        ResolvedFillColor = [.. origin.Tints],
        ResolvedFillColorantOrigin = origin,
        FillOverprint = overprint,
        FillAlpha = 1.0,
        Ctm = Matrix3x2.Identity,
    };

    private static ColorantOrigin NChannel(
        IReadOnlyList<ColourantComponent> comps, int? channelCount = 4) =>
        new([.. comps.Select(c => c.Name)], [.. comps.Select(c => c.Tint ?? 0.0)], "DeviceCMYK")
        {
            Subtype = "NChannel",
            Components = comps,
            ProcessChannelCount = channelCount,
        };

    /// <summary>Renders one fill and returns (plates, spot planes). `spotCount` is clamped to at least 1
    /// for the same reason CorpusRenderHash does it: a zero-plane SpotPlaneBuffer has nothing to write to,
    /// and an all-zero spare plane can neither mask nor fabricate a difference.</summary>
    private static (float[] Plates, float[] Planes, byte[] Bgra) Render(
        PdfGraphicsState state, SpotColorantRegistry registry)
    {
        var list = new PageDrawList(new BeginPageArgs(1, W, H, 1, 0, 0, 0),
            new List<DrawCommand> { new FillCommand(FullPage(), false, state) });
        using var buf = new CmykPageBuffer(W, H);
        using var spots = new SpotPlaneBuffer(W, H, Math.Max(registry.SpotCount, 1));
        CmykPageRenderer.RenderToBuffer(list, buf, Conv, spots: spots, registry: registry);
        byte[] bgra = SpotDisplayCombiner.ToSrgbBgra(buf, spots, registry, Conv, new CmykOverlayOptions());
        return (buf.PlatesCopy(), spots.PlanesCopy(), bgra);
    }

    private static (float C, float M, float Y, float K) PlateAt(float[] plates, int x, int y)
    {
        int o = (y * W + x) * 4;
        return (plates[o], plates[o + 1], plates[o + 2], plates[o + 3]);
    }

    private static SpotColorantRegistry Registry(PageColorant c, int planeCap = 16) =>
        SpotColorantRegistry.Build([c], Conv, planeCap);

    // The rendered twin of InkDeciderTests' driving fixture: the decision is right only if it also lands
    // on the plates. Reads the process buffer directly rather than sRGB, because the plates ARE the
    // separation — an sRGB comparison can cancel plate-level errors against each other.
    [Fact]
    public void NChannel_fill_paints_each_process_component_on_its_own_plate()
    {
        ColorantOrigin origin = NChannel([
            new ColourantComponent("Black",     ColourantRole.Process, 0.0,  null, 3),
            new ColourantComponent("PrCyan",    ColourantRole.Process, 0.36, null, 0),
            new ColourantComponent("PrMagenta", ColourantRole.Process, 0.57, null, 1),
            new ColourantComponent("PrYellow",  ColourantRole.Process, 0.02, null, 2),
        ]);

        (float[] plates, _, _) = Render(FillState(origin), SpotColorantRegistry.Build([], Conv));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0.36f, c, 3);
        Assert.Equal(0.57f, m, 3);
        Assert.Equal(0.02f, y, 3);
        Assert.Equal(0.00f, k, 3);
    }

    // CompositeStroke and CompositeFill both funnel through CompositeInk, so a reviewer should see the
    // stroke path pinned rather than assumed. A 20-wide stroke over the page edge covers the centre.
    [Fact]
    public void NChannel_stroke_takes_the_same_perComponent_path_as_a_fill()
    {
        ColorantOrigin origin = NChannel([
            new ColourantComponent("PrCyan",  ColourantRole.Process, 0.36, null, 0),
            new ColourantComponent("PrYellow", ColourantRole.Process, 0.02, null, 2),
        ]);
        PdfGraphicsState state = FillState(origin);
        state.ResolvedStrokeColorSpace = state.ResolvedFillColorSpace;
        state.ResolvedStrokeColor = state.ResolvedFillColor;
        state.ResolvedStrokeColorantOrigin = origin;
        state.StrokeAlpha = 1.0;
        state.LineWidth = W;

        var list = new PageDrawList(new BeginPageArgs(1, W, H, 1, 0, 0, 0),
            new List<DrawCommand> { new StrokeCommand(FullPage(), state) });
        using var buf = new CmykPageBuffer(W, H);
        using var spots = new SpotPlaneBuffer(W, H, 1);
        CmykPageRenderer.RenderToBuffer(list, buf, Conv, spots: spots,
            registry: SpotColorantRegistry.Build([], Conv));

        (float c, float m, float y, _) = PlateAt(buf.PlatesCopy(), W / 2, H / 2);
        Assert.Equal(0.36f, c, 3);
        Assert.Equal(0f, m, 3);
        Assert.Equal(0.02f, y, 3);
    }

    // A component that cannot be placed falls the WHOLE op back — the rendered output must equal what the
    // flatten path produces, i.e. exactly what this fixture painted before the per-component branch
    // existed. Asserted against an origin-free DeviceCMYK fill of the same flattened colour rather than
    // against hard-coded numbers, so the anchor survives refinements to the flatten path itself.
    [Fact]
    public void NChannel_with_an_unplaceable_component_renders_as_the_flattened_colour()
    {
        ColorantOrigin origin = NChannel([
            new ColourantComponent("PrCyan", ColourantRole.Process, 0.4, null, 0),
            new ColourantComponent("PlateX", ColourantRole.Process, 0.9, null, null),
        ]);
        PdfGraphicsState perComponent = FillState(origin);
        perComponent.ResolvedFillColorSpace = "DeviceCMYK";
        perComponent.ResolvedFillColor = [0.4, 0.9, 0, 0];

        (float[] fellBack, _, _) = Render(perComponent, SpotColorantRegistry.Build([], Conv));

        // The same colour with NO origin at all: the pre-branch answer for this operand set.
        var plain = new PdfGraphicsState
        {
            ResolvedFillColorSpace = "DeviceCMYK",
            ResolvedFillColor = [0.4, 0.9, 0, 0],
            FillAlpha = 1.0,
            Ctm = Matrix3x2.Identity,
        };
        (float[] baseline, _, _) = Render(plain, SpotColorantRegistry.Build([], Conv));

        Assert.Equal(baseline, fellBack);
    }

    // 256-entry ramp for a colorant whose alternate is 0.5·t cyan + 1.0·t yellow. The ramp AND the
    // component's OwnAlternateCmyk below are generated from THIS function, so the test measures the
    // invariant rather than my arithmetic.
    private static double[] Alternate(double tint) => [0.5 * tint, 0, 1.0 * tint, 0];

    private static PageColorant Spot1() =>
        new("Spot1", ColorantKind.Spot, "DeviceCMYK",
            [.. Enumerable.Range(0, 256).Select(t => Alternate(t / 255.0))], (0, 0, 0));

    /// <summary>
    /// PLANE-CAP INVARIANCE — the property that pins the combining rule with no external oracle. A spot
    /// component renders to the same combined CMYK whether it rides a plane or reverts to its alternate;
    /// crossing the 16-plane cap changes memory, not pixels.
    ///
    /// <para><b>Tint 0.4 is chosen, not arbitrary.</b> <c>SpotColorantRegistry.SpotToCmyk</c> samples its
    /// 256-entry ramp at <c>round(tint·255)</c>, and 0.4·255 = 102 exactly — so the routed path reads
    /// <c>ramp[102] = Alternate(102/255) = Alternate(0.4)</c>, the same point the reverted path evaluates.
    /// A tint that is not an exact multiple of 1/255 makes the two paths differ by a quantisation step and
    /// turns this property test into a tolerance-tuning exercise.</para>
    ///
    /// <para><b>Scope: over a fresh (paper) buffer.</b> With the plane registered the spot's ink arrives
    /// via SpotDisplayCombiner AFTER compositing; reverted, it arrives inside the op. Those agree over
    /// paper but need not agree over a non-paper backdrop under overprint, where the two paint masks
    /// differ. This property does not claim the wider case.</para>
    /// </summary>
    [Fact]
    public void Spot_renders_identically_whether_it_rides_a_plane_or_reverts()
    {
        const double tint = 0.4;
        ColorantOrigin origin = NChannel([
            new ColourantComponent("PrCyan", ColourantRole.Process, 0.25, null, 0),
            new ColourantComponent("Spot1",  ColourantRole.Spot,    tint, Alternate(tint), null),
        ]);

        (_, _, byte[] routed)   = Render(FillState(origin), Registry(Spot1()));
        (_, _, byte[] reverted) = Render(FillState(origin), Registry(Spot1(), planeCap: 0));

        Assert.Equal(routed, reverted);
    }

    // Guard for the test above: with planeCap 0 the registry really does hand out no plane, so the second
    // render genuinely exercises reversion. Without this, a mistake that left BOTH renders on the routed
    // path would satisfy the equality trivially — the fifth vacuous-test shape this programme has hit.
    [Fact]
    public void PlaneCap_zero_really_registers_no_plane()
    {
        Assert.NotNull(Registry(Spot1()).TryGetPlane("Spot1"));
        Assert.Null(Registry(Spot1(), planeCap: 0).TryGetPlane("Spot1"));
    }
}
```

> **One interaction to be aware of in `Render`.** With `planeCap: 0` the registry holds **zero** ramps
> while `SpotPlaneBuffer` is still allocated with `Math.Max(SpotCount, 1) == 1` plane. `SpotDisplayCombiner`
> then takes its slow path (`s == 1`, so the byte-identity fast path is skipped) and loops that plane — but
> `SpotToCmyk` would index an empty ramp array. It is never reached, because nothing routes to the plane so
> its tint is 0 everywhere and `if (tint <= 0f) continue;` fires first. **Both renders take the same slow
> path, which is what makes the byte comparison apples-to-apples** — but if you change the fixture such
> that a tint does reach a plane the registry has no ramp for, expect an `IndexOutOfRangeException` rather
> than a wrong colour.
>
> **Expected values, so you can check the invariant by hand before trusting the equality:** routed →
> process `[0.25, 0, 0, 0]` on the plates plus `ramp[102] = [0.2, 0, 0.4, 0]` from the combiner; reverted →
> `[0.25 + 0.2, 0, 0.4, 0]` inside the op. Both combine to **`[0.45, 0, 0.4, 0]`**. If they do not, the
> combining rule and `SpotDisplayCombiner` have diverged, which is precisely what this test exists to catch.
>
> **Check these four signatures before writing** — they are the ones most likely to differ:
> `CmykPageBuffer.PlatesCopy()` and `SpotPlaneBuffer.PlanesCopy()` (both used at `CorpusRenderHash.cs:66`),
> `SpotDisplayCombiner.ToSrgbBgra(process, spots, registry, converter, overlay, enabledPlates = null)`,
> and `PdfGraphicsState`'s stroke-side property names. If `PdfGraphicsState` is a record with `init`
> setters rather than a mutable class, rewrite the stroke fixture as an object initialiser.

- [ ] **Step 2: Run to verify they fail**

```powershell
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~NChannelPerComponentRenderTests
```

Expected: the plate test fails on values (the decision is right after Task 1, but `CompositeInk` still
ignores `SpotRoutes`, so a routed spot paints nothing). Record exactly which fail and which pass —
**the all-process plate test may already pass after Task 1**, because a decision with an empty
`SpotRoutes` falls through to `CompositeInk`'s final `Composite(...)` call with the correct C/M/Y/K. If so,
say it: this task's real subject is the *routing*, and the plate test is then a Task 1 anchor.

- [ ] **Step 3: Consume `SpotRoutes` in `CompositeInk`**

Insert immediately **before** the `if (d.RouteSpots && …)` arm at `CmykPageRenderer.cs:358`:

```csharp
        // ISO 32000-2 §8.6.6.5 per-component evaluation. Distinct from the routed arm below in one
        // load-bearing way: the routes were resolved PER COMPONENT, by Role, inside InkDecider — not by
        // re-walking Origin.Names against the registry. A colorant this space calls Process must not ride
        // a plane merely because the page inventory knows that name from another space, and iterating
        // names here would do exactly that.
        //
        // An EMPTY SpotRoutes is not the same as null: it means per-component evaluation succeeded with
        // nothing to route (every component Process, or reverted into d's CMYK above), which is the
        // conformance fixture's own shape. Test for null.
        if (d.SpotRoutes is { } perComponentRoutes)
        {
            if (spots is not null)
                foreach (SpotRoute r in perComponentRoutes)
                    CompositeSpotCoverage(spots, r.Plane, r.Tint, mask, op.Alpha, clip, softMask, buf.Width);

            // A pure spot under overprint marks no process plate: skip, leaving the backdrop untouched —
            // same rule as the routed arm.
            if (d.PaintC || d.PaintM || d.PaintY || d.PaintK)
                Composite(buf, mask, new CmykPaint(d.C, d.M, d.Y, d.K, d.PaintC, d.PaintM, d.PaintY, d.PaintK),
                    op.Alpha, clip, softMask, CmykBlendMode.Normal);

            // Returns before the blanket spot knockout below, matching the routed arm: other-spot
            // knockout is a deferral on every routed path, not a decision this branch reverses.
            return;
        }
```

- [ ] **Step 4: Run to verify they pass**

```powershell
dotnet test Pellucid.Rendering.Avalonia.Tests
dotnet test        # full suite
```

Expected full suite: **1291 + (new render tests) / 0**. Report the exact number.

- [ ] **Step 5: Mutation-verify**

| # | Mutation | Must go red |
|---|---|---|
| 1 | Skip the `CompositeSpotCoverage` loop | the registered-spot render test — its plane reads 0 |
| 2 | Move the new branch **after** the `d.RouteSpots` arm | nothing should change (the two are mutually exclusive: `SpotRoutes` non-null ⇒ `RouteSpots` false). **If a test DOES go red, the arms are not mutually exclusive and the design is wrong — stop and report.** |
| 3 | Change `d.SpotRoutes is { }` to `d.SpotRoutes is { Count: > 0 }` | the all-process plate test — an empty-vs-null confusion, the exact hazard the comment warns about |
| 4 | In the invariance test, change the tint from 0.4 to 0.35 | the invariance test itself — proving it is sensitive to the quantisation argument rather than passing by luck. **Revert; this one documents the fixture, it is not a code defect.** |

- [ ] **Step 6: Commit**

```bash
git add Pellucid.Rendering.Cmyk/CmykPageRenderer.cs Pellucid.Rendering.Avalonia.Tests/Cmyk/NChannelPerComponentRenderTests.cs
git commit -m "feat(colour): composite NChannel per-component routes and pin plane-cap invariance

CompositeInk consumes InkDecision.SpotRoutes, which InkDecider resolved per
COMPONENT by Role rather than by re-walking Origin.Names against the registry -
so a colorant this space calls Process cannot ride a plane just because the page
inventory knows that name from another space.

Adds the plane-cap invariance property test: a spot renders to the same combined
CMYK whether it rides a plane or reverts to its alternate. That pins the additive
combining rule directly, with no external oracle - crossing the 16-plane cap
changes memory, not pixels.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The veraPDF gate, and the value test it cannot replace

**Files:**
- Modify: `Pellucid.Rendering.Avalonia.Tests/Cmyk/CorpusRenderHash.cs` (add string overloads; **do not
  change the hashing** — a changed digest algorithm would move all 51 GWG baselines)
- Create: `VeraPdfNChannelCorpus.cs`, `NChannelRenderHashGateTests.cs`,
  `nchannel-render-hash-baseline.txt`, and the value test (put it in
  `NChannelPerComponentRenderTests.cs` from Task 2)

- [ ] **Step 1: The value test first — this is the actual evidence**

In `NChannelPerComponentRenderTests.cs`, add a test that loads the real `t02-pass-a`, renders it through
the same pipeline as `CorpusRenderHash.Digest`, and asserts the filled region's plates are
**C=0.36, M=0.57, Y=0.02, K=0.0** (±1e-3). Skip via `Assert.Skip` when the corpus is absent, exactly as
the GWG gate does — but **guard the skip**: if the file is missing the test skips, and a skip must never
be mistaken for a pass in the report.

Derivation, stated in the test as a comment: the content stream sets `/CS0 cs` then
`0.0 0.36 0.57 0.02 scn`, over names `[Black PrCyan PrMagenta PrYellow]` with
`/Process /Components [PrCyan PrMagenta PrYellow Black]`. All four are Process (Table 71: their
`/Colorants` entries are ignored), so each tint goes **directly** to its channel: PrCyan 0.36 → C,
PrMagenta 0.57 → M, PrYellow 0.02 → Y, Black 0.0 → K. **Derived from the file, not from a debugger, and
not from the digest.**

**Assert all four plates positionally. Nothing else can see this fix** — see the permutation hazard above:
before is `(0, 0.36, 0.57, 0.02)` and after is `(0.36, 0.57, 0.02, 0)`, so multiset, sum, max and total
ink are all unchanged. Put Task 0's measured *before* tuple in the comment; that contrast is what makes
this a demonstrated change rather than an assertion.

**Sample a pixel known to be covered.** Task 0 measured the painted region as 18,400 pixels with the first
at **(120, 144)** on a 1224 × 1584 buffer at scale 2.0 — the page centre is *not* guaranteed to be inside
it. Either use (120, 144), or scan for the first pixel with any non-zero plate and assert on that; if you
scan, also assert the painted-pixel **count** is 18,400, so a fixture that silently starts painting
almost nothing cannot pass by finding one lucky pixel.

**There are three NChannel fills, not one.** Commands [4] and [5] carry the same origin with all tints 0
and paint `(0,0,0,0)` before and after. If you assert on "the fill", disambiguate — and note that their
existence means the *number* of distinct plate tuples on the page (two: `(0,0,0,0)` and the painted one)
is itself a usable invariant.

- [ ] **Step 2: Discovery helper**

Create `VeraPdfNChannelCorpus.cs`, mirroring `GwgCorpus`'s walk-up shape:

```csharp
namespace Pellucid.Rendering.Avalonia.Tests.Cmyk;

/// <summary>
/// The three veraPDF PDF/A-2b NChannel conformance files, discovered by walking up from the test binary
/// to a sibling <c>veraPDF-corpus</c> checkout — so no corpus is copied into this repo and provenance
/// stays where it is. Empty when the corpus is absent; callers must fail loudly rather than skip when a
/// committed baseline exists (see NChannelRenderHashGateTests).
/// </summary>
public static class VeraPdfNChannelCorpus
{
    public sealed record Fixture(string Category, string File, string Path);

    // An EXPLICIT file list, not a glob. The corpus also carries a PDF_A-4 tree using the same 6-2-4-4
    // numbering, and a glob would silently widen the gate to files this pass never analysed.
    private static readonly string[] Files =
    [
        "veraPDF test suite 6-2-4-4-t02-pass-a.pdf",
        "veraPDF test suite 6-2-4-4-t03-fail-c.pdf",
        "veraPDF test suite 6-2-4-4-t03-fail-d.pdf",
    ];

    public static IReadOnlyList<Fixture> DiscoverAll()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string root = Path.Combine(dir.FullName, "veraPDF-corpus", "PDF_A-2b", "6.2 Graphics",
                "6.2.4.4 Separation and DeviceN colour spaces");
            if (!Directory.Exists(root)) continue;
            var found = new List<Fixture>();
            foreach (string f in Files.OrderBy(f => f, StringComparer.Ordinal))
            {
                string full = Path.Combine(root, f);
                if (File.Exists(full)) found.Add(new Fixture("PDF_A-2b/6.2.4.4", f, full));
            }
            return found;
        }
        return Array.Empty<Fixture>();
    }
}
```

- [ ] **Step 3: `CorpusRenderHash` overloads**

Add `Key(string category, string file)` and `Digest(string path)`, and make the existing
`Key(GwgCorpus.Fixture)`/`Digest(GwgCorpus.Fixture)` delegate to them. **The hashing itself must not
change** — Task 4's GWG gate must still read 51/51/0, and that is the check that this refactor was inert.

- [ ] **Step 4: The gate**

Create `NChannelRenderHashGateTests.cs` as a near-copy of `GwgRenderHashGateTests`, with
`BaselineFileName = "nchannel-render-hash-baseline.txt"`, `RegenVariable = "PELLUCID_NCHANNEL_HASH_REGEN"`,
`[Trait("Category", "LocalOnly")]`, and **both vacuous-pass guards carried over verbatim**:

1. Read the baseline **before** deciding to skip; a committed non-empty baseline next to zero discovered
   fixtures must **fail**, not skip — otherwise the gate can silently stop running and still report green.
2. Regeneration under the env var rewrites the baseline **and still fails**, so a regeneration can never
   be mistaken for a pass.

Add a third guard this corpus needs and GWG's does not:

```csharp
        // t03-fail-c and t03-fail-d are MALFORMED by construction — that is what they test. They must
        // not throw: CorpusRenderHash.Digest catches and returns "THREW:<Type>", which would otherwise
        // sit in the baseline looking like a legitimate digest forever. Assert on it directly rather
        // than letting a committed THREW: value quietly become the expected answer.
        foreach (KeyValuePair<string, string> kv in actual)
            Assert.False(kv.Value.StartsWith("THREW:", StringComparison.Ordinal),
                $"{kv.Key} threw during render ({kv.Value}). A malformed conformance file must degrade, "
                + "not fault — fix the fault rather than baselining it.");
```

- [ ] **Step 5: Generate and review the baseline**

```powershell
$env:PELLUCID_NCHANNEL_HASH_REGEN="1"
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~NChannelRenderHashGateTests --logger "console;verbosity=detailed"
Remove-Item Env:\PELLUCID_NCHANNEL_HASH_REGEN
```

The run **fails by design**. Read the generated file: three entries, none `THREW:`, none `EMPTY`. Then
re-run without the variable and confirm it passes. **Report the three digests.**

- [ ] **Step 6: Commit**

```bash
git add Pellucid.Rendering.Avalonia.Tests/Cmyk/
git commit -m "test(colour): render-hash gate over the three veraPDF NChannel files

Mirrors the GWG gate's proven shape, including its two vacuous-pass guards and
its walk-up corpus discovery, so no corpus is copied into the repo. Uses an
explicit file list rather than a glob: the corpus carries a PDF_A-4 tree with the
same 6-2-4-4 numbering, and a glob would silently widen the gate to files this
pass never analysed.

Adds a third guard the GWG gate does not need - t03-fail-c/d are malformed by
construction and must DEGRADE rather than fault, so a THREW: value is asserted
against instead of being allowed to become the baselined expectation.

The gate is a regression net, not the evidence. The evidence is the separate
value assertion that t02-pass-a paints C=0.36 M=0.57 Y=0.02 K=0.0, derived from
the file's own operands and /Process /Components ordering.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Gate, document, and record

- [ ] **Step 1: Both gates, both suites**

```powershell
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~GwgRenderHashGateTests --logger "console;verbosity=detailed"
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~NChannelRenderHashGateTests --logger "console;verbosity=detailed"
dotnet test
cd C:\Users\jorda\RiderProjects\PDF ; dotnet test PdfLibrary.Tests
```

**GWG must read `51 fixtures hashed, 51 baselined, 0 differences`.** No engine repack happens in this
plan, so the embedded SHA stays `fef2e7b…` — that is correct and expected here, because the engine did not
change. Quote it anyway.

**A moved GWG digest is a defect.** Task 0's M4 re-confirmed there is no NChannel fill or stroke in that
corpus, so nothing there can reach the new branch. Do not regenerate.

- [ ] **Step 2: Conformance matrix** (in the **PDF** repo, `Docs/colour/rendering-conformance.md`)

- **Row 5-3** moves from ❌ **VIOLATION** to ⚠️ — per-component evaluation now exists for fills and
  strokes, and for images via Pass 2b-engine. It is **not** ✅: shadings and meshes are unchanged (G-7),
  a one-channel process space falls back whole, and an all-process NChannel *image* is deliberately not
  per-component-evaluated. State each exclusion rather than ticking the row.
- **Row 5-11** (`/Subtype` not read on the render path) → ✅. It is now read, by both halves.
- **Row 5-10** — the "reversion only if a component is unavailable" warning narrows: reversion is now
  per-component for fills/strokes. Note that it still has **no corpus instance anywhere**, and is covered
  by synthetic fixtures plus plane-cap invariance.
- **G-4** — record it as closed for fills, strokes and images, with the exclusions above; the remaining
  scope is G-7.

- [ ] **Step 3: Ledger**

Create `.superpowers/sdd/2026-07-27-colour-pass2b-compositor/progress.md` (in the **Pellucid** repo, and
note `.superpowers/` is gitignored there too if it follows the PDF repo's convention — **check, and if it
is not ignored, do not commit it without saying so**). Follow the shape of
`PDF/.superpowers/sdd/2026-07-26-colour-pass2b-engine/progress.md`: BASE, plan/design SHAs, entering
counts, Task 0's four measurements **as numbers**, each task's commit and counts, every mutation with its
**observed failure mode**, every finding with whether it originated in plan text, and the standing lessons
carried forward verbatim.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs(colour): record Pass 2b-compositor's gate results and remaining exclusions"
```

---

## Whole-branch review, before merge

Dispatch a review over `b0b2447..HEAD` with these named risks:

1. **Is "no GWG digest can move" true, or merely unobserved?** A green gate is consistent with both
   "nothing changed" and "the gate cannot see what changed" — the Pass 2a′ shape. Verify independently of
   the gate that no GWG fill or stroke reaches the new branch.
2. **Empty vs null `SpotRoutes`.** Every consumer must test for null. An empty list is a successful
   per-component evaluation with nothing to route — the conformance fixture's own shape. Find any consumer
   that treats them alike.
3. **Are the new arm and the routed arm genuinely mutually exclusive?** Task 2 Mutation 2 asserts they are.
   Verify by tracing, not by the mutation's silence.
4. **Does the plane-cap invariance test prove the combining rule, or its own arithmetic?** Check that the
   ramp and the alternate come from one source, and that the tint lands on an exact ramp step.
5. **Which of the new tests survive ALL prescribed mutations?** Report the honest mutation-pinned count,
   as the engine pass's ledger does — do not let the total imply coverage.
6. **Is any claim in the plan, matrix, or commit messages false?** Answer explicitly. Twenty-three of
   twenty-three findings in this programme have originated in plan or design text.

## Success criteria

- `t02-pass-a` renders **C=0.36, M=0.57, Y=0.02, K=0.0** — derived from the file, asserted as values, and
  contrasted with Task 0's measurement of what it painted before.
- Process components route by `ProcessChannel`, pinned by a test that fails under a route-by-position
  mutation.
- Plane-cap invariance holds as a committed property test whose tint lands on an exact ramp step.
- One unplaceable component falls the whole op back, pinned by a mutation that silently drops it.
- GWG gate: **51/51, zero differences**. NChannel gate: 3 fixtures, none `THREW:`.
- Row 5-3 moves off ❌ with its exclusions stated; row 5-11 goes ✅.
- The matrix and ledger say plainly that **spot reversion has no corpus instance anywhere**.
