# Colour Pass 2a′ — NChannel Engine Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the engine's NChannel per-component data *correct* — accept an ICCBased CMYK/Gray process space, build each component's tint ramp from its own `/Colorants` Separation, and stop the page colorant inventory registering a named process colorant as a spot plane — while changing no rendered output.

**Architecture:** Three independent corrections to code Pass 2a already shipped. `ProcessSpaceName` (returns a family-name string, which is what forces the ICCBased rejection) becomes `ProcessChannelCount` (returns the channel count, the question the caller actually has). `BuildTintRamp` prefers `/Attributes /Colorants /<name>` over zeroing the other inputs of the whole-space transform. `PageColorantReader` classifies via the per-component `Role` that Pass 2a already computes, instead of by name alone. Nothing new is consumed; Pass 2b consumes it.

**Tech Stack:** C# / .NET, xUnit, `PdfLibrary` (engine). Verification crosses into Pellucid once, in Task 4.

**Design:** `Docs/superpowers/specs/2026-07-26-colour-pass2-nchannel-per-component-design.md` (commit `2c427c2`).

---

## Global Constraints

- Repo: `C:\Users\jorda\RiderProjects\PDF` for Tasks 0–3. Task 4 also touches `C:\Users\jorda\RiderProjects\Pellucid` and `C:\Users\jorda\PDFs\PdfCompare`.
- **This pass changes no rendered output.** Nothing consumes the corrections. The Pellucid corpus render-hash baseline must not move by a single digest — see the measurement in Task 0 that establishes why this is a *stronger* claim than the design originally predicted.
- Engine test baseline is **2591 passing, 0 failing** (at `master` `b4b9634`). Every task ends green. Never commit with a failing suite.
- **Nothing added here may throw out of the render path.** `OriginForColorSpaceObject` is called from `PdfRenderer.cs:1044-1047` on every colour-setting operator with no try/catch above it. `BuildTintRamp` is called from `PageColorantReader`, whose public entry point `PdfDocument.GetPageColorants` has the same must-never-throw contract (`ColorSpaceResolver.cs:546-556`).
- **Read `PdfStream.Dictionary` only — never `.Data` or `GetDecodedData()`.** The conformance fixture's ICC profile is 384 KB of Flate.
- `/N` is read for its channel count alone. Do not validate the profile and do not use it for colour conversion.
- **No caching** of built tint transforms. Still deferred (G-12).
- `ColourantComponent` and `ColorantOrigin` are public and cross the package boundary into Pellucid. Their existing members and positional constructors must keep compiling unchanged.

---

## File Structure

| File | Responsibility |
|---|---|
| `PdfLibrary/Rendering/ColorSpaceResolver.cs` | **Modify.** `ProcessSpaceName` → `ProcessChannelCount` (Task 1); `BuildTintRamp` prefers `/Colorants` (Task 2). |
| `PdfLibrary/Document/PageColorantReader.cs` | **Modify.** Classify by per-component `Role`, not by name alone (Task 3). |
| `PdfLibrary.Tests/Rendering/ColourantComponentTests.cs` | **Extend.** Task 1's degenerate-input rows. |
| `PdfLibrary.Tests/Rendering/NChannelRampTests.cs` | **Create.** Task 2's ramp-source tests. |
| `PdfLibrary.Tests/Document/PageColorantsTests.cs` | **Extend.** Task 3's classification tests. |

---

## The degenerate-input table

Pass 2a's post-mortem, restated: every review defect was in the plan text, and all of them were malformed- or degenerate-input cases the corpus cannot exercise. Each row below is a **required test**.

### `/Process /ColorSpace` → `ProcessChannelCount` (Task 1)

| Input | Required result | Why |
|---|---|---|
| `/ColorSpace` key absent | `4` | "No constraint" — preserves today's `""` behaviour exactly |
| present but not a name and not an array with a name head | `4` | Today's `_ => string.Empty`; degrade rather than reject |
| `/DeviceCMYK` | `4` | |
| `/DeviceGray` | `1` | |
| `/DeviceRGB`, `/Lab`, `/CalGray`, any other name | `null` → whole list suppressed | Unchanged from today. `/CalGray` is CIE-based, not a device space — deliberate, recorded as a gap |
| `[/ICCBased s]`, `s` resolves to a stream with `/N 4` | `4` | **The new case.** ISO 32000-1 EXAMPLE 5 |
| `[/ICCBased s]`, `/N 1` | `1` | |
| `[/ICCBased s]`, `/N 3` | `null` | Not reducible to plates here |
| `[/ICCBased s]`, `/N` absent or not a `PdfInteger` | `null` | We genuinely do not know the count |
| `[/ICCBased s]`, `s` unresolvable or not a stream | `null` | Preserves today exactly |
| `[/ICCBased]` with `Count < 2` | `null` | Malformed |
| **`[/ICCBased s]` where `s` is a CORRUPT indirect reference** | `null`, **no exception escapes** | **Axis B.** A new dereference of an object no path previously touched here |
| **`/N` is itself an indirect reference** | resolved | The established idiom at `ColorSpaceResolver.cs:714` derefs it; match it |

### `/Colorants` ramp source (Task 2)

| Input | Required result | Why |
|---|---|---|
| NChannel, `/Colorants /<name>` is a usable Separation | ramp from that Separation | Table 71: "the appearance of that colorant alone" |
| NChannel, `/Colorants` absent | ramp from today's isolated evaluation | Files lie; degrade to the status quo |
| NChannel, no `/Colorants` entry for this name | isolated evaluation | Same |
| NChannel, entry is not a Separation array | isolated evaluation | Same |
| NChannel, entry's alternate is not CMYK/Gray | isolated evaluation | `BuildTintToCmyk` cannot reduce it |
| NChannel, entry's tint transform **throws** on evaluate | isolated evaluation, no exception escapes | `PdfFunction.Create` can succeed and `Evaluate` still throw |
| **NChannel, entry is a CORRUPT indirect reference** | isolated evaluation, **no exception escapes** | **Axis B** |
| **Not** NChannel (`DeviceN`, absent, unknown subtype) | isolated evaluation — **byte-identical to today** | The 50 non-NChannel GWG files must not move |

### Classification (Task 3)

| Input | Required result | Why |
|---|---|---|
| NChannel, name listed in `/Process /Components` | `ColorantKind.Process` — **no spot plane** | It is a process colorant; `SpotColorantRegistry.Build` skips non-Spot |
| NChannel, name not listed, not reserved | `ColorantKind.Spot` | Unchanged |
| NChannel, component named `/None` | `ColorantKind.None` (skipped) | Unchanged |
| NChannel, component named `/All` | `ColorantKind.All` (skipped) | `RoleFor` maps `All` → `Spot`; the name-based `All` distinction must survive |
| **Not** NChannel — every DeviceN/Separation space | **byte-identical to today** | This is what keeps 50 GWG files still |

