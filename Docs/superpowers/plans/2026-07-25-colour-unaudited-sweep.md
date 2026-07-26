# ␀ Sweep — Clearing the Unaudited Colour Rows: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drive the renderer conformance matrix's ␀ (not-yet-audited) count from 9 to 0 — by reclassifying five file-validity rows into a new class F, and auditing (and where necessary fixing) the four that are genuinely renderer behaviour.

**Architecture:** Engine-only. The one behavioural change is row 4-4: `cs`/`CS` must set the current colour to the new space's initial value, which is per-space (ISO 32000-2 §8.6.8, Table 73). `PdfContentProcessor` cannot do this alone — it has no resource dictionary — so it gains a virtual hook that `PdfRenderer` (which does) overrides.

**Tech Stack:** C# / .NET 10, xUnit, SkiaSharp for pixel assertions, existing `ColourConformancePage` harness.

## Global Constraints

- **Repo:** `C:\Users\jorda\RiderProjects\PDF` only, branch `colour/unaudited-sweep`. No Pellucid changes until the verification task.
- **Clause-citing tests only:** every test names the ISO clause and row it pins, in a doc comment.
- **Every new test MUST be observed to fail before it counts as passing.** Ordinary TDD red where behaviour changes; a deliberate mutation where the test pins already-correct behaviour. A test that has only ever been green is evidence of nothing.
- **Backdrop / prior-colour must contrast** with the colour under test. A test whose expected colour coincides with what the old code produced is vacuous.
- **Painted-output claims are asserted on rendered pixels**, not resolver return values.
- **Scope guard:** only 4-4's fix is budgeted. If auditing 4-2, 5-8 or 5-13 uncovers a violation needing new machinery, a cross-repo change, or a device-policy decision, **record it as a gap with evidence and stop** — an audited row landing on ❌ with a written-up gap is a complete result, not an unfinished one.
- Baseline: `PdfLibrary.Tests` 2484 pass / 0 fail.

## File Structure

- Modify `PdfLibrary/Rendering/ColorSpaceResolver.cs` — add `InitialColorFor`.
- Modify `PdfLibrary/Content/PdfContentProcessor.cs` — add the `OnColorSpaceChanged` hook; call it from `cs`/`CS`.
- Modify `PdfLibrary/Rendering/PdfRenderer.cs` — override the hook.
- Create `PdfLibrary.Tests/Rendering/InitialColorValueTests.cs`.
- Create `PdfLibrary.Tests/Rendering/SeparationTintRangeTests.cs` (4-2) and `DeviceNNoneReversionTests.cs` (5-8).
- Modify `Docs/colour/rendering-conformance.md` — class F, row updates, score, gaps.

---

### Task 1: Introduce class F and migrate five rows (doc-only)

**Files:**
- Modify: `Docs/colour/rendering-conformance.md`

**Interfaces:** Consumes nothing. Produces the class-F convention that Task 4's score recount depends on.

- [ ] **Step 1: Add class F to the legend**

In the "How to read this" section, the class table currently has rows **N**, **L**, **D**. Add a fourth row after **D**:

```markdown
| **F** | File validity — the clause constrains what a conformant *file* may contain, not what the renderer paints. The standard specifies no renderer behaviour for violating input, so a renderer test would pin our choice of degradation rather than the standard's requirement. Enforcing these belongs to the validator (`PdfLibrary/Conformance/`), whose own matrix is `Docs/pdfua/matterhorn-coverage.md`. Excluded from the score, like L and D. |
```

Then, immediately after the sentence "**Score is over N rows only.** L and D rows are deliberately excluded — counting them would inflate the denominator with things that cannot be failed.", append:

```markdown
F rows are excluded for the same reason, added 2026-07-25. Moving a row to F **reassigns** it — it does
not retire it. Every F row below names who enforces it, including "validator gap" where nothing
currently does.
```

- [ ] **Step 2: Migrate the five rows**

Change the Class column from `N` to `F` and replace the Status and note for each of these five rows. Keep each row's `#` and normative-statement text exactly as it is; only Class, Status and the final note column change. Use `—` as the Status (F rows are not scored).

Row **4-1** → Class `F`, Status `—`, note:
```
File-shape constraint, not renderer behaviour. `ColorSpaceResolver` gates on `csArray.Count >= 4` and falls through for a malformed array, which is robustness rather than conformance. **Validator gap** — no rule in `PdfLibrary/Conformance/Rules/` checks Separation array shape.
```

Row **4-14** → Class `F`, Status `—`, note:
```
Constrains the file's alternateSpace, not what the renderer paints when it is violated. **Validator gap** — no rule checks this.
```

Row **5-1** → Class `F`, Status `—`, note:
```
Same constraint as 4-14, for DeviceN. **Validator gap** — no rule checks this.
```

Row **5-5** → Class `F`, Status `—`, note:
```
Constrains where `/None` may appear in a file. Previously recorded as blocked on G-4 because it needs DeviceN `/Subtype` awareness — as a validator row, that read belongs to the validator, so the dependency does not apply here. **Validator gap** — `PdfxNChannelColorantsRule` reads `/Subtype` but checks `/Colorants` presence, not `/None` placement.
```

