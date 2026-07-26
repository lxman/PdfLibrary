# Colour Pass 2a — Per-Component Colourant Carrier

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the engine carry, per colourant component of an NChannel space, everything the compositor needs to evaluate that component individually — its role, its tint, and its own alternate colour — while changing no rendered output.

**Architecture:** `ColorantOrigin` gains two init-only members alongside its existing three positional ones, so the public record stays source-compatible. `OriginForColorSpaceObject` populates them from the `SpotColorSpace` that Pass 1 already parses, but **only for NChannel spaces** — every other space gets `null` and the common path is untouched. Nothing consumes the new data; Pass 2b does.

**Tech Stack:** C# / .NET, xUnit, `PdfLibrary` (engine). Verification crosses into Pellucid once, in Task 4.

---

## Scope

**In:** the `ColourantRole` enum, the `ColourantComponent` record, the two additive `ColorantOrigin` members, and their population for NChannel spaces — roles from reserved names / `/None` / `/Process /Components`, tints from the operator's raw colour, and `OwnAlternateCmyk` from `/Attributes /Colorants`.

**Out, deliberately:**

- **Any consumer.** `InkDecider` and `CmykPageRenderer` are Pass 2b. This plan touches no Pellucid code.
- **Non-CMYK `/Process` colour spaces.** ISO 32000-2 EXAMPLE 7 shows an NChannel with an ICCBased-RGB process space and components named `/ProcessRed`, `/ProcessGreen`, `/ProcessBlue`. Mapping those onto a CMYK device is a colour conversion, not a per-component alternate, and `OriginForColorSpaceObject` has no converter. Half-handling it would be worse than not handling it: recognising `/ProcessRed` as a process component without being able to say which plate it marks maps it onto *no* plate. So when `/Process /ColorSpace` is present and is neither `DeviceCMYK` nor `DeviceGray`, this plan emits **no** `Components` at all, which Pass 2b will read as "fall back to whole-space flatten" — today's behaviour. Recorded as a new gap.
- **Shadings and meshes.** They resolve their origin with `rawColor: null`, so there is no per-op tint and no per-component alternate to compute. They keep today's flattened behaviour; that is gap G-7's territory.
- **Caching the built tint transforms.** See the cost note below.

**Consequence for the matrix, to be recorded honestly in Pass 2b:** row 5-3 closes for the spot half and the CMYK-process half. The non-CMYK-process case is gapped, so the row's note must say the closure is scoped rather than presenting a clean ✅.

---

## Global Constraints

- Repo: `C:\Users\jorda\RiderProjects\PDF` for Tasks 1–3. Task 4 also touches `C:\Users\jorda\RiderProjects\Pellucid` and `C:\Users\jorda\PDFs\PdfCompare`.
- **This pass changes no rendered output.** Nothing consumes the new members, so the Pellucid corpus render-hash baseline must not move by a single digest.
- `ColorantOrigin` is a **public** record consumed across the package boundary by `Pellucid.Rendering.Cmyk` (production: `InkDecider.cs`, `CmykPageRenderer.cs`) and constructed directly in seven Pellucid test files. Its three-element positional constructor must keep compiling unchanged. New members are `init`-only with `null` defaults.
- `Docs/RendererSpi.md` §4 documents `PdfGraphicsState.ResolvedFillColor`/`ResolvedFillColorSpace` and `PdfColorToRgb.ToRgb`. Do not change those shapes.
- Engine test baseline is **2540 passing, 0 failing**. Every task ends green.
- Never commit with a failing suite.
- Nothing added here may throw out of the render path. `OriginForColorSpaceObject` is called from `PdfRenderer.cs:1045-1048` on every colour-setting operator.

---

## The degenerate-input table

Pass 1's post-mortem: every review defect was in the plan text, and all of them were malformed- or degenerate-input cases that the corpus cannot exercise. So they are enumerated here, before the code, and each row is a required test.

| Input | Required behaviour | Why |
|---|---|---|
| Space is not NChannel (`Subtype` absent, `"DeviceN"`, or an unrecognised value) | `Components` is `null`, `Subtype` still populated | Per-component routing is NChannel-only; ISO 32000-2 Table 70 defaults `Subtype` to `DeviceN`, and an unknown value is not `NChannel` |
| NChannel, `/Colorants` absent entirely | `Components` populated; every spot component's `OwnAlternateCmyk` is `null` | The spec requires `/Colorants` for NChannel spaces with spots, but files lie. Pass 2b reads a null alternate as "cannot revert this component" |
| `/Colorants/<name>` present but not a `[/Separation …]` array | that component's `OwnAlternateCmyk` is `null` | Same |
| `/Colorants/<name>` is a Separation whose alternate is not CMYK or Gray | `OwnAlternateCmyk` is `null` | `BuildTintToCmyk` returns null; there is no converter here |
| `/Colorants/<name>`'s tint transform **throws** on evaluate | `OwnAlternateCmyk` is `null`, no exception escapes | `BuildTintToCmyk` has **no** internal catch — only `BuildTintRamp:546` does. The guard must be added here |
| Component named `/None` | `Role` is `None`, `OwnAlternateCmyk` is `null`, no `/Colorants` lookup performed | Row 5-7: `/None` components are discarded when painting directly. Looking one up would be meaningless work and could throw |
| Component named `Cyan`/`Magenta`/`Yellow`/`Black` | `Role` is `Process`, no `/Colorants` lookup | Table 71: the reserved names "shall always be considered to be process colours … they need not have entries in the process dictionary" |
| Component listed in `/Process /Components` **and** in `/Colorants` | `Role` is `Process`; the `/Colorants` entry is ignored | Table 71: "Any such definition shall be ignored if the colorant is also present in the process dictionary" |
| `/Process /ColorSpace` is neither DeviceCMYK nor DeviceGray | `Components` is `null` (whole space falls back) | Scope decision above |
| `/Process` present but `/Components` missing or not an array | treat as no process dictionary — reserved names still map to `Process` | Malformed; degrade rather than reject |
| `Tints` shorter than `Names` (including empty, as shadings produce) | the missing components get `Tint = null` and `OwnAlternateCmyk = null` | A component with no tint has no alternate colour to compute |
| `Tints` longer than `Names` | extra values ignored | Matches how `ProcessContribution` already tolerates the mismatch |
| A DeviceN component named `All` | `Role` is `Spot`, treated like any other name | §8.6.6.5 forbids `/All` in DeviceN, but `InkDecider.cs:95-100` documents a deliberate leniency for it that Pass 2b must not regress |
| Duplicate names in the names array | one component per position, duplicates preserved | The list is positional; the registry dedupes separately |
| **`/Colorants/<name>` is an indirect reference** | resolved, alternate computed normally | **This is the real-world shape, not an edge case.** GWG081 — the corpus's only NChannel file — has `/Colorants 51 0 R` whose value is `<< /GWG#20Green 14 0 R >>`. Both the dictionary and the entry are indirect, and tint transforms are typically indirect stream objects too |
| **`/Process /Components` is an indirect reference** | resolved | Same shape |

