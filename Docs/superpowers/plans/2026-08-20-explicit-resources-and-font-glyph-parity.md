# Explicit resources + font glyph parity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the last 8 PDF/A-2b whole-file parity misses, taking verdict agreement to 986/986 on all four profiles.

**Architecture:** One new conformance rule for clause 6.2.2 test 2 (a content stream must reference only resources present in its *directly associated* `/Resources`), three narrowly-scoped fixes in the existing font-program rule and its encoding/collector inputs, and four corrections to the parity tooling that mis-ranks and under-protects the work.

**Tech Stack:** C# / .NET 10, xUnit v3, `PdfLibrary.Conformance` rule framework, veraPDF corpus as the oracle.

**Spec:** `Docs/superpowers/specs/2026-08-20-explicit-resources-and-font-glyph-parity-design.md`

## Global Constraints

- **Zero false positives across all 1316 corpus files. Non-negotiable.** `ParityOracleTests.No_false_positives_vs_veraPDF` is the gate. If a task introduces one, revert that task — do not trade coverage for it.
- **Run the parity gate after EVERY task.** It costs 3 seconds and covers 1316 files — a stronger FP check than the unit suite.
- **Do NOT run the full `PdfLibrary.Tests` suite per task.** It costs ~8 min. One full run per branch, before the whole-branch review (Task 9).
- **Do NOT run `tools/gate.sh` per task.** ~40 min. Once per batch, at the end.
- `PARITY_REPORT` **must be an absolute path** — `dotnet test` runs with cwd set to the test output folder.
- `VERAPDF_CORPUS` must point at `C:\Users\jorda\RiderProjects\veraPDF-corpus`.
- Never regenerate the oi-corpus with `PELLUCID_OI_CORPUS_REGEN=1` — it wipes decomposition history.
- After writing any file containing a `\uXXXX` escape, **check the bytes** — the write tool has landed it as a raw NUL three times, which makes the file binary to git and hides its diff from review.
- `Finding` has **no test-number field**. Test numbers live in doc comments and messages only.
- If anything in this plan contradicts the code you find, **STOP and ASK.** Do not improvise. The previous session's handoff was wrong about three of four font mechanisms; plan text is the most common defect source in this project.

**Parity gate command (PowerShell):**

```powershell
$env:VERAPDF_CORPUS = "C:\Users\jorda\RiderProjects\veraPDF-corpus"
$env:PARITY_REPORT  = "$PWD\PdfLibrary.Tests\Conformance\parity\PARITY-REPORT.md"
dotnet test PdfLibrary.Tests --filter 'Category=Parity' -c Release
```

Expected at start: **5 passed**, PDF/A-2b agreement 978/986.

---

### Task 1: Lock the stale ratchet

`AgreementFloor[PdfA2b]` is 972 while actual agreement is 978 — the two most recent landings never raised it, so six points of gain are unprotected and a regression would pass CI silently. Lock it before any new code lands.

**Files:**
- Modify: `PdfLibrary.Tests/Conformance/ParityReportTests.cs:30`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. Test-only ratchet change.

- [ ] **Step 1: Confirm the current measured agreement**

Run the parity gate (command in Global Constraints). Open `PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md` and confirm the PDF/A-2b row reads `978`. If it does not, STOP and report the actual number.

- [ ] **Step 2: Raise the floor**

In `ParityReportTests.cs`, change line 30 from `[ConformanceProfile.PdfA2b] = 972,` to `978`, and add this comment immediately above the existing `// +1 (971->972)` comment block:

```csharp
            // +6 (972->978), 2026-08-20: catching up a ratchet the two prior landings left behind --
            // byte fidelity (6.1.6 t1/t2 + 6.1.13 t1, +4) and CMap max-CID (6.1.13 t10, +2) both raised
            // measured agreement without raising this floor, leaving those gains unprotected. No new
            // detection here; this only locks in what already shipped.
            [ConformanceProfile.PdfA2b] = 978,
```

(Replace the old `[ConformanceProfile.PdfA2b] = 972,` line entirely — do not leave two entries for the same key, which will not compile.)

- [ ] **Step 3: Run the parity gate**

Expected: PASS, 5 tests.

- [ ] **Step 4: Commit**

```bash
git add PdfLibrary.Tests/Conformance/ParityReportTests.cs
git commit -m "test: raise the A-2b agreement floor 972->978 to lock landed gains"
```

---

### Task 2: Correct ParityLeverage's verdict semantics

`FlipsAlone` counts only misses where a clause is the *sole* missed clause. But a miss means we flagged **nothing** (`!VeraCompliant && PdfLibraryConforms`), so flagging any one of that file's clauses flips the verdict. The current model measures clause-level parity, not verdict movement, and it under-ranks every clause that co-occurs.

Corpus proof: `6-2-11-8-t01-fail-d` is flagged by veraPDF on both 6.2.11.5 and 6.2.11.8, we flag only 6.2.11.8, and it is **not** a miss.

**Files:**
- Modify: `PdfLibrary.Tests/Conformance/ParityLeverage.cs`
- Modify: `PdfLibrary.Tests/Conformance/ParityReport.cs:123-150`
- Test: `PdfLibrary.Tests/Conformance/ParityLeverageTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ParityLeverage.ClauseLeverage(string Clause, int AppearsInMisses, int FlipsAlone, IReadOnlyList<string> MinimumPayingSet, int MinimumPayingSetFlips)` — record shape is UNCHANGED so `ParityReport` keeps compiling; only the computed values and prose change.

- [ ] **Step 1: Write the failing test**

Add to `ParityLeverageTests.cs`:

```csharp
    [Fact]
    public void Any_single_missed_clause_flips_a_miss()
    {
        // A miss means PdfLibrary flagged NOTHING, so flagging any ONE of the file's clauses
        // makes it non-conforming and the verdict agrees. Co-occurrence does not require
        // closing every clause.
        var files = new[]
        {
            new ParityComparison.FileComparison(
                "two-clause-miss.pdf", ConformanceProfile.PdfA2b,
                VeraCompliant: false, PdfLibraryConforms: true,
                VeraClauses: ["6.2.11.5", "6.2.11.8"], PdfLibraryClauses: []),
        };

        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(files);

        ParityLeverage.ClauseLeverage five = analysis.Clauses.Single(c => c.Clause == "6.2.11.5");
        Assert.Equal(1, five.AppearsInMisses);
        Assert.Equal(1, five.FlipsAlone);
        Assert.Equal(["6.2.11.5"], five.MinimumPayingSet);
        Assert.Equal(1, five.MinimumPayingSetFlips);
    }
```

**Before writing this**, open `ParityComparison.cs:22-31` and match the `FileComparison` constructor's real parameter names and order. If they differ from the above, use the real ones — do not change `ParityComparison`.

- [ ] **Step 2: Run it to verify it fails**

```powershell
dotnet test PdfLibrary.Tests --filter 'FullyQualifiedName~ParityLeverageTests' -c Release
```

Expected: FAIL — `FlipsAlone` is 0 and `MinimumPayingSet` is `["6.2.11.5","6.2.11.8"]`.

- [ ] **Step 3: Fix the computation**

In `ParityLeverage.Analyse`, replace the `flipsAlone` and `minimum` computation (currently lines ~57-67) with:

```csharp
            // A miss is !VeraCompliant && PdfLibraryConforms — PdfLibrary flagged NOTHING on that
            // file. Flagging ANY ONE of its clauses makes the file non-conforming, so the verdict
            // agrees. Every clause in a blocking set therefore flips every miss it appears in, and
            // the cheapest paying set is always the clause itself. (This is verdict leverage; clause
            // -level parity is a different, stricter goal measured by the coverage table.)
            int flipsAlone = appears;
            IReadOnlyList<string> minimum = [clause];
            int minimumFlips = appears;
```

