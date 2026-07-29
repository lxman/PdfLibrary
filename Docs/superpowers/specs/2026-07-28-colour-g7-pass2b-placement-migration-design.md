# G-7 migration: the two Pass 2b sites consume ColorantPlacement

**Date:** 2026-07-28
**Status:** approved in session; supersedes nothing — this is design §4.4 of
`2026-07-27-colour-g7-colorant-placement-design.md`, executed under its rules.
**Parent design:** `Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md`
**Gating measurement:** M4 (`.superpowers/sdd/2026-07-27-colour-g7-plan1-carrier-placement/task-0-report.md` §6)

---

## 1. Goal and non-goals

The migration ends with **one implementation of the position→plate rule**:
`ColorantPlacement.Build` becomes the only code in either repo that turns
`ColourantComponent.Role`/`ProcessChannel` into slots. Sites 1 and 2 (the parent design's §1.1
numbering) switch to consuming `ColorantOrigin.Placement`; sites 3, 4 and 5 already do.

**Zero behaviour change is the acceptance bar.** Every existing test stays green untouched; GWG
51/51 and NChannel 3/3 with 0 differences; and the three M4-documented refusal divergences are
preserved verbatim — including the **asymmetry between the sites** (site 1 refuses a no-spot split,
site 2 succeeds on one; that difference is load-bearing, per site 1's I-1 category-flip guard).

Non-goals: refusal-policy harmonization (a separate, measured pass if ever), `/All` shadings
(row 4-6), per-stop spot reversion (row 5-10), G-8/G-12, any change to what any pixel paints.

## 2. Why this is legal, and why now

M4 (measured, not argued): on all 17 corpus NChannel instances the placement table agrees with both
shipped sites' **slot assignment** component-for-component. The sites differ from the table only in
**refusal policy** — three cases where a site refuses whole and the table succeeds:

| # | Case | Who refuses | Table says |
|---|------|-------------|------------|
| R1 | Process component with a null `Tint` | site 2 | placeable (tint-free) |
| R2 | Spot with neither registry plane nor own alternate | site 2 | `Spot(n)`; registry question is compositor-side |
| R3 | Split containing no spot at all | site 1 (deliberate — I-1 guard) | non-null table |

Parent §6.1 rule 2 permits the migration on the slot mapping; parent §4.4 requires R1–R3 to be
re-stated at their sites or the migration changes behaviour. This spec re-states them.

Approach chosen: **adapter in place**. Each site keeps its public shape and call sites; only the
slot-assignment half of its internals reads `Placement`. Rejected: a shared engine-side conversion
helper (the two sites want different shapes — arrays vs an accumulating loop — so "shared" would be
duplication with extra steps), and any refusal harmonization (behaviour-changing, out of scope).

## 3. Site 1 — `PdfImageToCmyk` (engine)

**The change.** `SplitByComponents(IReadOnlyList<ColourantComponent>)` is replaced by a
placement-consuming equivalent (working name `SplitByPlacement(ColorantPlacement)`) producing the
same `(int[] Plate, int[] SpotOf, List<string> SpotNames)` triple both callers already consume:

- `ColorantSlotKind.Plate` → `plate[c] = slot.Index`, `spotOf[c] = -1`
- `ColorantSlotKind.Spot` → `plate[c] = -1`, `spotOf[c] = slot.Index`; `SpotNames` is taken
  directly from `placement.SpotNames`, not re-accumulated
- `ColorantSlotKind.Nothing` → both `-1` (the `/None` contributes-nothing answer, unchanged)

**R3 stays verbatim**: the closing `SpotNames.Count == 0 → null` guard keeps its full I-1 comment.
The `/All` and unplaceable-Process refusals **delete from site code** — `Build` already refuses
those by returning a null table. Their explanatory comments move to (or are confirmed already
present on) `ColorantPlacement.Build`, so no reasoning is lost.

**The gates.** `ComponentSplit`'s `origin is { Components: { }, ProcessChannelCount: 4 }` becomes
`origin.Placement is { } p` — the table's nullability rule is those same two checks plus
`/All`/unplaceable, cases the old path refused one call later. Same outcomes, one boundary earlier.
The alignment check `comps.Count == nameCount` becomes `p.Slots.Count == nameCount` (`Slots` is
index-aligned with `Components` by construction). `StencilInkFromFill`'s inline gate at
`PdfImageToCmyk.cs:447` migrates identically.

**Untouched:** the name-split fallback arm, `SpotImageInk`'s shape, the constant-cell replication,
the Tints-shorter-than-Names tolerance, `ComponentSplit`'s call-site try/catch and its rationale.

**Spot order is an equivalence claim, not an assumption.** `SplitByComponents` emits spot slots in
component order; `Build` constructs `SpotNames` in the same component order. Same sequence — but the
plan's tests pin it **positionally**, because a spot-order swap is silent plane corruption (the
adjacent-stop lesson).

## 4. Site 2 — `InkDecider.TryPerComponent` (Pellucid)

**The change.** The loop switches on `slots[i].Kind` instead of `components[i].Role`:

- `Nothing` → `continue` (today's `ColourantRole.None` arm — no `/Colorants` lookup, row 5-7)
- `Plate` → the process arm with `slot.Index` replacing `c.ProcessChannel!.Value` — the actual
  migration. The `Tint: { } pt` requirement stays (**R1**): a null tint is unplaceable, not zero.
- `Spot` → the three spot arms unchanged and in order: registry plane + tint → route; else
  own-alternate reversion (the `placed`-outside-the-loop white-alternate subtlety intact); else
  the `default` refusal (**R2**).

The loop still iterates `components` in parallel with `slots` for `Tint`/`Name`/`OwnAlternateCmyk`;
`Build` guarantees `Slots.Count == Components.Count`. How `TryPerComponent` receives the placement
(extra parameter vs reading the origin) is a plan-level choice matching how `Decide` threads
`components` today.

**Untouched:** the `placed` flag and its all-/None scope gate (all-`Nothing` slots place nothing and
the branch declines, exactly as today), additive-clamp combining, mask widening under overprint,
knockout behaviour, and the empty-routes success — site 2's side of the R3 asymmetry.

**The gate.** `Decide`'s entry condition (`Components` non-null ∧ `ProcessChannelCount == 4`)
becomes `Placement` non-null. Equivalence, stated so the plan can pin it: the cases where the old
gate passed but `Placement` is null are exactly `/All` (old path: Spot arm → registry miss →
alternate miss → `default` → refuse) and unplaceable-Process (old path: `default` → refuse) — both
already ended in the same whole-op fallback, one layer deeper.

> **Corrected 2026-07-28 by the whole-branch review, superseding the sentence above:** the premise
> is false. `Decide`'s `/All` arm fires only when `Names.Count == 1`; `RoleFor("All")` resolves to
> `Spot`; and `ColorSpaceResolver.OwnAlternateFor` has no `/All` special case. So a mixed-NChannel
> `/All` component carrying a tint plus an `/Attributes`/`/Colorants` entry did **not** fall through
> registry miss → alternate miss → default → refuse — it took the own-alternate **revert** arm,
> before this migration. Registry-plane divergence for `/All` is unreachable in practice
> (`PageColorantReader` filters `ColorantKind.All` out of the inventory), so the own-alternate
> revert was the *sole* live vector for such a component. After the migration, `Build`'s table is
> null for this shape, so the whole op now takes the routed/flatten arm instead of reverting.
>
> This is a fourth accepted divergence, **R4**: a mixed NChannel `/All` component with a tinted
> own-alternate `/Colorants` entry reverted before the migration and refuses-to-flatten after it.
> Accepted because it is corpus-unreachable (no fixture in the corpus reaches the own-alternate
> revert arm through `/All`), the post-migration behaviour is the more correct reading of "every
> colorant on the device at once" (an `/All` component is not a single ordinary spot and should not
> silently revert to one colorant's own alternate), and restoring the old behaviour would mean
> re-adding an `/All` special case to `Build` — exactly the kind of case this migration exists to
> delete.

**No engine pack, no repin.** `ColorantPlacement` is in the pinned `2.5.1-dev20260728182856`;
`ProcessContribution` already consumes it in the same file.

## 5. Testing and verification

1. **Positional per-plate assertions only** (parent §5.2, binding on every assertion).
2. **Equivalence pins per site**: a fixture where name-split and placement **differ** (a
   non-reserved process name, `PrCyan`-style) proving the placement path is taken; a fixture where
   `Placement` is null proving the name-split fallback still fires.
3. **R1–R3 each get their own pin**, one test per table row — R3 pinned from **both** sides (site 1
   refuses, site 2 succeeds).
4. **Spot-order pin** at site 1: two spots astride a process colorant, asserted per-plane
   positionally.
5. **Mutation discipline** (parent §5.4): every prescribed mutation names the assertion that catches
   it; the `slot.Index` → `i` transposition mutation is prescribed at both sites.
6. **Gates as guards, not proof** (parent §5.1): GWG 51/51, NChannel 3/3, 0 diffs, embedded-SHA
   verification on any engine rebuild; suites ≥ 2679 (engine, net8/9/10, 0 warnings) and ≥ 1311
   (Pellucid) green.
7. Site 1 and site 2 land as **separate commits on one branch**, each independently green — either
   alone is behaviour-preserving; there is no sites-3/4-style cross-boundary coupling.

## 6. Error handling

Nothing new throws. `Placement` null routes to the same fallbacks the sites use today. The
construction-time validation landed 2026-07-28 (`ColorantSlot`/`ColorantPlacement` validating
ctors, engine `8ddc69c`) bounds every slot index before either site can read one; neither site adds
bounds checks (that is the point of the validating ctor). Site 1's existing call-site catch in
`ComponentSplit` is retained unchanged.

## 7. Delivery

One plan, two tasks (site 1 engine, site 2 Pellucid), independently committable, subagent-driven
with per-task review. After both land: the conformance doc's "migration of Pass 2b's two shipped
sites" open item closes with a pointer here; the duplication record in parent §4.4 is marked
resolved-by-migration rather than resolved-by-recording.