Row **5-12** → Class `F`, Status `—`, note:
```
Requires the attributes dictionary to be present for NChannel. Partially enforced by `PdfxNChannelColorantsRule`, which requires `/Colorants` — but that rule is **profile-gated** (`AppliesToProfiles = AllPdfA | PdfX4`), so nothing enforces this at baseline ISO 32000-2.
```

- [ ] **Step 3: Verify the recount reflects the migration**

Run:

```bash
cd /c/Users/jorda/RiderProjects/PDF && python - <<'PYEOF'
import io,re
from collections import Counter
s=io.open('Docs/colour/rendering-conformance.md',encoding='utf-8',newline='').read()
rows=re.findall(r'^\| (\d-\d+) \|.*?\| ([NLDF]) \| (\S+) \|', s, re.M)
names={'✅':'tested','⚠':'untested','❌':'violation','␀':'unaudited','—':'n/a'}
for cls in 'NLDF':
    c=Counter(names.get(st[0],'?') for n,k,st in rows if k==cls)
    print(cls, 'total=%d'%sum(c.values()), dict(c))
PYEOF
```

Expected: `N total=21 {'tested': 11, 'untested': 4, 'violation': 2, 'unaudited': 4}` and `F total=5 {'n/a': 5}`.

Note the regex now includes `F`. If N is not 21, a row was missed or mis-edited.

- [ ] **Step 4: Update the score table's "Now" column**

Set the N figures to match Step 3's output (11 / 4 / 2 / 4, total 21) and add a row for the new class:

```markdown
| File validity (**F**, new) | — | 5 |
```

Leave the "Slice 1" column untouched. Do NOT edit the prose narrative yet — Task 4 does that once the audits are in, so it is rewritten once against final numbers rather than twice.

- [ ] **Step 5: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git add Docs/colour/rendering-conformance.md
git commit -m "docs(colour): add class F and migrate five file-validity rows

4-1, 4-14, 5-1, 5-5 and 5-12 constrain what a valid FILE may contain, not what
the renderer paints, and the standard specifies no renderer behaviour for
violating input. Scoring them as renderer rows would mean testing our choice of
degradation rather than the standard's requirement.

Each records who enforces it. Both relevant Conformance/ rules are profile-gated
to PDF/A and PDF/X-4, so most are honestly validator gaps at baseline.

N denominator 26 -> 21; unaudited 9 -> 4."
```

---

### Task 2: Row 4-4 — initial colour value on `cs`/`CS`

**Files:**
- Modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs`
- Modify: `PdfLibrary/Content/PdfContentProcessor.cs`
- Modify: `PdfLibrary/Rendering/PdfRenderer.cs`
- Test: `PdfLibrary.Tests/Rendering/InitialColorValueTests.cs` (create)

**Interfaces:**
- Consumes: `ColourConformancePage.Build(string colorSpaceDef, string content, bool withFont = false, string extraResources = "", params string[] extraObjects)` and `ColourConformancePage.RenderCentre(byte[] pdf)` — both existing. `ColorSpaceResolver.Deref(PdfObject?, PdfDocument?)` — existing private static helper in that file.
- Produces: `public static List<double>? ColorSpaceResolver.InitialColorFor(string? csName, PdfObject? csObj, PdfDocument? doc)`; `protected virtual void PdfContentProcessor.OnColorSpaceChanged(bool stroking)`.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Rendering/InitialColorValueTests.cs`:

```csharp
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.8, Table 73 (rows for <c>CS</c> and <c>cs</c>), matrix row 4-4: setting a colour
/// space "shall also set the current […] colour to its initial value, which depends on the colour
/// space". §8.6.6.4 states the Separation case again — "the initial value for both the stroking and
/// nonstroking colour in the graphics state shall be 1.0" — and §8.6.6.5 the DeviceN case.
///
/// <para>
/// The engine did neither. A prior attempt initialised every space to zero, which renders Separation
/// as tint 0 (lightest), and was backed out wholesale rather than corrected — leaving <c>cs</c> to
/// leave the PREVIOUS colour in place. Every test here therefore sets a contrasting colour first: if
/// the initial value is not applied, the fill paints that stale carry-over and the assertion fails.
/// Without the prior colour these tests would pass against a renderer that simply defaulted to black.
/// </para>
///
/// <para>
/// The per-space values are not uniform, which is the trap the abandoned fix fell into.
/// <b>DeviceCMYK's initial colour is [0 0 0 1], not all-zeros</b> — all-zeros in CMYK is white, and the
/// clause requires black via the K plate. Separation and DeviceN initialise to 1.0, the opposite end
/// from the device spaces, because their tints are subtractive.
/// </para>
/// </summary>
public class InitialColorValueTests
{
    /// <summary>Sets a contrasting red, then selects /Cs0 and fills WITHOUT any sc/scn operator.</summary>
    private const string RedThenSelectCs0 = "1 0 0 rg /Cs0 cs 100 400 200 200 re f";

    private static void AssertBlack(SKColor c, string what) =>
        Assert.True(c.Red < 25 && c.Green < 25 && c.Blue < 25,
            $"{what} painted RGB({c.Red},{c.Green},{c.Blue}); expected near-black. A red result means " +
            "the previous colour carried over instead of the space's initial value being applied.");

