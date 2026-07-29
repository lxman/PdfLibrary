# Release-Readiness Pass: Colour-Gap Hooks + 2.5.2 Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a measured baseline pin (or observation test) on every open colour gap (G-8 … G-13), fix the one trivial diagnostic gap (PageColorantReader's silent catch), bring the matrix / CHANGELOG / README current, and cut the `Lxman.PdfLibrary` 2.5.2 release — per the user's ruling: "present status is ready for release as long as the hooks are in place to resolve the remaining issues."

**Architecture:** Test-only except for two tiny production touches (a resolve-call counter for G-12; a log line in PageColorantReader). Every pin asserts *today's measured behaviour* with a comment naming the ruled goal — it catches interim drift, and the eventual fix must flip it red and retire it deliberately (the G-14 baseline-pin pattern, proven twice). All pins live in `PdfLibrary.Tests` (engine repo) and run in CI on both platforms. No Pellucid changes.

**Tech Stack:** .NET 10, xunit.v3 (implicit `using Xunit;` — do NOT add it), SkiaSharp render harness (`ColourConformancePage`), Keep-a-Changelog, GitHub Actions release workflow (`release: published` → publish-nuget.yml; package version comes from the tag name).

## Global Constraints

- **Repo:** everything in this plan is in `C:\Users\jorda\RiderProjects\PDF` (branch off `master`). Suggested branch: `colour/release-hooks-2.5.2`.
- **Version = `2.5.2`** (patch: behaviour fixes + internal changes only, no new public API since 2.5.1). Tag = `v2.5.2`. The user may override to 2.6.0 at the release gate — nothing before Task 9 depends on the number.
- **Baseline-pin discipline:** each pin predicts the current value. Run it; if it FAILS, the prediction was wrong — **STOP**, report the measured value to the orchestrator, and only then correct the assertion to the measured value (measurement, not accommodation). If the measured *shape* differs from the gap entry's description (e.g. something already paints nothing), STOP outright — that is a matrix defect, not a pin tweak.
- **Pin naming:** baseline pins end in `_G<N>Baseline`; their comment states the gap ID, current behaviour, and the ruled goal that will flip them.
- **`ColourConformancePage.Build` trap:** `extraResources` sits between `withFont` and `params extraObjects` — always pass `withFont:` and `extraResources:` **by name**. Extra objects are numbered **from 5** (`5 0 R` is the first).
- **Test gate:** `dotnet test PdfLibrary.Tests` filter `Category!=LocalOnly` must stay green; run the full suite once after Task 7.
- **Ask before pushing; the GitHub release cut (Task 10) is user-gated and irreversible.**
- **NEVER `git add -A`.** Stage files explicitly.

## File Structure

- `PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs` — G-9 unit pins (existing file; helpers `Image`, `Separation` already there).
- `PdfLibrary.Tests/Rendering/ColourGapBaselineTests.cs` — **new**: render-level baseline pins G-8, G-10, and the G-13 observation test (one file: they share the `ColourConformancePage` idiom and all trace to matrix gap entries).
- `PdfLibrary.Tests/Rendering/InitialColorValueTests.cs` — G-11 pin (this file is the row 4-4 initial-colour home; the Pattern case is its documented hole).
- `PdfLibrary.Tests/Rendering/ColorSpaceResolveCountTests.cs` — **new**: G-12 counted-resolve pin.
- `PdfLibrary/Rendering/ColorSpaceResolver.cs` + `PdfLibrary/Rendering/PdfRenderer.cs` — G-12 counter (internal, two lines + one accessor).
- `PdfLibrary/Document/PageColorantReader.cs` — log line in the defensive catch.
- `Docs/colour/rendering-conformance.md`, `CHANGELOG.md`, `README.md`, `PdfLibrary/PdfLibrary.csproj` — docs + version.

---

### Task 1: G-9 unit pins + G-14 Indexed-residual pin

**Files:**
- Modify: `PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs` (append to the class; helpers `Image(PdfObject, byte[], int, int, int)` at :16 and `Separation(string)` at :228 already exist)

