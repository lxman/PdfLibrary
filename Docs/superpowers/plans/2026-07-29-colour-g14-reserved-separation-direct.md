# G-14: Reserved-Name Separations Apply the Process Colourant Directly — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the CMYK soft-proof path, a Separation/DeviceN whose colourant names are all reserved-process (C/M/Y/K, optionally /None) applies each tint directly to its canonical plate — Adobe's answer — instead of flattening through its alternate, in every painting context (fill, stroke, shading/mesh, image, stencil).

**Architecture:** Spec `Docs/superpowers/specs/2026-07-29-g14-reserved-separation-direct-design.md` (approved). One site per context, all gated on the same predicate: fills/strokes get a new dedicated arm in `InkDecider.Decide` (Pellucid); shadings/meshes get a name-based direct-pack in `ShadingBuilder.BuildCmykMapper` (engine); images get a direct route in `PdfImageToCmyk.TryToCmyk` (engine); stencils get process-only ink from `PdfImageToCmyk.StencilInkFromFill` (engine) plus a gate relaxation in `CmykPageRenderer` (Pellucid). `ColorSpaceResolver.ResolveSeparation` is NOT touched — the RGB path must keep reverting (row 4-12).

**Tech Stack:** C# / .NET (engine PDF repo `C:\Users\jorda\RiderProjects\PDF`, multi-targets net8/9/10; Pellucid repo `C:\Users\jorda\RiderProjects\Pellucid`), xunit, GWG/NChannel render-hash gates.

## Global Constraints

- **Two repos.** Engine = `C:\Users\jorda\RiderProjects\PDF` (branch `master`). Pellucid = `C:\Users\jorda\RiderProjects\Pellucid` (branch `main`). Never `git add -A` in Pellucid (untracked `website/` must stay untracked).
- **The RGB path is out of scope** and must not change: row 4-12 requires reversion through the alternate there. No edits to `ColorSpaceResolver.ResolveSeparation`/`ResolveDeviceN`.
- **The predicate**, everywhere it appears: every name is `Cyan`/`Magenta`/`Yellow`/`Black` or `None`, AND at least one name is one of C/M/Y/K. `/All` never satisfies it (its own arms exist). Mixed spaces (any other name) are excluded — they keep flattening (§8.6.6.5 all-or-nothing).
- **Every new pin must be SEEN TO FAIL** — either red-before-fix (TDD) or by a deliberate mutation after — and every ink assertion is positional per-plate/per-plane (matrix rules).
- **Stop rule:** any measurement that contradicts this plan's stated expectation (a red step failing with a DIFFERENT value than predicted, an unpredicted gate digest move, a reserved name found in `SpotColorantRegistry`) = stop, report, do not improvise.
- **Suites at close:** engine 2685 + new / 0 failed, 0 warnings, net8/9/10; Pellucid 1319 ± retired/new / 0. Gates: GWG 51/51, NChannel 3/3, with only census-predicted digest movement.
- Engine changes reach Pellucid only via `pack-local.ps1` + repin (Task 6). Known traps: the script deletes the `PdfLibrary.Rendering.Skia` pin every run (re-add + read back), and stale NuGet cache (clear it).
- Test commands: engine `dotnet test PdfLibrary.Tests` from `C:\Users\jorda\RiderProjects\PDF`; Pellucid `dotnet test Pellucid.Rendering.Avalonia.Tests --filter <name>` from `C:\Users\jorda\RiderProjects\Pellucid`.

---

### Task 1: Census + registry guard (measurement only — no production code)

**Files:**
- Create (temporary, deleted in this task): `C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Tests\Rendering\G14CensusProbe.cs`
- Read: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary\Rendering\PageColorantReader.cs` (or wherever `PageColorant.Classify` is defined — grep `Classify`)

**Interfaces:**
- Produces: a census verdict recorded in the Task 8 comparison — the list of corpus files (GWG + NChannel) containing an all-reserved Separation/DeviceN whose tint transform output diverges from direct application, i.e. the predicted set of gate digests the fix may move. Also: confirmation that reserved names never enter `SpotColorantRegistry`.

- [ ] **Step 1: Verify the registry guard by reading code**

Open `PageColorant.Classify` (engine; grep `ColorantKind Classify` under `PdfLibrary`). Confirm `Cyan`/`Magenta`/`Yellow`/`Black` classify as `ColorantKind.Process` (not Spot), and confirm the Pellucid side: `SpotColorantRegistry.Build` (in `Pellucid.Rendering.Cmyk\SpotColorantRegistry.cs`) only creates planes for colorants classified Spot. If either is false: STOP — the new arm and the routed arm could both claim an op, and the design's mutual-exclusion argument fails.

- [ ] **Step 2: Write the census probe (temporary test, engine repo)**

`PdfLibrary.Tests` has `InternalsVisibleTo`, so `document.XrefTable` / `GetObject` are reachable. The corpus root is `C:\Users\jorda\RiderProjects\gwg-gos\Ghent_PDF_Output_Suite_V50_Patches\Categories` (sibling of both repos — hardcoding the absolute path is fine, this file is deleted before commit).

```csharp
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using PdfLibrary.Functions;
using Xunit;
using Xunit.Abstractions;

namespace PdfLibrary.Tests.Rendering;

// TEMPORARY G-14 census probe — DELETE BEFORE COMMIT. Walks every corpus PDF's xref for
// Separation/DeviceN arrays whose colourants are all reserved-process names, and reports
// whether the tint transform's output diverges from direct application (the only shape the
// G-14 fix can move a gate digest with).
public class G14CensusProbe(ITestOutputHelper output)
{
    private const string Categories =
        @"C:\Users\jorda\RiderProjects\gwg-gos\Ghent_PDF_Output_Suite_V50_Patches\Categories";

    private static readonly Dictionary<string, int> Plate = new()
        { ["Cyan"] = 0, ["Magenta"] = 1, ["Yellow"] = 2, ["Black"] = 3 };

    [Fact]
    public void Census()
    {
        foreach (string file in Directory.EnumerateDirectories(Categories)
                     .SelectMany(cat => Directory.Exists(Path.Combine(cat, "Patch_pages"))
                         ? Directory.EnumerateFiles(Path.Combine(cat, "Patch_pages"), "*.pdf")
                         : []))
        {
            PdfDocument doc;
            try { doc = PdfDocument.Load(file); } catch { output.WriteLine($"UNREADABLE {file}"); continue; }
            using (doc)
            {
                doc.MaterializeAllObjects();
                foreach (PdfXrefEntry entry in doc.XrefTable.Entries.Where(e => e.IsInUse).ToList())
                {
                    if (doc.GetObject(entry.ObjectNumber) is not PdfArray
                        { Count: >= 4 } arr || arr[0] is not PdfName { Value: "Separation" or "DeviceN" })
                        continue;
                    string[] names = Deref(arr[1], doc) switch
                    {
                        PdfName one => [one.Value],
                        PdfArray na => [.. na.Select(x => Deref(x, doc)).OfType<PdfName>().Select(p => p.Value)],
                        _ => [],
                    };
                    if (names.Length == 0) continue;
                    bool anyProcess = names.Any(n => Plate.ContainsKey(n));
                    if (!anyProcess || !names.All(n => n == "None" || Plate.ContainsKey(n))) continue;

                    // All-reserved space found. Divergence check: tint transform vs direct at 3 tints.
                    var diverges = false;
                    PdfFunction? tint = PdfFunction.Create(arr[3], doc);
                    string altName = Deref(arr[2], doc) switch
                    {
                        PdfName n2 => n2.Value,
                        PdfArray { Count: >= 1 } aa when aa[0] is PdfName at => at.Value,
                        _ => "?",
                    };
                    if (tint is null || altName != "DeviceCMYK") diverges = true;   // can't prove innocence
                    else
                        foreach (double t in (double[])[0.0, 0.5, 1.0])
                        {
                            double[] inputs = [.. names.Select(_ => t)];
                            double[] outp = tint.Evaluate(inputs);
                            var direct = new double[4];
                            for (var i = 0; i < names.Length; i++)
                                if (Plate.TryGetValue(names[i], out int p)) direct[p] = t;
                            if (outp.Length != 4 || Enumerable.Range(0, 4).Any(i => Math.Abs(outp[i] - direct[i]) > 0.004))
                                { diverges = true; break; }
                        }
                    output.WriteLine($"{(diverges ? "DIVERGES" : "matches ")} {Path.GetFileName(file)}: " +
                        $"[{string.Join(",", names)}] alt={altName}");
                }
            }
        }
    }

