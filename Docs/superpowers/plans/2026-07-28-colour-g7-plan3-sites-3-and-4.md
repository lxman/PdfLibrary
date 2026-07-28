# Colour G-7 Plan 3 — sites 3 and 4, landed together

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the shading/mesh spot split place colorants by `/Process` position (site 3, engine) and
make the compositor's process-plate mask do the same (site 4, Pellucid) — **in one branch pair, with
the pack-and-repin sequenced between them, because either alone is broken.**

**Architecture:** Both sites are the same literal reserved-name `switch`, and both now have
`ColorantOrigin.Placement` available. Site 3 changes which *ink* is computed; site 4 changes which
*plates* are painted. Landing site 3 alone flips an op from the flatten arm to the routed arm, where a
still-name-based mask returns `(F,F,F,F)` and the process split is never composited — **measured ink
loss.** Site 4 is also the "process-only, preserve plates" signal the design named as the precondition
for closing this.

**Tech Stack:** C# / .NET. Engine `PdfLibrary` multi-targets net8.0/net9.0/net10.0, `PdfLibrary.Tests`
net10.0 only. Compositor `Pellucid.Rendering.Cmyk`, consuming the engine as a NuGet package. xUnit.

**Design:** `Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md` — §4.1 (site 3),
§4.2 (site 4), §3 (why site 4 *is* the preserve signal), §6.2 delivery step 2.

**Predecessors, whose ledgers hold measurements this plan must not re-derive:**
- `PDF/.superpowers/sdd/2026-07-27-colour-g7-plan1-carrier-placement/` — the carrier, and the Task 0
  that measured the ink loss.
- `PDF/.superpowers/sdd/2026-07-28-colour-g7-plan2-all-process-shading/` — site 5, and the corpus census.

## Global Constraints

- **BASE** = PDF `master` @ `dc33810`; Pellucid `main` @ `ee72ae8`. Branches:
  `colour/g7-sites-3-and-4` in **both** repos.
- Entering baselines: engine **2667 passing / 0 failing**, build **0 warnings** on net8/9/10;
  Pellucid **1304 / 0 / 78**. Engine pin currently `2.5.1-dev20260728114409` (from master `4eafde5`).
- **`.superpowers/` is gitignored in BOTH repos.** The ledger lives on disk.
- **NEVER `git add -A` in the Pellucid repo** — the untracked `website/` is pre-existing and not ours.
- **Every assertion is a positional per-plate assertion, or it is decorative.** The defect is a
  permutation; the same values reordered have identical sum, max, multiset and total ink.
- **Every prescribed mutation names which assertion in which fixture changes value**, and must be
  observed red **by assertion**. A compile error is not equivalent.
- **Site 3's evidence is SYNTHETIC and must be labelled so.** Plan 1's Task 0 measured that **zero**
  corpus NChannel shadings differ between the name split and placement, and Plan 2's Task 0 measured
  **no NChannel mesh exists anywhere** in 3005 files. No render-hash gate can observe site 3. The
  gates run as a guard against unintended movement.
- **Consumers branch on `slot.Kind`, never on `slot == ColorantSlot.Nothing`** (design §2.2). The
  positional record struct's public constructor permits an inconsistent `Nothing`.
- `pack-local.ps1` **deletes the Skia pin on every run — ten times on record.** Re-add
  `<LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>`
  by hand after every pack, and verify it is present before trusting any gate result.
- A trx is now captured automatically for every `*.Tests` project in **both** repos (`dc33810` added
  it to the engine). **If a run goes red, keep its trx** — that is the whole reason it exists.

---

## THE SEQUENCING RULE — read before Task 1

Site 3 and site 4 live in different repos with a NuGet boundary between them. They cannot land in one
commit, but **neither may reach `master`/`main` without the other.** The order is:

1. Site 3 on an engine **branch** (Task 1). Not merged.
2. Pack a dev build **from that branch** (Task 2). Pellucid pins to it.
3. Site 4 on a Pellucid **branch** (Task 3), against that pinned engine.
4. Gates and suites with both in place (Task 4).
5. Docs (Task 5).
6. Merge **engine first, then Pellucid** — and repin Pellucid to a build from the merged engine
   master before merging Pellucid, so the compositor never points at a branch build.

