# Colour Pass 1 — One Parser for Separation/DeviceN Colour Spaces

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the five independent positional parsers for `[/Separation …]` and `[/DeviceN …]` colour spaces with one parsed record, changing no rendered output, and read `/Attributes` while doing it so Pass 2 (NChannel) has a place to stand.

**Architecture:** Add an internal `SpotColorSpace` record with a single `TryParse`, then migrate the query members of `ColorSpaceResolver` onto it one group at a time. Each migration is behaviour-preserving and is proven so by the engine suite plus the Pellucid corpus render-hash gate built in Pass 0.

**Tech Stack:** C# / .NET, xUnit, `PdfLibrary` (engine). Verification crosses into the Pellucid repo once, in Task 6.

---

## Scope: two deliberate reductions from the design

The design (`Docs/superpowers/specs/2026-07-26-colour-decision-surface-design.md` §4.1–4.2) called for more than this plan does. Both cuts are YAGNI, and both are recorded here so the difference is not mistaken for an oversight.

**1. `ResolveColorSpace` and `InitialColorFor` are NOT migrated.** Pass 2 (G-4) extends only the *query* side — `OriginFor` for the colourant carrier, `BuildTintRamp` for the per-colourant ramps. `ResolveColorSpace`'s positional parsing is legacy debt that does not grow when G-4 lands, so migrating it is risk without payoff for this program. **Consequence:** `ColorSpaceResolver` keeps two of its four dispatch heads, and gap G-12 is not closed.

**2. No cache.** The design called for caching the parse. `PdfFunction` is `internal abstract class` with `public abstract double[] Evaluate(double[] input)`; whether a given subclass holds mutable evaluation state is a per-subclass property that has not been verified. Today every call site constructs a fresh `PdfFunction`, so **sharing one instance is a hazard the cache would newly introduce**, not one that already exists. The cache is orthogonal to G-4. **Consequence:** G-12 stays open, and closing it later requires answering the thread-safety question first.

**3. The additive `ColorantOrigin` fields (design §4.2) move to their own plan, Pass 1b.** They add fields nothing consumes — a different risk profile from this pass, which changes only how existing answers are derived. This plan *does* parse `/Subtype`, `/Colorants` and `/Process` into the model and test that parse against the real GWG081 structure, so Pass 1b is small when it comes.

---

## Global Constraints

- Repo: `C:\Users\jorda\RiderProjects\PDF` for Tasks 1–5. Task 6 also touches `C:\Users\jorda\RiderProjects\Pellucid` and `C:\Users\jorda\PDFs\PdfCompare`.
- **This pass changes no rendered output.** Any diff in the Pellucid corpus hash baseline is a defect in this pass, not an expected result.
- `ColorSpaceResolver` is `internal`, and Pellucid is not on `PdfLibrary.csproj`'s `InternalsVisibleTo` list, so every member signature here may change freely. Do not change `ColorantOrigin` — that is a public record consumed across the package boundary, and it belongs to Pass 1b.
- `PdfGraphicsState.ResolvedFillColor` / `ResolvedFillColorSpace` and `PdfColorToRgb.ToRgb` are documented public renderer SPI in `Docs/RendererSpi.md` §4. Their shapes must not change.
- Engine test baseline is **2493 passing, 0 failing**. Every task ends green.
- Never `git commit` with a failing suite.

---

## The correctness trap this plan exists around

The five members being unified **do not agree on how strict to be**, and a naive unification silently changes behaviour on malformed input. Verified by reading each one:

| Member | Minimum array count | Requires every DeviceN name to be a `PdfName`? |
|---|---|---|
| `PaintsNothing(PdfObject?, …)` (`:806`) | `>= 2` | No — a non-name element simply is not `/None`, so the answer is `false` |
| `PlatesForColorSpaceObject` (`:606`) | `>= 2` | **Yes** — returns `null` if any element fails |
| `OriginForColorSpaceObject` (`:853`) | `>= 4` | **Yes** — returns `null` if any element fails |
| `BuildTintToRgb` (`:389`) | `>= 4` | **No** — it uses only `names.Count` |
| `BuildTintToCmyk` (`:452`) | `>= 4` | **No** — it uses only `names.Count` |

So `TryParse` must be **permissive**, and each caller keeps its own strictness:

- `TryParse` requires only `Count >= 2`. `AlternateObject` and `TintTransformObject` are **nullable** and are `null` when the array is too short. Members that need them check for null themselves, exactly as they do today via their own count guards.
- `Names` is `IReadOnlyList<string?>`, where `null` marks an element that did not resolve to a `PdfName`. Count is always right (so `BuildTintTo*` keep working), and `AllNamesResolved` lets the strict members reject exactly as before.
- `PaintsNothing`'s DeviceN rule becomes "every element equals `"None"`", and a `null` element is not `"None"`, so it returns `false` — matching today.

If you find yourself making `TryParse` stricter to simplify a caller, stop: that is the bug this table is here to prevent.

Note also that `Indexed` is **not** part of `SpotColorSpace`. `PaintsNothing` and `PlatesForColorSpaceObject` each recurse into their base space for `Indexed`; that recursion stays in those members.

---

## File Structure

| File | Responsibility |
|---|---|
| `PdfLibrary/Rendering/SpotColorSpace.cs` | **Create.** The parsed record and its single `TryParse`. No rendering logic. |
| `PdfLibrary/Rendering/ColorSpaceResolver.cs` | **Modify.** Query members migrate onto `SpotColorSpace`; `Deref` becomes `internal static`. |
| `PdfLibrary.Tests/Rendering/SpotColorSpaceTests.cs` | **Create.** Parser tests, including `/Attributes`. |
| `PdfLibrary.Tests/Rendering/ColorSpaceResolverCharacterizationTests.cs` | **Create.** Pins the three migrated members that have no direct test today. |

---

## Task 1: Characterize the untested members before touching them

**Files:**
- Test: `PdfLibrary.Tests/Rendering/ColorSpaceResolverCharacterizationTests.cs` (create)

**Interfaces:**
- Consumes: `ColorSpaceResolver.BuildTintToCmyk`, `ColorSpaceResolver.OriginForColorSpaceObject`, `ColorSpaceResolver.PaintsNothing(string?, PdfDictionary?, PdfDocument?)`; the test helper `ColourConformancePage.Build(string colorSpaceArrayLiteral, string contentStream)`.
- Produces: nothing consumed by later tasks. These tests are the safety net Tasks 3–5 refactor under.

**Background for the implementer:** the census found that three of the members this plan migrates have **no direct test** — they are exercised only through their callers. Refactoring them under only-integration coverage means a behaviour change can hide behind a caller that happens not to exercise the difference. These tests come first, before any production code moves.

