# G-7 Plan 4: migrate the two Pass 2b sites onto ColorantPlacement

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sites 1 (`PdfImageToCmyk`) and 2 (`InkDecider.TryPerComponent`) consume
`ColorantOrigin.Placement` for slot assignment, ending with `ColorantPlacement.Build` as the only
code in either repo that turns `Role`/`ProcessChannel` into slots — with **zero behaviour change**.

**Architecture:** Adapter in place at both sites (design §2). Site 1 replaces `SplitByComponents`
with a placement-consuming `SplitByPlacement` producing the identical
`(int[] Plate, int[] SpotOf, List<string> SpotNames)` triple; site 2's loop switches on
`slots[i].Kind` with `slot.Index` replacing `c.ProcessChannel`, keeping tint/registry/own-alternate
logic and all refusals (R1–R3) in place. No pack coupling: the sites migrate independently.

**Tech Stack:** C# / .NET (PdfLibrary multi-targets net8.0/net9.0/net10.0; PdfLibrary.Tests targets
net10.0 only; Pellucid net10.0), xUnit.

**Design:** `Docs/superpowers/specs/2026-07-28-colour-g7-pass2b-placement-migration-design.md`
(`d25c0f7`). Parent: `2026-07-27-colour-g7-colorant-placement-design.md` §4.4, gated on M4
(task-0-report §6: slot mapping agrees on all 17 corpus instances; only refusal policy differs).

## Global Constraints

- **BASE:** PDF `master` @ `d25c0f7` (branch `colour/g7-pass2b-migration`); Pellucid `main` @
  `f4729f4` (branch `colour/g7-pass2b-migration`). Pellucid's pinned engine at start:
  `2.5.1-dev20260728182856`.
- **Zero behaviour change is the acceptance bar.** GWG 51/51/0 differences, NChannel 3/3/0. **If any
  digest moves, STOP and report the fixture — never update a baseline.**
- **Every assertion is a positional per-plate/per-plane assertion, or it is decorative** (parent
  design §5.2). Sum/max/multiset/contains assertions pass a transposition both ways.
- **Every prescribed mutation names which assertion in which fixture changes value** and must be
  observed red **by assertion**; a compile error does not count.
- The three refusal divergences are **preserved verbatim** (design §2): R1 site 2 refuses a Process
  component with null `Tint`; R2 site 2 refuses a Spot with neither plane nor own alternate; R3
  site 1 refuses a no-spot split (site 2 succeeds on one — the asymmetry is load-bearing).
- Consumers branch on `slot.Kind`, never on `slot == ColorantSlot.Nothing`.
- `pack-local.ps1` **deletes the Skia pin on every run — twelve times on record.** After every pack,
  re-add `<LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>`
  to `Pellucid\Directory.Build.props.local` by hand and read the file back.
- **Check the embedded engine SHA, not the version number** — the gate prints `engine=2.5.1+<sha>`;
  it must equal the engine HEAD under test.
- **NEVER `git add -A` in Pellucid** (untracked pre-existing `website/`). Stage files by name in
  both repos.
- Suites at BASE: engine 2679/0 (0 warnings, net8/9/10); Pellucid 1311/0. Pellucid.App.Tests mass
  failure in ~1 s with XamlLoadException = stale build → rebuild `--no-incremental`; a HANG is the
  Avalonia session death — dump the child `Pellucid.App.Tests.exe` with `dotnet-stack`, kill the
  tree, re-run.

---

## Task 1: Engine — site 1 (`PdfImageToCmyk`) consumes Placement

**Files:**
- Modify: `PdfLibrary/Rendering/PdfImageToCmyk.cs` (gates at `:341` and `:447`, `ComponentSplit`
  `:625-642`, `SplitByComponents` `:517-587` replaced)
- Test: `PdfLibrary.Tests/Rendering/PdfImageToCmykPlacementTests.cs` (create)

**Interfaces:**
- Consumes: `ColorantOrigin.Placement`, `ColorantPlacement` (`Slots`, `SpotNames`, `Build`),
  `ColorantSlot` (`Kind`, `Index`), `ColorantSlotKind` — all existing, engine `8ddc69c` includes the
  validating ctors.
- Produces: `private static (int[] Plate, int[] SpotOf, List<string> SpotNames)? SplitByPlacement(ColorantPlacement placement)`
  in `PdfImageToCmyk`. `SplitByComponents` is **deleted**. Public/internal surface unchanged.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests\Rendering\PdfImageToCmykPlacementTests.cs`:

```csharp
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Site 1 of the G-7 migration (design §3): PdfImageToCmyk's split consumes ColorantOrigin.Placement
/// instead of re-deriving slots from Role/ProcessChannel. Every assertion is positional — a
/// transposition has the same multiset/sum/max, so aggregate assertions are decorative (§5.2).
/// </summary>
public class PdfImageToCmykPlacementTests
{
    private static ColourantComponent Proc(string name, int channel, double? tint = null) =>
        new(name, ColourantRole.Process, tint, null, channel);