Delete the now-unused `blockingSets` local (declared at line ~51) — leaving it produces an unused-variable warning and this build runs at zero warnings.

- [ ] **Step 4: Rewrite the class doc**

Replace the `<summary>` block on `ParityLeverage` (lines 7-16) with:

```csharp
/// <summary>
/// Verdict leverage over the whole-file misses: how many verdicts each clause would move.
///
/// A miss is a VERDICT disagreement — veraPDF rejects the file and PdfLibrary conforms, which means
/// PdfLibrary emitted no error finding at all. Flagging ANY ONE of that file's clauses therefore
/// flips it. Clause coverage is a different and stricter measure: matching veraPDF on every clause
/// it flags. Plan verdict work from this analysis and coverage work from the coverage table; do not
/// read one as the other.
///
/// Corrected 2026-08-20. The previous model counted a clause as flipping a miss only when it was the
/// SOLE missed clause, which reported zero leverage for the whole PDF/A-2b font cluster and read as
/// "partial closure moves nothing". The corpus disproves it directly: 6-2-11-8-t01-fail-d is flagged
/// by veraPDF on both 6.2.11.5 and 6.2.11.8, PdfLibrary flags only 6.2.11.8, and the file is not a miss.
/// </summary>
```

Also update the XML doc on `ClauseLeverage`'s parameters: `FlipsAlone` is "Misses this clause closes by itself — equal to AppearsInMisses, since any one clause flips a miss"; `MinimumPayingSet` is "Always the clause itself; retained for report-format stability".

- [ ] **Step 5: Update the report prose**

In `ParityReport.cs`, replace the paragraph above the leverage table (the text beginning "**Plan from this section, not from the clause-coverage ranking below.**") with:

```csharp
        sb.AppendLine(
            "**Plan verdict work from this section and coverage work from the table below.** A miss is a "
            + "VERDICT disagreement: veraPDF rejects the file and PdfLibrary conforms, having flagged "
            + "nothing. Closing ANY ONE of a miss's clauses flips it, so a clause's leverage is simply the "
            + "number of misses it appears in. Matching veraPDF on every clause it flags is a separate, "
            + "stricter goal — that is what the clause-coverage table measures.");
```

Then simplify the table header at line 143 and the row rendering at lines 147-149, since the "cheapest set" columns are now always trivial:

```csharp
        sb.AppendLine("| Clause | Misses it blocks | Flips alone |");
        sb.AppendLine("|---|--:|--:|");
        foreach (ParityLeverage.ClauseLeverage c in analysis.Clauses)
            sb.AppendLine($"| {c.Clause} | {c.AppearsInMisses} | {c.FlipsAlone} |");
```

Also fix the method's own doc comment (line ~123-126), which repeats the superseded "sole-cause" framing.

- [ ] **Step 6: Run the leverage tests, then the parity gate**

```powershell
dotnet test PdfLibrary.Tests --filter 'FullyQualifiedName~ParityLeverageTests' -c Release
```
Expected: PASS. Some existing tests in that file may encode the old semantics — if one fails, read it: if it asserts the old under-counting, update it and note the change in the commit body; if it asserts something else, STOP and ASK.

Then run the parity gate. Expected: PASS. `PARITY-REPORT.md` will be rewritten with the new leverage numbers — 6.2.2 → 3, 6.2.11.5 → 5, 6.2.11.4.1 → 3, 6.2.11.8 → 3.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary.Tests/Conformance/ParityLeverage.cs PdfLibrary.Tests/Conformance/ParityReport.cs PdfLibrary.Tests/Conformance/ParityLeverageTests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "test: correct ParityLeverage — any one clause flips a miss"
```

---

### Task 3: ExplicitResourcesRule — page scope

Closes `6-2-2-t04-fail-f` (page has no `/Resources`; `/X0` resolves only from an ancestor `/Pages` node).

**Files:**
- Create: `PdfLibrary/Conformance/Rules/ExplicitResourcesRule.cs`
- Modify: `PdfLibrary/Conformance/Preflighter.cs` (register, next to `ContentStreamOperatorRule` at ~line 62)
- Test: `PdfLibrary.Tests/Conformance/ExplicitResourcesRuleTests.cs`

**Interfaces:**
- Consumes: `ContentWalk`'s primitives — `PdfContentParser.Parse(byte[])`, `context.PageContentOperators(page)`, `context.Resolve(...)`, `context.ResolveName(...)`, `PdfResources`, `InvokeXObjectOperator.XObjectName`, `PdfOperator.Name` / `.Operands`.
- Produces: `internal sealed class ExplicitResourcesRule : IConformanceRule` with `RuleId => "explicit-resources"`. Tasks 4 and 5 extend its private walk.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Conformance/ExplicitResourcesRuleTests.cs`. Model the document builder on `ContentStreamOperatorRuleTests.cs:19-60` — read that file first and match its helper style.

```csharp
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

public class ExplicitResourcesRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static byte[] Ops(string s) => Encoding.ASCII.GetBytes(s);

    private static Finding[] Findings(PdfDocument doc) =>
        new ExplicitResourcesRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();

    /// <summary>A one-page doc whose page /Resources and ancestor /Pages /Resources are set separately.</summary>
    private static PdfDocument Doc(string pageContent, PdfDictionary? pageResources, PdfDictionary? pagesResources)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops(pageContent)));

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
        };
        if (pageResources is not null) page[N("Resources")] = pageResources;
        doc.AddObject(3, 0, page);

        var pages = new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        };
        if (pagesResources is not null) pages[N("Resources")] = pagesResources;
        doc.AddObject(2, 0, pages);

        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfDictionary XObjectResources(string name, int objNum) =>
        new() { [N("XObject")] = new PdfDictionary { [N(name)] = Ref(objNum) } };

    [Fact]
    public void An_xobject_inherited_from_an_ancestor_pages_node_is_flagged()
    {
        Finding f = Assert.Single(Findings(Doc("/X0 Do\n", null, XObjectResources("X0", 10))));
        Assert.Equal("explicit-resources", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.2.2"), f.Clause);
        Assert.Contains("X0", f.Message);
        Assert.Equal(0, f.PageIndex);
    }

    [Fact]
    public void An_xobject_in_the_pages_own_resources_is_not_flagged()
    {
        Assert.Empty(Findings(Doc("/X0 Do\n", XObjectResources("X0", 10), null)));
    }

    [Fact]
    public void A_name_absent_everywhere_is_not_flagged()
    {
        // Not "inherited" — it resolves nowhere. That is a different defect; veraPDF's property is
        // inheritedResourceNames, so staying silent here is both faithful and the lower-FP choice.
        Assert.Empty(Findings(Doc("/X0 Do\n", null, null)));
    }

    [Fact]
    public void Device_colour_operators_are_not_resource_references()
    {
        // The fail-e/pass-b fixture pair turns on exactly this: identical structure, and the only
        // difference is whether the stream NAMES a resource.
        Assert.Empty(Findings(Doc("1 1 1 rg\n0 0 10 10 re f\n", null, XObjectResources("X0", 10))));
    }

    [Fact]
    public void A_device_colour_space_name_is_not_a_resource_reference()
    {
        var pagesRes = new PdfDictionary
        {
            [N("ColorSpace")] = new PdfDictionary { [N("DeviceRGB")] = Ref(11) },
        };
        Assert.Empty(Findings(Doc("/DeviceRGB cs\n", null, pagesRes)));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```powershell