**Interfaces:**
- Consumes: `PdfImageToCmyk.TryToSpotInk(PdfImage, PdfDocument?, out int, out int)` (public), `PdfImageToCmyk.StencilInkFromFill(ColorantOrigin, int, int)` (internal, IVT), `ColorantOrigin(["All"], [0.7], "DeviceCMYK")`.
- Produces: pin names `All_image_gets_no_spot_ink_G9Baseline`, `All_stencil_fill_gets_no_ink_G9Baseline`, `Indexed_over_reserved_base_still_declines_G14ResidualBaseline` (Task 6 cites them in the matrix).

- [ ] **Step 1: Write the three pins**

Append inside `PdfImageToCmykTests`:

```csharp
    // G-9 BASELINE (see Docs/colour/rendering-conformance.md, G-9): an /All image gets correct
    // process plates but NO spot planes — TryToSpotInk routes a colourant to a plane only when
    // Classify(name) == Spot, and Classify("All") is ColorantKind.All, so spotNames stays empty
    // and the whole call declines. The ruled goal (rows 4-6/4-9, §8.6.6.4) is that /All paints
    // ALL colourants — process plates AND every open spot plane — matching the fill path's
    // AllColourants arm. The fix must flip this pin red and retire it deliberately.
    [Fact]
    public void All_image_gets_no_spot_ink_G9Baseline()
    {
        PdfImage img = Image(Separation("All"), [255, 128], 2, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

        Assert.Null(ink);
    }

    // G-9 BASELINE, stencil half: an /All fill behind a stencil produces no ink either —
    // StencilInkFromFill has the same Classify(name) == Spot gate, and "All" is not reserved
    // process, so the all-reserved G-14 arm declines too. The stencil then takes the RGBA path
    // with ResolvedFillColor's ADDITIVE complement baked in — a different colour than the same
    // tint painted as a fill on the same CMYK page (the divergence the G-9 entry records).
    [Fact]
    public void All_stencil_fill_gets_no_ink_G9Baseline()
    {
        var origin = new ColorantOrigin(["All"], [0.7], "DeviceCMYK");

        Assert.Null(PdfImageToCmyk.StencilInkFromFill(origin, 2, 2));
    }

    // G-14 RESIDUAL (a) BASELINE (rendering-conformance.md, G-14 residuals): the reserved-direct
    // image route covers a DIRECTLY-named reserved Separation/DeviceN driving image ink — not an
    // Indexed palette whose BASE resolves to one. An Indexed-over-[/Separation /Cyan …] image
    // declines both CMYK routes and flattens through the RGBA/ICC path. The ruled goal is the
    // same "Adobe or better" direct application G-14 delivered for the direct case; extending it
    // to Indexed must flip this pin red and retire it deliberately. The /Lookup placeholder is
    // never consulted on the decline path — if this test ever throws or passes differently, the
    // route has started reading Indexed entries: STOP and report.
    [Fact]
    public void Indexed_over_reserved_base_still_declines_G14ResidualBaseline()
    {
        PdfArray indexed = new(new PdfName("Indexed"), Separation("Cyan"),
            new PdfInteger(1), new PdfName("Lookup"));
        PdfImage img = Image(indexed, [0, 1], 2, 1);

        Assert.Null(PdfImageToCmyk.TryToCmyk(img, null, out _, out _));
        Assert.Null(PdfImageToCmyk.TryToSpotInk(img, null, out _, out _));
    }
```