Follow the existing fixture idiom exactly: build a throwaway one-page PDF containing the colour-space array as a resource, load it, and pull the `PdfArray` back out. `ColorSpaceResolverPaintsNothingTests.cs:20-31` is the model.

- [ ] **Step 1: Write the characterization tests**

Create `PdfLibrary.Tests/Rendering/ColorSpaceResolverCharacterizationTests.cs`:

```csharp
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Direct coverage for the ColorSpaceResolver query members that had none before Pass 1 —
/// BuildTintToCmyk, OriginForColorSpaceObject and the resource-name PaintsNothing overload were
/// exercised only through their callers. These pin current behaviour so the Pass 1 migration onto
/// SpotColorSpace has a net under it: a behaviour change that a caller happens not to exercise
/// would otherwise pass unnoticed.
/// </summary>
public class ColorSpaceResolverCharacterizationTests
{
    private const string Tint2 = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>";
    private const string TintGray = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    private static (PdfDictionary Spaces, PdfDocument Doc) ParseWithResources(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = doc.GetPage(0)!;
        return (page.GetResources()!.GetColorSpaces()!, doc);
    }

    // --- BuildTintToCmyk ---

    [Fact]
    public void BuildTintToCmyk_SeparationWithCmykAlternate_EvaluatesTheTransform()
    {
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");

        Func<double[], (double C, double M, double Y, double K)>? f =
            ColorSpaceResolver.BuildTintToCmyk(cs, null, out int inputs);

        Assert.NotNull(f);
        Assert.Equal(1, inputs);
        (double c, double m, double y, double k) = f!([1.0]);
        Assert.Equal(0.5, c, 3);
        Assert.Equal(0.0, m, 3);
        Assert.Equal(1.0, y, 3);
        Assert.Equal(0.0, k, 3);
    }

    [Fact]
    public void BuildTintToCmyk_GrayAlternate_MapsToKOnly()
    {
        // §10.3.3: DeviceGray separates onto the black plate alone, k = 1 - gray. At tint 1 the
        // transform yields gray 0, so k must be 1 — full black, not white.
        PdfArray cs = Parse("[/Separation /Spot1 /DeviceGray " + TintGray + "]");

        Func<double[], (double C, double M, double Y, double K)>? f =
            ColorSpaceResolver.BuildTintToCmyk(cs, null, out int _);

        Assert.NotNull(f);
        (double c, double m, double y, double k) = f!([1.0]);
        Assert.Equal(0.0, c, 3);
        Assert.Equal(0.0, m, 3);
        Assert.Equal(0.0, y, 3);
        Assert.Equal(1.0, k, 3);
    }

    [Fact]
    public void BuildTintToCmyk_RgbAlternate_ReturnsNull()
    {
        // Not convertible to native ink: the caller falls back to the RGB path.
        PdfArray cs = Parse("[/Separation /Spot1 /DeviceRGB "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");

        Assert.Null(ColorSpaceResolver.BuildTintToCmyk(cs, null, out int _));
    }

    [Fact]
    public void BuildTintToCmyk_SeparationAll_PaintsEveryPlateUncomplemented()
    {
        // §8.6.6.4 row 4-10: alternate and tint transform are ignored for /All, and on a subtractive
        // device the tint applies DIRECTLY. Tint 0.25 must be 0.25 on all four plates, not 0.75.
        PdfArray cs = Parse("[/Separation /All /DeviceRGB "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");

        Func<double[], (double C, double M, double Y, double K)>? f =
            ColorSpaceResolver.BuildTintToCmyk(cs, null, out int inputs);

        Assert.NotNull(f);
        Assert.Equal(1, inputs);
        (double c, double m, double y, double k) = f!([0.25]);
        Assert.Equal(0.25, c, 3);
        Assert.Equal(0.25, m, 3);
        Assert.Equal(0.25, y, 3);
        Assert.Equal(0.25, k, 3);
    }

    [Fact]
    public void BuildTintToCmyk_SeparationNone_ReturnsNull()
    {
        PdfArray cs = Parse("[/Separation /None /DeviceCMYK " + Tint2 + "]");
        Assert.Null(ColorSpaceResolver.BuildTintToCmyk(cs, null, out int _));
    }

    [Fact]
    public void BuildTintToCmyk_DeviceN_ReportsOneInputPerColorantName()
    {
        PdfArray cs = Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>]");

        ColorSpaceResolver.BuildTintToCmyk(cs, null, out int inputs);
        Assert.Equal(2, inputs);
    }

    // --- OriginForColorSpaceObject ---

    [Fact]
    public void OriginForColorSpaceObject_Separation_CarriesNameTintAndAlternate()
    {
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, [0.75], null);

        Assert.NotNull(origin);
        Assert.Equal(["GWGGreen"], origin!.Names);
        Assert.Equal([0.75], origin.Tints);
        Assert.Equal("DeviceCMYK", origin.AlternateSpace);
    }

    [Fact]
    public void OriginForColorSpaceObject_DeviceN_CarriesEveryNameInOrder()
    {
        PdfArray cs = Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK "
                            + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, [0.25, 0.5], null);

        Assert.NotNull(origin);
        Assert.Equal(["GWGGreen", "Cyan"], origin!.Names);
        Assert.Equal([0.25, 0.5], origin.Tints);
    }

    [Fact]
    public void OriginForColorSpaceObject_NullRawColor_YieldsEmptyTints()
    {
        // Shadings resolve their origin with rawColor null — a gradient has no single per-op tint.
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(cs, null, null);

        Assert.NotNull(origin);
        Assert.Empty(origin!.Tints);
        Assert.Equal(["GWGGreen"], origin.Names);
    }

    [Fact]
    public void OriginForColorSpaceObject_IccBased_ReturnsNull()
    {
        PdfArray cs = Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]");
        // A non-Separation/DeviceN family must yield no origin. Reuse an Indexed array as the negative.
        PdfArray indexed = Parse("[/Indexed /DeviceRGB 1 <FF0000 00FF00>]");

        Assert.NotNull(ColorSpaceResolver.OriginForColorSpaceObject(cs, [1.0], null));
        Assert.Null(ColorSpaceResolver.OriginForColorSpaceObject(indexed, [1.0], null));
    }

    // --- PaintsNothing (resource-name overload) ---

    [Fact]
    public void PaintsNothing_ByResourceName_ResolvesThroughTheColorSpaceDictionary()
    {
        (PdfDictionary spaces, PdfDocument doc) =
            ParseWithResources("[/Separation /None /DeviceRGB "
                               + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");
        using (doc)
        {
            Assert.True(ColorSpaceResolver.PaintsNothing("Cs0", spaces, doc));
        }
    }

    [Theory]
    [InlineData("DeviceGray")]
    [InlineData("DeviceRGB")]
    [InlineData("DeviceCMYK")]
    [InlineData("Pattern")]
    public void PaintsNothing_ByResourceName_DeviceAndPatternSpacesAreNeverSuppressed(string csName)
    {
        (PdfDictionary spaces, PdfDocument doc) =
            ParseWithResources("[/Separation /None /DeviceRGB "
                               + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");
        using (doc)
        {
            Assert.False(ColorSpaceResolver.PaintsNothing(csName, spaces, doc));
        }
    }

    [Fact]
    public void PaintsNothing_ByResourceName_UnknownNameIsFalse()
    {
        (PdfDictionary spaces, PdfDocument doc) =
            ParseWithResources("[/Separation /None /DeviceRGB "
                               + "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]");
        using (doc)
        {
            Assert.False(ColorSpaceResolver.PaintsNothing("NoSuchSpace", spaces, doc));
            Assert.False(ColorSpaceResolver.PaintsNothing("Cs0", null, doc));
            Assert.False(ColorSpaceResolver.PaintsNothing(null, spaces, doc));
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColorSpaceResolverCharacterizationTests"`
Expected: PASS, all tests.

