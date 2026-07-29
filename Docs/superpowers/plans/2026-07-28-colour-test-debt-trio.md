# Colour test-debt trio: render-path pins for rows 4-5, 5-6, 5-7 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rows 4-5, 5-6 and 5-7 of the conformance matrix move ⚠️ → ✅ on render-path (or cited
existing) tests that have been seen to fail under their named mutations — taking the N class to
18 ✅ / 2 ⚠️ / 0 ❌.

**Architecture:** Test-only. Four new render-level fixtures in one new Pellucid test file, reusing
`NChannelPerComponentRenderTests`' harness verbatim; row 5-6 closes by citing existing engine pins
(spec §6a). A Task 0 probe measures production's actual output for every fixture shape before any
assertion is written — a mismatch with the row's normative requirement is a discovered violation
and a STOP, never a production edit in this pass.

**Tech Stack:** C#/.NET, xUnit. Pellucid net10.0. Engine repo receives docs only.

**Design:** `Docs/superpowers/specs/2026-07-28-colour-test-debt-trio-design.md` (`afce05e` + §6a
correction committed with this plan). Matrix: `Docs/colour/rendering-conformance.md`.

## Global Constraints

- **BASE:** Pellucid `main` @ `fa0c76e`, branch `colour/test-debt-trio`. Engine `master` @
  `afce05e` — **docs commits only, no engine branch** (no engine code or test changes in this
  plan). Engine pin `2.5.1-dev20260728204828` — **no pack, no repin.**
- **Test-only:** if any probe or pin disagrees with the row's normative expectation, STOP and
  report (spec §1). Production code in either repo is out of bounds.
- **Every ink assertion is positional per-plate/per-plane** — a sum/multiset/`Contains` assertion
  is decorative.
- **Every prescribed mutation names its assertion** and is observed red BY ASSERTION, then
  reverted. A mutation that cannot go red is a finding to report, not a reason to weaken.
- **NEVER `git add -A` in Pellucid** (pre-existing untracked `website/`). Stage by name.
- Suites at BASE: Pellucid 1315/0 (78 Linux-only Cups skips are normal); engine untouched at
  2685/0. Gates GWG 51/51/0, NChannel 3/3/0 — pure guard here (a moved digest means scope was
  violated — STOP).
- Pellucid.App.Tests: ~1 s mass XamlLoadException = stale build → rebuild `--no-incremental`; a
  HANG = Avalonia session death → kill the `Pellucid.App.Tests.exe` tree, re-run once.

---

## Task 0: Measure before asserting

**No commits.** Output goes to the SDD workspace ledger.

**Files:**
- Create (temporary): `Pellucid.Rendering.Avalonia.Tests\Cmyk\ReservedAndNoneRenderTests.cs` — the
  REAL file, but with probes that print instead of assert. Task 1 converts it in place.

**Interfaces:**
- Consumes: `CmykPageRenderer.RenderToBuffer`, `CmykPageBuffer.PlatesCopy()`,
  `SpotPlaneBuffer.PlanesCopy()`, `SpotColorantRegistry.Build`, `SpotDisplayCombiner.ToSrgbBgra`,
  `PdfGraphicsState`, `ColorantOrigin(Names, Tints, AlternateSpace)`, `PageColorant`,
  `FillCommand`/`PageDrawList`/`BeginPageArgs` — all exactly as `NChannelPerComponentRenderTests`
  and `SpotRoutingTests` use them today.
- Produces: measured plate/plane/mask values for the four §3/§4 fixture shapes, recorded in the
  ledger; the probe file Task 1 edits into pins.

- [ ] **Step 1: Write the probe file**

Create `Pellucid.Rendering.Avalonia.Tests\Cmyk\ReservedAndNoneRenderTests.cs`:

