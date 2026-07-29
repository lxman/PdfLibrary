# Colour test-debt trio: render-path pins for rows 4-5, 5-6, 5-7

**Date:** 2026-07-28
**Status:** approved in session.
**Scope:** test-only. No production code changes in either repo. The deliverable is three
conformance-matrix rows moving ⚠️ → ✅ on the strength of soft-proof-path tests that have been seen
to fail, plus the matrix update itself.
**Matrix:** `Docs/colour/rendering-conformance.md` rows 4-5, 5-6, 5-7 and the score block.

---

## 1. Goal and non-goals

Rows 4-5, 5-6 and 5-7 are believed-correct behaviour with no render-path test — the last cheap
⚠️ → ✅ conversions in the N class. Closing them takes the N-class score to 18/20 ✅ with 0
violations. Each row gets pins at the level its cell demands (the CMYK soft-proof render path), with
oracles that fail positionally under the named mutation — this matrix's standing "test that has been
seen to fail" bar.

**Non-goals:** rows 5-3/5-10's residuals (image reversion, unregistered-spot shading reversion),
G-8/G-9 (those are `/None` shading *patterns* and `/All` images — different rows, real gaps, own
passes), any harmonization, any production change. **If a pin comes back red against today's
production code, that is a discovered violation, not a test bug to massage:** stop, record the row
back to ❌ with the evidence, and report — the fix is its own pass with its own review. Do not
adjust production code inside this test-only pass.

## 2. Where the tests live

