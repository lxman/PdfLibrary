# Colour G-7 — colorant placement on the carrier

**Date:** 2026-07-27
**Status:** design approved, scope measurement-gated (see §6)
**Supersedes nothing.** Continues the colour conformance programme: Pass 2a (carrier), 2a′ (prime),
2b-engine (images/stencils), 2b-compositor (fills/strokes).
**Conformance rows in play:** 5-3, 5-10 (shading exclusions), 4-6 (`/All` shadings — *not* addressed
here; see §8).

---

## 1. The defect, stated physically

A printing press has **units**. Each unit carries one ink and one plate. A CMYK press has four; a
six-colour press has four plus two spot units. Every clause in play here is about which unit a
colorant lands on, and this document uses that frame throughout because it is the frame that decides
the hard cases.

- **DeviceN/NChannel names** the colorants the artwork uses — each is a candidate for its own unit.
- **`/Process` (ISO 32000-2 Table 71)** says: these named colorants are *not* separate inks, they
  **are** the process inks, identified by **position**. `/Components [PrCyan PrMagenta PrYellow Black]`
  means "PrCyan is my name for the cyan unit." Position is the channel identity; the name does not
  carry it.
- **The alternate space + tint transform** is the *simulation recipe* — "if you have no unit for this
  ink, here is how to fake it with the inks you do have."

Read that way, ISO 32000-2 §8.6.6.5 — *"the components shall be evaluated individually; that is, only
the ones not present on the output device shall use the alternate colour space of that component"* —
is a one-line instruction to the printer:

> **Run the real ink where you have the unit. Simulate only where you don't.**

In this codebase that is not a metaphor. A **registered** spot means a plane exists, i.e. we have a
unit; **unregistered** means we don't, which is why reversion-through-the-alternate is correct there
and only there.

**The defect is a single one — *we simulate inks we actually have* — hand-written at five sites.**
Previous passes closed two of them. This design closes the rest by moving the rule off the consumers
entirely.

### 1.1 The five sites

Four are the same literal reserved-name `switch`. The fifth is the same defect by the opposite
mechanism: it does not mis-place the ink, it simulates ink it should have placed.

| # | Site | Repo | Mechanism | Status |
|---|------|------|-----------|--------|
| 1 | `PdfImageToCmyk` (`TryToSpotInk` / `StencilInkFromFill`) | PDF | name switch | closed by Pass 2b-engine |
| 2 | `InkDecider.TryPerComponent` (fills/strokes) | Pellucid | name switch | closed by Pass 2b-compositor |
| 3 | `ShadingSpotSplit.Split` + the mesh path | PDF | name switch | **open** |
| 4 | `InkDecider.ProcessContribution` (`:446-468`) | Pellucid | name switch | **open** |
| 5 | `ShadingBuilder.BuildCmykMapper`'s all-process arm | PDF | runs the tint transform | **open** |

Site 3, verbatim at HEAD:

```csharp
case ColorantKind.Process:
    switch (names[j])
    {
        case "Cyan": c = v; break; case "Magenta": m = v; break;
        case "Yellow": y = v; break; case "Black": k = v; break;
    }
```

Site 4, verbatim at HEAD:

```csharp
switch (origin.Names[i])
{
    case "Cyan":    c = tint; pc = true; break;
    ...
}
if (!overprint) pc = pm = py = pk = true;
```

Under an NChannel space naming `PrCyan`, neither switch matches. Site 3 sends the cyan ink to a
**spot plane**; site 4 reports that the object **does not image the cyan unit**. On press that is:
order a fifth can of ink, mount a unit for it, run it beside a cyan unit that now sits dry — and then
tell the press not to run cyan at all.

### 1.2 The stale premise this design corrects

`Docs/colour/rendering-conformance.md`'s G-7 entry says a shading *"falls through to the flattened
path."* **It does not.** `ShadingBuilder.cs:73-97` already builds a per-stop spot split (SP-7) and
`MeshShadingReader.cs:61` a per-vertex one (SP-7-mesh); the compositor consumes both at
`CmykPageRenderer.cs:611` and `:806`. What is true is narrower: `rawColor: null` leaves
`origin.Tints` empty, so the *fills/strokes* machinery turns shadings away — already on record as the
compositor pass's `placed`-guard reachability finding.

The matrix entry must be corrected as part of this work (§8). Recorded here because this programme's
standing lesson is that every review finding so far originated in plan or doc text, never in an
implementer's code — and this is one more.

---

## 2. Architecture: placement belongs on the carrier

### 2.1 Why the carrier

