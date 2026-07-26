# Clearing the unaudited rows — ␀ sweep of §8.6.6.4 / §8.6.6.5

**Date:** 2026-07-25
**Scope:** `PdfLibrary` (engine) only. No Pellucid changes.
**Matrix:** `Docs/colour/rendering-conformance.md`

## Goal

Drive the renderer matrix's **␀ (not yet audited) count to zero**. The matrix's own method note
says "the count of ␀ is itself the measure of how far this slice got"; nine of twenty-six normative
rows are currently unaudited, which is the gap between having a matrix and having an answer.

This is deliberately an *audit* slice, not a feature slice. Its output is knowledge — plus fixes for
whatever the audit finds, on the same principle the previous two slices established: a row that is
found non-conformant gets fixed and pinned in the same pass, because re-loading the context later
costs more than fixing it now.

**Scope guard.** That principle has a limit, and an audit slice is exactly where unbounded scope
creeps in: the whole point is that we do not yet know what we will find. Only 4-4 is a *known*
violation with a *known* fix, and it is budgeted for here. If auditing 4-2 or 5-13 uncovers a
violation whose fix is comparable in size to 4-4's or smaller, fix it. If it uncovers something
larger — anything needing new machinery, a cross-repo change, or a decision about device policy —
**record it as a gap with the evidence and stop**. The row still leaves ␀, because it has been
audited; ❌ with a written-up gap is a complete audit result, not an unfinished one. Discovering a
large violation is a good outcome for this slice, not a reason to grow it.

## The nine rows are not homogeneous

Investigating them first was the point of scoping this slice, and it changed the shape of the work:
five of the nine are not renderer statements at all.

| Group | Rows | Nature |
|---|---|---|
| **A — renderer behaviour** | 4-2, 4-4, 5-8, 5-13 | Real claims about what the renderer does. Audit and test. |
| **B — file validity** | 4-1, 4-14, 5-1, 5-5, 5-12 | Constrain what a valid *file* may contain. Not renderer behaviour. Reclassify. |

## Part 1 — Introduce class F, and move five rows into it

The document already classifies rows as **N** (normative, machine-verifiable), **L** (latitude) and
**D** (device-dependent), and states that the score is over N only because "counting [L and D] would
inflate the denominator with things that cannot be failed". The same argument applies to file-validity
clauses, and for a sharper reason: this document is explicitly "the **renderer's** conformance matrix
— the companion to `Docs/pdfua/matterhorn-coverage.md`, which does the same job for the validator."

A clause saying an `alternateSpace` "may not be another special colour space" tells you what a
conformant *writer* must emit and what a *validator* must reject. It says nothing about what the
renderer should paint when the constraint is violated — the clause specifies no behaviour for invalid
input at all. Testing it as renderer behaviour would pin *our* choice of degradation, not the
standard's requirement: a ✅ that means less than it looks, which is the failure mode this matrix
exists to avoid.

**Add class F — file validity.** Excluded from the N score, like L and D. Five rows move:

| Row | Clause |
|---|---|
| 4-1 | Separation "shall be a four-element array whose first element shall be the colour space family name Separation" |
| 4-14 | alternateSpace "may not be another special colour space (Pattern, Indexed, Separation, or DeviceN)" |
| 5-1 | Same restriction for DeviceN |
| 5-5 | None "may be present only for DeviceN colour spaces that do not have the NChannel subtype" |
| 5-12 | "If the value of the Subtype entry […] is NChannel, such information shall be present" |

**5-5 and 5-12 moving here retires a dependency.** Both were recorded as blocked on G-4 because they
need DeviceN `/Subtype` awareness. As renderer rows that was true. As file-validity rows the
`/Subtype` read belongs to the validator, so no NChannel work is needed in this slice at all.