    private static PdfObject Deref(PdfObject o, PdfDocument d) =>
        o is PdfIndirectReference r ? d.ResolveReference(r) ?? o : o;
}
```

If any of the API names differ (`XrefTable.Entries`, `PdfXrefEntry.IsInUse`, `ResolveReference`, `PdfFunction.Create` — all seen in production code at `PdfDocument.cs:322`, `ColorSpaceResolver.cs:266-288`), adjust the probe to the real names; the probe's OUTPUT is the deliverable, not its shape.

- [ ] **Step 3: Run the probe, record the prediction**

Run: `dotnet test PdfLibrary.Tests --filter G14CensusProbe -- --logger "console;verbosity=detailed"` (or read the trx/output). Record every `DIVERGES` line verbatim into the execution notes. **Prediction:** per the conformance matrix ("visible only under a lying alternate — no well-formed file diverges, hence 51/51/0"), the expected result is ZERO diverging files, i.e. zero gate digests move. A `DIVERGES` hit is not a stop — it becomes the predicted-movement list Task 8 verifies visually — but MORE THAN THREE hits means the matrix's reachability claim was wrong: stop and report before proceeding. Also run the NChannel gate's fixture list if it draws from a different directory (open `Pellucid.Rendering.Avalonia.Tests\Cmyk\NChannelRenderHashGateTests.cs`, find its fixture paths, and point the probe's `Categories` constant at them for a second run).

- [ ] **Step 4: Delete the probe**

Delete `G14CensusProbe.cs`. `git status` in the engine repo must be clean. Nothing is committed in this task.

---

### Task 2: Pellucid — the reserved-direct arm in InkDecider (fills + strokes)

**Files:**
- Modify: `C:\Users\jorda\RiderProjects\Pellucid\Pellucid.Rendering.Cmyk\InkDecider.cs` (arm after the `/All` arm ending at line 125; helper near `AnyRegistered` at line 446)
- Modify: `C:\Users\jorda\RiderProjects\Pellucid\Pellucid.Rendering.Avalonia.Tests\Cmyk\ReservedAndNoneRenderTests.cs`

**Interfaces:**
- Consumes: `ProcessContribution(ColorantOrigin, bool)` (existing, `InkDecider.cs:464`), `ColorantOrigin(Names, Tints, AlternateSpace)` (engine).
- Produces: `InkDecider.Decide` returns a direct-process `InkDecision` (`RouteSpots: false`, `SpotRoutes: null`) for any `SeparationDeviceN` op whose origin has per-op tints and all-reserved names. Private helper `AllReservedProcessOrNone(IReadOnlyList<string>)` — Task 7's renderer change relies on the DECISION, not the helper (it stays private).

- [ ] **Step 1: Rewrite the G-14 baseline pin to the Adobe expectation (red)**

In `ReservedAndNoneRenderTests.cs`, replace the whole `ReservedSeparation_Unregistered_FlattensThroughItsAlternate_G14Baseline` method (lines 76-99) and its comment with:

```csharp
    // G-14 CLOSED (2026-07-29): reserved process names are always-available colourants on the CMYK
    // soft-proof path (ISO 32000-2 §8.6.6.4 first clause; user ruling 2026-07-28 "Adobe or better").
    // The alternate here is a deliberately WRONG magenta ramp — direct application must ignore it.
    // This test replaced the G-14 baseline pin, which asserted the old flatten-through-alternate
    // behaviour (M=0.7); its deliberate retirement is this red→green flip.
    [Fact]
    public void ReservedSeparation_Unregistered_AppliesTheProcessColourantDirectly()
    {
        var origin = new ColorantOrigin(["Cyan"], [0.7], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            SpotColorantRegistry.Build([], Conv),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0, 0.7, 0, 0], origin)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0.7f, c, 3);    // the tint, on ITS plate — the lying alternate is ignored
        Assert.Equal(0f, m, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, k, 3);
        Assert.Equal(0f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
    }
```

- [ ] **Step 2: Add the remaining pins (all red)**

Append to the same class:

```csharp
    // --- G-14: direct application of all-reserved Separation/DeviceN (unregistered) ---

    // All-reserved DeviceN: both tints to their canonical plates; the alternate (a deliberate
    // C↔M transposition) is ignored. Transposition is the mutation this catches positionally.
    [Fact]
    public void AllReservedDeviceN_Unregistered_AppliesEachTintToItsOwnPlate()
    {
        var origin = new ColorantOrigin(["Cyan", "Magenta"], [0.3, 0.6], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            SpotColorantRegistry.Build([], Conv),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0.6, 0.3, 0, 0], origin)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0.3f, c, 3);
        Assert.Equal(0.6f, m, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, k, 3);
        Assert.Equal(0f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
    }

    // NEGATIVE CONTROL: a mixed DeviceN (reserved + unregistered spot) must STILL flatten through
    // its alternate — §8.6.6.5 direct application is all-or-nothing. The resolved colour here is a
    // fabricated alternate output distinct from both tints, so a wrong routing is positionally
    // visible on every plate.
    [Fact]
    public void MixedDeviceN_UnregisteredSpot_StillFlattensThroughItsAlternate()
    {
        var origin = new ColorantOrigin(["Cyan", "PANTONE-X"], [0.5, 0.5], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            SpotColorantRegistry.Build([], Conv),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0, 0.35, 0, 0], origin)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0f, c, 3);      // NOT 0.5 — direct application must not fire
        Assert.Equal(0.35f, m, 3);   // the alternate's output, flattened
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, k, 3);
        Assert.Equal(0f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
    }

    // Overprint: the direct arm marks ONLY the plates the space names (+ /None marks nothing);
    // the backdrop survives on every unnamed plate. /None's 0.9 must appear nowhere.
    [Fact]
    public void ReservedDirect_UnderOverprint_MarksOnlyNamedPlates_NoneDiscarded()
    {
        var origin = new ColorantOrigin(["Cyan", "None"], [0.7, 0.9], "DeviceCMYK");
        (float[] plates, float[] planes) = Render(
            SpotColorantRegistry.Build([], Conv),
            new FillCommand(FullPage(), false, BackdropCmykFill(0.25, 0.25, 0.25, 0.25)),
            new FillCommand(FullPage(), false, Fill("DeviceCMYK", [0, 0.7, 0, 0], origin, overprint: true)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0.7f, c, 3);    // named: painted with the tint
        Assert.Equal(0.25f, m, 3);   // unnamed: backdrop survives (0.9 appears nowhere)
        Assert.Equal(0.25f, y, 3);
        Assert.Equal(0.25f, k, 3);
        Assert.Equal(0f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);
    }

    // Strokes share Decide via CompositeStroke — one positional pin so the stroke path is
    // observed, not reasoned about. LineWidth 20 in an 8×8 page: the stroked band covers centre.
    [Fact]
    public void ReservedSeparation_Stroke_AppliesTheProcessColourantDirectly()
    {
        var origin = new ColorantOrigin(["Cyan"], [0.7], "DeviceCMYK");
        var state = new PdfGraphicsState
        {
            ResolvedStrokeColorSpace = "DeviceCMYK",
            ResolvedStrokeColor = [0, 0.7, 0, 0],
            ResolvedStrokeColorantOrigin = origin,
            StrokeAlpha = 1.0,
            LineWidth = 20,
            Ctm = System.Numerics.Matrix3x2.Identity,
        };
        (float[] plates, _) = Render(
            SpotColorantRegistry.Build([], Conv),
            new StrokeCommand([new MoveToSegment(0, H / 2.0), new LineToSegment(W, H / 2.0)], state));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0.7f, c, 3);
        Assert.Equal(0f, m, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, k, 3);
    }