dotnet test PdfLibrary.Tests --filter 'FullyQualifiedName~ExplicitResourcesRuleTests' -c Release
```

Expected: FAIL to compile — `ExplicitResourcesRule` does not exist.

- [ ] **Step 3: Write the rule**

Create `PdfLibrary/Conformance/Rules/ExplicitResourcesRule.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// ISO 19005-2 6.2.2 test 2 — "A content stream that references other objects, such as images and
/// fonts that are necessary to fully render or process the stream, shall have an explicitly
/// associated Resources dictionary" (ISO 32000-1:2008, 7.8.3).
///
/// <para>The predicate is veraPDF's <c>inheritedResourceNames == ''</c>: a name the stream references
/// that is absent from the /Resources dictionary DIRECTLY associated with that stream, yet resolvable
/// through the fallback a consumer would use. Requiring resolvability is deliberate — a name that
/// resolves nowhere is a different defect, and staying silent on it is the lower-false-positive
/// reading as well as the faithful one.</para>
///
/// <para>Device colour operators (<c>rg</c>/<c>g</c>/<c>k</c>) and the device colour space names are
/// NOT resource references. The corpus pins this precisely: 6-2-2-t04-fail-e and -pass-b are the same
/// structure — a Form XObject with no /Resources — and differ only in whether the stream names a
/// resource.</para>
///
/// <para>Deferred, deliberately: /Properties (BDC/DP) and inline-image /CS colour spaces. No corpus
/// fixture needs either and both are pure false-positive surface.</para>
/// </summary>
internal sealed class ExplicitResourcesRule : IConformanceRule
{
    private const int MaxDepth = 24;

    public string RuleId => "explicit-resources";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    /// <summary>Colour space names that name a device space rather than a /ColorSpace resource.</summary>
    private static readonly HashSet<string> DeviceColourSpaces =
        new(System.StringComparer.Ordinal) { "DeviceGray", "DeviceRGB", "DeviceCMYK", "Pattern" };

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var pageIndex = 0;
        foreach (PdfPage page in context.Pages)
        {
            PdfResources? direct = ResourcesOf(context, page.Dictionary);
            PdfResources? inherited = InheritedResources(context, page.Dictionary);

            IReadOnlyList<PdfOperator> ops;
            try { ops = context.PageContentOperators(page); }
            catch { pageIndex++; continue; }

            List<string> offenders = Offenders(context, ops, direct, inherited);
            if (offenders.Count > 0)
                yield return Make(context, pageIndex, page.Dictionary, offenders);

            pageIndex++;
        }
    }

    /// <summary>The /Resources dictionary DIRECTLY associated with a node — never inherited.</summary>
    private static PdfResources? ResourcesOf(ConformanceContext context, PdfDictionary? node) =>
        node is not null && context.Resolve(node.Get("Resources")) is PdfDictionary dict
            ? new PdfResources(dict, context.Document)
            : null;

    /// <summary>
    /// The nearest /Resources strictly ABOVE a page, up its full /Parent chain. Mirrors
    /// ReferencedFontWalker.EffectiveResources (PdfPage.GetResources() inherits only one level, and
    /// reads an injected parent node rather than the /Parent key, so it is unusable here).
    /// Cycle-guarded.
    /// </summary>
    private static PdfResources? InheritedResources(ConformanceContext context, PdfDictionary page)
    {
        var seen = new HashSet<int>();
        PdfDictionary? node = context.Resolve(page.Get("Parent")) as PdfDictionary;
        while (node is not null)
        {
            if (node.IsIndirect && !seen.Add(node.ObjectNumber))
                break;
            if (context.Resolve(node.Get("Resources")) is PdfDictionary dict)
                return new PdfResources(dict, context.Document);
            node = context.Resolve(node.Get("Parent")) as PdfDictionary;
        }
        return null;
    }

    /// <summary>Names referenced by these operators that are absent from <paramref name="direct"/>
    /// but present in <paramref name="inherited"/>, in first-seen order.</summary>
    private static List<string> Offenders(
        ConformanceContext context, IReadOnlyList<PdfOperator> ops,
        PdfResources? direct, PdfResources? inherited)
    {
        var offenders = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (PdfOperator op in ops)
        {
            if (ResourceReference(op) is not { } reference)
                continue;
            (string category, string name) = reference;
            if (Contains(context, direct, category, name))
                continue;
            if (!Contains(context, inherited, category, name))
                continue; // resolves nowhere — not an INHERITED name
            if (seen.Add($"{category}/{name}"))
                offenders.Add(name);
        }

        return offenders;
    }

    /// <summary>The (category, name) a resource-referencing operator names, or null.</summary>
    private static (string Category, string Name)? ResourceReference(PdfOperator op)
    {
        switch (op.Name)
        {
            case "Tf" when NameOperand(op, 0) is { } font:
                return ("Font", font);
            case "Do" when NameOperand(op, 0) is { } xobject:
                return ("XObject", xobject);
            case "sh" when NameOperand(op, 0) is { } shading:
                return ("Shading", shading);
            case "gs" when NameOperand(op, 0) is { } gstate:
                return ("ExtGState", gstate);
            case "cs" or "CS" when NameOperand(op, 0) is { } space && !DeviceColourSpaces.Contains(space):
                return ("ColorSpace", space);
            // scn/SCN name a Pattern only through a trailing name operand; the numeric forms are
            // colour components in the current space and reference nothing.
            case "scn" or "SCN" when TrailingNameOperand(op) is { } pattern:
                return ("Pattern", pattern);
            default:
                return null;
        }
    }

    private static string? NameOperand(PdfOperator op, int index) =>
        op.Operands.Count > index && op.Operands[index] is PdfName name ? name.Value : null;

    private static string? TrailingNameOperand(PdfOperator op) =>
        op.Operands.Count > 0 && op.Operands[^1] is PdfName name ? name.Value : null;

    /// <summary>True when the resources carry <paramref name="name"/> under <paramref name="category"/>.</summary>
    private static bool Contains(
        ConformanceContext context, PdfResources? resources, string category, string name) =>
        resources is not null
        && context.Resolve(resources.Dictionary.Get(category)) is PdfDictionary sub
        && sub.TryGetValue(new PdfName(name), out _);

    private Finding Make(
        ConformanceContext context, int pageIndex, PdfDictionary? owner, IReadOnlyList<string> names) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.2.2"),
        Message = $"A content stream refers to resource(s) {string.Join(", ", names)} not defined in an "
                  + "explicitly associated Resources dictionary.",
        PageIndex = pageIndex,
        ObjectNumber = owner is { IsIndirect: true } ? owner.ObjectNumber : null,
    };
}
```

**If `PdfDictionary` has no `Get(string)` or no `IsIndirect`/`ObjectNumber`, STOP and ASK** — do not invent an accessor. `ReferencedFontWalker.cs:119-133` uses `node.Get("Resources")`, `node.IsIndirect` and `node.ObjectNumber`, so all three should exist; confirm against that file.

- [ ] **Step 4: Register the rule**

In `PdfLibrary/Conformance/Preflighter.cs`, immediately after the `new Rules.ContentStreamOperatorRule(),` entry (~line 63), add:

```csharp
        // A content stream references only resources in its DIRECTLY associated /Resources (6.2.2 test 2).
        new Rules.ExplicitResourcesRule(),
```

- [ ] **Step 5: Run the unit tests**

```powershell
dotnet test PdfLibrary.Tests --filter 'FullyQualifiedName~ExplicitResourcesRuleTests' -c Release
```

Expected: all PASS.

- [ ] **Step 6: Run the parity gate**

Expected: PASS with **zero false positives**. Agreement should rise from 978 to **979** (`6-2-2-t04-fail-f` closes). If agreement does not move, the page-scope path is not reaching the fixture — diagnose before continuing. If a false positive appears, revert and report.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Conformance/Rules/ExplicitResourcesRule.cs PdfLibrary/Conformance/Preflighter.cs PdfLibrary.Tests/Conformance/ExplicitResourcesRuleTests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "feat: flag resources inherited by a page content stream (6.2.2 t2)"
```

