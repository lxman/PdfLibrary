# Font-Program Fault Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make font-program parse failures in `EmbeddedFontMetrics` observable as data, and add a local corpus canary that fails when a new one appears.

**Architecture:** Every one of the eleven `catch` blocks in `EmbeddedFontMetrics` records a `FontProgramFault(stage, exceptionType)` onto an append-only list exposed as `Faults`. Behaviour is otherwise untouched — no fallback changes, no throws. A `LocalOnly` test then walks the GWG corpus, collects faults per (file, font), and diffs them against a committed TSV baseline with a regen escape hatch.

**Tech Stack:** C# / .NET 10, xunit.v3 3.2.2, `PdfLibrary.Tests` (`OutputType=Exe`).

**Spec:** `Docs/superpowers/specs/2026-08-04-font-program-fault-diagnostics-design.md`

## Global Constraints

- **Repo:** `C:\Users\jorda\RiderProjects\PdfLibrary` (the engine). Nothing in this plan touches Pellucid.
- **No behaviour changes.** Every existing fallback value, assignment, and control-flow path stays character-for-character identical. This work is additive observation only. If a test that passed before now fails, that is a defect in this work, not a discovery.
- **Never record the exception message** in a `FontProgramFault`. `Stage` + `ExceptionType` only. Messages vary by .NET version and locale and would destabilise a committed baseline. Messages may still go to `PdfLogger`, which is not committed.
- **`EmbeddedFontMetrics` is `internal`.** New types alongside it are `internal` too. `PdfLibrary.Tests` already has `InternalsVisibleTo` (`PdfLibrary/PdfLibrary.csproj:85-93`).
- **Corpus tests carry `[Trait("Category", "LocalOnly")]`.** CI runs `--filter Category!=LocalOnly` and no PDF corpus is committed (`PdfLibrary/.gitignore:6` ignores `*.pdf`). Unit tests must NOT carry the trait.
- **Existing code style:** file-scoped namespaces, `var` where the type is on the right, collection expressions (`[]`), explicit types otherwise. Match the surrounding file.

---

### Task 1: Fault record and the eleven catch sites

**Files:**
- Create: `PdfLibrary/Fonts/Embedded/FontProgramFault.cs`
- Modify: `PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs` (eleven catch blocks at lines 262, 272, 283, 302, 316, 331, 345, 361, 394, 484, 1401; plus new field/property/method)
- Test: `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks. Uses `MinimalCff.Build(int? charsetOperand, int numGlyphs, ushort[]? customCharsetSids = null)` from `CffTestFixtures` (namespace `CffTestFixtures`), already linked into `PdfLibrary.Tests` via `PdfLibrary.Tests.csproj:47`.
- Produces, relied on by Tasks 2 and 3:
  - `internal enum FontProgramStage { SfntDirectory, Head, MaxP, Hhea, Hmtx, Name, Cmap, RawCff, CffTable, GlyfLoca, Type1Program }`
  - `internal readonly record struct FontProgramFault(FontProgramStage Stage, string ExceptionType)`
  - `EmbeddedFontMetrics.Faults` → `IReadOnlyList<FontProgramFault>`, never null, empty when clean, **append-only across the object's lifetime** (the `GlyfLoca` entry can only appear after a `GetGlyphOutline` call).

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs`:

```csharp
using CffTestFixtures;
using PdfLibrary.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// <see cref="EmbeddedFontMetrics"/> swallows every font-program parse failure into a fallback. These
/// tests pin the fact that it now also RECORDS each one. The recording is what makes the failure
/// assertable — the Type1C CFF charset bug (engine 6564363) survived for months because a bare
/// <c>catch { _isCffFont = false; }</c> made a broken parser look healthy.
/// <para>These are ordinary unit tests, NOT LocalOnly: they build their fixtures in memory.</para>
/// </summary>
public class FontProgramFaultTests
{
    /// <summary>A structurally valid raw CFF, cut short so the parser runs off the end.</summary>
    private static byte[] TruncatedRawCff()
    {
        byte[] whole = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        return whole[..(whole.Length / 2)];
    }

    [Fact]
    public void CleanProgram_RecordsNoFaults()
    {
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.True(metrics.IsValid);
        Assert.Empty(metrics.Faults);
    }

    [Fact]
    public void TruncatedRawCff_RecordsARawCffFault()
    {
        var metrics = new EmbeddedFontMetrics(TruncatedRawCff());

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.RawCff);
    }

    [Fact]
    public void TruncatedRawCff_FallbackBehaviourIsUnchanged()
    {
        // The point of the whole exercise: recording must not alter what the swallow does.
        var metrics = new EmbeddedFontMetrics(TruncatedRawCff());

        Assert.False(metrics.IsCffFont);
        Assert.False(metrics.IsValid);
    }

    [Fact]
    public void FaultsNeverCarryTheExceptionMessage()
    {
        // Messages vary by runtime and locale; a committed baseline keyed on them would churn.
        var metrics = new EmbeddedFontMetrics(TruncatedRawCff());

        Assert.NotEmpty(metrics.Faults);
        Assert.All(metrics.Faults, f =>
        {
            Assert.False(string.IsNullOrEmpty(f.ExceptionType));
            Assert.DoesNotContain(" ", f.ExceptionType); // a type name, not a sentence
        });
    }

    [Fact]
    public void GarbageSfntProgram_RecordsASfntDirectoryFault()
    {
        // Does not start 0x01 0x00, so it skips the raw-CFF arm and goes straight to the sfnt reader.
        var garbage = new byte[64];
        garbage[0] = 0xDE;
        garbage[1] = 0xAD;

        var metrics = new EmbeddedFontMetrics(garbage);

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.SfntDirectory);
        Assert.False(metrics.IsValid);
        Assert.Equal(1000, metrics.UnitsPerEm); // the documented fallback, unchanged
    }

    [Fact]
    public void OmittedCharsetOperator_IsNotAFault()
    {
        // The Type1C shape from 6564363. TN #5176 Table 9 defaults charset to 0, so this is a VALID
        // program and must record nothing. This is the "stays fixed" guard: if the charset regression
        // ever returns, this test goes red with a CffTable/RawCff fault.
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.Empty(metrics.Faults);
        Assert.True(metrics.IsCffFont);
    }

    [Fact]
    public void FaultsIsNeverNull()
    {
        Assert.NotNull(new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4)).Faults);
        Assert.NotNull(new EmbeddedFontMetrics([], length1: 0, length2: 0).Faults);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontProgramFaultTests"
```

