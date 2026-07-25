# Colour rendering conformance — ISO 32000-2 §8.6.6.4 / §8.6.6.5

> Slice 1 (2026-07-25): **Separation** and **DeviceN** colour spaces. Derived from ISO 32000-2:2020
> (PDF 2.0) including Errata Collection 2, §8.6.6.4 (pp. 201–203) and §8.6.6.5 (pp. 204–210).
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

Status: ✅ conformant with a test · ⚠️ conformant but untested · ❌ violation · ␀ not yet audited.

**Score is over N rows only.** L and D rows are deliberately excluded — counting them would inflate the
denominator with things that cannot be failed.

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

> ⚠️ **Open question (D-1).** It is not yet audited whether the RGB display path always reverts, as the
> additive-device rule requires. If any spot-plane routing is reachable outside soft-proof mode, that is a
> §8.6.6.4 violation. See the audit gap at the end.

---

## §8.6.6.4 — Separation colour spaces

| # | Normative statement | Class | Status | Implementation / note |
|---|---|---|---|---|
| 4-1 | "shall be a four-element array whose first element shall be the colour space family name Separation" | N | ␀ | Structural validation on the render path not audited. |
| 4-2 | Tint is a single component in [0.0, 1.0]; 0.0 = minimum colourant, 1.0 = maximum | N | ␀ | |
| 4-3 | "Tints shall always be treated as subtractive colours, even if the device produces output for the designated component by an additive method" | N | ␀ | 0.0 = lightest, 1.0 = darkest. Not audited. |
| 4-4 | "The initial value for both the stroking and nonstroking colour in the graphics state shall be 1.0" | N | ␀ | No initial-tint handling found in `PdfGraphicsState`; needs a targeted check. |
| 4-5 | Cyan / Magenta / Yellow / Black "are reserved to name the process colourants of a CMYK device" | N | ⚠️ | `PageColorant.Classify` → `ColorantKind.Process`. Tested in `PageColorantClassifyTests`; not tested end-to-end on the render path. |
| 4-6 | **All**: "painting operators shall apply tint values to all available colourants at once" | N | ⚠️ | `ColorSpaceResolver.cs:593` — `case "All": c = m = y = k = true`. Sets all four process plates; does **not** include registered spot planes, which "all available colourants" arguably requires. See gap G-2. |
| 4-7 | **All** on an additive device: "the subtractive tint values […] shall be complemented by subtracting from 1 before applying to all available colourants" | N | ␀ | No complement logic found. See gap G-3. |
| 4-8 | **None**: "shall not produce any visible output […] shall have no effect on the current page" | N | ⚠️ | `ColorSpaceResolver.cs:594` skips it with the clause cited; `ShadingSpotSplit.cs:43` treats All/None as "recognised, not a plate". `PageColorantsTests` asserts None is excluded from the colorant list. No test asserts *nothing is painted*. |
| 4-9 | "A PDF processor shall support Separation colour spaces with the colourant names All and None on all devices" | N | ⚠️ | Both are recognised kinds in `ColorantKind`. |
| 4-10 | For All/None, "PDF processors shall ignore the alternateSpace and tintTransform parameters" | N | ␀ | Not audited — must confirm neither is evaluated for All/None. |
| 4-11 | "the PDF reader shall determine whether the device has an available colourant […] If so […] shall apply the designated colourant directly" | D | ⚠️ | Soft-proof path only. Availability = registered in `SpotColorantRegistry`. |
| 4-12 | Additive device: "never applies a process colourant directly; it always reverts to the alternate colour space" | D | ␀ | **The key open question.** See D-1 above and gap G-1. |
| 4-13 | If unavailable, "shall arrange for subsequent painting operations to be performed in an alternate colour space" | N | ⚠️ | The compositor falls back to the flatten path for an unregistered spot (`PdfImageToCmyk.TryToSpotInk` comment). |
| 4-14 | alternateSpace "may not be another special colour space (Pattern, Indexed, Separation, or DeviceN)" | N | ␀ | Malformed-input rejection not audited. |
| 4-15 | tintTransform "shall be called with the tint value and shall return the corresponding colour component values" | N | ⚠️ | `ColorSpaceResolver.BuildTintToRgb` / `BuildTintToCmyk`. |
| 4-16 | NOTE 7 — alternate space "does not necessarily reflect the interactions […] when overprinting is enabled"; separation simulation "can be used as an alternative method" | L | — | The spec concedes the approximation and names §10.8.3 as the better path. Our spot planes **are** that path. No compliance debt. |

## §8.6.6.5 — DeviceN colour spaces