**Do not merge the engine branch after Task 1 "because it passes on its own".** It does pass on its
own — the engine suite cannot see the ink loss, which happens three files away in a compositor branch
keyed on `is not null`. That is precisely how Pass 2b-engine's I-1 shipped.

---

## File Structure

| File | Repo | Responsibility |
|------|------|----------------|
| `PdfLibrary/Rendering/ShadingSpotSplit.cs` | PDF | **modify.** Add `SplitByPlacement` beside the name-driven `Split`. |
| `PdfLibrary/Rendering/ShadingBuilder.cs` | PDF | **modify** (~`:73-97`). Prefer placement for spot names and for the per-stop split. |
| `PdfLibrary/Rendering/MeshShadingReader.cs` | PDF | **modify** (~`:58-68`). Same, per-vertex, including `hasProcess`. |
| `PdfLibrary.Tests/Rendering/ShadingSpotSplitTests.cs` | PDF | **modify.** Placement-driven split cases. |
| `Pellucid.Rendering.Cmyk/InkDecider.cs` | Pellucid | **modify** (`ProcessContribution`, ~`:446-468`). Derive the plate mask and tints from placement. |
| `Pellucid.Rendering.Cmyk.Tests/...` | Pellucid | **modify/new.** Mask cases, including the empty-`Tints` shading shape. |

---

## Task 0: Measurement — no commits

**This task re-establishes the ink-loss counterfactual at current HEAD and answers the questions that
decide Task 3's shape.** No production code, no commits, both trees clean at the end.

**Interfaces:**
- Consumes: `ColorantOrigin.Placement` and the `ColorantSlot` API, shipped in `79577ae`.
- Produces: M1-M5 and a SCOPE VERDICT in
  `PDF/.superpowers/sdd/2026-07-28-colour-g7-plan3-sites-3-and-4/progress.md`.

