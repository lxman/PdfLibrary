# Colour rendering conformance — ISO 32000-2 §8.6.6.4 / §8.6.6.5

> Slice 1 (2026-07-25): **Separation** and **DeviceN** colour spaces. Derived from ISO 32000-2:2020
> (PDF 2.0) including Errata Collection 2, §8.6.6.4 (pp. 201–203) and §8.6.6.5 (pp. 204–210).
>
> Ratchet pass 1 (2026-07-25): ten rows converted from "conformant by inspection" to clause-citing
> tests. Two of them turned out not to be conformant at all — see the score section.
>
> ␀ sweep (2026-07-26): added class F (five file-validity rows reassigned out of the score) and cleared
> every remaining unaudited row to zero — see the score section for the full accounting.
>
> This is the **renderer's** conformance matrix — the companion to `Docs/pdfua/matterhorn-coverage.md`,
> which does the same job for the validator. It answers "how standards compliant is our colour?" with a
> number that has a denominator, rather than an impression.

## How to read this

Each row is one normative statement. Statements are classified:

| Class | Meaning |
|---|---|
| **N** | Normative and machine-verifiable — a `shall` we can test. Counts toward the score. |
| **L** | Latitude — the spec explicitly permits implementation choice (`may`, `should`, "PDF processors are free to", "implementation-dependent"). Cannot be complied with or violated; documented so the freedom is deliberate rather than accidental. |
| **D** | Device-dependent — the answer depends on what device we model ourselves as. Resolved by our device policy (below), not by the clause alone. |
| **F** | File validity — the clause constrains what a conformant *file* may contain, not what the renderer paints. The standard specifies no renderer behaviour for violating input, so a renderer test would pin our choice of degradation rather than the standard's requirement. Enforcing these belongs to the validator (`PdfLibrary/Conformance/`), whose own matrix is `Docs/pdfua/matterhorn-coverage.md`. Excluded from the score, like L and D. |

Status: ✅ conformant with a test · ⚠️ conformant but untested · ❌ violation · ␀ not yet audited.

**Score is over N rows only.** L and D rows are deliberately excluded — counting them would inflate the
denominator with things that cannot be failed.
F rows are excluded for the same reason, added 2026-07-25. Moving a row to F **reassigns** it — it does
not retire it. Every F row below names who enforces it, including "validator gap" where nothing
currently does.

## Device policy (prerequisite for §8.6.6.4/5)

Both clauses condition behaviour on *"a colourant available on the device"*, so compliance is undefined
until we say what device we are. This is not a detail — §8.6.6.4 contains a hard fork on it:

> The preceding paragraph applies only to subtractive output devices such as printers and imagesetters.
> **For an additive device such as a computer display, a Separation colour space never applies a process
> colourant directly; it always reverts to the alternate colour space** […] because the model of applying
> process colourants independently does not work as intended on an additive device.

Pellucid runs in two modes, and they land on opposite sides of that fork:

| Mode | Device model | Separation/DeviceN behaviour required |
|---|---|---|
| **RGB display path** | Additive | Always revert to the alternate space. Direct colourant application is non-conformant. |
| **CMYK soft-proof path** | Simulated subtractive | Direct colourant application is conformant, and is what §8.6.6.4 NOTE 7 calls **separation simulation** (§10.8.3). |

The spot-plane machinery (`SpotPlaneBuffer`, `SpotColorantRegistry`, `SpotDisplayCombiner`) is therefore
a §10.8.3 separation simulation, and is conformant **in the soft-proof path only**. Availability is
defined as "registered in `SpotColorantRegistry`" (`TryGetPlane` returns a plane).