Expected: **compile failure** — `FontProgramStage` and `Faults` do not exist. That is the correct "red" for this task.

- [ ] **Step 3: Create the fault type**

Create `PdfLibrary/Fonts/Embedded/FontProgramFault.cs`:

```csharp
namespace PdfLibrary.Fonts.Embedded;

/// <summary>
/// The parse stage a font-program failure happened in. One value per <c>catch</c> block in
/// <see cref="EmbeddedFontMetrics"/>.
/// </summary>
internal enum FontProgramStage
{
    /// <summary>The sfnt table directory (<c>FontParser.SfntFont</c>) — a non-sfnt or malformed program.</summary>
    SfntDirectory,
    Head,
    MaxP,
    Hhea,
    Hmtx,
    Name,
    Cmap,
    /// <summary>A bare CFF program (no sfnt wrapper), detected by the <c>01 00</c> version prefix.</summary>
    RawCff,
    /// <summary>The <c>CFF </c> table inside an sfnt wrapper (OpenType/CFF).</summary>
    CffTable,
    /// <summary>The lazily-loaded <c>loca</c>/<c>glyf</c> pair. Only ever recorded after a
    /// <see cref="EmbeddedFontMetrics.GetGlyphOutline"/> call has forced the load.</summary>
    GlyfLoca,
    /// <summary>A PostScript Type1 program parsed through the Length1/Length2/Length3 constructor.</summary>
    Type1Program
}

/// <summary>
/// A single swallowed font-program parse failure: which stage threw, and the exception's type name.
/// <para>The exception MESSAGE is deliberately absent. These records are compared against a committed
/// corpus baseline, and messages vary across .NET versions and locales — including the message would
/// make the baseline churn for reasons that have nothing to do with the parser. The message still
/// reaches <c>PdfLogger</c>, which is not committed to anything.</para>
/// </summary>
internal readonly record struct FontProgramFault(FontProgramStage Stage, string ExceptionType)
{
    /// <summary>Stable one-line form for the corpus baseline, e.g. <c>CffTable:IndexOutOfRangeException</c>.</summary>
    public override string ToString() => $"{Stage}:{ExceptionType}";
}
```

- [ ] **Step 4: Add the field, property, and recorder to `EmbeddedFontMetrics`**

In `PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs`, add the field immediately after the `_isType1Font` field (currently line 44):

```csharp
    // Swallowed font-program parse failures. Append-only: constructor stages fill it first, and the
    // lazy GlyfLoca stage can add to it later on the first outline request.
    private readonly List<FontProgramFault> _faults = [];
```

Add the property immediately after the `IsValid` property (currently line 79):

```csharp
    /// <summary>
    /// Every font-program parse failure this instance swallowed, in the order they happened. Empty when
    /// the program parsed cleanly.
    /// <para>Exists because the fallbacks below are silent by construction: a failed <c>CFF </c> parse
    /// sets <see cref="IsCffFont"/> false, and the font then renders through the TrueType path reading a
    /// <c>glyf</c> table it does not have — producing blank glyphs and no error. That is how the Type1C
    /// charset bug (engine 6564363) hid for months. Nothing here changes any fallback; it only records
    /// that one was taken, so a test can assert on it.</para>
    /// <para>Append-only across the object's lifetime, not frozen at construction:
    /// <see cref="FontProgramStage.GlyfLoca"/> is recorded by the lazy <c>LoadGlyphTables</c>, so a
    /// caller wanting full coverage must request an outline before reading this.</para>
    /// </summary>
    public IReadOnlyList<FontProgramFault> Faults => _faults;
```