- [ ] **Step 1: Verify the entering baselines**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git log --oneline -1                 # expect dc33810
git status --porcelain               # expect empty
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug 2>&1 | tail -3
dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --no-incremental 2>&1 | grep -E "Warning\(s\)|error"
```

Expected: `Failed: 0, Passed: 2667`, `0 Warning(s)`. **If either differs, STOP.**

- [ ] **Step 2: M1 — re-measure the ink loss at current HEAD**

Plan 1's Task 0 measured this before site 5 landed. Site 5 has since changed `BuildCmykMapper`, so
**re-measure rather than cite.**

Build a **mixed** NChannel shading: `[PrCyan (Process, channel 0), Spot1 (a registered spot)]`.
Record, for the shading as it renders **today**:

- a. `ShadingSpotSplit.SpotNames(origin.Names)` — the name-derived list. Expected `[PrCyan, Spot1]`.
- b. `origin.Placement.SpotNames` — the placement-derived list. Expected `[Spot1]`.
- c. `routeShadingSpots` at `CmykPageRenderer.cs:613-623` today. Expected **False** (PrCyan has no
  plane), so the op **flattens**.
- d. The per-plate values it paints today, at a named pixel.
- e. **The counterfactual:** with the placement-derived list, `routeShadingSpots` would be **True**,
  `Decide` takes the routed arm, `ProcessContribution` returns its mask — record that mask, and
  record `anyProcess` at `CmykPageRenderer.cs:697`.

**This is the measurement the whole plan rests on.** If (e) no longer shows ink loss, say so loudly —
the plan's pairing rationale would then be stale and the tasks may be separable after all.

- [ ] **Step 3: M2 — what `ProcessContribution` does for a shading today**

`ProcessContribution` is reached from `Decide`'s routed arm only when `TryPerComponent` **declines**
and at least one name is registered. Record:

- a. For a shading origin (`Tints` **empty**), does `TryPerComponent` decline? Why — trace the
  condition, do not guess.
- b. `ProcessContribution`'s returned mask and tints for a mixed NChannel shading origin.
- c. Whether `origin.Placement` is non-null on that origin at the compositor (it crosses the package
  boundary — confirm it survives, as Plan 1's M3 did for `Components`).

- [ ] **Step 4: M3 — D4 re-verified per consumer**

Plan 1 carried this forward: `ProcessChannelCount == 4` can mean *"we never found out"* when the
`/Process` dereference throws. It was ruled safe **only while nothing consumed `Placement`**. Site 3
and site 4 are consumers.

For **each** of the two new consumers, answer: on the throw path (where `processChannels` is null and
only reserved names classify as Process), does the placement-driven answer differ from the name-driven
one? Plan 1's final review established the general argument rests on `PageColorant.Classify`'s Process
set being exactly the four reserved names — **cite that and check it still holds**, per consumer.

- [ ] **Step 5: M4 — corpus census for the shapes this plan changes**

Enumerate every **mixed** NChannel shading and mesh (placement non-null, `SpotNames` non-empty) across
the corpus. Walk page `/Resources`, Form-XObject and tiling-pattern `/Resources` — **and state
explicitly whether annotation appearance streams and soft-mask groups were walked**, because Plan 2's
final review flagged that omission as making the count a lower bound rather than an absolute.

Prediction: zero shadings differ, no mesh anywhere. Record the actual numbers.

- [ ] **Step 6: M5 — the `hasProcess` question in the mesh reader**

`MeshShadingReader.cs:66-67` computes `hasProcess` via `PageColorant.Classify` over names. If site 3
changes the spot-name derivation but not this, the two can disagree about whether a space marks any
plate. Record what `hasProcess` is used for downstream and what it would become under placement.

- [ ] **Step 7: SCOPE VERDICT and cleanup**

Numbers, not "as predicted". Then: does Task 1 proceed? Every plan defect found. Delete all scratch;
verify PDF clean and Pellucid showing only `?? website/`. **No commits.**

---

## Task 1: Site 3 — the shading and mesh split consume placement (ENGINE)

**Files:**
- Modify: `PdfLibrary/Rendering/ShadingSpotSplit.cs`, `ShadingBuilder.cs`, `MeshShadingReader.cs`
- Test: `PdfLibrary.Tests/Rendering/ShadingSpotSplitTests.cs`

**Interfaces:**
- Consumes: `ColorantPlacement`, `ColorantSlot`, `ColorantSlotKind`, `ColorantOrigin.Placement`.
- Produces: `ShadingSpotSplit.SplitByPlacement(double[] comps, ColorantPlacement placement, byte[] spotDest, int destOffset)` returning packed process CMYK as `uint` (`0xCCMMYYKK`), matching the existing `Split`.

> **This task's text is adapted from Plan 1's deferred Task 2, with four corrections already folded in
> (GWG081's real space; synthetic-evidence labelling; no NChannel mesh anywhere; the omitted outer
> `switch (PageColorant.Classify(...))`). Do not re-derive it from the design.**

- [ ] **Step 1: Write the failing tests**

Append to `PdfLibrary.Tests/Rendering/ShadingSpotSplitTests.cs`:

```csharp
    // --- placement-driven split (design §4.1) ---

    private static ColourantComponent Proc(string name, int channel) =>
        new(name, ColourantRole.Process, null, null, channel);

    private static ColourantComponent Sp(string name) =>
        new(name, ColourantRole.Spot, null, null, null);

    [Fact]
    public void SplitByPlacement_ListedProcessNames_LandOnTheirListedPlates()
    {
        // THE defect in one assertion. Names order (PrCyan, PrMagenta, PrYellow, Black) carrying
        // components (0.0, 0.36, 0.57, 0.02). The name split puts NONE of the first three on a plate.
        ColorantPlacement p = ColorantPlacement.Build(
            [Proc("PrCyan", 0), Proc("PrMagenta", 1), Proc("PrYellow", 2), Proc("Black", 3)], 4)!;

        uint proc = ShadingSpotSplit.SplitByPlacement([0.0, 0.36, 0.57, 0.02], p, [], 0);

        // PER PLATE. The same four values in any other order share sum, max and multiset.
        Assert.Equal(0u, (proc >> 24) & 0xFF);     // C
        Assert.Equal(92u, (proc >> 16) & 0xFF);    // M = round(0.36*255)
        Assert.Equal(145u, (proc >> 8) & 0xFF);    // Y = round(0.57*255)
        Assert.Equal(5u, proc & 0xFF);             // K = round(0.02*255)
    }

    [Fact]
    public void SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset()
    {
        // Also the mutation target for "index vs channel": Cyan sits at INDEX 1 but CHANNEL 0.
        ColorantPlacement p = ColorantPlacement.Build(
            [Sp("GWG Green"), Proc("Cyan", 0), Sp("PANTONE 032 C")], 4)!;
        var spot = new byte[6];   // 3 stops * 2 spots

        uint proc = ShadingSpotSplit.SplitByPlacement([0.5, 1.0, 0.2], p, spot, destOffset: 2);

        Assert.Equal(128, spot[2]);                // slot 0 at offset 2
        Assert.Equal(51, spot[3]);                 // slot 1 at offset 2
        Assert.Equal(0xFF000000u, proc);           // Cyan 1.0 on the C plate, not the M plate
        Assert.Equal(0, spot[0]);                  // stop 0 untouched
    }

    [Fact]
    public void SplitByPlacement_NoneContributesNothing_ToAnyPlateOrSpot()
    {
        ColorantPlacement p = ColorantPlacement.Build(
            [Proc("Cyan", 0), new("None", ColourantRole.None, null, null, null)], 4)!;
        var spot = new byte[1];

        uint proc = ShadingSpotSplit.SplitByPlacement([0.25, 1.0], p, spot, 0);

        Assert.Equal(64u, (proc >> 24) & 0xFF);
        Assert.Equal(0u, proc & 0x00FFFFFFu);
        Assert.Equal(0, spot[0]);                  // /None's 1.0 went nowhere
    }