```csharp
using System.Numerics;
using Pellucid.Rendering.Cmyk;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.Icc;
using Xunit;

namespace Pellucid.Rendering.Avalonia.Tests.Cmyk;

/// <summary>
/// Render-level pins for conformance rows 4-5 (reserved process names take their canonical plates,
/// end-to-end) and 5-7 (/None discarded when painting named colourants directly, incl. the
/// overprint mask). Row 5-6's contexts are pinned engine-side (ShadingSpotSplitTests,
/// PdfImageToCmykTests' GWG080 fixture) — see the matrix cells. Every ink assertion here is
/// POSITIONAL per-plate/per-plane, per this programme's standing rule: the defect class is
/// routing, and every aggregate of a permutation is unchanged.
/// </summary>
public class ReservedAndNoneRenderTests
{
    private readonly ITestOutputHelper _out;
    public ReservedAndNoneRenderTests(ITestOutputHelper o) => _out = o;

    private static readonly DeviceCmykConverter Conv =
        new(ICCSharp.Profile.IccProfile.Parse(System.IO.File.ReadAllBytes(TestProfile.Path)));

    private const int W = 8, H = 8;

    private static List<PathSegment> FullPage() =>
    [
        new MoveToSegment(0, 0), new LineToSegment(W, 0), new LineToSegment(W, H),
        new LineToSegment(0, H), new ClosePathSegment(),
    ];

    private static PdfGraphicsState Fill(
        string resolvedSpace, double[] resolvedColor, ColorantOrigin origin, bool overprint = false) => new()
    {
        ResolvedFillColorSpace = resolvedSpace,
        ResolvedFillColor = resolvedColor,
        ResolvedFillColorantOrigin = origin,
        FillOverprint = overprint,
        FillAlpha = 1.0,
        Ctm = Matrix3x2.Identity,
    };

    private static PdfGraphicsState BackdropCmykFill(double c, double m, double y, double k) => new()
    {
        ResolvedFillColorSpace = "DeviceCMYK",
        ResolvedFillColor = [c, m, y, k],
        FillAlpha = 1.0,
        Ctm = Matrix3x2.Identity,
    };

    private static SpotColorantRegistry Spot1Registry() => SpotColorantRegistry.Build(
        [new PageColorant("Spot1", ColorantKind.Spot, "DeviceCMYK", null, (0, 0, 0))], Conv);

    private static (float[] Plates, float[] Planes) Render(
        SpotColorantRegistry registry, params DrawCommand[] commands)
    {
        var list = new PageDrawList(new BeginPageArgs(1, W, H, 1, 0, 0, 0),
            new List<DrawCommand>(commands));
        using var buf = new CmykPageBuffer(W, H);
        using var spots = new SpotPlaneBuffer(W, H, Math.Max(registry.SpotCount, 1));
        CmykPageRenderer.RenderToBuffer(list, buf, Conv, spots: spots, registry: registry);
        return (buf.PlatesCopy(), spots.PlanesCopy());
    }

    private static (float C, float M, float Y, float K) PlateAt(float[] plates, int x, int y)
    {
        int o = (y * W + x) * 4;
        return (plates[o], plates[o + 1], plates[o + 2], plates[o + 3]);
    }

    private static float PlaneAt(float[] planes, int x, int y, int plane, int planeCount) =>
        planes[(y * W + x) * planeCount + plane];

    // --- Row 4-5: reserved names → their canonical plates, not planes, not the alternate ---

    // 4-5a: Separation /Cyan, alternate deliberately ramping to MAGENTA (the row 4-10 trick) so a
    // reversion or a plane-routing regression is positionally visible. The resolved fill colour IS
    // the alternate's output — that is what production hands the compositor for an unregistered
    // Separation — so the question this fixture answers is whether the reserved NAME still claims
    // its plate downstream. PROBE: record all four plates + the plane.
    [Fact]
    public void ReservedSeparation_Cyan_PaintsTheCyanPlate_NotItsMagentaAlternate()
    {
        var origin = new ColorantOrigin(["Cyan"], [0.7], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            SpotColorantRegistry.Build([], Conv),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0, 0.7, 0, 0], origin)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        _out.WriteLine($"4-5a: C={c} M={m} Y={y} K={k} plane0={PlaneAt(planes, W / 2, H / 2, 0, 1)}");
    }

    // 4-5b: mixed plain DeviceN [Magenta, Spot1], Spot1 registered → the ROUTED arm. Magenta is
    // NOT registered and must take its plate by reserved-name classification alone.
    [Fact]
    public void ReservedName_InRoutedDeviceN_TakesItsPlate_ByClassificationNotRegistration()
    {
        var origin = new ColorantOrigin(["Magenta", "Spot1"], [0.4, 0.6], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            Spot1Registry(),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0, 0.4, 0, 0], origin, overprint: true)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        _out.WriteLine($"4-5b: C={c} M={m} Y={y} K={k} plane0={PlaneAt(planes, W / 2, H / 2, 0, 1)}");
    }

    // --- Row 5-7: /None discarded when painting named colourants directly (routed arm) ---

    // 5-7a: [Magenta, None, Spot1], Spot1 registered ⇒ direct painting. /None's 0.9 is the value
    // that must appear NOWHERE — not on a plate, not on a plane.
    [Fact]
    public void None_InRoutedDeviceN_IsDiscarded_ItsTintAppearsNowhere()
    {
        var origin = new ColorantOrigin(["Magenta", "None", "Spot1"], [0.4, 0.9, 0.6], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            Spot1Registry(),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0, 0.4, 0, 0], origin, overprint: true)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        _out.WriteLine($"5-7a: C={c} M={m} Y={y} K={k} plane0={PlaneAt(planes, W / 2, H / 2, 0, 1)}");
    }

    // 5-7b: the discard rule's observable with teeth — under overprint the /None component sets no
    // mask bit, so a pre-painted backdrop on the plates the space does NOT name must survive.
    [Fact]
    public void None_InRoutedDeviceN_SetsNoMaskBit_BackdropSurvivesOnUnnamedPlates()
    {
        var origin = new ColorantOrigin(["Magenta", "None", "Spot1"], [0.4, 0.9, 0.6], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            Spot1Registry(),
            new FillCommand(FullPage(), false, BackdropCmykFill(0.25, 0, 0.25, 0.25)),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0, 0.4, 0, 0], origin, overprint: true)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        _out.WriteLine($"5-7b: C={c} M={m} Y={y} K={k} plane0={PlaneAt(planes, W / 2, H / 2, 0, 1)}");
    }
}
```