    private static ColourantComponent Sp(string name, double? tint = null) =>
        new(name, ColourantRole.Spot, tint, null, null);

    // B(0.42)=107, B(0.11)=28, B(0.99)=252 — Math.Round(v*255).

    [Fact]
    public void StencilInk_PlacementAlone_IsConsumed()
    {
        // Placement set, Components null. Before the migration the gate reads Components and falls to
        // the name split, which classifies PrCyan as a spot; after, the placement puts it on plate 0.
        var origin = new ColorantOrigin(["PrCyan", "Spot1"], [0.42, 0.11], "DeviceCMYK")
        {
            Placement = ColorantPlacement.Build([Proc("PrCyan", 0), Sp("Spot1")], 4),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "Spot1" }, ink!.Names);
        Assert.Equal(107, ink.ProcessCmyk[0]);   // PrCyan 0.42 on ITS plate, C
        Assert.Equal(0, ink.ProcessCmyk[1]);
        Assert.Equal(0, ink.ProcessCmyk[2]);
        Assert.Equal(0, ink.ProcessCmyk[3]);
        Assert.Equal(28, ink.TintPlanes[0]);     // Spot1 0.11 on plane 0
    }

    [Fact]
    public void StencilInk_Transposition_SlotIndexBeatsComponentChannel()
    {
        // Components says channel 0; the placement says Plate(1). Incoherent in production (Build is
        // the only producer) — constructed to pin WHICH source the site reads. Also the mutation
        // target for `plate[c] = slot.Index` -> `plate[c] = c`.
        var origin = new ColorantOrigin(["PrCyan", "Spot1"], [0.42, 0.11], "DeviceCMYK")
        {
            Components = [Proc("PrCyan", 0, 0.42), Sp("Spot1", 0.11)],
            ProcessChannelCount = 4,
            Placement = new ColorantPlacement([ColorantSlot.Plate(1), ColorantSlot.Spot(0)], ["Spot1"]),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(0, ink!.ProcessCmyk[0]);
        Assert.Equal(107, ink.ProcessCmyk[1]);   // slot.Index 1, NOT component channel 0
    }

    [Fact]
    public void StencilInk_NoPlacement_FallsBackToTheNameSplit()
    {
        // A plain DeviceN shape: no Components, no Placement. The name split must be byte-identical
        // to today — this is the fallback arm the 50 non-NChannel GWG patches ride.
        var origin = new ColorantOrigin(["Cyan", "Spot1"], [0.42, 0.11], "DeviceCMYK");

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "Spot1" }, ink!.Names);
        Assert.Equal(107, ink.ProcessCmyk[0]);
        Assert.Equal(28, ink.TintPlanes[0]);
    }

    [Fact]
    public void StencilInk_AllProcessPlacement_RefusesToTheNameSplit()
    {
        // R3, site 1's side: a no-spot split is REFUSED (the I-1 category-flip guard) and the whole
        // op takes the name split — which calls the non-reserved names spots. That is today's
        // recorded GAP, preserved bit-for-bit. Mutation target: dropping SplitByPlacement's
        // SpotNames.Count guard makes the placement path return a no-spot split, the caller's
        // `spotNames.Count == 0` fires, and this returns null instead.
        var origin = new ColorantOrigin(["PrCyan", "PrMagenta"], [0.42, 0.11], "DeviceCMYK")
        {
            Placement = ColorantPlacement.Build([Proc("PrCyan", 0), Proc("PrMagenta", 1)], 4),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "PrCyan", "PrMagenta" }, ink!.Names);   // the name split's answer
        Assert.Equal(28, ink.TintPlanes[1]);
        Assert.Equal(0, ink.ProcessCmyk[0]);
    }

    [Fact]
    public void StencilInk_SpotOrder_IsSlotOrder_ThroughBuild()
    {
        // The production shape: Build emits spot slots in component order. Pinned positionally
        // because a spot-order swap is silent plane corruption (the adjacent-stop lesson).
        var origin = new ColorantOrigin(["Spot1", "PrCyan", "Spot2"], [0.11, 0.42, 0.99], "DeviceCMYK")
        {
            Placement = ColorantPlacement.Build([Sp("Spot1"), Proc("PrCyan", 0), Sp("Spot2")], 4),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "Spot1", "Spot2" }, ink!.Names);
        Assert.Equal(28, ink.TintPlanes[0]);
        Assert.Equal(252, ink.TintPlanes[1]);
        Assert.Equal(107, ink.ProcessCmyk[0]);
    }

    [Fact]
    public void StencilInk_SpotPlane_IsTheSlotIndex_NotArrivalOrder()
    {
        // Hand-built placement with NON-sequential spot indexes — Build never makes one, so this is
        // the only fixture that can see a `spotOf[c] = <arrival counter>` mutation: sequential
        // re-counting assigns SpotA plane 0, but its slot says plane 1.
        var origin = new ColorantOrigin(["SpotA", "PrCyan", "SpotB"], [0.11, 0.42, 0.99], "DeviceCMYK")
        {
            Placement = new ColorantPlacement(
                [ColorantSlot.Spot(1), ColorantSlot.Plate(0), ColorantSlot.Spot(0)],
                ["SpotB", "SpotA"]),
        };

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);

        Assert.NotNull(ink);
        Assert.Equal(new[] { "SpotB", "SpotA" }, ink!.Names);
        Assert.Equal(252, ink.TintPlanes[0]);    // SpotB (0.99) at ITS slot, 0
        Assert.Equal(28, ink.TintPlanes[1]);     // SpotA (0.11) at ITS slot, 1
        Assert.Equal(107, ink.ProcessCmyk[0]);
    }
}
```

- [ ] **Step 2: Run the new tests and verify they fail — by assertion, for the right reason**

```
dotnet test PdfLibrary.Tests\PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfImageToCmykPlacementTests"
```

Expected: **4 FAIL, 2 PASS.** `PlacementAlone` fails on `Names` (actual `[PrCyan, Spot1]`);
`Transposition` fails on `ProcessCmyk[1]` (actual 0 — channel 0 wrote `[0]`); `SpotOrder_ThroughBuild`
fails on `Names` (actual 3 spots); `SpotPlane_NotArrivalOrder` fails on `Names`.
`NoPlacement_FallsBack` and `AllProcessPlacement_Refuses` PASS — they are guards proving the
fallback and R3 shapes do not move; that classification is a prediction, verify it.

- [ ] **Step 3: Implement**

In `PdfLibrary/Rendering/PdfImageToCmyk.cs`:

**(a)** Replace `SplitByComponents` (`:517-587`) with:

```csharp
    // ISO 32000-2 §8.6.6.5, for images: "the components shall be evaluated individually". The name split
    // in TryToSpotInk/StencilInkFromFill is right for a Separation or a plain DeviceN — neither carries
    // per-component roles — but for an NChannel space it misroutes two shapes the name cannot see:
    //   * a NON-RESERVED process colorant (e.g. /PrCyan listed in /Process /Components) — Classify calls
    //     it Spot, it is handed a plane the registry never holds, and the whole image drops to the
    //     whole-space flatten with its tint on neither a plate nor a plane;
    //   * a reserved name listed at a NON-CANONICAL index — Table 71 makes position the channel
    //     identity, so /Components [/Black /Cyan] puts Black on channel 0, and routing by name would
    //     transpose the colour.
    //
    // G-7 Plan 4: the slot assignment comes from ColorantPlacement — computed once, where /Process is
    // read — rather than being re-derived here from Role/ProcessChannel. The /All and
    // unplaceable-component refusals live on ColorantPlacement.Build now (they make the table null, and
    // a null table never reaches this method); the one refusal that is SITE-LOCAL, not a placement
    // fact, is the no-spot guard below.
    private static (int[] Plate, int[] SpotOf, List<string> SpotNames)? SplitByPlacement(
        ColorantPlacement placement)
    {
        IReadOnlyList<ColorantSlot> slots = placement.Slots;
        var plate = new int[slots.Count];       // process → 0..3 ; otherwise -1
        var spotOf = new int[slots.Count];      // spot-plane index ; otherwise -1
        for (var c = 0; c < slots.Count; c++)
        {
            ColorantSlot slot = slots[c];
            (plate[c], spotOf[c]) = slot.Kind switch
            {
                ColorantSlotKind.Plate => (slot.Index, -1),
                ColorantSlotKind.Spot => (-1, slot.Index),
                // §8.6.6.5: /None "shall never be painted on the page". Contributes nothing to either
                // output — the same answer the name split's All/None arm gives.
                _ => (-1, -1),
            };
        }

        // NO SPOT IN THE SPLIT ⇒ REFUSE, and let the caller's name split have the whole op.
        // TryToSpotInk and StencilInkFromFill exist to separate spot ink from process ink; a split with
        // no spot in it has nothing for either to do. This is not a limitation of the per-component rule
        // — it routes the op to exactly where it went before Pass 2b-engine, and the two answers are NOT
        // interchangeable downstream.
        //
        // The shape: an NChannel space whose components are ALL Process, e.g. /Components
        // [/PrCyan /PrMagenta] with both names listed and neither reserved. Placing them on their plates
        // leaves SpotNames empty, both callers' `if (spotNames.Count == 0) return null;` fires, and
        // ImageCommand.Spots stays null. Pellucid's CmykPageRenderer (:1119) picks
        // InkSourceCategory.SeparationDeviceN iff Spots is non-null — OverprintPlates is null here,
        // because PlatesForColorSpaceObject yields nothing for a non-reserved colorant name, so the
        // category alone decides. Null Spots ⇒ ProcessOther ⇒ InkDecider's "paint source on every process
        // plate" (:201-205) ⇒ KNOCKOUT. Under overprint that erases a backdrop this op used to preserve
        // via Table 148 row 3's nonzero-markedness proxy (InkDecider:149) — the GWG020-class failure this
        // programme treats as a defect everywhere else.
        //
        // The name split instead calls those non-reserved names Spot, hands them spot planes the registry
        // will not hold, and the compositor flattens — but Spots stays non-null and the op stays in row 3.
        // Per-component placement would be MORE correct on colour and LESS correct on overprint, and the
        // overprint regression is the one with teeth. Colour-only is the conservative direction here.
        //
        // GAP, recorded deliberately: an all-process NChannel op is not per-component-evaluated.
        //
        // Placed in THIS method, not in ComponentSplit, because this is the single point both callers
        // share: StencilInkFromFill calls SplitByPlacement directly and would otherwise flip the same
        // category for a stencil — the exact inversion of the GWG020 backdrop erasure SP-6d closed.
        return placement.SpotNames.Count > 0
            ? (plate, spotOf, [.. placement.SpotNames]) : null;
    }