```

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug --filter "FullyQualifiedName~ShadingSpotSplitTests"
```

Expected: build failure, `CS0117` — no `SplitByPlacement`.

- [ ] **Step 3: Add the placement-driven split**

In `PdfLibrary/Rendering/ShadingSpotSplit.cs`:

```csharp
    /// <summary>
    /// Splits <paramref name="comps"/> by <paramref name="placement"/> rather than by colorant name:
    /// each component goes to the plate or spot slot its NChannel <c>/Process /Components</c> POSITION
    /// gives it (ISO 32000-2 Table 71). Returns the packed process CMYK (<c>0xCCMMYYKK</c>) and writes
    /// spot tints to <paramref name="spotDest"/> at <paramref name="destOffset"/> + slot index.
    ///
    /// <para>The name-driven <see cref="Split"/> remains for every space with no placement — a plain
    /// DeviceN, a Separation, an NChannel over a one-channel process space, or one carrying an
    /// <c>/All</c> or an unplaceable component.</para>
    ///
    /// <para>No tint transform is used, exactly as in <see cref="Split"/>: a spot's alternate is
    /// applied once at display via the registry ramp, and a process component needs no alternate at
    /// all because it has a unit.</para>
    /// </summary>
    public static uint SplitByPlacement(
        double[] comps, ColorantPlacement placement, byte[] spotDest, int destOffset)
    {
        var plates = new double[4];
        IReadOnlyList<ColorantSlot> slots = placement.Slots;

        for (var j = 0; j < slots.Count; j++)
        {
            double v = j < comps.Length ? comps[j] : 0.0;
            ColorantSlot slot = slots[j];
            switch (slot.Kind)
            {
                case ColorantSlotKind.Plate:
                    plates[slot.Index] = v;
                    break;
                case ColorantSlotKind.Spot:
                    spotDest[destOffset + slot.Index] = B(v);
                    break;
                // Nothing: /None is never painted.
            }
        }

        return ((uint)B(plates[0]) << 24) | ((uint)B(plates[1]) << 16)
             | ((uint)B(plates[2]) << 8) | B(plates[3]);
    }
```