- [ ] **Step 2: Run the probes and record**

```
dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~ReservedAndNoneRenderTests" --logger "console;verbosity=detailed"
```

Record all four output lines VERBATIM in the ledger. Then judge each against its row's normative
expectation:

| Probe | Normative expectation (from the spec) |
|---|---|
| 4-5a | C=0.7, M=Y=K=0, plane0=0 — the reserved name claims its plate; the magenta alternate is overridden |
| 4-5b | M=0.4, C=Y=K=0, plane0=0.6 |
| 5-7a | M=0.4, C=Y=K=0, plane0=0.6 — 0.9 appears nowhere |
| 5-7b | M=0.4, C=0.25, Y=0.25, K=0.25 (backdrop preserved), plane0=0.6 |

- **All four match** → proceed to Task 1.
- **Any mismatch** → STOP. Record the measured tuple beside the expectation in the ledger and
  report to the human partner: this is either a discovered violation (row goes back toward ❌ with
  evidence) or a wrong expectation in the spec — either way it is not this pass's to fix. Delete
  nothing; the probe file is the evidence.

## Task 1: Convert probes to pins, mutation-check, suite

**Files:**
- Modify: `Pellucid.Rendering.Avalonia.Tests\Cmyk\ReservedAndNoneRenderTests.cs`