```

If `PdfGraphicsState`'s stroke properties are named differently (open the class and check — `ResolvedStrokeColorSpace` / `ResolvedStrokeColor` / `ResolvedStrokeColorantOrigin` / `StrokeAlpha` / `LineWidth` are the expected names, mirroring the fill ones used at `ReservedAndNoneRenderTests.cs:35-39`), use the real names. Any other adjustment needed to make the stroke render = measure first, don't guess.

- [ ] **Step 3: Run — verify red WITH THE PREDICTED VALUES**

Run: `dotnet test Pellucid.Rendering.Avalonia.Tests --filter ReservedAndNoneRenderTests`
Expected failures (these ARE the Task 0-style measurements — a different failing value is the stop rule):
- `ReservedSeparation_Unregistered_AppliesTheProcessColourantDirectly`: c is 0, m is 0.7 (flatten).
- `AllReservedDeviceN_Unregistered_AppliesEachTintToItsOwnPlate`: c is 0.6, m is 0.3 (the transposed alternate flattens).
- `ReservedDirect_UnderOverprint_MarksOnlyNamedPlates_NoneDiscarded`: c is not 0.7 (flatten painted the alternate's M under the nonzero-proxy mask).
- `ReservedSeparation_Stroke_AppliesTheProcessColourantDirectly`: c is 0, m is 0.7.
- `MixedDeviceN_UnregisteredSpot_StillFlattensThroughItsAlternate`: PASSES already (it pins current behaviour) — it must STAY green through this task.
- The four pre-existing tests (4-5b, 5-7a, 5-7b): green, untouched.

- [ ] **Step 4: Implement the arm**

In `InkDecider.cs`, insert between the `/All` arm's closing brace (line 125) and the NChannel per-component comment block (line 127):

```csharp
        // G-14 (ISO 32000-2 §8.6.6.4, first clause; ruling 2026-07-28 "Adobe or better"): the reserved
        // process names C/M/Y/K are ALWAYS-AVAILABLE colourants on this simulated-CMYK device, so a
        // Separation/DeviceN whose names are all reserved (± /None) applies each tint DIRECTLY to its
        // canonical plate — the alternate and tint transform are ignored, exactly as §8.6.6.4 ignores
        // them for /All//None. This widens row 4-11's availability rule (registry OR reserved); the RGB
        // path still reverts (row 4-12), which is why this lives here and not in ResolveSeparation.
        //
        // Gated on Tints being present: a shading/mesh resolves its origin with rawColor null (Tints
        // empty — a gradient has no single per-op tint), and its VALUE fix is engine-side
        // (ShadingBuilder.BuildCmykMapper's reserved-name pack, the sibling of this arm — see the
        // cross-reference there). Firing here on an empty-tints origin would flip the shading's MASK
        // to name-derived while its per-pixel colours still came from the alternate ramp — half a fix.
        //
        // Mixed spaces (any non-reserved name) fail the predicate and flatten: §8.6.6.5 makes DeviceN
        // direct application all-or-nothing. /All never satisfies the predicate (its arm is above).
        // ProcessContribution supplies the same value+mask semantics as the routed arm's process half;
        // RouteSpots is false because an all-reserved space has no spot to route.
        if (category == InkSourceCategory.SeparationDeviceN
            && origin is { Names.Count: > 0 }
            && origin.Tints.Count >= origin.Names.Count
            && AllReservedProcessOrNone(origin.Names))
        {
            (float dc, float dm, float dy, float dk, bool dbC, bool dbM, bool dbY, bool dbK) =
                ProcessContribution(origin, overprint);
            return new InkDecision(dc, dm, dy, dk, dbC, dbM, dbY, dbK,
                RouteSpots: false, KnockoutOtherSpots: knockoutOtherSpots);
        }
```

And next to `AnyRegistered` (after line 452):

```csharp
    // G-14 predicate — the ONE definition of "all-reserved" on this side of the repo boundary. The
    // engine's copy is ColorSpaceResolver.AllReservedProcessOrNone (same semantics, kept in step by
    // the cross-repo pins): every name reserved-process or /None, at least one reserved-process.
    private static bool AllReservedProcessOrNone(IReadOnlyList<string> names)
    {
        var anyProcess = false;
        for (var i = 0; i < names.Count; i++)
        {
            switch (names[i])
            {
                case "None": break;
                case "Cyan" or "Magenta" or "Yellow" or "Black": anyProcess = true; break;
                default: return false;
            }
        }
        return anyProcess;
    }
```

- [ ] **Step 5: Run — all green, suite-wide**

Run: `dotnet test Pellucid.Rendering.Avalonia.Tests`
Expected: full suite green (1319 − 1 retired + 5 new − but the retirement REPLACED a test, so net +4: expect 1323 passed, 0 failed). GWG/NChannel gate tests run inside this suite: they must be UNMOVED (Task 1 predicted zero movement; the engine is unchanged so far, and this arm only fires for origins no gate fixture produces per the census). Any gate movement here = unpredicted = STOP.

- [ ] **Step 6: Mutation-check the negative control**

The mixed-control pin has never been red. Temporarily weaken the predicate — change `default: return false;` to `default: break;` — and run the filter `ReservedAndNoneRenderTests`. Expected: `MixedDeviceN_UnregisteredSpot_StillFlattensThroughItsAlternate` goes RED (c reads 0.5 — the arm hijacked it). Revert the mutation, re-run, green. Record "seen to fail" in the commit message.

- [ ] **Step 7: Commit**

```powershell
cd C:\Users\jorda\RiderProjects\Pellucid
git add Pellucid.Rendering.Cmyk/InkDecider.cs Pellucid.Rendering.Avalonia.Tests/Cmyk/ReservedAndNoneRenderTests.cs
git commit -m @'
feat(colour): G-14 fills/strokes — all-reserved Separation/DeviceN paints its plates directly

Retires the G-14 baseline pin deliberately (red->green on this change); mixed-DeviceN
negative control mutation-checked (predicate weakened -> control red -> reverted).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: Engine — shared predicate + ShadingBuilder direct pack (shadings + meshes)