```

**(b)** In `ComponentSplit` (`:625-642`), replace the `try` body only (the catch and every comment
above the method stay):

```csharp
            if (ColorSpaceResolver.OriginForColorSpaceObject(spaceObj, rawColor: null, document)
                is not { Placement: { } placement }) return null;
            // Slots is index-aligned with the names array by construction; a disagreement means the
            // table is not describing this space. Refuse rather than index across them.
            return placement.Slots.Count == nameCount ? SplitByPlacement(placement) : null;
```

**(c)** In `StencilInkFromFill`, replace the gate at `:447-448`:

```csharp
        if (origin is { Placement: { } placement } && placement.Slots.Count == inC
            && SplitByPlacement(placement) is { } split)
```

**(d)** Update stale comment references: at `:285` and `:421-428` and `:584` change
`SplitByComponents` to `SplitByPlacement`; in the `:334-337` block ("Map each colorant …") and the
`:440-443` block, replace "when the space is NChannel over a four-channel process space" with "when
the origin carries a placement table (NChannel over a four-channel process space, no /All, every
component placeable — ColorantPlacement.Build's nullability rule)". Do not delete the surrounding
reasoning.

**(e)** Confirm (read, don't assume) that `ColorantPlacement.Build`'s XML docs still state the
`/All`-by-name and all-or-nothing refusals — they carry the reasoning deleted from site code.

- [ ] **Step 4: Run the new tests — all 6 green**

```
dotnet test PdfLibrary.Tests\PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfImageToCmykPlacementTests"
```

- [ ] **Step 5: Mutation check — each observed red by assertion, then reverted**

1. `plate[c] = slot.Index` → `plate[c] = c`: `Transposition` fails on `ProcessCmyk[1]`.
2. `spotOf[c] = slot.Index` → arrival-order counter (`spotOf[c] = nextSpot++`):
   `SpotPlane_NotArrivalOrder` fails on `TintPlanes[0]` (252 → 28).
3. Delete the `SpotNames.Count > 0` guard (return the tuple unconditionally):
   `AllProcessPlacement_RefusesToTheNameSplit` fails on `Assert.NotNull`.
4. Gate `origin is { Placement: { } placement }` → also require `Components: { }`:
   `PlacementAlone` fails on `Names`.

- [ ] **Step 6: Full engine suite, all TFMs, zero warnings**

```
dotnet build PdfLibrary\PdfLibrary.csproj 2>&1 | Select-String "Warning"
dotnet test  (repo root)
```

Expected: `0 Warning(s)`; PdfLibrary.Tests **2685/0** (2679 + 6); every other project green.

- [ ] **Step 7: Commit (engine branch)**

```bash
git add PdfLibrary/Rendering/PdfImageToCmyk.cs PdfLibrary.Tests/Rendering/PdfImageToCmykPlacementTests.cs
git commit -m "refactor(colour): PdfImageToCmyk's split consumes ColorantPlacement (G-7 site 1)