Add the private recorder immediately before the first constructor (currently line 228, just above the `/// <summary>Creates embedded font metrics from raw TrueType/OpenType font data</summary>` block):

```csharp
    /// <summary>Records a swallowed parse failure and mirrors it to the log. The record is the
    /// mechanism; the log line is a courtesy, because PdfLogger defaults every category to off.</summary>
    private void RecordFault(FontProgramStage stage, Exception ex)
    {
        _faults.Add(new FontProgramFault(stage, ex.GetType().Name));
        PdfLogger.Log(LogCategory.Text,
            $"[FONT-FAULT] stage={stage} exception={ex.GetType().Name}: {ex.Message}");
    }
```

- [ ] **Step 5: Wire the eleven catch blocks**

Each edit adds `(Exception ex)` and one `RecordFault` call. **Nothing else in any block changes.** Work top-down so line numbers stay predictable, or search by the quoted text.

1. Raw CFF (currently line 262):

```csharp
            catch (Exception ex)
            {
                // CFF parsing failed, try as TrueType below
                RecordFault(FontProgramStage.RawCff, ex);
                _isCffFont = false;
            }
```

2. sfnt directory (currently line 271-272) — the one-line form expands:

```csharp
        try { _sfnt = new FontParser.SfntFont(fontData, faceIndex); }
        catch (Exception ex) { RecordFault(FontProgramStage.SfntDirectory, ex); _sfnt = null; }
```

3. head (currently line 283):

```csharp
            catch (Exception ex)
            {
                RecordFault(FontProgramStage.Head, ex);
                UnitsPerEm = 1000; // Fallback default
            }
```

4. maxp (currently line 302):

```csharp
            catch (Exception ex)
            {
                // MaxP table parse failed
                RecordFault(FontProgramStage.MaxP, ex);
            }
```

5. hhea (currently line 316):

```csharp
            catch (Exception ex)
            {
                // Hhea table parse failed
                RecordFault(FontProgramStage.Hhea, ex);
            }
```

6. hmtx (currently line 331):

```csharp
            catch (Exception ex)
            {
                // Hmtx table parse failed
                RecordFault(FontProgramStage.Hmtx, ex);
            }
```

7. name (currently line 345):

```csharp
            catch (Exception ex)
            {
                // Name table parse failed
                RecordFault(FontProgramStage.Name, ex);
            }
```

8. cmap (currently line 361) — already has `(Exception ex)` and a log line; keep both, add one call:

```csharp
            catch (Exception ex)
            {
                RecordFault(FontProgramStage.Cmap, ex);
                PdfLogger.Log(LogCategory.Text, $"CMAP-PARSE-FAIL: Failed to parse cmap table: {ex.GetType().Name}: {ex.Message}");
                _cmapTable = null;
            }
```

9. `CFF ` table (currently line 394) — **the site that hid the Type1C bug**:

```csharp
            catch (Exception ex)
            {
                // CFF parsing failed, treat as invalid
                RecordFault(FontProgramStage.CffTable, ex);
                _cffTable = null;
                _isCffFont = false;
            }
```

10. Type1 program (currently line 484) — already has `(Exception ex)` and a log line; keep both:

```csharp
        catch (Exception ex)
        {
            RecordFault(FontProgramStage.Type1Program, ex);
            _isType1Font = false;
            IsValid = false;
            PdfLogger.Log(LogCategory.Text, $"[TYPE1] Type1 font parsing exception: {ex.Message}\n{ex.StackTrace}");
        }
```

11. `LoadGlyphTables` (currently line 1401):

```csharp
        catch (Exception ex)
        {
            // If parsing fails, leave tables as null
            RecordFault(FontProgramStage.GlyfLoca, ex);
            _glyphTable = null;
            _locaTable = null;
        }
```

- [ ] **Step 6: Run the new tests**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontProgramFaultTests"
```

Expected: PASS.

If `TruncatedRawCff_RecordsARawCffFault` fails because the truncated program does not throw, adjust the truncation in `TruncatedRawCff()` to be more aggressive (`whole[..12]`) and re-run. Do **not** weaken the assertion — the test's job is to prove a real failure gets recorded.

- [ ] **Step 7: Run the whole engine test suite for regressions**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly"
```

Expected: same pass/fail set as before this task. Any newly failing test means Step 5 changed behaviour somewhere — go back and diff that catch block against the original. **Do not proceed with a regression.**

- [ ] **Step 8: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git add PdfLibrary/Fonts/Embedded/FontProgramFault.cs PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs
git commit -m "feat(fonts): record swallowed font-program parse faults

All eleven catch blocks in EmbeddedFontMetrics now record a
FontProgramFault(stage, exceptionType) instead of failing silently. No
fallback behaviour changes — this is observation only.