---

## Task 0: Measure before building

**Files:**
- Create then **delete**: `PdfLibrary.Tests/Rendering/TempPass2aPrimeMeasurement.cs`

**Interfaces:**
- Consumes: `ColorSpaceResolver.BuildTintRamp`, `SpotColorSpace.TryParse`, `PdfFunction.Create` (all `internal`; `PdfLibrary.Tests` has access).
- Produces: two recorded measurements that Tasks 2 and 4 depend on. **No production code.**

**Background:** the design predicts Pass 2a′ moves **zero** GWG digests, on two grounds that must be confirmed, not assumed.

1. GWG081's whole-space transforms (objects 50 and 58) are Type 4 PostScript of the form `out = 1 − Π(1 − tᵢ·kᵢ)`, with GWG Green's coefficients `0.5, 0.0, 1.0, 0.0`. Zeroing the other input collapses the product to `t·k`. Object 14 — GWG Green's own `/Colorants` Separation — is `FunctionType 2`, `C0 [0 0 0 0]`, `C1 [0.5 0 1 0]`, `N 1`, i.e. also `t·k` with the same coefficients. **They should agree exactly.** That was derived by reading the decompressed PostScript; this task confirms it numerically. If they disagree, Task 2 will move GWG081's digest and Task 4's expectation changes — report it rather than proceeding on the prediction.
2. Pass 2b will build a render-hash gate over three veraPDF files. If any of them cannot even be loaded by the engine, that scope shrinks. Cheaper to learn now.

- [ ] **Step 1: Write the measurement scaffold**

Create `PdfLibrary.Tests/Rendering/TempPass2aPrimeMeasurement.cs`:

```csharp
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Functions;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// THROWAWAY measurement scaffold for Pass 2a′ Task 0 — delete in Step 4 of this task. Records two
/// numbers the rest of the plan depends on; it asserts nothing beyond "the files opened".
/// </summary>
public class TempPass2aPrimeMeasurement(ITestOutputHelper output)
{
    private const string Gwg081 =
        @"C:\Users\jorda\RiderProjects\gwg-gos\Ghent_PDF_Output_Suite_V50_Patches\Categories\2-SPOT\Patches\GWG081_DeviceN-Support_5c_X1a.pdf";

    private const string VeraDir =
        @"C:\Users\jorda\RiderProjects\veraPDF-corpus\PDF_A-2b\6.2 Graphics\6.2.4.4 Separation and DeviceN colour spaces";

    private static readonly string[] VeraFiles =
    [
        "veraPDF test suite 6-2-4-4-t02-pass-a.pdf",
        "veraPDF test suite 6-2-4-4-t03-fail-c.pdf",
        "veraPDF test suite 6-2-4-4-t03-fail-d.pdf",
    ];

    [Fact]
    public void Measure()
    {
        MeasureRampEquivalence();
        MeasureVeraLoadability();
    }

    /// <summary>For every NChannel space in GWG081, compare each spot component's isolated ramp
    /// (today's BuildTintRamp) against its /Colorants Separation evaluated over the same 256 steps.</summary>
    private void MeasureRampEquivalence()
    {
        using FileStream fs = File.OpenRead(Gwg081);
        using PdfDocument doc = PdfDocument.Load(fs);

        foreach (PdfArray spaceArray in NChannelSpaces(doc))
        {
            if (!SpotColorSpace.TryParse(spaceArray, doc, out SpotColorSpace? space, minimumElements: 4))
            {
                output.WriteLine("space did not parse — unexpected");
                continue;
            }

            (double[][]? _, _) = (null, 0);   // placate the compiler; real work below
            for (var i = 0; i < space.Names.Count; i++)
            {
                string name = space.Names[i]!;
                (double[][]? isolated, _) =
                    ColorSpaceResolver.BuildTintRamp(spaceArray, doc, i, space.Names.Count);
                double[][]? own = OwnRamp(space, name, doc);

                if (isolated is null || own is null)
                {
                    output.WriteLine($"[{string.Join(",", space.Names)}] {name}: "
                                     + $"isolated={(isolated is null ? "null" : "ok")} own={(own is null ? "null" : "ok")}");
                    continue;
                }

                var maxDiff = 0.0;
                for (var s = 0; s < 256; s++)
                    for (var ch = 0; ch < Math.Min(isolated[s].Length, own[s].Length); ch++)
                        maxDiff = Math.Max(maxDiff, Math.Abs(isolated[s][ch] - own[s][ch]));

                output.WriteLine($"[{string.Join(",", space.Names)}] {name}: "
                                 + $"components isolated={isolated[255].Length} own={own[255].Length} "
                                 + $"MAXDIFF={maxDiff:G17}");
            }
        }
    }

    /// <summary>256 samples of /Attributes /Colorants /&lt;name&gt;'s own Separation, or null.</summary>
    private static double[][]? OwnRamp(SpotColorSpace space, string name, PdfDocument doc)
    {
        try
        {
            if (space.Colorants is not { } colorants) return null;
            if (!colorants.TryGetValue(new PdfName(name), out PdfObject? entryObj)) return null;
            if (ColorSpaceResolver.Deref(entryObj, doc) is not PdfArray entry) return null;
            if (!SpotColorSpace.TryParse(entry, doc, out SpotColorSpace? sep, minimumElements: 4)) return null;

            PdfFunction? fn = PdfFunction.Create(sep.TintTransformObject, doc);
            if (fn is null) return null;

            var ramp = new double[256][];
            for (var s = 0; s < 256; s++) ramp[s] = fn.Evaluate([s / 255.0]);
            return ramp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>GWG081's NChannel spaces hide on a /Shading and behind an image's /Indexed base.</summary>
    private static List<PdfArray> NChannelSpaces(PdfDocument doc)
    {
        var found = new List<PdfArray>();
        for (var p = 0; p < doc.PageCount; p++)
        {
            PdfResources? res = doc.GetPage(p)?.GetResources();
            if (res is null) continue;

            foreach (string key in new[] { "Shading", "Pattern" })
            {
                if (Resolve(res.Dictionary.Get(key), doc) is not PdfDictionary bag) continue;
                foreach (KeyValuePair<PdfName, PdfObject> kv in bag)
                {
                    PdfObject o = Resolve(kv.Value, doc)!;
                    PdfDictionary? d = o as PdfDictionary ?? (o as PdfStream)?.Dictionary;
                    Collect(d?.Get("ColorSpace"), doc, found);
                }
            }

            if (res.GetXObjects() is not { } xo) continue;
            foreach (KeyValuePair<PdfName, PdfObject> kv in xo)
                if (Resolve(kv.Value, doc) is PdfStream st)
                    Collect(st.Dictionary.Get("ColorSpace"), doc, found);
        }
        return found;
    }

    private static void Collect(PdfObject? csObj, PdfDocument doc, List<PdfArray> found, int depth = 0)
    {
        if (csObj is null || depth > 4) return;
        if (Resolve(csObj, doc) is not PdfArray arr || arr.Count == 0) return;
        if (arr[0] is PdfName { Value: "Indexed" } && arr.Count >= 2)
        {
            Collect(arr[1], doc, found, depth + 1);
            return;
        }
        if (SpotColorSpace.TryParse(arr, doc, out SpotColorSpace? s, minimumElements: 4)
            && s.IsNChannel)
            found.Add(arr);
    }

    private static PdfObject? Resolve(PdfObject? o, PdfDocument doc) =>
        o is PdfIndirectReference r ? doc.ResolveReference(r) ?? o : o;

    private void MeasureVeraLoadability()
    {
        foreach (string f in VeraFiles)
        {
            string path = Path.Combine(VeraDir, f);
            try
            {
                using FileStream fs = File.OpenRead(path);
                using PdfDocument doc = PdfDocument.Load(fs);
                IReadOnlyList<PageColorant> colorants = doc.GetPageColorants();
                output.WriteLine($"{f}: LOADED pages={doc.PageCount} colorants="
                                 + string.Join(", ", colorants.Select(c => $"{c.Name}:{c.Kind}"
                                                                           + (c.TintRamp is null ? "(null ramp)" : ""))));
            }
            catch (Exception ex)
            {
                output.WriteLine($"{f}: FAILED {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
```