`ColorantOrigin`'s own XML documentation already states the fact this design turns on:

> *"Shadings and meshes resolve their origin with no per-op colour (`Tints` empty), so every component
> in the list gets a null `Tint` … a fully populated, **role-classified** list."*

**Role and channel are already populated for shadings and meshes.** Nobody reads them. They are
properties of the *colour space*, not of the paint operation: which unit a colorant belongs on does
not depend on how much ink is being laid down. The shading supplies the per-stop values; the carrier
supplies the placement; they compose.

`ColorSpaceResolver.BuildComponents` is the only site that can see `/Process`. It already computes
role, channel and channel-count there. Placement is the fourth thing derivable from the same read.

### 2.2 The shape

```
Slot      = Plate(0..3) | SpotSlot(n) | Nothing
Placement = { IReadOnlyList<Slot> Slots;  IReadOnlyList<string> SpotNames; }
```

Exposed as `ColorantOrigin.Placement`, non-null **exactly when every component is placeable and
`ProcessChannelCount == 4`**; null otherwise.

**"Placeable" is defined, not left to the reader.** A component is placeable when it maps to exactly
one of `Plate`, `SpotSlot` or `Nothing`:

| Role | Condition | Slot |
|------|-----------|------|
| Process | `ProcessChannel` non-null | `Plate(channel)` |
| Process | `ProcessChannel` null | **unplaceable** — refuses the whole table |
| Spot | always | `SpotSlot(next)` |
| None | always | `Nothing` |
| `/All` | always | **unplaceable** — refuses the whole table (§8) |

`Nothing` is a placement, not a failure: `/None` is a colorant the printer deliberately does not run.
Only a Process component whose channel cannot be determined, or an `/All`, refuses the table.

`Slots` is aligned index-for-index with `Names`/`Components`. `SpotSlot(n)` indexes `SpotNames`,
which fixes the order spot tint arrays are written in.

### 2.3 What that one nullability rule buys

Three rules currently re-implemented at every consumer move to the carrier and are stated once:

1. **The count-4 gate.** A channel index is a plate index only under a four-channel process space.
   Under `/DeviceGray` a listed name also gets index 0, byte-identical to a `/Cyan` under CMYK — the
   measured M4 finding from Pass 2b-engine. Any consumer mapping channel→plate must check the count;
   on the carrier it cannot forget to.
2. **The all-or-nothing rule.** If any single component is unplaceable, the whole table is refused
   and the consumer falls back whole. Pass 2b's equivalent was found **silently unpinned** by
   mutation B and needed a contingency test written mid-pass. On the carrier there is one place to
   pin it, and site five cannot reintroduce the gap.
3. **`/None` → `Nothing`; `/All` → refuse whole.**

Consumers collapse to:

```csharp
if (origin.Placement is { } p) { /* place by slot */ } else { /* whole-space fallback */ }
```

### 2.4 The boundary the table does not cross

The table says `SpotSlot(2)`. It never says *"spot slot 2 has a unit."* Registration is a registry
fact and the registry is compositor-side.

> **The carrier answers "which colorant is this." The compositor answers "do we have that unit."**

This is what keeps the design from becoming a layering violation, and it is the reason the engine
cannot simply emit final plate values.

### 2.5 Hard constraint: the table resolves nothing new

The programme's dominant defect class, across 22 findings, is: *a new member access resolves a PDF
object the previous code never touched, and throws out of a path that used to succeed.*

Placement is derivable **entirely from values `BuildComponents` has already materialized**, inside
its existing `/Process` try.

> **The placement table MUST be a pure function of already-materialized data. It MUST NOT dereference
> anything.**

If that constraint holds, this pass adds no deref, needs no new `try`, and sidesteps its own most
likely failure mode by construction. **Task 0 confirms it; this spec does not get to assert it.**

---

## 3. The preserve signal is the mask, generalized

The standing instruction for a preserve→knockout flip is *fix it properly — add the compositor-side
"process-only, preserve plates" signal* that Pass 2b-engine named as the precondition for closing
this. Working it through, **that signal is not a new mechanism.**

An all-process NChannel op has no spot components, so no routing occurs. Category stays
`SeparationDeviceN` (origin non-null), so `Decide` takes row 3, whose mask comes from
`ProcessContribution`. Drive that mask from the placement and the op paints **exactly the plates the
space names** and preserves the rest — which is the definition of "process-only, preserve plates."

The knockout arm (`InkDecider.cs:201-205`, *paint source on every process plate*) then fires only
when there is **no** placement — i.e. when we genuinely do not know which units the object images,
which is the only honest reason to knock out.