    /// <summary>
    /// Row 4-4 for Separation: initial tint 1.0. The tint transform ramps white → black, so tint 1.0
    /// is black and tint 0.0 (the value the abandoned fix produced) would be white — the two failure
    /// modes are distinguishable from each other and from the red carry-over.
    /// </summary>
    [Fact]
    public void Separation_WithoutScn_UsesInitialTintOfOne()
    {
        const string cs = "[/Separation /Spot /DeviceRGB " +
                          "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>]";

        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, RedThenSelectCs0));

        AssertBlack(c, "/Separation with no scn");
    }

    /// <summary>
    /// Row 4-4 for DeviceN: "each component shall be given an initial value of 1.0" (§8.6.6.5). The
    /// type 4 transform maps (t₁,t₂) → (0, t₁, t₂, 0), so the required initial (1,1) yields
    /// DeviceCMYK(0,1,1,0) — red.
    ///
    /// <para>
    /// The prior colour here is BLUE, not the red the other cases use, precisely because this
    /// transform's correct answer IS red: against a red backdrop the test would pass whether the
    /// initial value was applied or the previous colour carried over. Blue separates all three
    /// outcomes — carry-over paints blue, correct initialisation paints red, and a wrongly-zeroed
    /// initial paints CMYK(0,0,0,0) = white.
    /// </para>
    /// </summary>
    [Fact]
    public void DeviceN_WithoutScn_UsesInitialTintOfOnePerComponent()
    {
        // (t₁ t₂) → (0 t₁ t₂ 0): push 0, roll top 3 by 1, push 0. Same transform the /All and /None
        // suites already use, so its behaviour is established rather than newly assumed.
        const string ps = "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] /Length 16 >>\r\n" +
                          "stream\r\n{ 0 3 1 roll 0 }\r\nendstream";
        const string cs = "[/DeviceN [/SpotA /SpotB] /DeviceCMYK 5 0 R]";

        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, "0 0 1 rg /Cs0 cs 100 400 200 200 re f", withFont: false,
                extraResources: "", extraObjects: ps));

        Assert.True(c.Red > 200 && c.Green < 60 && c.Blue < 60,
            $"/DeviceN with no scn painted RGB({c.Red},{c.Green},{c.Blue}); initial tints of 1.0 map " +
            "through this transform to CMYK(0,1,1,0) = red. Blue means the previous colour carried " +
            "over; white means the initial tints were zeroed.");
    }

    /// <summary>
    /// Row 4-4 for DeviceCMYK: initial colour is <c>[0 0 0 1]</c> — black via the K plate — NOT
    /// all-zeros, which in CMYK is white. This is the case the abandoned fix would also have broken,
    /// and it is why "initialise everything to zero" is not the fix.
    /// </summary>
    [Fact]
    public void DeviceCmyk_WithoutScn_UsesBlackNotWhite()
    {
        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB",
                "1 0 0 rg /DeviceCMYK cs 100 400 200 200 re f"));

        AssertBlack(c, "/DeviceCMYK with no sc");
    }

    /// <summary>Row 4-4 for DeviceRGB: initial colour is all components 0.0, i.e. black.</summary>
    [Fact]
    public void DeviceRgb_WithoutScn_UsesBlack()
    {
        SKColor c = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB",
                "1 0 0 rg /DeviceRGB cs 100 400 200 200 re f"));

        AssertBlack(c, "/DeviceRGB with no sc");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~InitialColorValueTests" --nologo`

Expected: all four FAIL, each reporting approximately `RGB(255,0,0)` — the carried-over red.

**If any test reports something other than red, stop and diagnose before implementing.** A different colour means the fixture is not exercising carry-over and the test is not pinning what it claims. Record the actual output either way.

- [ ] **Step 3: Add `InitialColorFor` to the resolver**

In `PdfLibrary/Rendering/ColorSpaceResolver.cs`, add these two members immediately before `public static bool PaintsNothing(string? csName, …)`:

```csharp
    /// <summary>
    /// The initial colour a space takes when it becomes current via <c>cs</c>/<c>CS</c>
    /// (ISO 32000-2 §8.6.8, Table 73). Returns null when no component vector applies — Pattern, whose
    /// initial "colour" is a pattern object that paints nothing rather than a set of components, and
    /// any space that cannot be identified, where leaving the current colour alone is safer than
    /// guessing.
    ///
    /// <para>
    /// The values are NOT uniform across families, which is the trap that sank an earlier attempt at
    /// this: DeviceCMYK is <c>[0 0 0 1]</c> (black via K — all-zeros would be white), while Separation
    /// and DeviceN initialise to 1.0 because their tints are subtractive. Lab and ICCBased initialise
    /// to zero "unless that falls outside the intervals specified by the space's Range entry, in which
    /// case the nearest valid value shall be substituted".
    /// </para>
    /// </summary>
    public static List<double>? InitialColorFor(string? csName, PdfObject? csObj, PdfDocument? doc)
    {
        // The four families nameable without parameters resolve by name alone, and always identify
        // those spaces directly — they never refer to a ColorSpace resource.
        switch (csName)
        {
            case "DeviceGray": return [0.0];
            case "DeviceRGB": return [0.0, 0.0, 0.0];
            case "DeviceCMYK": return [0.0, 0.0, 0.0, 1.0];
            case "Pattern": return null;
        }

        if (csObj is null) return null;
        csObj = Deref(csObj, doc);

        if (csObj is PdfName aliasName)
            return InitialColorFor(aliasName.Value, null, doc);

        if (csObj is not PdfArray { Count: >= 1 } arr || arr[0] is not PdfName family)
            return null;

        switch (family.Value)
        {
            case "DeviceGray" or "CalGray": return [0.0];
            case "DeviceRGB" or "CalRGB": return [0.0, 0.0, 0.0];
            case "DeviceCMYK": return [0.0, 0.0, 0.0, 1.0];
            case "Indexed": return [0.0];
            case "Pattern": return null;

            case "Separation": return [1.0];

            case "DeviceN":
            {
                if (Deref(arr.Count >= 2 ? arr[1] : null, doc) is not PdfArray { Count: > 0 } names)
                    return null;
                var tints = new List<double>(names.Count);
                for (var i = 0; i < names.Count; i++) tints.Add(1.0);
                return tints;
            }

            case "Lab":
            {
                // L is always in [0,100] so 0 is valid; a and b are clamped to /Range (default
                // [-100 100 -100 100]).
                double[] range = LabRangeOrDefault(arr, doc);
                return [0.0, Clamp(0.0, range[0], range[1]), Clamp(0.0, range[2], range[3])];
            }

            case "ICCBased":
            {
                if (Deref(arr.Count >= 2 ? arr[1] : null, doc) is not PdfStream icc) return null;
                var n = 3;
                if (icc.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj)
                    && Deref(nObj, doc) is PdfInteger nInt) n = nInt.Value;
                if (n < 1) return null;

                var comps = new List<double>(n);
                PdfArray? iccRange = icc.Dictionary.TryGetValue(new PdfName("Range"), out PdfObject rObj)
                    ? Deref(rObj, doc) as PdfArray
                    : null;
                for (var i = 0; i < n; i++)
                {
                    double lo = iccRange is not null && iccRange.Count > 2 * i ? iccRange[2 * i].ToDouble() : 0.0;
                    double hi = iccRange is not null && iccRange.Count > 2 * i + 1 ? iccRange[2 * i + 1].ToDouble() : 1.0;
                    comps.Add(Clamp(0.0, lo, hi));
                }
                return comps;
            }

            default: return null;
        }
    }

    /// <summary>Lab <c>/Range</c> as [aMin aMax bMin bMax], defaulting per ISO 32000-2 Table 65.</summary>
    private static double[] LabRangeOrDefault(PdfArray labArray, PdfDocument? doc)
    {
        if (labArray.Count >= 2 && Deref(labArray[1], doc) is PdfDictionary d
            && d.TryGetValue(new PdfName("Range"), out PdfObject rObj)
            && Deref(rObj, doc) is PdfArray { Count: >= 4 } r)
            return [r[0].ToDouble(), r[1].ToDouble(), r[2].ToDouble(), r[3].ToDouble()];
        return [-100.0, 100.0, -100.0, 100.0];
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
```

- [ ] **Step 4: Add the hook to the content processor**

In `PdfLibrary/Content/PdfContentProcessor.cs`, add this next to the other virtual hooks, immediately after the line `protected virtual void OnColorChanged() { }`:

```csharp
    /// <summary>
    /// Raised after <c>cs</c>/<c>CS</c> sets a new colour space, so a subclass can apply that space's
    /// initial colour (ISO 32000-2 §8.6.8, Table 73). It is a hook rather than logic here because the
    /// initial value depends on the space's DEFINITION — a named Separation resolves to 1.0, a named
    /// ICCBased to zeros clipped to its Range — and this class has no resource dictionary to resolve
    /// names against. Default: no-op, so the non-rendering subclasses (text extraction, glyph and
    /// marked-content collection) are unaffected.
    /// </summary>
    /// <param name="stroking">True for <c>CS</c>, false for <c>cs</c>.</param>
    protected virtual void OnColorSpaceChanged(bool stroking) { }
```

Then replace both colour-space operator cases. Replace:

```csharp
            case SetStrokeColorSpaceOperator cs:
                CurrentState.StrokeColorSpace = cs.ColorSpace;
                // Note: Do NOT initialize color or call OnColorChanged() here.
                // The color will be set by a subsequent SC/SCN operator.
                // Initializing color to default values causes Separation color spaces
                // to render with tint=0 (white) until the SCN operator is processed.
                break;

            case SetFillColorSpaceOperator cs:
                PdfLogger.Log(LogCategory.Graphics, $"[cs OPERATOR] ColorSpace={cs.ColorSpace}");
                CurrentState.FillColorSpace = cs.ColorSpace;
                // Note: Do NOT initialize color or call OnColorChanged() here.
                // The color will be set by a subsequent sc/scn operator.
                // Initializing color to default values causes Separation color spaces
                // to render with tint=0 (white) until the scn operator is processed.
                break;
```