The exception message is deliberately excluded: these records get compared
against a committed corpus baseline, and messages vary by runtime and locale.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Corpus walker and the pure diff

**Files:**
- Modify: `PdfLibrary.Tests/Conformance/GwgGosHarness.cs` (add `PatchFiles()` alongside the existing `PdfX4Files()`)
- Create: `PdfLibrary.Tests/Fonts/FontFaultCanary.cs`
- Test: `PdfLibrary.Tests/Fonts/FontFaultCompareTests.cs`

**Interfaces:**
- Consumes from Task 1: `FontProgramStage`, `FontProgramFault`, `EmbeddedFontMetrics.Faults`.
- Produces, relied on by Task 3:
  - `GwgGosHarness.PatchFiles()` → `IEnumerable<string>`, absolute paths, stable ordinal order.
  - `FontFaultCanary.Scan(string pdfPath, string fileKey, SortedDictionary<string, string> into, ref int programsExamined)` — adds zero or more rows.
  - `FontFaultCanary.Compare(IReadOnlyDictionary<string,string> actual, IReadOnlyDictionary<string,string> expected)` → `List<string>` of `NEW`/`CHANGED`/`MISSING` lines.
  - Row shape: key `"<relative/path.pdf>\t<BaseFontName>"`, value `"Stage:ExceptionType"` or `"MetricsNull"`.

- [ ] **Step 1: Write the failing test for the pure diff**

Create `PdfLibrary.Tests/Fonts/FontFaultCompareTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The canary's diff, tested without a corpus. Pure in, pure out — no I/O, no skip semantics. This is
/// the part that decides whether a run is green, so it gets tested directly rather than only through a
/// LocalOnly gate that CI never runs.
/// </summary>
public class FontFaultCompareTests
{
    private static Dictionary<string, string> Map(params (string Key, string Value)[] rows)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in rows) d[key] = value;
        return d;
    }

    [Fact]
    public void IdenticalMaps_ProduceNoProblems()
    {
        var m = Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException"));

        Assert.Empty(FontFaultCanary.Compare(m, m));
    }

    [Fact]
    public void BothEmpty_ProduceNoProblems()
    {
        // The expected steady state once the corpus is clean: no faults, no baseline rows, green.
        Assert.Empty(FontFaultCanary.Compare(Map(), Map()));
    }

    [Fact]
    public void AFaultNotInTheBaseline_IsReportedAsNew()
    {
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException")),
            expected: Map());

        string problem = Assert.Single(problems);
        Assert.StartsWith("  NEW", problem);
        Assert.Contains("a.pdf\tFontA", problem);
        Assert.Contains("CffTable:IndexOutOfRangeException", problem);
    }

    [Fact]
    public void ADifferentFaultForTheSameFont_IsReportedAsChanged()
    {
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(("a.pdf\tFontA", "CffTable:EndOfStreamException")),
            expected: Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException")));

        string problem = Assert.Single(problems);
        Assert.StartsWith("  CHANGED", problem);
        Assert.Contains("IndexOutOfRangeException -> EndOfStreamException", problem);
    }

    [Fact]
    public void ABaselinedFaultThatStoppedHappening_IsReportedAsMissing()
    {
        // A fixed parser is still a baseline change and must be reviewed, not silently absorbed.
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(),
            expected: Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException")));

        string problem = Assert.Single(problems);
        Assert.StartsWith("  MISSING", problem);
        Assert.Contains("a.pdf\tFontA", problem);
    }

    [Fact]
    public void AllThreeKinds_AreReportedTogether()
    {
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(("new.pdf\tF", "Head:EndOfStreamException"), ("same.pdf\tF", "Cmap:X")),
            expected: Map(("gone.pdf\tF", "Head:X"), ("same.pdf\tF", "Cmap:X")));

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("  NEW") && p.Contains("new.pdf"));
        Assert.Contains(problems, p => p.StartsWith("  MISSING") && p.Contains("gone.pdf"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCompareTests"
```

Expected: **compile failure** — `FontFaultCanary` does not exist.

- [ ] **Step 3: Add `PatchFiles()` to the harness**

In `PdfLibrary.Tests/Conformance/GwgGosHarness.cs`, add after `PdfX4Files()` (which ends at line 36). Leave `PdfX4Files()` untouched — other tests depend on its `_X4`-only filter.

```csharp
    /// <summary>
    /// Every patch file under the GOS checkout — <c>*/Categories/*/Patches/*.pdf</c>, all PDF/X flavours,
    /// in stable path order. Distinct from <see cref="PdfX4Files"/>, which is deliberately narrowed to
    /// PDF/X-4 for the conformance oracle: the font canary wants maximum font variety, and the x1a/x3
    /// patches embed programs the x4 set does not.
    /// </summary>
    public static IEnumerable<string> PatchFiles()
    {
        if (Root is null)
            yield break;

        foreach (string path in Directory.EnumerateFiles(Root, "*.pdf", SearchOption.AllDirectories)
                                         .OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(Root, path).Replace('\\', '/');
            if (rel.Contains("/Categories/") && rel.Contains("/Patches/"))
                yield return path;
        }
    }

    /// <summary>Corpus-root-relative, forward-slashed path — the stable key form for committed
    /// baselines. Absolute paths differ per machine and must never reach a baseline file.</summary>
    public static string RelativeKey(string absolutePath) =>
        Root is null ? Path.GetFileName(absolutePath)
                     : Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');
```