If a member name above does not compile (for example `PdfFunction` living in a different namespace, or `GetPageColorants` taking an argument), **fix the scaffold rather than the production code** and note the correction in your report — the scaffold is disposable, the measurement is the point.

- [ ] **Step 2: Run it**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~TempPass2aPrimeMeasurement" --logger "console;verbosity=detailed"`

- [ ] **Step 3: Record both measurements verbatim in your report**

Expected, and what to do otherwise:

- **`MAXDIFF` is 0 (or below 1e-12) for every component.** Then Tasks 2 and 4 proceed on "zero GWG digests move".
  If any `MAXDIFF` is materially non-zero, **stop and report it**: Task 2 will move GWG081's digest, and Task 4's success criterion must change from "no digest moves" to "only GWG081 moves, by this measured amount".
- **All three veraPDF files report `LOADED`.** If any reports `FAILED`, record the exception and carry it into Pass 2b's plan — it does not block this plan, because Tasks 1–3 stand on their own, but it shrinks 2b's gate.
- Also record each veraPDF file's colorant list. `t02-pass-a` is expected to show `PrCyan:Spot`, `PrMagenta:Spot`, `PrYellow:Spot` **before** Task 3 — that is the divergence Task 3 fixes, and seeing it now makes Task 3's test meaningful.

- [ ] **Step 4: Delete the scaffold and confirm the tree is clean**

```bash
rm PdfLibrary.Tests/Rendering/TempPass2aPrimeMeasurement.cs
git status --porcelain
```

Expected: empty output. Paste it in your report. **Nothing is committed by this task.**

---

## Task 1: `ProcessChannelCount` — accept an ICCBased CMYK/Gray process space

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `BuildComponents`, `ProcessSpaceName` → `ProcessChannelCount`, `ProcessChannelFor`
- Test: `PdfLibrary.Tests/Rendering/ColourantComponentTests.cs` (extend)

**Interfaces:**
- Consumes: `SpotColorSpace.Process` (a `PdfDictionary?`, lazily resolved and guarded by `EnsureAttributes`); `ColorSpaceResolver.Deref`.
- Produces: `ProcessChannelCount(PdfDictionary process, PdfDocument? doc) → int?`. `ProcessChannelFor` loses its `bool processIsCmyk` parameter and gains the rule "canonical reserved index only when `channelCount == 4`". No public surface changes.

**Background:** `ProcessSpaceName` returns a family-name string, so `[/ICCBased 7 0 R]` yields `"ICCBased"` and `BuildComponents` suppresses the whole component list. ISO 32000-1 EXAMPLE 5 — the canonical *calibrated CMYK* NChannel, and what Illustrator and InDesign emit — is exactly that shape. Returning the channel count answers the caller's real question and absorbs the `processIsCmyk` bool.

**The hazard, stated once:** reading the ICC stream's `/N` dereferences an object no path previously touched here. It lands inside `BuildComponents`'s existing `try`/`catch (Exception)` — **and that is not evidence.** Pass 2a's Task 2 deref was inside a `try` too and still needed its own test at its own level. Step 1 writes that test first.

- [ ] **Step 1: Write the failing tests**

Append to `ColourantComponentTests.cs`:

```csharp
    // --- ICCBased process spaces (Pass 2a′) ---

    /// <summary>An [/ICCBased s] process space whose stream declares /N 4 is CMYK-shaped: components
    /// classify normally and reserved names get their canonical channels. ISO 32000-1 EXAMPLE 5.</summary>
    [Fact]
    public void IccBasedCmykProcessSpace_IsAcceptedAsFourChannel()
    {
        ColorantOrigin? o = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 4 /Length 0 >> stream\nendstream");

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        Assert.Equal(1, o.Components[0].ProcessChannel);      // Magenta, listed at index 0... see below
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }

    [Fact]
    public void IccBasedGrayProcessSpace_IsAcceptedAsOneChannel()
    {
        ColorantOrigin? o = ParseWithDoc(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Ink1] >> >>]",
            "<< /N 1 /Length 0 >> stream\nendstream");

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        Assert.Equal(0, o.Components[0].ProcessChannel);
    }

    [Fact]
    public void IccBasedThreeChannelProcessSpace_SuppressesTheComponentList()
    {
        // /N 3 is not reducible to plates here — the same answer /DeviceRGB gets.
        ColorantOrigin? o = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 3 /Length 0 >> stream\nendstream");

        Assert.Null(o!.Components);
    }

    [Fact]
    public void IccBasedWithoutN_SuppressesTheComponentList()
    {
        ColorantOrigin? o = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /Length 0 >> stream\nendstream");

        Assert.Null(o!.Components);
    }

    [Fact]
    public void IccBasedWhoseStreamIsMissing_SuppressesTheComponentList()
    {
        // Preserves today's behaviour exactly for an ICCBased we cannot read.
        ColorantOrigin? o = Origin(
            NChannel("/Process << /ColorSpace [/ICCBased 99 0 R] /Components [/Magenta] >>"), 0.25, 1.0);

        Assert.Null(o!.Components);
    }

    [Fact]
    public void IccBasedArrayWithNoStreamElement_SuppressesTheComponentList()
    {
        ColorantOrigin? o = Origin(
            NChannel("/Process << /ColorSpace [/ICCBased] /Components [/Magenta] >>"), 0.25, 1.0);

        Assert.Null(o!.Components);
    }

    /// <summary>THE AXIS-B TEST. Reading /N dereferences the ICC stream — an object no path previously
    /// touched here. A corrupt target must degrade, not throw out of OriginForColorSpaceObject, which
    /// PdfRenderer calls on every colour-setting operator with no try/catch above it.
    ///
    /// <para>A reference to a merely NON-EXISTENT object returns null without throwing, so the fixture
    /// uses a genuinely corrupt target: ColourConformancePage.Build writes an in-use xref entry for every
    /// extraObject, so an object body of a lone ']' reaches the on-demand parser and throws.</para></summary>
    [Fact]
    public void CorruptIccBasedStreamReference_DegradesToReservedNamesOnly_RatherThanThrowing()
    {
        ColorantOrigin? o = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "]");

        // Degrades to reserved-name classification: the list survives, Magenta is still Process.
        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }

    [Fact]
    public void IndirectN_IsResolved()
    {
        // /N may itself be an indirect reference; ColorSpaceResolver.cs:714's established idiom derefs it.
        ColorantOrigin? o = ParseWithDoc(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Magenta] >> >>]",
            "<< /N 6 0 R /Length 0 >> stream\nendstream",
            "4");

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
    }
```