---

### Task 4: ExplicitResourcesRule — Form XObject scope

Closes `6-2-2-t04-fail-e` (Form XObject with no `/Resources` using `/CS0 cs` from the page).

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/ExplicitResourcesRule.cs`
- Test: `PdfLibrary.Tests/Conformance/ExplicitResourcesRuleTests.cs`

**Interfaces:**
- Consumes: `ExplicitResourcesRule`'s `Offenders`, `ResourcesOf`, `Contains`, `Make` from Task 3.
- Produces: a recursive walk signature Task 5 extends:
  `private List<Finding> WalkStream(ConformanceContext context, IReadOnlyList<PdfOperator> ops, PdfResources? direct, PdfResources? inherited, int pageIndex, PdfDictionary? owner, int depth, HashSet<int> activeForms)`

- [ ] **Step 1: Write the failing test**

Add to `ExplicitResourcesRuleTests.cs`:

```csharp
    /// <summary>A page whose own /Resources hold a form and a colour space; the form's /Resources are
    /// supplied separately (null = the form has none, the fail-e shape).</summary>
    private static PdfDocument FormDoc(string formContent, PdfDictionary? formResources)
    {
        var doc = new PdfDocument();

        var formDict = new PdfDictionary
        {
            [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"),
            [N("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0),
                                       new PdfInteger(10), new PdfInteger(10)),
        };
        if (formResources is not null) formDict[N("Resources")] = formResources;
        doc.AddObject(10, 0, new PdfStream(formDict, Ops(formContent)));

        doc.AddObject(11, 0, new PdfArray(N("CalGray")));

        var pageResources = new PdfDictionary
        {
            [N("XObject")] = new PdfDictionary { [N("X0")] = Ref(10) },
            [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = Ref(11) },
        };

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops("/X0 Do\n")));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4), [N("Resources")] = pageResources,
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void A_form_inheriting_a_colour_space_from_the_page_is_flagged()
    {
        Finding f = Assert.Single(Findings(FormDoc("/CS0 cs\n0.5 sc\n", formResources: null)));
        Assert.Contains("CS0", f.Message);
        Assert.Equal(10, f.ObjectNumber);
    }

    [Fact]
    public void A_form_with_no_resources_using_only_device_colour_is_not_flagged()
    {
        // The fail-e / pass-b discriminator.
        Assert.Empty(Findings(FormDoc("1 1 1 rg\n0 0 10 10 re f\n", formResources: null)));
    }

    [Fact]
    public void A_form_carrying_its_own_colour_space_is_not_flagged()
    {
        var own = new PdfDictionary
        {
            [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = Ref(11) },
        };
        Assert.Empty(Findings(FormDoc("/CS0 cs\n0.5 sc\n", own)));
    }
```

- [ ] **Step 2: Run it to verify it fails**

```powershell
dotnet test PdfLibrary.Tests --filter 'FullyQualifiedName~ExplicitResourcesRuleTests' -c Release
```

Expected: `A_form_inheriting_a_colour_space_from_the_page_is_flagged` FAILS (no finding — the walk does not enter forms yet). The other two should already pass.

- [ ] **Step 3: Convert the walk to recurse into forms**

Replace `Offenders` with a recursive `WalkStream` that both collects this scope's offenders and descends. Add to `ExplicitResourcesRule`:

```csharp
    /// <summary>
    /// Findings for this stream and every stream it reaches. A form's DIRECT resources are its own
    /// /Resources; its fallback is the invoking scope's EFFECTIVE resources (direct, else inherited) —
    /// what a consumer would actually resolve against. Cycle-guarded on the active Do path and
    /// depth-capped, mirroring ContentWalk.
    /// </summary>
    private List<Finding> WalkStream(
        ConformanceContext context, IReadOnlyList<PdfOperator> ops,
        PdfResources? direct, PdfResources? inherited,
        int pageIndex, PdfDictionary? owner, int depth, HashSet<int> activeForms)
    {
        var findings = new List<Finding>();
        if (depth > MaxDepth)
            return findings;

        List<string> offenders = Offenders(context, ops, direct, inherited);
        if (offenders.Count > 0)
            findings.Add(Make(context, pageIndex, owner, offenders));

        PdfResources? effective = direct ?? inherited;

        foreach (PdfOperator op in ops)
        {
            if (op is not InvokeXObjectOperator invoke)
                continue;
            if (effective?.GetXObject(invoke.XObjectName) is not { } form)
                continue;
            if (context.ResolveName(form.Dictionary.Get("Subtype")) != "Form")
                continue;
            if (form.IsIndirect && !activeForms.Add(form.ObjectNumber))
                continue; // already on the active Do path — a cycle

            byte[] data;
            try { data = form.GetDecodedData(context.Document.Decryptor); }
            catch { data = []; }

            if (data.Length > 0)
            {
                List<PdfOperator>? formOps = null;
                try { formOps = PdfContentParser.Parse(data); }
                catch { /* unparseable form contributes nothing */ }

                if (formOps is not null)
                {
                    findings.AddRange(WalkStream(
                        context, formOps, ResourcesOf(context, form.Dictionary), effective,
                        pageIndex, form.Dictionary, depth + 1, activeForms));
                }
            }

            if (form.IsIndirect)
                activeForms.Remove(form.ObjectNumber);
        }

        return findings;
    }
```

Then rewrite the body of `Check`'s per-page block to call it:

```csharp
            foreach (Finding finding in WalkStream(
                         context, ops, direct, inherited, pageIndex, page.Dictionary, 0, new HashSet<int>()))
            {
                yield return finding;
            }
```

(Remove the old `Offenders`/`Make` call in `Check` — `WalkStream` now owns it. Keep `Offenders` itself; it is still used.)

- [ ] **Step 4: Run the unit tests**

Expected: all PASS.

- [ ] **Step 5: Run the parity gate**

Expected: agreement **979 → 980** (`6-2-2-t04-fail-e` closes), zero false positives.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Conformance/Rules/ExplicitResourcesRule.cs PdfLibrary.Tests/Conformance/ExplicitResourcesRuleTests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "feat: descend into Form XObjects for 6.2.2 t2"
```

---

### Task 5: ExplicitResourcesRule — Type3 glyph procedures, and the stale t04 comments

Closes `6-2-2-t04-fail-d` (Type3 font with no `/Resources`; charprocs use `/CS0 cs` from the page).