**Files:**
- Modify: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary\Rendering\ColorSpaceResolver.cs` (helpers near `ReservedProcessChannels`, line 1210)
- Modify: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary\Rendering\ShadingBuilder.cs` (`BuildCmykMapper`, lines 153-174)
- Test: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Tests\Rendering\ShadingAllProcessNChannelTests.cs` (append — the fixture helpers there are exactly what these tests need)

**Interfaces:**
- Consumes: `ColorSpaceResolver.ReservedProcessChannels` (private map, `ColorSpaceResolver.cs:1210`), `PackCmyk` (existing in ShadingBuilder).
- Produces: `internal static bool ColorSpaceResolver.AllReservedProcessOrNone(IReadOnlyList<string> names)` and `internal static int? ColorSpaceResolver.ReservedChannelOf(string name)` — Tasks 4 and 5 call BOTH. `internal static string[] ColorSpaceResolver.ColorantNamesOf(PdfArray sepOrDeviceN, PdfDocument? document)` — element-1 name extraction (single name or array), used here and reusable by later tasks.

- [ ] **Step 1: Write the failing tests**

Append to `ShadingAllProcessNChannelTests.cs` (reusing its `Reals`/`Names`/`Cmyk` helpers; note `DeviceN(...)` there builds DeviceN arrays — Separation needs a local helper):

```csharp
    // --- G-14: plain (non-NChannel) all-reserved Separation/DeviceN pack straight onto plates ---

    private static PdfDictionary LyingMagentaTint()
    {
        // tint t → (0, t, 0, 0): a deliberately WRONG alternate for a /Cyan separation. Direct
        // application must ignore it; the flatten path is positionally visible on the M plate.
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(0, 0, 0, 0));
        d.Add(new PdfName("C1"), Reals(0, 1, 0, 0));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    [Fact]
    public void G14_ReservedSeparation_MapperPacksItsPlateDirectly()
    {
        var cs = new PdfArray(new PdfName("Separation"), new PdfName("Cyan"),
            new PdfName("DeviceCMYK"), LyingMagentaTint());

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(cs, null);

        Assert.NotNull(toCmyk);
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.7]));
        Assert.Equal(178, c);        // 0.7 → its OWN plate
        Assert.Equal(0, m);          // the lying alternate is ignored
        Assert.Equal(0, y);
        Assert.Equal(0, k);
    }

    [Fact]
    public void G14_ReservedPlainDeviceN_MapperPacksByName_NoneDiscarded()
    {
        // Plain DeviceN (NO /Attributes → no placement → the pre-G-14 code ran the tint transform).
        // Names deliberately non-canonical order + /None: [Black, Cyan, None].
        var cs = new PdfArray(new PdfName("DeviceN"), Names("Black", "Cyan", "None"),
            new PdfName("DeviceCMYK"), IdentityTint());

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(cs, null);

        Assert.NotNull(toCmyk);
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.5, 0.25, 0.9]));
        Assert.Equal(64, c);         // Cyan is names[1] → C plate gets 0.25
        Assert.Equal(0, m);
        Assert.Equal(0, y);
        Assert.Equal(127, k);        // Black is names[0] → K plate gets 0.5
        // /None's 0.9 appears NOWHERE. With the identity transform the OLD path put 0.5 on C,
        // 0.25 on M and 0.9 on Y — every plate distinguishes the two paths positionally.
    }

    [Fact]
    public void G14_MixedDeviceN_MapperStillRunsTheTintTransform()
    {
        // NEGATIVE CONTROL: one non-reserved name → the predicate fails → tint transform runs.
        var cs = new PdfArray(new PdfName("DeviceN"), Names("Cyan", "PANTONE-X"),
            new PdfName("DeviceCMYK"), ConstantTint());

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(cs, null);

        Assert.NotNull(toCmyk);
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.5, 0.5]));
        Assert.Equal((byte)255, c);  // ConstantTint returns (1,1,1,1) — proof the transform RAN
        Assert.Equal((byte)255, m);
        Assert.Equal((byte)255, y);
        Assert.Equal((byte)255, k);
    }
```

If `BuildCmykMapper` is `internal` rather than visible to the test as written, it already is `internal` (`ShadingBuilder.cs:139`) and `PdfLibrary.Tests` has InternalsVisibleTo — no change needed.

- [ ] **Step 2: Run — verify red with predicted values**

Run: `dotnet test PdfLibrary.Tests --filter G14_`
Expected: `G14_ReservedSeparation_MapperPacksItsPlateDirectly` fails with c=0, m=178 (transform ran). `G14_ReservedPlainDeviceN_MapperPacksByName_NoneDiscarded` fails with c=127, m=64, y=229 (identity permutation). `G14_MixedDeviceN_MapperStillRunsTheTintTransform` PASSES (control, stays green). Different values = STOP.

- [ ] **Step 3: Implement — helpers in ColorSpaceResolver**

Next to `IsReservedProcessName` (`ColorSpaceResolver.cs:1221`), add:

```csharp
    /// <summary>G-14 predicate — the ONE definition of "all-reserved" on the engine side (the
    /// compositor's copy is InkDecider.AllReservedProcessOrNone, kept in step by the cross-repo
    /// pins): every name reserved-process or /None, at least one reserved-process. /All fails it.
    /// True means ISO 32000-2 §8.6.6.4's first clause applies on a CMYK device — every colourant
    /// is available, nothing may be simulated, tints go straight to plates.</summary>
    internal static bool AllReservedProcessOrNone(IReadOnlyList<string> names)
    {
        var anyProcess = false;
        foreach (string n in names)
        {
            if (n == "None") continue;
            if (!IsReservedProcessName(n)) return false;
            anyProcess = true;
        }
        return anyProcess;
    }

    /// <summary>The CMYK plate index of a reserved process name (Cyan 0 … Black 3); null for any
    /// other name, including /None and /All. The lookup is <see cref="ReservedProcessChannels"/> —
    /// the same single list every reserved-name decision draws from.</summary>
    internal static int? ReservedChannelOf(string name) =>
        ReservedProcessChannels.TryGetValue(name, out int ch) ? ch : null;

    /// <summary>Element-1 colourant names of a Separation/DeviceN array: a single name for
    /// Separation, the /Names array for DeviceN. Empty on any other shape.</summary>
    internal static string[] ColorantNamesOf(PdfArray sepOrDeviceN, PdfDocument? document)
    {
        PdfObject el = sepOrDeviceN.Count > 1 ? sepOrDeviceN[1] : PdfNull.Instance;
        if (el is PdfIndirectReference r && document is not null)
            el = document.ResolveReference(r) ?? el;
        return el switch
        {
            PdfName one => [one.Value],
            PdfArray arr => [.. arr.Select(x =>
                x is PdfIndirectReference xr && document is not null
                    ? document.ResolveReference(xr) ?? x : x)
                .OfType<PdfName>().Select(p => p.Value)],
            _ => [],
        };
    }
```

(If `PdfNull.Instance` doesn't exist, use any non-name placeholder — e.g. guard with `if (sepOrDeviceN.Count < 2) return [];` and read `sepOrDeviceN[1]` directly.)

- [ ] **Step 4: Implement — the mapper's reserved-name pack**

In `ShadingBuilder.BuildCmykMapper`, `case "Separation" or "DeviceN":` (line 153) — after the `AllProcessPlacement` bypass (line 166-167), before the `BuildTintToCmyk` fallback, insert:

```csharp
                        // G-14: a PLAIN Separation/DeviceN whose colourants are all reserved process
                        // names (± /None). The placement bypass above cannot see it — placement is
                        // NChannel-only by construction — but §8.6.6.4's first clause applies all the
                        // same: the device has a unit for every colourant, nothing may be simulated,
                        // and the tint transform (which prepress files use to LIE — the G-14 fixture
                        // is a /Cyan separation with a magenta-ramping alternate) is ignored. The
                        // compositor's fill/stroke sibling is InkDecider's reserved-direct arm.
                        string[] reservedNames = ColorSpaceResolver.ColorantNamesOf(arr, document);
                        if (reservedNames.Length >= 1
                            && ColorSpaceResolver.AllReservedProcessOrNone(reservedNames))
                            return c => PackByReservedName(c, reservedNames);