**Interfaces:**
- Consumes: Task 0's measured values (must equal the normative table above, or Task 0 stopped).
- Produces: four green pins; the file drops `ITestOutputHelper` if no longer used.

- [ ] **Step 1: Replace each probe's `_out.WriteLine` with positional assertions**

In each test, delete the `_out.WriteLine` line and append (values from the normative table —
identical to Task 0's measured values by this point):

4-5a:
```csharp
        Assert.Equal(0.7f, c, 3);
        Assert.Equal(0f, m, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, k, 3);
        Assert.Equal(0f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
```

4-5b:
```csharp
        Assert.Equal(0f, c, 3);
        Assert.Equal(0.4f, m, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, k, 3);
        Assert.Equal(0.6f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
```

5-7a:
```csharp
        Assert.Equal(0f, c, 3);
        Assert.Equal(0.4f, m, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, k, 3);
        Assert.Equal(0.6f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
```

5-7b:
```csharp
        Assert.Equal(0.25f, c, 3);
        Assert.Equal(0.4f, m, 3);
        Assert.Equal(0.25f, y, 3);
        Assert.Equal(0.25f, k, 3);
        Assert.Equal(0.6f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
```

If `_out` is now unused, remove the field, constructor and `using` accordingly.

- [ ] **Step 2: Run the four pins — green**

```
dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~ReservedAndNoneRenderTests"
```

- [ ] **Step 3: Mutation checks — each observed red by assertion, then reverted**

These mutate ENGINE-PINNED production code paths in the PELLUCID repo only where the code lives in
Pellucid (`InkDecider`/`CmykPageRenderer`); no engine file is touched (engine-side classification
lives behind the pin and cannot be mutated here — where a listed mutation would require an engine
edit, mutate the Pellucid consumer of the same fact instead, and record which file was mutated):

1. **Reserved-name routing:** in `InkDecider.ProcessContribution`'s name-fallback switch, delete
   the `case "Magenta"` arm → 4-5b `Assert.Equal(0.4f, m, 3)` fails (and 5-7a's too — record
   both). Revert.
2. **/None discard on the routed arm:** in the same switch (or the placement branch if the fixture
   routes there — Task 0's probe tells you which arm served it; record it), add a
   `case "None": k = tint; pk = true; break;` arm → 5-7a `Assert.Equal(0f, k, 3)` fails and 5-7b's
   backdrop `Assert.Equal(0.25f, k, 3)` fails (0.9 lands on K). Revert.
3. **Mask widening:** force the routed arm's returned mask all-true under overprint (`pc = pm = py
   = pk = true;` before the return) → 5-7b's backdrop assertions on C/Y/K fail (knocked to the
   op's values instead of preserved). Revert.

If a mutation cannot make its named assertion fail, STOP mutating that line and record the
mismatch as a finding — do not swap in a weaker assertion.

- [ ] **Step 4: Full Pellucid suite**

Expected **1319/0** (1315 + 4). App.Tests caveats per Global Constraints.

- [ ] **Step 5: Commit (Pellucid branch)**

```bash
git add Pellucid.Rendering.Avalonia.Tests/Cmyk/ReservedAndNoneRenderTests.cs
git commit -m "test(colour): render-path pins for reserved-name plates and /None discard (rows 4-5, 5-7)

Four positional plate/plane pins on the CMYK path: a reserved Separation
name claims its plate over its (deliberately wrong) alternate; a routed
DeviceN's reserved name routes by classification, not registration; /None's
tint appears nowhere; and /None sets no overprint mask bit, so the backdrop
survives on unnamed plates. Values measured before asserted (Task 0).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

## Task 2: Gates, matrix close-out, merge, push

- [ ] **Step 1: Gates on the branch.**
`dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~GwgRenderHashGateTests"`
then `NChannelRenderHashGateTests`. Expected 51/51/0 and 3/3/0, `engine=2.5.1+<sha>` unchanged
(this plan repacked nothing — the SHA is the Plan 4 merge commit `66b1156…`). A moved digest =
scope violation = STOP.

- [ ] **Step 2: Merge and push Pellucid.** `git checkout main`, merge `colour/test-debt-trio`
  (`--no-ff -m "Merge colour/test-debt-trio: rows 4-5 and 5-7 pinned at render level"`), delete the
  branch, full suite on the merge commit (1319/0), push. Verify `git status` shows only
  `?? website/`.

- [ ] **Step 3: Matrix close-out (engine repo, direct commit to master).** In
  `Docs/colour/rendering-conformance.md`:
  - **4-5** → ✅: cite `ReservedAndNoneRenderTests` fixtures 4-5a/4-5b by test name, the magenta-
    alternate trick, and mutation 1.
  - **5-6** → ✅: cite the EXISTING pins — `ShadingSpotSplitTests.Split_AllNone_ContributeNothing`,
    `SplitByPlacement_NoneContributesNothing_ToAnyPlateOrSpot`, and `PdfImageToCmykTests`' GWG080
    `/None×3` fixture — and state plainly the row closed by audit-and-cite, not new tests (spec
    §6a.1).
  - **5-7** → ✅ (cell currently near-empty): the discard arms —
    `InkDeciderTests.NChannel_None_component_is_discarded_not_reverted` (per-component, poisoned
    own-alternate) and the two new routed-arm render pins incl. the overprint-mask observable.
  - **Score block**: dated delta appended, snapshot preserved: N class now **18 ✅ / 2 ⚠️ (5-3,
    5-10) / 0 ❌**.
  Commit: `docs(colour): rows 4-5, 5-6, 5-7 close -- N-class 18/20 with zero violations` +
  Co-Authored-By trailer. Push engine master.

- [ ] **Step 4: Verify both repos clean**, default branches, no stray branches; Pellucid shows
  only `?? website/`.

---

## Self-review

**Spec coverage.** §1 goal/stop-rule → Global Constraints + Task 0 Step 2's stop. §2 harness → the
probe file (verbatim helper shapes from `NChannelPerComponentRenderTests`/`SpotRoutingTests`). §3
(4-5a/b) → Task 0 probes 1-2, Task 1 pins + mutation 1. §4 as corrected by §6a (5-7a/b routed arm
with registered spot; 5-6 closed by citation) → Task 0 probes 3-4, Task 1 pins + mutations 2-3,
Task 2 Step 3. §5 moot per §6a.1. §6 close-out → Task 2 Step 3 incl. the audit-and-cite honesty
note. §7 → Task 1 Step 4, Task 2 Steps 1-2.

**Placeholder scan.** None. The one deliberately unresolved value — which arm serves the routed
fixtures — is Task 0's measurement output, with mutation 2's instruction conditioned on it
explicitly.

**Type consistency.** `Render(SpotColorantRegistry, params DrawCommand[])` matches its two use
shapes; `PlateAt`/`PlaneAt` match `NChannelPerComponentRenderTests`; `ColorantOrigin(Names, Tints,
AlternateSpace)` positional ctor matches `ColorantOrigin.cs:15`; `PageColorant(name, kind, space,
null, (0,0,0))` matches `RegistryFor`'s usage in `InkDeciderTests`; `SpotColorantRegistry.Build`
overloads match `SpotRoutingTests:63` (no cap) and `NChannelPerComponentRenderTests:98` (cap).

**Known weaknesses, stated.** (1) The normative table's 4-5a expectation is the row's requirement,
not a prediction of current behaviour — Task 0 exists precisely because the flatten-arm interaction
is underived; a mismatch stops the pass rather than surprising Task 1. (2) Mutation 2's target arm
is data-dependent (placement vs name fallback); the instruction requires recording which. (3) The
plane-count argument to `PlaneAt` is 1 everywhere (one registered spot or none) — correct for these
fixtures but silently wrong if a future edit adds a second registered spot; the fixture comments
carry the coupling.