Also corrects two comments that describe the `6-2-2-t04-*` corpus files as run-together-operator fixtures. They are the Resources fixtures for test 2 — the filename-vs-test-number trap `ParityReportTests.cs:57-59` already warns about.

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/ExplicitResourcesRule.cs`
- Modify: `PdfLibrary/Conformance/Rules/ContentStreamOperatorRule.cs:23-27`
- Modify: `PdfLibrary.Tests/Conformance/ParityReportTests.cs` (~line 100)
- Test: `PdfLibrary.Tests/Conformance/ExplicitResourcesRuleTests.cs`

**Interfaces:**
- Consumes: `WalkStream` from Task 4.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

Add to `ExplicitResourcesRuleTests.cs`:

```csharp
    /// <summary>A page showing a Type3 glyph; the Type3 font's /Resources are supplied separately
    /// (null = the font has none, the fail-d shape).</summary>
    private static PdfDocument Type3Doc(string charProcContent, PdfDictionary? fontResources)
    {
        var doc = new PdfDocument();

        doc.AddObject(20, 0, new PdfStream(new PdfDictionary(), Ops(charProcContent)));
        doc.AddObject(21, 0, new PdfDictionary { [N("square")] = Ref(20) });
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Encoding"),
            [N("Differences")] = new PdfArray(new PdfInteger(97), N("square")),
        });

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"), [N("Subtype")] = N("Type3"),
            [N("FontBBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0),
                                           new PdfInteger(750), new PdfInteger(750)),
            [N("FontMatrix")] = new PdfArray(new PdfReal(0.001), new PdfReal(0), new PdfReal(0),
                                             new PdfReal(0.001), new PdfReal(0), new PdfReal(0)),
            [N("CharProcs")] = Ref(21), [N("Encoding")] = Ref(22),
            [N("FirstChar")] = new PdfInteger(97), [N("LastChar")] = new PdfInteger(97),
            [N("Widths")] = new PdfArray(new PdfInteger(1000)),
        };
        if (fontResources is not null) font[N("Resources")] = fontResources;
        doc.AddObject(10, 0, font);

        doc.AddObject(11, 0, new PdfArray(N("CalGray")));

        var pageResources = new PdfDictionary
        {
            [N("Font")] = new PdfDictionary { [N("F1")] = Ref(10) },
            [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = Ref(11) },
        };

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops("BT\n/F1 12 Tf\n(a) Tj\nET\n")));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4), [N("Resources")] = pageResources,
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void A_type3_glyph_inheriting_a_colour_space_from_the_page_is_flagged()
    {
        Finding f = Assert.Single(Findings(Type3Doc("1000 0 d0\n/CS0 cs\n0.5 sc\n0 0 750 750 re f\n", null)));
        Assert.Contains("CS0", f.Message);
    }

    [Fact]
    public void A_type3_glyph_using_only_device_colour_is_not_flagged()
    {
        // The fail-d / pass-a discriminator: pass-a's charprocs are d1 + re/f with no named resource.
        Assert.Empty(Findings(Type3Doc("1000 0 0 0 750 750 d1\n0 0 750 750 re f\n", null)));
    }

    [Fact]
    public void A_type3_font_carrying_its_own_colour_space_is_not_flagged()
    {
        var own = new PdfDictionary
        {
            [N("ColorSpace")] = new PdfDictionary { [N("CS0")] = Ref(11) },
        };
        Assert.Empty(Findings(Type3Doc("1000 0 d0\n/CS0 cs\n0.5 sc\n0 0 750 750 re f\n", own)));
    }
```

- [ ] **Step 2: Run it to verify it fails**

Expected: `A_type3_glyph_inheriting_a_colour_space_from_the_page_is_flagged` FAILS.

- [ ] **Step 3: Descend into Type3 charprocs**

Inside `WalkStream`'s operator loop in `ExplicitResourcesRule.cs`, after the `InvokeXObjectOperator` block, add:

```csharp
            if (op.Name == "Tf" && NameOperand(op, 0) is { } fontName)
            {
                foreach (Finding finding in WalkType3(
                             context, effective, fontName, pageIndex, depth, activeForms))
                {
                    findings.Add(finding);
                }
            }
```

and add the helper:

```csharp
    /// <summary>
    /// A Type3 glyph procedure's DIRECTLY associated resources are the Type3 FONT dictionary's
    /// /Resources (ISO 32000-1 9.6.5); absent that, a consumer falls back to the invoking scope's.
    /// Every charproc is walked, not only the glyphs shown — the font is reached, which is what
    /// veraPDF models, and the corpus fixture's unused glyph carries the same defect as its used ones.
    /// </summary>
    private List<Finding> WalkType3(
        ConformanceContext context, PdfResources? effective, string fontName,
        int pageIndex, int depth, HashSet<int> activeForms)
    {
        var findings = new List<Finding>();

        if (effective is null
            || context.Resolve(effective.Dictionary.Get("Font")) is not PdfDictionary fonts
            || !fonts.TryGetValue(new PdfName(fontName), out PdfObject? fontObj)
            || context.Resolve(fontObj) is not PdfDictionary font
            || context.ResolveName(font.Get("Subtype")) != "Type3"
            || context.Resolve(font.Get("CharProcs")) is not PdfDictionary charProcs)
        {
            return findings;
        }

        PdfResources? direct = ResourcesOf(context, font);

        foreach (PdfObject value in charProcs.Values.ToList())
        {
            if (context.Resolve(value) is not PdfStream proc)
                continue;

            byte[] data;
            try { data = proc.GetDecodedData(context.Document.Decryptor); }
            catch { continue; }
            if (data.Length == 0)
                continue;

            List<PdfOperator> ops;
            try { ops = PdfContentParser.Parse(data); }
            catch { continue; }

            findings.AddRange(WalkStream(
                context, ops, direct, effective, pageIndex, font, depth + 1, activeForms));
        }

        return findings;
    }
```

**If `PdfDictionary` exposes its values under a different member than `.Values`, STOP and ASK.**

- [ ] **Step 4: Run the unit tests**

Expected: all PASS.

- [ ] **Step 5: Fix the two stale comments**

In `ContentStreamOperatorRule.cs:23-27`, the "KNOWN LIMITATION (clause 6.2.2 test t04)" comment claims the t04 files are run-together-operator fixtures. Replace that claim with:

```csharp
    /// <para>Naming caution: the corpus files named 6-2-2-t04-* are NOT operator fixtures — they are
    /// veraPDF's clause 6.2.2 test NUMBER 2 (explicitly associated /Resources) fixtures, handled by
    /// ExplicitResourcesRule. A corpus filename names its section, not the test number that fires;
    /// read verapdf-verdicts.json.</para>
```

Keep any genuinely accurate part of the surrounding comment; delete only the incorrect t04 claim. Apply the same correction to the matching note in `ParityReportTests.cs` (~line 100).

- [ ] **Step 6: Run the parity gate**

Expected: agreement **980 → 981** (`6-2-2-t04-fail-d` closes), zero false positives. Clause 6.2.2 should now read 6/6 = 100%.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Conformance/Rules/ExplicitResourcesRule.cs PdfLibrary/Conformance/Rules/ContentStreamOperatorRule.cs PdfLibrary.Tests/Conformance/ExplicitResourcesRuleTests.cs PdfLibrary.Tests/Conformance/ParityReportTests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "feat: descend into Type3 charprocs for 6.2.2 t2; fix stale t04 notes"
```

---

### Task 6: Base-encoding name provenance

Closes `6-2-11-4-1-t02-fail-a` and `-fail-b`.

`ResolveSimpleGlyph` (`FontProgramRule.cs:356`) refuses to call a glyph absent when its name was *derived* — this engine's reverse-AGL reconstruction. That gate is correct. The bug is the classification: `CreateWinAnsiEncoding` builds codes 32–126 with `SetUnicode`, marking every WinAnsi name derived, while `CreateStandardEncoding` builds the same range with `SetCharacterName` and marks none. A name from the Annex-D WinAnsi table, named by the document's own `/Encoding`, is asserted by the document — not a guess.

The two Annex-D tables differ at exactly two codes: 39 (`quotesingle` vs `quoteright`) and 96 (`grave` vs `quoteleft`). So WinAnsi's ASCII names derive from the existing `StandardEncodingAsciiNames` with two overrides — do NOT hand-type 95 entries.

**Files:**
- Modify: `PdfLibrary/Fonts/PdfFontEncoding.cs:392-400` (WinAnsi), `:446-449` (MacRoman)
- Test: `PdfLibrary.Tests/Fonts/PdfFontEncodingTests.cs` (create if absent)

**Interfaces:**
- Consumes: `PdfFontEncoding.SetCharacterName(int, string)`, `IsDerivedName(int)`, `StandardEncodingAsciiNames`.
- Produces: no signature change. Only `IsDerivedName` results change; glyph names and Unicode values are unchanged.

- [ ] **Step 1: Write the failing test**

Add (creating the file if needed, matching the namespace `PdfLibrary.Tests.Fonts`):