```

And add alongside `PackByPlacement`:

```csharp
    // G-14: components at NAMES order → their canonical plates; /None contributes nothing.
    private static uint PackByReservedName(double[] comps, string[] names)
    {
        Span<double> plates = stackalloc double[4];
        for (var i = 0; i < names.Length && i < comps.Length; i++)
            if (ColorSpaceResolver.ReservedChannelOf(names[i]) is { } ch)
                plates[ch] = comps[i];
        return PackCmyk([plates[0], plates[1], plates[2], plates[3]]);
    }
```

(Match `PackCmyk`'s real parameter shape — read its declaration; if it takes `double[]` this is right as written.)

- [ ] **Step 5: Run — green, then whole engine suite**

Run: `dotnet test PdfLibrary.Tests --filter G14_` → 3/3 green.
Run: `dotnet test` (all engine test projects, net8/9/10) → everything green, 0 warnings. A pre-existing shading test moving = STOP (the mapper's behaviour changed for a shape a test pinned — read that test's intent before touching anything).

- [ ] **Step 6: Commit**

```powershell
cd C:\Users\jorda\RiderProjects\PDF
git add PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary/Rendering/ShadingBuilder.cs PdfLibrary.Tests/Rendering/ShadingAllProcessNChannelTests.cs
git commit -m @'
feat(colour): G-14 shadings — all-reserved Separation/DeviceN packs straight onto its plates

BuildCmykMapper gains a reserved-name pack beside the NChannel placement bypass; meshes
inherit it via MeshShadingReader. Adds the engine-side G-14 predicate helpers.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: Engine — direct image route in PdfImageToCmyk.TryToCmyk

**Files:**
- Modify: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary\Rendering\PdfImageToCmyk.cs` (the `Separation/DeviceN` branch of `TryToCmyk`, line 141)
- Test: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Tests\Rendering\PdfImageToCmykTests.cs` (append; reuse its `Image(...)` helper at line 16)