- [ ] **Step 4: Write the canary walker**

Create `PdfLibrary.Tests/Fonts/FontFaultCanary.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Walks a PDF's fonts and collects every swallowed font-program parse fault, keyed for a committed
/// baseline. Split out from the gate test so the diff is testable without a corpus.
/// <para>The resource walk (pages, then Form XObjects, with a depth cap and object-number cycle guards)
/// mirrors <c>Type0FallbackAuditTests</c>, which established the shape.</para>
/// </summary>
internal static class FontFaultCanary
{
    /// <summary>Emitted when GetEmbeddedMetrics returns null despite a FontFile being present. That is
    /// the OTHER swallow — TrueTypeFont.cs's <c>catch { return null; }</c> destroys the metrics object
    /// before any Faults list can be read, so the canary has to name it from outside.</summary>
    public const string MetricsNullValue = "MetricsNull";

    /// <summary>Diffs freshly collected faults against the committed baseline. Pure — no corpus, no I/O,
    /// no skip semantics (see <c>FontFaultCompareTests</c>).</summary>
    public static List<string> Compare(
        IReadOnlyDictionary<string, string> actual,
        IReadOnlyDictionary<string, string> expected)
    {
        var problems = new List<string>();
        foreach (KeyValuePair<string, string> kv in actual)
        {
            if (!expected.TryGetValue(kv.Key, out string? want))
                problems.Add($"  NEW      {kv.Key} = {kv.Value}");
            else if (want != kv.Value)
                problems.Add($"  CHANGED  {kv.Key}: {want} -> {kv.Value}");
        }
        foreach (string key in expected.Keys.Where(k => !actual.ContainsKey(k)))
            problems.Add($"  MISSING  {key}");
        return problems;
    }

    /// <summary>
    /// Opens one PDF, walks every font reachable from a page's resources, and adds a row for each
    /// embedded program that faulted. Clean programs add nothing.
    /// </summary>
    /// <param name="pdfPath">Absolute path to the PDF.</param>
    /// <param name="fileKey">Corpus-relative key for this file; forms the first half of each row key.</param>
    /// <param name="into">Row sink, keyed <c>"&lt;fileKey&gt;\t&lt;BaseFont&gt;"</c>.</param>
    /// <param name="programsExamined">Incremented once per embedded program actually inspected —
    /// the coverage counter the gate asserts on.</param>
    public static void Scan(string pdfPath, string fileKey, SortedDictionary<string, string> into,
        ref int programsExamined)
    {
        using PdfDocument doc = PdfDocument.Load(pdfPath);
        var seenFonts = new HashSet<int>();
        var seenRes = new HashSet<int>();
        var examined = 0;

        for (var i = 0; i < doc.PageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            Walk(page?.GetResources(), doc, into, fileKey, seenFonts, seenRes, 0, ref examined);
        }

        programsExamined += examined;
    }

    private static void Walk(PdfResources? res, PdfDocument doc, SortedDictionary<string, string> into,
        string fileKey, HashSet<int> seenFonts, HashSet<int> seenRes, int depth, ref int examined)
    {
        if (res is null || depth > 12) return;
        if (res.Dictionary.IsIndirect && !seenRes.Add(res.Dictionary.ObjectNumber)) return;

        if (res.GetFonts() is { } fonts)
            foreach (PdfObject f in fonts.Values)
                Inspect(f, doc, into, fileKey, seenFonts, ref examined);

        if (res.GetXObjects() is { } xobjs)
            foreach (PdfObject x in xobjs.Values)
                if (Deref(x, doc) is PdfStream { Dictionary: { } sd } &&
                    (sd.Get("Subtype") as PdfName)?.Value == "Form" &&
                    Deref(sd.Get("Resources"), doc) is PdfDictionary rd)
                    Walk(new PdfResources(rd, doc), doc, into, fileKey, seenFonts, seenRes, depth + 1, ref examined);
    }

    private static void Inspect(PdfObject fontObj, PdfDocument doc, SortedDictionary<string, string> into,
        string fileKey, HashSet<int> seenFonts, ref int examined)
    {
        if (fontObj is PdfIndirectReference r && !seenFonts.Add(r.ObjectNumber)) return;
        if (Deref(fontObj, doc) is not PdfDictionary font) return;
        if (!HasEmbeddedProgram(font, doc)) return; // no font file: nothing to parse, nothing to report

        string baseFont = (font.Get("BaseFont") as PdfName)?.Value ?? "(no BaseFont)";
        string key = $"{fileKey}\t{baseFont}";

        if (PdfFont.Create(font, doc) is not { } pdfFont) return;
        examined++;

        EmbeddedFontMetrics? metrics;
        try { metrics = pdfFont.GetEmbeddedMetrics(); }
        catch (Exception ex) { into[key] = $"Throw:{ex.GetType().Name}"; return; }

        if (metrics is null)
        {
            into[key] = MetricsNullValue;
            return;
        }

        // Force the lazy loca/glyf stage so a GlyfLoca fault can be seen. Faults is append-only, so this
        // must happen BEFORE reading it. The return value is irrelevant — we want the side effect.
        try { metrics.GetGlyphOutline(0); }
        catch { /* an outline throw is not a parse fault; the recorded Faults are the signal */ }

        if (metrics.Faults.Count == 0) return;

        // One row per font. Multiple faults on one program join with '+' so a font stays a single,
        // greppable baseline line.
        into[key] = string.Join("+", metrics.Faults.Select(f => f.ToString()));
    }

    /// <summary>True when the font (or a Type0's descendant CIDFont) declares any FontFile stream. The
    /// descriptor is reached by dictionary walk rather than PdfFont.GetDescriptor because a Type0's
    /// descriptor lives on its descendant, which PdfFont does not expose.</summary>
    private static bool HasEmbeddedProgram(PdfDictionary font, PdfDocument doc)
    {
        PdfDictionary? descriptorHolder = font;

        if ((font.Get("Subtype") as PdfName)?.Value == "Type0")
        {
            if (Deref(font.Get("DescendantFonts"), doc) is not PdfArray { Count: > 0 } df) return false;
            if (Deref(df[0], doc) is not PdfDictionary cid) return false;
            descriptorHolder = cid;
        }

        if (Deref(descriptorHolder.Get("FontDescriptor"), doc) is not PdfDictionary fd) return false;

        return fd.Get("FontFile") is not null
               || fd.Get("FontFile2") is not null
               || fd.Get("FontFile3") is not null;
    }

    private static PdfObject? Deref(PdfObject? obj, PdfDocument doc) =>
        obj is PdfIndirectReference r ? doc.ResolveReference(r) : obj;
}
```