```csharp
    [Fact]
    public void WinAnsi_ascii_names_are_document_asserted_not_derived()
    {
        PdfFontEncoding enc = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");

        // period and numbersign are the two the corpus fixtures turn on.
        Assert.Equal("period", enc.GetGlyphName(46));
        Assert.Equal("numbersign", enc.GetGlyphName(35));
        Assert.False(enc.IsDerivedName(46));
        Assert.False(enc.IsDerivedName(35));
    }

    [Fact]
    public void WinAnsi_differs_from_standard_at_exactly_two_ascii_codes()
    {
        PdfFontEncoding win = PdfFontEncoding.GetStandardEncoding("WinAnsiEncoding");
        PdfFontEncoding std = PdfFontEncoding.GetStandardEncoding("StandardEncoding");

        Assert.Equal("quotesingle", win.GetGlyphName(39));
        Assert.Equal("quoteright", std.GetGlyphName(39));
        Assert.Equal("grave", win.GetGlyphName(96));
        Assert.Equal("quoteleft", std.GetGlyphName(96));

        // Every other ASCII code agrees — this is what lets WinAnsi reuse the Standard table.
        for (var code = 32; code <= 126; code++)
        {
            if (code is 39 or 96) continue;
            Assert.Equal(std.GetGlyphName(code), win.GetGlyphName(code));
        }
    }
```

`IsDerivedName` is `internal`; the test project already sees internals (existing tests use `Preflighter`, `Finding`). If `GetGlyphName` has a different name, check `PdfFontEncoding.cs` and use the real one.

- [ ] **Step 2: Run it to verify it fails**

```powershell
dotnet test PdfLibrary.Tests --filter 'FullyQualifiedName~PdfFontEncodingTests' -c Release
```

Expected: `WinAnsi_ascii_names_are_document_asserted_not_derived` FAILS — `IsDerivedName` returns true.

- [ ] **Step 3: Build WinAnsi's ASCII range by name**

In `PdfFontEncoding.cs`, add next to `StandardEncodingAsciiNames`:

```csharp
    /// <summary>
    /// The Annex D.2 WinAnsiEncoding names for codes 32-126. Identical to StandardEncoding except
    /// 39 = quotesingle (not quoteright) and 96 = grave (not quoteleft), so it is derived from that
    /// table rather than restated. Assigning these BY NAME matters beyond correctness of the name
    /// itself: SetUnicode marks a name DERIVED (a reverse-AGL reconstruction), and FontProgramRule
    /// will not call a glyph absent on a derived name. A name from a base-encoding table the document
    /// explicitly named is asserted by the document, not reconstructed by us.
    /// </summary>
    private static readonly string[] WinAnsiEncodingAsciiNames = BuildWinAnsiAsciiNames();

    private static string[] BuildWinAnsiAsciiNames()
    {
        string[] names = (string[])StandardEncodingAsciiNames.Clone();
        names[39 - 32] = "quotesingle";
        names[96 - 32] = "grave";
        return names;
    }
```

Then replace the ASCII loop in `CreateWinAnsiEncoding` (lines 396-400):

```csharp
        // Codes 32-126 by Annex D.2 NAME, not by reverse-AGL from the code point — SetCharacterName
        // also derives the Unicode via the AGL, so the mappings are unchanged, but the names are now
        // marked document-asserted rather than derived.
        for (var i = 0; i < WinAnsiEncodingAsciiNames.Length; i++)
        {
            encoding.SetCharacterName(32 + i, WinAnsiEncodingAsciiNames[i]);
        }
```

Leave the 128–159 and 160–255 bands as they are — converting those means writing two more full Annex-D tables and is deferred.

- [ ] **Step 4: Run the encoding tests, then the parity gate**

Expected: encoding tests PASS. Parity gate: agreement **981 → 983** (`t02-fail-a` and `t02-fail-b` close), zero false positives.

**If a false positive appears, stop and report it before doing anything else.** This step widens what the CFF arm will call absent, which is exactly the class the derived gate protects. The `GetGlyphIdByCffEncoding` fallback (`FontProgramRule.cs:367`) is the remaining safety net.

- [ ] **Step 5: Apply the same fix to MacRoman — as a SEPARATE commit**

`CreateMacRomanEncoding` (lines 446-449) has the identical bug in its ASCII range. MacRomanEncoding's ASCII names match StandardEncoding except at the same two codes as WinAnsi (39, 96), so reuse `WinAnsiEncodingAsciiNames`:

```csharp
        // ASCII portion (32-126) — by NAME, for the same provenance reason as WinAnsi above.
        // MacRoman's ASCII names match WinAnsi's (quotesingle at 39, grave at 96).
        for (var i = 0; i < WinAnsiEncodingAsciiNames.Length; i++)
        {
            encoding.SetCharacterName(32 + i, WinAnsiEncodingAsciiNames[i]);
        }
```

Run the parity gate again. No corpus fixture needs this, so agreement must stay at **983** and false positives at **0**. If either moves, revert this step only — it is deliberately a separate commit so it can be dropped without losing the WinAnsi fix.

- [ ] **Step 6: Commit (two commits)**

```bash
git add PdfLibrary/Fonts/PdfFontEncoding.cs PdfLibrary.Tests/Fonts/PdfFontEncodingTests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "fix: assign WinAnsi ASCII names by name, not reverse-AGL (6.2.11.4.1)"
# then the MacRoman step
git commit -am "fix: assign MacRoman ASCII names by name for the same provenance reason"
```

---

### Task 7: An undefined code is .notdef

Closes `6-2-11-8-t01-fail-a` (Type1 + Type1C) and `-fail-b` (TrueType). Both show `<00>` — character code 0, which `/WinAnsiEncoding` leaves undefined, so the encoding yields no glyph name and the `== ".notdef"` test never fires.

Per ISO 32000-1 §9.6.6 a simple-font code with no entry in the effective encoding renders `.notdef`.

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/FontProgramRule.cs` (the `.notdef` check in `CheckSimple`, ~lines 189-194)
- Test: `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs`

**Interfaces:**
- Consumes: `EmbeddedFontMetrics.GetGlyphIdByCffEncoding(ushort)`, `.GetGlyphId(ushort)`, `.HasSymbolCmapEncoding()`, `FontProgramRule.IsSymbolic(...)`.
- Produces: no signature change.

- [ ] **Step 1: Verify the safety net empirically BEFORE writing code**

The predicate's third condition needs a *different* call per arm, and the CFF helper returns 0 for a TrueType program for the wrong reason. Write a temporary throwaway test that loads the two corpus fixtures and prints, for code 0: `GetGlyphIdByCffEncoding(0)` and `GetGlyphId(0)`. Confirm both read "no glyph" on the fixtures. Then load a **conformant** control (`PDFUA-Ref-2-04`, or any corpus `-pass-` file with a simple font) and confirm the same calls report a glyph for codes it actually shows.

**Delete the throwaway test in the same step.** Do not leave it in the tree.

If the TrueType arm's `GetGlyphId(0)` does NOT return 0 on `fail-b`, STOP and ASK — the predicate needs rethinking, not forcing.

- [ ] **Step 2: Write the failing test**

Add to `PreflightSlice19Tests.cs`. Reuse the existing `TrueTypeDoc`-style helpers in that file; read them first and match their shape. The fixture must show code 0 under a base encoding with no `/Differences`:

```csharp
    [Fact]
    public void Simple_truetype_undefined_code_fails_notdef()
    {
        // ISO 32000-1 9.6.6: a code with no entry in the effective encoding renders .notdef.
        // The corpus fixtures 6-2-11-8-t01-fail-a/-b both show <00> under WinAnsiEncoding, which
        // leaves code 0 undefined — the encoding yields NO name, so a `name == ".notdef"` test
        // never fires.
        Finding f = Assert.Single(
            Run(TrueTypeDocShowingUndefinedCode()), x => Clause(x) == "6.2.11.8");
        Assert.Contains(".notdef", f.Message);
    }

    [Fact]
    public void Symbolic_font_undefined_code_is_not_flagged()
    {
        // A symbolic font drives its own built-in encoding and routinely has null names — exempt.
        Assert.Empty(Run(TrueTypeDocShowingUndefinedCode(symbolic: true))
                     .Where(x => Clause(x) == "6.2.11.8"));
    }