**Interfaces:**
- Consumes: `ColorSpaceResolver.AllReservedProcessOrNone` + `ReservedChannelOf` (Task 3), `SeparationNames` + `B` (existing privates in this file).
- Produces: `TryToCmyk` returns a direct-plate CMYK plane for an all-reserved Separation/DeviceN image regardless of its alternate/tint transform. Indexed images over such a base are explicitly NOT covered (recorded in Task 9's matrix note).

- [ ] **Step 1: Write the failing tests**

Append to `PdfImageToCmykTests.cs` (build the lying tint inline — this file has no function helpers):

```csharp
    // --- G-14: all-reserved Separation/DeviceN image samples go straight to their plates ---

    private static PdfDictionary LyingMagentaTint()
    {
        // t → (0, t, 0, 0): a WRONG alternate for a /Cyan image. Direct routing must ignore it.
        var d = new PdfDictionary
        {
            [new PdfName("FunctionType")] = new PdfInteger(2),
            [new PdfName("Domain")] = new PdfArray(new PdfReal(0), new PdfReal(1)),
            [new PdfName("C0")] = new PdfArray(new PdfReal(0), new PdfReal(0), new PdfReal(0), new PdfReal(0)),
            [new PdfName("C1")] = new PdfArray(new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(0)),
            [new PdfName("N")] = new PdfReal(1),
        };
        return d;
    }

    [Fact]
    public void G14_ReservedSeparationImage_SamplesGoToItsPlate()
    {
        var cs = new PdfArray(new PdfName("Separation"), new PdfName("Cyan"),
            new PdfName("DeviceCMYK"), LyingMagentaTint());
        // Two pixels: tint 0.7 (178), tint 0 (0).
        PdfImage img = Image(cs, [178, 0], 2, 1);

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out int w, out int h);

        Assert.NotNull(cmyk);
        Assert.Equal(2, w); Assert.Equal(1, h);
        Assert.Equal(178, cmyk![0]);  // C ← the sample, directly
        Assert.Equal(0, cmyk[1]);     // M — the lying alternate is ignored
        Assert.Equal(0, cmyk[2]);
        Assert.Equal(0, cmyk[3]);
        Assert.Equal(0, cmyk[4]); Assert.Equal(0, cmyk[5]); Assert.Equal(0, cmyk[6]); Assert.Equal(0, cmyk[7]);
    }

    [Fact]
    public void G14_ReservedDeviceNImage_PacksByName_HonoursDecode()
    {
        // [Black, Cyan] with /Decode [1 0  0 1]: Black's samples invert, Cyan's pass through.
        var tint = LyingMagentaTint();   // any transform — it must be IGNORED
        var cs = new PdfArray(new PdfName("DeviceN"),
            new PdfArray(new PdfName("Black"), new PdfName("Cyan")),
            new PdfName("DeviceCMYK"), tint);
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = cs,
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("Decode")] = new PdfArray(
                new PdfReal(1), new PdfReal(0), new PdfReal(0), new PdfReal(1)),
        };
        var img = new PdfImage(new PdfStream(dict, [51, 102]));   // 0.2, 0.4

        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(img, null, out _, out _);

        Assert.NotNull(cmyk);
        Assert.Equal(102, cmyk![0]);  // Cyan (names[1]) → C, decode identity: 0.4
        Assert.Equal(0, cmyk[1]);
        Assert.Equal(0, cmyk[2]);
        Assert.Equal(204, cmyk[3]);   // Black (names[0]) → K, decode [1 0]: 1−0.2 = 0.8
    }
```

- [ ] **Step 2: Run — verify red with predicted values**

Run: `dotnet test PdfLibrary.Tests --filter G14_Reserved`
Expected: first test fails with cmyk[0]=0, cmyk[1]=178 (transform ran, magenta); second fails with the transform's output on M (whatever `LyingMagentaTint` makes of a 2-input evaluate — record the measured bytes; if `TryToCmyk` instead returns NULL for either, that is also a legitimate baseline — record it). A crash = STOP.

- [ ] **Step 3: Implement**

In `TryToCmyk`'s Separation/DeviceN branch (line 141), insert at the top of the branch, before `BuildTintToCmyk`:

```csharp
            // G-14: all-reserved colourants (± /None) — samples go straight to their plates;
            // the alternate + tint transform are ignored (§8.6.6.4 first clause; the fill/stroke
            // sibling is InkDecider's reserved-direct arm, the shading sibling is
            // ShadingBuilder.PackByReservedName). Indexed images over such a base are NOT routed
            // here (the Indexed branch above already returned) — recorded in the matrix.
            string[] rNames = SeparationNames(cs, document);
            if (rNames.Length >= 1 && ColorSpaceResolver.AllReservedProcessOrNone(rNames))
            {
                int rInC = rNames.Length;
                if (data.Length < px * rInC) return null;
                double[]? rDec = image.DecodeArray;
                bool rApplyDecode = rDec is not null && rDec.Length >= rInC * 2;
                var plateOf = new int[rInC];
                for (var c = 0; c < rInC; c++)
                    plateOf[c] = ColorSpaceResolver.ReservedChannelOf(rNames[c]) ?? -1;   // /None → −1

                var outR = new byte[px * 4];
                for (var i = 0; i < px; i++)
                {
                    int src = i * rInC, po = i * 4;
                    for (var c = 0; c < rInC; c++)
                    {
                        if (plateOf[c] < 0) continue;
                        double s = data[src + c] / 255.0;
                        if (rApplyDecode) s = rDec![2 * c] + s * (rDec[2 * c + 1] - rDec[2 * c]);
                        outR[po + plateOf[c]] = B(s);
                    }
                }
                return outR;
            }
```

- [ ] **Step 4: Run — green, then the whole engine suite**

Run: `dotnet test PdfLibrary.Tests --filter G14_Reserved` → green.
Run: `dotnet test` → all green, 0 warnings. Watch `SeparationDecodeArrayTests` and `ImageToCmykSourceProfileTests` specifically — they exercise this branch; if any moved, the moved test's fixture is all-reserved and its pinned expectation was the alternate's output: STOP and report (that is a deliberate-retirement decision, not a silent fix).

- [ ] **Step 5: Commit**

```powershell
cd C:\Users\jorda\RiderProjects\PDF
git add PdfLibrary/Rendering/PdfImageToCmyk.cs PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs
git commit -m @'
feat(colour): G-14 images — all-reserved Separation/DeviceN samples route straight to plates

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 5: Engine — process-only stencil ink from StencilInkFromFill

**Files:**
- Modify: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary\Rendering\PdfImageToCmyk.cs` (`StencilInkFromFill`, the `spotNames.Count == 0` guard at line 468)
- Test: `C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Tests\Rendering\PdfImageToCmykTests.cs` (append)

**Interfaces:**
- Consumes: `ColorSpaceResolver.AllReservedProcessOrNone` (Task 3); the `plate[]` array already computed by the split above the guard.
- Produces: `StencilInkFromFill` returns a `SpotImageInk` with `Names = []`, `TintPlanes = []` and a constant direct-plate `ProcessCmyk` for an all-reserved fill origin. **Task 7's renderer gate relaxation depends on exactly this shape** (empty Names ⇒ route with zero spot loops). `TryToSpotInk` is untouched and still never returns an empty-Names ink.

- [ ] **Step 1: Write the failing test**

Append to `PdfImageToCmykTests.cs`:

```csharp
    [Fact]
    public void G14_StencilInk_AllReservedFill_ReturnsProcessOnlyInk()
    {
        // A stencil whose FILL is [/Separation /Cyan] tint 0.7 (unregistered). Pre-G-14 the
        // no-spot guard returned null and the stencil painted the fill's resolved ALTERNATE.
        var origin = new ColorantOrigin(["Cyan"], [0.7], "DeviceCMYK");

        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 2, 2);

        Assert.NotNull(ink);
        Assert.Empty(ink!.Names);                 // no spot to route
        Assert.Empty(ink.TintPlanes);
        Assert.Equal(2 * 2 * 4, ink.ProcessCmyk.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(178, ink.ProcessCmyk[i * 4]);      // C = 0.7, directly
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 1]);    // M/Y/K untouched
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 2]);
            Assert.Equal(0, ink.ProcessCmyk[i * 4 + 3]);
        }
    }

    [Fact]
    public void G14_StencilInk_MixedUnregisteredFill_StillDeclines()
    {
        // NEGATIVE CONTROL: one non-reserved name → decline (null) exactly as before, so the
        // mixed fill keeps its flatten path. (An origin whose spot IS registered was already
        // handled — spotNames non-empty never hits the new branch.)
        var origin = new ColorantOrigin(["Cyan", "PANTONE-X"], [0.5, 0.5], "DeviceCMYK");
        Assert.Null(PdfImageToCmyk.StencilInkFromFill(origin, 2, 2));
    }
```

Note `StencilInkFromFill` is `internal` (`PdfImageToCmyk.cs:437`) — visible to the test project. The mixed control: with names ["Cyan","PANTONE-X"], the name split puts PANTONE-X in `spotNames`, so `spotNames.Count == 1 ≠ 0` and the method proceeds to build spot ink — verify what it returns TODAY by running the test red-first; if it currently returns non-null spot ink (spot present ⇒ the guard never fired), replace the control's assertion with the measured shape and a comment saying which arm serves it — the point pinned is only "the new branch does not hijack it".

- [ ] **Step 2: Run — verify red / verify the control's real baseline**

Run: `dotnet test PdfLibrary.Tests --filter G14_StencilInk`
Expected: `AllReservedFill` fails with `ink` null (the guard declined). The mixed control PASSES or reveals the measured shape per the note above — record it.

- [ ] **Step 3: Implement**

Replace `if (spotNames.Count == 0) return null;   // process-only fill → the RGBA path is fine (a non-goal)` (line 468) with:

```csharp
        if (spotNames.Count == 0)
        {
            // G-14: an ALL-RESERVED fill (± /None) is no longer "the RGBA path is fine" — that path
            // paints the fill's resolved ALTERNATE, and for a reserved-name separation the alternate
            // is ignored (§8.6.6.4 first clause). Return process-only ink: the plates directly from
            // the tints, no spot names, no planes. The compositor routes it with a zero-length spot
            // loop (CmykPageRenderer's image gate accepts empty Names for exactly this shape). Any
            // other process-only fill (e.g. a DeviceN of non-reserved process names without a
            // placement) still declines to the RGBA path, unchanged.
            if (!ColorSpaceResolver.AllReservedProcessOrNone(origin.Names)) return null;
            var cellR = new byte[4];
            for (var c = 0; c < inC; c++)
                if (plate[c] >= 0)
                    cellR[plate[c]] = B(c < origin.Tints.Count ? origin.Tints[c] : 0.0);
            int pxR = width * height;
            var processR = new byte[pxR * 4];
            for (var i = 0; i < pxR; i++)
            {
                int po = i * 4;
                processR[po] = cellR[0]; processR[po + 1] = cellR[1];
                processR[po + 2] = cellR[2]; processR[po + 3] = cellR[3];
            }
            return new SpotImageInk([], [], processR);
        }
```

(Check `SpotImageInk`'s constructor parameter order at its declaration in `PageDrawList.cs:23`-ish — `(Names, TintPlanes, ProcessCmyk)` per `TryToSpotInk`'s `new SpotImageInk(spotNames, planes, process)` — and match the literal argument types: if `Names` is `List<string>`, pass `new List<string>()`; if `TintPlanes` is `byte[]`, pass `Array.Empty<byte>()`.)

- [ ] **Step 4: Run — green + full engine suite**

Run: `dotnet test PdfLibrary.Tests --filter G14_StencilInk` → green.
Run: `dotnet test` → all green, 0 warnings, net8/9/10. `RecordingRenderTargetSpotTests` exercises the stencil path — a movement there means a pinned fixture was all-reserved: STOP and report.

- [ ] **Step 5: Commit**

```powershell
cd C:\Users\jorda\RiderProjects\PDF
git add PdfLibrary/Rendering/PdfImageToCmyk.cs PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs
git commit -m @'
feat(colour): G-14 stencils — all-reserved fill yields process-only stencil ink

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 6: Engine suites, pack, repin into Pellucid

**Files:**
- Run: `C:\Users\jorda\RiderProjects\PDF\pack-local.ps1` (or the repo's actual pack script — check the repo root; the memory-recorded name is `pack-local.ps1`)
- Modify: Pellucid's package pin (`Directory.Packages.props` or the csproj that pins `PdfLibrary` — grep Pellucid for the current engine version `2.5.1-dev20260728204828` to find the file)

**Interfaces:**
- Produces: a new `2.5.1-dev<stamp>` engine package pinned in Pellucid, with the `PdfLibrary.Rendering.Skia` pin `0.1.1-dev20260717153208` still present. Tasks 7-8 build against it.

- [ ] **Step 1: Full engine verification**

Run from `C:\Users\jorda\RiderProjects\PDF`: `dotnet test` (all frameworks). Expected: 2685 + 7 new = 2692 passed, 0 failed, 0 warnings, net8/9/10. Then `git status` → clean; `git log --oneline -3` → Tasks 3-5's commits present.

- [ ] **Step 2: Pack and repin (the three known traps)**

1. Run `.\pack-local.ps1` from the engine repo root (PowerShell, NOT the .sh — the sh variant writes a broken `/c/...` feed path).
2. In Pellucid, grep for `2.5.1-dev` and update the pin to the new stamp the pack printed.
3. **Read back the Skia pin:** the script deletes the `PdfLibrary.Rendering.Skia` pin `0.1.1-dev20260717153208` every run (13 occurrences on record). Open the pins file, confirm the Skia line is present, re-add it if not.
4. Clear the NuGet cache for the local feed so the new bits actually load: `dotnet nuget locals all --clear` (the Skia package is static but the engine package stamp is new — the clear guards against the recorded stale-cache failure).

- [ ] **Step 3: Pellucid builds and is still green pre-Task-7**

Run from `C:\Users\jorda\RiderProjects\Pellucid`: `dotnet test Pellucid.Rendering.Avalonia.Tests`. Expected: all green EXCEPT possibly gate digests IF the census (Task 1) predicted movement — engine-side image/shading/stencil changes are now live under the gates. Compare any movement against the Task 1 prediction list: predicted → leave red for Task 8's visual verification; unpredicted → STOP.

- [ ] **Step 4: Commit the repin**

```powershell
cd C:\Users\jorda\RiderProjects\Pellucid
git add <the pins file(s) only — never -A>
git commit -m @'
build: repin engine with G-14 direct-application (shadings/images/stencils)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 7: Pellucid — stencil gate relaxation + image/stencil/shading render pins

**Files:**
- Modify: `C:\Users\jorda\RiderProjects\Pellucid\Pellucid.Rendering.Cmyk\CmykPageRenderer.cs` (the image spot-routing gate, line 1213)
- Modify: `C:\Users\jorda\RiderProjects\Pellucid\Pellucid.Rendering.Avalonia.Tests\Cmyk\ReservedAndNoneRenderTests.cs` (append)

**Interfaces:**
- Consumes: `SpotImageInk` with empty `Names` (Task 5's new shape), the repinned engine (Task 6), `ImageCommand(Rgba, Width, Height, Alpha, Ctm, State, Cmyk, OverprintPlates, Spots)` (`PageDrawList.cs:40`).
- Produces: the renderer's image branch composites process-only `SpotImageInk` (zero spot routes); render-level pins for the image, stencil and shading contexts.

- [ ] **Step 1: Write the failing render pins**

Append to `ReservedAndNoneRenderTests.cs`:

```csharp
    // --- G-14 render-level pins: image, stencil, shading contexts ---

    // An IMAGE in [/Separation /Cyan] with a lying alternate: the engine (Task 4) now supplies a
    // direct-plate Cmyk plane on ImageCommand.Cmyk; this pin observes it end-to-end. The Rgba
    // plane deliberately carries the ALTERNATE's magenta so a regression to the RGBA path is
    // positionally visible.
    [Fact]
    public void ReservedSeparationImage_RendersDirectPlate_NotTheAlternate()
    {
        var state = new PdfGraphicsState { FillAlpha = 1.0, Ctm = Matrix3x2.CreateScale(W, H) };
        var rgba = new byte[1 * 1 * 4];
        rgba[0] = 255; rgba[1] = 0; rgba[2] = 255; rgba[3] = 255;   // magenta-ish: the WRONG answer
        var cmyk = new byte[] { 178, 0, 0, 0 };                     // C=0.7 direct: the RIGHT answer
        (float[] plates, _) = Render(
            SpotColorantRegistry.Build([], Conv),
            new ImageCommand(rgba, 1, 1, AlphaMode.Opaque, Matrix3x2.CreateScale(W, H), state,
                Cmyk: cmyk, OverprintPlates: (true, false, false, false)));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0.7f, c, 2);    // byte-quantised 178/255
        Assert.Equal(0f, m, 2);
        Assert.Equal(0f, y, 2);
        Assert.Equal(0f, k, 2);
    }

    // A STENCIL whose fill is [/Separation /Cyan] 0.7: Task 5's process-only SpotImageInk (empty
    // Names) must composite its ProcessCmyk — this is the gate relaxation under test. Red before
    // the CmykPageRenderer change: the empty-Names ink is refused and the stencil paints nothing
    // onto the C plate (its RGBA fallback carries the alternate).
    [Fact]
    public void ReservedSeparationStencil_ProcessOnlyInk_CompositesDirectly()
    {
        var origin = new ColorantOrigin(["Cyan"], [0.7], "DeviceCMYK");
        SpotImageInk? ink = PdfImageToCmyk.StencilInkFromFill(origin, 1, 1);
        Assert.NotNull(ink);                       // Task 5's contract
        var state = new PdfGraphicsState { FillAlpha = 1.0, Ctm = Matrix3x2.CreateScale(W, H) };
        var rgba = new byte[] { 255, 0, 255, 255 };   // alternate-magenta fallback = the WRONG answer
        (float[] plates, float[] planes) = Render(
            SpotColorantRegistry.Build([], Conv),
            new ImageCommand(rgba, 1, 1, AlphaMode.Opaque, Matrix3x2.CreateScale(W, H), state,
                Spots: ink));

        (float c, float m, float y, float k) = PlateAt(plates, W / 2, H / 2);
        Assert.Equal(0.7f, c, 2);
        Assert.Equal(0f, m, 2);
        Assert.Equal(0f, y, 2);
        Assert.Equal(0f, k, 2);
        Assert.Equal(0f, PlaneAt(planes, W / 2, H / 2, 0, 1), 3);   // no spot plane touched
    }
```

For the shading context, the engine's `BuildCmykMapper` fix (Task 3) feeds `ShadingDescriptor.CmykColors`; a render-level shading pin needs a `ShadingCommand` with a real `ShadingDescriptor`. Build one through the ENGINE builder rather than hand-rolling the descriptor — `ShadingBuilder.Build(...)`'s exact entry signature is at the top of `ShadingBuilder.cs` (Task 3 touched that file; read it): construct the same `[/Separation /Cyan /DeviceCMYK lying-tint]` dictionary as Task 3's `G14_ReservedSeparation_MapperPacksItsPlateDirectly` plus `ShadingType 2`, `Coords [0 0 8 0]`, `Function` = the lying tint, wrap in a `ShadingCommand` with a full-page clip per `CompositeShading`'s bare-`sh` rule (`CmykPageRenderer.cs:633` — give the command a preceding `ClipCommand(FullPage(), false)`), render, and assert `PlateAt` centre reads `c≈0.7, m=0`. If `ShadingBuilder.Build`'s inputs cannot be satisfied from a test (e.g. it requires a `PdfDocument`), pin the shading context ENGINE-side instead — Task 3's mapper test already covers the value; add one `ShadingBuilder.Build`-level test there asserting `CmykColors[StopCount−1]` unpacks to `(178,0,0,0)` — and record in the Task 9 matrix cell that the shading pin is engine-level (the same level row 5-6 accepted).

- [ ] **Step 2: Run — verify red where predicted**

Run: `dotnet test Pellucid.Rendering.Avalonia.Tests --filter ReservedAndNoneRenderTests`
Expected: the image pin GREEN already (`ImageCommand.Cmyk` plane compositing is pre-existing behaviour — the pin is new coverage, not a code change; it must then be mutation-checked in Step 5). The stencil pin RED: the gate at line 1213 requires `Names.Count > 0`, so the empty-Names ink is refused and c reads ≠ 0.7 (record the measured value). Different shape of failure = STOP.

- [ ] **Step 3: Relax the image-routing gate**

In `CmykPageRenderer.cs` line 1213, change:

```csharp
        if (spots is not null && registry is not null && ink is { Names.Count: > 0 }
            && ink.ProcessCmyk.Length >= iw * ih * 4 && ink.TintPlanes.Length >= iw * ih * ink.Names.Count)
```

to:

```csharp
        // G-14: `Names.Count > 0` relaxed to non-null. An EMPTY-Names ink is StencilInkFromFill's
        // process-only shape for an all-reserved fill (its doc names this site): route it with a
        // zero-length spot loop so ProcessCmyk composites — the RGBA fallback would paint the
        // fill's resolved ALTERNATE, which a reserved-name separation must ignore. TryToSpotInk
        // never returns empty Names, so no pre-G-14 producer reaches this branch differently.
        if (spots is not null && registry is not null && ink is not null
            && ink.ProcessCmyk.Length >= iw * ih * 4 && ink.TintPlanes.Length >= iw * ih * ink.Names.Count)
```

(The subsequent per-name `TryGetPlane` loop over zero names leaves `routeSpots = true`, and the per-pixel branch's spot loop runs zero times — read `CmykPageRenderer.cs:1216-1259` once to confirm nothing else indexes `Names[0]`.)

- [ ] **Step 4: Run — green**

Run: `dotnet test Pellucid.Rendering.Avalonia.Tests` (full suite — the gate hashes must not move from THIS change; empty-Names ink did not exist before Task 6's repin, and stencils with all-reserved fills either exist in the corpus (census said) or don't).

- [ ] **Step 5: Mutation-check the always-green image pin**

The image pin never went red. Mutate: in the test, change `Cmyk: cmyk` to `Cmyk: null` and run — expected RED (the RGBA magenta converts to a magenta-dominant CMYK, c≉0.7). Revert, green. Record in the commit message.

- [ ] **Step 6: Commit**

```powershell
cd C:\Users\jorda\RiderProjects\Pellucid
git add Pellucid.Rendering.Cmyk/CmykPageRenderer.cs Pellucid.Rendering.Avalonia.Tests/Cmyk/ReservedAndNoneRenderTests.cs
git commit -m @'
feat(colour): G-14 image/stencil/shading render pins; empty-Names process-only ink composites

Image pin mutation-checked (Cmyk plane nulled -> red -> reverted).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 8: Gates vs census predictions

**Files:**
- Run: `Pellucid.Rendering.Avalonia.Tests\Cmyk\GwgRenderHashGateTests.cs`, `NChannelRenderHashGateTests.cs`
- Possibly modify: their pinned digests (ONLY for census-predicted movements)

**Interfaces:**
- Consumes: Task 1's census prediction list (expected: empty).

- [ ] **Step 1: Run both gates**

Run: `dotnet test Pellucid.Rendering.Avalonia.Tests --filter "GwgRenderHashGateTests|NChannelRenderHashGateTests"`
Expected: **GWG 51/51, NChannel 3/3, zero digest movement** — the census predicted none (well-formed files have truthful alternates, so direct and flattened answers coincide).

- [ ] **Step 2: If a digest moved**

- On the census's DIVERGES list → render that fixture (the pdf-compare harness at `~/PDFs/PdfCompare`, or the gate's own image dump if it has one), open the fixture's `_ReadMe.pdf`, verify the rendered result against the fixture's OWN printed pass criterion (the oracle — never another renderer), and only then update the pinned digest with a comment citing G-14 and the ReadMe criterion.
- NOT on the list → **STOP.** Do not re-pin. Report the fixture, the old/new digests, and which task's change is implicated (bisect by stashing the Task 7 renderer change first, then the repin).

- [ ] **Step 3: Commit (only if digests were re-pinned)**

```powershell
cd C:\Users\jorda\RiderProjects\Pellucid
git add <gate test files only>
git commit -m @'
test(colour): re-pin census-predicted gate digests under G-14 direct application

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 9: Docs close-out, push both repos

**Files:**
- Modify: `C:\Users\jorda\RiderProjects\PDF\Docs\colour\rendering-conformance.md`
- Read for cross-reference staleness: the G-13 entry (line 550) and row 5-6/4-5/4-11 cells (rows table, lines 82-88 region)

**Interfaces:**
- Consumes: everything landed; both suites green; gates settled.

- [ ] **Step 1: Matrix updates**

In `rendering-conformance.md`:
1. **G-14 entry (line 562):** rewrite from open-gap to CLOSED — keep the discovery/measurement history, add: closed 2026-07-29; the fix sites (InkDecider reserved-direct arm, ShadingBuilder.PackByReservedName, PdfImageToCmyk.TryToCmyk reserved route + StencilInkFromFill process-only ink, CmykPageRenderer empty-Names gate); the retired baseline pin's replacement (`ReservedSeparation_Unregistered_AppliesTheProcessColourantDirectly`); and the recorded residuals: **(a)** Indexed images over an all-reserved base still flatten (Task 4 scope note), **(b)** the stencil fix requires the spot-plane buffer configuration (`spots`/`registry` passed — the standard soft-proof path), **(c)** shading pin level (render-level or engine-level, per what Task 7 actually did).
2. **Row 4-11:** availability rule rewritten — available = registered in the page inventory OR a reserved process name; ⚠️ → ✅ if the row's remaining caveats are all closed by this pass, else keep ⚠️ with the residuals named. Cite the new pins.
3. **Row 4-5 cell:** resolve the G-14 pointer ("direct-application half" now done), cite the replacement pin.
4. **G-13 entry and row 5-6 cells:** check for sentences invalidated by this pass (G-13 reasons about `StencilInkFromFill` routing — its "spot-ink path" characterisation now has a process-only variant; append one sentence noting it rather than rewriting).
5. **Spec cross-reference:** add the spec + this plan to whatever changelog convention the matrix header uses.

- [ ] **Step 2: Full final verification, both repos**

- Engine: `dotnet test` → 2692/0, 0 warnings, net8/9/10; `git status` clean.
- Pellucid: `dotnet test Pellucid.Rendering.Avalonia.Tests` → full green (expect 1319 + 6 new = 1325 ± the Task 7 shading-pin placement); gates GWG 51/51, NChannel 3/3.
- Spot-check for strays: no `G14CensusProbe.cs`, nothing in scratchpad left behind, `git status` clean in both repos except intended commits.

- [ ] **Step 3: Commit docs, push both repos**

```powershell
cd C:\Users\jorda\RiderProjects\PDF
git add Docs/colour/rendering-conformance.md
git commit -m @'
docs(colour): close G-14 — reserved-name direct application landed in every painting context

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
git push
cd C:\Users\jorda\RiderProjects\Pellucid
git push
```

---

## Self-Review (completed)

- **Spec coverage:** rule → Tasks 2-5 + 7; per-context sites incl. meshes (Task 3 via MeshShadingReader) — ✓; census + predictions → Task 1/8; registry guard → Task 1 Step 1; baseline-pin retirement → Task 2 Step 1; mutation rule → Task 2 Step 6, Task 7 Step 5, red-first everywhere else; gates oracle rule → Task 8; matrix close-out incl. cross-reference staleness → Task 9; pack traps → Task 6. Shading escape hatch (spec §3: "sizeable sub-system → explicitly-scoped follow-on") is narrowed here: measurement showed the mapper site is small (Task 3), so the escape hatch was not needed; the RENDER-level shading pin retains a bounded fallback to an engine-level pin (Task 7 Step 1), recorded in the matrix either way.
- **Placeholder scan:** no TBDs; the two "if the API differs, use the real name" notes (Task 1 probe, Task 2 stroke state) are measured-adjustment instructions with the expected names stated, not deferrals.
- **Type consistency:** `AllReservedProcessOrNone` — engine internal static on `ColorSpaceResolver` (Tasks 3/4/5), Pellucid private static on `InkDecider` (Task 2), deliberate duplication per spec; `ReservedChannelOf` returns `int?`; `SpotImageInk(Names, TintPlanes, ProcessCmyk)` construction order matches `TryToSpotInk`'s existing call; empty-Names contract stated identically in Task 5 (producer) and Task 7 (consumer).