SplitByComponents' role/channel loop was the placement table re-derived; the
site now reads the table. The /All and unplaceable refusals live on Build
(null table); the no-spot refusal (R3, the I-1 category-flip guard) stays
site-local with its comment intact. Behaviour-preserving: M4 measured slot
agreement on all 17 corpus instances.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 2: Pack and repin — verify site 1 moved nothing

**No commits in either repo except the pin files if the repo convention requires; pins are normally
left in `Directory.Build.props.local` (untracked) and `PdfCompare.csproj`.**

**Files:** `Pellucid\Directory.Build.props.local`, `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj`
(pins only — no source changes).

- [ ] **Step 1: Pack from the engine branch.** Run `pack-local.ps1`. Record `NEWVERSION`.
- [ ] **Step 2: Restore the Skia pin and repin.** Re-add to `Pellucid\Directory.Build.props.local`:

```xml
<LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>
```

Read the file back and confirm BOTH the new engine pin and the Skia pin are present. Repin
`PdfCompare.csproj` to `NEWVERSION`.

- [ ] **Step 3: Verify the embedded SHA.** The gate output prints `engine=2.5.1+<sha>`; `<sha>` must
  equal the engine branch HEAD from Task 1 Step 7. A matching version NUMBER proves nothing — check
  the SHA. If stale, clear the NuGet cache for `Lxman.PdfLibrary` and rebuild.