**Threading the document is therefore load-bearing, not hygiene.** `OriginForColorSpaceObject` receives a `PdfDocument? doc`; `BuildComponents` and everything below it must take and use it. Passing `null` would leave every indirect reference unresolved, which fails *silently*: fixture-built tests use direct arrays and pass, the corpus gate cannot see it because nothing consumes the data yet, and the defect would surface only in Pass 2b as "NChannel routing does nothing on real files". Task 2 changes `BuildComponents`'s signature to take `doc`; Task 3 threads it onward.

**Cost note.** Building a component's `OwnAlternateCmyk` calls `PdfFunction.Create` and evaluates it, per spot component, per colour-setting operator. This is gated on `IsNChannel`, and NChannel is rare — one file in the 51-patch corpus — so the common path pays nothing. If it ever matters, the mitigation is caching the built `Func` per `/Colorants` entry object number, which is the same caching-and-thread-safety question Pass 1 deferred. Do not add a cache here.

---

## File Structure

| File | Responsibility |
|---|---|
| `PdfLibrary/Rendering/ColourantComponent.cs` | **Create.** The `ColourantRole` enum and `ColourantComponent` record. Public, no logic. |
| `PdfLibrary/Rendering/ColorantOrigin.cs` | **Modify.** Two init-only members added; positional constructor untouched. |
| `PdfLibrary/Rendering/ColorSpaceResolver.cs` | **Modify.** `OriginForColorSpaceObject` populates the new members via a new private `BuildComponents`. |
| `PdfLibrary.Tests/Rendering/ColourantComponentTests.cs` | **Create.** Every row of the degenerate-input table. |

---

## Task 1: The carrier types and role classification

**Files:**
- Create: `PdfLibrary/Rendering/ColourantComponent.cs`
- Modify: `PdfLibrary/Rendering/ColorantOrigin.cs`
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `OriginForColorSpaceObject` (`:835`)
- Test: `PdfLibrary.Tests/Rendering/ColourantComponentTests.cs` (create)

**Interfaces:**
- Consumes: `SpotColorSpace` (Pass 1) — `Family`, `Names`, `Subtype`, `IsNChannel`, `Colorants`, `Process`, `AlternateSpaceName`, `AllNamesResolved`.
- Produces, used by Tasks 2 and 3 and by Pass 2b:
  - `public enum ColourantRole { Spot, Process, None }`
  - `public sealed record ColourantComponent(string Name, ColourantRole Role, double? Tint, IReadOnlyList<double>? OwnAlternateCmyk)`
  - `ColorantOrigin.Subtype` (`string?`) and `ColorantOrigin.Components` (`IReadOnlyList<ColourantComponent>?`), both `init`-only, defaulting to `null`.

**Background:** this task populates `Name`, `Role` and `Tint`. `OwnAlternateCmyk` is left `null` for every component — Task 3 fills it. Role classification in this task handles only `/None` and the four reserved process names; `/Process /Components` is Task 2.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Rendering/ColourantComponentTests.cs`:

```csharp
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The per-component carrier that Pass 2b's NChannel routing consumes. Nothing reads it yet, so these
/// tests are its only consumer — which is deliberate: the degenerate inputs enumerated in the Pass 2a
/// plan cannot appear in the Ghent corpus (they are malformed or exotic), so a test is the only thing
/// that can exercise them before the compositor depends on them.
/// </summary>
public class ColourantComponentTests
{
    private const string Tint1 = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>";
    private const string Tint2 = "<< /FunctionType 2 /Domain [0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    private static ColorantOrigin? Origin(string literal, params double[] tints) =>
        ColorSpaceResolver.OriginForColorSpaceObject(Parse(literal), tints, null);

    /// <summary>An NChannel [/Magenta /Spot1] with the given extra attribute entries.</summary>
    private static string NChannel(string extraAttributes) =>
        "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
        + extraAttributes + " >>]";

    // --- Components are populated ONLY for NChannel ---

    [Fact]
    public void PlainDeviceN_HasNoComponents_ButStillReportsItsSubtype()
    {
        ColorantOrigin? o = Origin("[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + "]", 0.25, 0.5);

        Assert.NotNull(o);
        Assert.Equal("DeviceN", o!.Subtype);      // Table 70 default
        Assert.Null(o.Components);
    }

    [Fact]
    public void Separation_HasNoComponents()
    {
        ColorantOrigin? o = Origin("[/Separation /Spot1 /DeviceCMYK " + Tint1 + "]", 0.75);

        Assert.NotNull(o);
        Assert.Equal("DeviceN", o!.Subtype);
        Assert.Null(o.Components);
    }

    [Fact]
    public void UnrecognisedSubtype_IsNotTreatedAsNChannel()
    {
        // Table 70 says the value SHALL be DeviceN or NChannel. Anything else is not NChannel, so
        // per-component routing must not engage.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /Sideways >>]", 0.25, 0.5);

        Assert.NotNull(o);
        Assert.Equal("Sideways", o!.Subtype);
        Assert.Null(o.Components);
    }

