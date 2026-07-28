# Colour G-7 Plan 2 — don't simulate inks the device has (site 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop running the tint transform for an NChannel shading or mesh whose every colorant is a
process colorant with a plate — place the components on their plates directly instead.

**Architecture:** `ShadingBuilder.BuildCmykMapper` currently sends every `Separation`/`DeviceN` space
through `BuildTintToCmyk`. For a space whose colorants all have units, ISO 32000-2 §8.6.6.5 forbids
using an alternate at all. `ColorantOrigin.Placement` (landed in Plan 1, `79577ae`) already says which
plate each component belongs to; this plan consumes it at the one site that decides the CMYK ramp.
`BuildCmykMapper` has exactly two production callers — `ShadingBuilder.Build:66` (axial/radial) and
`MeshShadingReader:57` (mesh) — so a single change covers all three shading families.

**Tech Stack:** C# / .NET (PdfLibrary multi-targets net8.0/net9.0/net10.0; PdfLibrary.Tests targets
net10.0 only), xUnit.

**Design:** `Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md` §4.3 (site 5),
delivery step 3 in the revised §6.2.

**This is the FIRST change in the G-7 programme that can move a pixel.** Plan 1 shipped a carrier with
no consumer and deliberately ran no render-hash gate. This plan changes what gets painted, so the gate
is mandatory (Task 2) and is a real check rather than a formality.

## Global Constraints

- **BASE** = PDF `master` @ **`b429928`** (the plan commit itself; `79577ae` is the Plan 1 merge one
  commit earlier). Branch `colour/g7-all-process-shading`.
- Entering baselines, verified before any change: **engine 2656 passing / 0 failing**; build **0
  warnings** across net8.0/net9.0/net10.0. Pellucid **1304 / 0 / 78**, pinned to engine
  `2.5.1-dev20260727160451` (built from `fef2e7b`) — Plan 1 was never packed, so Pellucid has not
  seen `ColorantPlacement` at all.
- **`.superpowers/` is gitignored in BOTH repos.** The ledger lives on disk. Never write a step that
  commits it.
- **NEVER `git add -A` in the Pellucid repo** — there is a pre-existing untracked `website/` that is
  not ours.
- **Every assertion is a positional per-plate assertion, or it is decorative.** This defect is a
  permutation and the fixture below is designed to prove it: components `(0.36, 0.57, 0.02, 0.80)`
  produce plate bytes `(92, 145, 5, 204)` today and `(145, 5, 204, 92)` after. **Identical multiset,
  identical sum, identical max, identical total ink.** Any assertion phrased as total ink, sum, max,
  `Assert.Contains` or a loose ΔE **passes both ways**.
- **Every prescribed mutation names which assertion in which fixture changes value.** If it cannot be
  named, it is decorative.
- **A "must already pass" classification is a prediction.** Verify it; do not assert it.
- `pack-local.ps1` **DELETES the Skia pin on every run** — eight times on record. Re-add
  `<LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>`
  to `Pellucid/Directory.Build.props.local` by hand after packing, every time.
- Do not repack or repin until Task 2.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `PdfLibrary/Rendering/ShadingBuilder.cs` | **modify** (`BuildCmykMapper`, ~`:127-150`). Add the all-process bypass ahead of the tint-transform path, plus two private helpers. |
| `PdfLibrary.Tests/Rendering/ShadingAllProcessNChannelTests.cs` | **new.** The permutation fixture, the bypass, and the shapes that must still take the tint transform. |

`MeshShadingReader.cs` is **not modified** — it consumes `BuildCmykMapper` and inherits the fix. Task 1
Step 8 pins that inheritance rather than assuming it.

---

## Task 0: Measurement — no commits

**This task supplies the "before" numbers Task 1's mutation table names, and answers one safety
question that could stop the plan.** It writes no production code and makes no commits. Both trees must
be clean when it ends; delete any scaffold.

**Files:** none committed. Scratch probes under the session scratchpad directory, deleted at the end.

**Interfaces:**
- Consumes: `ColorantOrigin.Placement`, `ColorantPlacement.Slots` / `.SpotNames`, `ColorantSlot.Kind` /
  `.Index`, `ColorantSlotKind` — all shipped in `79577ae`.
- Produces: measurements M1-M5 and a SCOPE VERDICT, written to
  `PDF/.superpowers/sdd/2026-07-28-colour-g7-plan2-all-process-shading/progress.md`.

- [ ] **Step 1: Verify the entering baselines rather than trusting this plan**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git log --oneline -1                 # expect 79577ae
git status --porcelain               # expect empty
dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --no-incremental 2>&1 | grep -E "Warning\(s\)|error"
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug 2>&1 | tail -3
```

Expected: `0 Warning(s)`, `Failed: 0, Passed: 2656`. **If either differs, STOP and report.**

- [ ] **Step 2: M1 — what the fixture packs TODAY**

Build this exact colour space and record what `ShadingBuilder.BuildCmykMapper` returns for it, and
what that mapper packs for the component vector `[0.36, 0.57, 0.02, 0.80]`.

The space, with names order and `/Components` order deliberately different — that difference **is** the
defect:

```
[ /DeviceN [/Black /PrCyan /PrMagenta /PrYellow]
  /DeviceCMYK
  << /FunctionType 2 /Domain [0 1] /C0 [1 1 1 1] /C1 [1 1 1 1] /N 1 >>
  << /Subtype /NChannel
     /Process << /ColorSpace /DeviceCMYK
                 /Components [/PrCyan /PrMagenta /PrYellow /Black] >> >> ]