These pin existing behaviour, so passing immediately is correct and expected — they are a net, not a spec. **Because they cannot be seen to fail first, mutation-verify them** (next step) rather than trusting a green run.

- [ ] **Step 3: Mutation-verify the net, then revert**

This project's standing rule: a test that pins already-correct behaviour must be shown capable of failing before it is trusted.

In `ColorSpaceResolver.BuildTintToCmyk` (`:510`), temporarily change the DeviceGray branch from
`return (0, 0, 0, C01(1.0 - (r.Length > 0 ? r[0] : 0)));`
to
`return (0, 0, 0, C01(r.Length > 0 ? r[0] : 0));`
(dropping the `1.0 -` complement).

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColorSpaceResolverCharacterizationTests"`
Expected: **`BuildTintToCmyk_GrayAlternate_MapsToKOnly` FAILS.**

Then **revert the mutation** and re-run to confirm green again. Record both outcomes in your report.

- [ ] **Step 4: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: 2493 + the new tests passing, 0 failing.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary.Tests/Rendering/ColorSpaceResolverCharacterizationTests.cs
git commit -m "test(colour): characterize the untested ColorSpaceResolver query members

BuildTintToCmyk, OriginForColorSpaceObject and the resource-name PaintsNothing
overload had no direct tests — only integration coverage through callers. Pins
them before the Pass 1 migration so a behaviour change cannot hide behind a
caller that happens not to exercise the difference. Mutation-verified."
```

---

## Task 2: The `SpotColorSpace` record and its single parser

**Files:**
- Create: `PdfLibrary/Rendering/SpotColorSpace.cs`
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs:568` (`Deref` visibility)
- Test: `PdfLibrary.Tests/Rendering/SpotColorSpaceTests.cs` (create)

**Interfaces:**
- Consumes: `ColorSpaceResolver.Deref(PdfObject, PdfDocument?)`, whose visibility changes from `private static` to `internal static` in this task. Its body does not change.
- Produces, and used by Tasks 3–5:
  - `internal sealed record SpotColorSpace(string Family, IReadOnlyList<string?> Names, PdfObject? AlternateObject, string AlternateSpaceName, PdfObject? TintTransformObject, string Subtype, PdfDictionary? Colorants, PdfDictionary? Process)`
  - `internal bool AllNamesResolved { get; }`
  - `internal bool IsNChannel { get; }`
  - `internal static bool TryParse(PdfObject? csObj, PdfDocument? doc, out SpotColorSpace? space)`

**Background for the implementer:** re-read "The correctness trap this plan exists around" above before writing `TryParse`. The parser is deliberately permissive; strictness lives in the callers. Nothing consumes `Subtype`, `Colorants` or `Process` in this pass — they are parsed and tested here so Pass 2 has a tested foundation, and the tests in this task are their consumer.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Rendering/SpotColorSpaceTests.cs`:

```csharp
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The single parser for [/Separation …] and [/DeviceN …]. Deliberately PERMISSIVE: it accepts
/// arrays too short to carry an alternate or tint transform (reporting those as null) and records
/// unresolvable DeviceN name elements as null rather than rejecting the space. Five ColorSpaceResolver
/// members disagreed about strictness before Pass 1 — see the plan's arity table — so strictness stays
/// with each caller and only the PARSING is shared.
/// </summary>
public class SpotColorSpaceTests
{
    private const string Tint2 = "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0.5 0 1 0] /N 1 >>";

    private static PdfArray Parse(string pdfArrayLiteral)
    {
        byte[] pdf = ColourConformancePage.Build(pdfArrayLiteral, "1 0 0 rg 0 0 1 1 re f");
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PdfDictionary colorSpaces = page.GetResources()!.GetColorSpaces()!;
        return (PdfArray)colorSpaces[new PdfName("Cs0")]!;
    }

    [Fact]
    public void Separation_ParsesNameAlternateAndTransform()
    {
        Assert.True(SpotColorSpace.TryParse(Parse("[/Separation /GWGGreen /DeviceCMYK " + Tint2 + "]"),
            null, out SpotColorSpace? s));

        Assert.Equal("Separation", s!.Family);
        Assert.Equal(["GWGGreen"], s.Names);
        Assert.True(s.AllNamesResolved);
        Assert.Equal("DeviceCMYK", s.AlternateSpaceName);
        Assert.NotNull(s.AlternateObject);
        Assert.NotNull(s.TintTransformObject);
    }

    [Fact]
    public void DeviceN_ParsesEveryNameInOrder()
    {
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal("DeviceN", s!.Family);
        Assert.Equal(["GWGGreen", "Cyan"], s.Names);
        Assert.True(s.AllNamesResolved);
    }

    [Fact]
    public void ArrayShorterThanFour_StillParses_WithNullAlternateAndTransform()
    {
        // PaintsNothing and PlatesForColorSpaceObject accept Count >= 2 today. If TryParse demanded 4,
        // a [/Separation /None] array would silently stop being suppressed.
        Assert.True(SpotColorSpace.TryParse(Parse("[/Separation /None]"), null, out SpotColorSpace? s));

        Assert.Equal(["None"], s!.Names);
        Assert.Null(s.AlternateObject);
        Assert.Null(s.TintTransformObject);
        Assert.Equal(string.Empty, s.AlternateSpaceName);
    }