| # | Normative statement | Class | Status | Implementation / note |
|---|---|---|---|---|
| 5-1 | alternateSpace "shall not be another special colour space (Pattern, Indexed, Separation, or DeviceN)" | N | ␀ | |
| 5-2 | "if any of the component names […] do not correspond to a colorant available on the device, [the processor] shall perform subsequent painting operations in the alternate colour space" | N | ⚠️ | All-or-nothing fallback. Correct for plain DeviceN — but see 5-3. |
| 5-3 | **"For NChannel colour spaces, the components shall be evaluated individually; that is, only the ones not present on the output device shall use the alternate colour space of that component."** | N | ❌ | **VIOLATION.** `NChannel` appears nowhere in the rendering path of either repo (only in `Conformance/`). With the all-or-nothing fallback, one unregistered colourant in an NChannel space flattens *every* colourant through the alternate, including those we can paint. See gap G-4. |
| 5-4 | tintTransform "shall be called with n tint values and returns m colour component values" | N | ⚠️ | |
| 5-5 | **None** "may be present only for DeviceN colour spaces that do not have the NChannel subtype" | N | ␀ | Not enforced; requires Subtype awareness (blocked on G-4). |
| 5-6 | None "indicates that the corresponding colour component shall never be painted on the page" | N | ⚠️ | `ShadingSpotSplit`, `TryToSpotInk` skip None components. |
| 5-7 | "When […] painting the named device colourants directly, colour components corresponding to None colourants shall be discarded" | N | ⚠️ | |
| 5-8 | "when the DeviceN colour space reverts to its alternate colour space, those components shall be passed to the tint transformation function" | N | ␀ | **Subtle and easy to get backwards**: None is discarded when painting directly but *passed through* on reversion. Not audited. |
| 5-9 | All-None space "shall always discard its output […] it shall never revert to the alternate colour space" | N | ␀ | |
| 5-10 | "Reversion shall occur only if at least one colour component (other than None) is specified and is not available on the device" | N | ⚠️ | Cited verbatim in `PdfImageToCmyk.TryToSpotInk` (SP-6c) — the routing splits by colorant name and never consults the alternate. |
| 5-11 | Subtype "shall be DeviceN or NChannel. Default value: DeviceN" | N | ❌ | Not read on the render path at all (G-4). |
| 5-12 | "If the value of the Subtype entry […] is NChannel, such information shall be present" (attributes) | N | ␀ | |
| 5-13 | Mixing hints: "applications shall ignore these process component entries if they can obtain the information from an ICC profile" | N | ␀ | Mixing hints not consumed on the render path. |
| 5-14 | "PDF processors need not use the alternateSpace and tintTransform parameters, and may instead use custom blending algorithms" | L | — | Explicit permission for our additive spot fold. **This is the clause that makes the spot-combine model a design decision rather than a compliance question.** |
| 5-15 | NOTE 5 — processors "are free to use such information instead of the alternateSpace parameter" | L | — | Same permission, restated for the attributes dictionary. |
| 5-16 | Guideline: "should apply either the specified tint transformation function or invoke the same alternative blending algorithm for all DeviceN instances in the document" | L | ⚠️ | `should`, not `shall`. We are consistent by construction (one registry per document). |
| 5-17 | Guideline: blending "should produce a similar appearance […] as separation colours or as a component of a DeviceN colour space" | L | ⚠️ | Same ramp per colorant regardless of arity — consistent by construction. |

---

## Score — slice 1

| | Count |
|---|--:|
| Normative + machine-verifiable (**N**) | 26 |
| — ✅ conformant with a test | **0** |
| — ⚠️ conformant, untested | 13 |
| — ❌ violation | 2 |
| — ␀ not yet audited | 11 |
| Latitude (**L**) | 5 |
| Device-policy (**D**) | 2 |

**Nothing in this slice is ✅.** That is the headline finding, and it is not the same as "broken": 13 rows
look correct on inspection and some have unit coverage of their helper, but no test asserts the *clause*.
Untested conformance is indistinguishable from accidental conformance, and cannot be ratcheted.

## Gaps

- **G-1 (D-1) — additive-device reversion.** §8.6.6.4 requires that on a display, Separation *always*
  reverts to the alternate. Whether any spot-plane routing is reachable outside the CMYK soft-proof path
  is unaudited. If it is, that is a violation; if it is not, this is conformant and needs a test naming
  the clause. **Audit this first — it determines whether rows 4-11/4-12 are compliance or violation.**
- **G-2 — `All` excludes spot planes.** Row 4-6 requires "all *available* colourants". We set the four
  process plates only. Once spots are registered and paintable, they are available by our own definition,
  so registration targets would miss them.
- **G-3 — `All` on an additive device is not complemented.** Row 4-7 requires subtracting from 1 before
  applying on an additive device. No such logic found.
- **G-4 — NChannel is not implemented on the render path.** Rows 5-3 and 5-11. The per-component
  evaluation rule is a `shall`, and we do the opposite (all-or-nothing). Blocks 5-5 and 5-12 too.

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

Next slices, in rough value order: §8.6.7 (overprint control / OPM), §8.6.5.x (CalRGB, CalGray, Lab,
ICCBased), §8.7.3 (blend modes), §11.6.5.3 (soft masks — the `/Matte` rule fixed 2026-07-25).
