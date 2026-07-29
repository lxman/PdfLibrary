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

## 7. Verification frame

- Suites: Pellucid 1315 + new (expected +5 or +6 per §5) / 0; engine unchanged (2685/0) unless §5's
  fallback adds one (+1).
- Gates: GWG 51/51/0 and NChannel 3/3/0 — pure guard here (test-only change cannot move a digest;
  a moved digest means the pass violated its own scope — stop).
- No pack, no repin: nothing engine-side changes unless §5's fallback fires, and that adds a test
  only — still no pack needed (engine tests run in the engine repo).