with:

```csharp
            // cs/CS "shall also set the current […] colour to its initial value, which depends on the
            // colour space" (ISO 32000-2 §8.6.8, Table 73). An earlier revision skipped this entirely
            // because initialising every space to zero renders Separation at tint 0 — the lightest
            // colour, not the required darkest. Zero is simply the wrong constant for half the
            // families (DeviceCMYK is [0 0 0 1]; Separation and DeviceN are 1.0), so the fix is
            // per-space initialisation, which OnColorSpaceChanged performs where the resources are.
            case SetStrokeColorSpaceOperator cs:
                CurrentState.StrokeColorSpace = cs.ColorSpace;
                OnColorSpaceChanged(stroking: true);
                break;

            case SetFillColorSpaceOperator cs:
                PdfLogger.Log(LogCategory.Graphics, $"[cs OPERATOR] ColorSpace={cs.ColorSpace}");
                CurrentState.FillColorSpace = cs.ColorSpace;
                OnColorSpaceChanged(stroking: false);
                break;
```

- [ ] **Step 5: Override the hook in the renderer**

In `PdfLibrary/Rendering/PdfRenderer.cs`, add immediately before `protected override void OnColorChanged()`:

```csharp
    /// <summary>
    /// Applies the newly-selected colour space's initial colour (ISO 32000-2 §8.6.8, Table 73), then
    /// re-resolves so the Resolved* fields and the colorant-origin/plate masks match it. Named spaces
    /// are looked up in the page's /ColorSpace resources, which is why this override lives here rather
    /// than in the base processor.
    /// </summary>
    protected override void OnColorSpaceChanged(bool stroking)
    {
        string? csName = stroking ? CurrentState.StrokeColorSpace : CurrentState.FillColorSpace;
        if (string.IsNullOrEmpty(csName)) return;

        PdfObject? csObj = null;
        PdfDictionary? colorSpaces = _currentResources?.GetColorSpaces();
        if (colorSpaces is not null && colorSpaces.TryGetValue(new PdfName(csName), out PdfObject? found))
            csObj = found;

        List<double>? initial = ColorSpaceResolver.InitialColorFor(csName, csObj, _document);

        // null = Pattern (whose initial colour is a pattern that paints nothing, not a component
        // vector) or an unidentifiable space. Leaving the current colour untouched is the safer of the
        // two wrong answers: it preserves today's behaviour rather than inventing components.
        if (initial is null) return;

        if (stroking) CurrentState.StrokeColor = initial;
        else CurrentState.FillColor = initial;

        OnColorChanged();
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~InitialColorValueTests" --nologo`
Expected: PASS, 4 of 4.

- [ ] **Step 7: Run the full engine suite**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo`
Expected: 2488 passed (2484 + 4), 0 failed.

**This is the step most likely to surface fallout.** `cs`/`CS` now change the current colour where they previously did not, so any existing test that relied on carry-over will fail. If tests fail: do NOT weaken the new behaviour to make them pass. Read each failure and decide whether the old expectation encoded the bug. Report every such test in your report with your reasoning — the controller needs to see them, because a test that encoded the bug is evidence about blast radius, not just an obstacle.

- [ ] **Step 8: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git add PdfLibrary/Rendering/ColorSpaceResolver.cs PdfLibrary/Content/PdfContentProcessor.cs PdfLibrary/Rendering/PdfRenderer.cs PdfLibrary.Tests/Rendering/InitialColorValueTests.cs
git commit -m "fix(colour): apply each colour space's initial colour on cs/CS (row 4-4)

ISO 32000-2 §8.6.8 Table 73: cs/CS 'shall also set the current colour to its
initial value, which depends on the colour space'. The engine did not, and the
code said why: an earlier attempt initialised everything to zero, which renders
Separation at tint 0 — the LIGHTEST colour, where the clause requires 1.0, the
darkest — and was backed out wholesale instead of corrected. cs/CS were left
leaving the previous colour in place, so a space selected without a following
sc/scn painted stale carry-over.

Zero was simply the wrong constant for half the families. DeviceCMYK's initial
colour is [0 0 0 1] — black via K, since all-zeros in CMYK is white — so the
abandoned fix would have broken that too; only the Separation symptom was seen.
Separation and DeviceN initialise to 1.0 because their tints are subtractive;
Lab and ICCBased to zero clipped to their Range.

PdfContentProcessor gains an OnColorSpaceChanged hook rather than doing this
itself: the initial value depends on the space's definition, and named spaces
need the resource dictionary, which only PdfRenderer has. Default no-op leaves
the text-extraction and collector subclasses unaffected.

Tests set a contrasting colour first, so carry-over is distinguishable from
both the correct answer and from the zeroed one."
```

---

### Task 3: Audit rows 5-8 and 4-2

**Files:**
- Test: `PdfLibrary.Tests/Rendering/DeviceNNoneReversionTests.cs` (create)
- Test: `PdfLibrary.Tests/Rendering/SeparationTintRangeTests.cs` (create)
- Possibly modify: `PdfLibrary/Rendering/ColorSpaceResolver.cs` (only if the audit finds a violation)

**Interfaces:** Consumes `ColourConformancePage` as in Task 2. Produces nothing consumed later.