> ✅ **D-1 audited 2026-07-25 — CONFORMANT.** The additive path provably never applies a colourant
> directly. The RGB-vs-CMYK decision is single-homed in `PageRenderService.WantsCmyk`:
> `SoftProofPolicy.WantsRaster(CmykDisplaySettings.Mode, OverprintDetector.HasProofableContent(list))`.
> Every `RenderForDisplayRasterAsync` overload returns `null` when it is false, and `null` routes the page
> to the vector path, which paints from `ResolvedFillColor` — the tint transform evaluated into the
> alternate space. `Pellucid.Rendering.Skia` (the RGB display walker) contains **no** reference to
> `SpotInk`, `SpotPlaneBuffer`, `SpotColorantRegistry` or `ColorantOrigin`, so there is no second route.
> Spot construction occurs at exactly two sites, both on the CMYK path (`CmykPageRenderer`, and registry
> build in `PageItemViewModel`). The mode truth table is pinned by `SoftProofPolicyTests`:
> `Never` → never raster; `Auto` → raster iff proofable; `Always` → always raster.
>
> Note for the record: `Auto` soft-proofs every non-blank page, so the **default** user experience is the
> simulated-subtractive device, not the additive one. That is a product choice and still conformant — the
> additive rule is satisfied because the additive path exists and is correct, not because it is the common
> case.

---

## §8.6.6.4 — Separation colour spaces