`Pellucid.Rendering.Avalonia.Tests\Cmyk\` — one new file, `ReservedAndNoneRenderTests.cs`, reusing
`NChannelPerComponentRenderTests`' harness shape verbatim: `Render(state, registry)` →
`(float[] Plates, float[] Planes, byte[] Bgra)` via `CmykPageRenderer.RenderToBuffer` +
`SpotDisplayCombiner`, with `PlateAt(plates, x, y)` and `PlaneAt(planes, x, y, plane, planeCount)`
positional readers. Every ink assertion is positional per-plate/per-plane; sums, multisets and
`Contains` are decorative here for the same reason as everywhere else in this programme.

One engine-side unit fixture is permitted as a fallback for the image context only (§5, decision
rule stated there).

## 3. Row 4-5 — reserved names take their canonical plates, end-to-end

**The claim:** Cyan/Magenta/Yellow/Black are reserved process-colourant names; on the CMYK path a
space naming them paints the named plate directly — not a spot plane, not the alternate.

**Fixture 4-5a (Separation):** `Separation /Cyan` fill at tint 0.7, alternate deliberately ramping
to MAGENTA (the row 4-10 trick — reversion or plane-routing is then positionally visible):
- `plate[0] == 0.7`, plates 1–3 == 0 (a magenta-ramp reversion would put 0.7 on plate 1)
- every spot plane == 0 (plane-routing would mark plane 0)

**Fixture 4-5b (plain DeviceN, mixed):** DeviceN `[Magenta, Spot1]`, Spot1 registered, tints
`[0.4, 0.6]`:
- `plate[1] == 0.4`, plates 0/2/3 == 0
- `plane[Spot1] == 0.6`, no other plane marked
- Pins that reserved-name CLASSIFICATION, not registration, routes the process half: Magenta is not
  in the registry and still takes its plate.

**Prescribed mutation:** in `PageColorant.Classify` (or the routed arm's name switch), treat
"Magenta" as Spot → 4-5b's `plate[1]` assertion fails (0.4 vanishes to an unregistered plane / the
flatten arm). Named assertion: 4-5b `Assert.Equal(0.4f, m, 3)`.

## 4. Rows 5-6 / 5-7 — `/None` in DeviceN: never painted; discarded when painting directly

The two rows are one physical rule observed from two arms, and are pinned separately because the
arms are different code:

**Fixture 5-7a (direct painting, fill):** plain DeviceN `[Magenta, None]`, tints `[0.4, 0.9]`.
0.9 is the value that must appear NOWHERE:
- `plate[1] == 0.4`; plates 0/2/3 == 0; every plane == 0
- The 0.9 chosen large and distinct from every other value in the fixture so any mis-route is
  identifiable by value, not just by nonzero-ness.

**Fixture 5-7b (direct painting, overprint mask):** same space, fill with `overprint: true` over a
pre-painted backdrop on plates 0/2/3:
- plate 1 carries 0.4; plates 0/2/3 retain the backdrop (the None component set no mask bit)
- This is the discard rule's observable with teeth: a None component that "paints" marks a plate
  and knocks the backdrop out.

**Fixture 5-6a (shading):** axial shading in DeviceN `[Magenta, None]` where the None channel ramps
0→1 and Magenta ramps 0→0.6. At a mid-shading pixel:
- plate 1 carries the interpolated magenta value (assert the exact stop value at a pixel chosen on
  a stop, per the shading tests' existing practice)
- plates 0/2/3 == 0 and every plane == 0 — the None ramp appears nowhere
  (`ShadingSpotSplit.Split`'s `ColorantKind.None` arm, `ShadingSpotSplit.cs:43`).

**Fixture 5-6b (image):** a small DeviceN `[Magenta, None]` image through the spot-image path
(`PdfImageToCmyk.TryToSpotInk`), same nowhere-oracle at a named pixel. See §5 for the
level-of-assertion decision rule.

**Prescribed mutations (each names its assertion):**
1. `ShadingSpotSplit.Split` `:43`: route `ColorantKind.None` to the spot arm → 5-6a's plane
   assertion fails (the None ramp lands on a plane).
2. `InkDecider` routed arm / `TryPerComponent`'s `Nothing`-continue: consult the component instead
   of skipping (simulating the "malformed file defines /Colorants for None" trap) → 5-7a's
   all-planes-zero or 5-7b's backdrop assertion fails.
3. `PdfImageToCmyk`'s None arm: give None a plane → 5-6b's plane assertion fails.

Mutations are observed red BY ASSERTION and reverted, per the standing rule. Where a listed
mutation cannot make its named assertion fail, that mismatch is itself a finding to report — do not
substitute a weaker assertion to make the mutation "work".

## 5. The image fixture's level — decision rule

Preference: 5-6b at render level (an `ImageCommand` through `CmykPageRenderer`), same as the other
fixtures. Fallback: if driving a DeviceN image through the render harness requires more than a
small fixture (new decode plumbing, >~40 lines of setup), pin `TryToSpotInk` directly in the engine
(`PdfLibrary.Tests`) with the same nowhere-oracle on the returned `SpotImageInk`, and say so in the
row cell ("image context pinned at the split, not the composite"). The row can still flip ✅ on
that evidence — the split IS the discard site — but the cell must not claim render-level coverage
it doesn't have.

## 6. Matrix close-out

In `Docs/colour/rendering-conformance.md`, same commit as the tests land (engine-repo docs; the
tests are Pellucid-side — the doc commit references the Pellucid commit SHA):
- 4-5: ⚠️ → ✅, cell gains the two fixture names and the mutation note.
- 5-6: ⚠️ → ✅, cell gains 5-6a/5-6b and states the image-context level per §5's outcome.
- 5-7: ⚠️ → ✅ (cell is currently empty), written fresh: the discard arm, both fixtures, the
  overprint-mask observable.
- Score block: append a dated delta row — N-class now 18 ✅ / 2 ⚠️ (5-3, 5-10) / 0 ❌ — preserving
  the existing snapshot text per the doc's convention.

## 6a. Corrections (2026-07-28, pre-plan coverage audit — superseding parts of §4/§5)

Grepping the actual suites before planning found the coverage picture better than §4 assumed:

1. **Row 5-6's two named contexts are ALREADY pinned, engine-side, through real calls** —
   `ShadingSpotSplitTests.Split_AllNone_ContributeNothing` (name arm) and
   `SplitByPlacement_NoneContributesNothing_ToAnyPlateOrSpot` (placement arm) for the shading
   split; `PdfImageToCmykTests`' GWG080-shaped fixture (`:356-374`, a real parsed
   `/DeviceN [/Black /PANTONE 265 C /None /None /None]` through `TryToSpotInk`, "/None contributes
   nothing" asserted per-plate) for the image split. **Fixtures 5-6a and 5-6b are therefore not
   written; the row closes by CITING these tests**, and §5's decision rule is moot.
2. **Row 5-7's per-component arm is already pinned at decision level** —
   `InkDeciderTests.NChannel_None_component_is_discarded_not_reverted`, including the poisoned
   own-alternate (`[1,1,1,1]`) malformed-`/Colorants` trap. What remains for 5-7 is the ROUTED
   (named-colorant) arm at render level, plates + overprint mask: fixtures 5-7a/5-7b stand, but
   their space gains a registered spot (`[Magenta, None, Spot1]`, Spot1 registered) because direct
   painting on the routed arm requires a registered name — a plain unregistered DeviceN correctly
   REVERTS whole (row 5-8's rule, where `/None` must flow INTO the transform), and a fixture
   without the spot would be pinning reversion, not discard.
3. **Fixture 4-5a's expectation must be measured before it is asserted.** An unregistered
   `Separation /Cyan` fill reaches the flatten arm (nothing registered ⇒ not routed), where the
   painted value is the resolved fill colour and the reserved name's plate identity arrives via the
   overprint plate-set — an arm interaction this spec's author cannot derive with confidence. The
   plan therefore leads with a Task 0 probe measuring what production paints for every fixture
   shape in this spec; pins assert the measured values ONLY where they match the row's normative
   requirement, and any mismatch is §1's stop rule firing early and cheaply.

Net scope after corrections: four render-level pins in Pellucid (4-5a, 4-5b, 5-7a, 5-7b), zero new
engine tests, and the matrix close-out cites the existing engine pins for 5-6.

## 6b. Correction (2026-07-28, Task 0 measurement + user ruling — superseding §3's fixture 4-5a)

Task 0 measured fixture 4-5a's shape against production: an unregistered `Separation /Cyan` with a
magenta-ramping alternate paints **M=0.7, C=0** — the flatten arm paints the alternate's output,
verified production-shaped (`ResolveSeparation` special-cases only `/All`/`/None`; a reserved name
flattens through element 2). This matches row 4-11's audited D-1 policy (availability = registry),
but the **user ruling of 2026-07-28 sets the bar at "Adobe or better"**, and Adobe applies a
reserved-name separation directly on a CMYK device — C=0.7, alternate ignored. The divergence is
therefore a **discovered gap, G-14**, not a defensible policy: recorded in the matrix with the
measured tuple; the production fix (treat C/M/Y/K as always-available on the CMYK soft-proof path,
i.e. widen row 4-11's availability rule for reserved names) is its own future pass.

In this pass: fixture 4-5a becomes a **G-14 baseline pin** asserting the measured current behaviour
with a comment naming the goal — so the G-14 fix flips it red and must update it deliberately, and
interim drift is caught. Row 4-5 flips ✅ on fixture 4-5b (classification end-to-end) with a cell
pointer to G-14 for the direct-application half; row 4-11 stays ⚠️ and gains the measurement and
the ruling. Visible only under a lying alternate — no well-formed file diverges, hence 51/51/0.

## 7. Verification frame

- Suites: Pellucid 1315 + new (expected +5 or +6 per §5) / 0; engine unchanged (2685/0) unless §5's
  fallback adds one (+1).
- Gates: GWG 51/51/0 and NChannel 3/3/0 — pure guard here (test-only change cannot move a digest;
  a moved digest means the pass violated its own scope — stop).
- No pack, no repin: nothing engine-side changes unless §5's fallback fires, and that adds a test
  only — still no pack needed (engine tests run in the engine repo).