**Note on the first test's expected channel.** `/Components [/Magenta]` lists Magenta at index 0, and Pass 2a's rule 1 (listed index wins over canonical) therefore gives it channel **0**, not 1. Write `Assert.Equal(0, o.Components[0].ProcessChannel);` and delete the misleading comment. This is called out because getting it backwards would look like a bug in `ProcessChannelFor` rather than in the test.

`ParseWithDoc` already exists in this file and takes `params string[] extraObjects` numbered from 5. Confirm its exact signature before use and adapt these calls to it rather than changing the helper.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: the ICCBased tests FAIL — today every one of them yields `Components == null`, so the four that assert a non-null list fail, and `IccBasedThreeChannelProcessSpace_…` / `IccBasedWithoutN_…` / the two "suppresses" tests PASS for the wrong reason. Record which are which.

- [ ] **Step 3: Replace `ProcessSpaceName` with `ProcessChannelCount`**

Delete `ProcessSpaceName` entirely and add:

```csharp
    /// <summary>The number of channels in an NChannel space's process colour space, or null when this
    /// engine cannot say — in which case <see cref="BuildComponents"/> suppresses the whole component
    /// list and the space falls back to its document tint transform.
    ///
    /// <para>An absent or unreadable <c>/ColorSpace</c> returns the no-constraint default rather than
    /// null: a malformed process dictionary should degrade to reserved-name classification, not suppress
    /// an otherwise usable space.</para>
    ///
    /// <para><c>[/ICCBased s]</c> is accepted by reading the stream's <c>/N</c> — 4 is CMYK-shaped, 1 is
    /// Gray-shaped, anything else is not reducible to plates here. ISO 32000-1 EXAMPLE 5 is exactly this
    /// shape and is what Illustrator and InDesign emit. Only the channel COUNT is read: the profile is
    /// not validated and is not used for conversion, which is a colour-management question rather than a
    /// plate-identity one. Read the stream's DICTIONARY only — never Data/GetDecodedData; a real profile
    /// is hundreds of kilobytes of Flate and this runs on every colour-setting operator.</para>
    ///
    /// <para>Dereferencing the ICC stream resolves an object no path previously touched here. The call
    /// site in <see cref="BuildComponents"/> wraps this in try/catch for exactly that reason;
    /// <c>CorruptIccBasedStreamReference_DegradesToReservedNamesOnly_RatherThanThrowing</c> pins it.</para>
    /// </summary>
    private static int? ProcessChannelCount(PdfDictionary process, PdfDocument? doc)
    {
        const int noConstraint = 4;

        if (!process.TryGetValue(new PdfName("ColorSpace"), out PdfObject? csObj)) return noConstraint;

        PdfObject resolved = Deref(csObj, doc);
        string family = resolved switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty,
        };

        switch (family)
        {
            case "": return noConstraint;          // unreadable shape — degrade, do not reject
            case "DeviceCMYK": return 4;
            case "DeviceGray": return 1;

            case "ICCBased":
                if (resolved is not PdfArray { Count: >= 2 } iccArray) return null;
                if (Deref(iccArray[1], doc) is not PdfStream icc) return null;
                // Matches the established idiom at the ICCBased arm of InitialColorFor: /N may itself be
                // an indirect reference, and only a PdfInteger counts.
                if (!icc.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj)
                    || Deref(nObj, doc) is not PdfInteger nInt) return null;
                return nInt.Value is 4 or 1 ? nInt.Value : null;

            // /DeviceRGB, /Lab, /CalGray, /Indexed, /Pattern, anything else. /CalGray is deliberately NOT
            // treated as Gray: it is CIE-based rather than a device space, and InkDecider.ToCmyk already
            // distinguishes it from /DeviceGray for that reason. Recorded as a gap.
            default: return null;
        }
    }
```

- [ ] **Step 4: Rewire `BuildComponents`**

Replace the `processIsCmyk` bool and the `ProcessSpaceName` call. The declarations become:

```csharp
        Dictionary<string, int>? processChannels = null;
        // Starts at the no-constraint default and is only ever LOWERED, by a successful DeviceGray or
        // /N 1 read. That is why the catch below does not restore it: if the lowering read already
        // succeeded, 1 is correct; if the throw preceded it, 4 is already correct. Restoring 4 here would
        // re-enable the canonical CMYK guess for a space known to have one channel.
        var channelCount = 4;
```

and the guarded block's head becomes:

```csharp
            try
            {
                if (ProcessChannelCount(process, doc) is not { } count) return null;
                channelCount = count;
```

Delete the now-dead `int channelCount = processIsCmyk ? 4 : 1;` line below the block. In the `catch`, keep `processChannels = null;` and the existing lazy log; do not add a `channelCount` reset.

- [ ] **Step 5: Update `ProcessChannelFor`**

Drop the `bool processIsCmyk` parameter; replace its guard:

```csharp
    private static int? ProcessChannelFor(
        string name, ColourantRole role, Dictionary<string, int>? processChannels, int channelCount)
    {
        if (role != ColourantRole.Process) return null;

        if (processChannels is not null && processChannels.TryGetValue(name, out int listedIndex))
            return listedIndex < channelCount ? listedIndex : null;

        // Canonical reserved channels exist only for a four-channel process space. Under a one-channel
        // space nothing in the spec says which reserved name owns the single channel, and guessing would
        // be the half-built mapping this plan's Scope warns is worse than not building it.
        if (channelCount != 4) return null;
        return name switch
        {
            "Cyan" => 0,
            "Magenta" => 1,
            "Yellow" => 2,
            "Black" => 3,
            _ => null,
        };
    }
```

Update its one call site to drop the removed argument. Update the XML doc's `processIsCmyk` references to `channelCount`.