Consequence for scope, and it is load-bearing: **the preserve signal is the placement-driven mask.**
A site the mask cannot reach is a site the signal does not fund, and per §6 it drops out of the pass.

**This section is reasoning, not measurement.** M1 must confirm which arm each site takes today.

---

## 4. Sites and changes

### 4.1 Site 3 — `ShadingSpotSplit.Split` (+ mesh)

Values from the shading function are placed by `Slots` rather than by reserved name. `SpotNames` for
`ShadingSpotInk` / `MeshSpotInk` comes from `Placement.SpotNames` (role-derived) rather than
`SpotNames(origin.Names)` (name-derived). Whole-space fallback when `Placement` is null.

**Note the consequence:** under per-component rules an all-process NChannel space has *zero* spot
components, so `splitSpots` goes false and the op never produces spot ink at all. It is then handled
by §4.3, not here. This is the shading analogue of the shape Pass 2b-engine's I-1 turned on.

### 4.2 Site 4 — `InkDecider.ProcessContribution`

The plate mask is derived from `Slots` when `Placement` is non-null, reserved names otherwise. This
is simultaneously the site-4 fix and the preserve signal of §3.

The existing precondition — *`Tints` may be shorter than `Names`; a shading wants only the boolean
mask* — is unchanged and still load-bearing, because the mask must be derivable with no per-op tint
at all.

### 4.3 Site 5 — `ShadingBuilder.BuildCmykMapper`'s all-process arm

Where every component is Process with a determinable channel, **the tint transform must not run**:
every colorant has a unit, so §1's rule forbids simulating any of it. Components are placed at their
channels directly.

This is the purest instance of the clause in the whole programme — "every colorant is available" is
exactly the condition under which *nothing* should use an alternate — and it is the one shape where
we currently simulate 100% of the colour. In veraPDF `6-2-4-4-t02-pass-a` the tint transform is an
identity pass-through, so values arrive in **names** order at CMYK positions: the yellow value prints
on the magenta unit.

**Gated on M5**, which must confirm a shading of that space gets `toCmyk` non-null at all.

### 4.4 Migration of sites 1 and 2

Migrated to `Placement` **only where M4 shows old and new agree component-for-component on every
corpus instance.** Where they do not, the site stays as it is and the duplication is recorded.

Two implementations of one physical rule is how a plate ends up disagreeing with itself. But
replacing verified code on an argument rather than a measurement is how this programme got I-1.

---

## 5. Evidence

### 5.1 The corpus is a guard, not evidence

M2's prediction on record: **no digest moves.** GWG081 `Sh0` is `[Black, GWG Green]`, where Black is
both a reserved name and `/Process` channel 3, so name-split and per-component agree.

A green gate is consistent with both *"nothing changed"* and *"the gate cannot see what changed"* —
the Pass 2a′ shape. Pass 2b-engine's whole-branch reviewer had to prove the central claim with an
independent corpus probe. **The same standard applies here: the claim is proven independently of the
gate, and the gate corroborates.**

### 5.2 Only positional assertions can see this fix

From Pass 2b-compositor's Task 0, and binding on every assertion in this pass:

> before `(0, 0.36, 0.57, 0.02)` → after `(0.36, 0.57, 0.02, 0)`

Identical multiset. Identical sum (0.95). Identical max. Identical total ink. **Any assertion phrased
as total ink, sum, max, `Assert.Contains`, or a loose ΔE passes both ways.**

> **Every assertion in this pass is a positional per-plate assertion, or it is decorative.**

### 5.3 Assert the arm, not only the colour

I-1 was invisible in colour: the value was right, the colour was right, and the regression was a
category flip three files away. Fixture assertions therefore cover **plate values *and* the overprint
arm taken**.

### 5.4 Mutation discipline

- Every prescribed mutation names **which assertion in which fixture changes value.** If that cannot
  be named, the mutation is decorative (defects #19 and #22 — twice a mutation was written against a
  fixture that could not observe it).
- The carrier's all-or-nothing rule gets its **own** pin. Pass 2b's equivalent was unpinned and the
  whole suite stayed green when it was dropped.
- A *"must already pass"* classification is a prediction like any other: verify it, do not assert it.

### 5.5 Not a usable oracle here

**Plane-cap invariance.** It is not universal: `OwnColorantRamp` gates on a DeviceCMYK whole-space
alternate and `OwnAlternateFor` does not. It must not be leaned on as the reversion oracle.

---