- [ ] **Step 4: Run to verify they pass**

Expected: PASS, existing tests plus the three new ones.

- [ ] **Step 5: Wire the axial/radial builder**

In `ShadingBuilder.cs`, replace the spot-name derivation (~`:74-75`):

```csharp
        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(shadingCs, null, document);
        // Placement first: a listed process name such as /PrCyan is NOT a spot, and the name-derived
        // split would put its ink on a spot plane while the cyan unit sat dry. Falls back whole when
        // the space has no placement (plain DeviceN, one-channel process space, /All, unplaceable).
        ColorantPlacement? placement = origin?.Placement;
        List<string> spotNames = placement is not null
            ? [.. placement.SpotNames]
            : origin is not null ? ShadingSpotSplit.SpotNames(origin.Names) : [];
```

and the per-stop split (~`:95`):

```csharp
            if (splitSpots)
                stopProcess[i] = placement is not null
                    ? ShadingSpotSplit.SplitByPlacement(components, placement, stopTints, i * spotN)
                    : ShadingSpotSplit.Split(components, origin!.Names, stopTints, i * spotN);
```

- [ ] **Step 6: Wire the mesh reader**

Same spot-name derivation at `MeshShadingReader.cs:61-62`, and `hasProcess` (~`:66-67`) must follow
the same rule so the two cannot disagree:

```csharp
        bool hasProcess = placement is not null
            ? placement.Slots.Any(s => s.Kind == ColorantSlotKind.Plate)
            : origin is not null && origin.Names.Any(n => PageColorant.Classify(n) == ColorantKind.Process);
```

Then find every `ShadingSpotSplit.Split(` call in the file and give it the same treatment. **Grep
rather than trusting this plan to have listed them all:**

```bash
grep -n "ShadingSpotSplit.Split(" PdfLibrary/Rendering/MeshShadingReader.cs
```

- [ ] **Step 7: Full engine suite and multi-TFM build**

Expected: 0 failed, 2667 + 3; `0 Warning(s)`. **If any pre-existing test changed its result, STOP and
report which** — M4 predicted the corpus does not move.

- [ ] **Step 8: Prescribed mutations**

| # | Mutation | Must go red, by ASSERTION |
|---|----------|---------------------------|
| A | `plates[slot.Index] = v` → `plates[j] = v` | `SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset` — `Cyan` is at index 1, channel 0, so `proc` reads `0x00FF0000` (M) instead of `0xFF000000` (C) |
| B | Drop the `Spot` arm | `SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset` — `spot[2]` is 0, not 128 |
| C | Route `Nothing` to `plates[0]` | `SplitByPlacement_NoneContributesNothing_...` — C reads 255 |
| D | In `ShadingBuilder`, force `placement` to `null` | **Predicted GREEN.** See below |
| E | In `MeshShadingReader`, force `placement` to `null` | **Predicted GREEN.** See below |

**Mutation A must be checked against the named fixture specifically.** In the four-component fixture
the slots are `[0,1,2,3]`, so index equals channel and `plates[j]` is byte-identical — that fixture
**cannot** observe A. Only `SpotsWriteAtTheirSlotPlusOffset`, where `Cyan` sits at index 1 with
channel 0, can. This trap has fired seven times in this programme.

**D and E are predicted to leave the suite green, and that prediction is the point.** The unit tests
cover `SplitByPlacement` directly; nothing yet asserts the *builders* call it — exactly Pass
2b-engine's I-2 shape, where `StencilInkFromFill`'s branch was entirely unpinned.

**If D or E leaves the suite green, this task is not complete.** Add a builder-level test that
constructs an NChannel shading (and mesh) whose placement and name split disagree, and asserts the
resulting `ShadingSpotInk.Names` / `MeshSpotInk.Names` **positionally**. Re-run D and E against it and
record them red by assertion. Do not argue that the unit tests "cover the logic".