- [ ] **Step 6: Run the focused tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: PASS.

- [ ] **Step 7: Mutation-verify the new guard**

Delete the `catch` block in `BuildComponents` (leaving the `try`) and re-run the focused filter.
Expected: `CorruptIccBasedStreamReference_…` **errors** with `PdfParseException` escaping through `ProcessChannelCount → BuildComponents → OriginForColorSpaceObject`. Record the exact message and stack.

Restore the catch. Then delete `if (channelCount != 4) return null;` from `ProcessChannelFor` and re-run.
Expected: `IccBasedGrayProcessSpace_IsAcceptedAsOneChannel` still passes (Ink1 is listed, so rule 1 answers it) — so **add a reserved-name-under-ICCBased-Gray case if none of the existing tests goes red**, because an unpinned guard is the failure mode this program has hit twice.

Restore both. Run `git diff` and confirm no production file has an unintended change. Paste that confirmation.

- [ ] **Step 8: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: 2591 + the new tests, 0 failing.

- [ ] **Step 9: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary.Tests/Rendering/ColourantComponentTests.cs
git commit -m "feat(colour): accept an ICCBased CMYK/Gray NChannel process space

ProcessSpaceName returned a family-name string, so /Process /ColorSpace
[/ICCBased n 0 R] yielded \"ICCBased\" and BuildComponents suppressed the whole
component list. ISO 32000-1 EXAMPLE 5 — the canonical calibrated-CMYK NChannel,
and what Illustrator and InDesign emit — is exactly that shape, so the gap was
wider than the register admitted.

ProcessChannelCount returns the channel count instead, which is the question the
caller actually has, and absorbs the processIsCmyk bool. /N 4 is CMYK-shaped,
/N 1 is Gray-shaped, anything else stays suppressed. Only the count is read: the
profile is not validated, not used for conversion, and its stream data is never
decoded.

Reading /N dereferences an object no path previously touched here, so the guard
is pinned by a test built on a genuinely corrupt target rather than a merely
absent one, and mutation-verified to escape when the catch is removed."
```

---

## Task 2: Build each component's ramp from its own `/Colorants` Separation

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `BuildTintRamp`
- Test: `PdfLibrary.Tests/Rendering/NChannelRampTests.cs` (create)

**Interfaces:**
- Consumes: `SpotColorSpace.IsNChannel`, `SpotColorSpace.Colorants`, `SpotColorSpace.TryParse`, `PdfFunction.Create`, `ColorSpaceResolver.Deref`.
- Produces: no signature change. `BuildTintRamp(PdfArray baseArray, PdfDocument? doc, int colorantIndex, int inputCount, int samples = 256)` keeps returning `(double[][]? Ramp, (byte R, byte G, byte B) Solid)`; only the *source* of the values changes, and only for NChannel spaces.

**Background:** `BuildTintRamp` answers "what does component *i* look like alone?" by zeroing the other inputs and evaluating the **whole-space** transform. For an NChannel space the file states the answer outright — Table 71 defines `/Attributes /Colorants /<name>` as a Separation describing "the appearance of that colorant alone". The isolated evaluation is an approximation of it, and coincides only when the transform is separable.

This feeds `PageColorant.TintRamp` → `SpotColorantRegistry`, which is built **once per page**, so there is no per-operator cost.

**Task 0 measured that GWG081's two sources agree exactly**, so this task must move no corpus digest. That is the point of the "not NChannel → byte-identical" row in the degenerate-input table.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Rendering/NChannelRampTests.cs`:

```csharp
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 Table 71: for an NChannel space, /Attributes /Colorants /&lt;name&gt; is a full Separation
/// describing "the appearance of that colorant alone" — authoritative for that component, where zeroing
/// the other inputs of the whole-space transform is only an approximation.
/// </summary>
public class NChannelRampTests
{
    /// <summary>A whole-space transform that IGNORES its inputs and always returns 0.9 on cyan, versus a
    /// /Colorants Separation that ramps linearly to 0.5 cyan. The two disagree at every tint except 0, so
    /// the ramp's SOURCE is what the assertion measures — not merely that a ramp was produced.</summary>
    private const string WholeSpaceAlways09 =
        "<< /FunctionType 2 /Domain [0 1 0 1] /C0 [0.9 0 0 0] /C1 [0.9 0 0 0] /N 1 "
        + "/Range [0 1 0 1 0 1 0 1] >>";

    private const string SpotOwnSeparation =
        "/Colorants << /Spot1 [/Separation /Spot1 /DeviceCMYK "
        + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 0 0] /N 1 >>] >>";

    private static PdfArray Parse(string literal) =>
        (PdfArray)PdfTestHelpers.ParseObject(literal);

    [Fact]
    public void NChannelComponent_RampComesFromItsOwnColorantsSeparation()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel " + SpotOwnSeparation + " >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.0, ramp![0][0], 3);      // tint 0  -> C0 = 0.0, NOT the whole-space 0.9
        Assert.Equal(0.25, ramp[128][0], 2);    // tint ~.5 -> 0.25
        Assert.Equal(0.5, ramp[255][0], 3);     // tint 1  -> C1 = 0.5
    }

    [Fact]
    public void NChannelComponentWithoutAColorantsEntry_FallsBackToTheIsolatedEvaluation()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09 + " << /Subtype /NChannel >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);    // today's behaviour, unchanged
    }

    [Fact]
    public void PlainDeviceN_IsUnaffected_EvenWithAColorantsDictionary()
    {
        // /Subtype defaults to DeviceN (Table 70). Row 5-3's per-component rule is NChannel-only, and the
        // 50 non-NChannel corpus files depend on this staying byte-identical.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << " + SpotOwnSeparation + " >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }

    [Fact]
    public void ColorantsEntryThatIsNotASeparation_FallsBackToTheIsolatedEvaluation()
    {
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Spot1 /DeviceRGB >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }

    [Fact]
    public void ProcessComponent_StillUsesTheIsolatedEvaluation()
    {
        // Table 71: a /Colorants definition "shall be ignored if the colorant is also present in the
        // process dictionary". Cyan is reserved, so it is a process colorant regardless.
        PdfArray space = Parse(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Cyan [/Separation /Cyan /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 0 0] /N 1 >>] >> >>]");

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, null, 1, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }
}
```

`PdfTestHelpers.ParseObject` is a placeholder for whatever this test project already uses to turn a PDF literal into a `PdfObject` — `ColourantComponentTests` has such a helper (`Parse`). **Reuse the existing one**; do not add a new helper. If it is private, promote it to `internal static` on a shared test helper class rather than duplicating it, and say which you did.

The corrupt-indirect-entry row of the degenerate table needs a document, so write it with the `ParseWithDoc`-style fixture used in `ColourantComponentTests` rather than the doc-less `Parse` above:

```csharp
    [Fact]
    public void CorruptColorantsEntryReference_FallsBackToTheIsolatedEvaluation_RatherThanThrowing()
    {
        // GetPageColorants must never throw (see BuildTintRamp's own catch comment); a corrupt entry is
        // a fallback, not a failure. Genuinely corrupt target — a lone ']' body under an in-use xref
        // entry — because a merely non-existent object returns null without throwing.
        byte[] pdf = ColourConformancePage.Build(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + WholeSpaceAlways09
            + " << /Subtype /NChannel /Colorants << /Spot1 5 0 R >> >>]",
            "1 0 0 rg 0 0 1 1 re f",
            extraObjects: ["]"]);

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        var space = (PdfArray)page.GetResources()!.GetColorSpaces()![new PdfName("Cs0")]!;

        (double[][]? ramp, _) = ColorSpaceResolver.BuildTintRamp(space, doc, 0, 2);

        Assert.NotNull(ramp);
        Assert.Equal(0.9, ramp![255][0], 3);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~NChannelRampTests"`
Expected: `NChannelComponent_RampComesFromItsOwnColorantsSeparation` FAILS with 0.9 where 0.5 is expected — today every ramp comes from the whole-space transform. The four fallback tests PASS already; they are the regression guard for the 50 unaffected corpus files, and they must still pass after Step 3.

- [ ] **Step 3: Implement the preference**

In `BuildTintRamp`, immediately after the `TryParse` guard and **before** `PdfFunction.Create(space.TintTransformObject, doc)`, insert:

```csharp
        // ISO 32000-2 Table 71: for an NChannel space, /Attributes /Colorants /<name> is a full
        // Separation describing "the appearance of that colorant alone" — authoritative for this
        // component. Zeroing the other inputs of the whole-space transform (below) only approximates it,
        // and coincides just when that transform is separable. Falls through to the approximation
        // whenever the entry is absent or unusable, which keeps every non-NChannel space — and every
        // NChannel space without usable /Colorants — byte-identical to before.
        if (space.IsNChannel && OwnColorantRamp(space, colorantIndex, doc, samples) is { } ownRamp)
            return ownRamp;
```

Add after `BuildTintRamp`:

```csharp
    /// <summary>
    /// The ramp and representative solid for one NChannel component taken from its own
    /// <c>/Attributes /Colorants /&lt;name&gt;</c> Separation, or null when there is no usable entry.
    ///
    /// <para>Null is the "use the whole-space approximation instead" signal, not an error: a missing
    /// dictionary, a missing entry, an entry that is not a Separation, an alternate this engine cannot
    /// reduce, or a tint transform that throws at evaluation time all land here. Everything is wrapped
    /// because this runs from <c>PdfDocument.GetPageColorants</c>, whose contract is that a malformed
    /// colour space still lists its colorants rather than failing the call — the same reason
    /// <see cref="BuildTintRamp"/>'s own evaluation loop is wrapped.</para>
    ///
    /// <para>Dereferencing the entry resolves an object no path previously touched here, which is why the
    /// try covers it and not merely the evaluation.</para>
    /// </summary>
    private static (double[][] Ramp, (byte R, byte G, byte B) Solid)? OwnColorantRamp(
        SpotColorSpace space, int colorantIndex, PdfDocument? doc, int samples)
    {
        try
        {
            if (colorantIndex < 0 || colorantIndex >= space.Names.Count) return null;
            if (space.Names[colorantIndex] is not { } name) return null;
            if (space.Colorants is not { } colorants) return null;
            if (!colorants.TryGetValue(new PdfName(name), out PdfObject? entryObj)) return null;
            if (Deref(entryObj, doc) is not PdfArray entry) return null;

            // Reuse the whole-space builders on the Separation itself: a /Colorants value IS a colour
            // space array, so the same arity and alternate-space rules apply to it unchanged.
            Func<double[], (double C, double M, double Y, double K)>? toCmyk =
                BuildTintToCmyk(entry, doc, out int inputs);
            if (toCmyk is null || inputs != 1) return null;

            var ramp = new double[samples][];
            for (var s = 0; s < samples; s++)
            {
                double t = samples == 1 ? 0.0 : (double)s / (samples - 1);
                (double c, double m, double y, double k) = toCmyk([t]);
                ramp[s] = [c, m, y, k];
            }

            // The solid comes from the SAME source as the ramp, so a swatch can never disagree with the
            // plate it represents.
            (byte R, byte G, byte B) solid = (0, 0, 0);
            Func<double[], (byte R, byte G, byte B)>? toRgb = BuildTintToRgb(entry, doc, out int _);
            if (toRgb is not null) solid = toRgb([1.0]);

            return (ramp, solid);
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, () =>
                $"OwnColorantRamp: /Colorants entry for component {colorantIndex} threw; falling back to "
                + $"the whole-space isolated evaluation: {ex}");
            return null;
        }
    }
```

`BuildTintToCmyk` and `BuildTintToRgb` are private members of this same class, so no accessibility change is needed. If `BuildTintToRgb`'s signature differs from the shape used here, adapt the call and report the difference — do not change that method.

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~NChannelRampTests"`
Expected: PASS, including the four fallback tests.

- [ ] **Step 5: Mutation-verify**

Change `if (space.IsNChannel && …)` to `if (false && …)` and re-run.
Expected: `NChannelComponent_RampComesFromItsOwnColorantsSeparation` goes red; the four fallback tests stay green.

Change it to `if (true && …)` (dropping the `IsNChannel` gate) and re-run.
Expected: `PlainDeviceN_IsUnaffected_EvenWithAColorantsDictionary` goes red. **If it does not, that test is vacuous** — fix it before proceeding, because it is the only thing standing between this task and 50 moved corpus digests.

Restore both, re-run, confirm green, and `git diff` to confirm nothing temporary survived. Paste the confirmation.

- [ ] **Step 6: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: 0 failing.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary.Tests/Rendering/NChannelRampTests.cs
git commit -m "feat(colour): take an NChannel component's ramp from its own /Colorants Separation

BuildTintRamp answered 'what does component i look like alone?' by zeroing the
other inputs and evaluating the whole-space tint transform. For an NChannel
space the file states the answer outright: Table 71 defines /Attributes
/Colorants /<name> as a Separation describing 'the appearance of that colorant
alone'. The isolated evaluation only approximates that, and coincides just when
the transform is separable.

Gated on IsNChannel and falling back to the approximation whenever the entry is
absent or unusable, so every plain DeviceN space stays byte-identical — the
mutation test for that gate is what protects the 50 unaffected corpus patches.
GWG081's two sources were measured to agree exactly before this landed, so no
corpus digest moves.

