# Colour Pass 2b-engine — NChannel per-component image split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax
> for tracking.

**Goal:** Give the compositor the two engine-side facts it needs for ISO 32000-2 §8.6.6.5 per-component
evaluation — the process space's channel *count* on `ColorantOrigin`, and an NChannel image's colorant
split done by *role and channel* instead of by *name*.

**Architecture:** Two additive engine changes. (1) `ColorantOrigin` gains `ProcessChannelCount`, sourced
from the value `BuildComponents` already computes internally, so a consumer can tell "channel 0 of a
four-channel space" (cyan) from "channel 0 of a one-channel space" (gray). (2) `PdfImageToCmyk`'s two
colorant splitters consult `ColorantOrigin.Components` when the space is NChannel over a four-channel
process space, and fall back whole to today's name split otherwise.

**Tech Stack:** C# / .NET 8+10 multi-target, xUnit (`PdfLibrary.Tests`), local NuGet pack via
`pack-local.ps1`.

---

## Why this plan exists at all — a correction to the design

`Docs/superpowers/specs/2026-07-26-colour-pass2-nchannel-per-component-design.md` scopes Pass 2b to
**"Pass 2b (compositor)"**, one plan, in the Pellucid repo. **That is wrong on two counts**, discovered by
reading the code the design names. Both are recorded here rather than silently absorbed:

1. **The image path has no `ColorantOrigin`.** The design cites `CmykPageRenderer.cs:1157` as the images'
   all-or-nothing gate. It is — but what it gates is `registry.TryGetPlane(ink.Names[k])` over a
   `SpotImageInk`, and `SpotImageInk` carries *spot names + tint planes + a pre-split `ProcessCmyk`
   plane*. The `ColourantComponent` list never reaches that site. The colorant→plate/plane decision for
   images is made **engine-side**, in `PdfImageToCmyk.TryToSpotInk:314-318` and
   `StencilInkFromFill:394-397`, both of which split by `PageColorant.Classify(name)` +
   `ProcessPlate(name)` — the same literal `Cyan/Magenta/Yellow/Black` switch the ledger flags at
   `InkDecider.ProcessContribution:280`. So the images half of Pass 2b is an engine change, not a
   compositor one.

2. **`ProcessChannel` alone is not safe to consume.** `ColourantComponent.ProcessChannel` is an index into
   the **process colour space's** channels. Under a `/DeviceGray` process space, a name listed in
   `/Process /Components` gets index **0** (`ColorSpaceResolver.cs:1242-1243` returns `listedIndex` whenever
   `listedIndex < channelCount`, and `0 < 1`). A consumer that maps channel 0 → the cyan plate would paint
   a *gray* colorant on *cyan*. Nothing currently carried distinguishes the two cases: `channelCount` is a
   local inside `BuildComponents` and is discarded. Any consumer of `ProcessChannel` — the compositor in
   Pass 2b-compositor, and `TryToSpotInk` in Task 2 below — needs it.

**Consequence for delivery.** Pass 2b becomes two plans on the same repo boundary that worked for
Pass 2a/2a′: this engine plan lands, merges, packs and repins first; the compositor plan
(per-component fills/strokes + `NChannelRenderHashGateTests`) is written in detail afterwards, because
this plan's Task 0 measurements are inputs to it. Update the design doc's "Delivery" section as part of
Task 4.

## Task 0 result — measured 2026-07-26, before any production line was written

All four recorded predictions **HELD**. The numbers, which every later task's criterion depends on:

- **M1** — GWG081 (`2-SPOT/Patches/GWG081_DeviceN-Support_5c_X1a.pdf`). `Im0` is `/Indexed` over an
  NChannel `[Black, GWG Green]`, `/Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow
  /Black] >>`. Per-component: `Black` → Process/**3**, `GWG Green` → Spot/null. Name split: `Black` →
  `ProcessPlate` **3**, `GWG Green` → Spot. **AGREE on every component.** A second image `Im1` is
  `/Indexed` over a plain `Separation`, so `Components` is **null** and it never enters the new path.
  ⇒ **Task 3's criterion stays "zero of 51 digests move."**
- **M2** — **2 NChannel spaces, 1 file**, exactly the design's census: GWG081's `Sh0` axial shading (G-7,
  untouched) and `Im0`. Confirmed against 73 Separation and 32 DeviceN spaces across all 51 fixtures, by a
  walk that also recursed into Form-XObject and tiling-pattern resources — *further* than the original
  census — and still found only these two.
- **M3** — `t02-pass-a` `/CS0`: Subtype `NChannel`, `/Process /ColorSpace [/ICCBased 7 0 R]` with the
  stream dictionary `<< /Filter /FlateDecode /N 4 /Length 384790 >>`. Components:
  `Black` Process/tint 0/**ch 3**, `PrCyan` Process/0.36/**ch 0**, `PrMagenta` Process/0.57/**ch 1**,
  `PrYellow` Process/0.02/**ch 2**; `OwnAlternateCmyk` **null for all four** (Table 71 — a process
  colorant's `/Colorants` entry is ignored). Effective channel count **4** (derived: a listed index is
  returned only when `< channelCount`, `Black` got 3, and the only other possible values are 1 or null —
  null would have suppressed `Components`). **This is the compositor plan's input contract, and it holds.**
- **M4** — `/Process << /ColorSpace /DeviceGray /Components [/Ink1] >>` → `Ink1` = Process/**0**. The
  identical space under `/DeviceCMYK` → `Ink1` = Process/**0**. **Byte-identical carrier state.** Probed
  with an unlisted `/Black` alongside: under Gray `Black` → **null**, under CMYK `Black` → **3**,
  confirming the counts were 1 and 4 respectively. **Task 1 is justified and does not shrink.**
- **Bonus, validating Task 2's transposition test before it is written:** `/Components [/Black /Cyan]`
  under `/DeviceCMYK` over `[Black, Cyan, GWGGreen]` gives `Black` → ch **0**, `Cyan` → ch **1**,
  `GWGGreen` → Spot. Listed index does beat the canonical index.

## What this plan does NOT claim

- **No corpus instance proves the image change.** GWG081 is the corpus's only NChannel file, and M1
  measured its image splitting **identically** under both rules. The three veraPDF NChannel files exercise
  a **fill**, not an image. So the image split is covered by **synthetic fixtures only**, and the GWG
  gate's role here is to prove *silence*, not to prove the feature. Do not write "corpus-validated"
  anywhere about Task 2.
- **`StencilInkFromFill`'s change has no corpus instance either — this is stronger than the design knew.**
  M2 found **zero NChannel spaces in any page `/ColorSpace` resource** across all 51 GWG fixtures. Both
  NChannel instances are a shading and an image XObject. So there is no NChannel fill, stroke *or stencil*
  anywhere in GWG, and a stencil reaches `StencilInkFromFill` only from a fill's colorant origin. Task 2's
  stencil half is therefore pinned by its shared `SplitByComponents` helper and by nothing else — worth
  stating rather than letting the GWG green run imply coverage it does not have.
- **No colour conversion through an ICC process space.** `ProcessChannelCount` is a count, nothing more.
- **Shadings and meshes are untouched** (`ShadingSpotInk`/`MeshSpotInk` keep their name split). That is
  **G-7**, unchanged.
- **Spot reversion for images is out of scope and stays out.** An image's spot tint varies per pixel, and
  the per-pixel own-alternate colour is not carried anywhere; an unregistered spot still drops the whole
  image to the whole-space flatten, exactly as today. Reversion lands for fills/strokes only, in the
  compositor plan. **New gap — record it in the matrix (Task 4).**

## Global Constraints

- **BASE = `0c0f3db`** (master, Pass 2a′ merged and pushed). Branch: `colour/pass2b-engine-nchannel-image-split`.
- **Engine test baseline entering: 2625 passing / 0 failing.** Pellucid: 1278 / 0 / 78 skipped.
- **Zero GWG render-hash digests may move.** All 51. Unlike Pass 2a′ this is not merely expected, it is
  *predicted structurally*: every non-NChannel space returns `Components: null` and takes the unchanged
  name-split fallback, and Task 0 measures the one NChannel file before a production line is written. **A
  moved digest is a defect, not a result** — do not regenerate the baseline to make this plan pass.
- **Ghostscript is not the oracle.** Evidence is derived from the files.
- **Every new guard must be observed to fail by mutation when removed.** Re-running an implementer's claim
  is not verification; re-running the *mutation* is. A crash-mutation and a behaviour-mutation are not
  interchangeable.
- **Additive only on public types.** `ColorantOrigin`'s three-element positional constructor stays intact
  (seven Pellucid test files construct it positionally, and Pellucid is not repinned until Task 3).
- **`pack-local.ps1` drops the Skia pin on every run.** After every pack, re-add
  `<LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>` to
  `C:\Users\jorda\RiderProjects\Pellucid\Directory.Build.props.local` by hand. Dropped 6 times so far.

### Commands

```powershell
# Engine suite (run from C:\Users\jorda\RiderProjects\PDF)
dotnet test PdfLibrary.Tests

# One test by name
dotnet test PdfLibrary.Tests --filter FullyQualifiedName~<TestName>

# GWG render-hash gate (run from C:\Users\jorda\RiderProjects\Pellucid)
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~GwgRenderHashGateTests --logger "console;verbosity=detailed"
```

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `PdfLibrary/Rendering/ColorantOrigin.cs` | the per-op carrier | **Modify** — add `ProcessChannelCount` |
| `PdfLibrary/Rendering/ColorSpaceResolver.cs` | builds the carrier | **Modify** — `BuildComponents` emits the count; `OriginForColorSpaceObject` sets it |
| `PdfLibrary/Rendering/PdfImageToCmyk.cs` | image + stencil colorant split | **Modify** — per-component split with whole fallback |
| `PdfLibrary.Tests/Rendering/NChannelRampTests.cs` | existing NChannel engine tests | **Modify** — Task 1 tests (house pattern: NChannel engine tests live here) |
| `PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs` | existing image-split tests | **Modify** — Task 2 tests |
| `Docs/colour/rendering-conformance.md` | the gap matrix | **Modify** — Task 4 |
| `Docs/superpowers/specs/2026-07-26-colour-pass2-nchannel-per-component-design.md` | the design | **Modify** — Task 4 delivery correction |

---

### Task 0: Measure before building

**No commits. No production changes. Delete the scaffold in the same turn it is used** (global CLAUDE.md
rule — a stray scaffold silently diverges from the canonical tree). `git status` must be clean at the end.

This task exists because Pass 2a′'s Task 0 caught plan defect #10 before an implementer hit it, and
because three of this plan's claims are currently *predictions*, not facts.

**Files:** temporary console scaffold under
`C:\Users\jorda\AppData\Local\Temp\claude\C--Users-jorda-RiderProjects-Pellucid\<session>\scratchpad`
— **not** inside either repo.

- [ ] **Step 1: M1 — does GWG081's image split identically under both rules?**

Load `gwg-gos/.../GWG081*.pdf` (find it with `GwgCorpus.DiscoverAll()`'s path shape; it is the only
patch whose name starts `GWG081`). Walk the page's image XObjects; for each whose `/ColorSpace` resolves
to a Separation/DeviceN array (unwrapping `/Indexed` to its base at index 1), print:

- the colorant names, in order;
- `ColorSpaceResolver.OriginForColorSpaceObject(sepObj, null, doc)` → `Subtype`, and for each component
  `(Name, Role, ProcessChannel)`;
- the **name-split** answer for each name: `PageColorant.Classify(name)` and `ProcessPlate(name)`
  (re-implement `ProcessPlate`'s four-arm switch in the scaffold — it is private).

**Recorded prediction, made before running:** the space is NChannel with
`/Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow /Black] >>`, colorants
`[Black, GWG Green]`; so Black → Role Process, ProcessChannel 3 (== `ProcessPlate("Black")`), and
GWG Green → Role Spot (== `Classify` Spot), spot-plane index 0 in both rules. **Identical split ⇒ zero
digest movement.**

If the two rules **disagree** on any component, Task 3's gate criterion changes from "zero digests move"
to "GWG081 alone may move, and only for the measured reason" — and the disagreement must be written into
the ledger before Task 1 starts. Do not proceed on the assumption; write down what you actually saw.

- [ ] **Step 2: M2 — is there any OTHER NChannel image or stencil in the GWG corpus?**

Over all 51 fixtures, for every image XObject and every `/ColorSpace` resource, count spaces whose
`/Attributes /Subtype` is `NChannel`. The Pass 2 design's census says: 1 file (GWG081), 2 spaces (an axial
shading and an image behind `/Indexed`). **Confirm that number.** If a third NChannel space appears, the
"zero digests move" prediction is no longer supported by the census and Task 3's criterion must be
re-derived.

- [ ] **Step 3: M3 — what does `t02-pass-a` actually carry?**

For `veraPDF-corpus/PDF_A-2b/6.2 Graphics/6.2.4.4 Separation and DeviceN colour spaces/veraPDF test suite 6-2-4-4-t02-pass-a.pdf`,
resolve the page's `/CS0` colour space with `rawColor: [0.0, 0.36, 0.57, 0.02]` and print `Subtype` plus
every component's `(Name, Role, Tint, ProcessChannel, OwnAlternateCmyk)`. **Do not try to print
`ProcessChannelCount` — Task 1 has not added it yet.** Derive it instead, and show the derivation: a
*listed* name only receives its index when that index is `< channelCount`, and `ProcessChannelCount()`
returns only 4, 1 or null (null suppresses `Components` entirely).

**Recorded prediction:** names `[Black, PrCyan, PrMagenta, PrYellow]`, all four `Role.Process`,
`ProcessChannel` = `[3, 0, 1, 2]` (Black is at *space position 0* but *process channel 3* — the
transposition the design calls load-bearing), `OwnAlternateCmyk` null for all four (Table 71: a process
colorant's `/Colorants` entry is ignored), `ProcessChannelCount` 4 (an ICCBased stream with `/N 4`).

This is the **input contract for the compositor plan**. If it does not hold, that plan is built on sand —
which is precisely why it is not being written until this measurement exists.

- [ ] **Step 4: M4 — does a one-channel process space really hand out channel 0?**

Build an in-memory NChannel space with `/Process << /ColorSpace /DeviceGray /Components [/Ink1] >>` and
names `[/Ink1]`, resolve it, and print `(Role, ProcessChannel)`.

**`channelCount` is a local inside `BuildComponents` and is not observable from outside — do not try to
print it.** Probe it instead: add an *unlisted reserved* name to the same space (`[/Ink1 /Black]`), since
a reserved name receives its canonical index **only** when the count is 4. Run the identical space twice,
once under `/DeviceGray` and once under `/DeviceCMYK`, and compare. That gives direct evidence of the
count while `Ink1` reports channel 0 in both.

**Recorded prediction:** `Role.Process`, `ProcessChannel == 0` — i.e. indistinguishable, from the carrier
alone, from cyan under a four-channel space. This is the fact that justifies Task 1 existing at all. **If
this prediction is wrong, Task 1 shrinks or disappears — stop and re-plan rather than adding a property
nothing needs.**

- [ ] **Step 5: Report and clean up**

**Do not write the ledger here** — this task forbids commits and requires a clean `git status`, and Task 4
Step 3 is what creates the ledger file. Return every measured value **in your report**, as numbers, never
as "as predicted"; Task 4 transcribes them.

Delete the scaffold directory, then run `git status` in **both** repos and confirm each is clean
(Pellucid's untracked `website/` is pre-existing and expected; nothing else may appear).

---

### Task 1: Carry the process channel count on `ColorantOrigin`

**Files:**
- Modify: `PdfLibrary/Rendering/ColorantOrigin.cs`
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs:984-1006` (`OriginForColorSpaceObject`),
  `:1017-1113` (`BuildComponents`)
- Test: `PdfLibrary.Tests/Rendering/NChannelRampTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `public int? ColorantOrigin.ProcessChannelCount { get; init; }` — non-null **iff**
  `Components` is non-null; value ∈ {1, 4}. Task 2 and the compositor plan both gate on `== 4`.

**Axis A — what the input contains.** Every row of `ProcessChannelCount`'s existing table must be
reflected: absent `/Process`, absent/unreadable `/Process /ColorSpace`, `/DeviceCMYK`, `/DeviceGray`,
ICCBased `/N 4`, ICCBased `/N 1`, and every suppressing shape. Plus: not-NChannel.

**Axis B — what reading it resolves.** *This task adds no new resolution site.* `channelCount` is already
computed by `ProcessChannelCount(process, doc)` inside the existing `try` at `:1061`; this task only
stops discarding it. State that explicitly rather than assuming the reviewer will re-derive it — and note
the corollary: the existing `catch` at `:1084` leaves `channelCount` at its lowered-or-default value on
purpose, so the count this task surfaces is exactly the one `ProcessChannelFor` was already bounded by.
Do not add a second catch.

- [ ] **Step 1: Write the failing tests**

Add to `PdfLibrary.Tests/Rendering/NChannelRampTests.cs`, using that file's **existing** `Parse` /
`ParseWithDoc` helpers (`:37-59`) and its `WholeSpaceAlways09` constant (`:29-31`) — the house pattern is
a raw PDF source literal for the colour-space array, resolved out of the page's `/ColorSpace` resource as
`Cs0`. **Do not add a second fixture builder.** (`Parse` disposes its document and the existing tests then
pass `doc: null`; every literal below is fully direct, so nothing needs resolving.)

**Scope note — deliberately not re-testing `ProcessChannelCount()`'s own table.** Pass 2a′ Task 1 already
pins every row of it (`/DeviceCMYK`, `/DeviceGray`, absent, unreadable, ICCBased `/N 4`/`/N 1`/other,
unresolvable stream, `Count < 2`, every other family). These tests pin the *new* thing: that the value is
**surfaced faithfully and paired with `Components`**. The one exception is the ICCBased row, included
because it is the only path where the count comes from neither a default nor a literal name.

```csharp
[Fact]
public void NChannelOverDeviceCmyk_CarriesAProcessChannelCountOfFour()
{
    PdfArray space = Parse(
        "[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09
        + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK /Components [/Cyan] >> >>]");

    ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

    Assert.NotNull(origin);
    Assert.NotNull(origin!.Components);
    Assert.Equal(4, origin.ProcessChannelCount);
}

// THE LOAD-BEARING TEST. Ink1 is LISTED, so ProcessChannelFor answers from processChannels and returns
// index 0 (0 < 1, so it is in range) — the same value a /Cyan gets under a four-channel space. Only the
// COUNT tells the two apart, which is the entire reason this property exists. Task 2 and the compositor
// both refuse to place a component when this is not 4; if this test can pass with the count reported as
// 4, both refusals are unpinned and a gray colorant lands on the cyan plate.
[Fact]
public void ListedNameUnderAOneChannelProcessSpace_GetsChannelZeroButACountOfOne()
{
    PdfArray space = Parse(
        "[/DeviceN [/Ink1] /DeviceCMYK " + WholeSpaceAlways09
        + " << /Subtype /NChannel /Process << /ColorSpace /DeviceGray /Components [/Ink1] >> >>]");

    ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5], null);

    ColourantComponent ink1 = Assert.Single(origin!.Components!);
    Assert.Equal(ColourantRole.Process, ink1.Role);
    Assert.Equal(0, ink1.ProcessChannel);        // indistinguishable from cyan on its own …
    Assert.Equal(1, origin.ProcessChannelCount); // … until the count says otherwise
}

[Fact]
public void NChannelWithNoProcessDictionary_CarriesTheNoConstraintCountOfFour()
{
    PdfArray space = Parse(
        "[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09
        + " << /Subtype /NChannel >>]");

    ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

    Assert.NotNull(origin!.Components);
    Assert.Equal(4, origin.ProcessChannelCount);
}

// The ICCBased row: the count comes from the profile stream's /N, which must be an INDIRECT object, so
// this is the one case needing ParseWithDoc. /N 1 (not 4) so the assertion cannot pass by defaulting.
[Fact]
public void NChannelOverAnIccBasedGrayProcessSpace_CarriesACountOfOne()
{
    (PdfArray space, PdfDocument doc) = ParseWithDoc(
        "[/DeviceN [/Ink1] /DeviceCMYK " + WholeSpaceAlways09
        + " << /Subtype /NChannel /Process << /ColorSpace [/ICCBased 5 0 R] /Components [/Ink1] >> >>]",
        "<< /N 1 /Length 0 >> stream\nendstream");
    using (doc)
    {
        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5], doc);

        Assert.NotNull(origin!.Components);
        Assert.Equal(1, origin.ProcessChannelCount);
    }
}

// A suppressed component list must carry a suppressed count too: the two are one answer, and a consumer
// that saw ProcessChannelCount == 4 alongside Components == null would be told the space is
// four-channel-shaped when the engine has in fact declined to describe it at all.
[Fact]
public void NChannelOverAnUnsupportedProcessSpace_SuppressesBothComponentsAndTheCount()
{
    PdfArray space = Parse(
        "[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09
        + " << /Subtype /NChannel /Process << /ColorSpace /DeviceRGB /Components [/Cyan] >> >>]");

    ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

    Assert.NotNull(origin);
    Assert.Null(origin!.Components);
    Assert.Null(origin.ProcessChannelCount);
}

// A plain DeviceN has no per-component answer at all, so it has no count either.
[Fact]
public void PlainDeviceN_CarriesNoProcessChannelCount()
{
    PdfArray space = Parse("[/DeviceN [/Cyan /Spot1] /DeviceCMYK " + WholeSpaceAlways09 + "]");

    ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(space, [0.5, 0.5], null);

    Assert.NotNull(origin);
    Assert.Null(origin!.Components);
    Assert.Null(origin.ProcessChannelCount);
}
```

> **On the ICCBased fixture's `extraObjects` string:** `ParseWithDoc` forwards `params string[]
> extraObjects` to `ColourConformancePage.Build`, and the corrupt-reference tests at `:332` / `:360` use
> object number **5** for the first extra object. Read `ColourConformancePage.Build` to confirm the exact
> body syntax it expects for a *stream* (the existing call sites pass a lone `"]"`, a bare token) and
> adjust; if a stream is awkward there, drop this one test and note in the ledger that the ICCBased
> surfacing path is covered only by Task 0's M3 measurement on the real `t02-pass-a` file.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test PdfLibrary.Tests --filter FullyQualifiedName~ProcessChannelCount
```

Expected: **compile error** — `ColorantOrigin` has no member `ProcessChannelCount`. That is the correct
first failure. (Note the filter also needs to catch
`ListedNameUnderAOneChannelProcessSpace_GetsChannelZeroButACountOfOne`, which does not contain the
substring — run the whole `NChannelRampTests` class instead if so.)

- [ ] **Step 3: Add the property to `ColorantOrigin`**

Append inside the record body of `PdfLibrary/Rendering/ColorantOrigin.cs`, after `Components`:

```csharp
    /// <summary>The number of channels in this NChannel space's process colour space — 4 for
    /// <c>/DeviceCMYK</c>, for an ICCBased process space whose stream declares <c>/N 4</c>, and for the
    /// "no constraint" case (no <c>/Process</c> dictionary, or one whose <c>/ColorSpace</c> is absent or
    /// unreadable); 1 for <c>/DeviceGray</c> and ICCBased <c>/N 1</c>. <b>Non-null exactly when
    /// <see cref="Components"/> is non-null</b> — the two are one answer, and a suppressed component list
    /// suppresses this too.
    ///
    /// <para><b>Why a consumer needs this and cannot infer it.</b>
    /// <see cref="ColourantComponent.ProcessChannel"/> indexes the PROCESS space's channels, not the CMYK
    /// plates. Under a one-channel process space a name listed in <c>/Process /Components</c> still gets
    /// index 0 (it is in range for that space), which is byte-identical to the index a <c>/Cyan</c> gets
    /// under a four-channel space. A consumer mapping channel→plate must therefore check this is 4 before
    /// treating an index as a CMYK plate; at any other count the component is not placeable on plates and
    /// the consumer must fall back rather than guess. Mirrors the reasoning in
    /// <c>ProcessChannelFor</c>'s own one-channel rule, which refuses to guess for exactly the same
    /// reason.</para></summary>
    public int? ProcessChannelCount { get; init; }
```

- [ ] **Step 4: Emit the count from `BuildComponents`**

In `ColorSpaceResolver.cs`, change the signature at `:1017` and every `return` inside it:

```csharp
    private static IReadOnlyList<ColourantComponent>? BuildComponents(
        SpotColorSpace space, IReadOnlyList<double> tints, PdfDocument? doc, out int? processChannelCount)
    {
        processChannelCount = null;
        if (!space.IsNChannel) return null;
```

…and at the `ProcessChannelCount` suppression inside the `try` (currently `:1063`):

```csharp
                if (ProcessChannelCount(process, doc) is not { } count) return null;
```

leave exactly as-is — `processChannelCount` is still null there, which is what a suppressed list requires.

Then immediately before the component loop at `:1102`, add:

```csharp
        // Surfaced alongside the list, from the value ProcessChannelFor was already bounded by. Set HERE
        // rather than at each successful read: every early return above is a suppression, and a
        // suppressed list must carry a suppressed count (see ColorantOrigin.ProcessChannelCount). No new
        // dereference — `channelCount` is already resolved by the guarded read above, and its catch
        // deliberately leaves it lowered-or-default, so this is the same number the loop below uses.
        processChannelCount = channelCount;
```

- [ ] **Step 5: Set it on the origin**

In `OriginForColorSpaceObject`, replace the object initialiser at `:1001-1005`:

```csharp
        IReadOnlyList<ColourantComponent>? components =
            BuildComponents(space, tints, doc, out int? processChannelCount);
        return new ColorantOrigin(names, tints, space.AlternateSpaceName)
        {
            Subtype = space.Subtype,
            Components = components,
            ProcessChannelCount = processChannelCount,
        };
```

- [ ] **Step 6: Run the tests to verify they pass**

```powershell
dotnet test PdfLibrary.Tests
```

Expected: **2631 passing / 0 failing** (2625 + 6).

- [ ] **Step 7: Verify the load-bearing test by MUTATION, not by re-running it**

The finding class this guards against is "a test that proves nothing" (four occurrences in this
programme). Apply this mutation and confirm the named test goes red **for the right reason**:

```csharp
        processChannelCount = 4;   // MUTATION: report the no-constraint count unconditionally
```

Run `dotnet test PdfLibrary.Tests --filter FullyQualifiedName~NChannelRampTests`. Expected:
`ListedNameUnderAOneChannelProcessSpace_GetsChannelZeroButACountOfOne` **and**
`NChannelOverAnIccBasedGrayProcessSpace_CarriesACountOfOne` FAIL with an *assertion* failure
(`Assert.Equal` 1 vs 4) — **not** a crash, and not a compile error. Then revert the mutation, delete any
backup file, and confirm `git status` shows only the intended files.

(If the ICCBased fixture was dropped per Step 1's note, `ListedNameUnder…` alone must go red — and that is
then the *only* test standing between this property and a wrong answer, which is worth saying in the
ledger.)

Also run the second mutation, on the suppression path:

```csharp
        processChannelCount = 4;   // MUTATION: placed at the TOP of BuildComponents instead of null
```

Expected: `NChannelOverAnUnsupportedProcessSpace_SuppressesBothComponentsAndTheCount` and
`PlainDeviceN_CarriesNoProcessChannelCount` FAIL. Revert.

**Record both mutation outcomes in the ledger.** "Revert-verified" from an implementer is not evidence;
the observed red run is.

- [ ] **Step 8: Commit**

```bash
git add PdfLibrary/Rendering/ColorantOrigin.cs PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary.Tests/Rendering/NChannelRampTests.cs
git commit -m "feat(colour): carry the process channel count on ColorantOrigin

ColourantComponent.ProcessChannel indexes the PROCESS space's channels, so
channel 0 means cyan only when that space has four of them. A name listed in
/Process /Components under a /DeviceGray process space also gets index 0, and
nothing carried today tells the two apart. Surface the count BuildComponents
already computes so a consumer can refuse to place a component it cannot map
to a plate, instead of guessing.

Non-null exactly when Components is non-null: a suppressed component list
suppresses the count too. No new dereference — the value comes from the read
already guarded at BuildComponents' /Process try.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Split an NChannel image's colorants by role and channel

**Files:**
- Modify: `PdfLibrary/Rendering/PdfImageToCmyk.cs` — `TryToSpotInk:314-318`,
  `StencilInkFromFill:394-397`, plus two new private helpers and a `using Logging;`
- Test: `PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs`

**Interfaces:**
- Consumes: `ColorantOrigin.ProcessChannelCount` (Task 1), `ColorantOrigin.Components`,
  `ColourantComponent.{Name, Role, ProcessChannel}`.
- Produces: no signature change. `SpotImageInk`'s shape and meaning are unchanged — this task only
  changes *which colorant lands where* inside it, for NChannel spaces over a four-channel process space.

**The defect being closed.** `PageColorant.Classify("PrCyan")` → `Spot`, so a non-reserved process
colorant listed in `/Process /Components` is handed a spot plane. After Pass 2a′,
`PageColorantReader` classifies it `Process`, so `SpotColorantRegistry` allocates **no** plane for it, so
`CmykPageRenderer.cs:1157`'s `TryGetPlane` returns null, so `routeSpots` goes false and the **whole
image** flattens through the whole-space alternate. Its tint reaches neither a plate nor a plane. That is
the images half of the window Pass 2a′ recorded.

**Axis A — what the input contains.** Separation (1 name); plain DeviceN; NChannel over DeviceCMYK;
NChannel over DeviceGray; NChannel over an unsupported process space; NChannel containing `/None`;
NChannel containing `/All`; NChannel whose `/Components` transposes a reserved name to a non-canonical
channel; `/Indexed` over any of those; a component list whose length disagrees with the names array.

**Axis B — what reading it RESOLVES.** `OriginForColorSpaceObject` is a **new resolution site for this
file**. It runs `SpotColorSpace.TryParse` (which derefs the names array, the alternate, and the tint
transform) and then `BuildComponents` (which derefs `/Attributes`, `/Process /ColorSpace` — possibly an
ICC **stream** — and every element of `/Process /Components`). `BuildComponents` guards its own
`/Process` subtree, but `TryParse` ahead of it is **not** guarded from here, and
`PdfDocument.GetObject` wraps a corrupt on-demand object's parse failure in `PdfParseException`.
**Wrap the whole call at THIS call site, not per level** — the Pass 2a′ design rule that turned four
review rounds into one, because a call-site wrap covers arbitrary depth. `TryToSpotInk` returning null is
always safe (the caller keeps the RGBA/flatten path), so degrading is correct.

`StencilInkFromFill` adds **no** resolution site: it receives an already-built `ColorantOrigin`, and
`Components` is a materialised `List` on an init-only property that holds no `PdfDocument` (established
by the Pass 2a′ Task 3 review). Say so; do not wrap it "to be safe" — an unnecessary catch hides real
faults.

- [ ] **Step 1: Write the failing tests**

Add to `PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs`. First add two fixture builders next to the
existing `Separation`/`DeviceN` helpers at `:225-229`:

```csharp
    // An NChannel space: [/DeviceN [names] /DeviceCMYK <tint fn> << /Subtype /NChannel /Process <<…>> >>].
    // The tint-function slot is still never evaluated — the split reads roles and channels, not colour.
    private static PdfArray NChannel(PdfDictionary? process, params string[] names)
    {
        var attrs = new PdfDictionary { [new PdfName("Subtype")] = new PdfName("NChannel") };
        if (process is not null) attrs[new PdfName("Process")] = process;
        return new PdfArray(
            new PdfName("DeviceN"),
            new PdfArray(names.Select(n => (PdfObject)new PdfName(n)).ToArray()),
            new PdfName("DeviceCMYK"), new PdfName("Identity"), attrs);
    }

    private static PdfDictionary Process(string colorSpace, params string[] components) =>
        new()
        {
            [new PdfName("ColorSpace")] = new PdfName(colorSpace),
            [new PdfName("Components")] =
                new PdfArray(components.Select(n => (PdfObject)new PdfName(n)).ToArray()),
        };
```

Then the tests:

```csharp
// THE DEFECT THIS TASK CLOSES. PrCyan is a non-reserved name listed in /Process /Components, so it is a
// PROCESS colorant on channel 0. The name split calls it Spot (Classify sees an unreserved name) and
// hands it a spot plane the registry will never hold, dropping the whole image to the whole-space
// flatten. Per-component: its tint lands on the cyan plate and it is not a spot at all.
[Fact]
public void NChannel_nonReservedProcessName_paints_its_channel_not_a_spot_plane()
{
    // NChannel [PrCyan, GWG Green] over DeviceCMYK with /Components [PrCyan].
    // 2 px: (PrCyan 1.0, Green 0.5) then (PrCyan 0, Green 1.0).
    byte[] data = [255, 128, 0, 255];
    PdfImage img = Image(NChannel(Process("DeviceCMYK", "PrCyan"), "PrCyan", "GWG Green"), data, 2, 1);

    SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

    Assert.NotNull(ink);
    Assert.Equal(new[] { "GWG Green" }, ink!.Names);          // PrCyan is NOT a spot
    Assert.Equal(new byte[] { 128, 255 }, ink.TintPlanes);
    // PrCyan → process channel 0 = the CYAN plate, at its own per-pixel tint.
    Assert.Equal(new byte[] { 255, 0, 0, 0,  0, 0, 0, 0 }, ink.ProcessCmyk);
}

// Table 71 makes POSITION the channel identity, so a reserved name listed at a non-canonical index takes
// the listed one. Routing by name instead would transpose the colour visibly — the same failure
// veraPDF t02-pass-a is built to catch.
[Fact]
public void NChannel_listed_index_beats_the_reserved_name_canonical_index()
{
    // /Components [/Black /Cyan] ⇒ Black is process channel 0 (cyan plate), Cyan is channel 1 (magenta).
    // 1 px, 3 colorants: Black 1.0, Cyan 0.0, GWG Green ~0.5.
    PdfImage img = Image(NChannel(Process("DeviceCMYK", "Black", "Cyan"), "Black", "Cyan", "GWG Green"),
        [255, 0, 128], 1, 1);

    SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

    Assert.NotNull(ink);
    Assert.Equal(new[] { "GWG Green" }, ink!.Names);
    Assert.Equal(new byte[] { 128 }, ink.TintPlanes);
    // Black's 255 on channel 0, Cyan's 0 on channel 1 — NOT K=255 as the name split would give.
    Assert.Equal(new byte[] { 255, 0, 0, 0 }, ink.ProcessCmyk);
}

// A one-channel process space hands a listed name index 0, which is NOT the cyan plate. There is no
// mapping from a gray channel to plates here, so the per-component split must refuse ENTIRELY and the
// name split must handle it — Ink1 is unreserved, so it stays a spot exactly as before this task.
[Fact]
public void NChannel_over_a_gray_process_space_falls_back_to_the_name_split()
{
    byte[] data = [255, 128];
    PdfImage img = Image(NChannel(Process("DeviceGray", "Ink1"), "Ink1", "GWG Green"),
        [255, 64, 128, 32], 2, 1);

    SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

    Assert.NotNull(ink);
    Assert.Equal(new[] { "Ink1", "GWG Green" }, ink!.Names);   // BOTH spots — the name split's answer
    Assert.All(ink.ProcessCmyk, b => Assert.Equal((byte)0, b));
}

// The governing principle: one unplaceable component falls the WHOLE op back, never a half-split. /All
// means "every available colourant" and SpotImageInk cannot express that, so its presence disqualifies
// the per-component split — and the name split's existing All arm (contributes nothing) takes over.
[Fact]
public void NChannel_containing_All_falls_back_to_the_name_split_whole()
{
    PdfImage img = Image(NChannel(Process("DeviceCMYK", "Cyan"), "Cyan", "All", "GWG Green"),
        [255, 255, 128], 1, 1);

    SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

    Assert.NotNull(ink);
    Assert.Equal(new[] { "GWG Green" }, ink!.Names);           // All contributes nothing, as today
    Assert.Equal(new byte[] { 255, 0, 0, 0 }, ink.ProcessCmyk);// Cyan → plate 0 via the NAME split
}

// §8.6.6.5: /None components "shall never be painted on the page". Same answer under both rules — this
// test exists so a future per-component edit cannot silently start painting it.
[Fact]
public void NChannel_None_component_contributes_nothing()
{
    PdfImage img = Image(NChannel(Process("DeviceCMYK", "PrCyan"), "PrCyan", "None", "GWG Green"),
        [255, 255, 128], 1, 1);

    SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

    Assert.NotNull(ink);
    Assert.Equal(new[] { "GWG Green" }, ink!.Names);
    Assert.Equal(new byte[] { 255, 0, 0, 0 }, ink.ProcessCmyk);  // None's 255 landed nowhere
}

// The 50 unaffected GWG patches depend on this: a plain DeviceN carries Components == null, so the name
// split is untouched and the output is byte-identical to before this task.
[Fact]
public void PlainDeviceN_is_unaffected_by_the_perComponent_split()
{
    byte[] data = [255, 128, 0, 255];
    PdfImage img = Image(DeviceN("Black", "GWG Green"), data, 2, 1);

    SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, null, out _, out _);

    Assert.NotNull(ink);
    Assert.Equal(new[] { "GWG Green" }, ink!.Names);
    Assert.Equal(new byte[] { 128, 255 }, ink.TintPlanes);
    Assert.Equal(new byte[] { 0, 0, 0, 255,  0, 0, 0, 0 }, ink.ProcessCmyk);
}

// AXIS B GUARD. The /Process value is an indirect reference into a document whose xref marks the object
// IN USE but whose body does not parse, so GetObject's on-demand path wraps and RETHROWS
// PdfParseException — the technique Pass 2a-prime's review called "corrupt rather than absent by
// construction". A reference to a merely NON-EXISTENT object returns null without throwing and would
// make this test vacuous. Removing the try/catch at the ComponentSplit call site must make this THROW.
[Fact]
public void CorruptProcessReference_fallsBackToTheNameSplit_ratherThanThrowing()
{
    // Copy NChannelRampTests.ParseWithDoc (:50-59) into this file — the established convention in this
    // test project is a private per-file copy, not a shared helper (NChannelRampTests' own header
    // documents five existing files that each carry their own). It builds a page whose Cs0 resource is
    // the literal below, plus extra object 5 whose xref entry is IN USE but whose body is a lone ']'.
    // That is what makes the target GENUINELY corrupt: GetObject's on-demand path wraps and RETHROWS
    // PdfParseException, whereas a merely NON-EXISTENT object returns null without throwing and would
    // make this test vacuous. Same technique and same object number as
    // CorruptProcessComponentsReference_FallsBackToTheIsolatedEvaluation_RatherThanThrowing (:360).
    (PdfArray space, PdfDocument doc) = ParseWithDoc(
        "[/DeviceN [/Cyan /GWG Green] /DeviceCMYK "
        + "<< /FunctionType 2 /Domain [0 1 0 1] /C0 [0 0 0 0] /C1 [1 0 0 0] /N 1 "
        + "/Range [0 1 0 1 0 1 0 1] >> << /Subtype /NChannel /Process 5 0 R >>]", "]");
    using (doc)
    {
        PdfImage img = Image(space, [255, 128], 1, 1);

        SpotImageInk? ink = PdfImageToCmyk.TryToSpotInk(img, doc, out _, out _);

        // Degrades to the NAME split rather than throwing: Cyan ⇒ plate 0, GWG Green ⇒ a spot plane.
        Assert.NotNull(ink);
        Assert.Equal(new[] { "GWG Green" }, ink!.Names);
        Assert.Equal(new byte[] { 128 }, ink.TintPlanes);
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, ink.ProcessCmyk);
    }
}
```

> **Two things to check before trusting this test.**
> 1. **A `/GWG Green` name with a space in it is not valid PDF name syntax in a literal.** Use `/GWGGreen`
>    (or the `#20` escape) in the source string and assert on whatever name the parser actually produces.
>    The existing helpers' literals all use space-free names for this reason.
> 2. **`Image(...)` at `:225`-ish takes a `PdfObject` colour space.** Confirm its exact signature and that
>    it accepts a `PdfArray` rather than only a `PdfName`; the existing `Separation(...)`/`DeviceN(...)`
>    call sites pass arrays, so it should.
>
> **Verify the fixture is genuinely corrupt before relying on it** (Step 8, Mutation C): with the `catch`
> removed the test must throw `PdfParseException`. A test that passes because nothing ever threw is the
> fourth failure mode this programme has hit.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
dotnet test PdfLibrary.Tests --filter FullyQualifiedName~PdfImageToCmykTests
```

**Exactly two must FAIL**, and only these two — they are the only ones asserting behaviour that does not
exist yet:

- `NChannel_nonReservedProcessName_paints_its_channel_not_a_spot_plane` — reports `Names` as
  `["PrCyan", "GWG Green"]` and `ProcessCmyk` as all zero.
- `NChannel_listed_index_beats_the_reserved_name_canonical_index` — reports `ProcessCmyk` as
  `{0, 0, 0, 255}` (Black routed by name to K) instead of `{255, 0, 0, 0}`.

**The other five must PASS already.** `NChannel_over_a_gray_process_space_falls_back_to_the_name_split`,
`NChannel_containing_All_falls_back_to_the_name_split_whole`,
`NChannel_None_component_contributes_nothing`, `PlainDeviceN_is_unaffected_by_the_perComponent_split` and
`CorruptProcessReference_fallsBackToTheNameSplit_ratherThanThrowing` all assert *today's* answer — they
are regression anchors that must keep holding after Step 3, not new behaviour. A green run for them here
is the point.

**If a test in the second group FAILS now, stop**: the fixture is wrong, and fixing it after the
implementation would mean tuning the anchor to the new code — which is how an anchor stops anchoring.
Record which failed and which passed.

- [ ] **Step 3: Add the per-component splitter**

In `PdfImageToCmyk.cs`, add `using Logging;` to the usings (it is **not** currently imported; `PdfLogger`
lives in the top-level `Logging` namespace, not `PdfLibrary.Core`). Then add both helpers next to
`ProcessPlate` at `:437`:

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
    // ALL OR NOTHING, deliberately. One component this cannot place returns null and the caller uses the
    // name split for the WHOLE image. A half-per-component split would silently drop a colorant, which is
    // strictly worse than the status quo — the governing principle the Pass 2 design borrows from SP-6c.
    private static (int[] Plate, int[] SpotOf, List<string> SpotNames)? SplitByComponents(
        IReadOnlyList<ColourantComponent> comps)
    {
        var plate = new int[comps.Count];       // process → 0..3 ; otherwise -1
        var spotOf = new int[comps.Count];      // spot-plane index ; otherwise -1
        var spotNames = new List<string>();
        for (var c = 0; c < comps.Count; c++)
        {
            ColourantComponent cp = comps[c];
            switch (cp.Role)
            {
                // Table 71: a process colorant's own /Colorants entry "shall be ignored", which is why
                // nothing here consults it. The bound is belt-and-braces — the caller has already
                // required a four-channel process space, and ProcessChannelFor bounds the index by that
                // same count — but it is what makes "plate[c] is a CMYK plate" true at THIS level rather
                // than only at the call site's.
                case ColourantRole.Process when cp.ProcessChannel is >= 0 and <= 3:
                    plate[c] = cp.ProcessChannel!.Value; spotOf[c] = -1; break;

                // §8.6.6.5: /None "shall never be painted on the page". Contributes nothing to either
                // output — the same answer the name split's All/None arm gives.
                case ColourantRole.None:
                    plate[c] = -1; spotOf[c] = -1; break;

                // ColourantRole has no All member — RoleFor maps the reserved /All onto Spot, and
                // KindFor recovers the distinction downstream. /All means "every available colourant",
                // which SpotImageInk cannot express (it has no per-name "paint everything" channel), so
                // its presence disqualifies the per-component split rather than being demoted to an
                // ordinary spot plane.
                case ColourantRole.Spot when cp.Name == "All":
                    return null;

                case ColourantRole.Spot:
                    plate[c] = -1; spotOf[c] = spotNames.Count; spotNames.Add(cp.Name); break;

                // A Process component whose channel could not be determined. Unplaceable ⇒ fall back
                // whole. Never invent a plate for it.
                default:
                    return null;
            }
        }
        return (plate, spotOf, spotNames);
    }

    // Resolves the space's per-component carrier for TryToSpotInk. Null whenever the per-component split
    // does not apply, which the caller reads as "use the name split".
    //
    // GATED ON A FOUR-CHANNEL PROCESS SPACE. ColourantComponent.ProcessChannel indexes the PROCESS
    // space's channels, not the plates: under a /DeviceGray process space a listed name also gets index
    // 0, and painting it on cyan would be a colour error the name split never made. Four channels or
    // fall back — the conservative direction, matching ProcessChannelFor's own refusal to guess.
    //
    // WRAPPED AT THIS CALL SITE, not per level. OriginForColorSpaceObject resolves objects this file
    // never touched: SpotColorSpace.TryParse derefs the names array, the alternate and the tint
    // transform, and BuildComponents derefs /Attributes, /Process /ColorSpace (possibly an ICC stream)
    // and every /Components element. BuildComponents guards its own /Process subtree, but TryParse ahead
    // of it is not guarded from here, and PdfDocument.GetObject wraps a corrupt on-demand object's parse
    // failure in PdfParseException. A call-site wrap covers arbitrary DEPTH rather than exactly the level
    // it names — the design rule that took Pass 2a-prime from four review rounds to one. Returning null
    // is always safe here: the caller keeps the flatten/RGBA path it used before this method existed.
    private static (int[] Plate, int[] SpotOf, List<string> SpotNames)? ComponentSplit(
        PdfObject spaceObj, PdfDocument? document, int nameCount)
    {
        try
        {
            if (ColorSpaceResolver.OriginForColorSpaceObject(spaceObj, rawColor: null, document)
                is not { Components: { } comps, ProcessChannelCount: 4 }) return null;
            // The two lists are built from the same names array, so a disagreement means one of them is
            // not describing this space. Refuse rather than index across them.
            return comps.Count == nameCount ? SplitByComponents(comps) : null;
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, () =>
                $"TryToSpotInk: per-component split threw, falling back to the colorant-name split: {ex}");
            return null;
        }
    }
```

> **Note on cost:** `OriginForColorSpaceObject` is called with `rawColor: null`, so every component's
> `Tint` is null and `OwnAlternateFor` returns at its first line (`ColorSpaceResolver.cs:1272`) without
> building or evaluating a single tint transform. This runs once per image, not per pixel.

- [ ] **Step 4: Use it in `TryToSpotInk`**

Replace `PdfImageToCmyk.cs:310-318` (the `// Map each colorant → …` comment through
`if (spotNames.Count == 0) return null;`) with:

```csharp
        // Map each colorant → a process plate (0..3) or a spot-plane index. Prefer the per-component
        // answer (ISO 32000-2 §8.6.6.5) when the space is NChannel over a four-channel process space;
        // otherwise the colorant-NAME split, unchanged, which is what a Separation or a plain DeviceN
        // gets and is why the 50 non-NChannel GWG patches cannot move. Bail if no spot colorant.
        int[] plate;
        int[] spotOf;
        List<string> spotNames;
        if (ComponentSplit(sepObj, document, inC) is { } split)
        {
            (plate, spotOf, spotNames) = split;
        }
        else
        {
            plate = new int[inC];
            spotOf = new int[inC];
            spotNames = [];
            for (var c = 0; c < inC; c++)
                if (PageColorant.Classify(names[c]) == ColorantKind.Spot)
                { plate[c] = -1; spotOf[c] = spotNames.Count; spotNames.Add(names[c]); }
                else { plate[c] = ProcessPlate(names[c]); spotOf[c] = -1; }
        }
        if (spotNames.Count == 0) return null;
```

- [ ] **Step 5: Use it in `StencilInkFromFill`**

`StencilInkFromFill` already receives a built `ColorantOrigin`, so it needs no resolver call and adds
**no** resolution site — `Components` is a materialised `List` on an init-only property that holds no
`PdfDocument`. Replace `:391-398`:

```csharp
        // The same per-component preference as TryToSpotInk, so a stencil and an image of the same
        // colorants still decide the same way (SP-6d's stated invariant). No try/catch here and none
        // needed: Components is a materialised list already built and guarded upstream, not a lazy
        // handle onto the document.
        int[] plate;
        int[] spotOf;
        List<string> spotNames;
        if (origin is { Components: { } comps, ProcessChannelCount: 4 } && comps.Count == inC
            && SplitByComponents(comps) is { } split)
        {
            (plate, spotOf, spotNames) = split;
        }
        else
        {
            plate = new int[inC];
            spotOf = new int[inC];
            spotNames = [];
            for (var c = 0; c < inC; c++)
                if (PageColorant.Classify(origin.Names[c]) == ColorantKind.Spot)
                { plate[c] = -1; spotOf[c] = spotNames.Count; spotNames.Add(origin.Names[c]); }
                else { plate[c] = ProcessPlate(origin.Names[c]); spotOf[c] = -1; }
        }
        if (spotNames.Count == 0) return null;   // process-only fill → the RGBA path is fine (a non-goal)
```

- [ ] **Step 6: Update the two stale doc comments**

Both now say the split is by name, which stops being unconditionally true:

- `PageDrawList.cs:26-27` — "routes TintPlanes to the spot planes when the registry knows Names".
  Leave; still accurate.
- `PdfImageToCmyk.cs:269-274` (`TryToSpotInk`'s summary) — "Splits … BY NAME". Amend to: splits by
  **per-component role and channel** for an NChannel space over a four-channel process space, and by name
  otherwise; note the fallback is whole, never partial.
- `PdfImageToCmyk.cs:381-383` (`StencilInkFromFill`'s "The name split is SP-6a's, deliberately
  identical"). Amend to say both methods now prefer the same per-component split and fall back to the same
  name split, so the invariant that a stencil and an image of the same colorants decide alike is
  preserved — that is *why* both were changed together.
- `PdfImageToCmykTests.cs:223-224` — "The tint-function slot is never evaluated by TryToSpotInk (it
  splits by colorant NAME)". Amend: it is still never evaluated (the per-component path passes
  `rawColor: null`), but the reason is no longer "splits by name".

- [ ] **Step 7: Run the tests to verify they pass**

```powershell
dotnet test PdfLibrary.Tests
```

Expected: **2638 passing / 0 failing** (2631 + 7).

- [ ] **Step 8: Verify BOTH guards by MUTATION**

**Mutation A — the four-channel gate.** In `ComponentSplit`, widen it:

```csharp
                is not { Components: { } comps }) return null;   // MUTATION: drop ProcessChannelCount: 4
```

Expected: `NChannel_over_a_gray_process_space_falls_back_to_the_name_split` FAILS with an assertion
mismatch — `Names` comes back as `["GWG Green"]` (Ink1 having been placed on the cyan plate) instead of
`["Ink1", "GWG Green"]`. Revert.

**Mutation B — the all-or-nothing rule.** In `SplitByComponents`, change the `default:` arm from
`return null;` to `plate[c] = -1; spotOf[c] = -1; break;` (silently drop the unplaceable component).

Expected: at least one test FAILS. **If none does, the rule is unpinned** — add a fixture whose NChannel
space has a Process component with no determinable channel (a name listed at an index at or beyond the
channel count, e.g. `/Components [/A /B /C /D /PrCyan]` under `/DeviceCMYK`, which
`ProcessChannelFor:1243` rejects as out of range) and assert it falls back to the name split. Revert.

**Mutation C — the call-site wrap.** Delete the `catch` in `ComponentSplit` (make it a bare block).

Expected: `CorruptProcessReference_fallsBackToTheNameSplit_ratherThanThrowing` FAILS with an **unhandled
`PdfParseException`**, not with an assertion mismatch. A mismatch instead means the fixture is not
genuinely corrupt and the guard is unpinned — fix the fixture before proceeding. Revert.

Record all three outcomes in the ledger with the observed failure mode for each.

- [ ] **Step 9: Commit**

```bash
git add PdfLibrary/Rendering/PdfImageToCmyk.cs PdfLibrary.Tests/Rendering/PdfImageToCmykTests.cs
git commit -m "feat(colour): split an NChannel image's colorants by role and channel

TryToSpotInk and StencilInkFromFill split by colorant NAME, which is right for
a Separation or a plain DeviceN but misroutes two NChannel shapes: a
non-reserved process colorant (/PrCyan listed in /Process /Components) is
called Spot and handed a plane the registry never holds, dropping the whole
image to the whole-space flatten with its tint on neither a plate nor a plane;
and a reserved name listed at a non-canonical index is routed by name when
Table 71 makes POSITION the channel identity.

Both now prefer ColorantOrigin.Components when the space is NChannel over a
FOUR-channel process space, and fall back whole to the name split otherwise --
never a partial split, which would silently drop a colorant. Gated on the
channel count because ProcessChannel indexes the process space, not the plates:
under /DeviceGray a listed name also gets index 0.

No corpus instance exercises this; synthetic fixtures only. The GWG gate proves
silence, not the feature.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Gate — pack, repin, and prove the corpus did not move

**Files:** no source changes. Modifies `C:\Users\jorda\RiderProjects\Pellucid\Directory.Build.props.local`
and `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj` (both gitignored / outside this branch).

**Gate criterion:** **zero of 51 GWG digests move**, unless Task 0's M1 measured a disagreement, in which
case GWG081 alone may move for exactly that measured reason. Nothing else, ever. **Do not regenerate the
baseline.**

- [ ] **Step 1: Record the pins before touching anything**

```powershell
Get-Content C:\Users\jorda\RiderProjects\Pellucid\Directory.Build.props.local
Select-String -Path C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj -Pattern "Lxman"
```

Expected before: engine `2.5.1-dev20260726204055`, Skia `0.1.1-dev20260717153208`, PdfCompare on the same
engine version. Write them into the ledger.

- [ ] **Step 2: Pack the engine**

```powershell
cd C:\Users\jorda\RiderProjects\PDF
.\pack-local.ps1
```

Capture the new version string (`NEWVERSION`) from the output.

- [ ] **Step 3: Restore the Skia pin — it WILL have been dropped**

`pack-local.ps1` rewrites `Directory.Build.props.local` and drops the Skia line every single time (six
occurrences on record). Re-add by hand:

```xml
    <LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>
```

Then repin PdfCompare's `<PackageReference Include="Lxman.PdfLibrary" Version="…" />` to `NEWVERSION`.
Re-read both files and confirm all three pins are what you intend before running anything.

- [ ] **Step 4: Run the GWG render-hash gate**

```powershell
cd C:\Users\jorda\RiderProjects\Pellucid
dotnet test Pellucid.Rendering.Avalonia.Tests --filter FullyQualifiedName~GwgRenderHashGateTests --logger "console;verbosity=detailed"
```

Expected line: `51 fixtures hashed, 51 baselined, 0 differences. engine=<NEWVERSION-ish>+<SHA> icc=<…>`

**Check the embedded SHA equals this branch's HEAD.** A matching version *number* alone does not prove the
gate ran your code — NuGet can resolve a cached package of the same version. If the SHA is stale, clear
the NuGet cache for `Lxman.PdfLibrary` and re-run. Quote the full line in your report.

- [ ] **Step 5: Run both full suites**

```powershell
cd C:\Users\jorda\RiderProjects\PDF ; dotnet test PdfLibrary.Tests
cd C:\Users\jorda\RiderProjects\Pellucid ; dotnet test
```

Expected: engine **2638 / 0**; Pellucid **1278 passing / 0 failing / 78 skipped**. Pellucid's count must
not move — nothing in this plan touches the compositor. A moved Pellucid count is a finding, not a
rounding error.

- [ ] **Step 6: Commit the ledger, not the pins**

The pin files are gitignored or live outside the repo. Commit only the ledger update:

```bash
git add .superpowers/sdd/2026-07-26-colour-pass2b-engine/progress.md
git commit -m "chore(colour): record the Pass 2b-engine gate result

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Correct the design, the matrix, and the ledger

**Files:**
- Modify: `Docs/superpowers/specs/2026-07-26-colour-pass2-nchannel-per-component-design.md`
- Modify: `Docs/colour/rendering-conformance.md`
- Create/modify: `.superpowers/sdd/2026-07-26-colour-pass2b-engine/progress.md`

This task is not paperwork. Every one of the fourteen review findings in this programme originated in
plan or design *text*, and the specific defect this task fixes — a design that asserts a repo boundary the
code does not have — is the same class as Pass 2a′'s I-1 (a Global Constraint whose premise was false).

- [ ] **Step 1: Correct the design's Delivery and Scope sections**

In the design doc, mark the correction the same way the existing post-merge corrections are marked
(**CORRECTED (Pass 2b planning)**, inline, not by deleting the original claim):

- **Scope → In → "Pass 2b (compositor)"**: images are **not** a compositor change. Their colorant split is
  engine-side, in `PdfImageToCmyk.TryToSpotInk` / `StencilInkFromFill`; the `ColourantComponent` list
  never reaches `CmykPageRenderer.cs:1157`, which gates on `SpotImageInk.Names` against the registry.
- **Delivery → "two plans, not one"**: it is now **three** — Pass 2a′ (engine, merged `0c0f3db`), Pass
  2b-engine (this plan), Pass 2b-compositor (to be written after this merges, because Task 0's M1/M3
  measurements are its inputs).
- **Per-component rules table**: add the row that consuming `ProcessChannel` requires
  `ProcessChannelCount == 4`, and say why (channel 0 of a one-channel space is not the cyan plate).
- **The fixture that drives the design**: note that `t02-pass-a` is a **fill**, so the three veraPDF files
  give the *image* half of Pass 2b no coverage at all.

- [ ] **Step 2: Update the conformance matrix**

In `Docs/colour/rendering-conformance.md`, under **G-4**:

- Record that the images half of the Pass 2a′ routed→flattened window is **closed** for NChannel spaces
  over a four-channel process space, and still **open** for fills/strokes until Pass 2b-compositor lands.
- Record the **new gap**: *image spot reversion*. An NChannel image whose spot colorant has no registered
  plane still drops the whole image to the whole-space flatten. Per-pixel own-alternate colour is carried
  nowhere, so there is nothing to revert *to* at that site. Reversion lands for fills/strokes only.
- Record the **new gap**: *NChannel over a one-channel (`/DeviceGray` or ICCBased `/N 1`) process space
  is not placed per-component at all* — deliberately, since a gray channel has no plate mapping here.
- Note that neither the image split nor the channel-count gate has a corpus instance; both are covered by
  synthetic fixtures, and the GWG gate proves silence rather than correctness.

- [ ] **Step 3: Write the ledger**

Create `.superpowers/sdd/2026-07-26-colour-pass2b-engine/progress.md` following the shape of
`.superpowers/sdd/2026-07-26-colour-pass2a-prime/progress.md`: BASE, design/plan commit SHAs, entering
test counts, the pin values and the Skia-drop warning, Task 0's four measurements **as numbers**, each
task's commit SHA and test count, every mutation run with its observed failure mode, and every review
finding with whether it originated in plan text.

Carry forward the standing lessons verbatim — the two-axis review rule, "wrap at the call site not per
level", "when a finding IS *this test is vacuous*, re-run the MUTATION not the claim", and "a
crash-mutation and a behaviour-mutation are not interchangeable".

- [ ] **Step 4: Commit**

```bash
git add Docs/superpowers/specs/2026-07-26-colour-pass2-nchannel-per-component-design.md Docs/colour/rendering-conformance.md .superpowers/sdd/2026-07-26-colour-pass2b-engine/progress.md
git commit -m "docs(colour): correct Pass 2b's repo boundary and record the new gaps

The design scoped Pass 2b to the compositor. The image half is engine-side:
PdfImageToCmyk splits colorants by name and the ColourantComponent list never
reaches CmykPageRenderer. Pass 2b is therefore two plans, not one, and this is
the engine half.

Also records two new gaps: image spot reversion (no per-pixel own-alternate
colour is carried, so an unregistered spot still flattens the whole image) and
NChannel over a one-channel process space (no plate mapping for a gray
channel).

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Whole-branch review, before merge

Dispatch a review over `0c0f3db..HEAD` with these named risks — the ones this plan's author considers
most likely to be where a defect actually is:

1. **`ProcessChannelCount` non-null iff `Components` non-null.** Trace every `return` in `BuildComponents`
   and confirm no path leaves one set and the other not. A consumer gating on `{ Components: { } , ProcessChannelCount: 4 }`
   is only safe if that invariant holds.
2. **Does the per-component split ever differ from the name split for a space in the GWG corpus?** Verify
   against Task 0's M1/M2 numbers, not against this plan's prediction of them.
3. **Is `ComponentSplit`'s try/catch at the right level, and does anything reach `SplitByComponents`
   un-guarded?** `StencilInkFromFill` calls it with no try — confirm the claim that `Components` is a
   materialised list holding no `PdfDocument` is still true at HEAD, rather than inherited from the Pass
   2a′ review.
4. **Are the two `spotNames.Count == 0` bails still equivalent?** The per-component split can produce an
   empty `spotNames` for a space the name split would have produced a non-empty one for (an all-process
   NChannel image). Confirm returning null there is right — the caller falls to `TryToCmyk`/RGBA, which
   for an all-process NChannel image over DeviceCMYK is the correct flatten, not a lost colorant.
5. **Did any test go green for the wrong reason?** For each new test, name the mutation that was observed
   to make it red and the *kind* of failure (assertion vs throw).

Expect findings. Fourteen for fourteen so far have been in plan text — read each one as "which sentence
of this document was wrong?" before reading it as an implementer error.

## Success criteria

- `ColorantOrigin.ProcessChannelCount` is non-null exactly when `Components` is, pinned by a test that
  fails under the "report 4 unconditionally" mutation.
- An NChannel image with a non-reserved process colorant paints that colorant's tint on its own plate and
  does not consume a spot plane — pinned by
  `NChannel_nonReservedProcessName_paints_its_channel_not_a_spot_plane`.
- A listed index beats a reserved name's canonical index, pinned by a test that would go red under a
  name-routed implementation.
- An NChannel space over a one-channel process space falls back **whole** to the name split, pinned by a
  test that fails when the `ProcessChannelCount: 4` gate is dropped.
- A corrupt `/Process` reference degrades to the name split rather than throwing, pinned by a fixture
  observed to throw `PdfParseException` when the catch is removed.
- Engine 2638 / 0; Pellucid 1278 / 0 / 78 unchanged.
- GWG gate: **51 hashed, 51 baselined, 0 differences**, with the embedded SHA equal to HEAD.
- The design, the matrix and the ledger all record the corrected repo boundary and the two new gaps.
