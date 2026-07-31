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
> G-14 close-out (2026-07-29): reserved-name direct application landed in every painting context
> (fills/strokes, shadings/meshes, images, stencils) on the CMYK soft-proof path. Spec:
> `Docs/superpowers/specs/2026-07-29-g14-reserved-separation-direct-design.md`; plan:
> `Docs/superpowers/plans/2026-07-29-colour-g14-reserved-separation-direct.md`. See the G-14 gap entry
> and rows 4-5/4-11 for detail.
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
| 4-5 | Cyan / Magenta / Yellow / Black "are reserved to name the process colourants of a CMYK device" | N | ✅ | **Closed 2026-07-28 (test-debt trio).** `PageColorant.Classify` → `ColorantKind.Process` (`PageColorantClassifyTests`), now also pinned END-TO-END on the CMYK render path: `ReservedAndNoneRenderTests.ReservedName_InRoutedDeviceN_TakesItsPlate_ByClassificationNotRegistration` — a routed DeviceN's `/Magenta` (deliberately NOT registered) takes plate 1 positionally while the registered spot takes its plane, so classification, not registration, is what routes a reserved name. Mutation-verified: deleting `ProcessContribution`'s `case "Magenta"` arm fails this fixture's `m` assertion (and both 5-7 fixtures'). **The DIRECT-APPLICATION half for a pure reserved-name Separation was gap G-14 — CLOSED 2026-07-29.** This row's claim is the reserved-name identity, which holds; what a lone unregistered `Separation /Cyan` paints is 4-11's availability policy — now direct application on the CMYK soft-proof path, pinned by `ReservedSeparation_Unregistered_AppliesTheProcessColourantDirectly` (Pellucid, replacing the retired `..._FlattensThroughItsAlternate_G14Baseline`). See the G-14 gap entry and row 4-11 for the full closure. |
| 4-6 | **All**: "painting operators shall apply tint values to all available colourants at once" | N | ✅ | Fixed 2026-07-25 (was the G-2 gap). Scope is **fills and strokes**: `CmykPageRenderer.CompositeInk` (the only call site that can reach the `/All` branch — the shading and mesh sites pass an origin with empty `Tints`, which the guard excludes per G-7, and the image site passes no origin at all) branches on `InkDecision.AllColourants` and, when set, paints the four process plates *and* loops `SpotColorantRegistry.PlaneNames` to cover every registered spot plane — availability as this document's device policy defines it. The four process plates themselves come from `InkDecider.cs:~106` (CMYK path) and `ColorSpaceResolver.cs:250-253` (RGB path); `ColorSpaceResolver.PlatesForColorSpaceObject` (~line 653) also produces four true plates for `/All`, but that is the **overprint mask**, a different computation reached from a different call site, not the paint loop. `AllColourantRoutingTests` asserts all four plates and both registered spot planes, and was mutation-checked by capping the loop at one plane. Images and stencil masks on the CMYK path are NOT covered by this row — see G-9. |
| 4-7 | **All** on an additive device: "the subtractive tint values […] shall be complemented by subtracting from 1 before applying to all available colourants" | N | ✅ | Fixed 2026-07-25 (was the G-3 violation). `ResolveSeparation` complements before reading the alternate space, so tint *t* paints the neutral 1−*t* on R, G and B. The device fork is honoured on both sides: the additive complement lives in the engine, and the subtractive path applies the tint directly via `InkDecider` (G-5, closed). Pinned by `SeparationAll_At{Full,Zero}Tint_*` and `All_colourant_applies_the_origin_tint_*`. |
| 4-8 | **None**: "shall not produce any visible output […] shall have no effect on the current page" | N | ✅ | **Was a violation, not ⚠️ — fixed 2026-07-25.** The line cited in slice 1 (`:594`) is the overprint *plate mask*, not the paint path. `PdfGraphicsState.Fill/StrokePaintsNothing` suppress the operator at the same sites as `OcHidden`. Coverage is now **every painting operator**: f, S, B (per-half), glyphs incl. Type 3, image XObjects (incl. `/Indexed` over a `/None` base), inline images, stencil masks (gated on the FILL signal, since a stencil has no colour space of its own) and `sh`. Shading *patterns* are the one remaining route — see G-8. Glyph suppression for mode 4 (fill + clip) has its own caveat — see G-10: the fill is correctly suppressed, but so is the clip it was meant to add, which is not itself "no effect on the current page". |
| 4-9 | "A PDF processor shall support Separation colour spaces with the colourant names All and None on all devices" | N | ✅ | Handled and tested for **fills and strokes** on both devices (rows 4-7, 4-8). For `/All` on the CMYK soft-proof path, the paint loop lives in `CmykPageRenderer.CompositeInk` (fed by `InkDecider`'s arm, `InkDecider.cs:~106`) and is pinned by `AllColourantRoutingTests` (G-5, closed) — but that test exercises fills/strokes only; `/All` images and stencil masks diverge on the CMYK path (G-9), so "all devices" does not yet hold for every painting operator. For `/None`, "on all devices" still holds *structurally* rather than by a dedicated CMYK-path test: suppression sits in `PdfGraphicsState`/`ColorSpaceResolver`, upstream of every render target, so the soft-proof path cannot bypass it, but no test exercises `/None` on the CMYK path directly. |
| 4-10 | For All/None, "PDF processors shall ignore the alternateSpace and tintTransform parameters" | N | ✅ | **Was a violation — fixed 2026-07-25.** `ResolveSeparation` evaluated the transform for every colourant name, so `/All` painted whatever it returned. Both names are now handled before the alternate space or the transform is read. Pinned by `SeparationAll_AtFullTint_IgnoresTintTransformAndPaintsBlack`, whose space ramps to red: evaluating the transform paints red, ignoring it paints the required black. |
| 4-11 | "the PDF reader shall determine whether the device has an available colourant […] If so […] shall apply the designated colourant directly" | D | ⚠️ | Soft-proof path only, audited D-1. **Availability rule rewritten 2026-07-29 (G-14 close):** available = registered in `SpotColorantRegistry` **OR** the name is a reserved process colourant (Cyan/Magenta/Yellow/Black) — `ColorSpaceResolver.AllReservedProcessOrNone`/`ReservedChannelOf`/`ColorantNamesOf` (engine) and `InkDecider`'s reserved-direct arm (Pellucid) implement the rule; a reserved name now applies its process colourant directly instead of falling through to the registry check. **Closed for fills/strokes, shadings/meshes (`ShadingBuilder.PackByReservedName`), images (`PdfImageToCmyk.TryToCmyk` reserved route) and stencil masks (`StencilInkFromFill` process-only empty-Names ink, gated by `CmykPageRenderer`'s relaxed empty-Names check).** Pinned by `ReservedSeparation_Unregistered_AppliesTheProcessColourantDirectly` (fills/strokes, replacing the retired `..._FlattensThroughItsAlternate_G14Baseline` baseline), `Separation_Black_CalGrayAlternate_RoutesDirectly_G14` plus its negative control `Separation_SpotName_CalGrayAlternate_StillReverts` (replacing `Separation_with_a_CalGray_alternate_still_reverts` — scope half preserved: a non-reserved spot name still reverts through a CalGray alternate), and `G14_ReservedSeparation_BuildPacksTheLastStopDirectly` (engine-level, `ShadingBuilder.Build` — the shading pin lands at the engine level, not render level; see G-14 residual (c)). **Still ⚠️, not ✅ — one caveat survives this pass:** an **Indexed image over an all-reserved base still flattens** (out of scope this pass — G-14 residual (a)); the stencil fix also requires the spot-plane-buffer configuration (spots + registry passed, the standard soft-proof path — residual (b)), which is the ordinary case but worth naming since a bare empty-config stencil is untested. Gate outcome: GWG 51/51, NChannel 3/3 unaffected structurally; exactly two digests re-pinned (`GWG030_Gray_K_black_OP_X1`, `GWG230_Four_different Grays_x1a`), both value-only sub-perceptual quantisation deltas verified against each fixture's own `_ReadMe` criterion — GWG230 now matches its DeviceCMYK reference exactly. Invisible in any well-formed file whose reserved-name alternate already ramps to the same colour; visible under a lying alternate. See the G-14 gap entry for the full closure record. |
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
| 5-3 | **"For NChannel colour spaces, the components shall be evaluated individually; that is, only the ones not present on the output device shall use the alternate colour space of that component."** | N | ⚠️ | **Was ❌ VIOLATION. Implemented 2026-07-27 (Pass 2b), for fills/strokes and images — NOT a clean ✅, and the exclusions below are the reason.** Fills/strokes: `InkDecider.TryPerComponent` routes each Process component to its `/Process /Components` **position** (Table 71 makes position the channel identity, which a name cannot carry), routes registered spots to their planes, and reverts unregistered spots through their own `/Colorants` alternate. Images/stencils: `PdfImageToCmyk` splits by role and channel rather than by name. **Evidence:** veraPDF `6-2-4-4-t02-pass-a` renders C=0.36 M=0.57 Y=0.02 K=0.0, asserted positionally on the real file, against a measured pre-change `C=0 M=0.36 Y=0.57 K=0.02` — its tint transform is an identity pass-through, so the whole visible defect was a channel permutation, and only a positional assertion can see it. Mutation-verified: routing by position instead of channel reproduces the pre-change tuple exactly. **Still excluded:** ~~shadings and meshes **with a spot component** (sites 3/4, still open — no per-op tint reaches `InkDecider`, so name-based routing stands there);~~ a one-channel (`/DeviceGray` or ICCBased `/N 1`) process space, where channel 0 is not the cyan plate; an all-process NChannel *image or stencil* (see G-4's note — the overprint category, not the colour, decides that); and spot reversion for images. **Closed 2026-07-28 (G-7 sites 3+4, engine `6bcaa38` / Pellucid `37f7c5b`):** ~~shadings and meshes drop out of this row's exclusion list entirely~~ — **corrected 2026-07-28 (final review): that overstates it.** Routing here is all-or-nothing — `CmykPageRenderer.cs:621-625` flattens the *whole* shading to the whole-space alternate the moment one spot has no registered plane — so shadings and meshes *narrow* out of this row's exclusion list rather than dropping out of it entirely; the surviving exclusion is reversion of an unregistered spot specifically, exactly as row 5-10 (below) states it. `ShadingSpotSplit.SplitByPlacement`, the placement-preferring wiring in `ShadingBuilder`/`MeshShadingReader` (including `hasProcess`), and `InkDecider.ProcessContribution`'s placement-derived mask now route a shading or mesh's *registered* spot and process components the same way fills/strokes and images already do, falling back whole to the name-driven path only when a space has no placement, or when a spot in it is unregistered. See the G-7 gap entry below for why the two sites had to land together and for the corpus evidence, which is synthetic. **Narrowed 2026-07-28 (G-7 site 5, `25f0f23`):** an all-process NChannel shading or mesh — no spot component at all — is no longer excluded from this row. `ShadingBuilder.BuildCmykMapper` now evaluates its components individually, packing each onto its own plate instead of running the tint transform (see the G-7 site-5 entry below). That path is independent of `InkDecider`, so it narrowed this row's exclusion list separately from — and before — sites 3/4's closure above. **Reversion has no corpus instance anywhere** — synthetic fixtures plus plane-cap invariance only. **Hook status 2026-07-29:** the row's *closed* claims carry pins (the veraPDF positional assertion, mutation-verified; G-7 sites 3/4/5's entries). Its three surviving exclusions are **unpinned and deliberately so this pass**, which was scoped to G-8…G-13: (i) a one-channel `/DeviceGray`/ICCBased-`/N 1` process space where channel 0 is not the cyan plate — engine-side and cheaply pinnable, **logged as the strongest candidate for the next pass**; (ii) an all-process NChannel image or stencil, where G-4's overprint-category note governs rather than this row; (iii) spot reversion for images, which the row already explains is structurally impossible to express per-pixel. None acquired a new test here. |
| 5-4 | tintTransform "shall be called with n tint values and returns m colour component values" | N | ✅ | Same test: a type 4 transform maps (t₁, t₂) → (0, t₁, t₂, 0), so a dropped or transposed component paints a visibly different colour. Verified by mutation — transposing the oracle fails. |
| 5-5 | **None** "may be present only for DeviceN colour spaces that do not have the NChannel subtype" | F | — | Constrains where `/None` may appear in a file. Previously recorded as blocked on G-4 because it needs DeviceN `/Subtype` awareness — as a validator row, that read belongs to the validator, so the dependency does not apply here. **Validator gap** — `PdfxNChannelColorantsRule` reads `/Subtype` but checks `/Colorants` presence, not `/None` placement. |
| 5-6 | None "indicates that the corresponding colour component shall never be painted on the page" | N | ✅ | **Closed 2026-07-28 by AUDIT-AND-CITE, not new tests** (test-debt trio, spec §6a.1): this cell was stale — both named contexts already carry real pins. Shading split: `ShadingSpotSplitTests.Split_AllNone_ContributeNothing` (name arm) and `SplitByPlacement_NoneContributesNothing_ToAnyPlateOrSpot` (placement arm — "/None's 1.0 went nowhere", per-slot positional). Image split: `PdfImageToCmykTests`' GWG080-shaped fixture (real parsed `/DeviceN [/Black /PANTONE 265 C /None /None /None]` with a `/Lab` alternate through `TryToSpotInk`; the three `/None` channels carry live values that must contribute nothing, asserted per-plate). Fill/stroke contexts are row 5-7's. |
| 5-7 | "When […] painting the named device colourants directly, colour components corresponding to None colourants shall be discarded" | N | ✅ | **Closed 2026-07-28 (test-debt trio).** Both direct-painting arms pinned. Per-component (NChannel): `InkDeciderTests.NChannel_None_component_is_discarded_not_reverted`, including the poisoned own-alternate (`[1,1,1,1]`) a malformed file's `/Colorants /None` entry would supply — it must be ignored. Routed (named-colorant): `ReservedAndNoneRenderTests.None_InRoutedDeviceN_IsDiscarded_ItsTintAppearsNowhere` (the `/None` tint 0.9 appears on no plate and no plane, positionally) and `None_InRoutedDeviceN_SetsNoMaskBit_BackdropSurvivesOnUnnamedPlates` — the observable with teeth: under overprint `/None` contributes no mask bit, so a pre-painted backdrop survives on every plate the space does not name. Mutation-verified: a `case "None": k = tint; pk = true;` arm fails both (0.9 lands on K); forcing the routed mask all-true fails the backdrop fixture. NOTE the direct-painting precondition: a plain DeviceN with NOTHING registered correctly reverts WHOLE (row 5-8's rule — `/None` then flows INTO the transform), so these fixtures carry a registered spot to force the routed arm. |
| 5-8 | "when the DeviceN colour space reverts to its alternate colour space, those components shall be passed to the tint transformation function" | N | ✅ | Audited 2026-07-26 — **conformant**. `ColorSpaceResolver.ResolveDeviceN` already evaluated the tint transform over every component unfiltered, `/None` included, so reversion was correct before this pass; it simply had no test. Pinned by `DeviceNNoneReversionTests.DeviceN_Reversion_PassesNoneComponentsToTheTintTransform`, mutation-verified: filtering `/None` out before calling the transform (the bug this row exists to catch, and the mirror image of row 5-7's discard-when-direct rule one paragraph away) makes the test fail with the wrong yellow plate. Mutation reverted; suite green again. |
| 5-9 | All-None space "shall always discard its output […] it shall never revert to the alternate colour space" | N | ✅ | Implemented 2026-07-25 via `ColorSpaceResolver.PaintsNothing`, which treats an all-`/None` DeviceN exactly like `/Separation /None` — so it is never flattened through its tint transform on the way to painting nothing. `AllNoneDeviceN_DiscardsOutput_WithoutRevertingToItsAlternate` paints over red with a transform ramping to magenta. |
| 5-10 | "Reversion shall occur only if at least one colour component (other than None) is specified and is not available on the device" | N | ⚠️ | Cited verbatim in `PdfImageToCmyk.TryToSpotInk` (SP-6c). **Narrowed 2026-07-27 (Pass 2b):** for **fills and strokes** reversion is now genuinely per-component — only a spot with no registered plane takes its own alternate, and the components that *are* available are painted directly, which is what this row and 5-3 together require. Still ⚠️, for three reasons: reversion remains whole-space for **images** (an image's tint varies per pixel and no per-pixel own-alternate colour is carried anywhere); ~~it remains whole-space for shadings/meshes (G-7);~~ **narrowed 2026-07-28 (G-7 sites 3+4 closed):** a shading or mesh now routes a *registered* spot to its plane and a Process component to its plate per-component, the same as fills/strokes — so this row's shading/mesh exclusion narrows to **reversion of an unregistered spot** specifically, which still has no per-sample own-alternate colour to revert through and so still takes the whole-space alternate (explicitly out of scope — design doc §8); and **no corpus file anywhere exercises reversion at all** — not GWG, not veraPDF — so the per-component path is covered by synthetic fixtures plus the plane-cap invariance property test and by nothing else. **Hook status 2026-07-29: unpinned, with the reason already stated in-row rather than missing.** Both surviving exclusions — whole-space reversion for images, and reversion of an unregistered spot in a shading/mesh — lack a per-sample own-alternate colour to revert through, so there is no observable to pin without first building the carrier the design doc §8 explicitly deferred. The hook for this row is therefore the design deferral, not a test. Nearest live coverage: the synthetic per-component fixtures and the plane-cap invariance property test named above. |
| 5-11 | Subtype "shall be DeviceN or NChannel. Default value: DeviceN" | N | ✅ | **Was ❌. Closed 2026-07-27 (Pass 2b).** `/Subtype` is read on the render path by both halves: `ColorSpaceResolver.BuildComponents` gates the whole per-component carrier on `space.IsNChannel`, and that gate is what keeps a plain DeviceN (`Components == null`) on the unchanged whole-space path. The default is honoured — `SpotColorSpace.cs:296` defaults an absent `/Subtype` to `"DeviceN"` per Table 70. **Evidence, stated precisely because an earlier draft of this cell overstated it:** the ✅ rests on `:1030` gating `Components`, and on `Components == null` being what keeps a plain DeviceN off the per-component path — both traced on the Pass 2b-compositor branch. There is a *second*, distinct `IsNChannel` gate at `BuildTintRamp` (`ColorSpaceResolver.cs:527`) whose removal changes a plain DeviceN's **ramp**; that one was **hand-traced** by the Pass 2a′ reviewer, not run, and the prediction that the GWG gate would catch it has **never been executed**. Do not cite it as mutation evidence. |
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
| Device-policy (**D**) | 2 | 2 — 4-12 tested; 4-11 (soft-proof) tested and closed for fills/strokes, shadings/meshes, images and stencils as of 2026-07-29 (G-14), still ⚠️ on the Indexed-image residual — see Delta 2026-07-29 |

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
5-7, 5-10) whose behaviour lives on the CMYK soft-proof path, where no direct test yet exists, and ~~G-4's
two ❌ rows (5-3, 5-11) — NChannel colour spaces are not implemented on the render path at all, the one
substantive violation this matrix has tracked since slice 1~~.

> **Snapshot, not current state.** The paragraph above describes the 2026-07-26 ␀-sweep close, before
> Pass 2b and G-7 touched either row. As of this document's latest update, row 5-11 is **✅** (closed
> 2026-07-27, Pass 2b) and row 5-3 is **⚠️** (raised from ❌ the same day, narrowed further by G-7 sites
> 3+4 and site 5 on 2026-07-28) — see their cells above, not this paragraph, for current status.

> **Delta 2026-07-28 (test-debt trio).** Rows 4-5, 5-6 and 5-7 close ✅ — 4-5 and 5-7 on new
> render-level positional pins (`ReservedAndNoneRenderTests`, Pellucid `551eb12`), 5-6 by
> audit-and-cite of pins that already existed (its cell had gone stale). **N class now:
> 18 ✅ / 2 ⚠️ (5-3, 5-10) / 0 ❌.** The same pass's Task 0 measurement opened **G-14**
> (unregistered reserved-name Separations flatten through their alternate; ruled a gap against the
> "Adobe or better" bar — see the gap entry and row 4-11), so the D class gained its first
> measured, pinned divergence.
>
> **Delta 2026-07-29 (G-14 close-out).** **G-14 is CLOSED.** Row 4-11's availability rule now reads
> "registered OR reserved process name" and is closed for fills/strokes, shadings/meshes, images and
> stencils on the CMYK soft-proof path — but the row **stays ⚠️, not ✅**: an Indexed image over an
> all-reserved base still flattens (residual (a), out of scope this pass), so the row's own caveat
> list is not fully empty. **N class unaffected — 4-11 is class D**, not N; the N counts above are
> unchanged by this delta. D class is now **2 — 4-12 tested, 4-11 tested for its closed contexts,
> ⚠️ for residual (a)**. Gate outcome: GWG 51/51, NChannel 3/3, two digests re-pinned
> (quantisation-only, verified against each fixture's own criterion — see the G-14 gap entry). Engine
> suite 2694/2694; Pellucid `Pellucid.Rendering.Avalonia.Tests` 547/547.

> **Delta 2026-07-29 (release hooks).** Every open gap now carries a measured hook: G-8, G-10 and
> G-11 baseline pins; G-9 unit pins on both decline sites; G-12 a counted-resolve pin (**4** per
> `cs`+`sc`, measured exactly as predicted) via the new `ColorSpaceResolver.ResolveCallCount`;
> G-13 observed green (no longer reasoned-only); and the G-14 Indexed residual (a) pinned at the
> image routes. `PageColorantReader`'s defensive catch now logs instead of swallowing. A future fix
> for any of these starts by flipping its pin red — none can land half-done or unnoticed.
> **Two corrections this pass, both to prediction rather than to code:** G-8's pin measured **white
> RGB(255,255,255)**, not the predicted constant black — still a *paints* outcome (the fixture's red
> backdrop is what "paints nothing" would leave); the tint transform is never evaluated (`BuildTintToRgb`
> declines the `/None` space, `BuildColorMapper` falls back to reading the shading function's single
> 1.0 tint as grey), explained during independent review; and G-10's two line references were
> stale (`:947`→`:949`, `:1266`→`:1307`). **Also corrected during independent review:** the G-14
> residual (a) fixture's `/Lookup` placeholder was a `PdfName`, which `ResolveLookup` rejects before
> reaching the base colour space, so the pin measured malformed-lookup validation, not G-14; it now
> uses a real lookup string so the decline happens for the intended reason and the pin can flip on
> the fix. **Unpinned by decision, not omission:** G-14 residual (b)
> needs a Pellucid no-spot-buffer harness; residual (c) is pinned engine-level by design; rows 5-3
> and 5-10 carry `Hook status` notes naming their surviving exclusions, of which **5-3's one-channel
> process space is the strongest candidate for the next pass**. Engine suite 2661/2661 at the time of
> the pins (`Category!=LocalOnly`).
>
> **One NEW gap was found by this pass's own review, not by the pass:** **G-15** —
> `BuildColorMapper` conflates "cannot map this space" with "this space must paint nothing" and
> fabricates a colour for both. It surfaced only because the review refused to accept the G-8 pin's
> unexplained white and traced it to ground. It is **unpinned**, and it is the one open colour gap
> with no hook — recorded so that the accounting stays honest rather than looking complete.

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
  so `Tints` is empty: there is no single per-op tint, because the tint varies across the ramp. ~~Such an
  op falls through to the flattened path.~~ Correct handling needs the `/All` rule applied per-sample
  inside the ramp evaluation, not once per op.

  > **Corrected 2026-07-27 by the G-7 colorant-placement plan (Task 0 / Task 4), superseding "such an op
  > falls through to the flattened path."** That was false, and had been for some time:
  > `ShadingBuilder.cs:73-97` already builds a per-stop spot split (SP-7) and `MeshShadingReader.cs:61` a
  > per-vertex one (SP-7-mesh), both consumed by the compositor — `CmykPageRenderer.cs:611` for the
  > per-stop split, `:806` for the mesh one. What survives from the original sentence is correct and
  > load-bearing: `rawColor: null` leaving `origin.Tints` empty is exactly what keeps a shading out of the
  > fills/strokes machinery — it does not mean the shading goes unhandled.
  >
  > The real position, split into its genuine sub-gaps:
  >
  > - **Landed.** The placement carrier exists: `ColorantOrigin.Placement`, a colorant→slot table —
  >   `ColorantSlotKind.Nothing` / `.Plate` / `.Spot`, built via `ColorantSlot.Nothing` /
  >   `ColorantSlot.Plate(int plateIndex)` / `ColorantSlot.Spot(int spotIndex)` — computed in
  >   `ColorSpaceResolver.OriginForColorSpaceObject`. ~~**Nothing consumes it yet** — verified by grep
  >   across both the PDF and Pellucid repos.~~
  >
  >   **Corrected 2026-07-28, superseding "nothing consumes it yet."** That was already stale the same
  >   day it was written: `ShadingBuilder.BuildCmykMapper`'s `AllProcessPlacement` (site 5, engine
  >   `25f0f23`) consumed `Placement` first, closed earlier the same day and already merged. Two more
  >   consumers land below in this same list: `ShadingSpotSplit.SplitByPlacement` (site 3, engine,
  >   `colour/g7-sites-3-and-4` @ `6bcaa38`) and `InkDecider.ProcessContribution` (site 4, Pellucid @
  >   `37f7c5b`) both consume `Placement.Slots` — landed together, each still on its own unmerged
  >   branch, not yet on either repo's default branch.
  > - ~~**Still open — site 3.** `ShadingSpotSplit.Split` (`PdfLibrary/Rendering/ShadingSpotSplit.cs`) still
  >   switches on the literal names Cyan/Magenta/Yellow/Black, so an NChannel colorant named e.g.
  >   `/PrCyan` is routed to a spot plane instead of the cyan plate.~~
  > - ~~**Still open — site 4.** `InkDecider.ProcessContribution` (Pellucid, `:446-468`) derives the
  >   process-plate mask from the same literal names.~~
  >
  > **Closed 2026-07-28 — sites 3 and 4, landed together** (engine `colour/g7-sites-3-and-4` @
  > `6bcaa38`, Pellucid @ `37f7c5b`), superseding both "still open" lines above. Site 3:
  > `ShadingSpotSplit.SplitByPlacement` plus placement-preferring wiring in `ShadingBuilder` and
  > `MeshShadingReader`, including `hasProcess`, routing by `Placement.Slots` instead of the literal
  > name switch. Site 4: `InkDecider.ProcessContribution` derives its plate mask and tints from
  > placement. Both fall back whole to the name-driven path when a space has no placement.
  >
  > **Why together, measured rather than argued.** Landing site 3 alone flips a mixed NChannel op — a
  > registered spot alongside a Process colorant the name switch mislabels — from the compositor's
  > flatten arm onto its routed arm, exactly the shape the "two name-switch sites must land together"
  > bullet below predicted. There, the mask at `ProcessContribution` was still name-based and returned
  > `(F,F,F,F)`, so `anyProcess` went false at `CmykPageRenderer.cs:697` and the process split was never
  > composited. Measured on a real mixed NChannel axial shading: **C 0.3608 → 0 at overprint** — not a
  > colour shift, ink loss. There is a second, independent channel with the same shape in the mesh
  > reader: leaving `hasProcess` name-based while the spot names go placement-based nulls
  > `MeshSpotInk.VertexProcessCmyk`; that one bites at **knockout**, where site 4 cannot repair it
  > because the knockout mask is forced all-true before any name matching happens.
  >
  > **The evidence is synthetic, not corpus — stated plainly rather than left to be inferred from a
  > green gate.** Task 0's M4 corpus census for this closure, over **3 005** files / **13 233** pages,
  > finds **7** mixed NChannel shadings (a registered spot alongside a process colorant) and **0** where
  > the name split and placement disagree — every one of the 7 agrees because its only process colorant
  > is literally named `Cyan`. **No NChannel mesh exists anywhere in the corpus.** The walk covers page
  > `/Resources`, Form-XObject and tiling-pattern `/Resources`, and — going beyond production
  > `PageColorantReader`'s own walk — annotation appearance streams and soft-mask groups; adding those
  > two raised reached shadings from 60 to 76, none of the additional 18 NChannel, which is what makes
  > the 7 an **absolute** for this walk rather than a lower bound. Type 3 font `/CharProcs` remains
  > unwalked. **No render-hash gate can observe site 3 or site 4 as landed** — the corpus simply
  > contains no instance where the fix changes the outcome. The gates that ran (GWG **51/51**, NChannel
  > **3/3**, both 0 differences, engine commit SHA `6bcaa38` verified embedded in the build under test)
  > are a **guard against unintended movement, not evidence the fix works** — the evidence that the fix
  > works is the unit and builder-level tests, each verified red-by-assertion under mutation. Suites:
  > engine 2672/0 (net8.0/net9.0/net10.0, 0 warnings), Pellucid 1309/0.
  > - **Closed 2026-07-28 — site 5.** ~~`ShadingBuilder.BuildCmykMapper`'s all-process arm still runs
  >   the tint transform even when every component is Process with a determinable channel — i.e. it
  >   simulates inks the device actually has, the mirror-image defect to sites 3/4.~~ Superseded by
  >   commit `25f0f23`: `BuildCmykMapper` (`PdfLibrary/Rendering/ShadingBuilder.cs:155-156`) now checks
  >   `AllProcessPlacement` (`:189` — non-null iff `ColorantOrigin.Placement` is non-null **and**
  >   `Placement.SpotNames.Count == 0`, i.e. every colorant is Process with a plate) and, when it
  >   holds, packs components straight onto their plates via `PackByPlacement` (`:200`) instead of
  >   building the tint transform. `BuildCmykMapper` has exactly two production callers
  >   (`ShadingBuilder.cs:66`, `MeshShadingReader.cs:57`), so this single change **covers axial, radial
  >   and mesh together** — `MeshShadingReader.cs` itself is unmodified.
  >
  >   **Tightened 2026-07-28 by the final whole-branch review (IMPORTANT 1), superseding "non-null iff
  >   ... `SpotNames.Count == 0`."** That predicate alone is also satisfied when EVERY colorant is
  >   `/None` — `Placement.Slots` is then `[Nothing, Nothing, …]` with no `Plate` slot at all, so the
  >   bypass fired and `PackByPlacement` returned `0x00000000` for a space `BuildTintToCmyk` had always
  >   refused via its own `PaintsNothing` check (`ColorSpaceResolver.cs:461`). `AllProcessPlacement` now
  >   additionally requires `placement.Slots.Any(s => s.Kind == ColorantSlotKind.Plate)`, so an all-`/None`
  >   space declines the bypass and falls through to `BuildTintToCmyk`/`PaintsNothing` as before. Covered
  >   by `AllNoneNChannel_DoesNotBypass_SoTheTintPathStillRefusesIt` in
  >   `ShadingAllProcessNChannelTests.cs`.
  >
  >   **The evidence is synthetic, not corpus.** Task 0's M3 found **zero** all-process NChannel
  >   shadings or meshes across 3 005 corpus files (7 NChannel shadings total, every one carrying a
  >   spot; 0 NChannel meshes), so no render-hash gate can move on this fix. The gate (51 GWG + 3
  >   veraPDF fixtures, both green, embedded engine SHA equal to `25f0f23`) is therefore a **guard
  >   against unintended movement, not evidence for the fix** — the evidence is the commit's per-plate
  >   synthetic assertions.
  >
  >   **Scoped 2026-07-28 by the final whole-branch review (Finding 5).** "Zero" here is zero among
  >   what M3 walked, not an absolute zero — state it as a lower bound. M3's method (Task 0's report,
  >   §4) inspected every page's `/Shading` and `/Pattern` resources, recursing into Form-XObject
  >   `/Resources` and tiling-pattern `/Resources`. It did **not** descend into annotation appearance
  >   streams or soft-mask group `/Resources` — both are separate resource trees this walk never
  >   visited. Since this count is the row's load-bearing safety claim (it is the reason no
  >   render-hash gate can move on the fix), an all-process NChannel shading or mesh reachable only
  >   through an annotation appearance stream or a soft-mask group would not have been counted either
  >   way, so "zero corpus instances" should be read as "zero among page content, Form XObjects, and
  >   tiling patterns" rather than "zero, full stop."
  >
  >   **The plate mask can change, and this is recorded rather than smoothed over.** `OverprintPlates`
  >   is null for this space, so at `op=true` the compositor's process mask is the nonzero-markedness
  >   proxy against the per-pixel colour. For an all-process shading or mesh with at least one
  >   zero-valued component whose permuted destination plate differs from its pre-permutation one, the
  >   marked set moves — measured at `[0.0, 0.57, 0.02, 0.80]`: `{M,Y,K} → {C,M,Y}`, one plate gained
  >   and one lost. A gained plate paints where a backdrop used to survive; a lost plate preserves one
  >   that used to be overpainted — an overprint-behaviour change, not merely a colour change. With
  >   every component non-zero the mask is `[CMYK]` on both sides (a property of that vector, not of
  >   the fix), and at `op=false` the mask is fixed `(T,T,T,T)` and unaffected.
  >
  >   **Corrected 2026-07-28 by the final whole-branch review (Finding 3 / MINOR 3), superseding
  >   "`OverprintPlates` is null for this space."** That is not a property of the SHAPE (an
  >   all-process NChannel with a permuted `/Process /Components`) — it is a property of THIS
  >   fixture's colorant names, which are the non-reserved `PrCyan`/`PrMagenta`/`PrYellow` (plus
  >   `Black`). `PlatesForColorSpaceObject` (`ColorSpaceResolver.cs:789-806`) reads `space.Names` — the
  >   array's own colorant names — and returns null only when one of them is not among the six it
  >   recognises (`Cyan`, `Magenta`, `Yellow`, `Black`, `All`, `None`); it never looks at `/Process
  >   /Components` at all. So a *different*, equally legal all-process NChannel whose colorant names
  >   ARE the four reserved ones — `[/Cyan /Magenta /Yellow /Black]` — but whose `/Process
  >   /Components` lists them in non-canonical order still hits this row and still gets its bypass:
  >   `PlatesForColorSpaceObject` returns `(T,T,T,T)` for that space (every name is reserved), while
  >   the permutation defect — which lives entirely in `/Process /Components`, a table
  >   `PlatesForColorSpaceObject` never reads — still fires. **The conclusion survives**: a fixed
  >   `(T,T,T,T)` mask at `op=false` is unaffected either way, which is all this row's stated behaviour
  >   depends on. But the reason is "this fixture's names happen to be non-reserved", not "this space
  >   has no plate mask" — the same fix over reserved names would keep a non-null mask throughout.
  > - **The two name-switch sites must land together — measured, not argued, and this is the MIXED
  >   case specifically:** an NChannel shading with a registered spot alongside a process colorant the
  >   name switch mislabels, e.g. `[PrCyan(Process ch0), Spot1(registered)]`. Fixing site 3 alone removes
  >   `PrCyan` from the spot-name list, which flips `routeShadingSpots` from False to True, which routes
  >   the op to `ProcessContribution` — still name-based — whose mask comes back `(F,F,F,F)`, so
  >   `anyProcess` is false at `CmykPageRenderer.cs:697` and the process split is never composited.
  >   Measured: today that op flattens and paints C=0.3608 M=0.5020; after site 3 alone, `PrCyan`'s 0.36
  >   is lost outright. **The all-process case is different and was measured safe** (Task 0's M1c):
  >   `routeShadingSpots` is already False there today, with or without site 3, so fixing site 3 alone is
  >   a no-op on that arm — the regression is specific to the mixed shape.
  > - **Still open.** `/All` shadings (row 4-6) and per-stop spot reversion for unregistered spots
  >   (row 5-10).
  > - ~~**Still open — an unbounded slot index, in both repos**~~ **Closed 2026-07-28.** `ColorantSlot`'s
  >   constructor now validates the index against its kind (Plate in [0,4), Spot ≥ 0, Nothing = 0), and
  >   `ColorantPlacement`'s constructor refuses a Spot index ≥ `SpotNames.Count` — the bound the slot
  >   alone cannot check, and the one that actually closed the silent adjacent-stop write at
  >   `ShadingSpotSplit.cs:80`. Duplicate in-bounds spot indexes remain legal (Pellucid's mutation
  >   fixtures construct that shape deliberately). Covered by the construction-validation tests in
  >   `ColorantPlacementTests.cs`. Original record follows.
  >
  > - **Recorded 2026-07-28 by G-7 Plan 3's final
  >   review.** Both placement consumers index a 4-element process buffer with `slot.Index` directly.
  >   `ColorantPlacement.Build` bounds every Plate index via `ColorSpaceResolver.ProcessChannelFor`, and
  >   it is the only production construction path in either repo, so **there is no reachable defect
  >   today** — but `ColorantSlot`'s public constructor permits `new(Plate, 9)`, the same
  >   inconsistent-construction hazard the "branch on `slot.Kind`, never on `slot == ColorantSlot.Nothing`"
  >   rule exists to guard. The two are **not** equally severe:
  >   `Pellucid`'s `InkDecider.cs` writes through a bounds-checked `Span<float>` and would **throw**,
  >   whereas `ShadingSpotSplit.cs:80`'s `spotDest[destOffset + slot.Index]` writes into `stopTints` and
  >   an over-range Spot index lands inside a **different gradient stop's tint slice — silent
  >   corruption, no exception.** That one is the worse of the pair and was missed by every per-task
  >   review; it surfaced only in the whole-branch pass. If this is ever acted on, the fix is one
  >   validating constructor on `ColorantSlot` (or an assert in `Build`), not scattered bounds checks
  >   across two repos.
  > - **Still open — the placement branch's *tint* half has little production reach.** For any
  >   well-formed mixed NChannel **fill**, `InkDecider.TryPerComponent` succeeds and the routed arm is
  >   never taken, so `ProcessContribution`'s placement branch is reached mainly by shadings and meshes —
  >   whose `ColorantOrigin` resolves with `rawColor: null`, leaving `Tints` **empty** and every tint it
  >   reads 0. The mask half is what carries the fix. Reachability itself is unpinned: the unit fixtures
  >   construct a `Placement` directly rather than arriving through `Decide`'s routing, so a future change
  >   that stopped `TryPerComponent` declining would leave those tests green while the fixed code stopped
  >   firing. The cross-repo join test (`Pellucid`, `NChannelPerComponentRenderTests`) covers the shading
  >   path end to end; no equivalent pins the fill path's routing.
  > - ~~**Still open — migration of Pass 2b's two shipped sites onto Placement.** Sites 1
  >   (`PdfImageToCmyk`) and 2 (`InkDecider.TryPerComponent`) still carry their own
  >   position/role-derived split rather than consuming `ColorantOrigin.Placement`, the carrier sites 3,
  >   4 and 5 already read — two implementations of one physical rule, recorded rather than argued away
  >   in the parent design's §4.4.~~ **Closed 2026-07-28.** Site 1 (engine `d56c22f`) and site 2
  >   (Pellucid `5457fd5`) now both consume `ColorantOrigin.Placement`; `ColorantPlacement.Build` is the
  >   only code in either repo that turns `ColourantComponent.Role`/`ProcessChannel` into slots. Zero
  >   behaviour change: GWG 51/51 and NChannel 3/3, both 0 differences, embedded engine SHA verified
  >   equal to the merge commit; engine suite 2685/0 and Pellucid suite 1315/0. The three M4-measured
  >   refusal divergences (R1: site 2 refuses a null-`Tint` Process component the table would place; R2:
  >   site 2 refuses an unregistered spot with no own alternate the table would still slot; R3: site 1
  >   refuses a no-spot split the table succeeds on, site 2 does not — the asymmetry is load-bearing, per
  >   site 1's I-1 category-flip guard) are preserved verbatim at their sites. One additional divergence
  >   was found and accepted during the migration, **R4**: a mixed NChannel `/All` component with a
  >   tinted own-alternate `/Colorants` entry reverted through its own alternate before the migration and
  >   refuses-to-flatten after it — corpus-unreachable, and the post-migration reading is the more
  >   correct one (see the migration design's §4, dated-correction block, for the full argument). Engine
  >   merge `66b11565e42839ff57459daa116c28d835efb757`, Pellucid merge
  >   `fa0c76e19fad11e21a52344e0d175b7308a11adf`. Design:
  >   `Docs/superpowers/specs/2026-07-28-colour-g7-pass2b-placement-migration-design.md`.
  >
  > ~~**No render-hash gate can observe site 3.** Across all 2999 corpus files there are 17 NChannel
  > spaces, exactly 2 where name-split and placement disagree — both the same `6-2-4-4-t02-pass-a`
  > `/CS0`, and both a **fill** space, not a shading. Zero corpus NChannel shadings differ, and there is
  > no NChannel mesh anywhere in the corpus, so evidence for site 3 will have to be synthetic.~~ The
  > prediction held: sites 3 and 4 closed 2026-07-28, and the corpus evidence is indeed synthetic — see
  > the "Closed 2026-07-28 — sites 3 and 4" note above for the closure-time census (3 005 files, a wider
  > pass than the 2999 counted here), the mixed-case ink-loss measurement, and the gate results.
- **G-8 — `/None` shading *patterns*.** The `sh` operator is covered; a shading used as a *pattern*
  (via `scn` on a Pattern colour space) paints through the pattern machinery, which does not consult
  `PaintsNothing`. Narrower than G-7 and likely a few lines, but untested and so unclaimed.
  **Pinned 2026-07-29:** `NoneShadingPattern_paints_G8Baseline` (`ColourGapBaselineTests`) asserts the
  measured paint; the fix flips it red. **Measurement note:** the pin was drafted predicting the
  fixture's constant-black tint transform would show through, but the measured centre pixel is
  **white RGB(255,255,255)**, so the pin asserts white. White is still a *paints* outcome here, not a
  disguised pass: the fixture lays a red backdrop first, so "paints nothing" would render RED — and
  when G-8 is fixed the surviving red fails the pin's `Green > 235` clause exactly as intended.
  **Why it renders white, not the constant-black tint transform (answered 2026-07-29):** the tint
  transform (fixture object 8) is never evaluated. `ShadingBuilder.BuildColorMapper` calls
  `ColorSpaceResolver.BuildTintToRgb`, which declines the `/None` colourant at
  `ColorSpaceResolver.cs:414` and returns null before `PdfFunction.Create` ever touches the
  Separation's tint transform. `BuildColorMapper` then falls through to the `ToArgbByCount`
  fallback, which reads the shading `/Function`'s single 1.0 tint component as DeviceGray level
  1.0 = white. That second defect is now recorded as its own row — **see G-15**, which enumerates
  all five `BuildTintToRgb` null sites and explains why closing G-8 does not close it. **Do not
  retire this pin without reading G-15 first:** this pin is currently the only thing that
  incidentally exercises the conflation, and retiring it on G-8's fix removes that coverage.
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
  **Pinned 2026-07-29:** `All_image_gets_no_spot_ink_G9Baseline` + `All_stencil_fill_gets_no_ink_G9Baseline`
  (`PdfImageToCmykTests`) pin both decline sites.
- **G-10 — `/None` glyph suppression drops the mode-4 clip along with the fill.**
  `PdfGraphicsState.TextPaintsNothing` masks `RenderingMode` with `& 3`, so mode 4 (fill + add to clip)
  is treated identically to mode 0 (fill only): both map to `FillPaintsNothing`. `PdfRenderer.cs:949` and
  `:1307` skip the entire `_coreText.Render` call when `TextPaintsNothing` is true, which is correct for
  the fill half — a `/None` fill really does have no effect — but for mode 4 it also discards the "add to
  the clipping path" half. A later painting operator that relies on that clip then paints somewhere it
  should have been clipped out of, which IS a visible effect on the current page, contradicting the very
  clause row 4-8 cites. Pre-existing slice-1 debt, not introduced by this branch; out of scope to fix here
  — flagged so it is not mistaken for coverage row 4-8 already claims.
  **Pinned 2026-07-29:** `Mode4NoneText_establishes_no_clip_G10Baseline` (`ColourGapBaselineTests`)
  asserts the trailing fill lands unclipped across the whole rect. Both line references above were
  stale and are corrected: `:947`→`:949` and `:1266`→`:1307` (verified by grep for
  `TextPaintsNothing`; the plan predicted `:1305`, which was right before this pass's G-12 counter
  added two lines near the top of `PdfRenderer.cs`). The other two `TextPaintsNothing` sites, `:817`
  and `:1163`, gate the Type 3 routes and are not part of this gap's claim.
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
  **Pinned 2026-07-29:** `Pattern_without_scn_carries_over_previous_colour_G11Baseline`
  (`InitialColorValueTests`) asserts the carried-over red.
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
  **Hooked 2026-07-29:** `Cs_then_sc_resolves_four_times_G12Baseline` (`ColorSpaceResolveCountTests`)
  pins one `cs`+`sc` at **4** `ResolveColorSpace` passes (the predicted count, measured exactly) via the
  new `ColorSpaceResolver.ResolveCallCount` counter, surfaced as `PdfRenderer.ColorSpaceResolveCount`.
  **Counter semantics, so a future change does not silently redefine
  the metric:** the increment is the *first* statement of `ResolveColorSpace`, so it counts method
  **entries**, not resolutions performed — the early `string.IsNullOrEmpty` return and the
  device-colour-space skip both count. The pinned 4 depends on that: the fixture uses `/DeviceRGB`,
  whose passes all take the device-skip return. **This pin guards the fill/stroke-split fix shape
  only** (4 → 2 if `cs`/`sc` resolve only the side they set). Caching a parsed tint transform per
  colour-space resource — the option the G-12 entry emphasises, since the complaint is redundant
  re-parsing through the uncached `PdfFunction.Create` — leaves this method-entry count at 4 and
  does NOT flip this pin; that shape remains unhooked. Guarding it needs a second counter
  incremented at the tint-transform parse site, exercised by a `/Separation` fixture with a real
  type-2 tint transform. Anyone lowering this number must keep the counting semantics identical or
  retire the pin explicitly rather than re-baselining it.
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
  Recorded per this matrix's own discipline (row 4-4) rather than left implicit. **Note added 2026-07-29
  (G-14 close):** `StencilInkFromFill`'s "spot-ink, overprint-preserving CMYK path" characterisation above
  is no longer the whole story — G-14 added a **process-only** variant (empty `Names`, reserved-colourant
  ink) reached through the same relaxed `CmykPageRenderer` empty-`Names` gate, so a stencil inheriting a
  reserved-name Separation's initial colour after a bare `cs` can now take either arm depending on
  registration; this row's untested combination is unaffected either way.
  **Observed 2026-07-29:** `Stencil_after_bare_cs_takes_the_initial_tint_G13` (`ColourGapBaselineTests`)
  — the initial tint 1.0 renders black through the stencil, so "reasoned about, only" no longer
  applies and the row 4-4 note's untested-combination caveat is closed. Note the fixture uses a
  `/Spot`-named Separation with a `/DeviceRGB` alternate, so it observes the **initial-colour
  inheritance** this gap was about; it does not by itself exercise the reserved-name process-only arm
  described in the G-14 note above.

- **G-14 — Unregistered reserved-name Separations flatten through their alternate instead of
  applying the process colourant directly.** Found 2026-07-28 by the test-debt trio's Task 0 probe,
  measured on the real render path: `Separation /Cyan` at tint 0.7 with a deliberately
  magenta-ramping alternate paints **M=0.7, C=0**. Production-shaped and traced:
  `ColorSpaceResolver.ResolveSeparation` special-cases only `/All` and `/None`, so a reserved name
  resolves through element 2; with nothing registered the compositor's routed arm never fires
  (`AnyRegistered` needs a spot plane) and the flatten arm paints the resolved colour. This is row
  4-11's documented availability-equals-registry policy behaving as recorded — and the **user
  ruling of 2026-07-28 supersedes that policy's sufficiency: the bar is "Adobe or better"**, and
  Adobe applies a reserved-name separation directly on a CMYK device (C=0.7, alternate ignored;
  §8.6.6.4 row 4-11's own first clause).

  **CLOSED 2026-07-29.** The availability rule is now: available = registered in
  `SpotColorantRegistry` **OR** the name is a reserved process colourant. Fix sites, both repos:

  - Pellucid `InkDecider` — reserved-direct arm plus `AllReservedProcessOrNone` (`083be5e`).
  - Engine `ColorSpaceResolver` — `AllReservedProcessOrNone`/`ReservedChannelOf`/`ColorantNamesOf`,
    and `ShadingBuilder.PackByReservedName` — the shading bypass that packs a reserved name's plate
    directly instead of running the tint transform (`ea3edbe`).
  - Engine `PdfImageToCmyk.TryToCmyk` — reserved-name image route (`1459d73`).
  - Engine `StencilInkFromFill` — process-only ink for the empty-`Names` (reserved) case
    (`f13bd52`).
  - Pellucid `CmykPageRenderer` — empty-`Names` gate relaxed so the process-only ink above actually
    composites, plus image/stencil render pins (`180aeab`).
  - Engine — IVT grant plus the ENGINE-LEVEL shading pin
    `G14_ReservedSeparation_BuildPacksTheLastStopDirectly` (`768e6d4`).

  **Retired pins, both recorded:**
  1. `ReservedSeparation_Unregistered_FlattensThroughItsAlternate_G14Baseline` → replaced by
     `ReservedSeparation_Unregistered_AppliesTheProcessColourantDirectly` (the baseline pin flipped
     red on the fix, as designed, and was retired rather than inverted in place).
  2. `Separation_with_a_CalGray_alternate_still_reverts` → replaced by
     `Separation_Black_CalGrayAlternate_RoutesDirectly_G14` (positive: a reserved name with a CalGray
     alternate now routes directly) plus a new negative control
     `Separation_SpotName_CalGrayAlternate_StillReverts` (a non-reserved spot name with the same
     CalGray alternate still reverts) — the original pin's scope is half preserved, half superseded,
     not silently dropped.

  **Residuals, recorded rather than closed:**
  - **(a)** An **Indexed image over an all-reserved base still flattens** — out of scope this pass
    (Task 4 scope note); the reserved-direct image route covers a directly-named reserved Separation
    driving image ink, not an Indexed palette resolving to one.
    **Pinned 2026-07-29 (fixture corrected 2026-07-29):** `Indexed_over_reserved_base_still_declines_G14ResidualBaseline`
    (`PdfImageToCmykTests`) uses a real `/Lookup` string so the route actually reaches the base
    colour space instead of bailing on a malformed placeholder. It asserts both CMYK routes
    decline for the right reason: `TryToCmyk` reaches `BuildIndexedEntryToCmyk`'s Separation arm
    and dies in the uncached tint transform (`BuildTintToCmyk`/`PdfFunction.Create`), because the
    Indexed route has no reserved-direct arm mirroring `ShadingBuilder.BuildCmykMapper`.
    `TryToSpotInk` declines separately and permanently (`Classify("Cyan") != Spot`), which is
    decoration, not part of the hook. The reserved-direct fix flips `TryToCmyk`'s assertion red.
  - **(b)** The stencil fix requires the **spot-plane-buffer configuration** — `spots`/`registry`
    passed through, the standard soft-proof path. A stencil rendered with no spot-plane configuration
    at all is not exercised by this pass's fixtures.
    **Hook status 2026-07-29: deliberately unpinned.** The observable needs a render harness driven
    with no spot buffers at all, which lives on the Pellucid side (`CmykPageRenderer`'s caller), not in
    this engine-only pass. Nearest existing coverage is the G-14 stencil pin `f13bd52` plus Pellucid's
    `180aeab` render pins, both of which run *with* the spot configuration. Pinning this needs a
    Pellucid task, not an engine one.
  - **(c)** The shading pin is **ENGINE-level** (`ShadingBuilder.Build`), not render-level — measurement
    (Task 3/7) found the mapper site small enough that the render-level pin's bounded fallback to an
    engine-level pin, reserved by the design's escape hatch, was what actually landed; recorded here
    rather than claimed as render-level coverage it isn't.
    **Hook status 2026-07-29: pinned, at engine level only — by design, not by omission.** The pin is
    `G14_ReservedSeparation_BuildPacksTheLastStopDirectly` (`768e6d4`). Promoting it to render level is
    the open item, not adding a pin.

  **Gate outcome:** GWG 51/51, NChannel 3/3. Exactly two digests re-pinned
  (`GWG030_Gray_K_black_OP_X1`, `GWG230_Four_different Grays_x1a`), both value-only sub-perceptual
  quantisation deltas, each verified against its own fixture's `_ReadMe` criterion — GWG230 now
  matches its DeviceCMYK reference exactly, which is strictly better than before. **Census lesson
  worth keeping:** a correctness tolerance (0.004) cannot predict digest-identity flips; only
  quantisation-only deltas move a SHA gate, and the two that moved here were exactly that kind.

  **Suites at close:** engine 2694/2694 (net8/9/10, 0 warnings); Pellucid
  `Pellucid.Rendering.Avalonia.Tests` 547/547.

  Reachability: invisible in any well-formed file (a real reserved-name alternate is a matching ramp
  — the two answers coincide); visible under a lying alternate, which prepress files do contain as
  pranks and errors. The RGB path is NOT in scope: row 4-12 requires reversion there, and reversion
  is what it does. Design: `Docs/superpowers/specs/2026-07-29-g14-reserved-separation-direct-design.md`;
  plan: `Docs/superpowers/plans/2026-07-29-colour-g14-reserved-separation-direct.md`.

- **G-15 — `BuildColorMapper` cannot tell "no mapper possible" from "must paint nothing", and
  invents a colour for both.** Found 2026-07-29 while the release-hooks whole-branch review was
  tracing *why* the G-8 pin measured white instead of the predicted black. It is the mechanism
  underneath G-8 rather than a restatement of it: G-8 is "the pattern route doesn't consult
  `PaintsNothing`", this is "even the route that *did* consult it cannot say so to its caller".

  `ColorSpaceResolver.BuildTintToRgb` (`ColorSpaceResolver.cs:400`) returns `null` at **five**
  distinct sites, and only one of them means "this space must not paint":
  - `:409` — `SpotColorSpace.TryParse` fails the `minimumElements: 4` arity gate (malformed).
  - `:414` — **`PaintsNothing(baseArray, document)` — the `/None` case. Semantic: paint nothing.**
  - `:429` — `inputComponents < 1` (malformed).
  - `:432` — empty alternate space (malformed).
  - `:436` — the tint transform could not be built (malformed / unsupported function type).

  `ShadingBuilder.BuildColorMapper`'s `case "Separation" or "DeviceN"` arm
  (`ShadingBuilder.cs:342-346`) does `if (tint is not null) return …; break;` — so all five
  collapse to the same `break`, falling through to the `ToArgbByCount` fallback
  (`:351`, `:367-388`). That fallback **fabricates a colour from the component count alone**:
  one component is read as DeviceGray, three as RGB, four as CMYK. For the G-8 fixture the
  shading `/Function` emits a single constant 1.0, so `/None` — a space whose entire contract is
  to mark nothing — renders as **DeviceGray 1.0 = opaque white**, which is not "nothing"; it is
  an opaque paint that obliterates whatever was behind it. On a white page that is invisible, and
  that invisibility is why it survived this long.

  **Why this is its own row and not a G-8 sub-clause:** fixing G-8 (adding the `PaintsNothing`
  gate to `FillWithShadingPattern`, mirroring `OnPaintShading:725`) stops the *pattern* route
  reaching this code, and the G-8 pin flips. It does **not** fix the conflation. Any other caller
  of `BuildColorMapper` — present or future — still gets a fabricated colour where the honest
  answers are "suppress this paint" (the `/None` case) and "decline, I cannot map this"
  (the four malformed cases), which are different instructions that a `null` cannot distinguish.
  A malformed Separation currently renders as a plausible-looking grey/RGB/CMYK value rather than
  failing visibly, which is the same "correct in value, wrong in cost" substitution species this
  matrix has caught repeatedly.

  **Ruled goal:** `BuildTintToRgb` (or a sibling) must distinguish *paints-nothing* from
  *cannot-map*, and `BuildColorMapper` must honour the first by suppressing rather than
  substituting. The `ToArgbByCount` fallback should remain reachable only for genuinely
  unrecognised spaces.

  **Hook status: UNPINNED.** No test asserts this today, and the G-8 pin does not cover it — that
  pin asserts the *paint*, and after G-8 is fixed it will be retired, taking the only incidental
  coverage with it. Pinning it needs a fixture that reaches `BuildColorMapper` by a route other
  than the fill-pattern one (e.g. `sh` with a malformed Separation, where `OnPaintShading`'s
  `PaintsNothing` gate does not apply because the space is malformed rather than `/None`).
  Recorded unfixed and unpinned rather than folded into G-8, so that closing G-8 cannot be
  mistaken for closing this.

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

---

# B-2 slice: OutputIntent-destination ICC source conversion

> Added 2026-07-31, **ahead of the code** — this project's rule is that the gap table leads the
> implementation, not the other way round. The two sections below cover the B-2 phase: converting
> device-independent source colour (ICCBased, Lab) through the PDF/X `DestOutputProfile` proof
> destination instead of (or in addition to) the provider's bundled default CMYK profile. Design:
> Pellucid repo `docs/superpowers/specs/2026-07-31-b2-icc-cms-design.md`; plan: Pellucid repo
> `docs/superpowers/plans/2026-07-31-b2-icc-source-conversion.md` (cross-repo — this engine repo has
> no local copy of either). Clause text below was pulled from the indexed ISO 32000-2:2020 EC2 PDF via
> the pdf-rag index, cross-checked against PDF 32000-1:2008 where the two editions' wording differs.
>
> **veraPDF-validation-profiles note:** the task brief pointed at
> `C:\Users\jorda\RiderProjects\veraPDF-validation-profiles\PDF_X\` for ISO 15930-7 (PDF/X-4) rule XML.
> That path does not exist on this machine — only `veraPDF-corpus` (test fixtures, no profile XML) is
> present, confirmed by a repo-wide glob under `C:\Users\jorda\RiderProjects` before writing this
> section. PDF/X-4 semantics below (`GTS_PDFX` subtype, required `DestOutputProfile` for a non-standard
> production condition) are therefore cited from ISO 32000-2 §14.11.5 Table 401/402 directly, which
> PDF/X-4 (ISO 15930-7) normatively incorporates by reference rather than restating — not from a
> veraPDF rule ID, since none could be read. If the profiles repo is cloned later, this note is the
> marker to go back and add rule-ID citations alongside the clause numbers.
>
> **Status column legend for this slice only:** the code these rows describe has not landed yet (this
> is Task 1 of a multi-task plan). `🔧 PLANNED` marks a row whose behaviour is designed and cited but
> unverified by any test — deliberately not `✅`, per this document's own rule that a ✅ row needs a
> test seen to fail. `✅`/`⚠️`/`␀` keep their normal meanings for rows describing already-existing
> behaviour.

## §14.11.5 — Output intents

| # | Normative statement | Class | Status | Implementation / note |
|---|---|---|---|---|
| 6-1 | Table 401/402 `DestOutputProfile`: "the 'to CIE' (AToB) information may optionally be used to remap source colour values to some other destination colour space, such as **for screen preview or hardcopy proofing**" (ISO 32000-2:2020 EC2 §14.11.5, p.822/839; PDF 32000-1:2008 §14.11.5 Table 365, p.641, identical wording) | L | 🔧 PLANNED | **Proof destination = first `GTS_PDFX` CMYK `DestOutputProfile`.** The clause is permissive (`may`), so choosing to exercise it for proofing is a design decision under this document's L class, the same way row 5-14 records the spot-blend latitude as a decision rather than a compliance question. `ProofCmykResolver` (`PdfLibrary.Rendering.Icc`) will pick the destination: the first output intent whose `/S` is `GTS_PDFX` and whose embedded profile decodes to `OutputIntentColorSpace.Cmyk` (`OutputIntentDescriptor.ColorSpace`/`HasDestProfile`, `PdfLibrary/Document/OutputIntents.cs`), else `CmykProfileProvider.Default`. The transform itself uses the profile's **BToA leg** ("from CIE"), matching the clause's own framing of `DestOutputProfile` as "the transformation from the PDF document's source colours to output device colourants" — the forward (AToB) direction is reserved for reading an ICCBased *source* space (row 7-1 below), not for the proof destination. |
| 6-2 | Table 401 `DestOutputProfile`: "**Required if `OutputConditionIdentifier` does not specify a standard production condition; optional otherwise.**" No PDF-level fallback profile is specified for the optional case — the standard is silent on renderer behaviour when the field is legitimately absent. | D | ✅ (existing) | **`OutputConditionIdentifier`-only intents (no embedded profile) fall back to the active provider profile.** This is the existing cascade, not new code: `OutputIntentDescriptor.HasDestProfile` is already `false` when a `GTS_PDFX` intent names a registry-standard condition (e.g. `CGATS TR 001`) without embedding `DestOutputProfile`, and `ProofCmykResolver` (task 3+) falls through to `CmykProfileProvider.Default` on exactly that condition — the same default every bare `DeviceCMYK`/resolved-Separation conversion already uses (`DeviceCmykConverter.cs:91`). Device-dependent per this document's class D: the answer is "whatever the active provider profile is," which is a Pellucid/host configuration, not a value the clause supplies. |
| 6-3 | ISO 32000-2:2020 EC2 §14.11.5 (page-level output intents, PDF 2.0): "**If a PDF processor chooses to respect output intents, then when processing a page that has an associated (page-level) output intent, that page-level output intent shall be used.**" Distinct from NOTE 1's "[the choice of output intent] is outside the scope of ISO 32000-2" — that NOTE covers the *document*-level array with no PDF-mandated selector; the page-level `shall` is a separate, later PDF 2.0 addition that narrows the choice once a page-level intent exists. | N | GAP | **Multiple output intents beyond the first `GTS_PDFX` CMYK one.** `ProofCmykResolver` takes the *first* qualifying document-level intent (row 6-1) and does not special-case a page-level `/OutputIntents` entry (7.7.3.3 Page objects) taking precedence over it, nor does it choose among several document-level `GTS_PDFX` intents by any rule beyond first-match. Because we *do* choose to respect output intents (that is row 6-1's whole premise), the page-level `shall` above binds us and is currently unimplemented — a genuine N-class gap, not a latitude question. Scope for a later phase; not part of this plan's task list. |

## §8.6.5 / §11.4.5 — Device-independent source conversion

> §11.4.5 in the task brief's title refers to the transparency-group/rendering-intent machinery
> (§11.7.5.3 in the EC2 renumbering — "Rendering intent, black point compensation and colour
> conversions" — §11.4.5 is its PDF 1.7/32000-1 section number before the EC2 restructure); rows 7-10
> and 7-11 below are that half. §8.6.5 rows are the per-object source colour space conversion.

| # | Normative statement | Class | Status | Implementation / note |
|---|---|---|---|---|
| 7-1 | §8.6.5.5 ICCBased colour spaces: "the colour space is being used as a source colour space, only the 'to CIE' profile information (**AToB** in ICC terminology) shall be used; the 'from CIE' (BToA) information shall be ignored when present" (ISO 32000-2 §8.6.5.5, p.193/206; identical in PDF 32000-1:2008 §8.6.5.5, p.151) | N | 🔧 PLANNED | **ICCBased fills/strokes, N=3 (RGB) and N=4 (CMYK).** `IccPcsLabTransform` (ICCSharp) reads the source ICCBased profile's AToB leg into Lab-as-PCS, then the destination profile's BToA leg (row 6-1's `ProofCmykResolver` destination) back out to CMYK — Lab is the PCS carrier between the two legs, not a PDF colour space substitution. `PdfGraphicsState.ResolvedFillProofCmyk`/`ResolvedStrokeProofCmyk` hold the resolved value. Rendering intent: RelativeColorimetric, no black-point compensation, for the whole phase (row 7-10). |
| 7-2 | §8.6.5.4 Lab colour spaces: L* range is "0 to 100" (fixed by the space, not by `/Range`); a*/b* range is the dictionary's `/Range` entry, "[Component] values falling outside the specified range shall be adjusted to the nearest valid value without error indication" (ISO 32000-2 §8.6.5.4, p.190/204; PDF 32000-1:2008 §8.6.5.4, p.148) | N | 🔧 PLANNED | **Lab fills.** Same `IccPcsLabTransform` destination leg as row 7-1, since Lab is already the PCS the transform expects — no AToB source leg is needed for a Lab source space, only the destination profile's BToA leg and the L*/a*/b* clamp above (existing `/Range` clamping logic, unaffected by this phase). |
| 7-3 | Same clause as 7-1, applied to image sample data rather than `scn`/`SCN` operands — §8.9.5.2's "Colour space" entry on an image XObject reuses §8.6.5.5 unchanged; no separate normative text for images. | N | 🔧 PLANNED | **ICCBased images, 8-bit, N=3/N=4.** `ImageCommand.ProofCmyk` carries the resolved per-image proof value alongside the existing device conversion, following the same AToB(source) → Lab-as-PCS → BToA(destination) path as row 7-1, at image-sample granularity rather than per-operand. |
| 7-4 | Same clause as 7-1, applied to JPEG 2000 image data whose PDF-level `/ColorSpace` is a 3-channel ICCBased entry overriding the codestream's own colour space (§7.4.9's `/JPXDecode` filter defers colour-space authority to the PDF-level entry when present) | N | 🔧 PLANNED | **JPX with a 3-channel PDF-level ICC override.** Same conversion as row 7-3; the JPX-specific part is only which colour space wins (PDF-level, per §7.4.9), not the conversion itself. |
| 7-5 | §8.6.5.5 Table 65/68 N=1 (Gray): "Valid values for N: 1, 3, or 4" — N=1 is normatively as valid a source space as N=3/N=4; nothing in the clause singles it out. | N | GAP | **ICCBased gray, N=1 — deliberately excluded this phase**, for stability reasons named in the plan, not a spec reading: the 18.x rendering-stability work this phase must not disturb touches the gray conversion path more than the RGB/CMYK ones. Recorded as a real gap against the clause, not folded into an L/D reclassification — the clause does not grant latitude here, we are declining it on purpose. |
| 7-6 | §8.6.6.3 Indexed colour spaces: "base parameter … shall be any device or CIE-based colour space … If the base colour space is a CIE-based ABC space such as a CalRGB or Lab space, the values shall be interpreted as A, B, and C components" — an ICCBased base is likewise CIE-based and in scope (ISO 32000-2 §8.6.6.3, p.199/214) | N | GAP | **Non-JPX Indexed-over-ICCBased.** An `/Indexed` colour space whose `base` is `[/ICCBased …]` needs the looked-up base-space triple converted through row 7-1's transform before painting; this phase's `ProofCmyk` plumbing reaches direct ICCBased operands and image samples but not an Indexed lookup's resolved base colour. JPX is excluded from this gap's scope because JPX images carry their own indexed/palette handling upstream of this path (see row 7-4's PDF-level override framing). |
| 7-7 | Same clause as 7-2, applied to image sample data (paired with row 7-3's ICCBased-image pairing: §8.9.5.2 reuses §8.6.5.4 unchanged for image colour spaces) | N | GAP | **Lab images.** Row 7-2 covers Lab operands (`scn`); Lab image XObjects are not converted through the destination profile this phase — `ImageCommand.ProofCmyk` is populated for ICCBased image sources (row 7-3) but not for a `/Lab` image `/ColorSpace`. |
| 7-8 | Same clause as 7-1/7-2, applied to a shading or mesh's embedded ICCBased/Lab colour space (§8.7.4.5.x shading dictionaries reuse §8.6.5 colour space definitions for their `/ColorSpace` entry, same as every other colour-space-bearing dictionary) | N | GAP | **Shadings/meshes through embedded profiles.** `ShadingBuilder.BuildColorMapper` flattens a shading's colour space by component count (`/N`) only — the same fabricate-by-count fallback documented in gap G-15 above for the Separation/DeviceN slice, and structurally the same gap here: an ICCBased or Lab shading colour space is read for its channel count, not converted through this phase's transform. Not scoped to this phase's tasks; recorded so a future ICC-aware shading pass has a named starting point. |
| 7-9 | §8.6.5.3 CalRGB / (CalGray, by the parallel clause immediately preceding it in both editions) colour spaces: WhitePoint/Gamma/Matrix (CalRGB) or WhitePoint/Gamma (CalGray) define a **matrix/TRC-style transform to CIE XYZ** — not an ICC profile, so it has no AToB/BToA legs of its own (ISO 32000-2 §8.6.5.3, p.187/202; PDF 32000-1:2008 §8.6.5.3, p.146) | N | GAP | **CalRGB/CalGray CMM legs.** This phase's `IccPcsLabTransform` is built for ICC profile AToB/BToA data; CalRGB/CalGray have no ICC profile to read; They need their own matrix/TRC-to-XYZ evaluation followed by XYZ→destination-profile-BToA, which is out of scope this phase. Existing CalRGB/CalGray handling (device-space approximation) is unchanged and untouched by the B-2 work. |
| 7-10 | §11.7.5.3 (EC2; §11.4.5 in pre-EC2/32000-1 numbering) Rendering intent and colour conversions: "the rendering intent used shall be the current rendering intent in effect in the graphics state at the time of the painting operation" — a per-object parameter, set by the `ri` operator (§8.4.5 Table 57) | N | GAP | **Per-object rendering intents — Phase C, per this plan's rendering-intent decision.** The whole B-2 phase fixes the rendering intent at **RelativeColorimetric, no black-point compensation** for every conversion (rows 7-1 through 7-4), rather than reading the graphics state's current `ri` value per object as this clause requires. Documented here as a deliberate, scoped-out gap rather than an oversight — Phase C's job, not this plan's. |
| 7-11 | §8.6.5.4 Lab colour spaces: `/WhitePoint` is a **required, per-space** array — "[XW YW ZW] that shall specify the tristimulus value … of the diffuse white point" — with no PDF-mandated default to D50 or any other illuminant (ISO 32000-2 §8.6.5.4 Table 64, p.190/204) | N | GAP | **Non-D50 Lab `/WhitePoint` — chromatic adaptation not performed.** This phase's Lab path (row 7-2) assumes the source white point is already D50 (the ICC PCS illuminant `IccPcsLabTransform` targets) and does not chromatically adapt a Lab space whose `/WhitePoint` differs (e.g. the D65 `[0.9505 1.0000 1.0890]` shown in both editions' own §8.6.5.4 EXAMPLE). A `/WhitePoint` that is not approximately D50 will convert with a colour cast this phase does not correct. |

**Score note:** these two sections are **not** folded into the "Score — slice 1" table above — that
table's Normative/Latitude/Device denominators are scoped to §8.6.6.4/§8.6.6.5 (Separation/DeviceN) by
its own header. A B-2-scoped score, covering only rows 6-1 through 7-11, becomes meaningful once the
later tasks in this plan land code and tests against the 🔧 PLANNED rows above; adding it before then
would count unverified rows the same way this document's own rules forbid for ✅.