    [Fact]
    public void NChannel_PopulatesOneComponentPerName_InOrder()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5);

        Assert.NotNull(o);
        Assert.Equal("NChannel", o!.Subtype);
        Assert.NotNull(o.Components);
        Assert.Equal(2, o.Components!.Count);
        Assert.Equal("Magenta", o.Components[0].Name);
        Assert.Equal("Spot1", o.Components[1].Name);
    }

    // --- Roles ---

    [Fact]
    public void ReservedProcessNames_AreProcess_WithoutAProcessDictionary()
    {
        // Table 71: the reserved names "shall always be considered to be process colours ... they need
        // not have entries in the process dictionary".
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Cyan /Magenta /Yellow /Black] /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1 0 1 0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>"
            + " << /Subtype /NChannel >>]", 0.1, 0.2, 0.3, 0.4);

        Assert.NotNull(o);
        Assert.All(o!.Components!, c => Assert.Equal(ColourantRole.Process, c.Role));
    }

    [Fact]
    public void NoneComponent_IsRoleNone()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Spot1 /None] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]", 0.5, 0.5);

        Assert.NotNull(o);
        Assert.Equal(ColourantRole.Spot, o!.Components![0].Role);
        Assert.Equal(ColourantRole.None, o.Components[1].Role);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }

    [Fact]
    public void OrdinaryName_IsSpot()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);   // Magenta
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);        // Spot1
    }

    [Fact]
    public void AllInADeviceN_IsTreatedAsAnOrdinarySpotName()
    {
        // 8.6.6.5 forbids /All in a DeviceN names array, but InkDecider documents a deliberate
        // leniency for it (InkDecider.cs:95-100) that Pass 2b must not regress. Classifying it as a
        // spot here keeps that arm reachable.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/All /Spot1] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]", 0.5, 0.5);

        Assert.Equal(ColourantRole.Spot, o!.Components![0].Role);
    }

    // --- Tints ---

    [Fact]
    public void Tints_ArePairedPositionally()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5);

        Assert.Equal(0.25, o!.Components![0].Tint);
        Assert.Equal(0.5, o.Components[1].Tint);
    }

    [Fact]
    public void FewerTintsThanNames_LeavesTheRemainderNull()
    {
        // A shading resolves its origin with rawColor null, so Tints is empty. Such a component has no
        // per-op tint and therefore no alternate colour to compute.
        ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(Parse(NChannel("")), null, null);

        Assert.NotNull(o);
        Assert.Equal(2, o!.Components!.Count);
        Assert.Null(o.Components[0].Tint);
        Assert.Null(o.Components[1].Tint);
        Assert.Null(o.Components[0].OwnAlternateCmyk);
    }

    [Fact]
    public void MoreTintsThanNames_IgnoresTheExtras()
    {
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 0.5, 0.9);

        Assert.Equal(2, o!.Components!.Count);
        Assert.Equal(0.5, o.Components[1].Tint);
    }

    // --- The positional constructor is untouched ---

    [Fact]
    public void PositionalConstructor_StillCompiles_AndDefaultsTheNewMembersToNull()
    {
        // Seven Pellucid test files construct ColorantOrigin this way; that must keep working.
        var o = new ColorantOrigin(["Spot1"], [1.0], "DeviceCMYK");

        Assert.Null(o.Subtype);
        Assert.Null(o.Components);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: **compile error** — `ColourantRole` and `ColourantComponent` do not exist, and `ColorantOrigin` has no `Subtype` or `Components`.

- [ ] **Step 3: Create the carrier types**

Create `PdfLibrary/Rendering/ColourantComponent.cs`:

```csharp
namespace PdfLibrary.Rendering;

/// <summary>How one component of a DeviceN/NChannel colour space relates to the output device.</summary>
public enum ColourantRole
{
    /// <summary>A named spot colourant. May or may not be available on the device — that is the
    /// compositor's question, not the engine's.</summary>
    Spot,

    /// <summary>A process component: one of the four reserved names, or a name listed in
    /// <c>/Attributes /Process /Components</c>.</summary>
    Process,

    /// <summary>The reserved name <c>/None</c>. ISO 32000-2 §8.6.6.5: such components "shall never be
    /// painted on the page" when painting named colourants directly.</summary>
    None,
}

/// <summary>
/// One component of an NChannel colour space, carrying what ISO 32000-2 §8.6.6.5 needs in order to
/// evaluate that component individually: "only the ones not present on the output device shall use the
/// alternate colour space <b>of that component</b>."
///
/// <para>Populated engine-side and carried on <see cref="ColorantOrigin"/>, matching the precedent set
/// by <c>SpotImageInk</c>, <c>ShadingSpotInk</c> and <c>MeshSpotInk</c>, all of which carry names plus
/// pre-resolved process CMYK so that PDF function evaluation stays out of the compositor.</para>
/// </summary>
/// <param name="Name">The colourant name, verbatim from the names array.</param>
/// <param name="Role">Spot, Process, or None.</param>
/// <param name="Tint">This component's tint from the painting operator, or null when the operator
/// supplied no value for it — a shading resolves its origin with no per-op colour at all.</param>
/// <param name="OwnAlternateCmyk">The component's own alternate colour, evaluated at
/// <paramref name="Tint"/>, as four CMYK values — derived from <c>/Attributes /Colorants</c>, which for
/// an NChannel space defines each spot colourant as a full Separation space describing "the appearance
/// of that colorant alone". Null when there is no usable alternate: no <c>/Colorants</c> entry, an
/// entry that is not a Separation, an alternate this engine cannot reduce to CMYK, a tint transform
/// that failed, or no tint to evaluate at. A null here means the component cannot be reverted
/// individually, and the consumer must fall back rather than invent a colour.</param>
public sealed record ColourantComponent(
    string Name,
    ColourantRole Role,
    double? Tint,
    IReadOnlyList<double>? OwnAlternateCmyk);
```

- [ ] **Step 4: Extend `ColorantOrigin` additively**

Replace `PdfLibrary/Rendering/ColorantOrigin.cs` in full:

```csharp
namespace PdfLibrary.Rendering;

/// <summary>
/// The named-colorant identity of a resolved Separation/DeviceN paint, preserved alongside the
/// flattened device colour on <see cref="PdfLibrary.Content.PdfGraphicsState"/> and
/// <see cref="ShadingDescriptor"/>. Null for device (DeviceGray/RGB/CMYK) and Pattern colours.
/// Soft-Proof SP-1: the data SP-2's N-channel compositor uses to route paint to spot plates.
///
/// <para><b>The two members below are additive by design.</b> This record is public and crosses the
/// package boundary: <c>Pellucid.Rendering.Cmyk</c> reads it in production and seven Pellucid test
/// files construct it positionally. Keeping the three-element constructor intact means Pass 2a needs no
/// lockstep engine-pack-and-repin. They are folded into the positional shape by a later contract pass,
/// once <see cref="Components"/> has fully replaced <see cref="Names"/> and <see cref="Tints"/>.</para>
/// </summary>
public sealed record ColorantOrigin(
    IReadOnlyList<string> Names,   // Separation → 1 name; DeviceN → N names, in colorant order
    IReadOnlyList<double> Tints,   // the raw tint inputs supplied to the colour operator
    string AlternateSpace)         // the tint transform's alternate space name (e.g. "DeviceCMYK", "Lab")
{
    /// <summary><c>/Attributes /Subtype</c> — "DeviceN" or "NChannel", defaulting to "DeviceN" per
    /// ISO 32000-2 Table 70. Null only when this origin was built by a caller that predates Pass 2a.
    /// A value other than "NChannel" means per-component evaluation does not apply.</summary>
    public string? Subtype { get; init; }

    /// <summary>One entry per colourant name, in order, when this is an NChannel space whose components
    /// can be evaluated individually. <b>Null</b> for every other space — a Separation, a plain DeviceN,
    /// an unrecognised subtype, or an NChannel whose process colour space this engine cannot reduce to
    /// CMYK. A null here is the signal to fall back to whole-space behaviour, which is exactly what
    /// ISO 32000-2 §8.6.6.5 requires of a non-NChannel DeviceN.</summary>
    public IReadOnlyList<ColourantComponent>? Components { get; init; }
}
```

- [ ] **Step 5: Populate `Subtype` and the component list**

In `ColorSpaceResolver.cs`, replace the final `return` of `OriginForColorSpaceObject` (currently
`return new ColorantOrigin(names, tints, space.AlternateSpaceName);`) with:

```csharp
        return new ColorantOrigin(names, tints, space.AlternateSpaceName)
        {
            Subtype = space.Subtype,
            Components = BuildComponents(space, tints),
        };
```

Then add this private helper immediately after `OriginForColorSpaceObject`:

```csharp
    /// <summary>
    /// Builds the per-component carrier for an NChannel space (ISO 32000-2 §8.6.6.5: "the components
    /// shall be evaluated individually"). Returns null for every space that is not NChannel — a
    /// Separation, a plain DeviceN, or an unrecognised subtype — because for those the whole space
    /// reverts together and there is nothing per-component to say.
    ///
    /// <para><paramref name="tints"/> may be SHORTER than the name list, including empty: a shading
    /// resolves its origin with no per-op colour. Those components get a null tint.</para>
    /// </summary>
    private static IReadOnlyList<ColourantComponent>? BuildComponents(
        SpotColorSpace space, IReadOnlyList<double> tints)
    {
        if (!space.IsNChannel) return null;

        var components = new List<ColourantComponent>(space.Names.Count);
        for (var i = 0; i < space.Names.Count; i++)
        {
            string name = space.Names[i]!;   // callers gate on AllNamesResolved before reaching here
            double? tint = i < tints.Count ? tints[i] : null;
            components.Add(new ColourantComponent(name, RoleFor(name), tint, OwnAlternateCmyk: null));
        }
        return components;
    }

    /// <summary>
    /// Classifies one colourant name. ISO 32000-2 Table 71: the reserved names Cyan, Magenta, Yellow
    /// and Black "shall always be considered to be process colours … they need not have entries in the
    /// process dictionary". <c>/None</c> is its own role because §8.6.6.5 requires those components to
    /// be discarded when painting named colourants directly — classifying one as a spot would send it
    /// down the revert path and paint it, inverting that rule.
    /// </summary>
    private static ColourantRole RoleFor(string name) => name switch
    {
        "Cyan" or "Magenta" or "Yellow" or "Black" => ColourantRole.Process,
        "None" => ColourantRole.None,
        _ => ColourantRole.Spot,
    };
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: PASS, all tests.

- [ ] **Step 7: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: 2540 plus the new tests, 0 failing.

- [ ] **Step 8: Commit**

```bash
git add PdfLibrary/Rendering/ColourantComponent.cs PdfLibrary/Rendering/ColorantOrigin.cs PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary.Tests/Rendering/ColourantComponentTests.cs
git commit -m "feat(colour): per-component colourant carrier for NChannel spaces

ColorantOrigin gains Subtype and Components as init-only members, so its
three-element positional constructor keeps compiling for the seven Pellucid
test files and two production consumers that use it.

Components is populated only for NChannel spaces; everything else gets null,
which is the signal to fall back to whole-space behaviour — exactly what
8.6.6.5 requires of a non-NChannel DeviceN. Roles cover /None and the four
reserved process names here; /Process /Components mapping is the next task.
OwnAlternateCmyk is null throughout until Task 3.

Nothing consumes any of this yet."
```

---

## Task 2: `/Process /Components` role mapping

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `BuildComponents`, `RoleFor`
- Test: `PdfLibrary.Tests/Rendering/ColourantComponentTests.cs` (extend)

**Interfaces:**
- Consumes: `SpotColorSpace.Process` (a `PdfDictionary?`, lazily resolved — Pass 1).
- Produces: no new public surface. `RoleFor` gains the process-dictionary parameter.

**Background:** ISO 32000-2 Table 71 lets an NChannel space name its process components anything, mapping them through `/Process /Components` — EXAMPLE 7 uses `/ProcessRed`, `/ProcessGreen`, `/ProcessBlue`. Today those names match none of the four reserved strings and would be classified `Spot`, then dropped by the compositor because they have no plane.

**The scope boundary, which is the important part of this task:** `/Process /ColorSpace` may be any device or CIE-based space. Reducing a non-CMYK one to plates is a colour conversion this method has no converter for. So when `/Process /ColorSpace` is present and is neither `DeviceCMYK` nor `DeviceGray`, `BuildComponents` returns **null** for the whole space — meaning "fall back to whole-space flatten", which is today's behaviour and is safe. Do not partially classify such components; a process component mapped to no plate is worse than one that falls back.

- [ ] **Step 1: Write the failing tests**

Append to `ColourantComponentTests.cs`:

```csharp
    // --- /Process /Components mapping ---

    private const string CmykProcess =
        "/Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow /Black] >>";

    [Fact]
    public void ProcessComponents_MapNonReservedNamesToProcessRole()
    {
        // The GWG081 shape: a CMYK process dictionary alongside a spot.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Spot1 /Cyan] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel " + CmykProcess + " >>]", 0.25, 0.5);

        Assert.Equal(ColourantRole.Spot, o!.Components![0].Role);
        Assert.Equal(ColourantRole.Process, o.Components[1].Role);
    }

    [Fact]
    public void NameListedInProcessComponents_IsProcess_EvenIfNotReserved()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/Ink1 /Magenta /Yellow /Black] >> >>]", 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }

    [Fact]
    public void ProcessEntryWins_OverAColorantsEntryForTheSameName()
    {
        // Table 71: "Any such definition shall be ignored if the colorant is also present in the
        // process dictionary."
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/Ink1 /Magenta /Yellow /Black] >> "
            + "/Colorants << /Ink1 [/Separation /Ink1 /DeviceCMYK " + Tint1 + "] >> >>]", 0.25, 0.5);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);
    }

    [Fact]
    public void NoneStaysNone_EvenIfListedInProcessComponents()
    {
        // /None's discard rule is unconditional; a malformed process dictionary must not override it.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/None /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK "
            + "/Components [/None /Magenta /Yellow /Black] >> >>]", 0.5, 0.5);

        Assert.Equal(ColourantRole.None, o!.Components![0].Role);
    }

    [Fact]
    public void NonCmykProcessSpace_SuppressesComponentsEntirely()
    {
        // EXAMPLE 7's shape. Mapping RGB process components onto plates is a colour conversion this
        // engine has no converter for here, and classifying them as Process without being able to say
        // which plate they mark would map them onto none. Falling back is the safe answer.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/ProcessRed /ProcessGreen /ProcessBlue /Red] /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1 0 1 0 1 0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>"
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceRGB "
            + "/Components [/ProcessRed /ProcessGreen /ProcessBlue] >> >>]", 0.1, 0.2, 0.3, 0.4);

        Assert.NotNull(o);
        Assert.Equal("NChannel", o!.Subtype);
        Assert.Null(o.Components);
    }

    [Fact]
    public void DeviceGrayProcessSpace_IsAccepted()
    {
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Ink1 /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceGray /Components [/Ink1] >> >>]",
            0.25, 0.5);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
    }

    [Fact]
    public void MalformedProcessDictionary_DegradesToReservedNamesOnly()
    {
        // /Components missing. Reserved names must still classify; the rest stay spots.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Process << /ColorSpace /DeviceCMYK >> >>]", 0.25, 0.5);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Process, o.Components![0].Role);
        Assert.Equal(ColourantRole.Spot, o.Components[1].Role);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: the new tests FAIL — non-reserved process names currently classify as `Spot`, and a non-CMYK process space currently still yields components.

- [ ] **Step 3: Implement the process mapping**

`BuildComponents` gains a `PdfDocument? doc` parameter in this step, so **update its call site too**. In `OriginForColorSpaceObject`, change `Components = BuildComponents(space, tints),` to:

```csharp
            Components = BuildComponents(space, tints, doc),
```

`doc` is already a parameter of `OriginForColorSpaceObject`. Then replace `BuildComponents` and `RoleFor` with:

```csharp
    private static IReadOnlyList<ColourantComponent>? BuildComponents(
        SpotColorSpace space, IReadOnlyList<double> tints, PdfDocument? doc)
    {
        if (!space.IsNChannel) return null;

        // ISO 32000-2 Table 72: /Process names the process colour space and its component names. That
        // space may be any device or CIE-based space, but reducing a non-CMYK one to plates is a colour
        // conversion this method has no converter for. Rather than classify such components as Process
        // and leave the consumer unable to say which plate they mark — which maps them onto NONE, worse
        // than the status quo — suppress the whole component list and let the space fall back to its
        // document tint transform. Recorded as a gap; see the Pass 2a plan.
        HashSet<string>? processNames = null;
        if (space.Process is { } process)
        {
            string processSpace = ProcessSpaceName(process, doc);
            if (processSpace is not ("DeviceCMYK" or "DeviceGray" or "")) return null;

            if (process.TryGetValue(new PdfName("Components"), out PdfObject? compsObj)
                && Deref(compsObj, doc) is PdfArray comps)
            {
                processNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (PdfObject c in comps)
                    if (c is PdfName cn) processNames.Add(cn.Value);
            }
        }

        var components = new List<ColourantComponent>(space.Names.Count);
        for (var i = 0; i < space.Names.Count; i++)
        {
            string name = space.Names[i]!;
            double? tint = i < tints.Count ? tints[i] : null;
            components.Add(new ColourantComponent(
                name, RoleFor(name, processNames), tint, OwnAlternateCmyk: null));
        }
        return components;
    }

    /// <summary>The family name of an NChannel process dictionary's /ColorSpace, or the empty string
    /// when absent or unreadable — absent is treated as "no constraint" rather than as a rejection,
    /// because a malformed process dictionary should degrade to reserved-name classification rather
    /// than suppress an otherwise usable space.</summary>
    private static string ProcessSpaceName(PdfDictionary process, PdfDocument? doc)
    {
        if (!process.TryGetValue(new PdfName("ColorSpace"), out PdfObject? csObj)) return string.Empty;
        return Deref(csObj, doc) switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Classifies one colourant name. ISO 32000-2 Table 71: the reserved names Cyan, Magenta, Yellow
    /// and Black "shall always be considered to be process colours … they need not have entries in the
    /// process dictionary", and any name listed in the process dictionary is a process component whose
    /// /Colorants definition, if any, "shall be ignored".
    ///
    /// <para><c>/None</c> is tested FIRST and unconditionally: §8.6.6.5 requires those components to be
    /// discarded when painting named colourants directly, and a malformed process dictionary listing
    /// /None must not override that. Classifying /None as anything else would send it down a paint
    /// path.</para>
    /// </summary>
    private static ColourantRole RoleFor(string name, HashSet<string>? processNames) => name switch
    {
        "None" => ColourantRole.None,
        "Cyan" or "Magenta" or "Yellow" or "Black" => ColourantRole.Process,
        _ when processNames is not null && processNames.Contains(name) => ColourantRole.Process,
        _ => ColourantRole.Spot,
    };
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: PASS.

- [ ] **Step 5: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: no failures.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary.Tests/Rendering/ColourantComponentTests.cs
git commit -m "feat(colour): map /Process /Components names to the Process role

An NChannel space may name its process components anything and map them
through /Process /Components (8.6.6.5 EXAMPLE 7). Those names matched none of
the four reserved strings and would have classified as spots, then been
dropped by the compositor for having no plane.

A non-CMYK process colour space suppresses the component list entirely rather
than being half-handled: reducing RGB process components to plates is a colour
conversion this method has no converter for, and classifying them as Process
without a plate mapping is worse than falling back. Recorded as a gap.

/None is classified first and unconditionally, so a malformed process
dictionary listing it cannot route it onto a paint path."
```

---

## Task 3: `OwnAlternateCmyk` from `/Attributes /Colorants`

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `BuildComponents`
- Test: `PdfLibrary.Tests/Rendering/ColourantComponentTests.cs` (extend)

**Interfaces:**
- Consumes: `SpotColorSpace.Colorants` (a `PdfDictionary?`, lazily resolved — Pass 1); `ColorSpaceResolver.BuildTintToCmyk`.
- Produces: `ColourantComponent.OwnAlternateCmyk` populated for spot components that have a usable `/Colorants` entry.

**Background:** for an NChannel space, `/Attributes /Colorants /<name>` is required to be a full Separation space for that colourant, and Table 71 says its alternate and tint transform "describe the appearance of **that colorant alone**" — which is exactly the per-component alternate §8.6.6.5 calls for. GWG081's is `[/Separation /GWG#20Green /DeviceCMYK << Type 2, C1 [0.5 0 1 0] >>]`.

**Three hazards, all required behaviour:**

1. **`BuildTintToCmyk` has no internal catch.** Only `BuildTintRamp` does, at `:546`, whose comment says a malformed function "must never throw into the render path" — and `OriginForColorSpaceObject` runs on every colour-setting operator. Wrap the *evaluation* in a try/catch and degrade to null.
2. **Compute only for `Spot` components that have a tint.** A `Process` or `None` component needs no alternate, and a component with a null tint has nothing to evaluate at.
3. **Null is a meaningful answer, not a failure.** It means "this component cannot be reverted individually", and Pass 2b will fall back rather than invent a colour.

- [ ] **Step 1: Write the failing tests**

Append to `ColourantComponentTests.cs`:

```csharp
    // --- OwnAlternateCmyk from /Colorants ---

    private const string SpotColorants =
        "/Colorants << /Spot1 [/Separation /Spot1 /DeviceCMYK "
        + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>] >>";

    [Fact]
    public void SpotComponent_GetsItsOwnAlternateEvaluatedAtItsTint()
    {
        // C1 is [0.5 0 1 0] with N 1, so at tint 1 the component's own alternate is exactly that —
        // independently derivable from the function, not copied from a debugger.
        ColorantOrigin? o = Origin(NChannel(SpotColorants), 0.25, 1.0);

        IReadOnlyList<double>? alt = o!.Components![1].OwnAlternateCmyk;
        Assert.NotNull(alt);
        Assert.Equal(4, alt!.Count);
        Assert.Equal(0.5, alt[0], 3);
        Assert.Equal(0.0, alt[1], 3);
        Assert.Equal(1.0, alt[2], 3);
        Assert.Equal(0.0, alt[3], 3);
    }

    [Fact]
    public void SpotComponent_AlternateTracksTheTint()
    {
        ColorantOrigin? o = Origin(NChannel(SpotColorants), 0.25, 0.5);

        IReadOnlyList<double>? alt = o!.Components![1].OwnAlternateCmyk;
        Assert.NotNull(alt);
        Assert.Equal(0.25, alt![0], 3);   // 0.5 * 0.5
        Assert.Equal(0.5, alt[2], 3);     // 1.0 * 0.5
    }

    [Fact]
    public void ProcessComponent_HasNoOwnAlternate()
    {
        ColorantOrigin? o = Origin(NChannel(SpotColorants), 0.25, 1.0);

        Assert.Equal(ColourantRole.Process, o!.Components![0].Role);   // Magenta
        Assert.Null(o.Components[0].OwnAlternateCmyk);
    }

    [Fact]
    public void NChannelWithoutColorants_LeavesSpotAlternatesNull()
    {
        // The spec requires /Colorants for an NChannel space with spot colourants, but files lie.
        // A null alternate means "cannot revert this component individually", which is the signal
        // Pass 2b falls back on.
        ColorantOrigin? o = Origin(NChannel(""), 0.25, 1.0);

        Assert.NotNull(o!.Components);
        Assert.Equal(ColourantRole.Spot, o.Components![1].Role);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }

    [Fact]
    public void ColorantsEntryThatIsNotASeparation_LeavesTheAlternateNull()
    {
        ColorantOrigin? o = Origin(
            NChannel("/Colorants << /Spot1 /DeviceRGB >>"), 0.25, 1.0);

        Assert.Null(o!.Components![1].OwnAlternateCmyk);
    }

    [Fact]
    public void ColorantsEntryWithANonCmykAlternate_LeavesTheAlternateNull()
    {
        // BuildTintToCmyk accepts only DeviceCMYK and DeviceGray alternates; anything else is not
        // reducible to plates here.
        ColorantOrigin? o = Origin(
            NChannel("/Colorants << /Spot1 [/Separation /Spot1 /DeviceRGB "
                     + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>] >>"),
            0.25, 1.0);

        Assert.Null(o!.Components![1].OwnAlternateCmyk);
    }

    [Fact]
    public void SpotComponentWithNoTint_HasNoAlternate()
    {
        // Shadings resolve with no per-op colour, so there is no point to evaluate the alternate at.
        ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(
            Parse(NChannel(SpotColorants)), null, null);

        Assert.NotNull(o!.Components);
        Assert.Null(o.Components![1].Tint);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }

    [Fact]
    public void IndirectColorantsEntry_IsResolved()
    {
        // THE REAL-WORLD SHAPE, not an edge case. GWG081 — the corpus's only NChannel file — has
        // /Colorants 51 0 R whose value is << /GWG#20Green 14 0 R >>. Both the dictionary and the
        // entry are indirect, and the Separation's tint transform is an indirect stream object too.
        // With a null document every Deref is a no-op, so this test is the only thing standing
        // between "works" and "silently produces no alternate on every real NChannel file".
        byte[] pdf = ColourConformancePage.Build(
            "[/DeviceN [/Magenta /Spot1] /DeviceCMYK " + Tint2
            + " << /Subtype /NChannel /Colorants << /Spot1 5 0 R >> >>]",
            "1 0 0 rg 0 0 1 1 re f",
            // Body only — Build writes the "5 0 obj … endobj" wrapper itself. And BY NAME: the
            // helper's own doc warns that a positional argument here silently binds to
            // extraResources instead, which compiles and produces a file missing the object.
            extraObjects:
            ["[/Separation /Spot1 /DeviceCMYK "
             + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>]"]);

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        var cs = (PdfArray)page.GetResources()!.GetColorSpaces()![new PdfName("Cs0")]!;

        ColorantOrigin? o = ColorSpaceResolver.OriginForColorSpaceObject(cs, [0.25, 1.0], doc);

        IReadOnlyList<double>? alt = o!.Components![1].OwnAlternateCmyk;
        Assert.NotNull(alt);
        Assert.Equal(0.5, alt![0], 3);
        Assert.Equal(1.0, alt[2], 3);
    }

    [Fact]
    public void NoneComponent_NeverLooksUpAColorantsEntry()
    {
        // Row 5-7: /None components are discarded when painting directly. Evaluating an alternate for
        // one would be meaningless work on a path that must never paint.
        ColorantOrigin? o = Origin(
            "[/DeviceN [/Spot1 /None] /DeviceCMYK " + Tint2 + " << /Subtype /NChannel "
            + "/Colorants << /None [/Separation /None /DeviceCMYK "
            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>] >> >>]",
            0.5, 1.0);

        Assert.Equal(ColourantRole.None, o!.Components![1].Role);
        Assert.Null(o.Components[1].OwnAlternateCmyk);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: the alternate-bearing tests FAIL — `OwnAlternateCmyk` is currently always null.

- [ ] **Step 3: Implement the alternate lookup**

In `BuildComponents`, replace the component-construction loop with:

```csharp
        var components = new List<ColourantComponent>(space.Names.Count);
        for (var i = 0; i < space.Names.Count; i++)
        {
            string name = space.Names[i]!;
            double? tint = i < tints.Count ? tints[i] : null;
            ColourantRole role = RoleFor(name, processNames);
            components.Add(new ColourantComponent(
                name, role, tint, OwnAlternateFor(space, name, role, tint, doc)));
        }
        return components;
```

And add this helper after `RoleFor`:

```csharp
    /// <summary>
    /// The component's own alternate colour as CMYK, from <c>/Attributes /Colorants /&lt;name&gt;</c> —
    /// which ISO 32000-2 Table 71 defines as a full Separation space describing "the appearance of that
    /// colorant alone", i.e. exactly the "alternate colour space of that component" §8.6.6.5 calls for.
    ///
    /// <para>Null whenever that cannot be produced: a non-spot role (process components take the process
    /// space, /None is never painted), no tint to evaluate at, no /Colorants dictionary, no entry for
    /// this name, an entry that is not a Separation, or an alternate this engine cannot reduce to CMYK.
    /// Null is a meaningful answer meaning "this component cannot be reverted individually" — the
    /// consumer falls back rather than inventing a colour.</para>
    ///
    /// <para>The evaluation is wrapped because <see cref="BuildTintToCmyk"/> has no internal catch — only
    /// <see cref="BuildTintRamp"/> does — and a Type 0 or Type 4 function can build successfully and
    /// still throw at evaluation time on a malformed body. This method runs from
    /// <see cref="OriginForColorSpaceObject"/>, which <c>PdfRenderer</c> calls on every colour-setting
    /// operator, so a throw here would take down the render of an otherwise fine page.</para>
    /// </summary>
    private static IReadOnlyList<double>? OwnAlternateFor(
        SpotColorSpace space, string name, ColourantRole role, double? tint, PdfDocument? doc)
    {
        if (role != ColourantRole.Spot || tint is not { } t) return null;
        if (space.Colorants is not { } colorants) return null;
        if (!colorants.TryGetValue(new PdfName(name), out PdfObject? entryObj)) return null;

        // doc is load-bearing: GWG081's /Colorants entry is `14 0 R`, and a Separation's tint
        // transform is normally an indirect stream object. Passing null here would leave both
        // unresolved and silently yield no alternate on every real NChannel file.
        if (Deref(entryObj, doc) is not PdfArray entry) return null;

        try
        {
            Func<double[], (double C, double M, double Y, double K)>? toCmyk =
                BuildTintToCmyk(entry, doc, out int inputs);
            if (toCmyk is null || inputs < 1) return null;

            (double c, double m, double y, double k) = toCmyk([t]);
            return [c, m, y, k];
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics,
                $"OwnAlternateFor: /Colorants entry for '{name}' threw during evaluation; "
                + $"treating the component as having no individual alternate: {ex}");
            return null;
        }
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: PASS.

- [ ] **Step 5: Mutation-verify the throw guard**

The catch block cannot be reached by any fixture `ColourConformancePage` can build, so prove it exists rather than assuming. Temporarily change the `try` body's first line to `throw new InvalidOperationException("mutation");`.

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColourantComponentTests"`
Expected: **PASS** — every alternate becomes null, so only the four tests asserting a non-null alternate fail. Record which failed.

Now remove the `catch` block entirely (leaving the throw) and re-run.
Expected: tests **error** with `InvalidOperationException` escaping.

Then **revert both mutations** and re-run to confirm green. Verify with `git diff` that no production file has an uncommitted change. Report all three outcomes.

- [ ] **Step 6: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: no failures.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary.Tests/Rendering/ColourantComponentTests.cs
git commit -m "feat(colour): evaluate each NChannel spot's own alternate from /Colorants

Table 71 defines /Attributes /Colorants /<name> as a Separation space
describing 'the appearance of that colorant alone' — exactly the 'alternate
colour space of that component' that 8.6.6.5 requires for per-component
evaluation. Evaluated at the component's own tint and carried as CMYK.

Null is a meaningful answer meaning the component cannot be reverted
individually: no /Colorants entry, an entry that is not a Separation, an
alternate not reducible to CMYK, no tint, or a non-spot role. The consumer
falls back rather than inventing a colour.

The evaluation is wrapped because BuildTintToCmyk has no internal catch and
this runs on every colour-setting operator; a malformed Type 0/4 body can
build fine and throw at evaluation time. Mutation-verified."
```

---

## Task 4: Prove it changed nothing

**Files:** none in the engine. This task verifies.

**Interfaces:** consumes everything Tasks 1–3 produced, plus the Pellucid corpus render-hash gate.

**Background:** nothing consumes the new members, so **every corpus digest must be unchanged**. This is a stronger and simpler claim than Pass 1's, and if any digest moves it means population had a side effect on the flattened colour — which would be a defect in Tasks 1–3, not an expected result.

Two hazards, both observed repeatedly: `pack-local.ps1` rewrites `Pellucid/Directory.Build.props.local` and **silently drops the `LxmanPdfLibraryRenderingSkiaVersion` pin** (value to restore: `0.1.1-dev20260717153208`), and `PdfCompare.csproj` pins the engine independently and the script does not touch it.

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
cd /c/Users/jorda/RiderProjects/Pellucid && dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~GwgRenderHashGateTests"
```

Expected: **PASS with zero CHANGED lines.**

If any digest moved, **report BLOCKED** with the full list. Do not regenerate the baseline and do not set `PELLUCID_GWG_HASH_REGEN`. Nothing in this pass has a consumer, so a moved digest means population changed the flattened colour — a real defect.

The baseline header records the engine version that produced it and will differ after your repack. That is informational and is not asserted on; only moved digests matter.

- [ ] **Step 7: Run the remaining suites**

```bash
cd /c/Users/jorda/RiderProjects/Pellucid && dotnet test
cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests
```

Expected: Pellucid 1278 passing / 0 failing / 78 skipped; engine 2540 plus this plan's new tests, 0 failing.

- [ ] **Step 8: Report, do not commit**

The pin files are gitignored and `PdfCompare` is untracked. Nothing to commit. Report the recorded pins, NEWVERSION, the Skia-pin restoration, the resolved version, the gate result, and both suite totals.

---

## Self-Review

**Spec coverage.** The design's §4.2 asks for `ColorantOrigin` to gain `Subtype` and `Components` additively with a `ColourantComponent` record carrying name, role, tint and own-alternate — Tasks 1–3. §4.3's classification order (`/None` first, then process, then plane, then alternate) is split correctly: the engine owns the `/None`-and-process half here, and the plane-versus-alternate half is the compositor's and belongs to Pass 2b, since only the compositor holds the registry. The gate in §5 for this pass ("hashes byte-identical") is Task 4 Step 6. Deliberate deviations from the design, both stated in Scope: non-CMYK `/Process` deferred, and shadings unchanged.

**Placeholder scan.** No `TBD`, no "similar to Task N", no "add error handling". Every code step carries complete code. Task 4 contains one discovered value (NEWVERSION) that cannot be known before the pack runs; it is named and recorded in Step 2.

**Type consistency.** `ColourantComponent`'s four members are declared once in Task 1 and used with those names in Tasks 2 and 3. `RoleFor` gains its second parameter in Task 2 and both call sites are updated in the same step. `BuildComponents`'s signature is stable across all three tasks. `OwnAlternateFor` returns `IReadOnlyList<double>?`, matching `ColourantComponent.OwnAlternateCmyk`. `space.Names[i]!` is null-forgiving only where `OriginForColorSpaceObject` has already gated on `AllNamesResolved`.

**Known cost, accepted:** building a component's alternate calls `PdfFunction.Create` and evaluates it, per spot component, per colour-setting operator — gated on `IsNChannel`, which is one file in the 51-patch corpus. No cache; that is the thread-safety question Pass 1 deferred.

---

## Execution Handoff

Executing **subagent-driven**: four tasks, each with its own test cycle, and Tasks 1–3 are separable enough that a reviewer could reject one while approving its neighbours. Task 3's mutation-verify step and Task 4's cross-repo hard stop both benefit from an independent check.