Feeds PageColorant.TintRamp, which is built once per page, so nothing here runs
per colour operator. The solid swatch now comes from the same source as the
ramp, so the two cannot disagree."
```

---

## Task 3: Stop the page inventory registering a named process colorant as a spot

**Files:**
- Modify: `PdfLibrary/Document/PageColorantReader.cs` — `AddColorants`
- Test: `PdfLibrary.Tests/Document/PageColorantsTests.cs` (extend)

**Interfaces:**
- Consumes: `ColorantOrigin.Components` and `ColourantComponent.Role` (Pass 2a), already computed by the `OriginForColorSpaceObject` call `AddColorants` makes at line 133.
- Produces: no signature change. `PageColorant.Kind` becomes correct for NChannel process components.

**Background:** `PageColorant.Classify` is **name-only** — `Cyan`/`Magenta`/`Yellow`/`Black` → Process, everything else → Spot. `BuildComponents` classifies by name **and** `/Process /Components`. On the veraPDF fixture the two disagree about `/PrCyan`, `/PrMagenta`, `/PrYellow`: the inventory calls them Spot and `SpotColorantRegistry.Build` gives each a plane (`if (c.Kind != ColorantKind.Spot) continue;`), while the per-operator path calls them Process channels 0/1/2. Three of sixteen planes consumed by colorants that are not spots, a colorant liable to be painted twice, and `AnyRegistered` reporting true for a space containing no spots at all.

`AddColorants` already has the origin in hand, so the fix is to prefer the role it computed.

- [ ] **Step 1: Write the failing tests**

Append to `PageColorantsTests.cs`:

```csharp
    /// <summary>A name listed in /Process /Components is a PROCESS colorant, whatever it is called.
    /// Classifying it as Spot gives it a plane in SpotColorantRegistry and lets it be painted twice —
    /// once on its plate, once on its plane. The veraPDF NChannel conformance fixture is exactly this
    /// shape: /Components [/PrCyan /PrMagenta /PrYellow /Black].</summary>
    [Fact]
    public void NChannelProcessComponent_IsClassifiedProcess_NotSpot()
    {
        byte[] pdf = ColourConformancePage.Build(
            "[/DeviceN [/PrCyan /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components [/PrCyan] >> >>]",
            "1 0 0 rg 0 0 1 1 re f");

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants();

        Assert.Equal(ColorantKind.Process, colorants.Single(c => c.Name == "PrCyan").Kind);
        Assert.Equal(ColorantKind.Spot, colorants.Single(c => c.Name == "Spot1").Kind);
    }

    /// <summary>The name-based classification must survive for everything else — this is what keeps the
    /// 50 non-NChannel corpus patches byte-identical.</summary>
    [Fact]
    public void PlainDeviceN_KeepsTheNameBasedClassification()
    {
        byte[] pdf = ColourConformancePage.Build(
            "[/DeviceN [/PrCyan /Spot1] /DeviceCMYK " + Tint2 + "]",
            "1 0 0 rg 0 0 1 1 re f");

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants();

        Assert.Equal(ColorantKind.Spot, colorants.Single(c => c.Name == "PrCyan").Kind);
    }

    /// <summary>ColourantRole has no All member — RoleFor maps /All to Spot as a documented leniency —
    /// so the name-based All distinction must be preserved for a Spot role rather than lost.</summary>
    [Fact]
    public void NChannelAllComponent_IsStillClassifiedAll_AndSkipped()
    {
        byte[] pdf = ColourConformancePage.Build(
            "[/DeviceN [/All /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]",
            "1 0 0 rg 0 0 1 1 re f");

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants();

        Assert.DoesNotContain(colorants, c => c.Name == "All");
        Assert.Contains(colorants, c => c.Name == "Spot1");
    }

    [Fact]
    public void NChannelNoneComponent_IsStillSkipped()
    {
        byte[] pdf = ColourConformancePage.Build(
            "[/DeviceN [/None /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]",
            "1 0 0 rg 0 0 1 1 re f");

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        IReadOnlyList<PageColorant> colorants = doc.GetPageColorants();

        Assert.DoesNotContain(colorants, c => c.Name == "None");
    }
```

`Tint2` and `ColourConformancePage` live in the test project already; if `PageColorantsTests` cannot see them, reference them by their existing namespace rather than duplicating either.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~PageColorantsTests"`
Expected: `NChannelProcessComponent_IsClassifiedProcess_NotSpot` FAILS — `PrCyan` classifies as `Spot` today. The other three PASS already and are the regression guard.

- [ ] **Step 3: Implement**

In `PageColorantReader.AddColorants`, replace

```csharp
            ColorantKind kind = PageColorant.Classify(name);
```

with

```csharp
            ColorantKind kind = KindFor(origin, i, name);
```

and add to the same class:

```csharp
    /// <summary>
    /// The colorant's kind, preferring the per-component role the resolver already computed over the
    /// name-only classification.
    ///
    /// <para><see cref="PageColorant.Classify"/> knows only the four reserved names, so a process
    /// colorant named anything else — <c>/PrCyan</c> in the veraPDF NChannel conformance fixture — reads
    /// as Spot and is handed a plane by the compositor's registry, while the per-operator path treats it
    /// as a process channel. The same colorant then exists twice.</para>
    ///
    /// <para><c>Components</c> is populated only for NChannel spaces, so every DeviceN and Separation
    /// space keeps the name-based answer unchanged. A Spot role also falls back to the name, because
    /// <c>ColourantRole</c> has no <c>All</c> member — <c>RoleFor</c> maps <c>/All</c> to Spot as a
    /// documented leniency — and that distinction must survive.</para>
    /// </summary>
    private static ColorantKind KindFor(ColorantOrigin origin, int index, string name)
    {
        if (origin.Components is not { } components || index >= components.Count)
            return PageColorant.Classify(name);

        return components[index].Role switch
        {
            ColourantRole.Process => ColorantKind.Process,
            ColourantRole.None => ColorantKind.None,
            _ => PageColorant.Classify(name),
        };
    }
```

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~PageColorantsTests"`
Expected: PASS.

- [ ] **Step 5: Mutation-verify**

Replace `KindFor(origin, i, name)` with `PageColorant.Classify(name)` and re-run.
Expected: `NChannelProcessComponent_IsClassifiedProcess_NotSpot` goes red, the rest stay green.

Then change `origin.Components is not { } components` to `origin.Components is { } components` inverted such that the name fallback never runs (i.e. force the role path for every space) and re-run.
Expected: `PlainDeviceN_KeepsTheNameBasedClassification` or `NChannelAllComponent_…` goes red. **If neither does, those tests are vacuous** — fix before proceeding.

Restore, re-run, `git diff` to confirm clean. Paste the confirmation.

- [ ] **Step 6: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: 0 failing.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Document/PageColorantReader.cs PdfLibrary.Tests/Document/PageColorantsTests.cs
git commit -m "fix(colour): classify NChannel process colorants by role, not by name