- [ ] **Step 4: Pellucid full suite.** Expected **1311/0** (no Pellucid changes yet). If
  App.Tests fails en masse in ~1 s with XamlLoadException, rebuild `--no-incremental` and re-run.
- [ ] **Step 5: GWG gate.**

```
dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~GwgRenderHashGateTests"
```

Expected: `51 fixtures hashed, 51 baselined, 0 differences`, SHA verified.

- [ ] **Step 6: NChannel gate.**

```
dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~NChannelRenderHashGateTests"
```

Expected: `3 fixtures hashed, 3 baselined, 0 differences`, SHA verified. This corpus contains
`6-2-4-4-t02-pass-a` — the one space where name-split and placement disagree — but its disagreement
is a **fill** space and site 1 is the image/stencil path, and M4 measured the sites' slot mapping
identical to placement everywhere. Zero movement is the prediction; **a moved digest is a STOP.**

---

## Task 3: Pellucid — site 2 (`InkDecider.TryPerComponent`) consumes Placement

**Files:**
- Modify: `Pellucid.Rendering.Cmyk\InkDecider.cs` (gate at `:142-147`, `TryPerComponent`
  `:338-434`)
- Test: `Pellucid.Rendering.Avalonia.Tests\Cmyk\InkDeciderTests.cs` (append)

**Interfaces:**
- Consumes: `ColorantOrigin.Placement`, `ColorantPlacement`, `ColorantSlot`, `ColorantSlotKind`
  from the engine pin; the test file's existing `Conv` and `RegistryFor(params string[])` helpers.
- Produces: `private static bool TryPerComponent(IReadOnlyList<ColourantComponent> components, ColorantPlacement placement, SpotColorantRegistry? registry, bool overprint, out InkDecision decision)`
  — one added parameter; callers outside `Decide`: none.

- [ ] **Step 1: Write the failing test and the three guard pins**

Append to `InkDeciderTests.cs` (inside the class, after the Plan 3 site-4 region):