- [ ] **Step 5: Run the diff tests**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCompareTests"
```

Expected: PASS, 6 tests.

If `FontFaultCanary.cs` does not compile, the likely causes are the `Deref` helper's signature or `PdfResources`' constructor. Both are copied from `PdfLibrary.Tests/Fonts/Type0FallbackAuditTests.cs` (`Deref` near line 380, `WalkResourcesForFonts` at line 229) — check that file for the exact working forms and match them.

- [ ] **Step 6: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git add PdfLibrary.Tests/Conformance/GwgGosHarness.cs PdfLibrary.Tests/Fonts/FontFaultCanary.cs PdfLibrary.Tests/Fonts/FontFaultCompareTests.cs
git commit -m "test(fonts): add the font-fault corpus walker and its pure diff

GwgGosHarness gains PatchFiles() (all PDF/X flavours, unlike the X4-only
oracle) and RelativeKey(). FontFaultCanary walks pages and Form XObjects,
forces the lazy glyf/loca stage, and emits one baseline row per faulting
font. Compare() is pure and directly tested; the gate that uses it lands next.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The gate and its baseline

**Files:**
- Create: `PdfLibrary.Tests/Fonts/FontFaultCanaryTests.cs`
- Create: `PdfLibrary.Tests/Fonts/font-program-fault-baseline.txt` (generated in Step 3, then committed)

**Interfaces:**
- Consumes from Task 2: `GwgGosHarness.PatchFiles()`, `GwgGosHarness.RelativeKey(string)`, `GwgGosHarness.IsAvailable`, `FontFaultCanary.Scan(...)`, `FontFaultCanary.Compare(...)`.
- Produces: nothing downstream. This is the terminal deliverable.

- [ ] **Step 1: Write the gate**

Create `PdfLibrary.Tests/Fonts/FontFaultCanaryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLibrary.Tests.Conformance;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Corpus canary: no embedded font program in the GWG corpus may start failing to parse without someone
/// deciding that is acceptable and re-pinning the baseline.
/// <para>This exists because the failure it watches for is invisible by construction. When Type1Table
/// threw on GWG090's embedded Type1C program, EmbeddedFontMetrics set <c>_isCffFont = false</c> and the
/// font rendered through the TrueType path against a <c>glyf</c> table it did not have. Nothing was red.
/// The bug survived months of green runs and was found only by reading bytes.</para>
/// <para>Baseline, not zero-tolerance: a known-and-accepted fault is a row, so this stays usable if it is
/// ever pointed at a corpus containing deliberately-broken programs. Regenerate with
/// <c>PDFLIBRARY_FONT_FAULT_REGEN=1</c>; the run rewrites the baseline and still fails, so a
/// regeneration can never be mistaken for a pass.</para>
/// <para>LocalOnly: needs the sibling <c>../gwg-gos</c> checkout, absent on CI. The diff it relies on is
/// unit-tested separately in <c>FontFaultCompareTests</c>, which does run on CI.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class FontFaultCanaryTests
{
    private const string BaselineFileName = "font-program-fault-baseline.txt";
    private const string RegenVariable = "PDFLIBRARY_FONT_FAULT_REGEN";

    private readonly ITestOutputHelper _out;
    public FontFaultCanaryTests(ITestOutputHelper o) => _out = o;

    /// <summary>Walk up to the test project directory so regeneration writes the source file, not the
    /// copy under bin/.</summary>
    private static string BaselinePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PdfLibrary.Tests.csproj")))
                return Path.Combine(dir.FullName, "Fonts", BaselineFileName);
        throw new InvalidOperationException("could not locate the PdfLibrary.Tests project directory");
    }

    private static Dictionary<string, string> ReadBaseline(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return map;
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int tab = line.LastIndexOf('\t');
            if (tab <= 0) continue;
            map[line[..tab]] = line[(tab + 1)..];
        }
        return map;
    }

    [Fact]
    public void No_embedded_font_program_faults_outside_the_baseline()
    {
        string path = BaselinePath();
        Dictionary<string, string> expected = ReadBaseline(path);
        var files = new List<string>(GwgGosHarness.PatchFiles());

        // Read the baseline BEFORE deciding to skip. A populated baseline sitting next to zero discovered
        // files means the gate is checking nothing — that must fail loudly, not skip quietly.
        if (files.Count == 0)
        {
            Assert.True(expected.Count == 0,
                $"gwg-gos corpus not present, but {BaselineFileName} has {expected.Count} committed " +
                "entries. The gate is NOT checking any of them — this must fail, not skip. Restore the " +
                "corpus (or set GWG_GOS) before trusting a green run of this test.");
            Assert.Skip("gwg-gos corpus not present (LocalOnly); baseline is also empty.");
            return;
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var programsExamined = 0;
        var unreadable = 0;

        foreach (string file in files)
        {
            try { FontFaultCanary.Scan(file, GwgGosHarness.RelativeKey(file), actual, ref programsExamined); }
            catch (Exception ex)
            {
                unreadable++;
                _out.WriteLine($"unreadable: {GwgGosHarness.RelativeKey(file)}: {ex.GetType().Name}");
            }
        }

        bool regen = Environment.GetEnvironmentVariable(RegenVariable) == "1";
        if (regen)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Embedded font-program parse faults across the GWG corpus.");
            sb.AppendLine("# key = <corpus-relative path>\\t<BaseFont>, value = Stage:ExceptionType");
            sb.AppendLine("# (multiple faults on one program join with '+'; 'MetricsNull' means the font");
            sb.AppendLine("# class swallowed the failure and returned null before any fault list existed).");
            sb.AppendLine("# An EMPTY body is the healthy state: every embedded program parsed cleanly.");
            sb.AppendLine($"# {programsExamined} programs examined across {files.Count} files ({unreadable} unreadable).");
            sb.AppendLine($"# {actual.Count} faulting fonts. Regenerate with {RegenVariable}=1 and review the diff.");
            foreach (KeyValuePair<string, string> kv in actual)
                sb.AppendLine($"{kv.Key}\t{kv.Value}");
            File.WriteAllText(path, sb.ToString());

            Assert.Fail($"{RegenVariable}=1: baseline rewritten with {actual.Count} entries at {path} " +
                        $"({programsExamined} programs examined). Review the diff and re-run without the variable set.");
        }

        // The canary-of-the-canary. With a healthy (empty) baseline, an empty result set is
        // indistinguishable from a walk that silently stopped finding fonts — so coverage is asserted
        // directly. This is the guard doing the real work; note it deliberately replaces the GWG hash
        // gate's `expected.Count > 0` assertion, which cannot apply when zero faults is the goal state.
        Assert.True(programsExamined > 0,
            $"the canary examined ZERO embedded font programs across {files.Count} corpus files. The " +
            "resource walk has regressed, or the corpus is not what it was. An empty result here would " +
            "otherwise read as a clean pass.");

        List<string> problems = FontFaultCanary.Compare(actual, expected);

        _out.WriteLine($"{programsExamined} programs examined across {files.Count} files " +
                       $"({unreadable} unreadable), {actual.Count} faulting, {expected.Count} baselined, " +
                       $"{problems.Count} differences.");

        Assert.True(problems.Count == 0,
            $"embedded font-program faults diverged from {BaselineFileName}:\n" + string.Join("\n", problems) +
            $"\n\nA NEW row means a font program that used to parse no longer does — check the parser " +
            $"before accepting it. A MISSING row means one started parsing, which is usually good news " +
            $"and still needs re-pinning. If these changes are intended, regenerate with " +
            $"{RegenVariable}=1 and review the diff.");
    }
}
```

- [ ] **Step 2: Run without a baseline to confirm the corpus is found**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCanaryTests"
```