- [ ] **Step 1: Write the 5-8 test**

Expected outcome: **PASS on first run.** `ResolveDeviceN` already calls `tintTransform.Evaluate(color.ToArray())` — every component, unfiltered. Because this pins already-correct behaviour, Step 2's mutation is what makes it count.

Create `PdfLibrary.Tests/Rendering/DeviceNNoneReversionTests.cs`:

```csharp
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.5, matrix row 5-8: "when the DeviceN colour space reverts to its alternate
/// colour space, those components shall be passed to the tint transformation function" — where
/// "those" are the components naming <c>/None</c>.
///
/// <para>
/// This is the clause most easily got backwards, because its neighbour 5-7 requires the opposite:
/// when painting named device colourants DIRECTLY, <c>/None</c> components are discarded. Discarded
/// when painting direct, passed through on reversion. An implementation that filtered <c>/None</c>
/// out at the colour-space level would satisfy 5-7 and violate this row, and the two are one
/// paragraph apart in the specification.
/// </para>
///
/// <para>
/// The transform is chosen so the <c>/None</c> component's tint MATTERS to the output: it maps
/// (t₁,t₂) → (0, t₁, t₂, 0), so t₂ — the <c>/None</c> component — lands on the yellow plate. A
/// transform that ignored its second input would pass whether or not the component was passed, which
/// is the vacuity trap.
/// </para>
/// </summary>
public class DeviceNNoneReversionTests
{
    [Fact]
    public void DeviceN_Reversion_PassesNoneComponentsToTheTintTransform()
    {
        // (t₁ t₂) → (0 t₁ t₂ 0): push 0, roll top 3 by 1, push 0.
        const string ps = "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1] /Length 16 >>\r\n" +
                          "stream\r\n{ 0 3 1 roll 0 }\r\nendstream";
        const string cs = "[/DeviceN [/SpotA /None] /DeviceCMYK 5 0 R]";

        SKColor viaDeviceN = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build(cs, ColourConformancePage.FillRect("/Cs0 cs 0.25 0.75 scn"),
                withFont: false, extraResources: "", extraObjects: ps));

        // The oracle: the same colour painted directly in the alternate space. If the /None component
        // (0.75) reached the transform, the yellow plate is 0.75 and the two must be identical.
        SKColor direct = ColourConformancePage.RenderCentre(
            ColourConformancePage.Build("/DeviceRGB", ColourConformancePage.FillRect("0 0.25 0.75 0 k")));

        Assert.True(viaDeviceN == direct,
            $"DeviceN reversion painted RGB({viaDeviceN.Red},{viaDeviceN.Green},{viaDeviceN.Blue}) but " +
            $"the alternate space painted directly gives RGB({direct.Red},{direct.Green},{direct.Blue}). " +
            "§8.6.6.5 requires /None components to be passed to the tint transform on reversion.");
    }
}
```

- [ ] **Step 2: Run it, then mutation-check it**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~DeviceNNoneReversionTests" --nologo`
Expected: PASS.

Now prove it is not vacuous. In `ColorSpaceResolver.ResolveDeviceN`, temporarily replace:

```csharp
        double[] result = tintTransform.Evaluate(color.ToArray());
```

with a version that filters out the components whose colourant name is `/None` — the violation this row guards against:

```csharp
        // TEMPORARY MUTATION — filters /None components out before the transform.
        var kept = new List<double>();
        if (Deref(csArray[1], document) is PdfArray mutNames)
            for (var i = 0; i < mutNames.Count && i < color.Count; i++)
                if (Deref(mutNames[i], document) is not PdfName { Value: "None" }) kept.Add(color[i]);
        double[] result = tintTransform.Evaluate(kept.ToArray());
```

Re-run the test. Expected: **FAIL** — either a colour mismatch or a PostScript stack error from the transform receiving one input instead of two. Record the output.

**Then revert the mutation** and re-run to confirm PASS. Do not commit the mutation.

- [ ] **Step 3: Write the 4-2 test**

Create `PdfLibrary.Tests/Rendering/SeparationTintRangeTests.cs`:

```csharp
using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.4, matrix row 4-2: "A colour value in a Separation colour space shall consist of
/// a single tint component in the range 0.0 to 1.0."
///
/// <para>
/// The component-count half is already enforced — <c>ResolveSeparation</c> acts only when
/// <c>color.Count == 1</c>. This pins the range half: a tint outside [0,1] must behave as the nearest
/// valid tint rather than extrapolating the transform beyond its domain, which would produce colours
/// no valid file could request.
/// </para>
/// </summary>
public class SeparationTintRangeTests
{
    private const string Cs = "[/Separation /Spot /DeviceCMYK " +
                              "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 0 0] /N 1 >>]";

    private static SKColor AtTint(string tint) => ColourConformancePage.RenderCentre(
        ColourConformancePage.Build(Cs, ColourConformancePage.FillRect($"/Cs0 cs {tint} scn")));

    [Fact]
    public void TintAboveOne_ClampsToOne()
    {
        Assert.True(AtTint("1.5") == AtTint("1"),
            "A tint above 1.0 must behave as 1.0 (§8.6.6.4 bounds the component to [0.0, 1.0])");
    }

    [Fact]
    public void TintBelowZero_ClampsToZero()
    {
        Assert.True(AtTint("-0.5") == AtTint("0"),
            "A tint below 0.0 must behave as 0.0 (§8.6.6.4 bounds the component to [0.0, 1.0])");
    }
}
```

