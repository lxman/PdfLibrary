# G-14: Reserved-Name Separations Apply the Process Colourant Directly (CMYK Path)

**Date:** 2026-07-29
**Status:** Approved (user, 2026-07-29 — scope, DeviceN inclusion, approach, and all three design
sections approved in-session)
**Supersedes nothing; closes:** gap G-14 (`Docs/colour/rendering-conformance.md`), opened
2026-07-28 by the test-debt trio's Task 0 measurement and the same day's user ruling ("Adobe or
better").

## 1. Problem

Measured on the real render path: `[/Separation /Cyan alt tint]` at tint 0.7 with a deliberately
magenta-ramping alternate paints **M=0.7, C=0**. Traced: `ColorSpaceResolver.ResolveSeparation`
(engine) special-cases only `/All` and `/None`, so a reserved name resolves through element 2
(the alternate); with nothing registered, the compositor's routed arm never fires
(`InkDecider.AnyRegistered` needs a spot plane) and the flatten arm paints the resolved
(alternate) colour. Adobe applies a reserved-name separation directly on a CMYK device — C=0.7,
alternate ignored — which is also ISO 32000-2 §8.6.6.4 row 4-11's own first clause. The overprint
plate mask was already correct (the flatten arm gets its plate set from
`PlatesForColorSpaceObject`); only the painted **value** is wrong.

Reachability: invisible in any well-formed file (a truthful reserved-name alternate is a matching
ramp — the two answers coincide); visible under a lying alternate, which prepress files contain
as pranks and errors.

## 2. The behaviour rule (approved)

On the **CMYK soft-proof path**, the reserved process names Cyan/Magenta/Yellow/Black are
**always-available colourants** (§8.6.6.4 first clause — widening row 4-11's
availability-equals-registry policy). A Separation or DeviceN whose colourant names are **all**
reserved-process (or `/None`, which paints nothing) applies each tint **directly to its canonical
plate**; the alternate space and tint transform are ignored. `[/Separation /Cyan alt tint]` at
0.7 paints C=0.7 regardless of what the alternate says.

Unchanged, explicitly:

- **Mixed spaces** (any unregistered non-reserved name, e.g. `[/DeviceN [/Cyan /PANTONE-X] …]`)
  still flatten through the alternate — DeviceN direct application is all-or-nothing (§8.6.6.5).
- **Registered-spot cases** still take the routed / per-component arms.
- **`/All` and `/None`** keep their existing dedicated arms.
- **The RGB path** still reverts through the alternate (row 4-12 requires reversion; this is why
  the fix cannot live in `ResolveSeparation`, which serves both device paths).
- **Overprint plate masks** — already correct; only the painted value changes.

Scope of contexts (user ruling this session): **all painting contexts** — fill, stroke, shading,
image, stencil — with the caveat that shadings have no per-op tint (the function evaluates
per-sample), so their mechanism differs and Task 0 must measure that path before the plan commits
to its shape.

## 3. Architecture (approved — "Approach A", dedicated arms)

One site per context, all gated on the same predicate: **every name is reserved-process or
`/None`, and at least one is reserved-process.**

- **Fill/stroke (Pellucid):** a new dedicated arm in `InkDecider.Decide`, placed after the `/All`
  arm and before the NChannel per-component arm. Returns `ProcessContribution`'s direct tints
  with `RouteSpots: false` — reusing the helper that already paints named process colourants
  correctly under overprint/knockout. The decision takes the flatten tail's composite in
  `CmykPageRenderer`, so the op's own blend mode is respected (unlike the routed arm's forced
  Normal, which exists for spot-plane coherence a pure-process op does not need).
- **Image + stencil (engine, `PdfImageToCmyk` — CMYK-only, RGB path untouched):** the split
  machinery already routes reserved names to plates; the blocker is the no-spot refusal
  (`spotNames.Count == 0` / `SplitByPlacement`'s no-spot guard returns null → whole-space flatten
  through the alternate). Add a process-only direct route for all-reserved origins — per-pixel
  tint→plate for images, constant cell for stencils. Exact mechanism confirmed by Task 0
  measurement before implementation.
- **Shading/mesh:** Task 0 measures. If the sampled path flattens per-sample through the
  alternate (expected), the fix routes each sample's tint to its plate at the same site the
  shading resolves its colour. If that turns out to be its own sizeable sub-system, it is
  recorded as an explicitly-scoped follow-on task **in the same plan**, not silently dropped.
- **Shared predicate:** one helper, one definition of "all-reserved", used by both repos' sites.
  Each repo gets its own copy (they do not share a library for this) with cross-referencing
  comments naming the other site.
- **Guard (verify in Task 0):** `SpotColorantRegistry` never holds a reserved name
  (`PageColorant.Classify` calls them Process), so the new arm and the routed arm cannot both
  claim an op. If this assumption is wrong, the stop rule fires.

Rejected alternatives, recorded: **(B)** engine-side special-casing of C/M/Y/K in
`ResolveSeparation` like `/All`/`/None` — changes the RGB path, which row 4-12 forbids, and the
resolver cannot know the device. **(C)** widening the routed arm's `AnyRegistered` gate — couples
the fix to the spot-plane buffer being present (the renderer falls through to the flatten tail
when `spots` is null) and forces Normal blend on a pure-process op.

## 4. Measurement, testing, gates, close-out (approved)

- **Task 0 — measure, don't argue** (the trio's Task 0 caught a wrong spec expectation; the
  streak of spec-text defects is the reason this section leads): probe production's painted value
  for **every** context in scope (fill, stroke, image, stencil, shading, all-reserved DeviceN,
  mixed-DeviceN control) with lying-alternate fixtures, **before any code change**. Also the
  **corpus census**: scan GWG 51 + NChannel 3 for reserved-name Separations/DeviceN whose
  alternate is not a matching ramp; predict which gate digests will move. Any surprise = stop
  rule fires.
- **Tests:** retire
  `ReservedAndNoneRenderTests.ReservedSeparation_Unregistered_FlattensThroughItsAlternate_G14Baseline`
  (Pellucid) deliberately — it was written to flip red on this fix and is **replaced** by the
  Adobe-behaviour pin (C=0.7, M=0). New render-level pins per context, each **seen to fail** via
  a deliberate mutation (the matrix's own rule: a test that has only ever been green is evidence
  of nothing). The mixed-DeviceN still-flattens case is pinned as a negative control.
- **Gates:** GWG/NChannel digests re-run after the fix. A digest the census predicted to move is
  visually verified against the fixture's own `_ReadMe` criterion (the fixture's printed
  criterion is the oracle, per project convention), then re-pinned. An **unpredicted** move is a
  stop.
- **Close-out:** matrix updates — G-14 closed; row 4-11's availability rule rewritten (registry
  OR reserved); row 4-5's cell G-14 pointer resolved; G-13 / row 5-6 cross-references checked for
  staleness. Engine version packed and pinned in Pellucid (pack-local.ps1 traps: re-add the Skia
  pin it deletes, clear the NuGet cache), both repos committed and pushed, suites green (engine
  2685+new/0 across net8/9/10 at 0 warnings, Pellucid 1319±/0).

## 5. Verification frame

- This is a **deliberate behaviour change** with its own pass, measurement and gate run — the
  first render-behaviour change since the trio pinned the baseline.
- Success: every context in scope paints the direct process value under a lying alternate; the
  mixed-DeviceN control still flattens; RGB path digests unmoved; predicted gate movements
  visually verified and re-pinned; zero unpredicted movements; both suites green.