| # | Normative statement | Class | Status | Implementation / note |
|---|---|---|---|---|
| 4-1 | "shall be a four-element array whose first element shall be the colour space family name Separation" | F | — | File-shape constraint, not renderer behaviour. `ColorSpaceResolver` gates on `csArray.Count >= 4` and falls through for a malformed array, which is robustness rather than conformance. **Validator gap** — no rule in `PdfLibrary/Conformance/Rules/` checks Separation array shape. |
| 4-2 | Tint is a single component in [0.0, 1.0]; 0.0 = minimum colourant, 1.0 = maximum | N | ✅ | Audited 2026-07-26. Out-of-range tints behave as the nearest valid tint, but the clamp is **not** independently enforced in `ResolveSeparation` — it is delegated to the tint transform's own `/Domain` (`ExponentialFunction.Evaluate`, `PdfLibrary/Functions/ExponentialFunction.cs:63`, `Clamp(input[0], Domain[0], Domain[1])`). This holds for every conformant file: §7.10.1 Table 38 requires every function dictionary to declare a `Domain`, and "input values outside the declared domain shall be clipped to the nearest boundary value" — a Separation/DeviceN tint transform with the required `/Domain [0 1]` clamps by construction. Widening `/Domain` to `[-1 2]` in the test reproduces an out-of-range colour, but that file has already violated §7.10.1's Domain requirement, so this is class F territory (a malformed tint transform), not a renderer gap — no gap opened. Pinned by `SeparationTintRangeTests` (`TintAboveOne_ClampsToOne`, `TintBelowZero_ClampsToZero`), mutation-verified by widening the test's own `/Domain`. |
| 4-3 | "Tints shall always be treated as subtractive colours, even if the device produces output for the designated component by an additive method" | N | ✅ | `SeparationAlternateSpaceTests.SeparationTints_AreSubtractive_HigherTintIsDarker` — tint 0 is lighter than tint 1 by luma. Not implied by the reversion rows: those compare against a direct fill in the alternate space, which inverts identically if the ramp does. |
| 4-4 | "The initial value for both the stroking and nonstroking colour in the graphics state shall be 1.0" | N | ✅ | **Was a confirmed violation, found by auditing — fixed 2026-07-26.** No test previously exercised `cs`/`CS` without a following `sc`/`scn`; when audited, `cs`/`CS` were found to select a colour space but leave the *previous* colour in the graphics state untouched, rather than applying §8.6.8 Table 73's initial value. Fixed via `ColorSpaceResolver.InitialColorFor` (returns the per-space initial value) and a new `PdfContentProcessor.OnColorSpaceChanged` hook, overridden in `PdfRenderer`, invoked from every `cs`/`CS` case. Pinned by `InitialColorValueTests.Separation_WithoutScn_UsesInitialTintOfOne`, part of a 4-test suite covering all families — each test sets a *contrasting* prior colour first, so a carry-over regression paints visibly wrong instead of accidentally matching. **The detail most likely to be re-broken by a future "simplification": DeviceCMYK's initial colour is `[0 0 0 1]`, not all-zeros** — all-zeros in CMYK is white. A prior attempt initialised every space to zero, which broke Separation to white (this row's requirement is 1.0, not 0.0), and was backed out wholesale rather than corrected. Separation and DeviceN initialise to 1.0; DeviceGray/DeviceRGB/CalGray/CalRGB to 0.0; Pattern returns no component vector — its initial colour is a pattern object, not implemented (gap G-11). |
| 4-5 | Cyan / Magenta / Yellow / Black "are reserved to name the process colourants of a CMYK device" | N | ⚠️ | `PageColorant.Classify` → `ColorantKind.Process`. Tested in `PageColorantClassifyTests`; not tested end-to-end on the render path. |
| 4-6 | **All**: "painting operators shall apply tint values to all available colourants at once" | N | ✅ | Fixed 2026-07-25 (was the G-2 gap). Scope is **fills and strokes**: `CmykPageRenderer.CompositeInk` (the only call site that can reach the `/All` branch — the shading and mesh sites pass an origin with empty `Tints`, which the guard excludes per G-7, and the image site passes no origin at all) branches on `InkDecision.AllColourants` and, when set, paints the four process plates *and* loops `SpotColorantRegistry.PlaneNames` to cover every registered spot plane — availability as this document's device policy defines it. The four process plates themselves come from `InkDecider.cs:~106` (CMYK path) and `ColorSpaceResolver.cs:250-253` (RGB path); `ColorSpaceResolver.PlatesForColorSpaceObject` (~line 653) also produces four true plates for `/All`, but that is the **overprint mask**, a different computation reached from a different call site, not the paint loop. `AllColourantRoutingTests` asserts all four plates and both registered spot planes, and was mutation-checked by capping the loop at one plane. Images and stencil masks on the CMYK path are NOT covered by this row — see G-9. |
| 4-7 | **All** on an additive device: "the subtractive tint values […] shall be complemented by subtracting from 1 before applying to all available colourants" | N | ✅ | Fixed 2026-07-25 (was the G-3 violation). `ResolveSeparation` complements before reading the alternate space, so tint *t* paints the neutral 1−*t* on R, G and B. The device fork is honoured on both sides: the additive complement lives in the engine, and the subtractive path applies the tint directly via `InkDecider` (G-5, closed). Pinned by `SeparationAll_At{Full,Zero}Tint_*` and `All_colourant_applies_the_origin_tint_*`. |
| 4-8 | **None**: "shall not produce any visible output […] shall have no effect on the current page" | N | ✅ | **Was a violation, not ⚠️ — fixed 2026-07-25.** The line cited in slice 1 (`:594`) is the overprint *plate mask*, not the paint path. `PdfGraphicsState.Fill/StrokePaintsNothing` suppress the operator at the same sites as `OcHidden`. Coverage is now **every painting operator**: f, S, B (per-half), glyphs incl. Type 3, image XObjects (incl. `/Indexed` over a `/None` base), inline images, stencil masks (gated on the FILL signal, since a stencil has no colour space of its own) and `sh`. Shading *patterns* are the one remaining route — see G-8. Glyph suppression for mode 4 (fill + clip) has its own caveat — see G-10: the fill is correctly suppressed, but so is the clip it was meant to add, which is not itself "no effect on the current page". |
| 4-9 | "A PDF processor shall support Separation colour spaces with the colourant names All and None on all devices" | N | ✅ | Handled and tested for **fills and strokes** on both devices (rows 4-7, 4-8). For `/All` on the CMYK soft-proof path, the paint loop lives in `CmykPageRenderer.CompositeInk` (fed by `InkDecider`'s arm, `InkDecider.cs:~106`) and is pinned by `AllColourantRoutingTests` (G-5, closed) — but that test exercises fills/strokes only; `/All` images and stencil masks diverge on the CMYK path (G-9), so "all devices" does not yet hold for every painting operator. For `/None`, "on all devices" still holds *structurally* rather than by a dedicated CMYK-path test: suppression sits in `PdfGraphicsState`/`ColorSpaceResolver`, upstream of every render target, so the soft-proof path cannot bypass it, but no test exercises `/None` on the CMYK path directly. |
| 4-10 | For All/None, "PDF processors shall ignore the alternateSpace and tintTransform parameters" | N | ✅ | **Was a violation — fixed 2026-07-25.** `ResolveSeparation` evaluated the transform for every colourant name, so `/All` painted whatever it returned. Both names are now handled before the alternate space or the transform is read. Pinned by `SeparationAll_AtFullTint_IgnoresTintTransformAndPaintsBlack`, whose space ramps to red: evaluating the transform paints red, ignoring it paints the required black. |
| 4-11 | "the PDF reader shall determine whether the device has an available colourant […] If so […] shall apply the designated colourant directly" | D | ⚠️ | Soft-proof path only, audited D-1. Availability = registered in `SpotColorantRegistry`. Conformant as §10.8.3 separation simulation. |
| 4-12 | Additive device: "never applies a process colourant directly; it always reverts to the alternate colour space" | D | ✅ | Audited 2026-07-25 (G-1), and now asserted: `SpotSeparation_OnAdditiveDevice_PaintsItsAlternateSpaceColour` renders a spot Separation on the RGB path and requires the pixel to equal the same colour filled directly in the alternate space. |
| 4-13 | If unavailable, "shall arrange for subsequent painting operations to be performed in an alternate colour space" | N | ✅ | `SpotSeparation_OnAdditiveDevice_PaintsItsAlternateSpaceColour`. The oracle is the alternate space painted directly, not a hard-coded triple — "reverts to the alternate space" *means* the two are indistinguishable, so the test survives refinements to CMYK→RGB instead of having to be rewritten by them. |
| 4-14 | alternateSpace "may not be another special colour space (Pattern, Indexed, Separation, or DeviceN)" | F | — | Constrains the file's alternateSpace, not what the renderer paints when it is violated. **Validator gap** — no rule checks this. |
| 4-15 | tintTransform "shall be called with the tint value and shall return the corresponding colour component values" | N | ✅ | `SpotSeparation_{OnAdditiveDevice,AtFullTint}_*` assert both ends of the ramp, so a renderer that ignored the tint and evaluated at a fixed point fails. |
| 4-16 | NOTE 7 — alternate space "does not necessarily reflect the interactions […] when overprinting is enabled"; separation simulation "can be used as an alternative method" | L | — | The spec concedes the approximation and names §10.8.3 as the better path. Our spot planes **are** that path. No compliance debt. |

## §8.6.6.5 — DeviceN colour spaces

| # | Normative statement | Class | Status | Implementation / note |
|---|---|---|---|---|
| 5-1 | alternateSpace "shall not be another special colour space (Pattern, Indexed, Separation, or DeviceN)" | F | — | Same constraint as 4-14, for DeviceN. **Validator gap** — no rule checks this. |
| 5-2 | "if any of the component names […] do not correspond to a colorant available on the device, [the processor] shall perform subsequent painting operations in the alternate colour space" | N | ✅ | `DeviceN_RevertsToAlternate_PassingEveryTintToTheTransform`. The all-or-nothing fallback is correct for plain DeviceN, which is what this row covers — but see 5-3. |
| 5-3 | **"For NChannel colour spaces, the components shall be evaluated individually; that is, only the ones not present on the output device shall use the alternate colour space of that component."** | N | ❌ | **VIOLATION.** `NChannel` appears nowhere in the rendering path of either repo (only in `Conformance/`). With the all-or-nothing fallback, one unregistered colourant in an NChannel space flattens *every* colourant through the alternate, including those we can paint. See gap G-4. |
| 5-4 | tintTransform "shall be called with n tint values and returns m colour component values" | N | ✅ | Same test: a type 4 transform maps (t₁, t₂) → (0, t₁, t₂, 0), so a dropped or transposed component paints a visibly different colour. Verified by mutation — transposing the oracle fails. |
| 5-5 | **None** "may be present only for DeviceN colour spaces that do not have the NChannel subtype" | F | — | Constrains where `/None` may appear in a file. Previously recorded as blocked on G-4 because it needs DeviceN `/Subtype` awareness — as a validator row, that read belongs to the validator, so the dependency does not apply here. **Validator gap** — `PdfxNChannelColorantsRule` reads `/Subtype` but checks `/Colorants` presence, not `/None` placement. |
| 5-6 | None "indicates that the corresponding colour component shall never be painted on the page" | N | ⚠️ | `ShadingSpotSplit`, `TryToSpotInk` skip None components. |
| 5-7 | "When […] painting the named device colourants directly, colour components corresponding to None colourants shall be discarded" | N | ⚠️ | |
| 5-8 | "when the DeviceN colour space reverts to its alternate colour space, those components shall be passed to the tint transformation function" | N | ✅ | Audited 2026-07-26 — **conformant**. `ColorSpaceResolver.ResolveDeviceN` already evaluated the tint transform over every component unfiltered, `/None` included, so reversion was correct before this pass; it simply had no test. Pinned by `DeviceNNoneReversionTests.DeviceN_Reversion_PassesNoneComponentsToTheTintTransform`, mutation-verified: filtering `/None` out before calling the transform (the bug this row exists to catch, and the mirror image of row 5-7's discard-when-direct rule one paragraph away) makes the test fail with the wrong yellow plate. Mutation reverted; suite green again. |
| 5-9 | All-None space "shall always discard its output […] it shall never revert to the alternate colour space" | N | ✅ | Implemented 2026-07-25 via `ColorSpaceResolver.PaintsNothing`, which treats an all-`/None` DeviceN exactly like `/Separation /None` — so it is never flattened through its tint transform on the way to painting nothing. `AllNoneDeviceN_DiscardsOutput_WithoutRevertingToItsAlternate` paints over red with a transform ramping to magenta. |
| 5-10 | "Reversion shall occur only if at least one colour component (other than None) is specified and is not available on the device" | N | ⚠️ | Cited verbatim in `PdfImageToCmyk.TryToSpotInk` (SP-6c) — the routing splits by colorant name and never consults the alternate. |
| 5-11 | Subtype "shall be DeviceN or NChannel. Default value: DeviceN" | N | ❌ | Not read on the render path at all (G-4). |
| 5-12 | "If the value of the Subtype entry […] is NChannel, such information shall be present" (attributes) | F | — | Requires the attributes dictionary to be present for NChannel. Partially enforced by `PdfxNChannelColorantsRule`, which requires `/Colorants` — but that rule is **profile-gated** (`AppliesToProfiles = AllPdfA | PdfX4`), so nothing enforces this at baseline ISO 32000-2. |
| 5-13 | Mixing hints: "applications shall ignore these process component entries if they can obtain the information from an ICC profile" | L | — | **Reclassified 2026-07-26, N → L.** Read in context: the paragraph immediately preceding this row states "PDF processors need not use this [MixingHints] information" — the same optionality already recorded as L for the sibling clauses 5-14/5-15, three rows below. This row's `shall` is a conditional obligation nested inside that optional feature: it binds only an application that has chosen to consume `/MixingHints` process-component entries for blending calculations. `grep -rli mixinghints` (case-insensitive) across both the `PDF` and `Pellucid` repos finds **zero** matches outside this document — nothing on the render path reads `/MixingHints` at all, so the antecedent for this row's `shall` never arises for us; the clause is inapplicable rather than satisfied, exactly as 5-14/5-15 are. Reclassifying avoids scoring a row that cannot fail: "we never read the key" is a fact about the codebase, not a behaviour a test can exercise and watch fail — an unfalsifiable ✅ here would misrepresent an absence of engagement as a passed conformance check. |
| 5-14 | "PDF processors need not use the alternateSpace and tintTransform parameters, and may instead use custom blending algorithms" | L | — | Explicit permission for our additive spot fold. **This is the clause that makes the spot-combine model a design decision rather than a compliance question.** |
| 5-15 | NOTE 5 — processors "are free to use such information instead of the alternateSpace parameter" | L | — | Same permission, restated for the attributes dictionary. |
| 5-16 | Guideline: "should apply either the specified tint transformation function or invoke the same alternative blending algorithm for all DeviceN instances in the document" | L | ⚠️ | `should`, not `shall`. We are consistent by construction (one registry per document). |
| 5-17 | Guideline: blending "should produce a similar appearance […] as separation colours or as a component of a DeviceN colour space" | L | ⚠️ | Same ramp per colorant regardless of arity — consistent by construction. |

---

## Score — slice 1

Updated 2026-07-26, at the close of the ␀-sweep. Three passes are folded into this history: the first
ratchet pass (2026-07-25, ten rows converted from "conformant by inspection" to clause-citing tests, which
found 4-8 and 4-10 to be violations), a same-day second pass closing G-2/G-5/G-6, and this sweep (Tasks
1–4), which added class F to separate file-validity rows from renderer rows and then audited every row
the earlier passes had left ␀. The slice-1 column is the original audit; the deltas are everything since.

| | Slice 1 | Now |
|---|--:|--:|
| Normative + machine-verifiable (**N**) | 26 | 20 |
| — ✅ conformant with a test | 0 | **14** |
| — ⚠️ conformant, untested | 11 | 4 |
| — ❌ violation | 2 | 2 |
| — ␀ not yet audited | 13 | **0** |
| File validity (**F**) | — | 5 |
| Latitude (**L**) | 5 | 6 |
| Device-policy (**D**) | 2 | 2 — 4-12 tested, 4-11 (soft-proof) still untested |

> The slice-1 table published 13 untested / 11 unaudited. Those two figures were transposed — counting
> the rows gives 11 untested and 13 unaudited. Corrected here rather than silently, because the whole
> point of the matrix is that its denominator can be recomputed from the rows.

**Zero rows remain unaudited.** Two reclassifications did part of the work. Five rows (4-1, 4-14, 5-1,
5-5, 5-12) constrain what a conformant *file* may contain, not what the renderer paints, so class F moved
them out of the N denominator rather than scoring them against renderer behaviour the standard never
specifies — a reassignment, not a retirement: every F row still names its enforcer, mostly "validator
gap". One further row, 5-13 (DeviceN mixing hints), moved from N to **L** in this sweep: its `shall`
binds only a processor that has chosen to consume `/MixingHints`, which the spec explicitly says a
processor need not do, and nothing on the render path reads that key at all — the row is inapplicable
rather than satisfied, and scoring it as N would have meant an unfalsifiable ✅.

The rest of the work was auditing the four rows the ratchet pass left ␀. Three came back conformant:
**4-2** (Separation tint range — clamped, but via the tint transform's own required `/Domain`, not an
independent check in `ResolveSeparation`) and **5-8** (DeviceN `/None` components passed through on
reversion) were already-correct behaviour that simply had no test before now. **4-4 was not** — it is
this sweep's confirmed violation, found by auditing rather than by a bug report, the same way 4-8 and
4-10 were in the first ratchet pass. `cs`/`CS` set the current colour space but never applied its §8.6.8
Table 73 initial value, so a `cs` with no following `sc`/`scn` painted whatever colour was already
current instead of the colour space's required initial one. Fixed via `ColorSpaceResolver.InitialColorFor`
and a new `OnColorSpaceChanged` hook, pinned by `InitialColorValueTests` — and recorded there for the
record most likely to be re-broken by a future "simplification": DeviceCMYK's initial colour is
`[0 0 0 1]`, not all-zeros, because all-zeros in CMYK is white.

What remains non-✅ in the N class is unchanged by this sweep and pre-existing: four ⚠️ rows (4-5, 5-6,
5-7, 5-10) whose behaviour lives on the CMYK soft-proof path, where no direct test yet exists, and G-4's
two ❌ rows (5-3, 5-11) — NChannel colour spaces are not implemented on the render path at all, the one
substantive violation this matrix has tracked since slice 1.

## Gaps

- ~~**G-1 (D-1) — additive-device reversion.**~~ **CLOSED 2026-07-25.** Audited conformant, and now
  asserted — row 4-12 is ✅.
- ~~**G-2 — `All` excludes spot planes.**~~ **CLOSED 2026-07-25.** Row 4-6 is ✅; `/All` now paints its
  tint on every plane in `SpotColorantRegistry.PlaneNames`.
- ~~**G-3 — `All` on an additive device is not complemented.**~~ **CLOSED 2026-07-25.** Row 4-7 is ✅ on
  the additive path; the subtractive half was tracked separately as G-5, closed below the same day.
- **G-4 — NChannel is not implemented on the render path.** Rows 5-3 and 5-11. The per-component
  evaluation rule is a `shall`, and we do the opposite (all-or-nothing). This remains the substantive
  violation in the slice, and is untouched by this pass. (5-5 and 5-12 were previously listed as blocked
  on this gap too; both moved to class F in this sweep — file-shape constraints are the validator's job by
  this document's own class definition, so G-4's remaining scope is 5-3 and 5-11 alone.)
- ~~**G-5 — `/All` is not device-aware on the soft-proof path.**~~ **CLOSED 2026-07-25.** The engine
  keeps producing the additive answer (it cannot know the device — `WantsCmyk` is decided after the
  draw list is built), and `InkDecider` derives the subtractive answer from `ColorantOrigin`.
  `BuildTintToCmyk`/`BuildTintToRgb` no longer evaluate the tint transform for either reserved name.
- ~~**G-6 — `/None` suppression does not cover images.**~~ **CLOSED 2026-07-25.** Extended to image
  XObjects, inline images, stencil masks and `sh`.
- **G-7 — `/All` shadings and meshes.** A shading resolves its `ColorantOrigin` with `rawColor: null`,
  so `Tints` is empty: there is no single per-op tint, because the tint varies across the ramp. Such an
  op falls through to the flattened path. Correct handling needs the `/All` rule applied per-sample
  inside the ramp evaluation, not once per op.
- **G-8 — `/None` shading *patterns*.** The `sh` operator is covered; a shading used as a *pattern*
  (via `scn` on a Pattern colour space) paints through the pattern machinery, which does not consult
  `PaintsNothing`. Narrower than G-7 and likely a few lines, but untested and so unclaimed.
- **G-9 — `/All` images and stencil masks diverge on the CMYK path.** Rows 4-6/4-9's `/All` coverage is
  fills and strokes only, via `CmykPageRenderer.CompositeInk`'s `AllColourants` branch. Two other painting
  operators reach the CMYK page by a different route and do NOT go through that branch:
  - An **`/All` image** gets correct process plates (`PdfImageToCmyk`'s `BuildTintToCmyk` call), but no
    spot planes: `TryToSpotInk` (`PdfImageToCmyk.cs:315`) routes a colorant to a plane only when
    `PageColorant.Classify(name) == ColorantKind.Spot`, and `Classify("All")` returns `ColorantKind.All`
    (`PageColorant.cs:28`), never `Spot` — so the loop that would populate `SpotImageInk` never fires for
    `/All`.
  - An **`/All` stencil mask** takes its colour from the graphics state's `ResolvedFillColor`
    (`RecordingRenderTarget.cs:125`), which is the value `ColorSpaceResolver.ResolveSeparation` already
    computed for the RGB path — the ADDITIVE complement (1 − t neutral on R/G/B). That gets baked into
    the mask's RGBA and then converted to CMYK by an ICC round-trip downstream, producing a neutral grey
    from ICC rather than the direct, uncomplemented tint on all four plates that `InkDecider`'s `/All` arm
    would give a fill.

  Net effect: an `/All` fill and an `/All` image (or stencil mask) painted with the *same tint*, on the
  *same CMYK page*, currently render two different colours — one via the direct §8.6.6.4 rule, one via an
  additive-device complement that clause explicitly reserves for a different device. Untested and
  unclaimed; no fix attempted in this pass.
- **G-10 — `/None` glyph suppression drops the mode-4 clip along with the fill.**
  `PdfGraphicsState.TextPaintsNothing` masks `RenderingMode` with `& 3`, so mode 4 (fill + add to clip)
  is treated identically to mode 0 (fill only): both map to `FillPaintsNothing`. `PdfRenderer.cs:947` and
  `:1266` skip the entire `_coreText.Render` call when `TextPaintsNothing` is true, which is correct for
  the fill half — a `/None` fill really does have no effect — but for mode 4 it also discards the "add to
  the clipping path" half. A later painting operator that relies on that clip then paints somewhere it
  should have been clipped out of, which IS a visible effect on the current page, contradicting the very
  clause row 4-8 cites. Pre-existing slice-1 debt, not introduced by this branch; out of scope to fix here
  — flagged so it is not mistaken for coverage row 4-8 already claims.
- **G-11 — Pattern's initial colour is not implemented, only defaulted.** Noted during the row 4-4 clause
  read (§8.6.8 Table 73): "In a Pattern colour space, the initial colour shall be a pattern object that
  causes nothing to be painted." `ColorSpaceResolver.InitialColorFor` returns `null` for `"Pattern"` —
  which `OnColorSpaceChanged` treats as "leave the current colour alone", i.e. carries over whatever the
  *previous* colour space's value was. That is a safe default (it cannot invent a plausible-looking wrong
  colour) but it is not the clause's requirement, which is a pattern object that paints nothing — a
  distinct concept from the `/None` colourant's `PaintsNothing` signal used elsewhere in this matrix (rows
  4-8, 5-9). Conflating "no initial value handled" with "correctly initialised to paint nothing" without
  an audit is exactly the error this matrix exists to prevent. Untested and unclaimed; no fix attempted in
  this pass.

## Fixtures

GWG 2-SPOT (`gwg-gos/…/Categories/2-SPOT/`) carries 17 files including GWG020 (CMYK+spot overprint),
GWG030/031 (grey/K black overprint), GWG040/041/120 (white overprint and knockout) and GWG080/081
(DeviceN 6c/5c). Each ships a `_ReadMe.pdf` stating its own visual pass criterion — per project
convention, the fixture's printed criterion is the oracle, not another renderer's output.

## Method note

Clause text was read from the indexed ISO 32000-2 EC2 PDF rather than recalled, and every implementation
claim above cites a file and line that was opened. Rows marked ␀ are honestly unaudited — they are not
assumed conformant. A future slice should either verify or demote them; the count of ␀ is itself the
measure of how far this slice got.

The ratchet pass added a second rule, learned the hard way. **A ✅ row requires a test that has been seen
to fail.** Rows 4-8 and 4-10 show why the reading-and-reasoning pass is not enough on its own: both looked
conformant to a careful reader, and neither was. Less obviously, a test written for an already-correct
behaviour can be vacuous without anyone noticing — the first draft of the 5-9 test chose tint values whose
alternate-space colour happened to equal the backdrop it was painted over, so it passed whether or not the
space discarded anything. Every ✅ in this matrix was therefore run against a deliberate mutation (an
inverted oracle, a transposed component, a renamed colourant) and confirmed to fail. A test that has only
ever been green is evidence of nothing.

Claims about painted output are asserted on **rendered pixels**, not on resolver return values. The two
come apart in both directions: a resolver can return a colour that nothing paints, and a renderer can
paint black for a space the resolver declined to resolve. Only the raster settles it.

Next slices, in rough value order: §8.6.7 (overprint control / OPM), §8.6.5.x (CalRGB, CalGray, Lab,
ICCBased), §8.7.3 (blend modes), §11.6.5.3 (soft masks — the `/Matte` rule fixed 2026-07-25).