```csharp
    // --- G-7 Plan 4 site 2: TryPerComponent's slot assignment comes from Placement; the tint,
    // registry, and own-alternate logic — and refusals R1/R2 — stay site-local. These fixtures set
    // BOTH Components and Placement, because the migrated gate requires both: Placement carries the
    // slots, Components carries Tint/Name/OwnAlternateCmyk. (Plan 3's Placed fixtures set ONLY
    // Placement precisely so this branch never fires for them — that must stay true.) ---

    private static ColourantComponent Proc(string name, int channel, double? tint) =>
        new(name, ColourantRole.Process, tint, null, channel);

    private static ColourantComponent Sp(string name, double? tint,
        IReadOnlyList<double>? ownAlternate = null) =>
        new(name, ColourantRole.Spot, tint, ownAlternate, null);

    [Fact]
    public void PerComponent_Transposition_SlotIndexBeatsComponentChannel()
    {
        // Component says channel 0, placement says Plate(1). Incoherent in production — constructed
        // to pin WHICH source is read, and the mutation target for `cmyk[slot.Index]` -> `cmyk[i]`.
        var origin = new ColorantOrigin(["PrCyan"], [0.36], "DeviceCMYK")
        {
            Components = [Proc("PrCyan", 0, 0.36)],
            ProcessChannelCount = 4,
            Placement = new ColorantPlacement([ColorantSlot.Plate(1)], []),
        };

        InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
            [0.36], "DeviceN", origin, overprint: true, overprintMode: 0, Conv);

        Assert.Equal(0f, d.C, 3);
        Assert.Equal(0.36f, d.M, 3);         // the SLOT's plate, not the component's channel
        Assert.False(d.PaintC);
        Assert.True(d.PaintM);
        Assert.False(d.RouteSpots);          // per-component succeeded — not the routed arm
    }

    [Fact]
    public void PerComponent_NullTintProcess_DeclinesWhole()
    {
        // R1: a null tint is unplaceable, not zero (a shading resolves its origin with no per-op
        // colour). The op must fall through WHOLE to the routed arm — RouteSpots true is that arm's
        // signature and TryPerComponent never sets it. Mutation target: treating a null tint as 0.
        var origin = new ColorantOrigin(["PrCyan", "Spot1"], [], "DeviceCMYK")
        {
            Components = [Proc("PrCyan", 0, null), Sp("Spot1", 0.5)],
            ProcessChannelCount = 4,
            Placement = new ColorantPlacement(
                [ColorantSlot.Plate(0), ColorantSlot.Spot(0)], ["Spot1"]),
        };

        InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
            [], "DeviceN", origin, overprint: true, overprintMode: 0, Conv,
            null, RegistryFor("Spot1"));

        Assert.True(d.RouteSpots);
    }

    [Fact]
    public void PerComponent_SpotWithNoPlaneAndNoAlternate_DeclinesWhole()
    {
        // R2: SpotA has a tint but neither a registry plane nor an own alternate — unplaceable, so
        // the WHOLE op declines even though SpotB alone was routable. Mutation target: skipping an
        // unplaceable spot instead of refusing (the `default` arm).
        var origin = new ColorantOrigin(["SpotA", "SpotB"], [0.5, 0.3], "DeviceCMYK")
        {
            Components = [Sp("SpotA", 0.5), Sp("SpotB", 0.3)],
            ProcessChannelCount = 4,
            Placement = new ColorantPlacement(
                [ColorantSlot.Spot(0), ColorantSlot.Spot(1)], ["SpotA", "SpotB"]),
        };

        InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
            [0.5, 0.3], "DeviceN", origin, overprint: true, overprintMode: 0, Conv,
            null, RegistryFor("SpotB"));

        Assert.True(d.RouteSpots);
    }

    [Fact]
    public void PerComponent_NoSpotAtAll_StillSucceeds()
    {
        // R3, site 2's side of the asymmetry: an all-process per-component op SUCCEEDS with an empty
        // route list (site 1 refuses the equivalent split — deliberately different, I-1 guard).
        var origin = new ColorantOrigin(["PrCyan", "PrMagenta"], [0.2, 0.3], "DeviceCMYK")
        {
            Components = [Proc("PrCyan", 0, 0.2), Proc("PrMagenta", 1, 0.3)],
            ProcessChannelCount = 4,
            Placement = new ColorantPlacement(
                [ColorantSlot.Plate(0), ColorantSlot.Plate(1)], []),
        };

        InkDecision d = InkDecider.Decide(InkSourceCategory.SeparationDeviceN,
            [0.2, 0.3], "DeviceN", origin, overprint: true, overprintMode: 0, Conv);

        Assert.Equal(0.2f, d.C, 3);
        Assert.Equal(0.3f, d.M, 3);
        Assert.Equal(0f, d.Y, 3);
        Assert.True(d.PaintC);
        Assert.True(d.PaintM);
        Assert.False(d.PaintY);
        Assert.False(d.PaintK);
        Assert.False(d.RouteSpots);
        Assert.Empty(d.SpotRoutes!);
    }
```

- [ ] **Step 2: Run and verify the expected red/green split**

```
dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~InkDeciderTests"
```

Expected: `PerComponent_Transposition` **FAILS** on `Assert.Equal(0f, d.C, 3)` (today the component's
channel 0 wins: C=0.36). The other three **PASS** today — they are guards pinning behaviour that
must survive the migration (R1, R2, R3-succeeds are reachable through the CURRENT role-driven code
too). Verify the classification; a guard that is red means the fixture is wrong — stop and fix it
before touching production code.

- [ ] **Step 3: Implement**

In `Pellucid.Rendering.Cmyk\InkDecider.cs`:

**(a)** The gate at `:142-147` becomes (keep the surrounding comment block; update its
"Gated on ProcessChannelCount == 4" paragraph to "Gated on Placement — whose nullability rule IS the
count-4 gate plus /All and unplaceable-component refusal (see ColorantPlacement.Build) — and on
Components, which still carries Tint/Name/OwnAlternateCmyk for the loop. Slots and Components are
index-aligned by construction; the count check refuses a hand-built origin that lies."):

```csharp
        if (category == InkSourceCategory.SeparationDeviceN
            // `nchannelComponents`, not `components`: Decide's own `components` parameter is the flattened
            // colour operand list, and a pattern variable of that name here is CS0136 against it.
            && origin is { Placement: { } nchannelPlacement, Components: { } nchannelComponents }
            && nchannelPlacement.Slots.Count == nchannelComponents.Count
            && TryPerComponent(nchannelComponents, nchannelPlacement, registry, overprint,
                out InkDecision perComponent))
            return perComponent;
```

**(b)** `TryPerComponent` gains the parameter and the loop switches on the slot. The signature:

```csharp
    private static bool TryPerComponent(
        IReadOnlyList<ColourantComponent> components, ColorantPlacement placement,
        SpotColorantRegistry? registry, bool overprint, out InkDecision decision)
```

The loop body (`:371-420`) becomes — every comment currently attached to an arm moves with its arm;
the `placed` declaration comment and everything after the loop are untouched:

```csharp
        IReadOnlyList<ColorantSlot> slots = placement.Slots;
        for (var i = 0; i < components.Count; i++)
        {
            ColourantComponent c = components[i];
            ColorantSlot slot = slots[i];
            switch (slot.Kind)
            {
                // Row 5-7: /None components are DISCARDED when painting named colourants directly. No
                // /Colorants lookup for them — a malformed file may define one, and it must be ignored.
                case ColorantSlotKind.Nothing:
                    continue;

                // §8.6.6.4: a named process colorant maps to the device colorant. Table 71: its own
                // /Colorants entry "shall be ignored", which is why nothing here consults it — and
                // POSITION is the channel identity, which is what slot.Index carries. A null tint
                // (a shading resolves its origin with no per-op colour) is unplaceable, not zero (R1).
                case ColorantSlotKind.Plate when c.Tint is { } pt:
                    cmyk[slot.Index] += (float)pt;
                    marked[slot.Index] = true;
                    placed = true;
                    continue;

                case ColorantSlotKind.Spot when registry?.TryGetPlane(c.Name) is { } plane
                                                && c.Tint is { } st:
                    routes.Add(new SpotRoute(plane, (float)st));
                    placed = true;
                    continue;

                // No plane: revert through this component's OWN alternate (Table 71 — the /Colorants
                // Separation describing "the appearance of that colorant alone"), which the engine has
                // already evaluated at this component's tint.
                case ColorantSlotKind.Spot when c.OwnAlternateCmyk is { Count: >= 4 } alt:
                    // `placed` is set OUTSIDE the loop below, deliberately: a spot whose alternate is all
                    // zeros (white — the shape the flatten arm's comment says GWG041 requires) marks
                    // nothing and routes nothing, yet it WAS placeable and the op must still succeed.
                    // That is why the guard below is a `placed` flag and not `marked.Any() ||
                    // routes.Count > 0`, which would misread this component as an unplaceable one.
                    placed = true;
                    for (var p = 0; p < 4; p++)
                    {
                        var v = (float)alt[p];
                        if (v == 0f) continue;
                        cmyk[p] += v;
                        marked[p] = true;
                    }
                    continue;

                // Unplaceable: a Plate slot with no tint (R1), or a Spot with neither a plane nor a
                // usable alternate (R2). Fall back whole. (The old "Process with no channel" and /All
                // refusals are now Build's: either makes the table null and this method unreachable.)
                default:
                    return false;
            }
        }
```

**(c)** In `TryPerComponent`'s XML doc, add one sentence to the "All or nothing" para: "Slot
assignment comes from `ColorantPlacement` (G-7 Plan 4); the refusal policy in this method is
site-local and deliberately stricter than the table — see the migration design's R1/R2."

- [ ] **Step 4: Run the four new tests — all green; then the whole InkDecider file's tests**

```
dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~InkDeciderTests"
```

Every pre-existing InkDecider test must be green untouched — in particular Plan 3's
`ProcessContribution_*` fixtures (Components null ⇒ this branch still never fires for them).

- [ ] **Step 5: Mutation check — each observed red by assertion, then reverted**

1. `cmyk[slot.Index]` → `cmyk[i]`: `PerComponent_Transposition` fails on `d.M`.
2. `case ColorantSlotKind.Plate when c.Tint is { } pt` → drop the `when` (null tint reads 0):
   `PerComponent_NullTintProcess_DeclinesWhole` fails on `RouteSpots` (per-component would succeed).
3. `default: return false` → `default: continue` (skip unplaceables):
   `PerComponent_SpotWithNoPlaneAndNoAlternate_DeclinesWhole` fails on `RouteSpots`.
4. Gate: drop the `Components: { }` requirement (pass `[]` or similar to compile):
   Plan 3's `ProcessContribution_ListedProcessNames_MarkTheirListedPlates` fails — its
   Components-null fixture would enter per-component and decline into a different arm, or throw;
   either way red. (If it happens to stay green, the mutation is decorative — investigate before
   trusting the gate shape.)

- [ ] **Step 6: Full Pellucid suite**

Expected: **1315/0** (1311 + 4). Same App.Tests caveats as Task 2 Step 4.

- [ ] **Step 7: Commit (Pellucid branch)** — stage by name, never `-A`:

```bash
git add Pellucid.Rendering.Cmyk/InkDecider.cs Pellucid.Rendering.Avalonia.Tests/Cmyk/InkDeciderTests.cs
git commit -m "refactor(colour): InkDecider.TryPerComponent's slots come from Placement (G-7 site 2)