```

Follow the in-memory `PdfArray` idiom in `PdfLibrary.Tests/Rendering/ShadingBuilderColorSpaceTests.cs`
(`Reals`, `Type2`, `AxialShading`) rather than writing a PDF to disk.

**The tint transform is a deliberate constant `[1,1,1,1]`.** It is not an identity: it is chosen so
that "the transform ran" and "the transform was bypassed" cannot produce the same bytes. Record:

- a. Is `BuildCmykMapper` non-null for this space? **If it is null, STOP and report** — the plan's
  premise is that this space takes the tint-transform path, and a null mapper means it takes the RGB
  path instead, which is a different fix.
- b. The packed value for `[0.36, 0.57, 0.02, 0.80]`, as four separate plate bytes (C, M, Y, K).
  Prediction on record: `(255, 255, 255, 255)` — the constant transform. **Record the actual bytes.**
- c. The `ColorantPlacement` for the space: every slot's `Kind` and `Index`, and `SpotNames`.
  Prediction: `Slots = [Plate(3), Plate(0), Plate(1), Plate(2)]`, `SpotNames` empty.

- [ ] **Step 3: M2 — the throw-ordering question (this one can stop the plan)**

Task 1 makes `BuildCmykMapper` call `ColorSpaceResolver.OriginForColorSpaceObject`, which it does not
call today. That is this programme's dominant defect class: *a new member access resolves a PDF object
the previous code never touched and throws out of a path that used to succeed.*

The argument that it is safe is that **both callers already call `OriginForColorSpaceObject` on the
same object a few lines later** — `ShadingBuilder.cs:73` (after `:66`) and `MeshShadingReader.cs:61`
(after `:57`) — with no `try` in between, so a throw leaves the same method either way and only its
line number moves.

**Verify that argument rather than accepting it:**

- a. Read both call sites and confirm there is no `try`/`catch` between the `BuildCmykMapper` call and
  the `OriginForColorSpaceObject` call in either file, and no early `return` that could skip the
  latter.
- b. Build a space with a **corrupt alternate** (array element 2 an indirect reference to a
  non-existent object — the shape Pass 2b-engine used for `CorruptAlternateReference_...`) and record
  what `ShadingBuilder.Build` does with it TODAY: throws (with which exception type) or returns.
- c. State whether moving the resolution earlier can change the observable outcome for any input.

**If any caller can reach `BuildCmykMapper` without subsequently reaching
`OriginForColorSpaceObject`, STOP and report** — the fix then needs its own guard and the plan must be
revised before Task 1 writes code.

- [ ] **Step 4: M3 — corpus census for the shapes this plan changes**

Enumerate every **all-process** NChannel space (placement non-null, `SpotNames` empty) reached by a
shading or a mesh across the GWG corpus and the veraPDF files. Recurse into Form-XObject and
tiling-pattern resources.

Prediction on record, from Plan 1's Task 0: the two corpus disagreements between name-split and
placement are both `6-2-4-4-t02-pass-a` `/CS0`, and both are **fill** spaces, not shadings — so the
expected count here is **zero** and no digest moves. **Record the actual count.** If it is non-zero,
name the files: those are the fixtures Task 2's gate will move, and a moved digest then needs a
visual check, not a baseline update.

- [ ] **Step 5: M4 — does the plate mask change?**

On the flatten arm at `op=true` the process mask is the nonzero-markedness proxy against the per-pixel
colour, so **changing which plate carries which value can change which plates are marked.**

For the M1 fixture, record which of the four plates are non-zero before and after the permutation.
With all four components non-zero the mask is unchanged, but that is a property of this fixture, not a
general one. Also record the answer for a component vector with a **zero** in it — e.g.
`[0.0, 0.57, 0.02, 0.80]` — where the marked set genuinely differs before and after.

State plainly whether this plan can change a plate mask, and for which shapes.

- [ ] **Step 6: M5 — confirm the mesh path shares the fix**

Confirm by reading `MeshShadingReader.cs:57` that the mesh path obtains `toCmyk` from
`ShadingBuilder.BuildCmykMapper` and does not build its own CMYK mapper anywhere. Note that Plan 1's
Task 0 measured **there is no NChannel mesh anywhere in the corpus**, so the mesh half will again be
covered by a synthetic test and nothing else.

- [ ] **Step 7: Record the SCOPE VERDICT and clean up**

Write M1-M5 to the ledger as **numbers, not "as predicted"**. Then state:

1. Does Task 1 proceed, or is it blocked by M2?
2. Every plan defect found while measuring, with the plan text it collides with.

Delete every scratch probe, then verify:

```bash
cd /c/Users/jorda/RiderProjects/PDF && git status --porcelain      # expect empty
cd /c/Users/jorda/RiderProjects/Pellucid && git status --porcelain # expect ONLY "?? website/"
```

**No commits in this task.**

---

## Task 1: Bypass the tint transform when every colorant has a plate

**BLOCKED IF** Task 0's M2 found a path reaching `BuildCmykMapper` without subsequently reaching
`OriginForColorSpaceObject`. Read the SCOPE VERDICT before Step 1.

**Files:**
- Modify: `PdfLibrary/Rendering/ShadingBuilder.cs` (`BuildCmykMapper` and two new private helpers)
- Test: `PdfLibrary.Tests/Rendering/ShadingAllProcessNChannelTests.cs` (new)

**Interfaces:**
- Consumes: `ColorantOrigin.Placement`, `ColorantPlacement.Slots` / `.SpotNames`,
  `ColorantSlot.Kind` / `.Index`, `ColorantSlotKind.Plate`, `ColorSpaceResolver.OriginForColorSpaceObject`.
- Produces: no new public surface. Two private statics inside `ShadingBuilder`:
  `AllProcessPlacement(PdfObject?, PdfDocument?) -> ColorantPlacement?` and
  `PackByPlacement(double[], ColorantPlacement) -> uint`.

- [ ] **Step 1: Confirm the task is not blocked**

Read the SCOPE VERDICT in
`PDF/.superpowers/sdd/2026-07-28-colour-g7-plan2-all-process-shading/progress.md`. If it says blocked,
**STOP and report** — do not proceed on the argument that the change looks safe.

- [ ] **Step 2: Write the failing tests**

Create `PdfLibrary.Tests/Rendering/ShadingAllProcessNChannelTests.cs`:

```csharp
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.5, read physically: the alternate colour space and tint transform are the recipe
/// for SIMULATING an ink the output device has no unit for. When every colorant of an NChannel space is
/// a process colorant with a plate, the device has a unit for all of them and nothing may be simulated —
/// the components go straight to their plates.
///
/// <para>Every assertion here is PER PLATE. The defect is a permutation: the same four values in a
/// different order have the same sum, max, multiset and total ink, so any aggregate assertion passes
/// both before and after the fix.</para>
/// </summary>
public class ShadingAllProcessNChannelTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    private static PdfArray Names(params string[] n)
    {
        var items = new PdfObject[n.Length];
        for (var i = 0; i < n.Length; i++) items[i] = new PdfName(n[i]);
        return new PdfArray(items);
    }

    /// <summary>A tint transform returning a CONSTANT (1,1,1,1) for every input. Deliberately not an
    /// identity: it makes "the transform ran" and "the transform was bypassed" impossible to confuse,
    /// so a bypass failure shows up as 0xFFFFFFFF rather than as a subtly wrong ramp.
    ///
    /// <para><b>Its /Domain is one pair, and that is correct.</b> A <c>FunctionType 2</c> exponential
    /// is single-input by construction — <c>ExponentialFunction</c> consumes <c>input[0]</c> and
    /// ignores the declared arity — so <c>/Domain [0 1]</c> and <c>/Domain [0 1 0 1 0 1 0 1]</c>
    /// return byte-identical output. Measured, both arities give (255,255,255,255).</para></summary>
    private static PdfDictionary ConstantTint()
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(1, 1, 1, 1));
        d.Add(new PdfName("C1"), Reals(1, 1, 1, 1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    /// <summary>A true 4-in/4-out IDENTITY tint transform: a Type 4 PostScript calculator whose body
    /// is empty, so the four inputs are left on the stack as the four outputs. This is exactly the
    /// shape veraPDF <c>6-2-4-4-t02-pass-a</c> uses.
    ///
    /// <para><b>Why both fixtures exist.</b> The constant transform proves the BYPASS (bypassed and
    /// not-bypassed cannot produce the same bytes). Only the identity transform shows the DEFECT — a
    /// pure channel permutation, where the four values arrive in <c>/DeviceN</c> names order at CMYK
    /// positions. With the constant transform every "before" value is (255,255,255,255) and the
    /// permutation is invisible.</para></summary>
    private static PdfStream IdentityTint()
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(4));
        d.Add(new PdfName("Domain"), Reals(0, 1, 0, 1, 0, 1, 0, 1));
        d.Add(new PdfName("Range"), Reals(0, 1, 0, 1, 0, 1, 0, 1));
        return new PdfStream(d, Encoding.ASCII.GetBytes("{ }"));
    }

    private static PdfDictionary Attributes(PdfArray components, string processSpace = "DeviceCMYK")
    {
        var process = new PdfDictionary();
        process.Add(new PdfName("ColorSpace"), new PdfName(processSpace));
        process.Add(new PdfName("Components"), components);

        var attrs = new PdfDictionary();
        attrs.Add(new PdfName("Subtype"), new PdfName("NChannel"));
        attrs.Add(new PdfName("Process"), process);
        return attrs;
    }

    // Note the no-attributes overload still yields a FOUR-element array, which matters:
    // OriginForColorSpaceObject parses with minimumElements: 4 and returns no origin for a shorter one.
    private static PdfArray DeviceN(PdfArray names, PdfDictionary? attributes, PdfObject? tint = null)
    {
        PdfObject t = tint ?? ConstantTint();
        return attributes is null
            ? new PdfArray(new PdfName("DeviceN"), names, new PdfName("DeviceCMYK"), t)
            : new PdfArray(new PdfName("DeviceN"), names, new PdfName("DeviceCMYK"), t, attributes);
    }

    private static (byte C, byte M, byte Y, byte K) Cmyk(uint packed) =>
        ((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);

    // Names order and /Components order differ ON PURPOSE — that difference is the whole defect.
    //   names:      [Black, PrCyan, PrMagenta, PrYellow]
    //   /Components:[PrCyan, PrMagenta, PrYellow, Black]  => PrCyan=0, PrMagenta=1, PrYellow=2, Black=3
    // so the slots are [Plate(3), Plate(0), Plate(1), Plate(2)].
    private static PdfArray AllProcessSpace() =>
        DeviceN(Names("Black", "PrCyan", "PrMagenta", "PrYellow"),
                Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")));

    /// <summary>The same space with a true IDENTITY transform — the shape that shows the defect as a
    /// permutation rather than merely showing that the bypass fired.</summary>
    private static PdfArray AllProcessSpaceIdentity() =>
        DeviceN(Names("Black", "PrCyan", "PrMagenta", "PrYellow"),
                Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")),
                IdentityTint());

    [Fact]
    public void AllProcessNChannel_PlacesEachComponentOnItsOwnPlate_NotThroughTheTintTransform()
    {
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpace(), null);
        Assert.NotNull(toCmyk);

        // Components in NAMES order: Black=0.36, PrCyan=0.57, PrMagenta=0.02, PrYellow=0.80.
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57, 0.02, 0.80]));

        // PER PLATE. Black's 0.36 lands on K, PrCyan's 0.57 on C, PrMagenta's 0.02 on M,
        // PrYellow's 0.80 on Y. Running the constant tint transform would give (255,255,255,255).
        Assert.Equal(145, c);   // 0.57
        Assert.Equal(5, m);     // 0.02
        Assert.Equal(204, y);   // 0.80
        Assert.Equal(92, k);    // 0.36
    }

    [Fact]
    public void AllProcessNChannel_UnderAnIdentityTransform_TheDefectIsAPurePermutation()
    {
        // THE fixture that shows the defect rather than merely showing the bypass fired. Measured
        // before this change: (92, 145, 5, 204) — the four values in /DeviceN NAMES order at CMYK
        // positions (Black 0.36 -> C, PrCyan 0.57 -> M, PrMagenta 0.02 -> Y, PrYellow 0.80 -> K).
        // After: each on the plate /Process /Components gives it. IDENTICAL multiset, sum, max and
        // total ink — which is why this is asserted per plate and can be asserted no other way.
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpaceIdentity(), null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57, 0.02, 0.80]));

        Assert.Equal(145, c);   // was 92
        Assert.Equal(5, m);     // was 145
        Assert.Equal(204, y);   // was 5
        Assert.Equal(92, k);    // was 204
    }

    [Fact]
    public void AllProcessNChannel_IdentityTransform_ZeroComponent_MovesTheMARKEDPlate()
    {
        // The overprint consequence, pinned. Measured before: (0, 145, 5, 204) marks {M,Y,K}.
        // After: (145, 5, 204, 0) marks {C,M,Y}. One plate GAINED, one LOST — on the flatten arm at
        // op=true the mask is the nonzero-markedness proxy against this colour, so this is an
        // overprint-behaviour change, not only a colour change. A gained plate paints where a
        // backdrop used to survive; a lost plate preserves one that used to be overpainted.
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpaceIdentity(), null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.0, 0.57, 0.02, 0.80]));

        Assert.Equal(145, c);   // C GAINED: was 0
        Assert.Equal(5, m);
        Assert.Equal(204, y);
        Assert.Equal(0, k);     // K LOST: was 204
    }

    [Fact]
    public void AllProcessNChannel_ZeroComponent_LeavesThatPlateUnmarked()
    {
        // The mask consequence, pinned separately: a zero must land on the plate its POSITION names,
        // not the plate its ordinal would have. With the tint transform bypassed, C is the zero one.
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(AllProcessSpace(), null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.0, 0.02, 0.80]));

        Assert.Equal(0, c);     // PrCyan is the zero component and PrCyan IS the cyan plate
        Assert.Equal(5, m);
        Assert.Equal(204, y);
        Assert.Equal(92, k);
    }

    [Fact]
    public void NoneComponent_ContributesToNoPlate()
    {
        // /None is a colorant the printer deliberately does not run. Placement gives it Nothing,
        // and Nothing must reach no plate at all.
        PdfArray space = DeviceN(Names("PrCyan", "None"),
                                 Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")));

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 1.0]));

        Assert.Equal(92, c);    // PrCyan -> plate 0
        Assert.Equal(0, m);
        Assert.Equal(0, y);
        Assert.Equal(0, k);     // /None's 1.0 went nowhere
    }

    // --- shapes that must STILL take the tint transform ---

    [Fact]
    public void NChannelWithASpotComponent_StillRunsTheTintTransform()
    {
        // One colorant with no unit means the space still needs simulating. All-or-nothing:
        // the bypass must not fire just because SOME components are process.
        PdfArray space = DeviceN(Names("PrCyan", "GWG Green"),
                                 Attributes(Names("PrCyan", "PrMagenta", "PrYellow", "Black")));

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        // The constant transform ran. Asserted per plate, not as a tuple: a tuple literal of ints
        // will not unify with (byte, byte, byte, byte) for Assert.Equal's type inference.
        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57]));
        Assert.Equal(255, c);
        Assert.Equal(255, m);
        Assert.Equal(255, y);
        Assert.Equal(255, k);
    }

    [Fact]
    public void PlainDeviceN_StillRunsTheTintTransform()
    {
        // No /Attributes at all => no Subtype => not NChannel => no placement.
        PdfArray space = DeviceN(Names("PrCyan", "PrMagenta"), attributes: null);

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36, 0.57]));
        Assert.Equal(255, c);
        Assert.Equal(255, m);
        Assert.Equal(255, y);
        Assert.Equal(255, k);
    }

    [Fact]
    public void NChannelOverAOneChannelProcessSpace_StillRunsTheTintTransform()
    {
        // Under /DeviceGray a listed name also gets channel 0, byte-identical to a CMYK cyan.
        // ColorantPlacement refuses the whole table there, so the bypass must not fire.
        PdfArray space = DeviceN(Names("Ink1"),
                                 Attributes(Names("Ink1"), processSpace: "DeviceGray"));

        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(space, null);
        Assert.NotNull(toCmyk);

        (byte c, byte m, byte y, byte k) = Cmyk(toCmyk!([0.36]));
        Assert.Equal(255, c);
        Assert.Equal(255, m);
        Assert.Equal(255, y);
        Assert.Equal(255, k);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
cd /c/Users/jorda/RiderProjects/PDF
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug --filter "FullyQualifiedName~ShadingAllProcessNChannelTests"
```

Expected: the three "must still run the tint transform" tests **PASS** (they assert today's
behaviour), and the three bypass tests **FAIL by assertion** — reporting `(255,255,255,255)` where the
test wants the placement-derived bytes.

**Record which tests failed and with which actual values.** If any of the three "still runs the tint
transform" tests fails, that is a finding: those are predictions about current behaviour, not
assertions of the new behaviour, and a failure means the fixture does not do what this plan claims.

- [ ] **Step 4: Add the bypass**

In `PdfLibrary/Rendering/ShadingBuilder.cs`, replace the `Separation or DeviceN` case inside
`BuildCmykMapper` with:

```csharp
                    case "Separation" or "DeviceN":
                    {
                        // ISO 32000-2 §8.6.6.5: "only the ones not present on the output device shall
                        // use the alternate colour space of that component." Read physically: the
                        // alternate + tint transform are the recipe for SIMULATING an ink the device
                        // has no unit for. When every colorant of this space is a process colorant
                        // with a plate, the device has a unit for all of them and nothing may be
                        // simulated — the components go straight to their plates.
                        //
                        // This is the shape where running the transform is most obviously wrong: with
                        // an identity transform (veraPDF 6-2-4-4-t02-pass-a's is a Type 4 `{}`) the
                        // values arrive in NAMES order at CMYK positions, which is a pure channel
                        // permutation — same total ink, wrong plate for every component.
                        if (AllProcessPlacement(csObj, document) is { } placement)
                            return c => PackByPlacement(c, placement);

                        Func<double[], (double C, double M, double Y, double K)>? tint =
                            ColorSpaceResolver.BuildTintToCmyk(arr, document, out _);
                        if (tint is not null)
                            return c => { (double cc, double mm, double yy, double kk) = tint(c); return PackCmyk([cc, mm, yy, kk]); };
                        break;
                    }
```

and add these two private statics next to `PackCmyk`:

```csharp
    /// <summary>
    /// This space's colorant placement when EVERY component is on a process plate or on nothing —
    /// i.e. the output device has a unit for every colorant the space names, so §8.6.6.5 leaves
    /// nothing to simulate. Null otherwise, which sends the caller to the tint transform unchanged:
    /// any spot component at all (the space needs simulating), a non-NChannel space, an /All, a
    /// component whose plate cannot be determined, or an NChannel over a process space that is not
    /// four-channel. <see cref="ColorantPlacement.Build"/> owns all of those refusals.
    /// </summary>
    /// <remarks>
    /// <para><b>Emptiness of <see cref="ColorantPlacement.SpotNames"/> is the whole test.</b> Spot
    /// slots and spot names are appended in lockstep, so no spot names means no
    /// <see cref="ColorantSlotKind.Spot"/> slot exists — every slot is Plate or Nothing.</para>
    /// <para><b>On the resolution this adds.</b> Both callers of
    /// <see cref="BuildCmykMapper"/> — <c>Build</c> and <c>MeshShadingReader.Build</c> — already call
    /// <see cref="ColorSpaceResolver.OriginForColorSpaceObject"/> on this same object a few lines
    /// later, with no try/catch in between, so this resolves nothing they were not about to resolve
    /// anyway and a throw leaves the same method it always did. The cost is one extra resolve per
    /// shading BUILD, not per stop and not per pixel.</para>
    /// </remarks>
    private static ColorantPlacement? AllProcessPlacement(PdfObject? csObj, PdfDocument? document)
    {
        ColorantPlacement? placement =
            ColorSpaceResolver.OriginForColorSpaceObject(csObj, null, document)?.Placement;
        return placement is { SpotNames.Count: 0 } ? placement : null;
    }

    /// <summary>Packs components onto the plates <paramref name="placement"/> assigns them
    /// (0xCCMMYYKK). A <see cref="ColorantSlotKind.Nothing"/> slot contributes to no plate — /None is
    /// a colorant the printer deliberately does not run. Callers must have established that no slot is
    /// <see cref="ColorantSlotKind.Spot"/>; <see cref="AllProcessPlacement"/> is that check.</summary>
    private static uint PackByPlacement(double[] comps, ColorantPlacement placement)
    {
        var plates = new double[4];
        IReadOnlyList<ColorantSlot> slots = placement.Slots;
        for (var j = 0; j < slots.Count; j++)
        {
            ColorantSlot slot = slots[j];
            if (slot.Kind != ColorantSlotKind.Plate) continue;
            plates[slot.Index] = j < comps.Length ? comps[j] : 0.0;
        }
        return PackCmyk(plates);
    }
```

Also update `BuildCmykMapper`'s leading comment (currently "Separation/DeviceN with a DeviceCMYK
alternate run their tint transform to CMYK") — that sentence is now false for the all-process case and
would be the fifth false comment this programme has shipped.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug --filter "FullyQualifiedName~ShadingAllProcessNChannelTests"
```

Expected: PASS, 8 tests (Step 8 adds a ninth for the mesh path). Count them and report the actual
number rather than trusting this line.

### Two scope declarations this task must make explicitly

Both were surfaced by Task 0. Neither is a defect in the code; both are things a reviewer would
otherwise raise, and silence on them reads as an oversight rather than a decision.

**1. One behaviour change on malformed input, and it is in the safe direction.** The bypass returns
*before* `BuildTintToCmyk` runs, so an all-process NChannel space with a **corrupt tint transform**
goes from **throwing** to **returning a working mapper**. That is strictly better — the page renders
where it previously did not — and Task 0's M3 measured **zero** corpus instances of an all-process
NChannel shading, so nothing real changes. **State it in the commit message.** Do not add a test for
it: a test asserting "no longer throws" would pin the bypass's *position* relative to the transform
build, which is incidental, not contractual.

**2. `ShadingBuilder.cs:74`'s name-derived spot names are OUT of scope.** For the all-process space it
still classifies `PrCyan`/`PrMagenta`/`PrYellow` as spots by name and ships
`SpotInk.Names = [PrCyan, PrMagenta, PrYellow]` for a space with **zero** spots by placement — on the
very descriptor this task corrects. It is **inert**: Plan 1's Task 0 measured that
`PageColorantReader` classifies those names as **Process**, so they are never registered as planes,
`routeShadingSpots` is always False for them, the op always flattens, and the bogus `SpotInk` is
discarded unread.

That derivation is **site 3**, which was deliberately deferred because fixing it alone drops ink on a
*mixed* space. Do not fix it here. Record the limitation instead: **if a page ever did register such
a name as a spot plane, the op would route and consume `ShadingSpotSplit.Split`'s name-based process
CMYK, and this plan's fix would not apply.** Plan 1's measurement says that cannot happen today.

- [ ] **Step 6: Run the full engine suite and the multi-TFM build**

```bash
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj -c Debug 2>&1 | tail -3
dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --no-incremental 2>&1 | grep -E "Warning\(s\)|error"
```

Expected: 0 failed, 2656 + 6 = 2662; `0 Warning(s)`. **If any pre-existing test changed its result,
STOP and report which** — Task 0's M3 predicted no corpus shape reaches this branch, and a moved test
is that prediction failing.

- [ ] **Step 7: Run the prescribed mutations**

Revert after each; confirm the tree is clean between.

| # | Mutation | Must go red, by ASSERTION |
|---|----------|---------------------------|
| A | Delete the `if (AllProcessPlacement(...) is { } placement)` bypass | `AllProcessNChannel_PlacesEachComponentOnItsOwnPlate_...` — all four plates read 255. **Also** `..._UnderAnIdentityTransform_TheDefectIsAPurePermutation` — reads `(92,145,5,204)`, the pre-change permutation |
| B | `plates[slot.Index] = …` → `plates[j] = …` | `..._UnderAnIdentityTransform_TheDefectIsAPurePermutation` — C reads 92 not 145, K reads 204 not 92. **Named against the identity fixture deliberately**: see below |
| C | Change the guard to `placement is not null` (drop the `SpotNames.Count: 0` test) | `NChannelWithASpotComponent_StillRunsTheTintTransform` — reads the placement-derived bytes instead of 255 |
| D | Remove the `slot.Kind != ColorantSlotKind.Plate` continue, packing every slot | `NoneComponent_ContributesToNoPlate` — /None's 1.0 lands on a plate |
| E | `plates[slot.Index] = …` → `plates[j] = …` (same as B) | `AllProcessNChannel_IdentityTransform_ZeroComponent_MovesTheMARKEDPlate` — C reads 0 and K reads 204, i.e. the MARKED SET reverts. Confirms the mask claim is pinned and not merely documented |

**Mutation B is the one that must be checked most carefully.** For the all-process fixture the names
order is `[Black, PrCyan, PrMagenta, PrYellow]` and the plates are `[3, 0, 1, 2]`, so **no component
has `j == slot.Index`** and every plate moves. Confirm that; if any plate reads the same under B, the
fixture's `/Components` order is not doing its job and must be changed before this task completes.

**B and E are named against the IDENTITY fixture, not the constant one, and that distinction is the
plan's own Task 0 finding.** Under the constant transform every pre-change value is
`(255,255,255,255)`, so the constant fixture can show that the bypass *fired* but can never show
*what it fixed* — the permutation is invisible there. The constant fixture pins mutations A, C and D;
the identity fixture pins B and E. **Do not collapse them into one fixture.**

- [ ] **Step 8: Pin the mesh path's inheritance of the fix**

`MeshShadingReader` is not modified by this task; it inherits the fix through `BuildCmykMapper`. That
inheritance is currently asserted by nothing.

Add one test to `ShadingAllProcessNChannelTests.cs` that drives an all-process NChannel **mesh**
through `MeshShadingReader.Build` and asserts the resulting vertex CMYK **per plate**. Follow the
fixture idiom in `PdfLibrary.Tests/Rendering/MeshShadingReaderTests.cs` for constructing the mesh
stream.

Then re-run **mutation A** against it and confirm it goes red there too.

**If mutation A leaves the mesh test green, the mesh path is not actually covered** — say so plainly
and do not claim mesh coverage. Plan 1's Task 0 measured there is no NChannel mesh anywhere in the
corpus, so this synthetic test is the only thing that can ever pin it. That is exactly the position
`StencilInkFromFill` was in during Pass 2b-engine, where the branch shipped unpinned until review
caught it.

- [ ] **Step 9: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git add PdfLibrary/Rendering/ShadingBuilder.cs \
        PdfLibrary.Tests/Rendering/ShadingAllProcessNChannelTests.cs
git commit -m "fix(colour): stop simulating inks the device has, for all-process NChannel shadings

BuildCmykMapper sent every Separation/DeviceN space through its tint
transform. ISO 32000-2 8.6.6.5 says only colorants NOT present on the device
use an alternate -- so for an NChannel space whose every colorant is a process
colorant with a plate, the transform must not run at all. Physically: the
alternate is the recipe for faking an ink you have no unit for, and this is
the one shape where the device has a unit for everything.

It is also where running it is most visibly wrong. With an identity transform
(veraPDF 6-2-4-4-t02-pass-a's is a Type 4 empty procedure) the values arrive
in NAMES order at CMYK positions: a pure channel permutation, same total ink,
wrong plate for every component. The fixture here uses a CONSTANT transform
instead of an identity so that bypassed and not-bypassed cannot be confused.

Consumes ColorantOrigin.Placement, landed unconsumed in 79577ae. Covers
axial, radial and mesh at once -- BuildCmykMapper has exactly two production
callers and the mesh reader is one of them.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Gate, suites, and the pin

**This is a real render change, so the gate is evidence, not a formality.** Task 0's M3 predicted zero
corpus instances; this task tests that prediction.

**Files:** `Pellucid/Directory.Build.props.local`, `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj`
(pins only — no source changes).

- [ ] **Step 1: Pack the engine and restore the Skia pin**

Run `pack-local.ps1`. Record `NEWVERSION`.

**`pack-local.ps1` deletes the Skia pin on every run — eight times on record.** Immediately re-add to
`Pellucid/Directory.Build.props.local`:

```xml
<LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>
```

Then repin `PdfCompare.csproj` to `NEWVERSION`.

- [ ] **Step 2: Run the GWG render-hash gate**

Expected: `51 fixtures hashed, 51 baselined, 0 differences.`

**Check the embedded engine SHA, not the version number.** The gate prints `engine=2.5.1+<sha>`; that
SHA must equal the PDF HEAD under test. A matching version *number* does not prove the right build
ran — a stale package with the right name passes silently.

**If any digest moves, STOP and report which fixture.** Do NOT update a baseline. M3 predicted zero,
and a moved digest means either M3's census was incomplete or the change reaches further than
measured. Either way it needs a visual comparison before anything is re-baselined.

- [ ] **Step 3: Run the NChannel render-hash gate**

Expected: `3 fixtures hashed, 3 baselined, 0 differences`, same SHA check.

**Note this gate contains `6-2-4-4-t02-pass-a`**, whose `/CS0` is the space this whole plan is about.
If its *fill* rendering is what the gate hashes, it will not move — the fill path was fixed in Pass
2b-compositor. If the fixture also contains a shading of that space, it **will** move, and that
movement is the fix working. Task 0's M3 tells you which to expect; report the outcome against it
explicitly.

- [ ] **Step 4: Run the Pellucid suites**

```bash
cd /c/Users/jorda/RiderProjects/Pellucid
dotnet test 2>&1 | tail -5
```

Expected: **1304 passing / 0 failing / 78 skipped.**

The Cups project (39 + 39 skipped on Windows) is **not** in the default `dotnet test` set and must be
run by full path if wanted — which is why a filtered run shows 0 skipped. Verify rather than assume.

If a run hangs: that is the known App.Tests headless-session death, not this branch. Dump the **child**
`Pellucid.App.Tests.exe` (not `testhost.exe`) with `dotnet-stack`; a thread parked in
`AvaloniaTestCase.Run` awaiting a `RunSummary` with no dispatch thread and no test-body frame confirms
it. Kill the three-process tree and re-run. The `.trx` is useless — the run never reports.

- [ ] **Step 5: Record in the ledger**

Write both gate outputs verbatim including the SHAs, and both suite totals. **No commit** — the pin
files are local-only.

---

## Task 3: Documentation

**Files:**
- Modify: `Docs/colour/rendering-conformance.md` (the G-7 entry, and rows 5-3 / 5-10 if this narrows them)
- Modify: `Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md` (§1.1 site table, §6.2 delivery)

- [ ] **Step 1: Move site 5 from open to closed in the G-7 entry**

The G-7 entry lists site 5 (`ShadingBuilder.BuildCmykMapper`'s all-process arm) among the still-open
sub-gaps. Move it to closed, stating:

- what closed it, with the file and the condition (`Placement` non-null and `SpotNames` empty);
- that it covers **axial, radial and mesh** together, because `BuildCmykMapper` has two production
  callers and the mesh reader is one of them;
- that the evidence is **synthetic** — Task 0's M3 count of corpus instances (record the actual
  number), with the gate as a guard rather than as evidence;
- **whether the plate mask can change** and for which shapes, from Task 0's M5. Do not omit this
  because it is inconvenient: a mask change is an overprint-behaviour change and this document is
  where that gets recorded.

Leave sites 3 and 4 exactly as they are — still open, still required to land together.

- [ ] **Step 2: Update the design's site table and delivery**

In `Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md`:

- §1.1's five-site table: mark site 5 closed, with this plan's commit.
- §6.2's revised delivery: mark step 3 done and note that **the order was changed from the design's
  original** — site 5 was taken before sites 3+4 because it is engine-only, independently safe, and
  applies to a disjoint shape. Preserve the original ordering text as superseded rather than deleting
  it, per this programme's convention.

- [ ] **Step 3: Check rows 5-3 and 5-10 for narrowing**

Row 5-3's exclusion list names "shadings and meshes (G-7 — no per-op tint)". That exclusion is now
**partly** lifted: an all-process NChannel shading is evaluated per component. Rows 5-3 and 5-10 must
say precisely which part remains excluded (spaces with a spot component — sites 3 and 4) rather than
claiming shadings wholesale either way.

**Only change these rows if this plan actually narrowed them.** If the wording already covers it,
say so in the report and leave them alone — an unnecessary edit to a conformance row is its own risk.

- [ ] **Step 4: Commit (docs only)**

```bash
git add Docs/colour/rendering-conformance.md \
        Docs/superpowers/specs/2026-07-27-colour-g7-colorant-placement-design.md
git commit -m "docs(colour): record site 5 closed and narrow the shading exclusions

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage.** Design §4.3 (site 5) → Task 1. §1.1's site-5 row → Task 3 Step 2. §2.4's
carrier/compositor boundary is untouched: this plan reads `Placement` inside the engine and never asks
it about units. §5.2 (positional only) → Global Constraints plus every assertion in Task 1 Step 2.
§5.4 (mutations name their fixture) → Task 1 Step 7, and Step 8 for the mesh half. §6.1 rule 1(b) (a
fixture that can observe the fix) → satisfied synthetically and **said so explicitly**, because Plan
1's Task 0 measured the corpus cannot observe it. Sites 3 and 4 remain out of scope and are named as
such. §6.2's delivery order is changed, and Task 3 Step 2 records the change rather than quietly
reordering.

**Placeholder scan.** No TBD/TODO. Every code step carries complete code. Task 1 Step 8's mesh test is
described by fixture-source rather than transcribed, which is the one place a step says "follow the
idiom in <file>" instead of giving code — deliberate, because the mesh stream encoding is bit-packed
and copying it blind is worse than reading the existing builder. Its assertion shape and its mutation
are fully specified.

**Type consistency.** `AllProcessPlacement(PdfObject?, PdfDocument?) -> ColorantPlacement?` and
`PackByPlacement(double[], ColorantPlacement) -> uint` are used with those signatures in Steps 4 and 7.
`ColorantSlotKind.Plate`, `ColorantSlot.Kind`, `.Index`, `ColorantPlacement.Slots`, `.SpotNames` all
match the types shipped in `79577ae`. `BuildCmykMapper`'s existing signature is unchanged, so both
callers keep compiling untouched.

**Caught in this self-review and fixed inline.**

- Three tests compared a `(byte, byte, byte, byte)` tuple against an int tuple literal, which will not
  unify for `Assert.Equal`'s type inference. Rewritten as per-plate assertions, which the plan's own
  Global Constraints demand anyway.
- I also parameterised `ConstantTint`'s `/Domain` by input count, reasoning that under-declared arity
  would make the transform fail to build. **Task 0 measured that wrong and it has been reverted:** a
  `FunctionType 2` is single-input by construction, `ExponentialFunction` reads `input[0]` and ignores
  the declared arity, and both `/Domain [0 1]` and `/Domain [0 1 0 1 0 1 0 1]` return
  (255,255,255,255). The parameterisation was dead weight defending against a failure that cannot
  occur. Recorded because a self-review guess is a prediction like any other and this one was wrong.

**Amended after Task 0 (six plan defects, all in this plan's or its brief's text — zero in code).**

- **The fixture could not show the defect it was chosen to characterise.** The constant `(1,1,1,1)`
  transform makes every pre-change value `(255,255,255,255)`, so it proves the bypass *fired* but
  never shows *what it fixed* — and the plan's Global Constraints quoted the permutation numbers
  `(92,145,5,204) → (145,5,204,92)` as if they were this fixture's. **Both fixtures now exist:** the
  constant one pins mutations A/C/D, and a true Type 4 `{ }` identity — the shape veraPDF
  `6-2-4-4-t02-pass-a` actually uses — pins B/E and carries the permutation and mask assertions.
  This is the sixth time in this programme a prescribed mutation has been aimed at a fixture that
  cannot observe it.
- **The mask claim is now pinned, not merely documented.** Task 0's M4 measured that at
  `[0.0, 0.57, 0.02, 0.80]` the marked set moves `{M,Y,K} → {C,M,Y}` — one plate gained, one lost.
  That is an overprint change, and I-1's lesson is that such a change can be invisible in colour, so
  it gets its own test and its own mutation rather than a sentence in the matrix.
- **BASE corrected** from `79577ae` to `b429928`; the brief's Step 1 could not have passed as written.
- **Two scope declarations added to Task 1** (the corrupt-tint-transform throw→return, and site 3's
  name-derived spot names being deliberately out of scope), so neither reads as an oversight.

**Known weaknesses, stated rather than hidden.**

1. **Task 1 adds a PDF-object resolution to a method that had none.** That is this programme's
   dominant defect class, and the safety argument (both callers resolve the same object a few lines
   later anyway) is checked by Task 0's M2 **before** Task 1 writes code, with a STOP if it fails.
2. **The mesh half has no corpus instance and never will**, so Step 8's synthetic test is its only
   possible pin — and the step is written to fail loudly rather than let it ship unpinned.
3. **The gate cannot corroborate this fix** if M3's count is zero. It runs as a guard against
   unintended movement, and Task 3 Step 1 requires that limitation be written into the matrix rather
   than implied by a green result.
4. **The plate mask may change** for a component vector containing a zero. Task 0's M5 measures it and
   Task 3 Step 1 requires it be recorded — it is an overprint-behaviour change, not merely a colour
   change, and this programme's I-1 was exactly a change that was invisible in colour.
