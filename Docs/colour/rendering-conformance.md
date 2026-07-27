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
| **F** | File validity — the clause constrains what a conformant *file* may contain, not what the renderer paints. The standard specifies no renderer behaviour for violating input, so a renderer test would pin our choice of degradation rather than the standard's requirement. Enforcing these belongs to the validator (`PdfLibrary/Conformance/`); none of them are currently tracked in any validator conformance matrix — `Docs/pdfua/matterhorn-coverage.md` is the validator's matrix for PDF/UA (Matterhorn) accessibility checkpoints and has no rows for §8.6.6.4/§8.6.6.5. Excluded from the score, like L and D. |

Status: ✅ conformant with a test · ⚠️ conformant but untested · ❌ violation · ␀ not yet audited.

**Score is over N rows only.** L and D rows are deliberately excluded — counting them would inflate the
denominator with things that cannot be failed.
F rows are excluded for the same reason, added 2026-07-26. Moving a row to F **reassigns** it — it does
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
| 4-2 | Tint is a single component in [0.0, 1.0]; 0.0 = minimum colourant, 1.0 = maximum | N | ✅ | Audited 2026-07-26. Out-of-range tints behave as the nearest valid tint, but the clamp is **not** independently enforced in `ResolveSeparation` — it is delegated to the tint transform's own `/Domain` (`ExponentialFunction.Evaluate`, `PdfLibrary/Functions/ExponentialFunction.cs:63`, `Clamp(input[0], Domain[0], Domain[1])`). This holds for every conformant file: §7.10.1 Table 38 requires every function dictionary to declare a `Domain`, and "input values outside the declared domain shall be clipped to the nearest boundary value" — a Separation/DeviceN tint transform with the required `/Domain [0 1]` clamps by construction. Widening `/Domain` to `[-1 2]` in the test reproduces an out-of-range colour, but that file has already violated §7.10.1's Domain requirement, so this is class F territory (a malformed tint transform), not a renderer gap — no gap opened. Pinned by `SeparationTintRangeTests` (`TintAboveOne_ClampsToOne`, `TintBelowZero_ClampsToZero`) — but the mutation (widening the test's own `/Domain`) only kills the **low** half: `TintBelowZero_ClampsToZero` fails as intended (the unclamped tint −0.5 overflows the `(byte)` cast in `ColorConverter`'s DeviceCMYK conversion, which has no lower clamp, producing a wrapped RGB(255,126,255) instead of white). `TintAboveOne_ClampsToOne` passes unchanged under the same mutation: the unclamped high tint (M = 1.5) is independently re-saturated one layer downstream by `ColorConverter`'s own `Math.Min(1.0, …)`, which lands on the exact same byte as the correctly-clamped M = 1.0 case — so no pixel-level assertion through `DeviceCMYK`'s magenta channel can tell "clamped by `ExponentialFunction`'s `/Domain`" apart from "left unclamped, saturated downstream by coincidence". The high side of this row is therefore conformant **by inspection** of `ExponentialFunction` and the required `/Domain` (the same §7.10.1 argument above), not by a test that has been seen to fail against it — a genuine, currently-unclosable gap in this row's own "test that has been seen to fail" standard, recorded honestly rather than papered over with a vacuous assertion. |
| 4-3 | "Tints shall always be treated as subtractive colours, even if the device produces output for the designated component by an additive method" | N | ✅ | `SeparationAlternateSpaceTests.SeparationTints_AreSubtractive_HigherTintIsDarker` — tint 0 is lighter than tint 1 by luma. Not implied by the reversion rows: those compare against a direct fill in the alternate space, which inverts identically if the ramp does. |
| 4-4 | "The initial value for both the stroking and nonstroking colour in the graphics state shall be 1.0" | N | ✅ | **Was a confirmed violation, found by auditing — fixed 2026-07-26.** No test previously exercised `cs`/`CS` without a following `sc`/`scn`; when audited, `cs`/`CS` were found to select a colour space but leave the *previous* colour in the graphics state untouched, rather than applying §8.6.8 Table 73's initial value. Fixed via `ColorSpaceResolver.InitialColorFor` (returns the per-space initial value) and a new `PdfContentProcessor.OnColorSpaceChanged` hook, overridden in `PdfRenderer`, invoked from every `cs`/`CS` case. Pinned by `InitialColorValueTests`, a 6-test suite — each test sets a *contrasting* prior colour first, so a carry-over regression paints visibly wrong instead of accidentally matching. **Exercised:** Separation, DeviceN, DeviceCMYK, DeviceRGB, Lab (`Lab_WithoutScn_ClampsAToDeclaredRange` — a narrow `/Range` forces initial *a* to clamp away from 0, the only way to observe the clamp rather than a coincidental default) and ICCBased (`ICCBased_WithoutScn_ClampsToDeclaredRange`, `/N 4` with a narrow `/Range`, same reasoning). **Not exercised:** Indexed, CalGray, CalRGB — all three take the same constant-value code path as an already-tested sibling (Indexed → `[0.0]` same as DeviceGray's arm; CalGray/CalRGB share `InitialColorFor`'s `DeviceGray`/`DeviceRGB` case labels outright), so the gap is structural rather than a distinct behaviour, but it is still untested and unclaimed rather than assumed. **The detail most likely to be re-broken by a future "simplification": DeviceCMYK's initial colour is `[0 0 0 1]`, not all-zeros** — all-zeros in CMYK is white. A prior attempt initialised every space to zero, which broke Separation to white (this row's requirement is 1.0, not 0.0), and was backed out wholesale rather than corrected. Separation and DeviceN initialise to 1.0; DeviceGray/DeviceRGB/CalGray/CalRGB to 0.0; Pattern returns no component vector — its initial colour is a pattern object, not implemented (gap G-11). Stencil-mask routing after a bare `cs` (no `scn`) is untested in either direction — see gap G-13. |
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
| 5-3 | **"For NChannel colour spaces, the components shall be evaluated individually; that is, only the ones not present on the output device shall use the alternate colour space of that component."** | N | ⚠️ | **Was ❌ VIOLATION. Implemented 2026-07-27 (Pass 2b), for fills/strokes and images — NOT a clean ✅, and the exclusions below are the reason.** Fills/strokes: `InkDecider.TryPerComponent` routes each Process component to its `/Process /Components` **position** (Table 71 makes position the channel identity, which a name cannot carry), routes registered spots to their planes, and reverts unregistered spots through their own `/Colorants` alternate. Images/stencils: `PdfImageToCmyk` splits by role and channel rather than by name. **Evidence:** veraPDF `6-2-4-4-t02-pass-a` renders C=0.36 M=0.57 Y=0.02 K=0.0, asserted positionally on the real file, against a measured pre-change `C=0 M=0.36 Y=0.57 K=0.02` — its tint transform is an identity pass-through, so the whole visible defect was a channel permutation, and only a positional assertion can see it. Mutation-verified: routing by position instead of channel reproduces the pre-change tuple exactly. **Still excluded:** shadings and meshes (G-7 — no per-op tint); a one-channel (`/DeviceGray` or ICCBased `/N 1`) process space, where channel 0 is not the cyan plate; an all-process NChannel *image or stencil* (see G-4's note — the overprint category, not the colour, decides that); and spot reversion for images. **Reversion has no corpus instance anywhere** — synthetic fixtures plus plane-cap invariance only. |
| 5-4 | tintTransform "shall be called with n tint values and returns m colour component values" | N | ✅ | Same test: a type 4 transform maps (t₁, t₂) → (0, t₁, t₂, 0), so a dropped or transposed component paints a visibly different colour. Verified by mutation — transposing the oracle fails. |
| 5-5 | **None** "may be present only for DeviceN colour spaces that do not have the NChannel subtype" | F | — | Constrains where `/None` may appear in a file. Previously recorded as blocked on G-4 because it needs DeviceN `/Subtype` awareness — as a validator row, that read belongs to the validator, so the dependency does not apply here. **Validator gap** — `PdfxNChannelColorantsRule` reads `/Subtype` but checks `/Colorants` presence, not `/None` placement. |
| 5-6 | None "indicates that the corresponding colour component shall never be painted on the page" | N | ⚠️ | `ShadingSpotSplit`, `TryToSpotInk` skip None components. |
| 5-7 | "When […] painting the named device colourants directly, colour components corresponding to None colourants shall be discarded" | N | ⚠️ | |
| 5-8 | "when the DeviceN colour space reverts to its alternate colour space, those components shall be passed to the tint transformation function" | N | ✅ | Audited 2026-07-26 — **conformant**. `ColorSpaceResolver.ResolveDeviceN` already evaluated the tint transform over every component unfiltered, `/None` included, so reversion was correct before this pass; it simply had no test. Pinned by `DeviceNNoneReversionTests.DeviceN_Reversion_PassesNoneComponentsToTheTintTransform`, mutation-verified: filtering `/None` out before calling the transform (the bug this row exists to catch, and the mirror image of row 5-7's discard-when-direct rule one paragraph away) makes the test fail with the wrong yellow plate. Mutation reverted; suite green again. |
| 5-9 | All-None space "shall always discard its output […] it shall never revert to the alternate colour space" | N | ✅ | Implemented 2026-07-25 via `ColorSpaceResolver.PaintsNothing`, which treats an all-`/None` DeviceN exactly like `/Separation /None` — so it is never flattened through its tint transform on the way to painting nothing. `AllNoneDeviceN_DiscardsOutput_WithoutRevertingToItsAlternate` paints over red with a transform ramping to magenta. |
| 5-10 | "Reversion shall occur only if at least one colour component (other than None) is specified and is not available on the device" | N | ⚠️ | Cited verbatim in `PdfImageToCmyk.TryToSpotInk` (SP-6c). **Narrowed 2026-07-27 (Pass 2b):** for **fills and strokes** reversion is now genuinely per-component — only a spot with no registered plane takes its own alternate, and the components that *are* available are painted directly, which is what this row and 5-3 together require. Still ⚠️, for three reasons: reversion remains whole-space for **images** (an image's tint varies per pixel and no per-pixel own-alternate colour is carried anywhere); it remains whole-space for shadings/meshes (G-7); and **no corpus file anywhere exercises reversion at all** — not GWG, not veraPDF — so the per-component path is covered by synthetic fixtures plus the plane-cap invariance property test and by nothing else. |
| 5-11 | Subtype "shall be DeviceN or NChannel. Default value: DeviceN" | N | ✅ | **Was ❌. Closed 2026-07-27 (Pass 2b).** `/Subtype` is read on the render path by both halves: `ColorSpaceResolver.BuildComponents` gates the whole per-component carrier on `space.IsNChannel`, and that gate is what keeps a plain DeviceN (`Components == null`) on the unchanged whole-space path. The default is honoured — `SpotColorSpace` defaults an absent `/Subtype` to `"DeviceN"` per Table 70. Mutation-verified from the other side: removing the `IsNChannel` gate changes a plain DeviceN's ramp, which the 51-fixture GWG gate catches. |
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
- ~~**G-4 — NChannel is not implemented on the render path.**~~ **SUBSTANTIALLY CLOSED 2026-07-27** by
  Pass 2b (2b-engine `fef2e7b`, 2b-compositor). Row 5-11 is ✅; row 5-3 moved ❌ → ⚠️, and the ⚠️ is
  honest rather than grudging — read its cell for the exclusions. Original text, kept: *the per-component
  evaluation rule is a `shall`, and we do the opposite (all-or-nothing). This remains the substantive
  violation in the slice.* (5-5 and 5-12 were previously listed as blocked on this gap too; both moved to
  class F in the 2026-07-26 sweep — file-shape constraints are the validator's job by this document's own
  class definition, so G-4's remaining scope was 5-3 and 5-11 alone.)

  **What actually closed it, and what the corpora did and did not prove.** Fills and strokes evaluate
  per-component in `InkDecider.TryPerComponent`; images and stencils split by role and channel in
  `PdfImageToCmyk`. The evidence is **one file** — veraPDF `6-2-4-4-t02-pass-a`, asserted positionally at
  C=0.36 M=0.57 Y=0.02 K=0.0 against a measured pre-change `C=0 M=0.36 Y=0.57 K=0.02`. The other two files
  in `NChannelRenderHashGateTests` are plain `DeviceN` (measured) and can never reach the new branch: they
  are a must-not-throw check and regression ballast, **not** three-fixture evidence. The 51-fixture GWG
  gate stayed at zero differences throughout both halves — which proves *silence*, not correctness, because
  GWG contains **zero** NChannel spaces in any page `/ColorSpace` resource (measured twice, two independent
  ways) and its only two NChannel spaces are a shading and an `/Indexed` image whose colorants split
  identically under the old and new rules.

  **Deliberate exclusions, each a live gap:**
  1. **Shadings and meshes** — no per-op tint (`rawColor: null`). **G-7**, unchanged. Note they *reach*
     `TryPerComponent` and are turned away one arm at a time; a `placed` guard keeps an all-`/None` space
     from succeeding there, because the correctness of an out-of-scope path should not be decided by which
     `case` a null tint happens to fall through.
  2. **A one-channel process space** (`/DeviceGray`, ICCBased `/N 1`) is refused whole.
     `ColourantComponent.ProcessChannel` indexes the *process* space, so a listed name there gets index 0 —
     byte-identical to `/Cyan` under four channels, measured. Mapping a gray channel to a plate is a guess
     this pass declines to make.
  3. **An all-process NChannel image or stencil** is refused whole — a knowing trade. Returning null would
     leave `ImageCommand.Spots` null, flipping `InkSourceCategory` out of Table 148 row 3, so an
     **overprinting** op would erase a backdrop it used to preserve (the GWG020 shape SP-6d closed). Better
     on colour, worse on overprint; the overprint regression is the one with teeth.
  4. **Image spot reversion** — an image's tint varies per pixel and no per-pixel own-alternate colour is
     carried anywhere.
  5. **Plane-cap invariance is not universal.** `OwnColorantRamp` (`ColorSpaceResolver.cs:621`) requires the
     space's whole-space alternate to be `DeviceCMYK`; `OwnAlternateFor` (`:1286`) does not. For an NChannel
     space with a Lab/RGB/ICCBased/Gray whole-space alternate and a CMYK-reducible `/Colorants` entry, a
     spot reverts through its real alternate while the registry ramp falls back to the zeroed approximation
     — the routed and reverted paths give different ink and the invariant genuinely fails. Derived by
     reading both engine methods; no corpus instance; the compositor's own invariance test cannot see it,
     because at that level both values are inputs. Closing it means widening a gate Pass 2a′ deliberately
     narrowed, so it is a separately-gated engine change.

  **Note (Pass 2a′, engine-only, 2026-07-26; corrected post-merge by whole-branch review):** this pass
  does not close G-4 (that is Pass 2b's job — the compositor still does not evaluate NChannel components
  individually). But `PageColorant.Kind`/`TintRamp`, which Pass 2a′ *does* change, already feed the
  shipped `SpotColorantRegistry`/`CorpusRenderHash` in Pellucid. The corpus render-hash gate stayed
  silent through Pass 2a′ only because the 51-fixture GWG corpus contains none of three input shapes:
  (1) an ICCBased process space with a non-reserved process name (e.g. `/PrCyan`) — routes to zero spot
  planes now instead of three, and until Pass 2b lands `ProcessChannel` routing such a colorant's tint
  reaches neither a plate nor a plane, only the flattened alternate; (2) an NChannel space with a
  non-separable whole-space transform and a usable `/Colorants` entry — different ramp, different plane
  ink; (3) an NChannel space with a `DeviceGray` alternate and a usable `/Colorants` entry — closed by
  this same pass narrowing `OwnColorantRamp`'s gate to `DeviceCMYK` only. **If a future corpus digest
  moves and traces to `SpotColorantRegistry.Build`/`BuildCmykRamp`, check these three shapes before
  treating it as an unexplained regression** — see the design doc's Gaps section for the full writeup.

  **Note (Pass 2b-engine, 2026-07-26).** Rows 5-3 and 5-11 stay ❌ — this pass does not close G-4 either.
  It closes the **images** half of the routed→flattened window Pass 2a′ recorded, and supplies the one
  carrier the compositor half needs. Two changes: `ColorantOrigin.ProcessChannelCount`, and
  `PdfImageToCmyk.TryToSpotInk`/`StencilInkFromFill` splitting an NChannel space's colorants by
  **role and channel** rather than by name, all-or-nothing, gated on a four-channel process space.

  Three things this pass makes true that the matrix should not overstate:

  1. **The window is now closed for images that carry a spot, still open for fills/strokes.** `InkDecider.
     ProcessContribution` still switches on the literal names `Cyan`/`Magenta`/`Yellow`/`Black`, so a
     non-reserved process colorant in a **fill or stroke** still reaches neither a plate nor a plane.
     That is Pass 2b-compositor's first obligation. And note the qualifier: an **all-process** NChannel
     image or stencil is refused by the per-component split (gap 3 below), so the window stays open for it
     too. That is not a corner case — the design's own driving fixture, `t02-pass-a`'s `/CS0`, is all four
     components Process, so the *image* analogue of the motivating case is precisely the shape that falls
     back.
  2. **Nothing in either corpus exercises any of it.** Measured, not assumed: across all 51 GWG fixtures
     there are exactly **two** NChannel spaces, both in GWG081 — an axial shading (G-7) and an `/Indexed`
     image whose colorants split **identically** under the old and new rules. And **zero** NChannel spaces
     appear in any page `/ColorSpace` resource, so there is no NChannel fill, stroke *or stencil* anywhere
     in GWG. The render-hash gate's 51/51 zero-difference result is therefore proof of *silence*, not of
     correctness. The three veraPDF NChannel files exercise a **fill**, not an image.
  3. **New gaps opened, deliberately:**
     - **An all-process NChannel op is not per-component-evaluated.** When the per-component split yields
       no spot colorant, both splitters fall back to the name split. This is a *correctness trade made
       knowingly*: placing those components on their plates is better on colour, but returning null would
       leave `ImageCommand.Spots` null, which flips `CmykPageRenderer`'s `InkSourceCategory` from
       `SeparationDeviceN` to `ProcessOther` — moving the op out of Table 148 row 3, so an **overprinting**
       image or stencil would erase a backdrop it used to preserve (the GWG020-class failure SP-6d
       closed). The overprint regression has teeth; the colour approximation does not. Closing this
       properly needs a compositor-side signal for "process-only, preserve plates".
     - **NChannel over a one-channel process space** (`/DeviceGray`, ICCBased `/N 1`) is not
       per-component-evaluated at all. `ColourantComponent.ProcessChannel` indexes the *process space's*
       channels, so a listed name there gets index 0 — byte-identical to `/Cyan` under a four-channel
       space. Measured directly. Mapping a gray channel to a plate is a guess this pass declines to make.
     - **Image spot reversion remains out of scope.** An NChannel image whose spot has no registered plane
       still drops the whole image to the whole-space flatten: an image's tint varies per pixel and no
       per-pixel own-alternate colour is carried anywhere. Reversion lands for fills/strokes only.
     - **Pre-existing, untouched, now precisely located:** `PdfImageToCmyk.TryToCmyk` reaches a corrupt
       colour-space *alternate* unguarded, one call **before** `TryToSpotInk` on the render path
       (`RecordingRenderTarget.cs:139` → `TryToCmyk` → `BuildTintToCmyk` → `AlternateSpaceName` →
       `SpotColorSpace.EnsureAlternate`). `ComponentSplit`'s own call-site catch is therefore
       defence-in-depth for a public method, not the thing standing between the pipeline and a crash.
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
- **G-12 — `cs`/`CS` now cost roughly double, unmeasured.** Fixing row 4-4 added a second full
  `ResolveColorSpace` pass to every `cs`/`CS` operator: `OnColorSpaceChanged` resolves once to compute the
  initial colour, then `OnColorChanged` resolves again to populate the `Resolved*` fields. Each pass repeats
  `PdfFunction.Create` (which re-parses a type-4 PostScript tint transform from its stream every time, with
  no cache), `OverprintPlatesFor`, `PaintsNothing` and `OriginFor`. A generator emitting one `cs` per text
  run (e.g. `/Cs0 cs 0.5 scn` per glyph run) now pays close to double for colour resolution work it
  previously paid once. The corpus gate proves **correctness** — no fixture regressed — but says nothing
  about **throughput**, and nothing in this branch measures or bounds the added cost. Recorded rather than
  fixed: the two passes exist for different reasons (one applies the initial value, the other resolves
  the *current*, possibly just-set, colour) and de-duplicating them needs a design decision about caching
  a parsed tint transform per colour-space resource, not a one-line change.
- **G-13 — Stencil-mask routing after a bare `cs` is untested.** No test exercises `cs` immediately
  followed by an image-mask `Do` with no intervening `scn` — i.e. whether a stencil mask picks up a colour
  space's *initial* colour the same way a fill does. Traced as spec-correct (the initial colour populates
  `CurrentState.FillColor`/`ResolvedFillColor` the same way an explicit `scn` would, and the stencil path
  reads from there), but the *routing* changes for a Separation `cs`: a stencil preceded by
  `[/Separation … /DeviceCMYK …] cs` (initial tint 1.0, no `scn`) now takes `StencilInkFromFill` — the
  spot-ink, overprint-preserving CMYK path — rather than the plain RGBA path a stencil took before row 4-4
  was fixed, because the fill colour it inherits is no longer the graphics state's untouched carry-over.
  That is a different compositing behaviour, not merely a different shade, and no fixture in the corpus
  gate exercises the combination, so it has not been observed rendering correctly, only reasoned about.
  Recorded per this matrix's own discipline (row 4-4) rather than left implicit.

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