- [ ] **Step 9: Commit — and DO NOT MERGE**

```bash
git add PdfLibrary/Rendering/ShadingSpotSplit.cs PdfLibrary/Rendering/ShadingBuilder.cs \
        PdfLibrary/Rendering/MeshShadingReader.cs \
        PdfLibrary.Tests/Rendering/ShadingSpotSplitTests.cs
git commit -m "fix(colour): split a shading's colorants by placement, not by name

ShadingSpotSplit.Split switched on the literal names Cyan/Magenta/Yellow/Black
-- the third of five sites of the same defect. Under an NChannel space naming
/PrCyan the switch never matched, so the cyan ink went to a SPOT PLANE while
the cyan unit sat dry.

Both builders now prefer ColorantOrigin.Placement and fall back whole when
there is none. The mesh reader's hasProcess follows the same rule so the two
cannot disagree about whether a space marks any plate.

INCOMPLETE ON ITS OWN. This flips a mixed NChannel op from the flatten arm to
the routed arm, where InkDecider.ProcessContribution's mask is still
name-based and returns (F,F,F,F) -- so the process split is never composited
and the ink is dropped. Site 4 lands in Pellucid before either is merged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Pack the branch build and repin — the sequencing point

**No commits.** This exists so Task 3 develops against an engine that actually has site 3.

- [ ] **Step 1: Pack from the engine BRANCH**

Run `pack-local.ps1`. Record `NEWVERSION`. **Re-add the Skia pin by hand** (ten times on record) and
verify it is present. Repin `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj`.

- [ ] **Step 2: Confirm Pellucid builds and record the pre-site-4 state**

```bash
cd /c/Users/jorda/RiderProjects/Pellucid
dotnet build 2>&1 | grep -E "Warning\(s\)|error"
dotnet test 2>&1 | grep -E "Passed!|Failed!"
```

**Record the totals.** This is the state with site 3 but not site 4 — the broken intermediate. If a
Pellucid test goes red here, that is informative: it means the ink loss is observable from the
compositor suite, which Plan 1's Task 0 did not expect. Record it either way; do not fix it here.

---

## Task 3: Site 4 — the compositor mask consumes placement (PELLUCID)

**Files:**
- Modify: `Pellucid.Rendering.Cmyk/InkDecider.cs` (`ProcessContribution`, ~`:446-468`)
- Test: the `Pellucid.Rendering.Cmyk` test project — extend the existing ink-decision tests

**Interfaces:**
- Consumes: `ColorantOrigin.Placement` (crosses the package boundary; Task 0's M2c confirms it survives).
- Produces: no new public surface. `ProcessContribution`'s signature is unchanged.

- [ ] **Step 1: Write the failing tests**

The shape that matters is the one with **empty `Tints`** — a shading — because the existing
precondition (*"`Tints` is allowed to be SHORTER than `Names`… such a caller wants only the boolean
mask"*) is what makes this reachable at all.

Write, at minimum:

- `ProcessContribution_ListedProcessNames_MarkTheirListedPlates` — origin with
  `Placement.Slots = [Plate(0), Plate(1), Plate(2), Plate(3)]` over names `[PrCyan, PrMagenta,
  PrYellow, Black]`, `overprint: true`. Assert **per plate** that all four boolean flags are true.
  Today they are all **false**.
- `ProcessContribution_MixedSpace_MarksOnlyThePlatesItNames` — `[PrCyan(Plate 0), Spot1(Spot 0)]`,
  `overprint: true`. Assert `pc` true and `pm`/`py`/`pk` false — *preserve* the plates it does not
  image. **This is the preserve signal** (design §3).
- `ProcessContribution_WithEmptyTints_StillDerivesTheMask` — the shading shape. Assert the mask is
  correct with `Tints` empty and every tint therefore 0.
- `ProcessContribution_NoPlacement_FallsBackToReservedNames` — a plain DeviceN. Assert today's
  behaviour is unchanged.
- `ProcessContribution_NotOverprinting_PaintsAllFour` — the `if (!overprint) pc = pm = py = pk = true;`
  line must survive. Assert it.

- [ ] **Step 2: Run to verify the right ones fail**

Expected: the first three fail **by assertion**; the last two pass (they assert current behaviour).
**If either of the last two fails, that is a finding** — report it rather than adjusting the fixture.

- [ ] **Step 3: Derive the mask from placement**

In `ProcessContribution`, prefer placement and fall back whole:

```csharp
    private static (float, float, float, float, bool, bool, bool, bool) ProcessContribution(
        ColorantOrigin origin, bool overprint)
    {
        float c = 0f, m = 0f, y = 0f, k = 0f;
        bool pc = false, pm = false, py = false, pk = false;

        if (origin.Placement is { } placement)
        {
            // ISO 32000-2 Table 71: /Process /Components makes POSITION the channel identity, which a
            // name cannot carry. This is also the "process-only, preserve plates" signal — the op
            // marks exactly the plates its space names and leaves the rest to the backdrop.
            Span<float> plates = stackalloc float[4];
            Span<bool> marked = stackalloc bool[4];
            IReadOnlyList<ColorantSlot> slots = placement.Slots;

            for (var i = 0; i < slots.Count; i++)
            {
                ColorantSlot slot = slots[i];
                if (slot.Kind != ColorantSlotKind.Plate) continue;   // Spot rides its plane; /None never paints
                plates[slot.Index] = i < origin.Tints.Count ? (float)origin.Tints[i] : 0f;
                marked[slot.Index] = true;
            }

            c = plates[0]; m = plates[1]; y = plates[2]; k = plates[3];
            pc = marked[0]; pm = marked[1]; py = marked[2]; pk = marked[3];
        }
        else
        {
            for (var i = 0; i < origin.Names.Count; i++)
            {
                // Tints is allowed to be SHORTER than Names (a shading/mesh resolves its ColorantOrigin
                // with rawColor: null — a gradient has no single per-op tint — so Tints comes back
                // empty). Such a caller wants only the boolean mask, so the missing tints read as 0
                // rather than throwing from inside a private helper.
                float tint = i < origin.Tints.Count ? (float)origin.Tints[i] : 0f;
                switch (origin.Names[i])
                {
                    case "Cyan":    c = tint; pc = true; break;
                    case "Magenta": m = tint; pm = true; break;
                    case "Yellow":  y = tint; py = true; break;
                    case "Black":   k = tint; pk = true; break;
                }
            }
        }

        if (!overprint) pc = pm = py = pk = true;
        return (c, m, y, k, pc, pm, py, pk);
    }