- [ ] **Step 2: Run them — expected GREEN (they pin current behaviour)**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~G9Baseline|FullyQualifiedName~G14ResidualBaseline" -v minimal`
Expected: 3 passed. If any fails, STOP per Global Constraints (the gap entry's mechanism description would be wrong).

- [ ] **Step 3: Commit**

```bash
git add PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs
git commit -m "test(colour): G-9 + G-14-Indexed-residual baseline pins on the image ink routes"
```

---

### Task 2: G-8 + G-10 render pins and the G-13 observation test

**Files:**
- Create: `PdfLibrary.Tests/Rendering/ColourGapBaselineTests.cs`

**Interfaces:**
- Consumes: `ColourConformancePage.Build(string colorSpaceDef, string content, bool withFont, string extraResources, params string[] extraObjects)`, `.RenderCentre(byte[]) → SKColor`, `.ForEachPixelInRect(byte[], Action<int,int,SKColor>)`, `.ExponentialTint(string, string)` (all in `PdfLibrary.Tests.Rendering`, internal static).
- Produces: pin names `NoneShadingPattern_paints_G8Baseline`, `Mode4NoneText_establishes_no_clip_G10Baseline`, observation `Stencil_after_bare_cs_takes_the_initial_tint_G13`.

- [ ] **Step 1: Write the file**

```csharp
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Baseline pins for the open colour-gap entries G-8 and G-10, plus the G-13 observation test —
/// see Docs/colour/rendering-conformance.md. Each baseline asserts TODAY'S measured behaviour;
/// the comment names the ruled goal, and the eventual fix must flip the pin red and retire it
/// deliberately (the G-14 pattern).
/// </summary>
public class ColourGapBaselineTests
{
    // G-8 BASELINE: a shading used as a PATTERN (PatternType 2 via scn) does not consult
    // PaintsNothing — only OnFill's fill-space gate does, and the FILL space here is /Pattern,
    // not /None. The shading's own [/Separation /None ...] colour space paints through
    // ShadingBuilder anyway. Tint transform is CONSTANT black so the current behaviour has one
    // predictable value. Ruled goal (§8.6.6.4, G-7's rule extended to the pattern route): a
    // /None shading paints nothing and the red backdrop survives.
    [Fact]
    public void NoneShadingPattern_paints_G8Baseline()
    {
        // Objects from 5: pattern → shading → shading function (t → tint, 1-out, constant 1.0)
        // → Separation tint transform (tint → RGB, 3-out, constant black). The two functions are
        // DISTINCT on purpose: the shading /Function outputs the space's 1 tint component; the
        // Separation's element-3 transform outputs the alternate's 3.
        const string pattern = "<< /Type /Pattern /PatternType 2 /Matrix [1 0 0 1 0 0] /Shading 6 0 R >>";
        const string shading = "<< /ShadingType 2 /ColorSpace [/Separation /None /DeviceRGB 8 0 R] " +
                               "/Coords [100 500 300 500] /Domain [0 1] /Extend [true true] /Function 7 0 R >>";
        const string shadingFn = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [1] /N 1 >>";
        const string tint = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [0 0 0] /N 1 >>";
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Pattern cs /P1 scn 100 400 200 200 re f";

        byte[] pdf = ColourConformancePage.Build("/DeviceRGB", content, withFont: false,
            extraResources: " /Pattern << /P1 5 0 R >>", pattern, shading, shadingFn, tint);

        SKColor c = ColourConformancePage.RenderCentre(pdf);
        Assert.True(c.Red < 25 && c.Green < 25 && c.Blue < 25,
            $"G-8 baseline moved: /None shading pattern painted RGB({c.Red},{c.Green},{c.Blue}), " +
            "expected the tint's constant black. If it now leaves the red backdrop, G-8 is FIXED — " +
            "retire this pin deliberately and update the matrix.");
    }

    // G-10 BASELINE: TextPaintsNothing masks RenderingMode with & 3, so mode 4 (fill + add to
    // clip) with a /None fill skips the entire glyph render — INCLUDING the add-to-clip half.
    // The blue fill painted after ET therefore covers the whole rect unclipped. Ruled goal
    // (row 4-8's clause, G-10): /None suppresses the FILL only; mode 4 must still establish the
    // glyph clip, so the trailing blue would land only inside the glyph outlines.
    [Fact]
    public void Mode4NoneText_establishes_no_clip_G10Baseline()
    {
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Cs0 cs 1 scn BT /F1 48 Tf 4 Tr 110 480 Td (NONE) Tj ET " +
                               "0 0 1 rg 100 400 200 200 re f";
        byte[] pdf = ColourConformancePage.Build(
            $"[/Separation /None /DeviceRGB {ColourConformancePage.ExponentialTint("1 1 1", "0 0 0")}]",
            content, withFont: true);

        ColourConformancePage.ForEachPixelInRect(pdf, (x, y, c) =>
            Assert.True(c.Blue > 235 && c.Red < 20,
                $"G-10 baseline moved at ({x},{y}): RGB({c.Red},{c.Green},{c.Blue}) is not the " +
                "unclipped blue. If red now survives outside glyph shapes, the mode-4 clip is " +
                "IMPLEMENTED — retire this pin deliberately and update the matrix."));
    }

    // G-13 OBSERVATION (not a limitation pin — this is the missing fixture): a stencil mask
    // painted immediately after a bare `cs` (no scn) must take the colour space's INITIAL
    // colour, exactly as a fill would. Separation initial tint is 1.0; the tint ramps
    // white -> black, so the stencil paints black over the red backdrop. GREEN converts the
    // matrix's "reasoned about, only" into "observed". If this FAILS, STOP - that is a real
    // routing bug, not a baseline to record.
    [Fact]
    public void Stencil_after_bare_cs_takes_the_initial_tint_G13()
    {
        const string img = "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
                           "/ImageMask true /BitsPerComponent 1 /Length 2 >>\r\nstream\r\n" +
                           "\u0000\u0000\r\nendstream";
        const string content = "1 0 0 rg 100 400 200 200 re f " +
                               "/Cs0 cs q 200 0 0 200 100 400 cm /Im0 Do Q";

        byte[] pdf = ColourConformancePage.Build(
            $"[/Separation /Spot /DeviceRGB {ColourConformancePage.ExponentialTint("1 1 1", "0 0 0")}]",
            content, withFont: false, extraResources: " /XObject << /Im0 5 0 R >>", img);

        SKColor c = ColourConformancePage.RenderCentre(pdf);
        Assert.True(c.Red < 25 && c.Green < 25 && c.Blue < 25,
            $"stencil after bare cs painted RGB({c.Red},{c.Green},{c.Blue}); expected the initial " +
            "tint 1.0 = black. Red means the stencil did not pick up the initial colour a fill gets.");
    }
}
```

- [ ] **Step 2: Run — expected GREEN all three**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourGapBaselineTests" -v minimal`
Expected: 3 passed.
- `G8Baseline` FAIL → STOP, report the measured RGB (a non-black paint means the shading resolved differently than predicted — correct the assertion to the measured value only if it still *paints*; if the backdrop survives, the gap entry is wrong).
- `G10Baseline` FAIL → STOP, report which pixels are not blue.
- `G13` FAIL → STOP unconditionally — a real bug, escalate to the orchestrator; do NOT convert it into a baseline pin.

- [ ] **Step 3: Commit**

```bash
git add PdfLibrary.Tests/Rendering/ColourGapBaselineTests.cs
git commit -m "test(colour): G-8/G-10 baseline pins + G-13 stencil-after-bare-cs observation"
```

---

### Task 3: G-11 pin — Pattern initial colour carries over

**Files:**
- Modify: `PdfLibrary.Tests/Rendering/InitialColorValueTests.cs` (append; this file already holds the six row 4-4 initial-colour tests and has no Pattern case)

**Interfaces:**
- Consumes: `ColourConformancePage.Build` / `.RenderCentre` as above.
- Produces: pin name `Pattern_without_scn_carries_over_previous_colour_G11Baseline`.

- [ ] **Step 1: Write the pin**

Append inside `InitialColorValueTests`:

```csharp
    // G-11 BASELINE: InitialColorFor("Pattern") returns null, and OnColorSpaceChanged treats
    // null as "leave the current colour alone" — so a fill after `/Pattern cs` with no scn
    // paints the PREVIOUS space's colour (the red set by rg). Ruled goal (§8.6.8 Table 73):
    // the initial colour of a Pattern space is "a pattern object that causes nothing to be
    // painted", so this fill should leave the page untouched (white here). The fix must flip
    // this pin red and retire it deliberately.
    [Fact]
    public void Pattern_without_scn_carries_over_previous_colour_G11Baseline()
    {
        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB", "1 0 0 rg /Pattern cs 100 400 200 200 re f"));

        Assert.True(c.Red > 235 && c.Green < 20 && c.Blue < 20,
            $"G-11 baseline moved: fill after bare /Pattern cs painted RGB({c.Red},{c.Green},{c.Blue}), " +
            "not the carried-over red. If the page is now untouched, G-11 is FIXED — retire this " +
            "pin deliberately and update the matrix.");
    }
```

- [ ] **Step 2: Run — expected GREEN**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~G11Baseline" -v minimal`
Expected: 1 passed. FAIL → STOP and report the measured RGB.

- [ ] **Step 3: Commit**

```bash
git add PdfLibrary.Tests/Rendering/InitialColorValueTests.cs
git commit -m "test(colour): G-11 baseline pin - bare /Pattern cs carries over the previous colour"
```

---

### Task 4: G-12 hook — counted colour-space resolves per `cs`+`sc`

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` (instance counter)
- Modify: `PdfLibrary/Rendering/PdfRenderer.cs` (internal accessor; `_colorSpaceResolver` field is at :35)
- Create: `PdfLibrary.Tests/Rendering/ColorSpaceResolveCountTests.cs`

**Interfaces:**
- Consumes: `ColorSpaceResolver.ResolveColorSpace(ref string?, ref List<double>?, PdfDictionary?, bool, string?)` at ColorSpaceResolver.cs:28; `MockRenderTarget` (PdfLibrary.Tests.Rendering); operator classes `SetFillColorSpaceOperator(PdfName)` / `SetFillColorOperator(List<PdfObject>)` (namespace `PdfLibrary.Content.Operators`, internal, IVT); `PdfName`/`PdfReal` (`PdfLibrary.Core.Primitives`, internal).
- Produces: `internal int ColorSpaceResolver.ResolveCallCount` (incremented once per `ResolveColorSpace` entry); `internal int PdfRenderer.ColorSpaceResolveCount`; pin name `Cs_then_sc_resolves_four_times_G12Baseline`.

- [ ] **Step 1: Add the counter to `ColorSpaceResolver`**

In `ColorSpaceResolver.cs`, add a property to the class body and an increment as the FIRST statement of `ResolveColorSpace` (line 28's method):

```csharp
    /// <summary>
    /// Diagnostic counter for the G-12 throughput hook (Docs/colour/rendering-conformance.md):
    /// incremented once per ResolveColorSpace entry, read by PdfRenderer.ColorSpaceResolveCount.
    /// One resolver instance per PdfRenderer, so no thread-safety needed.
    /// </summary>
    internal int ResolveCallCount { get; private set; }
```

and at the top of `ResolveColorSpace`:

```csharp
        ResolveCallCount++;
```

- [ ] **Step 2: Add the accessor to `PdfRenderer`**

Near the `_colorSpaceResolver` field (PdfRenderer.cs:35), add:

```csharp
    /// <summary>Total ResolveColorSpace calls this renderer has made — the G-12 hook's observable.</summary>
    internal int ColorSpaceResolveCount => _colorSpaceResolver.ResolveCallCount;
```

- [ ] **Step 3: Write the failing-then-measured pin**

Create `PdfLibrary.Tests/Rendering/ColorSpaceResolveCountTests.cs`:

```csharp
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class ColorSpaceResolveCountTests
{
    // G-12 BASELINE (Docs/colour/rendering-conformance.md): fixing row 4-4 made `cs` run the
    // whole of OnColorChanged (which resolves BOTH fill and stroke), and `sc`/`scn` runs it
    // again — so one cs + one sc costs FOUR ResolveColorSpace passes, each of which re-parses
    // any tint transform via the uncached PdfFunction.Create. This pin is the throughput hook:
    // the de-duplication design the G-12 entry calls for (caching a parsed tint transform per
    // colour-space resource, or splitting fill/stroke resolution) must LOWER this number and
    // deliberately retire this pin with the new count.
    [Fact]
    public void Cs_then_sc_resolves_four_times_G12Baseline()
    {
        var renderer = new PdfRenderer(new MockRenderTarget());

        renderer.ProcessOperators(new List<PdfOperator>
        {
            new SetFillColorSpaceOperator(new PdfName("DeviceRGB")),
            new SetFillColorOperator([new PdfReal(1), new PdfReal(0), new PdfReal(0)]),
        });

        Assert.Equal(4, renderer.ColorSpaceResolveCount);
    }
}
```

- [ ] **Step 4: Run — expected GREEN at 4**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~G12Baseline" -v minimal`
Expected: 1 passed with count 4 (cs → OnColorSpaceChanged → OnColorChanged resolves fill+stroke = 2; sc → OnColorChanged = 2 more). If the count differs, STOP, report the measured number, then set the assertion to the measured value and record it in Task 8's matrix text — the pin's value is the measurement.

- [ ] **Step 5: Full-suite sanity (production file touched)**

Run: `dotnet test PdfLibrary.Tests --filter "Category!=LocalOnly" -v minimal`
Expected: all green (the counter is add-only; nothing else can move).

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary/Rendering/PdfRenderer.cs PdfLibrary.Tests/Rendering/ColorSpaceResolveCountTests.cs
git commit -m "test(colour): G-12 hook - count ResolveColorSpace calls, pin cs+sc at 4"
```

---

### Task 5: PageColorantReader — log the defensive catch

**Files:**
- Modify: `PdfLibrary/Document/PageColorantReader.cs` (the bare `catch` at :34)

**Interfaces:**
- Consumes: `PdfLogger.Log(LogCategory.Graphics, string)` (`using Logging;` — same call shape as PdfRenderer.cs:681).
- Produces: nothing downstream; diagnostics only.

No test: the contract (never throw, return partial) is already pinned by existing PageColorantReader tests; the log sink is a Serilog file configured at app level, and asserting on it would test Serilog, not this class. This task is the "fix it, cheaper than hooking" item from the approved proposal.

- [ ] **Step 1: Make the edit**

Change:

```csharp
        catch
        {
            \ Defensive: a malformed resource graph must never throw out of the public GetPageColorants
            // (spec "Guards and stability" contract). Return whatever was collected before the fault.
        }
```

to:

```csharp
        catch (Exception ex)
        {
            // Defensive: a malformed resource graph must never throw out of the public GetPageColorants
            // (spec "Guards and stability" contract). Return whatever was collected before the fault —
            // but say so, or a truncated inventory is indistinguishable from a complete one.
            PdfLogger.Log(LogCategory.Graphics,
                $"GetPageColorants: resource walk faulted ({ex.GetType().Name}: {ex.Message}); returning partial inventory");
        }
```

(Note: the existing comment's first line currently starts with a stray `\ ` instead of `// ` — fix that while here. Add `using Logging;` if the file lacks it.)

- [ ] **Step 2: Build + run the reader's tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~PageColorant" -v minimal`
Expected: all green.

- [ ] **Step 3: Commit**

```bash
git add PdfLibrary/Document/PageColorantReader.cs
git commit -m "fix(colour): log PageColorantReader's defensive catch instead of swallowing silently"
```

---

### Task 6: Matrix — record every hook

**Files:**
- Modify: `Docs/colour/rendering-conformance.md` (gap entries G-8 :514, G-9 :517, G-10 :536, G-11 :545, G-12 :555, G-13 :566; delta notes block near :187)

**Interfaces:**
- Consumes: the pin names and measured values from Tasks 1-4 (if any STOP corrected a value, use the corrected one).
- Produces: the matrix as the single tracker each future fix starts from.

- [ ] **Step 1: Append a pin line to each gap entry**

Add to the END of each entry (keep existing text untouched):

- G-8: `**Pinned 2026-07-29:** \`NoneShadingPattern_paints_G8Baseline\` (ColourGapBaselineTests) asserts the constant-black paint; the fix flips it red.`
- G-9: `**Pinned 2026-07-29:** \`All_image_gets_no_spot_ink_G9Baseline\` + \`All_stencil_fill_gets_no_ink_G9Baseline\` (PdfImageToCmykTests) pin both decline sites.`
- G-10: `**Pinned 2026-07-29:** \`Mode4NoneText_establishes_no_clip_G10Baseline\` (ColourGapBaselineTests) asserts the trailing fill lands unclipped across the whole rect.`
- G-11: `**Pinned 2026-07-29:** \`Pattern_without_scn_carries_over_previous_colour_G11Baseline\` (InitialColorValueTests) asserts the carried-over red.`
- G-12: `**Hooked 2026-07-29:** \`Cs_then_sc_resolves_four_times_G12Baseline\` (ColorSpaceResolveCountTests) pins one cs+sc at 4 ResolveColorSpace passes via the new \`ColorSpaceResolver.ResolveCallCount\` counter; the de-dup design must lower it.` (substitute the measured count if Task 4 corrected it)
- G-13: `**Observed 2026-07-29:** \`Stencil_after_bare_cs_takes_the_initial_tint_G13\` (ColourGapBaselineTests) — the initial tint 1.0 renders black through the stencil; "reasoned about, only" no longer applies. The row 4-4 note's untested-combination caveat is closed.`

Also:
- G-14 residual (a): append `**Pinned 2026-07-29:** \`Indexed_over_reserved_base_still_declines_G14ResidualBaseline\` (PdfImageToCmykTests).`
- Correct the G-10 entry's stale line reference `PdfRenderer.cs:1266` → `:1305`.

- [ ] **Step 2: Audit the 5-3 / 5-10 residuals and G-14 residuals (b)/(c)**

Read the 5-3 and 5-10 matrix cells and the G-14 residual (b) (stencil with no spot-plane configuration) and (c) (engine-level-only shading pin) entries. For each, confirm the entry either names a pin/test or states an explicit reason it is unpinned. Where an entry is silent, add a one-line `**Hook status:**` note stating which existing test is nearest and why no new pin was added in this pass (e.g. residual (b)'s observable needs a Pellucid render harness without spot buffers — out of this engine-only pass's scope). Do NOT write new tests in this task; if the audit finds a residual that both lacks a pin and is cheaply pinnable engine-side, STOP and report it to the orchestrator instead of scope-creeping.

- [ ] **Step 3: Add a delta note**

After the "Delta 2026-07-29 (G-14 close-out)" block, add:

```markdown
> **Delta 2026-07-29 (release hooks).** Every open gap now carries a measured hook: G-8, G-10,
> G-11 baseline pins; G-9 unit pins on both decline sites; G-12 a counted-resolve pin (4 per
> cs+sc) via `ColorSpaceResolver.ResolveCallCount`; G-13 observed green (no longer
> reasoned-only). `PageColorantReader`'s defensive catch now logs. A future fix for any of these
> starts by flipping its pin red — none can land half-done or unnoticed.
```

(Extend the note with the G-14 Indexed-residual pin and any Hook-status findings from Step 2.)

- [ ] **Step 4: Commit**

```bash
git add Docs/colour/rendering-conformance.md
git commit -m "docs(colour): record release-hook pins for G-8..G-13; PageColorantReader logging"
```

---

### Task 7: Full-suite verification on the branch

**Files:** none (verification gate).

- [ ] **Step 1: Full test run**

Run: `dotnet test PdfLibrary.Tests --filter "Category!=LocalOnly" -v minimal`
Expected: all green. Any failure → STOP, report; do not proceed to docs/release tasks on a red suite.

---

### Task 8: CHANGELOG 2.5.2 section

**Files:**
- Modify: `CHANGELOG.md` (the `## [Unreleased]` heading at line 7 and the version-history summary table if present near the bottom)

**Interfaces:**
- Produces: the dated `## [2.5.2]` section Task 10's GitHub release notes reference.

- [ ] **Step 1: Insert the 2.5.2 section**

Replace `## [Unreleased]` (line 7, currently empty) with:

```markdown
## [Unreleased]

## [2.5.2] - 2026-07-29

### Fixed

- **Reserved process-name Separations/DeviceN now apply their colourant directly on the CMYK
  soft-proof path** — an unregistered `[/Separation /Cyan …]` (or an all-reserved DeviceN) paints
  its process plate directly, ignoring the alternate's tint transform, in every painting context:
  fills, strokes, shadings, images, and stencil masks (ISO 32000-2 §8.6.6.4; matches Adobe).
  Previously such spaces flattened through their alternate, so a lying alternate painted the wrong
  plate. `/None` components in such spaces still paint nothing. The RGB rendering path is
  unchanged (it correctly keeps reverting through the alternate).
- **Atomic file saves retry transient Windows locks** — the final rename in `PdfDocument.Save`
  (and all atomic writes) now retries with backoff when antivirus/Search-indexer scans transiently
  hold the destination (`IOException`/`UnauthorizedAccessException`), instead of failing the save.
  Persistent locks still throw after the retry budget.
- **`GetPageColorants` logs when a malformed resource graph truncates the inventory** instead of
  swallowing the fault silently (the partial-result contract is unchanged).

### Known limitations (colour rendering)

Tracked, each pinned by a baseline test so no fix can land unnoticed — see
`Docs/colour/rendering-conformance.md` (gap entries G-8 … G-13) for mechanisms and pins:
`/None` shadings used as *patterns* still paint; `/All` images/stencils get no spot planes on the
CMYK path; a `/None` fill in text mode 4 drops the add-to-clip half; a bare `/Pattern cs` carries
over the previous colour instead of painting nothing; `cs`+`sc` costs 4 colour-space resolves
(uncached tint-transform parse); Indexed images over all-reserved bases still flatten.
```

(If any Task 1-4 STOP changed a measured value, adjust the wording accordingly.)

- [ ] **Step 2: Update the version-history summary table** (if the file has one, as 2.5.0's did near :477): add a `2.5.2 | 2026-07-29 | Reserved-name direct colour, save-retry, colour-gap pins` row.

- [ ] **Step 3: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs: CHANGELOG 2.5.2 - reserved-name direct colour, save retry, known colour limitations"
```

---

### Task 9: README known-limitations section + version bump

**Files:**
- Modify: `README.md` (add one section after `## Supported PDF Features`' subsections, before `## License`)
- Modify: `PdfLibrary/PdfLibrary.csproj` (`<Version>2.5.1</Version>` at :11 → `2.5.2`)

**Interfaces:**
- Consumes: the CHANGELOG limitations text (Task 8) — keep the two consistent.
- Produces: release-ready docs; the csproj version Task 10's pack uses locally (the CI package version comes from the tag, but the csproj must not lag it).

- [ ] **Step 1: Add the README section**

Insert before `## License`:

```markdown
## Known Limitations

Colour rendering: a small set of edge-case gaps is tracked in
[`Docs/colour/rendering-conformance.md`](Docs/colour/rendering-conformance.md) (entries G-8 … G-13),
each pinned by a baseline test that a future fix must deliberately flip. The notable ones:
`/None` shadings used as fill *patterns* still paint; `/All` images and stencil masks do not
receive spot planes on the CMYK soft-proof path; text rendering mode 4 with a `/None` fill drops
the add-to-clip half; a bare `/Pattern cs` (no `scn`) carries the previous colour over instead of
painting nothing. General-purpose RGB rendering is unaffected by all of these.
```

- [ ] **Step 2: Bump the version**

In `PdfLibrary/PdfLibrary.csproj` change `<Version>2.5.1</Version>` → `<Version>2.5.2</Version>`.

- [ ] **Step 3: Build sanity**

Run: `dotnet build PdfLibrary -c Release -v minimal`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add README.md PdfLibrary/PdfLibrary.csproj
git commit -m "docs: README known-limitations section; bump version to 2.5.2"
```

---

### Task 10: Merge, push, and cut the release (USER-GATED)

**Files:** none (git/GitHub mechanics).

- [ ] **Step 1: Merge to master**

```bash
git checkout master
git merge --no-ff colour/release-hooks-2.5.2 -m "Merge colour/release-hooks-2.5.2: colour-gap pins, 2.5.2 docs"
```

- [ ] **Step 2: Verify tests on the merged result**

Run: `dotnet test PdfLibrary.Tests --filter "Category!=LocalOnly" -v minimal`
Expected: all green.

- [ ] **Step 3: ASK THE USER before pushing** (standing rule). On approval:

```bash
git push origin master
```

Then confirm CI green on both platforms before the next step.

- [ ] **Step 4: ASK THE USER before cutting the release** (irreversible — NuGet packages can only be unlisted). Confirm the version number (2.5.2 vs a 2.6.0 override) at the same time. On approval:

```bash
gh release create v2.5.2 --title "v2.5.2" --notes-file <notes>
```

where `<notes>` is the `## [2.5.2]` CHANGELOG section body extracted to a temp file in the scratchpad. The `release: published` event triggers `.github/workflows/publish-nuget.yml`, which runs the test gate, packs, and pushes the packages with the tag's version.

- [ ] **Step 5: Verify the publish workflow succeeded**

Run: `gh run list --workflow publish-nuget.yml --limit 1`
Expected: completed / success. Report the nuget.org package URL.