**Each F row records who enforces it.** Investigation found `PdfLibrary/Conformance/` has exactly two
relevant rules — `PdfxNChannelColorantsRule` and `PdfxSeparationConsistencyRule` — and **both are
profile-gated** (`AppliesToProfiles = AllPdfA | PdfX4`). Neither checks the array shape (4-1) or the
alternateSpace restriction (4-14, 5-1), and neither applies at baseline ISO 32000-2. So the honest
note on most F rows is *"validator gap — not enforced at baseline"*. Moving a row to F must not read
as retiring it; it reassigns it, and says to whom.

## Part 2 — Audit the four renderer rows

### 4-4 — initial colour value. **Confirmed violation; fix in this slice.**

`PdfContentProcessor.cs:322` handles `cs` and carries this comment:

```csharp
case SetFillColorSpaceOperator cs:
    CurrentState.FillColorSpace = cs.ColorSpace;
    // Note: Do NOT initialize color or call OnColorChanged() here.
    // The color will be set by a subsequent sc/scn operator.
    // Initializing color to default values causes Separation color spaces
    // to render with tint=0 (white) until the scn operator is processed.
```

A previous attempt initialised to zero, broke Separation to white, and was backed out entirely rather
than corrected. So `cs`/`CS` now leave the *previous* colour in place, and a content stream that sets
a colour space and paints without an intervening `scn` uses stale carry-over.

The required behaviour is per-space, read from ISO 32000-2:2020 EC2 §8.6.8 Table 73 (not recalled):

| Colour space | Initial colour |
|---|---|
| DeviceGray, DeviceRGB, CalGray, CalRGB | all components 0.0 |
| **DeviceCMYK** | **[0.0 0.0 0.0 1.0]** |
| Lab, ICCBased | all 0.0, "unless that falls outside the intervals specified by the space's Range entry, in which case the nearest valid value shall be substituted" |
| Indexed | 0 |
| **Separation, DeviceN** | **initial tint 1.0 for all colourants** |
| Pattern | "a pattern object that causes nothing to be painted" |

Two traps sit in that table, and the abandoned fix walked into the first:

1. **DeviceCMYK is not all-zeros.** All-zeros in CMYK is *white*; the spec requires `[0 0 0 1]`, i.e.
   black via the K plate. A naive "initialise everything to 0" makes DeviceCMYK wrong in the same way
   it made Separation wrong. §8.6.6.5 states the DeviceN rule independently: "each component shall be
   given an initial value of 1.0".
2. **Lab and ICCBased are Range-clamped**, not simply zero.

The Pattern row is noted but **out of scope**: "a pattern object that causes nothing to be painted" is
a distinct concept from the `PaintsNothing` signal introduced for `/None`, and conflating them without
an audit would be exactly the kind of unexamined leap this matrix is built to prevent. Recorded as a
gap instead.

### 5-8 — None components passed to the transform on reversion. **Expected conformant; needs a test.**

`ResolveDeviceN` evaluates `tintTransform.Evaluate(color.ToArray())` — every component, unfiltered,
including those naming `/None`. That is exactly what the clause requires. The matrix flags this row as
"subtle and easy to get backwards" because its sibling 5-7 requires the *opposite* (None components
discarded when painting colourants directly), and the two are one line apart in the spec.

A test must fail if the components were filtered — so the `/None` component's tint has to
*matter* to the transform's output. A transform whose output ignores that input would pass whether or
not the component was passed.

### 4-2 — tint is a single component in [0.0, 1.0]

`ResolveSeparation` guards on `color.Count == 1`, so component count is handled. The open question is
range: what happens to a tint outside [0,1]. Audit whether it clamps, and where — the function's
`/Domain` may already clip. Test the outcome, not the mechanism.

### 5-13 — mixing hints ignored when ICC data is available

We never consume mixing hints at all, which is a superset of the required behaviour. Two honest
readings, to be settled by reading the clause in context during the audit:

- **✅** — the requirement is "don't prefer mixing hints over an ICC profile", and never reading them
  satisfies it unconditionally.