The loop's Role/ProcessChannel reads were the placement table re-derived; the
slot assignment now comes from the table while tint, registry and own-alternate
logic — and the R1/R2 refusals — stay site-local. Decide's gate reads
Placement + Components. Behaviour-preserving per M4.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 4: Gates, merges, docs — verified on the MERGED result

- [ ] **Step 1: GWG + NChannel gates on the branch state.** Same commands and expectations as Task 2
  Steps 5-6 (site 2 is compositor-side — no repack needed for it). Expected 51/51/0 and 3/3/0,
  SHA still the Task 1 engine HEAD. STOP on any movement.
- [ ] **Step 2: Merge the engine branch** into `master` (`--no-ff`, message
  "Merge colour/g7-pass2b-migration: site 1's slot assignment comes from the placement table"),
  delete the branch. Run the full engine suite ON THE MERGE COMMIT: 2685/0, 0 warnings.
- [ ] **Step 3: Repack from merged master.** `pack-local.ps1`; record `NEWVERSION`; **re-add the
  Skia pin, read the file back**; repin `PdfCompare.csproj`; verify the embedded SHA equals the
  MERGE commit.
- [ ] **Step 4: Merge the Pellucid branch** into `main` (`--no-ff`, message
  "Merge colour/g7-pass2b-migration: site 2's slot assignment comes from the placement table"),
  delete the branch. Full Pellucid suite ON THE MERGE COMMIT against the Step 3 pin: 1315/0. GWG and
  NChannel gates once more: 51/51/0, 3/3/0, SHA = the merge commit.
- [ ] **Step 5: Close the records.** In `Docs/colour/rendering-conformance.md`, mark the "migration
  of Pass 2b's two shipped sites onto Placement" open item closed with a pointer to the design and
  both merge commits, preserving the original text (strike-through + "Closed", the repo's
  convention). In the parent design's §4.4, add a dated correction note: migrated 2026-07-28, refusal
  divergences preserved, see Plan 4. Commit to engine master:

```bash
git add Docs/colour/rendering-conformance.md Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md
git commit -m "docs(colour): record the Pass 2b -> Placement migration as closed

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 6: Push both repos.** `git push` in each; confirm `git status` clean, both on default
  branches, no stray branches or worktrees; Pellucid still shows only `?? website/`.

---

## Self-review

**Spec coverage.** Design §1 (goal/non-goals) → Global Constraints + Tasks 1/3 scope. §2 R1–R3 →
Task 1 test 4 (R3 site 1), Task 3 tests 2/3/4 (R1, R2, R3 site 2) — pinned from both sides. §3
(site 1: adapter, gates, comment moves, spot order) → Task 1 Steps 1/3; spot order pinned by two
fixtures (Build-shaped and hand-built non-sequential). §4 (site 2: loop, gate equivalence, no
repin) → Task 3; the gate-equivalence argument is exercised by Plan 3's existing Components-null
fixtures plus mutation 4. §5.1-5.5 (testing) → the red/green split verification, mutation steps,
gates in Tasks 2/4. §6 (error handling: no new throws, validating ctors upstream, catch retained) →
Task 1 Step 3(b) keeps the catch; no task adds a bounds check. §7 (delivery: one plan, two tasks,
separate commits, docs close-out) → Tasks 1/3 commits, Task 4.

**Placeholder scan.** No TBD/TODO; every code step carries complete code; comment edits name their
line ranges and replacement text.

**Type consistency.** `SplitByPlacement(ColorantPlacement)` matches between Task 1 Steps 1/3.
`TryPerComponent(IReadOnlyList<ColourantComponent>, ColorantPlacement, SpotColorantRegistry?, bool,
out InkDecision)` matches Task 3 Steps 1/3 and the gate. `ColourantComponent(Name, Role, Tint,
OwnAlternateCmyk, ProcessChannel = null)` matches the record. `SpotImageInk(Names, TintPlanes,
ProcessCmyk)` matches `PageDrawList.cs:28`. `ColorantOrigin(Names, Tints, AlternateSpace)` + init
`Components`/`ProcessChannelCount`/`Placement` matches `ColorantOrigin.cs:15`. `Decide`'s argument
order matches `InkDecider.cs:80-88`.

**Known weaknesses, stated.** (1) The Task 3 guard tests (R1/R2/R3-succeeds) pass before AND after —
their value is surviving the migration plus their named mutations; the red proof of the migration
itself rests on the two transposition fixtures, which use production-incoherent origins by design.
(2) Suite-count expectations (2685/1315) assume no unrelated drift; a differing count with zero
failures is a report-and-continue, not a stop. (3) Task 3 mutation 4's outcome is a prediction about
fixture reach — the step says to investigate if it stays green rather than trust it.