PageColorant.Classify knows only Cyan/Magenta/Yellow/Black, so a process
colorant named anything else read as Spot. The compositor's registry skips
non-Spot colorants when handing out planes, so such a colorant got a spot plane
while the per-operator path treated it as a process channel — the same colorant
existing twice, and AnyRegistered reporting true for a space with no spots in
it. The veraPDF NChannel conformance fixture is exactly this shape:
/Components [/PrCyan /PrMagenta /PrYellow /Black].

AddColorants already had the resolved origin in hand, so it now prefers the
per-component role. Components is populated only for NChannel spaces, so every
DeviceN and Separation space keeps the name-based answer; a Spot role also falls
back to the name, because ColourantRole has no All member and that distinction
must survive."
```

---

## Task 4: Prove it changed nothing

**Files:** none in the engine. This task verifies.

**Interfaces:** consumes everything Tasks 1–3 produced, plus the Pellucid corpus render-hash gate.

**Background:** nothing consumes the corrections, and Task 0 measured that GWG081's two ramp sources agree, so **every corpus digest must be unchanged** — the same claim Pass 2a made, for the same reason. If any digest moves, it means one of these three changes had a side effect on flattened colour, which is a defect in Tasks 1–3 rather than an expected result.

Two hazards, both observed on every pass so far: `pack-local.ps1` rewrites `Pellucid/Directory.Build.props.local` and **silently drops the `LxmanPdfLibraryRenderingSkiaVersion` pin** (value to restore: `0.1.1-dev20260717153208`), and `PdfCompare.csproj` pins the engine independently and the script does not touch it.

- [ ] **Step 1: Record the current pins**

```bash
cat /c/Users/jorda/RiderProjects/Pellucid/Directory.Build.props.local
grep -n "Lxman.PdfLibrary" /c/Users/jorda/PDFs/PdfCompare/PdfCompare.csproj
```

Record both in your report.

- [ ] **Step 2: Pack the engine**

From `C:\Users\jorda\RiderProjects\PDF`, run `./pack-local.ps1` in PowerShell. Read the new `LxmanPdfLibraryVersion` it wrote into `Pellucid/Directory.Build.props.local` and record it as NEWVERSION.

- [ ] **Step 3: Restore the Skia pin**

If `LxmanPdfLibraryRenderingSkiaVersion` is absent from `Directory.Build.props.local`, add it back inside the same `<PropertyGroup>` with value `0.1.1-dev20260717153208`.

- [ ] **Step 4: Repin PdfCompare**

Set the `Lxman.PdfLibrary` `PackageReference` `Version` in `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj` to NEWVERSION.

- [ ] **Step 5: Confirm Pellucid resolved the new engine**

```bash
cd /c/Users/jorda/RiderProjects/Pellucid && dotnet restore
grep -o "Lxman.PdfLibrary/[0-9][^\"]*" Pellucid.Core/obj/project.assets.json | sort -u
```

Expected: exactly NEWVERSION. **Do not proceed until it matches** — running the gate against the old engine would prove nothing.

- [ ] **Step 6: Run the corpus render-hash gate**

```bash
cd /c/Users/jorda/RiderProjects/Pellucid
dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~GwgRenderHashGateTests" --logger "console;verbosity=detailed"
```

Expected: **PASS with zero CHANGED lines.**

Capture the diagnostic line, which reads `N fixtures hashed, N baselined, 0 differences. engine=… icc=…`, and **confirm the engine string's embedded SHA is this branch's HEAD**. That is positive proof the gate ran the code under test rather than a cached package; a matching version number alone is not.

If any digest moved, **report BLOCKED** with the full list. Do not regenerate the baseline and do not set `PELLUCID_GWG_HASH_REGEN`.

- [ ] **Step 7: Run the remaining suites**

```bash
cd /c/Users/jorda/RiderProjects/Pellucid && dotnet test
cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests
```

Expected: Pellucid 1278 passing / 0 failing / 78 skipped; engine 2591 plus this plan's new tests, 0 failing.

Note: default-parallel engine runs intermittently hit a pre-existing `Path.GetTempFileName()` race in the Editing/Annotations tests, reproduced at unmodified `b4b9634`. If you see it, re-run and say so — it is not this plan's defect.

- [ ] **Step 8: Report, do not commit**

The pin files are gitignored and `PdfCompare` is untracked. Nothing to commit. Report the recorded pins, NEWVERSION, the Skia-pin restoration, the resolved version, the gate result including the embedded SHA, and both suite totals.

---

## Self-Review

**Spec coverage.** The design's Pass 2a′ section lists three engine changes: accept an ICCBased CMYK/Gray process space (Task 1), per-component ramps from `/Colorants` (Task 2), and the `Classify` reconciliation (Task 3). Each has a task. The design's Verification section assigns 2a′ the "GWG unchanged" gate — Task 4 — and its two open risks (does object 14 differ; do the veraPDF files load) are Task 0, whose results feed Task 4's expectation and Pass 2b's plan. Deliberate deviations, both stated in the design's Scope: `/CalGray` stays suppressed, and `/N` is read for channel count only with no colour conversion. The design's Pass 2b items — per-component evaluation in the compositor and `NChannelRenderHashGateTests` — are correctly absent from this plan; they are the next plan, as the design's Delivery section requires.

**Placeholder scan.** No `TBD`, no "similar to Task N", no "add error handling". Every code step carries complete code. Three places name a discovered value rather than a literal, each explicitly: NEWVERSION in Task 4 Step 2, and the two Task 0 measurements. Two places instruct the implementer to adapt to an existing helper's real signature (`ParseWithDoc` in Task 1, `Parse`/`ParseObject` in Task 2) rather than guessing it — flagged as instructions, not gaps, because inventing a second helper is the worse failure.

**Type consistency.** `ProcessChannelCount` returns `int?` and is consumed as `is not { } count` in Task 1 Step 4. `ProcessChannelFor` loses `bool processIsCmyk` and gains nothing, so its parameter list is `(string, ColourantRole, Dictionary<string,int>?, int)` at both definition and call site. `channelCount` is the name in both `BuildComponents` and `ProcessChannelFor`. `OwnColorantRamp` returns `(double[][] Ramp, (byte R, byte G, byte B) Solid)?`, matching `BuildTintRamp`'s own return shape so Step 3's `return ownRamp;` type-checks. `KindFor(ColorantOrigin, int, string)` matches its single call site. `PdfInteger` is the engine's integer primitive — there is no `PdfNumber` — and `nInt.Value` is the idiom already used at `ColorSpaceResolver.cs:714-716`.

**One known-fragile expectation, called out rather than buried.** Task 1's first test asserts `ProcessChannel == 0` for `/Magenta` listed at `/Components` index 0, because rule 1 (listed index) outranks rule 2 (canonical index). Writing `1` there would look like a `ProcessChannelFor` bug rather than a test bug, so the step says so explicitly.