- **Class F or L** — the clause presumes a processor that consumes them, making it inapplicable rather
  than satisfied.

Pick one, state the reasoning in the row, and do not mark ✅ without a test if ✅ is chosen — a row
that cannot fail should be reclassified, not scored.

## Expected outcome

| | Now | After |
|---|--:|--:|
| **N** total | 26 | **21** |
| — ✅ | 11 | 14–15 |
| — ⚠️ | 4 | 4 |
| — ❌ | 2 | 2 |
| — ␀ | 9 | **0** |
| **F** (new) | — | 5–6 |

The headline is **␀ = 0**: no unaudited rows left in the renderer matrix. What remains non-✅ is then
exactly two known things — the four ⚠️ rows whose behaviour lives on the CMYK soft-proof path, and
G-4 (NChannel), which owns both ❌.

The ✅ range is honest uncertainty: 4-2 and 5-13 may resolve to ✅, to a violation, or to
reclassification. **A ␀ row resolving to ❌ is the audit succeeding, not failing** — that is what
happened to 4-8 and 4-10 in the first ratchet pass, and it is the reason this slice is worth running.

## Testing

The standard set by the previous two slices holds without change:

- Clause-citing: every test names the ISO clause and row in a doc comment.
- **Every test observed to fail before it counts** — ordinary TDD red where behaviour is new, and a
  deliberate mutation where the test pins already-correct behaviour.
- Claims about painted output asserted on **rendered pixels**, via `ColourConformancePage`.
- Backdrop colours must contrast with the colour under test.

4-4 additionally needs a test per initial-colour row of the table above, because the per-space values
differ and a single-space test would leave the DeviceCMYK trap uncovered.

## Verification

1. `PdfLibrary.Tests` (2484 baseline) green.
2. Repack the engine, repin **both** consumers — `Directory.Build.props.local` (re-adding the Skia pin
   the packer drops) and `PdfCompare.csproj` — then Pellucid's suites (1268 baseline) green.
3. **Full GWG / veraPDF corpus gate**, compared against baseline.

Step 3 is mandatory and 4-4 is why. Changing what `cs`/`CS` do to the current colour touches the two
most frequently executed colour operators in every content stream, and the one prior attempt at this
fix caused a visible regression. Unit tests cannot cover the interaction surface; real documents can.

## Risks

**4-4 is the entire risk of this slice.** Everything else is reading and testing. Specific hazards:

- Content streams that rely on today's carry-over behaviour will change appearance. Some of those
  changes are the fix working; the gate is how we tell them apart from regressions.
- Setting the initial colour requires deciding whether `OnColorChanged()` fires on `cs`. It must, or
  the resolved colour goes stale — but that is a second behavioural change riding along with the
  first, and it needs to be called out rather than slipped in.
- If the gate moves fixtures, **stop and report** rather than re-baselining. A moved fixture is either
  a genuine improvement worth recording or a regression worth fixing; both are the human's call.

## Out of scope

- **G-4** — NChannel per-component evaluation. Both remaining ❌ rows. Its own slice.
- **G-9** — `/All` images and stencil masks diverging on the CMYK path.
- **G-7, G-8, G-10** — recorded gaps, untouched.
- **Pattern initial colour** ("a pattern object that causes nothing to be painted") — noted during the
  4-4 clause read, recorded as a new gap, not implemented.
- The four ⚠️ CMYK-path rows (4-5, 5-6, 5-7, 5-10) — they need a soft-proof test harness, which is a
  different slice from an audit.

## Success criteria

- ␀ = 0 in the renderer matrix.
- Class F exists, is excluded from the N score with the reasoning stated, and every F row names who
  enforces it — including "validator gap" where nothing does.
- 4-4 fixed with a test per initial-colour case, including DeviceCMYK's `[0 0 0 1]`.
- Every new ✅ backed by a test observed to fail.
- Corpus gate clean, or any movement reported rather than absorbed.
- The score table recomputes from the rows (the script, not by hand).
