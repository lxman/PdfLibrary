# Colour G-7 Plan 1 — colorant placement on the carrier

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a colorant→slot placement table on `ColorantOrigin`, computed once where `/Process` is
read, and make the shading and mesh spot split consume it instead of switching on literal reserved
colorant names.

**Architecture:** `ColorSpaceResolver.BuildComponents` already computes role, channel and channel-count
for every NChannel space — including for shadings, whose component list is fully role-classified today
and read by nobody. A pure function turns that materialised data into a placement table; the table's
single nullability rule carries the count-4 gate, the all-or-nothing rule and `/None` handling that
four consumers currently re-implement. Nothing new is dereferenced.

**Tech Stack:** C# / .NET (PdfLibrary multi-targets net8.0/net9.0/net10.0; PdfLibrary.Tests targets
net10.0 only), xUnit.

**Design:** `Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md` (`c812a2d`).

**This is Plan 1 of the delivery in design §6.2** — the engine carrier plus site 3. Site 4 (the
compositor mask / preserve signal), site 5 (`BuildCmykMapper`'s all-process arm, gated on M5) and the
migration of the two shipped Pass 2b sites (gated on M4) are later plans. **The delivery count is
provisional** — Pass 2b's design said two plans and it was three.

## Global Constraints

- **BASE** = PDF `master` @ `c812a2d`. Branch `colour/g7-carrier-placement`.
- Entering baselines, verified before any change: **engine 2643 passing / 0 failing**; Pellucid
  **1304 / 0 / 78**. Build is **0 warnings** across net8.0/net9.0/net10.0 (`8c506d0` closed the last two).
- **`.superpowers/` is gitignored in BOTH repos.** The ledger lives on disk. Never write a step that
  commits it.
- **NEVER `git add -A` in the Pellucid repo** — there is a pre-existing untracked `website/` that is
  not ours.
- **Every assertion is a positional per-plate assertion, or it is decorative.** The defect is a
  permutation: `(0, 0.36, 0.57, 0.02)` → `(0.36, 0.57, 0.02, 0)` has an identical multiset, sum, max
  and total ink. Assertions phrased as total ink, sum, max, `Assert.Contains` or a loose ΔE **pass both
  ways** (design §5.2).
- **Every prescribed mutation names which assertion in which fixture changes value.** If it cannot be
  named, it is decorative and must be replaced (design §5.4).
- **A "must already pass" classification is a prediction.** Verify it; do not assert it. Pass 2b got
  one of seven wrong.
- **The placement table MUST NOT dereference anything** (design §2.5). It is a pure function of
  already-materialised values.
- Do not repack the engine or repin Pellucid until Task 3.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `PdfLibrary/Rendering/ColorantPlacement.cs` | **new.** The slot type, the placement record, and the pure `Build` function. |
| `PdfLibrary/Rendering/ColorantOrigin.cs` | **modify.** Add the `Placement` init-only property + its contract docs. |
| `PdfLibrary/Rendering/ColorSpaceResolver.cs` | **modify** (`OriginForColorSpaceObject`, ~`:1016-1021`). Populate `Placement`. |
| `PdfLibrary/Rendering/ShadingSpotSplit.cs` | **modify.** Add the placement-driven split beside the name-driven one. |
| `PdfLibrary/Rendering/ShadingBuilder.cs` | **modify** (~`:73-97`). Prefer placement for spot names + split. |
| `PdfLibrary/Rendering/MeshShadingReader.cs` | **modify** (~`:58-68`). Same, per-vertex. |
| `PdfLibrary.Tests/Rendering/ColorantPlacementTests.cs` | **new.** Unit tests for `Build`, plus resolver-level tests through real PDF fixtures. |
| `PdfLibrary.Tests/Rendering/ShadingSpotSplitTests.cs` | **modify.** Placement-driven split cases. |

---

## Task 0: Measurement — no commits

**This task fixes the scope of Tasks 2 onward. It writes no production code and makes no commits.**
Both trees must be clean when it ends; delete any scaffold.

**Files:** none committed. Scratch probes under
`C:\Users\jorda\AppData\Local\Temp\claude\...\scratchpad`, deleted at the end.

**Interfaces:**
- Consumes: nothing.
- Produces: the six measurements below, written to the ledger at
  `PDF/.superpowers/sdd/2026-07-27-colour-g7-carrier/progress.md`, and a **SCOPE VERDICT** that
  amends this plan.

- [ ] **Step 1: Verify the entering baselines rather than trusting this plan**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git log --oneline -1                 # expect c812a2d
git status --porcelain               # expect empty
dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --no-incremental 2>&1 | grep -E "Warning|error"
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug 2>&1 | tail -3
```

Expected: `0 Warning(s)`, `Passed! - Failed: 0, Passed: 2643`. **If either differs, STOP and report** —
the plan's premises are stale.

- [ ] **Step 2: M3 — is the carrier actually populated for a shading?**

Design §2.1 rests on this. Write a throwaway probe that builds an NChannel axial shading and prints
the origin `ShadingBuilder` resolves for it.

Print, for the shading's `ColorantOrigin`: `Subtype`, `ProcessChannelCount`, and for each component
`Name / Role / Tint / ProcessChannel / OwnAlternateCmyk`.

Expected (from `ColorantOrigin`'s own XML docs): `Components` non-null and fully role-classified,
every `Tint` **null**, every `OwnAlternateCmyk` **null**, `ProcessChannel` **populated**.
**Record the actual values.** If `Components` is null for a shading, §2.1 is false and the whole
design returns for revision.

- [ ] **Step 3: M6 — does placement need a dereference?**

Read `ColorSpaceResolver.BuildComponents` (`:1039` to its `return components;`) and
`OriginForColorSpaceObject` (`:997-1021`). Answer in writing: can a slot be derived for every
component from `ColourantComponent.Role`, `.Name`, `.ProcessChannel` and `processChannelCount` alone,
with no call that resolves a `PdfObject`?

Expected: yes — all four are locals or already-built record fields at that point. **If no, STOP:**
design §2.5's constraint is void, and per scope rule #4 the design returns for revision before any
implementation.

- [ ] **Step 4: M1 — what each open site paints today**

For one fixture per open site, record **per-plate values at a named pixel**, the **overprint arm
taken**, and the **`InkSourceCategory`**. Colour alone is insufficient — Pass 2b-engine's I-1 was
invisible in colour and showed only in the category.

**M1c is the one this plan turns on, and it must be answered explicitly:**

> For an **all-process** NChannel shading (every component Process, e.g. `[PrCyan PrMagenta PrYellow
> Black]` with `/Process /Components` naming all four): does `PageColorantReader` register
> `PrCyan`/`PrMagenta`/`PrYellow` as **spot planes**, and does `routeShadingSpots` therefore **succeed**
> today?

Why it decides the task: today `ShadingSpotSplit.SpotNames` classifies `PrCyan` as a spot (it is not a
reserved name), so `splitSpots` is true and spot ink is built. Under placement those components are
**Process**, so `SpotNames` is empty, `splitSpots` is false, and **no spot ink is built at all**.

- If the registry does **not** register them, `routeShadingSpots` already fails today and the op
  already flattens → the change is a no-op on the arm and Task 2 proceeds.
- If the registry **does** register them, the op routes today (onto bogus spot planes) and would
  flatten after → **that is a category change**, and per design §6.1 rule 3 Task 2 is **BLOCKED**
  pending the compositor mask, because the preserve signal that would fund it lives in Plan 2.

- [ ] **Step 5: M2 — corpus census**

Enumerate every NChannel shading and mesh across the GWG corpus (51 fixtures) and the veraPDF files.
Recurse into Form-XObject and tiling-pattern resources, as Pass 2b-engine's census did.

Prediction on record, to be confirmed or refuted: the only NChannel shading is **GWG081 `Sh0`**,
`[Black, GWG Green]`, where Black is both a reserved name and `/Process` channel 3 — so name-split and
placement **agree**, and **no digest moves**.

- [ ] **Step 6: M4 and M5**

**M4:** for each corpus instance reaching Pass 2b's two shipped sites (`PdfImageToCmyk`,
`InkDecider.TryPerComponent`), record whether the placement table and the existing split agree
component-for-component. This is the precondition for the later migration plan; record it, do not act
on it.

**M5:** read element 2 (the alternate) of veraPDF `6-2-4-4-t02-pass-a`'s `/CS0` array and record
whether it resolves to CMYK — i.e. whether a shading of that space would get `toCmyk` non-null. This
gates site 5 in a later plan; record it, do not act on it.

- [ ] **Step 7: Record the SCOPE VERDICT and clean up**

Write all six measurements to the ledger as **numbers, not "as predicted"**. Then state the verdict:

1. Does Task 2 proceed, or is it blocked by M1c?
2. Any plan defect found while measuring — record it against this plan's text, since that is where
   every finding in this programme has originated so far (22 for 22 through Pass 2b).

Delete every scratch probe. Verify both trees:

```bash
cd /c/Users/jorda/RiderProjects/PDF && git status --porcelain     # expect empty
cd /c/Users/jorda/RiderProjects/Pellucid && git status --porcelain # expect ONLY "?? website/"
```

**No commits in this task.**

---

## Task 1: `ColorantPlacement` on the carrier

**Files:**
- Create: `PdfLibrary/Rendering/ColorantPlacement.cs`
- Modify: `PdfLibrary/Rendering/ColorantOrigin.cs`
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` (`OriginForColorSpaceObject`, ~`:1016-1021`)
- Test: `PdfLibrary.Tests/Rendering/ColorantPlacementTests.cs` (new)

**Interfaces:**
- Consumes: `ColourantComponent(string Name, ColourantRole Role, double? Tint, IReadOnlyList<double>? OwnAlternateCmyk, int? ProcessChannel)`; `ColourantRole { Spot, Process, None }`; `ColorantOrigin.Components`, `.ProcessChannelCount`.
- Produces, relied on by Task 2:
  - `enum ColorantSlotKind { Nothing, Plate, Spot }`
  - `readonly record struct ColorantSlot(ColorantSlotKind Kind, int Index)` with statics `ColorantSlot.Nothing`, `ColorantSlot.Plate(int plateIndex)`, `ColorantSlot.Spot(int spotIndex)`
  - `sealed record ColorantPlacement(IReadOnlyList<ColorantSlot> Slots, IReadOnlyList<string> SpotNames)`
  - `static ColorantPlacement? ColorantPlacement.Build(IReadOnlyList<ColourantComponent>? components, int? processChannelCount)`
  - `ColorantOrigin.Placement` — `ColorantPlacement?`, init-only.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Rendering/ColorantPlacementTests.cs`:

```csharp
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The placement table: one colorant → one slot. ISO 32000-2 Table 71 makes POSITION the channel
/// identity for a /Process component, which a name cannot carry — so these tests assert slots
/// POSITIONALLY. A permutation of the same slots has the same multiset and would pass any
/// count/sum/contains assertion (design §5.2).
/// </summary>
public class ColorantPlacementTests
{
    private static ColourantComponent Process(string name, int channel) =>
        new(name, ColourantRole.Process, null, null, channel);

    private static ColourantComponent Spot(string name) =>
        new(name, ColourantRole.Spot, null, null, null);

    private static ColourantComponent None() =>
        new("None", ColourantRole.None, null, null, null);

    // --- the table is built when every component is placeable and the count is 4 ---

    [Fact]
    public void ListedProcessNames_TakeTheirListedChannel_NotTheirName()
    {
        // The whole point: none of these three is a reserved name, and each must land on the plate its
        // /Process /Components POSITION gives it.
        ColorantPlacement? p = ColorantPlacement.Build(
            [Process("PrCyan", 0), Process("PrMagenta", 1), Process("PrYellow", 2), Process("Black", 3)], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Plate(1), p.Slots[1]);
        Assert.Equal(ColorantSlot.Plate(2), p.Slots[2]);
        Assert.Equal(ColorantSlot.Plate(3), p.Slots[3]);
        Assert.Empty(p.SpotNames);
    }

    [Fact]
    public void TranspositionIsVisible_ListedIndexBeatsCanonicalName()
    {
        // /Components [/Black /Cyan] — Black listed at 0, Cyan at 1. The canonical answer would be
        // Black→3, Cyan→0. Listed position must win, and a positional assert is the only thing that
        // can see the difference.
        ColorantPlacement? p = ColorantPlacement.Build([Process("Black", 0), Process("Cyan", 1)], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);   // Black on CYAN's plate, as listed
        Assert.Equal(ColorantSlot.Plate(1), p.Slots[1]);
    }

    [Fact]
    public void SpotsGetSequentialSlots_AndSpotNamesInThatOrder()
    {
        ColorantPlacement? p = ColorantPlacement.Build(
            [Spot("GWG Green"), Process("Cyan", 0), Spot("PANTONE 032 C")], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Spot(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Plate(0), p.Slots[1]);
        Assert.Equal(ColorantSlot.Spot(1), p.Slots[2]);
        Assert.Equal(new[] { "GWG Green", "PANTONE 032 C" }, p.SpotNames);
    }

    [Fact]
    public void NoneIsAPlacement_NotARefusal()
    {
        // /None is a colorant the printer deliberately does not run. It must NOT refuse the table.
        ColorantPlacement? p = ColorantPlacement.Build([Process("Cyan", 0), None()], 4);

        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Nothing, p.Slots[1]);
    }

    // --- the table is refused, whole, in exactly these cases ---

    [Fact]
    public void AllRefusesTheTable_EvenThoughItsRoleIsSpot()
    {
        // RoleFor maps "All" => ColourantRole.Spot, so the ROLE cannot distinguish it. Only the NAME
        // can. If this test fails, /All is silently being routed to a spot slot.
        Assert.Null(ColorantPlacement.Build([Process("Cyan", 0), Spot("All")], 4));
    }

    [Fact]
    public void AProcessComponentWithNoDeterminableChannel_RefusesTheWholeTable()
    {
        // All-or-nothing: one unplaceable component and the consumer must fall back WHOLE. Pass 2b's
        // equivalent rule was found silently unpinned; this is its pin.
        Assert.Null(ColorantPlacement.Build(
            [Process("Cyan", 0), new("PlateX", ColourantRole.Process, null, null, null)], 4));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(null)]
    public void AnyChannelCountOtherThanFour_RefusesTheTable(int? count)
    {
        // A channel index is a PLATE index only under a four-channel process space. Under /DeviceGray a
        // listed name also gets index 0 — byte-identical to a /Cyan under CMYK.
        Assert.Null(ColorantPlacement.Build([Process("Ink1", 0)], count));
    }

    [Fact]
    public void NullComponents_RefusesTheTable()
    {
        Assert.Null(ColorantPlacement.Build(null, 4));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd /c/Users/jorda/RiderProjects/PDF
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug --filter "FullyQualifiedName~ColorantPlacementTests"
```

Expected: **build failure**, `CS0103`/`CS0246` — `ColorantPlacement`, `ColorantSlot` and
`ColorantSlotKind` do not exist. A compile failure is the correct first failure here; do not "fix" it
by stubbing before Step 3.

- [ ] **Step 3: Create the placement type**

Create `PdfLibrary/Rendering/ColorantPlacement.cs`:

```csharp
namespace PdfLibrary.Rendering;

/// <summary>Which kind of destination a colorant is placed on.</summary>
public enum ColorantSlotKind
{
    /// <summary><c>/None</c> — a colorant that is deliberately never run. A placement, not a failure.</summary>
    Nothing,

    /// <summary>A process plate, identified by its index in the four-channel process space.</summary>
    Plate,

    /// <summary>A spot colorant, identified by its index into
    /// <see cref="ColorantPlacement.SpotNames"/>. Whether a UNIT exists for it is the compositor's
    /// question — see <see cref="ColorantPlacement"/>.</summary>
    Spot,
}

/// <summary>Where one colorant is placed. <paramref name="Index"/> is the plate index for
/// <see cref="ColorantSlotKind.Plate"/>, the spot index for <see cref="ColorantSlotKind.Spot"/>, and
/// meaningless (0) for <see cref="ColorantSlotKind.Nothing"/>.</summary>
public readonly record struct ColorantSlot(ColorantSlotKind Kind, int Index)
{
    public static ColorantSlot Nothing => new(ColorantSlotKind.Nothing, 0);
    public static ColorantSlot Plate(int plateIndex) => new(ColorantSlotKind.Plate, plateIndex);
    public static ColorantSlot Spot(int spotIndex) => new(ColorantSlotKind.Spot, spotIndex);
}

/// <summary>
/// Which output colorant each component of an NChannel space belongs to — computed once, where
/// <c>/Process</c> is read, instead of re-derived by every consumer.
///
/// <para><b>The physical statement.</b> A press has UNITS; each carries one ink and one plate.
/// ISO 32000-2 Table 71's <c>/Process /Components</c> says which named colorant IS which unit, by
/// POSITION — a name cannot carry that (consider a plate named <c>/PlateX</c>). The alternate colour
/// space and tint transform are the recipe for simulating an ink you have no unit for. §8.6.6.5 then
/// reduces to one instruction: <i>run the real ink where you have the unit, simulate only where you
/// don't.</i> This table is the first half of that answer.</para>
///
/// <para><b>The boundary this type does not cross.</b> It says "spot slot 2". It never says "spot slot
/// 2 has a unit" — registration is a registry fact and the registry is compositor-side. The carrier
/// answers <i>which colorant is this</i>; the compositor answers <i>do we have that unit</i>.</para>
/// </summary>
/// <param name="Slots">One slot per component, aligned index-for-index with
/// <see cref="ColorantOrigin.Names"/> and <see cref="ColorantOrigin.Components"/>.</param>
/// <param name="SpotNames">The spot colorant names, in slot order. Empty when every component is
/// Process or None.</param>
public sealed record ColorantPlacement(
    IReadOnlyList<ColorantSlot> Slots,
    IReadOnlyList<string> SpotNames)
{
    /// <summary>
    /// Builds the table, or returns null meaning "fall back to whole-space behaviour".
    ///
    /// <para><b>Null in exactly three cases</b>, and this single nullability rule is why consumers do
    /// not each re-implement them:</para>
    /// <list type="number">
    /// <item><b>No components</b> — not an NChannel space, so there is nothing per-component to say.</item>
    /// <item><b><paramref name="processChannelCount"/> is not 4.</b> A channel index is a PLATE index
    /// only under a four-channel process space; under <c>/DeviceGray</c> a listed name also gets index
    /// 0, byte-identical to a <c>/Cyan</c> under CMYK. See
    /// <see cref="ColorantOrigin.ProcessChannelCount"/>.</item>
    /// <item><b>Any component is unplaceable</b> — a Process component whose channel could not be
    /// determined, or an <c>/All</c>. All-or-nothing: one unplaceable colorant and the whole space
    /// falls back, because a partly-placed space would put some ink on the right unit and silently
    /// drop the rest.</item>
    /// </list>
    ///
    /// <para><b><c>/All</c> is detected by NAME, not by role.</b>
    /// <c>ColorSpaceResolver.RoleFor</c> maps <c>"All"</c> to <see cref="ColourantRole.Spot"/>, so the
    /// role cannot distinguish it and a role-only check would silently route <c>/All</c> to a spot
    /// slot. <c>/All</c> means "every colorant on the device at once", which is a different rule from
    /// placement and is out of scope here.</para>
    ///
    /// <para><b>Dereferences nothing.</b> Every value read is already materialised on
    /// <see cref="ColourantComponent"/> or is the count computed beside it, so this adds no resolution
    /// site and needs no <c>try</c>. That is deliberate: the dominant defect class in this programme is
    /// a new member access resolving a PDF object the previous code never touched and throwing out of a
    /// path that used to succeed.</para>
    /// </summary>
    public static ColorantPlacement? Build(
        IReadOnlyList<ColourantComponent>? components, int? processChannelCount)
    {
        if (components is null || processChannelCount != 4) return null;

        var slots = new ColorantSlot[components.Count];
        var spotNames = new List<string>();

        for (var i = 0; i < components.Count; i++)
        {
            ColourantComponent c = components[i];

            // Name, not Role — RoleFor collapses /All into Spot.
            if (c.Name == "All") return null;

            switch (c.Role)
            {
                case ColourantRole.None:
                    slots[i] = ColorantSlot.Nothing;
                    break;

                case ColourantRole.Process:
                    // Bounded by construction: ProcessChannelFor returns an index only when it is
                    // < channelCount, and channelCount IS processChannelCount, which is 4 here.
                    if (c.ProcessChannel is not { } channel) return null;
                    slots[i] = ColorantSlot.Plate(channel);
                    break;

                default:
                    slots[i] = ColorantSlot.Spot(spotNames.Count);
                    spotNames.Add(c.Name);
                    break;
            }
        }

        return new ColorantPlacement(slots, spotNames);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug --filter "FullyQualifiedName~ColorantPlacementTests"
```

Expected: **PASS**, 10 tests (7 `[Fact]` + 3 from the `[Theory]`'s `[InlineData]` rows). Count them
and record the actual number rather than trusting this line.

- [ ] **Step 5: Surface it on `ColorantOrigin`**

In `PdfLibrary/Rendering/ColorantOrigin.cs`, add after the `ProcessChannelCount` property:

```csharp
    /// <summary>Which output colorant each component belongs to (ISO 32000-2 Table 71), or null when
    /// this space must fall back to whole-space behaviour. See <see cref="ColorantPlacement.Build"/>
    /// for the three cases that produce null.
    ///
    /// <para><b>Non-null implies <see cref="Components"/> non-null and
    /// <see cref="ProcessChannelCount"/> == 4, but NOT the converse</b> — a component list can be
    /// fully populated and still be unplaceable (an <c>/All</c>, or a Process component whose channel
    /// could not be determined). A consumer that wants to place ink checks THIS, not
    /// <c>Components is not null</c>.</para>
    ///
    /// <para>Populated for shadings and meshes too. Those resolve their origin with no per-op colour,
    /// so every <see cref="ColourantComponent.Tint"/> is null — but placement does not depend on tint.
    /// Which unit a colorant belongs on is a property of the COLOUR SPACE, not of the paint operation:
    /// the shading supplies the per-stop values, this supplies the destinations.</para></summary>
    public ColorantPlacement? Placement { get; init; }
```

In `PdfLibrary/Rendering/ColorSpaceResolver.cs`, in `OriginForColorSpaceObject`, extend the object
initialiser (currently ~`:1016-1021`):

```csharp
        return new ColorantOrigin(names, tints, space.AlternateSpaceName)
        {
            Subtype = space.Subtype,
            Components = components,
            ProcessChannelCount = processChannelCount,
            Placement = ColorantPlacement.Build(components, processChannelCount),
        };
```

- [ ] **Step 6: Add the resolver-level tests**

Append to `PdfLibrary.Tests/Rendering/ColorantPlacementTests.cs`, inside the class. Add
`using PdfLibrary.Core.Primitives;`, `using PdfLibrary.Document;` and `using PdfLibrary.Structure;`
to the file's using block:

```csharp
    // --- through the real resolver, on real parsed PDF colour spaces ---

    private const string Tint4 = "<< /FunctionType 2 /Domain [0 1 0 1 0 1 0 1] "
        + "/C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>";

    private static PdfArray ParseCs(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    private static ColorantOrigin? OriginFor(string literal) =>
        ColorSpaceResolver.OriginForColorSpaceObject(ParseCs(literal), null, null);

    [Fact]
    public void Resolver_NChannelListedProcessNames_PlacesByListedPosition()
    {
        // /Process /Components names all four, so all four are Process and none is a reserved name.
        ColorantOrigin? o = OriginFor(
            "[/DeviceN [/PrCyan /PrMagenta /PrYellow /Black] /DeviceCMYK " + Tint4
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/PrCyan /PrMagenta /PrYellow /Black] >> >>]");

        Assert.NotNull(o);
        ColorantPlacement? p = o!.Placement;
        Assert.NotNull(p);
        Assert.Equal(ColorantSlot.Plate(0), p!.Slots[0]);
        Assert.Equal(ColorantSlot.Plate(1), p.Slots[1]);
        Assert.Equal(ColorantSlot.Plate(2), p.Slots[2]);
        Assert.Equal(ColorantSlot.Plate(3), p.Slots[3]);
        Assert.Empty(p.SpotNames);
    }

    [Fact]
    public void Resolver_PlainDeviceN_HasNoPlacement()
    {
        ColorantOrigin? o = OriginFor("[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint4 + "]");

        Assert.NotNull(o);
        Assert.Null(o!.Placement);
    }

    [Fact]
    public void Resolver_NChannelOverAOneChannelProcessSpace_HasNoPlacement()
    {
        // Ink1 gets ProcessChannel 0 under /DeviceGray, which is NOT the cyan plate.
        ColorantOrigin? o = OriginFor(
            "[/DeviceN [/Ink1] /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] "
            + "/C1 [1 1 1 1] /N 1 >> << /Subtype /NChannel /Process << /ColorSpace /DeviceGray "
            + "/Components [/Ink1] >> >>]");

        Assert.NotNull(o);
        Assert.NotNull(o!.Components);          // the carrier IS populated ...
        Assert.Null(o.Placement);               // ... and placement still refuses it
    }
```

- [ ] **Step 7: Run the full engine suite**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug 2>&1 | tail -3
```

Expected: **0 failed**, 2643 + the new count. Record the actual total.

- [ ] **Step 8: Verify the build is still warning-free on every TFM**

```bash
dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --no-incremental 2>&1 | grep -E "Warning\(s\)|error"
```

Expected: `0 Warning(s)`. A new public type on a multi-targeted library is the kind of change that
surfaces net8.0-only diagnostics, which the net10.0-only test run cannot see.

- [ ] **Step 9: Run the prescribed mutations**

Each names the assertion that must change value. Revert after each; confirm the tree is clean between.

| # | Mutation | Must go red, by ASSERTION |
|---|----------|---------------------------|
| A | Delete the `if (c.Name == "All") return null;` line | `AllRefusesTheTable_EvenThoughItsRoleIsSpot` — `Assert.Null` sees a non-null table |
| B | Change `processChannelCount != 4` to `processChannelCount is null` | `AnyChannelCountOtherThanFour_RefusesTheTable(1)` and `(3)`, and `Resolver_NChannelOverAOneChannelProcessSpace_HasNoPlacement` |
| C | Replace `if (c.ProcessChannel is not { } channel) return null;` with `int channel = c.ProcessChannel ?? 0;` | `AProcessComponentWithNoDeterminableChannel_RefusesTheWholeTable` — `Assert.Null` sees a table with `PlateX` on plate 0 |
| D | Change `slots[i] = ColorantSlot.Plate(channel)` to `Plate(i)` | `SpotsGetSequentialSlots_AndSpotNamesInThatOrder` — `Slots[1]` reads `Plate(1)` instead of `Plate(0)` |
| E | Route `ColourantRole.None` to `ColorantSlot.Spot(spotNames.Count)` | `NoneIsAPlacement_NotARefusal` — `Slots[1]` is `Spot(0)`, not `Nothing` |

**Mutation D's target fixture is chosen deliberately and the reasoning must not be shortcut.**
`Plate(i)` and `Plate(channel)` differ only where the component's *position in the names array*
differs from its *channel*. In `ListedProcessNames` (0,1,2,3 → 0,1,2,3) and in
`TranspositionIsVisible` (0,1 → 0,1) they are **equal**, so both fixtures stay green under D and
neither can observe it. Only `SpotsGetSequentialSlots` diverges — `Cyan` sits at index 1 and channel
0. Confirm D goes red *there*; if it goes red anywhere else as well, record that, but the named
fixture is the pin.

**Honest labelling required in the report.**
`TranspositionIsVisible_ListedIndexBeatsCanonicalName` is a **positive control**, not a pin on
`Build`. The listed-index-beats-canonical-name rule is enforced upstream by
`ColorSpaceResolver.ProcessChannelFor`, which has already resolved `ProcessChannel` before `Build`
sees it; no mutation of `Build` can make that test red. It earns its place by documenting the
contract `Build` consumes. Report it as a positive control — do not let the tally claim it as
mutation-pinned coverage (Pass 2b-engine's honest tally was 11 of 12 and 4 of 6; the number is the
point).

- [ ] **Step 10: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git add PdfLibrary/Rendering/ColorantPlacement.cs PdfLibrary/Rendering/ColorantOrigin.cs \
        PdfLibrary/Rendering/ColorSpaceResolver.cs \
        PdfLibrary.Tests/Rendering/ColorantPlacementTests.cs
git commit -m "feat(colour): place each NChannel colorant on the carrier, once

ColorSpaceResolver.BuildComponents is the only site that can see /Process, and
it already computes role, channel and channel count. ColorantPlacement turns
that materialised data into one colorant -> slot table, so consumers stop
re-deriving placement.

One nullability rule carries three rules that four consumers currently
re-implement: the count-4 gate, the all-or-nothing refusal (Pass 2b's
equivalent was found silently unpinned), and /None as a placement rather than
a failure. /All is detected by NAME because RoleFor collapses it into Spot.

Dereferences nothing -- every value read is already materialised -- so it adds
no resolution site and needs no try. Nothing consumes it yet.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: The shading and mesh split consume placement

**BLOCKED IF** Task 0's M1c showed that `routeShadingSpots` succeeds today for an all-process
NChannel shading. In that case this task changes an overprint category with no preserve signal to
fund it, and per design §6.1 rule 3 it stops and waits for Plan 2. **Read the Task 0 verdict before
Step 1.**

**Files:**
- Modify: `PdfLibrary/Rendering/ShadingSpotSplit.cs`
- Modify: `PdfLibrary/Rendering/ShadingBuilder.cs` (~`:73-97`)
- Modify: `PdfLibrary/Rendering/MeshShadingReader.cs` (~`:58-68`)
- Test: `PdfLibrary.Tests/Rendering/ShadingSpotSplitTests.cs`

**Interfaces:**
- Consumes from Task 1: `ColorantPlacement`, `ColorantSlot`, `ColorantSlotKind`, `ColorantOrigin.Placement`.
- Produces: `ShadingSpotSplit.SplitByPlacement(double[] comps, ColorantPlacement placement, byte[] spotDest, int destOffset)` returning the packed process CMYK as `uint` (`0xCCMMYYKK`), matching the existing `Split`.

- [ ] **Step 1: Confirm the task is not blocked**

Read the SCOPE VERDICT in `PDF/.superpowers/sdd/2026-07-27-colour-g7-carrier/progress.md`. If it says
Task 2 is blocked, **STOP and report** — do not proceed on the argument that the change looks safe.

- [ ] **Step 2: Write the failing tests**

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
        // THE defect, in one assertion. Names order (PrCyan, PrMagenta, PrYellow, Black) carries
        // components (0.0, 0.36, 0.57, 0.02). Split-by-name puts NONE of the first three on a plate.
        // Split-by-placement puts them on C, M, Y by their listed position.
        ColorantPlacement p = ColorantPlacement.Build(
            [Proc("PrCyan", 0), Proc("PrMagenta", 1), Proc("PrYellow", 2), Proc("Black", 3)], 4)!;

        uint proc = ShadingSpotSplit.SplitByPlacement([0.0, 0.36, 0.57, 0.02], p, [], 0);

        // Asserted PER PLATE. The same four values in any other order have the same sum, max and
        // multiset and would satisfy any aggregate assertion (design §5.2).
        Assert.Equal(0, (proc >> 24) & 0xFF);     // C
        Assert.Equal(92, (proc >> 16) & 0xFF);    // M = round(0.36*255)
        Assert.Equal(145, (proc >> 8) & 0xFF);    // Y = round(0.57*255)
        Assert.Equal(5, proc & 0xFF);             // K = round(0.02*255)
    }

    [Fact]
    public void SplitByPlacement_Transposed_FollowsThePlacementNotTheName()
    {
        // /Components [/Black /Cyan]: Black listed at channel 0 (the CYAN plate), Cyan at 1 (MAGENTA).
        ColorantPlacement p = ColorantPlacement.Build([Proc("Black", 0), Proc("Cyan", 1)], 4)!;

        uint proc = ShadingSpotSplit.SplitByPlacement([1.0, 0.5], p, [], 0);

        Assert.Equal(255, (proc >> 24) & 0xFF);   // Black's value on the C plate
        Assert.Equal(128, (proc >> 16) & 0xFF);   // Cyan's value on the M plate
        Assert.Equal(0, (proc >> 8) & 0xFF);
        Assert.Equal(0, proc & 0xFF);
    }

    [Fact]
    public void SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset()
    {
        ColorantPlacement p = ColorantPlacement.Build(
            [Sp("GWG Green"), Proc("Cyan", 0), Sp("PANTONE 032 C")], 4)!;
        var spot = new byte[6];   // 3 stops * 2 spots

        uint proc = ShadingSpotSplit.SplitByPlacement([0.5, 1.0, 0.2], p, spot, destOffset: 2);

        Assert.Equal(128, spot[2]);               // slot 0 at offset 2
        Assert.Equal(51, spot[3]);                // slot 1 at offset 2
        Assert.Equal(0xFF000000u, proc);          // Cyan 1.0 on the C plate
        Assert.Equal(0, spot[0]);                 // stop 0 untouched
    }

    [Fact]
    public void SplitByPlacement_NoneContributesNothing_ToAnyPlateOrSpot()
    {
        ColorantPlacement p = ColorantPlacement.Build(
            [Proc("Cyan", 0), new("None", ColourantRole.None, null, null, null)], 4)!;
        var spot = new byte[1];

        uint proc = ShadingSpotSplit.SplitByPlacement([0.25, 1.0], p, spot, 0);

        Assert.Equal(64, (proc >> 24) & 0xFF);    // Cyan only
        Assert.Equal(0u, proc & 0x00FFFFFFu);
        Assert.Equal(0, spot[0]);                 // /None's 1.0 went nowhere
    }
```

- [ ] **Step 3: Run to verify they fail**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug --filter "FullyQualifiedName~ShadingSpotSplitTests"
```

Expected: build failure, `CS0117` — `ShadingSpotSplit` has no `SplitByPlacement`.

- [ ] **Step 4: Add the placement-driven split**

In `PdfLibrary/Rendering/ShadingSpotSplit.cs`, add:

```csharp
    /// <summary>
    /// Splits <paramref name="comps"/> by <paramref name="placement"/> rather than by colorant name:
    /// each component goes to the plate or spot slot its NChannel <c>/Process /Components</c> position
    /// gives it (ISO 32000-2 Table 71). Returns the packed process CMYK (<c>0xCCMMYYKK</c>) and writes
    /// spot tints to <paramref name="spotDest"/> at <paramref name="destOffset"/> + slot index.
    ///
    /// <para>The name-driven <see cref="Split"/> remains for every space with no placement — a plain
    /// DeviceN, a Separation, an NChannel over a one-channel process space, or one carrying an
    /// <c>/All</c> or an unplaceable component. See <see cref="ColorantPlacement.Build"/>.</para>
    ///
    /// <para>No tint transform is used here, exactly as in <see cref="Split"/>: a spot's alternate is
    /// applied once at display via the registry ramp. A process component needs no alternate at all —
    /// it has a unit.</para>
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

- [ ] **Step 5: Run to verify they pass**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug --filter "FullyQualifiedName~ShadingSpotSplitTests"
```

Expected: PASS, existing tests plus the four new ones.

- [ ] **Step 6: Wire the axial/radial builder to it**

In `PdfLibrary/Rendering/ShadingBuilder.cs`, replace the spot-name derivation (~`:74-75`):

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

- [ ] **Step 7: Wire the mesh reader to it**

In `PdfLibrary/Rendering/MeshShadingReader.cs`, replace the spot-name derivation (~`:61-62`):

```csharp
        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(csObj, null, document);
        ColorantPlacement? placement = origin?.Placement;
        List<string> spotNames = placement is not null
            ? [.. placement.SpotNames]
            : origin is not null ? ShadingSpotSplit.SpotNames(origin.Names) : [];
```

and `hasProcess` (~`:66-67`), which must agree with the same rule:

```csharp
        bool hasProcess = placement is not null
            ? placement.Slots.Any(s => s.Kind == ColorantSlotKind.Plate)
            : origin is not null && origin.Names.Any(n => PageColorant.Classify(n) == ColorantKind.Process);
```

Then find every `ShadingSpotSplit.Split(` call in this file and give it the same
`placement is not null ? SplitByPlacement(...) : Split(...)` treatment as Step 6. **Grep for them
rather than trusting this plan to have listed them all:**

```bash
grep -n "ShadingSpotSplit.Split(" PdfLibrary/Rendering/MeshShadingReader.cs
```

- [ ] **Step 8: Run the full engine suite**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug 2>&1 | tail -3
dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --no-incremental 2>&1 | grep -E "Warning\(s\)|error"
```

Expected: 0 failed, 0 warnings. **If any pre-existing test changed its result, STOP and report which**
— M2 predicted the corpus does not move, and a moved test is that prediction failing.

- [ ] **Step 9: Run the prescribed mutations**

| # | Mutation | Must go red, by ASSERTION |
|---|----------|---------------------------|
| A | In `SplitByPlacement`, `plates[slot.Index] = v` → `plates[j] = v` | `SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset` — `Cyan` sits at index 1, channel 0, so `proc` reads `0x00FF0000` (M) instead of `0xFF000000` (C) |
| B | Drop the `Spot` arm (fall through to nothing) | `SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset` — `spot[2]` is 0, not 128 |
| C | Route `Nothing` to `plates[0]` | `SplitByPlacement_NoneContributesNothing_ToAnyPlateOrSpot` — C reads 255 |
| D | In `ShadingBuilder`, force `placement` to `null` | **Predicted: WHOLE SUITE GREEN.** See below |
| E | In `MeshShadingReader`, force `placement` to `null` | **Predicted: WHOLE SUITE GREEN.** See below |

**`SplitByPlacement_Transposed_FollowsThePlacementNotTheName` is a positive control here too**, for
the same reason as Task 1's: `[Black@0, Cyan@1]` has index == channel, so mutation A cannot move it.
It documents the contract; report it as a positive control, not as pinned coverage.

**D and E are predicted to leave the suite green, and that prediction is the point.** The unit tests
cover `SplitByPlacement` directly; nothing yet asserts that the *builders* call it. That is the exact
shape of Pass 2b-engine's I-2, where `StencilInkFromFill`'s branch was entirely unpinned and forcing
it off left 2639 tests green.

**If D or E leaves the suite green, this task is not complete.** Add a builder-level test that
constructs an NChannel shading (and mesh) whose placement and name split disagree, and asserts the
resulting `ShadingSpotInk.Names` / `MeshSpotInk.Names` **positionally**. Run D and E again against
that test and record them going red by assertion. Do not proceed on the argument that the unit tests
"cover the logic".

- [ ] **Step 10: Commit**

```bash
git add PdfLibrary/Rendering/ShadingSpotSplit.cs PdfLibrary/Rendering/ShadingBuilder.cs \
        PdfLibrary/Rendering/MeshShadingReader.cs \
        PdfLibrary.Tests/Rendering/ShadingSpotSplitTests.cs
git commit -m "fix(colour): split a shading's colorants by placement, not by name

ShadingSpotSplit.Split switched on the literal names Cyan/Magenta/Yellow/Black
-- the third of the five sites of the same defect, after PdfImageToCmyk and
InkDecider.TryPerComponent. Under an NChannel space naming /PrCyan the switch
never matched, so the cyan ink went to a SPOT PLANE while the cyan unit sat
dry.

Both builders now prefer ColorantOrigin.Placement and fall back whole when
there is none. The mesh reader's hasProcess follows the same rule, so the two
cannot disagree about whether a space marks any plate.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Gate, suites, and the pin

**Files:** `Pellucid/Directory.Build.props.local`, `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj`
(pins only — no source changes).

**Interfaces:** consumes the merged engine from Tasks 1-2. Produces the verified pin later plans build on.

- [ ] **Step 1: Pack the engine**

Run `pack-local.ps1`. Record `NEWVERSION`.

**`pack-local.ps1` DELETES the Skia pin line on EVERY run — eight times on record.** Immediately
after packing, re-add by hand to `Pellucid/Directory.Build.props.local`:

```xml
<LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>
```

Then repin `PdfCompare.csproj` to `NEWVERSION`.

- [ ] **Step 2: Run the GWG render-hash gate**

Expected: `51 fixtures hashed, 51 baselined, 0 differences.`

**Check the embedded engine SHA, not the version number.** The gate prints
`engine=2.5.1+<sha>`; that SHA must equal the PDF HEAD under test. A matching version *number* does
not prove the right build ran — a stale package with the right name will pass silently.

**If any digest moves, STOP and report which fixture.** M2 predicted zero. A moved digest means either
M2's census was incomplete or the change reaches further than measured; both are findings, not
inconveniences.

- [ ] **Step 3: Run the NChannel render-hash gate**

Expected: `3 fixtures hashed, 3 baselined, 0 differences`, same SHA check.

- [ ] **Step 4: Run the Pellucid suites**

```bash
cd /c/Users/jorda/RiderProjects/Pellucid
dotnet test 2>&1 | tail -5
```

Expected: **1304 passing / 0 failing / 78 skipped.**

The Cups project (39 + 39 skipped on Windows) is **not** in the default `dotnet test` set and must be
run by full path if you want it — which is why a filtered run shows 0 skipped. Verify this rather
than assuming it.

If a run hangs: that is the known App.Tests headless-session death, not this branch. Dump the **child**
`Pellucid.App.Tests.exe` (not `testhost.exe`) with `dotnet-stack`; a thread parked in
`AvaloniaTestCase.Run` awaiting a `RunSummary` with no dispatch thread and no test-body frame confirms
it. Kill the three-process tree and re-run. The `.trx` is useless for this — the run never reports.

- [ ] **Step 5: Record results in the ledger**

Write the gate output verbatim, including both SHAs, plus both suite totals. **No commit** — the pin
files are local-only and `.superpowers/` is gitignored.

---

## Task 4: Documentation

**Files:**
- Modify: `Docs/colour/rendering-conformance.md` (the G-7 entry, ~`:282-288`)
- Modify: `Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md` (§6.2 delivery)

- [ ] **Step 1: Correct the G-7 entry**

G-7 currently says a shading *"falls through to the flattened path."* That is false and has been for
some time — `ShadingBuilder` builds a per-stop split (SP-7) and `MeshShadingReader` a per-vertex one
(SP-7-mesh), both consumed by the compositor.

Rewrite it to state what is actually true, and split it into the sub-gaps design §1.1 names:

- **closed here:** the shading/mesh split places colorants by `/Process` position rather than by
  reserved name.
- **still open:** site 4 (`InkDecider.ProcessContribution`'s name-derived plate mask, which is also
  the preserve signal), site 5 (`BuildCmykMapper`'s all-process arm), `/All` shadings (row 4-6), and
  per-stop spot reversion (row 5-10).
- State plainly that **`rawColor: null` leaving `Tints` empty is what keeps shadings out of the
  fills/strokes machinery** — that part of the old entry was correct and should survive.

Preserve the original text as superseded rather than deleting it, per this programme's convention.

- [ ] **Step 2: Correct §6.2 of the design if Task 0 contradicted it**

If Task 0's verdict changed the plan count or blocked Task 2, amend §6.2 with the original text
preserved and marked superseded. Pass 2b's design said two plans and it was three; the correction is
the record, not an embarrassment.

- [ ] **Step 3: Commit (docs only)**

```bash
git add Docs/colour/rendering-conformance.md Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md
git commit -m "docs(colour): correct G-7 and record what placement closed

G-7 said a shading falls through to the flattened path. It does not, and has
not since SP-7: ShadingBuilder builds a per-stop split and MeshShadingReader a
per-vertex one. The true remainder is narrower and is now split into the sites
design section 1.1 names.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage.** §2.2/§2.3 (table shape, three rules) → Task 1 Steps 3, 6, 9. §2.4 (carrier/compositor
boundary) → Task 1 Step 3, in the type's docs. §2.5 (no dereference) → Task 0 Step 3 (M6) + Task 1
Step 3. §3 (preserve signal) → **not in this plan**; it is site 4, Plan 2, as §6.2 states. §4.1
(site 3) → Task 2. §4.2, §4.3, §4.4 → later plans, gated on M1c, M5 and M4 respectively. §5.1 (gate as
guard) → Task 3 Steps 2-3. §5.2 (positional only) → Global Constraints + every assertion written.
§5.3 (assert the arm) → Task 0 Step 4; **no arm assertion exists in Tasks 1-2 because neither touches
the compositor** — that is why M1c can block Task 2 rather than Task 2 handling it. §5.4 → Task 1
Step 9, Task 2 Step 9. §6 (M1-M6) → Task 0. §8 (`/All` out of scope) → Task 1's `/All` refusal + Task 4.

**Placeholder scan.** No TBD/TODO. Every code step carries complete code. Task 2 Step 7's "find every
call" is paired with the exact `grep` rather than left as an instruction to be thorough.

**Type consistency.** `ColorantPlacement.Build(IReadOnlyList<ColourantComponent>?, int?)` is used with
that signature in Tasks 1 and 2. `ColorantSlot.Plate/Spot/Nothing` and `ColorantSlotKind.Plate/Spot/
Nothing` are used consistently. `SplitByPlacement(double[], ColorantPlacement, byte[], int)` matches
between Task 2 Steps 2, 4, 6 and 7. `ColourantComponent`'s five-parameter positional form matches the
record at `PdfLibrary/Rendering/ColourantComponent.cs`.

**Known weaknesses, stated rather than hidden.**

1. **Two mutations were originally prescribed against fixtures that cannot observe them**, caught in
   this self-review and corrected above. Both transposition fixtures have component index == channel
   index, so `Plate(i)` and `Plate(channel)` — and `plates[j]` and `plates[slot.Index]` — produce
   byte-identical output there. Retargeted at `SpotsGetSequentialSlots` /
   `SplitByPlacement_SpotsWriteAtTheirSlotPlusOffset`, where index and channel diverge. **This is the
   fifth instance in this programme of a mutation written against a fixture that cannot see it**
   (Pass 2b defects #19 and #22, then twice here). The pattern is now well enough established to
   treat as a rule: *when the mutation swaps one index for another, name a fixture where those two
   indices differ, or the mutation is decorative.*
2. Both transposition tests are **positive controls**, not pins — the rule they assert is enforced
   upstream in `ProcessChannelFor`. Labelled as such at both sites so the tally cannot overstate
   itself.
3. Task 2 mutations D and E are *predicted to leave the suite green*, with the follow-up spelled out
   — Pass 2b-engine's I-2 shape, anticipated instead of discovered in review.
4. `ColorantPlacement`, `ColorantSlot` and `ColorantSlotKind` are **public** by necessity, not by
   preference: `ColorantOrigin` is public and crosses the package boundary, and a public property
   cannot expose an internal type (CS0053). Plan 2 needs them in Pellucid regardless.