```

Add a `TrueTypeDocShowingUndefinedCode(bool symbolic = false)` helper modelled directly on the existing `TrueTypeDocShowingNotdefGlyph` (line ~335): same font, `/Encoding` a plain `/WinAnsiEncoding` name with **no** `/Differences`, `/Flags` 32 (or 4 when `symbolic`), content showing `<00>`.

- [ ] **Step 3: Run it to verify it fails**

```powershell
dotnet test PdfLibrary.Tests --filter 'FullyQualifiedName~PreflightSlice19Tests' -c Release
```

Expected: `Simple_truetype_undefined_code_fails_notdef` FAILS (no finding).

- [ ] **Step 4: Extend the .notdef predicate**

In `CheckSimple`, replace the single-line `.notdef` condition with a helper call, and add the helper. The existing check is:

```csharp
        if (codes.Any(code => font.Encoding?.GetGlyphName(code) == ".notdef") && notdefReported.Add(DedupKey(font)))
```

Replace with:

```csharp
        if (codes.Any(code => IsNotdefReference(font, metrics, code, isTrueType, symbolic))
            && notdefReported.Add(DedupKey(font)))
```

and add:

```csharp
    /// <summary>
    /// True when a shown simple-font code references .notdef. Two ways that happens:
    /// the effective encoding names it ".notdef" outright, or the encoding defines NO name for the
    /// code at all — ISO 32000-1 9.6.6, a code outside the effective encoding renders .notdef. The
    /// corpus's two 6.2.11.8 fail fixtures are the second kind: both show &lt;00&gt; under
    /// WinAnsiEncoding, which leaves code 0 undefined.
    ///
    /// <para>The no-name case is gated twice, because "no name" is also what a symbolic font looks
    /// like when it is driving its own built-in encoding. A symbolic font is exempt outright, and a
    /// nonsymbolic one still has to fail a program-side lookup of the RAW code through the font's own
    /// built-in encoding before we call it .notdef. The two arms need different calls: the CFF
    /// encoding helper is meaningless for a TrueType program (it would answer 0 for the wrong
    /// reason), so TrueType keys the font's cmap by the code directly.</para>
    /// </summary>
    private static bool IsNotdefReference(
        PdfFont font, EmbeddedFontMetrics metrics, int code, bool isTrueType, bool symbolic)
    {
        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (glyphName == ".notdef")
            return true;
        if (glyphName is not null)
            return false;

        if (symbolic || metrics.HasSymbolCmapEncoding())
            return false; // built-in encoding territory — a null name says nothing

        return isTrueType
            ? metrics.GetGlyphId((ushort)code) == 0
            : metrics.GetGlyphIdByCffEncoding((ushort)code) == 0;
    }
```

`symbolic` is already computed in `CheckSimple` (line ~187). Confirm it is in scope at the `.notdef` check; if it is declared below, move its declaration above.

- [ ] **Step 5: Run the slice tests, then the parity gate**

Expected: slice tests PASS. Parity gate: agreement **983 → 985**, zero false positives.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Conformance/Rules/FontProgramRule.cs PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "feat: an undefined simple-font code references .notdef (6.2.11.8)"
```

---

### Task 8: An incomplete final composite code is .notdef

Closes `6-2-11-4-1-t02-fail-e`. Its content ends with `(#)` — a one-byte string under a two-byte Identity-H CMap. `ToUnicodeUsageCollector.cs:115-127` skips the odd trailing byte as "not a complete code", so the font's collected CIDs are all legitimately present and nothing fires. A code that cannot be completed cannot map to a glyph; that is the defect.

We do **not** fabricate a padded CID. We record that an unmappable code was shown.

**Files:**
- Modify: `PdfLibrary/Content/ToUnicodeUsageCollector.cs:115-127`
- Modify: `PdfLibrary/Conformance/ConformanceContext.cs` (`UsedFontCodes` record at :31, and `EnsureUsedTextGlyphs` at ~:420-460)
- Modify: `PdfLibrary/Conformance/Rules/FontProgramRule.cs` (`CheckType0`, ~:123-137)
- Test: `PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs`

**Interfaces:**
- Consumes: `UsedFontCodes(PdfFont Font, IReadOnlyCollection<int> Codes, IReadOnlyCollection<int> VisibleCodes)`.
- Produces: `UsedFontCodes(PdfFont Font, IReadOnlyCollection<int> Codes, IReadOnlyCollection<int> VisibleCodes, bool ShowedIncompleteCode)`. **This is a breaking record change** — every construction site must be updated. Find them all with `grep -rn "new UsedFontCodes" --include=*.cs`.

- [ ] **Step 1: Write the failing test**

Add to `PreflightSlice19Tests.cs`, modelled on the existing `Type0Doc` helper (line ~113) — read it first:

```csharp
    [Fact]
    public void Type0_incomplete_final_code_fails_notdef()
    {
        // A one-byte string under a two-byte Identity-H CMap: the final code cannot be completed,
        // so it cannot map to any glyph. The corpus fixture 6-2-11-4-1-t02-fail-e ends with (#)
        // exactly this way; every CID it does complete is legitimately present in the program.
        Finding f = Assert.Single(
            Run(Type0DocWithOddLengthString()), x => Clause(x) == "6.2.11.8");
        Assert.Contains(".notdef", f.Message);
    }

    [Fact]
    public void Type0_even_length_string_is_not_flagged_as_incomplete()
    {
        // Guard the premise: the same font showing only complete codes must stay silent, or the
        // test above would pass for the wrong reason.
        Assert.Empty(Run(Type0DocWithEvenLengthString()).Where(x => Clause(x) == "6.2.11.8"));
    }
```

Add `Type0DocWithOddLengthString()` and `Type0DocWithEvenLengthString()` helpers built on the existing `Type0Doc`, differing only in the shown string's byte length. Use CIDs that genuinely exist in `PublicPixel.ttf` for the even case so the guard is meaningful.

- [ ] **Step 2: Run it to verify it fails**

Expected: `Type0_incomplete_final_code_fails_notdef` FAILS.

- [ ] **Step 3: Record the incomplete code in the collector**

In `ToUnicodeUsageCollector.cs`, add a field and expose it:

```csharp
    private readonly HashSet<PdfFont> _incompleteCode = new(ReferenceEqualityComparer.Instance);

    /// <summary>Fonts shown with a trailing byte that could not complete a multi-byte code — an
    /// unmappable code, which for conformance is a .notdef reference (see FontProgramRule).</summary>
    public IReadOnlySet<PdfFont> IncompleteCodeFonts => _incompleteCode;
```

and replace the Type0 branch of `Accumulate` (lines 115-127):

```csharp
        if (font is Type0Font)
        {
            // Two-byte big-endian codes (Identity-H/V and the common CID case).
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                int code = (bytes[i] << 8) | bytes[i + 1];
                codes.Add(code);
                visibleCodes?.Add(code);
            }

            // A trailing odd byte cannot complete a two-byte code. Do NOT invent a padded CID —
            // record that an unmappable code was shown and let the conformance rule judge it.
            if ((bytes.Length & 1) == 1)
                _incompleteCode.Add(font);
        }
```

Nested collectors (the `Do` recursion) must propagate this set to the parent — find where `VisibleResult` is merged from a nested collector and merge `IncompleteCodeFonts` the same way. If there is no such merge, note it and continue.