Expected: either PASS (corpus found, zero faults, empty baseline — the healthy outcome) or a failure listing `NEW` rows. A **skip** means the corpus was not located: check that `C:\Users\jorda\RiderProjects\gwg-gos` exists, or set `GWG_GOS` to it, then re-run. Do not proceed on a skip — the whole task is unverified until the walk runs.

Read the test output line: `N programs examined across M files`. `M` should be 51. If `N` is 0, stop and fix the walk before continuing.

- [ ] **Step 3: Generate the baseline**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
PDFLIBRARY_FONT_FAULT_REGEN=1 dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCanaryTests"
```

Expected: **FAIL** with `PDFLIBRARY_FONT_FAULT_REGEN=1: baseline rewritten...`. That failure is the designed behaviour, not a problem.

- [ ] **Step 4: Review the generated baseline**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
cat PdfLibrary.Tests/Fonts/font-program-fault-baseline.txt
```

Read every row. For each one, decide whether it is a known-and-accepted fault or a live bug worth reporting. **An empty body is the expected and healthy result** — `6564363` fixed the CFF charset defect, so the corpus should parse clean. If rows appear, they are new information: note them in the commit message rather than silently baselining them.

Confirm the header's `programs examined` count is greater than zero and the file count reads 51.

- [ ] **Step 5: Run again without the regen variable**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCanaryTests"
```

Expected: PASS.

- [ ] **Step 6: Verify the gate actually catches something**

Prove the canary is not green-by-construction. Temporarily add a bogus row to the baseline:

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
printf 'sentinel.pdf\tSentinelFont\tCffTable:SentinelException\n' >> PdfLibrary.Tests/Fonts/font-program-fault-baseline.txt
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCanaryTests"
```

