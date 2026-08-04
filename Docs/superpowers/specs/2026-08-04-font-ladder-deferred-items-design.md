# Font resolution ladder — deferred items from the slice-1 whole-branch review

Date: 2026-08-04
Status: design approved, plan not yet written
Predecessor: `2026-08-04-font-substitution-metadata-design.md` (slice 1, landed as engine `27aa4f7`)

## Why this exists

The slice-1 whole-branch review raised nine findings. Five were fixed before the merge. Three were
deliberately deferred here because they either change render output on gates that had just been
re-pinned, or were cleanups not worth widening the pre-merge diff for. This spec covers those three.

They are independent. Each can land, or be dropped, without touching the others.

## Item 1 — step 1 of the ladder is style-blind

### The defect

`SystemFontLocator.Resolve` step 1 accepts an exact PostScript-name hit unconditionally:

```csharp
FontFaceRecord? hit = _index.ByPostScriptName(stripped);
if (hit is null) hit = FirstFamilyHit(...);   // step 2, style-aware — never reached on a step-1 hit
```

A PDF whose `/BaseFont` is `/ArialMT` and whose `/FontDescriptor` sets the Italic flag hits the
upright `ArialMT` face and never reaches the style-aware step 2. The returned face is a file whose
name looked right — the exact failure class this ladder was built to eliminate.

This is not a regression. Master was equally blind. It is deferred work because the ladder now has
the metadata to do better and discards it.

### Why the review's proposed fix does not work

The review proposed falling through to `FirstFamilyHit(Base35Aliases.FamiliesFor(family), …)` when
the exact hit's style disagrees. Traced against the code, that is a no-op on the motivating case:

- `Base35Aliases.Split("ArialMT")` finds no `-` or `,` separator, so `Family` is `"ArialMT"`.
- `FamiliesFor("ArialMT")` does not match the base-35 table and aliases the name to itself.
- `FontMetadataIndex._byFamily` is keyed on **name-table family names** (ID 1 / ID 16). Arial's
  family record is `"Arial"`. `"ArialMT"` is the PostScript name and is not a key.

So `ByFamily("ArialMT")` misses, `FirstFamilyHit` returns null, and we fall back to the same upright
face. The fix has to start from the hit record's own `Families`, not from a re-split of the request
string.

### Design

**Two style pairs, not one.** `SubstituteFontResolver.Classify` folds `descriptor.StemV >= 120` into
`bold`. That is an inference about a number, not a statement of intent, and it must not gain the
power to reject a face the document named outright. So the request carries a second, narrower pair:

- **Merged style** (`Bold` / `Italic`, today's fields, unchanged): descriptor flags, plus `StemV`
  inference, plus name tokens. Steps 2 and 3 keep using this, unchanged — those steps are already
  guessing, and the inference earns its place there.
- **Explicit style** (`ExplicitBold` / `ExplicitItalic`, new): descriptor `IsBold` / `IsItalic`
  flags, plus explicit style tokens in the name (`-Bold`, `,Italic`, `-Oblique`). Never `StemV`.
  Only step 1 consults this pair.

Both pairs are computed in `SubstituteFontResolver.Load` and carried on `FontRequest`. As in the
slice-1 fix, the name-derived half is re-merged inside `SystemFontLocator` so a request whose
descriptor says nothing but whose name says `-Italic` still counts as explicit.

**Step 1 becomes:**

1. `hit = ByPostScriptName(stripped)`. If null, proceed to step 2 as today.
2. If the explicit pair is empty — no descriptor flag, no name token — return `hit`. This is the
   overwhelmingly common path and is byte-identical to today.
3. If `hit` already agrees with the explicit pair, return `hit`.
4. Otherwise gather the faces indexed under `hit.Families` and run `PickBest` over them using the
   explicit pair. Return the sibling **only if it scores strictly better than `hit` does**;
   otherwise return `hit`.

Step 1 therefore never returns null where it returns non-null today, and never returns a *worse*
face than today. The document's named typeface is preserved in every case: we look only inside the
hit's own family, never at the base-35 alias table. Fidelity argument for that boundary — the
document named this typeface, so an upright Arial is a better answer than some other typeface's
italic. A request that genuinely wants the alias ladder does not produce an exact PostScript hit in
the first place and reaches step 2 on its own.

### Scoring note

`PickBest` scores `+1` for italic agreement and `+1` for bold agreement. "Strictly better" means a
strictly higher score than `hit` scores under the same explicit pair. A sibling that fixes italic
while breaking bold ties at 1–1 and is therefore rejected, keeping the named face. That is
deliberate: a tie is not evidence, and the named face is the incumbent.

## Item 2 — `PickBest`'s tie-break is not deterministic across machines

`FontMetadataIndex.PickBest` breaks ties on `f.FaceIndex >= best.FaceIndex`, documented as "ties keep
the LOWEST face index." Within one file that is meaningful. Via `FirstFamilyHit` the candidates come
from *different files* and every one has `FaceIndex == 0`, so the comparison is always true and the
effective rule is "first indexed wins" — i.e. `Directory.EnumerateFiles` order, which is not
guaranteed stable across machines or filesystems.

Reachable whenever a family has no exact style match: request Regular from a family holding only Bold
and Italic and both candidates score 1.

`FontFaceRecord`'s own documentation already names the intended remedy — `EnglishFamily` exists "only
for canonicalisation and deterministic tie-breaking." So ties break on `(EnglishFamily,
PostScriptName)` ordinal comparison, with `FaceIndex` retained as the within-file tie-break it was
actually written for. Ordinal, not culture-aware: the comparison must not vary with the host locale.

The doc comment is corrected to describe the real rule.

## Item 3 — the two `ReadFace` implementations are duplicated verbatim

`SfntNameReader` carries a `byte[]` and a `Stream` implementation of both `FaceCount` and `ReadFace`,
roughly 70 duplicated lines. The platform-0 fix landed in slice 1 had to be applied twice, in exactly
the pattern that produces drift.

The `byte[]` overloads' only production caller is `FontMetadataIndex.PickFaceIndex`, which operates
on an already-in-memory array from a third-party provider. Collapse them to
`ReadFace(new MemoryStream(data), faceIndex, path)` and `FaceCount(new MemoryStream(data))` and
delete the duplicated bodies, keeping the `byte[]` signatures so callers are untouched.

The global no-whole-file-read constraint is unaffected: nothing new is read, an existing array is
wrapped. The cross-implementation tests stay as cheap wrapper guards.

This item must be **byte-identical** in behaviour. If it moves a render hash, that is a bug in the
collapse, not a baseline to re-pin.

## Testing

TDD throughout — failing test first, watched failing, then the implementation.

**Item 1.** The existing ladder tests call `new SystemFontLocator(SystemFontLocator.DefaultFontDirectories())`
and so depend on whatever the box happens to have installed — which is precisely why the slice-1
ladder tests never caught this defect (`Times-Italic` has no exact PostScript match on the CI boxes,
so those tests always fell through to step 2 and never exercised step 1's short-circuit).

These tests therefore use **synthesised fixtures, not the live system**: two minimal sfnt files
written to a temp directory — same name-table family, distinct PostScript names, one upright and one
italic — with the locator constructed as `new SystemFontLocator([tempDir])`. The `Sfnt()` builder in
`SfntNameReaderTests` already produces valid name and head tables; it moves to a shared test helper.
The files need a `.ttf` extension to pass `FontMetadataIndex.Extensions`, and assertions are made on
`FontMatch.Data` against the fixture bytes, so no parseable glyph program is required.

This makes the cases below deterministic and identical on all three boxes. Required cases:

- Exact hit whose style agrees with an explicit italic request → returned unchanged.
- Exact hit that disagrees, whose family holds a correctly-styled sibling → the sibling is returned.
- Exact hit that disagrees, whose family holds no better face → the exact hit is kept, not null.
- A `StemV >= 120` descriptor with no explicit bold flag and no name token → exact hit kept.
  This is the regression guard for the whole two-pair design; if it ever fails, the explicit pair
  has been contaminated by the inference.
- Explicit style drawn from the name alone (`Arial-Italic`, no descriptor) still counts as explicit.

**Item 2.** A family whose candidates all tie, indexed twice in opposite insertion orders, returns
the same face both times.

**Item 3.** Existing cross-implementation tests must pass unchanged. No new behavioural tests — the
claim is that behaviour does not change.

## Render gate risk

| Item | Can move a render hash? |
|---|---|
| 1 | Yes — only for a document producing an exact PostScript hit whose explicit style disagrees AND whose family has a better face installed. Expected to be narrow; the plan measures it rather than predicting it. |
| 2 | Yes — only where a tie exists today and ordinal order differs from enumeration order. |
| 3 | **No.** Any movement is a defect. |

Gates are Windows, Linux (llmbox) and macOS (macmini), covering the GWG corpus and the Ghent
scoreboard. Per the `render-verify` skill, any fixture that moves has its crop **viewed** against the
page's embedded reference before a baseline is re-pinned — hash agreement between two boxes is not
evidence of correctness, only of agreement.

Because Pellucid's `ci/dependencies.json` pins the engine by full SHA, an engine merge, a baseline
re-pin, and that pin bump are one atomic unit and must land in the same push. Slice 1 nearly shipped
broken on exactly this.

## Out of scope

Slice 2 (bundled Liberation floor under OFL 1.1, and the symbolic guard keeping Symbol and
ZapfDingbats away from it) remains scoped in the slice-1 spec and is not touched here. The Type1C
parse failure — GWG090 embeds a font program the engine cannot parse, so a document with a valid
embedded font silently renders substituted — is a separate investigation and is not addressed by any
item above.