## 6. Task 0 and the scope rule

Scope is **measurement-gated**. Task 0 makes no commits and leaves both trees clean.

| ID | Measurement |
|----|-------------|
| M1 | What each open site paints **today**: per-plate values at a named pixel, the overprint arm taken, and the `InkSourceCategory`. Colour alone is insufficient (§5.3). |
| M2 | Corpus census of every NChannel shading and mesh across GWG and veraPDF. Prediction on record: no digest moves. |
| M3 | That `Components`, `ProcessChannel` and `ProcessChannelCount` really are populated for a *shading* origin. §2.1 rests on it. |
| M4 | Whether old and new placement agree component-for-component at sites 1 and 2, on every corpus instance. Precondition for §4.4. |
| M5 | Whether a shading of `t02-pass-a`'s space gets `toCmyk` non-null — i.e. whether its own alternate (array element 2) resolves to CMYK. **Unverified assumption in §4.3.** |
| M6 | That placement is derivable with **no new dereference** (§2.5). |

### 6.1 The rule, fixed in advance

Written before the measurements exist, so the results cannot be read to suit us.

1. A site joins the pass **only if both**: (a) it demonstrably mis-places a colorant that has a unit,
   **and** (b) a fixture exists that can observe the fix **positionally**.
2. A 2b site is migrated **only if** M4 shows agreement everywhere. Otherwise it stays; the
   duplication is recorded.
3. If a site's fix would flip preserve→knockout and the placement-driven mask does not cover it,
   **that site drops out** and gets its own pass. The preserve signal is the mask (§3); a site the
   mask cannot reach is unfunded.
4. If M6 fails — placement needs a dereference — the constraint in §2.5 is void and the design
   returns for revision **before** implementation, because the guard placement becomes the pass's
   dominant risk rather than a non-issue.

---

## 6.2 Delivery

**Provisional, and deliberately so.** Pass 2b's design stated a two-plan delivery, was wrong, and had
to be corrected mid-pass to three once Task 0 established that the image path had no `ColorantOrigin`
at all. The repo boundary is a fact discovered by reading, not a shape chosen in advance.

Expected shape, to be confirmed or corrected by Task 0:

1. **Engine — carrier.** `Placement` on `ColorantOrigin`, computed in `BuildComponents`. Site 3
   (`ShadingSpotSplit` + mesh) consumes it. Possibly site 5, gated on M5.
2. **Compositor — mask.** Site 4 (`ProcessContribution`), which is also the preserve signal (§3).
   Requires an engine pack-and-repin, so it follows (1).
3. **Migration**, if and only if M4 permits (§4.4). Own plan; it re-opens verified behaviour and
   should not share a review surface with new work.

The count is a prediction. If Task 0 contradicts it, the correction is recorded in the design with
the original text preserved and superseded rather than deleted, per this programme's convention.

---

## 7. Validation frame

For every site, the question is not code-shaped:

> **What would the printer do here, and does our code do that?**

| Printer's question | Answered by |
|---|---|
| Which units are mounted? | registry — compositor |
| Which colorant belongs on which unit? | placement table — carrier |
| How much ink, and where? | per-stop values × coverage mask — paint loop |
| Do I run a unit at all for this element? | preserve vs. knockout — the mask |

---

## 8. Explicitly out of scope

- **`/All` shadings and meshes (row 4-6).** `/All` refuses the placement table whole (§2.3) and keeps
  today's behaviour. An `/All` shading images *every* mounted unit including spots, which is a
  different rule from placement and deserves its own pass.
- **Per-stop spot reversion for unregistered spots (row 5-10).** Reversion needs a per-sample own-
  alternate colour, which nothing carries; and §5.5 removes the invariant that would have been its
  oracle.
- **Row 5-3 for one-channel process spaces.** The count-4 gate refuses them by construction, as in
  Pass 2b.
- **The matrix correction is in scope but is a separate docs-only commit**: G-7's stale "falls through
  to the flattened path" text (§1.2) is rewritten to name the real sub-gaps.

---

## 9. Open items carried in

Unchanged by this design, listed so they are not re-derived:

- `TryToCmyk` reaches a corrupt alternate unguarded, one call before `TryToSpotInk` on the render
  path. Pre-existing.
- No `/Indexed`-over-NChannel synthetic test, though that is the only real-world NChannel image shape.
- `PageColorantReader`'s outer catch at `:34-38` still has no `PdfLogger.Log`.
- G-12 (no cache).
- The App.Tests headless-session death (Pellucid). Root cause unknown; mitigation partial.