- [ ] **Step 4: Run the 4-2 tests and record the outcome**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SeparationTintRangeTests" --nologo`

Two outcomes, both valid results for an audit:

- **PASS** — the function's `/Domain` already clips, as expected. Mutation-check by temporarily widening the space's `/Domain` to `[-1 2]` in the test's `Cs` constant; if the tests still pass, clipping happens elsewhere and the tests are pinning it; if they fail, `/Domain` was doing the work and the row is conformant *because of* the function machinery. Record which, revert, and note it in the row.
- **FAIL** — row 4-2 is a violation. Apply the scope guard: a clamp in `ResolveSeparation` before evaluating is a small fix comparable to 4-4's and may be made; anything larger gets recorded as a gap instead. Either way, write it up.

- [ ] **Step 5: Run the full engine suite**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo`
Expected: 2491 passed (2488 + 3), 0 failed — or 2488 + however many of the 4-2 tests you kept if the audit changed the shape.

- [ ] **Step 6: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git add PdfLibrary.Tests/Rendering/ PdfLibrary/Rendering/ColorSpaceResolver.cs
git commit -m "test(colour): audit rows 5-8 and 4-2

5-8 (/None components passed to the tint transform on reversion) was already
conformant — ResolveDeviceN evaluates over every component unfiltered. Pinned,
and mutation-checked by filtering /None out, which fails as it should.

The transform deliberately maps the /None component's tint onto a visible
plate: a transform ignoring its second input would pass whether or not the
component was passed, which is how a test like this goes vacuous.

4-2 (tint bounded to [0,1]) audited; see the row for the finding."
```

---

### Task 4: Row 5-13, final matrix update, and verification

**Files:**
- Modify: `Docs/colour/rendering-conformance.md`
- Modify: `C:\Users\jorda\RiderProjects\Pellucid\Directory.Build.props.local` (repin; gitignored, do not commit)
- Modify: `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj` (repin)

**Interfaces:** Consumes Tasks 1–3. Produces nothing.

- [ ] **Step 1: Decide row 5-13**

Read the clause in context first:

```
mcp__pdf-rag__hybrid_search, query: "DeviceN attributes MixingHints Process components ignore ICC profile"
```

Row 5-13 says applications "shall ignore these process component entries if they can obtain the information from an ICC profile". The render path never consumes mixing hints at all. Two honest readings:

- **✅** — the requirement is "do not prefer mixing hints over an ICC profile", and never reading them satisfies it unconditionally. If you choose this, the row must still name what makes it true and cite that nothing on the render path reads `/MixingHints`; do NOT write a test that cannot fail.
- **Class F or L** — the clause presumes a processor that consumes them, making it inapplicable rather than satisfied.

Choose one, and record the reasoning in the row's note. Prefer reclassification over an unfalsifiable ✅: a row that cannot fail should not be scored.

- [ ] **Step 2: Update rows 4-2, 4-4, 5-8, 5-13**

Set each row's Status from the audit results, with a note that cites the test by name (or, for 4-2/5-13 if they resolved to a gap or reclassification, the reasoning). Row 4-4's note must state that it was a confirmed violation, name `InitialColorValueTests`, and record the DeviceCMYK `[0 0 0 1]` detail — it is the part most likely to be re-broken by a future "simplification".

- [ ] **Step 3: Recount and update the score table**

Run the Task 1 Step 3 script again. Update the "Now" column to match its output exactly. Expected shape: `N total=21` (or 20 if 5-13 moved to F) with `unaudited: 0`.

If the script disagrees with what you were about to write, the script is right — the matrix's whole point is that its denominator recomputes from its rows.

- [ ] **Step 4: Rewrite the score narrative**

The prose under the score table still describes the first ratchet pass. Rewrite it to state: what this sweep did (␀ → 0), that class F now separates renderer rows from file-validity rows, that 4-4 was a confirmed violation found by auditing, and what remains non-✅ — the four ⚠️ CMYK-path rows and G-4's two ❌. Every count in the prose must match the table.

- [ ] **Step 5: Add gaps found during the sweep**

Add a gap entry for the **Pattern initial colour** noted during the 4-4 clause read: §8.6.8 says a Pattern space's initial colour "shall be a pattern object that causes nothing to be painted", which `InitialColorFor` returns null for (leaving the current colour untouched) rather than implementing. This is a distinct concept from the `/None` `PaintsNothing` signal and conflating them without an audit is exactly what this matrix exists to prevent. Number it after the highest existing gap.

Add any further gaps the Task 3 audits produced.

- [ ] **Step 6: Repack, repin both consumers, and run the corpus gate**

```powershell
& "C:\Users\jorda\RiderProjects\PDF\pack-local.ps1"
```

Note the printed `2.5.1-dev<timestamp>`, then:

1. Re-add the Skia pin the packer deletes — `Pellucid/Directory.Build.props.local` must end with:
```xml
    <LxmanPdfLibraryRenderingSkiaVersion>0.1.1-dev20260717153208</LxmanPdfLibraryRenderingSkiaVersion>
```
2. Set `C:\Users\jorda\PDFs\PdfCompare\PdfCompare.csproj`'s `Lxman.PdfLibrary` PackageReference to the same new version.
3. Confirm the pin took (a stale package still builds cleanly, so this check is not optional):

Run: `cd /c/Users/jorda/RiderProjects/Pellucid && dotnet build -c Debug --nologo 2>&1 | tail -3 && grep -o "Lxman.PdfLibrary/2\.5\.1[^\"]*" Pellucid.Core/obj/project.assets.json | sort -u`

4. Pellucid suites: `cd /c/Users/jorda/RiderProjects/Pellucid && dotnet test --nologo` — expect 1268 pass / 0 fail.
5. Corpus gate: `cd /c/Users/jorda/RiderProjects/Pellucid && dotnet test Pellucid.Rendering.Avalonia.Tests/Pellucid.Rendering.Avalonia.Tests.csproj --filter "FullyQualifiedName~GwgFidelityTests|FullyQualifiedName~GwgX4RenderScoreboardTests|FullyQualifiedName~GwgX4FidelityScoreboardTests" --nologo`

**The gate is the real test of Task 2.** `cs`/`CS` now change the current colour on every content stream in every fixture. The baseline contains 6 legitimately-stale entries from earlier accepted deltas — those are not regressions. **If any fixture moves, STOP and report the delta** with the fixture names and direction. Do not re-baseline to make the gate green: a moved fixture is either a genuine improvement worth recording or a regression worth fixing, and both are the human's call.

- [ ] **Step 7: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git add Docs/colour/rendering-conformance.md
git commit -m "docs(colour): ␀ sweep complete — no unaudited rows remain

Rows 4-2, 4-4, 5-8 and 5-13 audited and recorded; class F carries the five
file-validity rows. The renderer matrix now has zero unaudited rows: what is
not ✅ is either a known-untested CMYK-path row or G-4's NChannel violation.

4-4 was a confirmed violation, found by auditing rather than by a bug report."
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| Part 1 — class F, five rows migrated | Task 1, Steps 1–2 |
| Part 1 — each F row names its enforcer, incl. "validator gap" | Task 1, Step 2 |
| Part 2 — 4-4 confirmed violation, per-space table, DeviceCMYK `[0 0 0 1]` | Task 2, Steps 1, 3 |
| Part 2 — 5-8 expected conformant, needs a test whose /None tint matters | Task 3, Steps 1–2 |
| Part 2 — 4-2 range audit | Task 3, Steps 3–4 |
| Part 2 — 5-13 decision with criteria, no unfalsifiable ✅ | Task 4, Step 1 |
| Pattern initial colour recorded as a gap, not implemented | Task 4, Step 5 |
| Expected outcome / score recount from the rows | Task 1 Step 3, Task 4 Step 3 |
| Testing standard (clause-citing, observed-to-fail, contrast) | Global Constraints; Task 2 Step 2, Task 3 Step 2 |
| Verification: repack, repin BOTH consumers, corpus gate | Task 4, Step 6 |
| Scope guard | Global Constraints; Task 3 Step 4 |
| Out of scope: G-4, G-7/8/9/10, ⚠️ CMYK rows | No task touches them |

**Placeholder scan:** No TBD/TODO. Three steps are deliberately outcome-branched rather than pre-decided — Task 3 Step 4 (4-2 may pass or fail), Task 4 Step 1 (5-13's classification) — and each states the decision criteria and what to do in either branch, which is what an audit task requires. The pack timestamp in Task 4 Step 6 is unknowable until the packer runs and the step says to read it from the output.

**Type consistency:** `InitialColorFor(string?, PdfObject?, PdfDocument?)` is defined in Task 2 Step 3 and called with that signature in Task 2 Step 5. `OnColorSpaceChanged(bool stroking)` is declared in Task 2 Step 4 and overridden with the same signature in Step 5. `Deref` and `LabRangeOrDefault` are used only inside `ColorSpaceResolver`, where `Deref` already exists and `LabRangeOrDefault` is added alongside. `ColourConformancePage.Build`'s `extraResources`/`extraObjects` are always passed by name, per the `<remarks>` warning on that method.

**Prior-colour choice is per-test, not boilerplate.** Three of the four Task 2 cases set red first because their correct answer is black or white; the DeviceN case sets **blue**, because its correct answer through that transform is red — against a red backdrop it would pass whether or not the fix worked. This is the same vacuity that slipped through an earlier slice (tints whose alternate-space colour equalled the backdrop), so the rule is explicit: the prior colour must differ from both the correct answer and from the plausible wrong answers.

**One risk the plan cannot remove:** Task 2 Step 7 may surface existing tests that encoded the old carry-over behaviour. The plan cannot enumerate them in advance, so the step instructs the implementer to report each with reasoning rather than silently adapting either side. That reporting is deliberate — which tests encoded the bug is evidence about blast radius, and it is the controller's call, not the implementer's.