- [ ] **Step 4: Carry it through the context**

Add the flag to `UsedFontCodes` (`ConformanceContext.cs:31`):

```csharp
internal readonly record struct UsedFontCodes(
    PdfFont Font, IReadOnlyCollection<int> Codes, IReadOnlyCollection<int> VisibleCodes,
    bool ShowedIncompleteCode);
```

Update its doc comment to explain the new member. In `EnsureUsedTextGlyphs`, accumulate a `HashSet<PdfFont> incomplete` across pages from `collector.IncompleteCodeFonts` and pass `incomplete.Contains(kv.Key)` when building each `UsedFontCodes`.

Then fix every other construction site found by the grep in **Interfaces**.

- [ ] **Step 5: Judge it in the rule**

In `FontProgramRule.CheckType0`, seed the `.notdef` flag from the new member. Change:

```csharp
        bool notdefHit = false;
```

to:

```csharp
        // An incomplete final code (an odd trailing byte under a two-byte CMap) cannot map to any
        // glyph, so it is a .notdef reference — the same conclusion veraPDF reaches on
        // 6-2-11-4-1-t02-fail-e, whose completed CIDs are all present in the program.
        bool notdefHit = showedIncompleteCode;
```

`CheckType0`'s signature must take the flag. Add a `bool showedIncompleteCode` parameter and pass `usage.ShowedIncompleteCode` from `Check`.

Note this sits **below** the Identity-CMap gate at line 115, so it only applies to Identity CMaps. That is correct and deliberate: the two-byte assumption the collector makes is only sound there.

- [ ] **Step 6: Run the slice tests, then the parity gate**

Expected: slice tests PASS. Parity gate: agreement **985 → 986**, zero false positives, **PDF/A-2b misses = 0**.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Content/ToUnicodeUsageCollector.cs PdfLibrary/Conformance/ConformanceContext.cs PdfLibrary/Conformance/Rules/FontProgramRule.cs PdfLibrary.Tests/Conformance/PreflightSlice19Tests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "feat: an incomplete final composite code references .notdef (6.2.11.8)"
```

---

### Task 9: Lock the result, measure the clauses, file the deferrals

**Files:**
- Modify: `PdfLibrary.Tests/Conformance/ParityReportTests.cs` (floor)
- Modify: `PdfLibrary.Tests/Conformance/ParityOracleTests.cs:36-42` (`ParityFullClauses`)
- Modify: `PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md` (regenerated)

**Interfaces:**
- Consumes: everything above.
- Produces: the final ratchet state.

- [ ] **Step 1: Corpus scan against real files**

Unit tests have passed while a production arm was completely dead before (the 6.1.13 object arm). Build the app and scan the real-document corpus:

```powershell
dotnet build Pellucid.App -c Release
pellucid scan <real-document corpus path>
```

Compare finding counts against the pre-change baseline. A large jump in `explicit-resources` findings on real documents is a false-positive signal even though the veraPDF corpus is clean — investigate before continuing. Record the counts in the commit body.

- [ ] **Step 2: Raise the agreement floor to 986**

In `ParityReportTests.cs`, set `[ConformanceProfile.PdfA2b] = 986,` and add above the existing history comments:

```csharp
            // +8 (978->986), 2026-08-20: PDF/A-2b reaches FULL verdict parity. 6.2.2 t2 via the new
            // ExplicitResourcesRule (+3: t04-fail-d/-e/-f); the font cluster via three targeted fixes
            // (+5) -- WinAnsi/MacRoman ASCII names assigned BY NAME so they are no longer marked
            // reverse-AGL "derived" (t02-fail-a/-b), an undefined encoding code treated as .notdef
            // (8-t01-fail-a/-b), and an incomplete final composite code treated as .notdef
            // (t02-fail-e). All four profiles now agree on every file.
```

- [ ] **Step 3: Read the measured clause coverage and update ParityFullClauses**

Open the regenerated `PARITY-REPORT.md` and read the actual clause-coverage percentages. Add to `ParityFullClauses[PdfA2b]` **only clauses measuring 100%**:

- `6.2.2` — expected 6/6, add it.
- `6.1.6` and `6.1.13` — already at full parity from earlier landings and never added; add them now.
- The font clauses — **expected NOT to reach full** (projection: 6.2.11.4.1 8/11, 6.2.11.8 6/8, 6.2.11.5 7/13). Add one only if it actually measures 100%. Do not add a clause on the strength of this plan's projection; read the report.

Record the measured numbers in the commit body.

- [ ] **Step 4: Assess issue 32**

Issue 32 is "6.2.11.4.1 not a locked parity clause". It closes only if that clause measures full parity in Step 3. On the projection it will not. Report the measured number and leave the issue open if it is short — do not close it on plan.

- [ ] **Step 5: File the deferred items as tracker issues**

One issue each, quoting the spec's "Deferred" section:

1. Type0 fonts with a stream or indirect `/Encoding` are skipped entirely — `Type0Font.EncodingName` returns null for both, so `CheckType0`'s Identity gate drops the font and loses its `.notdef` and width checks. `CidCMap` already exists; the "engine lacks a CMap parser" comment at `FontProgramRule.cs:113-114` is stale. No corpus fixture exercises it.
2. `GID >= NumGlyphs` is never checked for an Identity `/CIDToGIDMap`. The predicate exists at `SubsetProgramGlyphs.cs:30-55` and is used by 6.2.11.4.2 but not `FontProgramRule`.
3. Predefined-charset CFF (ISOAdobe/Expert/ExpertSubset) remains out of scope for the simple-font arm.
4. WinAnsi/MacRoman bands 128–255 still assign names via `SetUnicode`, so they remain marked derived; Task 6 converted only the ASCII range.

- [ ] **Step 6: Full test suite — ONCE**

```powershell
dotnet test PdfLibrary.Tests -c Release
```

Expected: green. This is the one full-suite run for the branch. Read the SKIP count, not just PASS.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary.Tests/Conformance/ParityReportTests.cs PdfLibrary.Tests/Conformance/ParityOracleTests.cs PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "test: PDF/A-2b at full verdict parity — floor 986, 6.2.2 locked"
```

- [ ] **Step 8: Whole-branch review**

Request a review of the entire branch diff, not per-task diffs. Every prior program in this repo has had a shipping Critical that only the whole-branch view exposed — defects that live at task seams are invisible per-task.

- [ ] **Step 9: Batch gate — ONCE**

```bash
tools/gate.sh
```

~40 minutes. Judge liveness by the child `Pellucid.App.Tests` CPU, never `testhost`. Read the SKIP count.

---

## Self-review notes

- **Spec coverage.** Job A → Tasks 3-5. Job B: B1 → Task 6, B2 → Task 7, B3 → Task 8. Tooling: T1 → Task 2, T2 → Tasks 1 and 9, T3 → Task 5, T4 → Task 9. Deferred items → Task 9 Step 5. Test cadence → Global Constraints.
- **Projected agreement per task:** 978 → 979 (T3) → 980 (T4) → 981 (T5) → 983 (T6) → 985 (T7) → 986 (T8). Each task states its expected number so a task that closes nothing is caught immediately rather than at the end.
- **Type consistency.** `WalkStream` and `WalkType3` share the `(context, ops, direct, inherited, pageIndex, owner, depth, activeForms)` shape; `ResourcesOf`/`Contains`/`Make`/`NameOperand` are introduced in Task 3 and reused unchanged in Tasks 4-5. `UsedFontCodes` gains exactly one member, in Task 8 only.
- **Known risk concentration.** Task 6 is the one step that widens an existing FP-safety gate. It is deliberately split into two commits (WinAnsi, then MacRoman) so the lower-value half can be dropped alone.