Expected: **FAIL** with `MISSING sentinel.pdf`. If it passes, the gate is not reading the baseline — fix `BaselinePath()` before continuing.

Then remove the sentinel line and confirm green again:

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git checkout PdfLibrary.Tests/Fonts/font-program-fault-baseline.txt 2>/dev/null || sed -i '/SentinelFont/d' PdfLibrary.Tests/Fonts/font-program-fault-baseline.txt
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCanaryTests"
```

Expected: PASS.

- [ ] **Step 7: Confirm CI-visible tests still pass**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly"
```

Expected: PASS, and the canary itself does not appear in the run (it is `LocalOnly`).

- [ ] **Step 8: Commit**

The baseline is a `.txt` and is not caught by the `*.pdf` ignore, but confirm it is staged — the gate is worthless uncommitted.

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git add PdfLibrary.Tests/Fonts/FontFaultCanaryTests.cs PdfLibrary.Tests/Fonts/font-program-fault-baseline.txt
git status --short
git commit -m "test(fonts): gate embedded font-program parse faults against a baseline

Walks every embedded program in the GWG corpus and diffs its swallowed parse
faults against a committed baseline. LocalOnly, like the render-hash gates;
the diff itself is unit-tested and does run on CI.

Two guards: a populated baseline with zero discovered files fails rather than
skips, and programsExamined > 0 is asserted directly — with zero faults as the
goal state, an empty result set would otherwise be indistinguishable from a
walk that stopped finding fonts.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Done when

- `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly"` passes with the same result set as before Task 1.
- `dotnet test ... --filter "FullyQualifiedName~FontFaultCanaryTests"` passes locally against the GWG corpus, reporting a non-zero `programs examined`.
- The sentinel check in Task 3 Step 6 was observed to fail, then observed to pass again after removal.
- Three commits on the engine's `master`, working tree clean.

## Deliberately not in this plan

Recorded so a reviewer does not read them as omissions. All are noted in the spec's closing section.

- Making `TrueTypeFont.cs:113`, `Type1Font.cs:212`, or `Type0Font.cs:182` honour `IsValid`. The canary measures the consequences first; changing behaviour is separate work.
- Any Preflight finding or Pellucid UI surface.
- Pointing the canary at the veraPDF, PDF/UA, or BFO corpora.
- The `Type1Table` positional-Encoding-read bug and the ladder step-1 TOCTOU.

### One deviation from the spec

The spec asked for per-stage unit tests covering "a truncated sfnt directory, corrupt `head`, corrupt
`cmap`". The plan covers `RawCff` and `SfntDirectory` — the two stages an in-memory fixture can reach
today — plus the clean-program and unchanged-fallback guards.

`Head` and `Cmap` are not covered, because reaching them needs a *structurally valid sfnt* with one
corrupted table, and no sfnt fixture builder exists in the repo. `MinimalCff` builds bare CFF only.
Hand-rolling an sfnt builder inside this plan risks a fixture that is wrong in a way that makes the
test pass for the wrong reason — worse than no test.

The recording mechanism is identical at all eleven sites and is proven by the two covered stages, so
this is a coverage gap rather than a correctness risk. If per-stage coverage is wanted later, the
right move is an `MinimalSfnt` fixture builder next to `MinimalCff`, shared the same way — its own
small piece of work.