    [Fact]
    public void DeviceN_NonNameElement_IsNull_ButTheCountIsStillRight()
    {
        // BuildTintToRgb/BuildTintToCmyk use only Names.Count and must keep working; the strict members
        // use AllNamesResolved to reject. Element 1 here is a number, not a name.
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen 42] /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal(2, s!.Names.Count);
        Assert.Equal("GWGGreen", s.Names[0]);
        Assert.Null(s.Names[1]);
        Assert.False(s.AllNamesResolved);
    }

    [Fact]
    public void ArrayAlternate_ReportsItsFamilyName()
    {
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/Separation /Spot1 [/CalRGB << /WhitePoint [0.9505 1 1.089] >>] "
                  + "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >>]"),
            null, out SpotColorSpace? s));

        Assert.Equal("CalRGB", s!.AlternateSpaceName);
    }

    [Theory]
    [InlineData("[/Indexed /DeviceRGB 1 <FF0000 00FF00>]")]
    [InlineData("[/ICCBased 5 0 R]")]
    public void NonSpotFamilies_DoNotParse(string literal)
    {
        Assert.False(SpotColorSpace.TryParse(Parse(literal), null, out SpotColorSpace? s));
        Assert.Null(s);
    }

    [Fact]
    public void NullObject_DoesNotParse()
    {
        Assert.False(SpotColorSpace.TryParse(null, null, out SpotColorSpace? s));
        Assert.Null(s);
    }

    // --- /Attributes: parsed here, consumed in Pass 2 ---

    [Fact]
    public void Subtype_DefaultsToDeviceN_WhenNoAttributesDictionary()
    {
        // ISO 32000-2 Table 70: "Values shall be DeviceN or NChannel. Default value: DeviceN."
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK " + Tint2 + "]"), null, out SpotColorSpace? s));

        Assert.Equal("DeviceN", s!.Subtype);
        Assert.False(s.IsNChannel);
        Assert.Null(s.Colorants);
        Assert.Null(s.Process);
    }

    [Fact]
    public void NChannelAttributes_AreParsed()
    {
        // The shape of GWG081_DeviceN-Support_5c_X1a.pdf, the corpus's only NChannel file.
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/DeviceN [/GWGGreen /Cyan] /DeviceCMYK " + Tint2 + " << "
                  + "/Subtype /NChannel "
                  + "/Colorants << /GWGGreen [/Separation /GWGGreen /DeviceCMYK " + Tint2 + "] >> "
                  + "/Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow /Black] >> "
                  + ">>]"),
            null, out SpotColorSpace? s));

        Assert.Equal("NChannel", s!.Subtype);
        Assert.True(s.IsNChannel);
        Assert.NotNull(s.Colorants);
        Assert.True(s.Colorants!.TryGetValue(new PdfName("GWGGreen"), out PdfObject? _));
        Assert.NotNull(s.Process);
        Assert.True(s.Process!.TryGetValue(new PdfName("Components"), out PdfObject? _));
    }

    [Fact]
    public void SeparationNeverCarriesAttributes()
    {
        // /Attributes is a DeviceN-only element; a five-element Separation array is malformed and its
        // fifth element must not be mistaken for an attributes dictionary.
        Assert.True(SpotColorSpace.TryParse(
            Parse("[/Separation /Spot1 /DeviceCMYK " + Tint2 + " << /Subtype /NChannel >>]"),
            null, out SpotColorSpace? s));

        Assert.Equal("DeviceN", s!.Subtype);
        Assert.False(s.IsNChannel);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~SpotColorSpaceTests"`
Expected: **compile error** — `The type or namespace name 'SpotColorSpace' could not be found`.

- [ ] **Step 3: Make `Deref` internal**

In `PdfLibrary/Rendering/ColorSpaceResolver.cs:568`, change:

```csharp
    private static PdfObject Deref(PdfObject obj, PdfDocument? document) =>
```

to:

```csharp
    internal static PdfObject Deref(PdfObject obj, PdfDocument? document) =>
```

The body is unchanged. This is the only edit to `ColorSpaceResolver.cs` in this task.

- [ ] **Step 4: Write the record and parser**

Create `PdfLibrary/Rendering/SpotColorSpace.cs`:

```csharp
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// One parsed <c>[/Separation name alternateSpace tintTransform]</c> or
/// <c>[/DeviceN [names] alternateSpace tintTransform attributes?]</c> colour space.
///
/// <para>Before Pass 1 this shape was re-derived positionally by five separate ColorSpaceResolver
/// members, each with slightly different strictness. This parser is deliberately the PERMISSIVE union
/// of them — it accepts arrays too short to carry an alternate or tint transform, and records a DeviceN
/// name element that is not a <see cref="PdfName"/> as <c>null</c> rather than rejecting the whole
/// space. Callers keep their own strictness via <see cref="AllNamesResolved"/> and null checks, so
/// unifying the parse changes no behaviour.</para>
///
/// <para><c>Indexed</c> is deliberately NOT modelled here. The members that handle it recurse into the
/// base space themselves, which keeps that recursion where its callers can see it.</para>
/// </summary>
/// <param name="Family">"Separation" or "DeviceN".</param>
/// <param name="Names">One entry for Separation, one per colorant for DeviceN. An entry is null when
/// that element did not resolve to a name; the COUNT is always the declared colorant count.</param>
/// <param name="AlternateObject">The dereferenced alternate space object, or null when the array is
/// shorter than three elements.</param>
/// <param name="AlternateSpaceName">The alternate's family name ("DeviceCMYK", "Lab", "CalRGB", …), or
/// the empty string when absent or unrecognised.</param>
/// <param name="TintTransformObject">The dereferenced tint transform object, or null when the array is
/// shorter than four elements. Deliberately NOT a built <c>PdfFunction</c>: building one per call is
/// today's behaviour, and caching a shared instance is a thread-safety question this pass does not
/// answer (see the Pass 1 plan's scope note).</param>
/// <param name="Subtype">/Attributes /Subtype, defaulting to "DeviceN" per ISO 32000-2 Table 70.
/// Always "DeviceN" for a Separation space.</param>
/// <param name="Colorants">/Attributes /Colorants, or null. Required to be present for NChannel spaces
/// that carry spot colourants. Parsed but not yet consumed — Pass 2 (G-4) is its consumer.</param>
/// <param name="Process">/Attributes /Process, or null. Parsed but not yet consumed.</param>
internal sealed record SpotColorSpace(
    string Family,
    IReadOnlyList<string?> Names,
    PdfObject? AlternateObject,
    string AlternateSpaceName,
    PdfObject? TintTransformObject,
    string Subtype,
    PdfDictionary? Colorants,
    PdfDictionary? Process)
{
    /// <summary>True when every entry in <see cref="Names"/> resolved to a name. The members that
    /// refuse to answer for a malformed name list gate on this; the ones that need only the count
    /// (the tint-transform builders) ignore it.</summary>
    internal bool AllNamesResolved
    {
        get
        {
            for (var i = 0; i < Names.Count; i++)
                if (Names[i] is null)
                    return false;
            return true;
        }
    }

    /// <summary>ISO 32000-2 §8.6.6.5: NChannel spaces evaluate their components individually. Nothing
    /// consumes this yet — Pass 2 does.</summary>
    internal bool IsNChannel => Subtype == "NChannel";

    /// <summary>Parses a colour-space object into a <see cref="SpotColorSpace"/>. Returns false for
    /// every other family (including Indexed and ICCBased), for a null object, for an array shorter
    /// than two elements, and for a Separation whose colorant name does not resolve.</summary>
    internal static bool TryParse(PdfObject? csObj, PdfDocument? doc, out SpotColorSpace? space)
    {
        space = null;
        if (csObj is null) return false;

        PdfObject resolved = ColorSpaceResolver.Deref(csObj, doc);
        if (resolved is not PdfArray { Count: >= 2 } arr || arr[0] is not PdfName family)
            return false;

        List<string?> names;
        switch (family.Value)
        {
            case "Separation":
                // Every caller requires a Separation's colorant name, so a missing one is a parse
                // failure rather than a null entry.
                if (ColorSpaceResolver.Deref(arr[1], doc) is not PdfName sepName) return false;
                names = [sepName.Value];
                break;

            case "DeviceN":
                if (ColorSpaceResolver.Deref(arr[1], doc) is not PdfArray namesArr) return false;
                names = new List<string?>(namesArr.Count);
                foreach (PdfObject nameObj in namesArr)
                    names.Add(ColorSpaceResolver.Deref(nameObj, doc) is PdfName n ? n.Value : null);
                break;

            default:
                return false;
        }

        PdfObject? altObj = arr.Count >= 3 ? ColorSpaceResolver.Deref(arr[2], doc) : null;
        string altName = altObj switch
        {
            PdfName n => n.Value,
            PdfArray { Count: >= 1 } a when a[0] is PdfName t => t.Value,
            _ => string.Empty,
        };

        PdfObject? tintObj = arr.Count >= 4 ? ColorSpaceResolver.Deref(arr[3], doc) : null;

        var subtype = "DeviceN";
        PdfDictionary? colorants = null;
        PdfDictionary? process = null;

        // /Attributes is the optional fifth element and is a DeviceN-only feature.
        if (family.Value == "DeviceN" && arr.Count >= 5
            && ColorSpaceResolver.Deref(arr[4], doc) is PdfDictionary attrs)
        {
            if (attrs.TryGetValue(new PdfName("Subtype"), out PdfObject? stObj)
                && ColorSpaceResolver.Deref(stObj!, doc) is PdfName st)
                subtype = st.Value;

            if (attrs.TryGetValue(new PdfName("Colorants"), out PdfObject? coObj))
                colorants = ColorSpaceResolver.Deref(coObj!, doc) as PdfDictionary;

            if (attrs.TryGetValue(new PdfName("Process"), out PdfObject? prObj))
                process = ColorSpaceResolver.Deref(prObj!, doc) as PdfDictionary;
        }

        space = new SpotColorSpace(family.Value, names, altObj, altName, tintObj, subtype, colorants, process);
        return true;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~SpotColorSpaceTests"`
Expected: PASS, all tests.

If `ColourConformancePage.Build` cannot express one of the literals (for example the `[/ICCBased 5 0 R]` case, which needs an object that exists), replace that specific negative case with another non-spot family the helper can build — `[/CalRGB << /WhitePoint [0.9505 1 1.089] >>]` — and note the substitution in your report. Do not weaken any positive assertion.

- [ ] **Step 6: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: no failures. Nothing production-facing changed except `Deref`'s visibility, so the count should be the previous total plus the new tests.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Rendering/SpotColorSpace.cs PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary.Tests/Rendering/SpotColorSpaceTests.cs
git commit -m "feat(colour): one parser for Separation/DeviceN colour spaces

Five ColorSpaceResolver members re-derived the same shape positionally, each
with slightly different strictness about array length and name resolution.
SpotColorSpace.TryParse is the permissive union of all five; strictness stays
with the callers via AllNamesResolved and null checks, so nothing changes.

Also reads /Attributes — Subtype, Colorants, Process — which no render path
has ever read. Nothing consumes them yet; Pass 2 (NChannel, G-4) does. They
are parsed and tested here so that pass starts from a tested foundation.

No production call site migrated yet; Deref becomes internal so the new file
can share it."
```

---

## Task 3: Migrate `PaintsNothing` and the plate-mask members

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `PaintsNothing(PdfObject?, PdfDocument?)` (`:806`), `PlatesForColorSpaceObject` (`:606`)

**Interfaces:**
- Consumes: `SpotColorSpace.TryParse`, `SpotColorSpace.Names`, `SpotColorSpace.AllNamesResolved` (Task 2).
- Produces: no signature changes. `PaintsNothing` and `PlatesForColorSpaceObject` keep their exact public signatures and behaviour.

**Background for the implementer:** these two members keep their `Indexed` recursion — `SpotColorSpace` does not model Indexed. Only the Separation/DeviceN name extraction moves onto the parser. `OverprintPlatesFor` (`:583`) is not edited at all: it already delegates to `PlatesForColorSpaceObject` after its own name/device guards.

Re-read the arity table at the top of this plan. `PaintsNothing` returns `false` — not `true` — when a DeviceN name element is not a name, because such an element is not `/None`. `PlatesForColorSpaceObject` returns `null` in the same situation. Preserving that difference is the point of this task.

- [ ] **Step 1: Replace `PaintsNothing(PdfObject?, PdfDocument?)`'s body**

Replace the body of `PaintsNothing(PdfObject? csObj, PdfDocument? doc)` (currently `:806-833`) with:

```csharp
    public static bool PaintsNothing(PdfObject? csObj, PdfDocument? doc)
    {
        if (csObj is null) return false;
        csObj = Deref(csObj, doc);

        // Indexed paints through its BASE space, so it marks nothing exactly when the base marks
        // nothing. SpotColorSpace does not model Indexed, so this recursion stays here.
        if (csObj is PdfArray { Count: >= 2 } indexedArr && indexedArr[0] is PdfName { Value: "Indexed" })
            return PaintsNothing(indexedArr[1], doc);

        if (!SpotColorSpace.TryParse(csObj, doc, out SpotColorSpace? space)) return false;

        // Separation: the single colorant is /None.
        // DeviceN: EVERY component is /None. A name that did not resolve is not "None", so it makes
        // the answer false — matching the pre-Pass-1 behaviour exactly.
        if (space!.Names.Count == 0) return false;
        for (var i = 0; i < space.Names.Count; i++)
            if (space.Names[i] != "None")
                return false;
        return true;
    }
```

- [ ] **Step 2: Replace `PlatesForColorSpaceObject`'s colorant-gathering**

Replace the body of `PlatesForColorSpaceObject` (currently `:606-662`) with:

```csharp
    public static (bool C, bool M, bool Y, bool K)? PlatesForColorSpaceObject(PdfObject? csObj, PdfDocument? doc)
    {
        if (csObj is null) return null;
        csObj = Deref(csObj, doc);

        // An Indexed image's samples are palette indices into its base space; the plates it marks are
        // the base space's plates (e.g. Indexed[/DeviceN[Black Cyan]] duotone marks K + C).
        if (csObj is PdfArray { Count: >= 2 } indexedArr && indexedArr[0] is PdfName { Value: "Indexed" })
            return PlatesForColorSpaceObject(indexedArr[1], doc);

        if (!SpotColorSpace.TryParse(csObj, doc, out SpotColorSpace? space)) return null;

        // Any unresolvable colorant name means we cannot know the plate set — fall back to OPM.
        if (!space!.AllNamesResolved || space.Names.Count == 0) return null;

        bool c = false, m = false, y = false, k = false;
        foreach (string? name in space.Names)
        {
            switch (name)
            {
                case "Cyan": c = true; break;
                case "Magenta": m = true; break;
                case "Yellow": y = true; break;
                case "Black": k = true; break;
                case "All": c = m = y = k = true; break;
                case "None": break;   // marks no colorant (ISO 32000 §8.6.6.4) — DeviceN padding
                default:
                    // A real spot colorant isn't a CMYK plate → fall back to OPM behaviour.
                    return null;
            }
        }

        return (c, m, y, k);
    }
```

- [ ] **Step 3: Run the targeted tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColorSpaceResolverPaintsNothingTests|FullyQualifiedName~DeviceNOverprintEngineTests|FullyQualifiedName~OverprintPlatesTests|FullyQualifiedName~ColorSpaceResolverCharacterizationTests"`
Expected: PASS, no failures.

- [ ] **Step 4: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: no failures, same total as after Task 2.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs
git commit -m "refactor(colour): PaintsNothing and PlatesForColorSpaceObject parse via SpotColorSpace

Both keep their own Indexed recursion (SpotColorSpace does not model Indexed)
and their own strictness: PaintsNothing answers false for an unresolvable
DeviceN name because such an element is not /None, while PlatesForColorSpaceObject
answers null. Only the positional parsing moves. No behaviour change."
```

---

## Task 4: Migrate `OriginForColorSpaceObject`

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `OriginForColorSpaceObject` (`:853`)

**Interfaces:**
- Consumes: `SpotColorSpace.TryParse`, `.Names`, `.AllNamesResolved`, `.AlternateSpaceName` (Task 2).
- Produces: no signature change. Still returns `ColorantOrigin?`.

**Background for the implementer:** this member is the one Pass 2 extends, so leaving it clean matters. Note it currently requires `Count >= 4` while `TryParse` requires only `>= 2` — that difference must be preserved explicitly, because a `[/Separation /Spot1]` array yields no origin today and must continue to. `TintTransformObject` being non-null is exactly the "had at least four elements" condition, so gate on that.

Do **not** change `ColorantOrigin` in this task. Its additive fields belong to Pass 1b.

- [ ] **Step 1: Replace the body**

Replace `OriginForColorSpaceObject` (currently `:853-891`) with:

```csharp
    public static ColorantOrigin? OriginForColorSpaceObject(
        PdfObject? csObj, IReadOnlyList<double>? rawColor, PdfDocument? doc)
    {
        if (!SpotColorSpace.TryParse(csObj, doc, out SpotColorSpace? space)) return null;

        // Pre-Pass-1 this member required Count >= 4, unlike PaintsNothing/PlatesForColorSpaceObject.
        // A non-null TintTransformObject is exactly that condition, so gate on it to keep a short
        // [/Separation /Spot1] array yielding no origin, as before.
        if (space!.TintTransformObject is null) return null;

        if (!space.AllNamesResolved || space.Names.Count == 0) return null;

        var names = new List<string>(space.Names.Count);
        foreach (string? n in space.Names) names.Add(n!);

        double[] tints = rawColor is null ? [] : [.. rawColor];
        return new ColorantOrigin(names, tints, space.AlternateSpaceName);
    }
```

- [ ] **Step 2: Run the targeted tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~ColorantOriginTests|FullyQualifiedName~ColorantOriginRenderTests|FullyQualifiedName~ShadingColorantOriginTests|FullyQualifiedName~PageColorantsTests|FullyQualifiedName~ColorSpaceResolverCharacterizationTests"`
Expected: PASS, no failures.

- [ ] **Step 3: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: no failures.

- [ ] **Step 4: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs
git commit -m "refactor(colour): OriginForColorSpaceObject parses via SpotColorSpace

Keeps its stricter arity rule — it alone required four elements — by gating on
a non-null TintTransformObject, so a short [/Separation /Spot1] array still
yields no origin. ColorantOrigin itself is untouched; its additive fields are
Pass 1b."
```

---

## Task 5: Migrate the tint-transform builders

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` — `BuildTintToRgb` (`:389`), `BuildTintToCmyk` (`:452`), `BuildTintRamp` (`:523`)

**Interfaces:**
- Consumes: `SpotColorSpace.TryParse`, `.Family`, `.Names`, `.AlternateObject`, `.AlternateSpaceName`, `.TintTransformObject` (Task 2).
- Produces: no signature changes.

**Background for the implementer:** these three are the ones that use only `Names.Count` and never require the names to resolve — `AllNamesResolved` must **not** be consulted here. Both builders also handle `/All` and `/None` before reading the alternate space, per §8.6.6.4 row 4-10, and that ordering must be preserved: the reserved-name checks come before the alternate-space gate.

`BuildTintRamp` calls `BuildTintToRgb` internally (`:546`) for its representative solid; leave that call as it is.

Each builder keeps its own `Count < 4` rejection, expressed as `TintTransformObject is null`.

- [ ] **Step 1: Replace `BuildTintToRgb`'s parsing preamble**

In `BuildTintToRgb`, replace everything from the opening guard through the `PdfFunction? tint = …` line (currently `:392-431`) with:

```csharp
        inputComponents = 0;
        if (!SpotColorSpace.TryParse(baseArray, document, out SpotColorSpace? space)) return null;
        if (space!.TintTransformObject is null) return null;   // pre-Pass-1: Count < 4 → null

        // §8.6.6.4 row 4-10: for /All and /None the alternateSpace and tintTransform SHALL be ignored.
        // Handled before either is read, exactly as ResolveSeparation does for fills — otherwise an /All
        // IMAGE paints a different colour from an identical /All FILL.
        if (PaintsNothing(baseArray, document)) return null;   // /None: build no evaluator at all
        if (space.Family == "Separation" && space.Names[0] == "All")
        {
            // Additive device: the subtractive tint is complemented before being applied to R, G and B,
            // so tint t is the neutral 1 − t.
            inputComponents = 1;
            return t =>
            {
                byte g = Clamp255(1.0 - (t.Length > 0 ? t[0] : 0.0));
                return (g, g, g);
            };
        }

        // Names.Count is the colorant count for both families; the names themselves need not resolve.
        inputComponents = space.Names.Count;
        if (inputComponents < 1) return null;

        string altSpace = space.AlternateSpaceName;
        if (altSpace.Length == 0) return null;
        PdfArray? labArray = altSpace == "Lab" ? space.AlternateObject as PdfArray : null;

        PdfFunction? tint = PdfFunction.Create(space.TintTransformObject, document);
        if (tint is null) return null;
```

Leave the returned lambda (currently `:433-442`) exactly as it is.

- [ ] **Step 2: Replace `BuildTintToCmyk`'s parsing preamble**

In `BuildTintToCmyk`, replace everything from the opening guard through the `PdfFunction? tint = …` line (currently `:455-503`) with:

```csharp
        inputComponents = 0;
        if (!SpotColorSpace.TryParse(baseArray, document, out SpotColorSpace? space)) return null;
        if (space!.TintTransformObject is null) return null;   // pre-Pass-1: Count < 4 → null

        // §8.6.6.4 row 4-10, as in BuildTintToRgb. Placed BEFORE the alternate-space gate below, because
        // the clause says the alternate space is ignored for these names — an /All space is convertible
        // here whatever its alternate happens to be.
        if (PaintsNothing(baseArray, document)) return null;   // /None: build no evaluator at all
        if (space.Family == "Separation" && space.Names[0] == "All")
        {
            // Subtractive device: the tint applies DIRECTLY to every colourant, uncomplemented.
            inputComponents = 1;
            return t =>
            {
                double v = Math.Clamp(t.Length > 0 ? t[0] : 0.0, 0.0, 1.0);
                return (v, v, v, v);
            };
        }

        inputComponents = space.Names.Count;
        if (inputComponents < 1) return null;

        string altSpace = space.AlternateSpaceName;
        // A DeviceGray alternate is just as convertible as a DeviceCMYK one: PDF 32000-1 §10.3.3 makes
        // DeviceGray a DEVICE space that separates onto the black plate alone (k = 1 − gray), which is
        // exactly the rule the FILL path already applies (Pellucid's InkDecider.ToCmyk). Rejecting it sent
        // a [/Separation /Black /DeviceGray] image down the managed RGBA path, where RGB(g,g,g) was
        // ICC-converted into a RICH gray (ink on all four plates) that no longer matched an adjacent
        // DeviceCMYK(0,0,0,k) box — GWG230 c/d, which draw the same X twice (once as a fill, once as an
        // image) and so rendered HALF an X: the fill half correct, the image half rich.
        //
        // CalGray is deliberately NOT included: it is CIE-based, not a device space, so it keeps colour
        // management and must still take the RGBA path (same split InkDecider.ToCmyk makes for fills).
        var grayAlt = altSpace == "DeviceGray";
        if (altSpace != "DeviceCMYK" && !grayAlt) return null;

        PdfFunction? tint = PdfFunction.Create(space.TintTransformObject, document);
        if (tint is null) return null;
```

Leave the returned lambda (currently `:505-513`) exactly as it is.

- [ ] **Step 3: Replace `BuildTintRamp`'s transform lookup**

In `BuildTintRamp`, replace the opening guard and function creation (currently `:526-530`) with:

```csharp
        if (colorantIndex < 0 || colorantIndex >= inputCount)
            return (null, (0, 0, 0));
        if (!SpotColorSpace.TryParse(baseArray, doc, out SpotColorSpace? space))
            return (null, (0, 0, 0));
        if (space!.TintTransformObject is null)
            return (null, (0, 0, 0));   // pre-Pass-1: baseArray.Count < 4

        PdfFunction? tint = PdfFunction.Create(space.TintTransformObject, doc);
        if (tint is null) return (null, (0, 0, 0));
```

Everything from `var ramp = new double[samples][];` onward is unchanged, including the internal `BuildTintToRgb` call and the `catch` block.

- [ ] **Step 4: Run the targeted tests**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~SeparationDeviceNPaletteTests|FullyQualifiedName~TintRampTests|FullyQualifiedName~SeparationAlternateSpaceTests|FullyQualifiedName~PdfImageToCmykTests|FullyQualifiedName~ShadingSpotInkTests|FullyQualifiedName~ColorSpaceResolverCharacterizationTests"`
Expected: PASS, no failures.

- [ ] **Step 5: Run the full engine suite**

Run: `dotnet test PdfLibrary.Tests`
Expected: no failures.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Rendering/ColorSpaceResolver.cs
git commit -m "refactor(colour): tint-transform builders parse via SpotColorSpace

BuildTintToRgb, BuildTintToCmyk and BuildTintRamp use only the colorant COUNT
and never required the names to resolve, so they deliberately do not consult
AllNamesResolved — that asymmetry with the strict members is preserved, not
smoothed over. The /All and /None checks stay ahead of the alternate-space
gate, per 8.6.6.4 row 4-10. No behaviour change."
```

---

## Task 6: Prove it across the repo boundary

**Files:**
- Modify (temporarily, engine repo): none — this task verifies rather than changes the engine.
- Modify: `C:\Users\jorda\RiderProjects\Pellucid\Directory.Build.props.local` (gitignored)
- Modify: `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj:22`

**Interfaces:**
- Consumes: everything Tasks 2–5 produced, plus the Pellucid corpus render-hash gate built in Pass 0 (`GwgRenderHashGateTests`).
- Produces: the evidence that this pass is behaviour-neutral. Nothing depends on it downstream.

**Background for the implementer:** the engine suite proves the engine's own contracts, but the claim this whole pass rests on is *"no rendered pixel changed."* Only the Pellucid corpus gate can test that, and it consumes the engine as a NuGet package from a local feed. So the engine must be packed and Pellucid repinned.

**Two hazards, both previously observed on this machine:**

1. `pack-local.ps1` **rewrites `Directory.Build.props.local` and silently drops the `LxmanPdfLibraryRenderingSkiaVersion` pin.** After every pack you must re-add it, or Pellucid resolves a different Skia rendering package.
2. `PdfCompare.csproj` pins the engine version independently and is not touched by the pack script.

The Skia pin value to restore is `0.1.1-dev20260717153208`.

- [ ] **Step 1: Record the current pins before packing**

Run:

```bash
cat /c/Users/jorda/RiderProjects/Pellucid/Directory.Build.props.local
grep -n "Lxman.PdfLibrary" /c/Users/jorda/PDFs/PdfCompare/PdfCompare.csproj
```

Write both values into your report. You will restore the Skia pin from this record.

- [ ] **Step 2: Pack the engine**

From `C:\Users\jorda\RiderProjects\PDF`, run `./pack-local.ps1` in PowerShell.

Then read the new `LxmanPdfLibraryVersion` that the script wrote into
`C:\Users\jorda\RiderProjects\Pellucid\Directory.Build.props.local`. Record it in your report — later steps refer to it as NEWVERSION.

- [ ] **Step 3: Restore the Skia pin**

Confirm whether `Directory.Build.props.local` still contains `LxmanPdfLibraryRenderingSkiaVersion`. If it does not, add it back inside the same `<PropertyGroup>`, so the file reads:

```xml
  <PropertyGroup>
    <LxmanPdfLibraryVersion>NEWVERSION</LxmanPdfLibraryVersion>
    <LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>
  </PropertyGroup>
```

(substituting the actual NEWVERSION the script wrote).

- [ ] **Step 4: Repin PdfCompare**

In `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj`, update the `Lxman.PdfLibrary` `PackageReference` `Version` attribute to NEWVERSION.

- [ ] **Step 5: Confirm Pellucid actually resolved the new engine**

Run:

```bash
cd /c/Users/jorda/RiderProjects/Pellucid && dotnet restore
grep -o "Lxman.PdfLibrary/[0-9][^\"]*" Pellucid.Core/obj/project.assets.json | sort -u
```

Expected: exactly NEWVERSION. If a stale version appears, clear the NuGet cache for the package and restore again — a stale local-feed cache has bitten this workflow before. **Do not proceed until the resolved version matches**; running the gate against the old engine would prove nothing.

- [ ] **Step 6: Run the corpus render-hash gate — THE gate for this pass**

Run:

```bash
cd /c/Users/jorda/RiderProjects/Pellucid && dotnet test Pellucid.Rendering.Avalonia.Tests --filter "FullyQualifiedName~GwgRenderHashGateTests"
```

Expected: **PASS, with zero CHANGED lines.**

This is the whole point of Pass 1. If any fixture's digest moved, this pass is **not** behaviour-neutral and something in Tasks 3–5 changed rendering. **Do not regenerate the baseline. Do not set `PELLUCID_GWG_HASH_REGEN`.** Report BLOCKED with the full list of CHANGED lines from the failure message.

Note that the failure message and baseline header both report the engine version; it will legitimately differ from the baseline's recorded version, because you just repacked. That difference is informational and is *not* itself a failure — only moved digests are.

- [ ] **Step 7: Run the rest of the Pellucid suite**

Run: `cd /c/Users/jorda/RiderProjects/Pellucid && dotnet test`
Expected: 1278 passing, 0 failing, 78 skipped (`Print.Cups` on both target frameworks — expected on Windows).

- [ ] **Step 8: Run the engine suite one final time**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests`
Expected: no failures.

- [ ] **Step 9: Report, do not commit**

The gitignored pin files are not committed, and `PdfCompare` is a separate untracked project. There is nothing to commit in this task.

Write into your report: the recorded old pins, NEWVERSION, confirmation that the Skia pin was restored, the resolved engine version from `project.assets.json`, the gate result, and both suite totals.

---

## Self-Review

**Spec coverage.** The design's §4.1 asks for a typed model replacing positional parsing — Tasks 2–5, narrowed to the query members with the reason stated in "Scope: two deliberate reductions". §4.2's additive `ColorantOrigin` is explicitly deferred to Pass 1b, and §4.1's cache is explicitly deferred with a stated reason. The design's Pass 1 gate ("hashes byte-identical; engine + Pellucid suites green") is Task 6 Steps 6–8. The design's §6.3 coverage debt is addressed for the three members this pass migrates (Task 1); `LabRange` and `InitialColorFor` remain untested and are **not** covered here, because this pass does not touch them — recorded below as remaining debt.

**Placeholder scan.** No `TBD`, no "similar to Task N", no "add error handling". Every code step carries complete code. Task 6 contains one discovered value (NEWVERSION) that cannot be known before the pack runs; it is named, recorded in Step 2, and referenced explicitly rather than left vague. Task 2 Step 5 contains one contingency (an alternative negative-case literal if the fixture helper cannot express `[/ICCBased 5 0 R]`) with the exact substitute given.

**Type consistency.** `SpotColorSpace`'s eight positional members are declared once in Task 2 and used with those exact names in Tasks 3, 4 and 5. `Names` is `IReadOnlyList<string?>` throughout, which is why Task 4 copies into a `List<string>` with `n!` before constructing `ColorantOrigin` (whose `Names` is `IReadOnlyList<string>`). `AllNamesResolved` is consulted in Tasks 3 and 4 and deliberately **not** in Task 5. `TintTransformObject is null` is the uniform stand-in for the old `Count < 4` guard in Tasks 4 and 5. `ColorSpaceResolver.Deref` changes from `private static` to `internal static` in Task 2 Step 3 and is called from `SpotColorSpace` thereafter.

**Remaining debt this pass does not close** — carry into the matrix rather than losing:
- G-12 (`cs`/`CS` double resolution) — untouched; the cache was deferred.
- `ResolveColorSpace` and `InitialColorFor` keep their positional parsing and their own dispatch heads.
- `LabRange` still has zero test coverage; `InitialColorFor` still has no direct test.

---

## Execution Handoff

Executing **subagent-driven**: six tasks, each with its own test cycle, and Tasks 3–5 are exactly the shape where a fresh reviewer should be able to reject one migration while approving its neighbours. Task 6 crosses a repo boundary and has a hard stop condition that benefits from an independent check.