```

**Note `slot.Kind != ColorantSlotKind.Plate`, not an equality test against `ColorantSlot.Nothing`** —
design §2.2's consumer rule.

- [ ] **Step 4: Run to verify they pass, then the full Pellucid suite**

Expected: 1304 + the new tests, 0 failed.

- [ ] **Step 5: Prescribed mutations**

| # | Mutation | Must go red, by ASSERTION |
|---|----------|---------------------------|
| A | Delete the `if (origin.Placement is { } placement)` branch | `ProcessContribution_ListedProcessNames_MarkTheirListedPlates` — all four flags false |
| B | `plates[slot.Index]` → `plates[i]` | `ProcessContribution_MixedSpace_MarksOnlyThePlatesItNames` — `PrCyan` at index 0 channel 0 makes this **invisible**; **use a fixture where index ≠ channel** and say so |
| C | Drop the `slot.Kind != Plate` continue | `ProcessContribution_MixedSpace_...` — the spot's slot index collides with a plate |
| D | Delete `if (!overprint) pc = pm = py = pk = true;` | `ProcessContribution_NotOverprinting_PaintsAllFour` |

**Mutation B is a trap by construction.** If your mixed fixture uses `PrCyan` at channel 0 and index 0,
B cannot be observed. Build the fixture so at least one component's index differs from its channel, and
name that component in the report.

- [ ] **Step 6: Commit (Pellucid branch)**

Do **not** `git add -A`. Stage the specific files.

---

## Task 4: Gates and suites, both sites in place

- [ ] **Step 1: Repack from the engine branch** (site 3 unchanged since Task 2 unless a fix round
  touched it — if it did, repack and repin, re-adding the Skia pin).
- [ ] **Step 2: GWG gate.** Expect `51 fixtures hashed, 51 baselined, 0 differences`. **Check the
  embedded SHA equals the engine HEAD under test**, not just the version number.
- [ ] **Step 3: NChannel gate.** Expect `3 fixtures hashed, 3 baselined, 0 differences`, same SHA check.
- [ ] **Step 4: Pellucid suite.** Expect 1304 + Task 3's new tests, 0 failed.
- [ ] **Step 5: Engine suite.** Expect 2667 + Task 1's new tests, 0 failed, 0 warnings on all TFMs.

**If any digest moves, STOP and report which fixture. Do not update a baseline.** M4 predicted zero
movement; a moved digest means the census was incomplete or the change reaches further than measured.

---

## Task 5: Documentation

- [ ] **Step 1:** In `Docs/colour/rendering-conformance.md`, move sites 3 and 4 from open to closed in
  the G-7 entry. State that they landed **together** and why either alone was broken — the measured
  ink loss. Record that the evidence is **synthetic** with the corpus count from M4, and that the
  gates were a guard.
- [ ] **Step 2:** Rows 5-3 and 5-10. Row 5-3's exclusion should now drop shadings/meshes entirely if
  both sites landed; row 5-10's shading exclusion narrows to **reversion** only, which still has no
  per-sample own-alternate colour. **Only edit what actually changed** — say so in the report if a row
  already reads correctly.
- [ ] **Step 3:** Design §1.1's site table: mark sites 3 and 4 closed. §6.2: mark delivery step 2 done.
  Preserve superseded text rather than deleting it.
- [ ] **Step 4:** Commit docs-only.

---

## Self-review

**Spec coverage.** §4.1 → Task 1. §4.2 and §3 (the preserve signal) → Task 3. §6.2 step 2 → the whole
plan, with the sequencing rule making "together" operational rather than aspirational. §5.2 (positional
only) → Global Constraints and every assertion. §5.4 → Tasks 1 and 3 mutation tables. §2.2's consumer
rule → Task 3 Step 3's `slot.Kind` test, called out explicitly. §6.1 rule 1(b) → satisfied
synthetically and **labelled so**, because the corpus provably cannot observe site 3.

**Placeholder scan.** No TBD/TODO. Task 3's tests are specified by name, shape and assertion rather
than transcribed — deliberate, because the Pellucid test project's fixture idiom has not been read in
this plan and inventing it blind would be worse than naming the requirements precisely. Their
assertions and mutations are fully specified. Task 1's code is complete.

**Type consistency.** `SplitByPlacement(double[], ColorantPlacement, byte[], int) -> uint` matches
between Steps 1, 3, 5 and 6. `ColorantSlotKind.Plate/Spot`, `ColorantSlot.Kind/.Index`,
`ColorantPlacement.Slots/.SpotNames` match the types shipped in `79577ae`. `ProcessContribution`'s
signature is unchanged, so its single call site keeps compiling.

**Known weaknesses, stated rather than hidden.**

1. **Mutation A (Task 1) and mutation B (Task 3) both swap one index for another**, and both have a
   fixture that *cannot* observe them. Each step names the fixture that can and requires the
   difference be stated. This trap has fired seven times in this programme.
2. **Task 1 mutations D and E are predicted green** — the builders' calls are unpinned until a
   builder-level test exists. The step is written to fail loudly rather than let it ship.
3. **The mesh half has no corpus instance and never will.** Its only possible pin is synthetic.
4. **The broken intermediate is real:** between Task 1 and Task 3 the engine branch alone drops ink.
   The sequencing rule exists to keep that state off both default branches, and Task 2 Step 2
   deliberately records it rather than hiding it.
