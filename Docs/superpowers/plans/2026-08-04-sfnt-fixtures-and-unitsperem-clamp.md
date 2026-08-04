# MinimalSfnt Fixtures and the UnitsPerEm Clamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take per-stage font-fault coverage from 2 of 11 stages to 9 of 11, then clamp a zero `UnitsPerEm` to 1000 with a recorded fault so it stops poisoning seven unguarded division sites.

**Architecture:** A synthetic sfnt builder (`MinimalSfnt`) emits a table directory over arbitrary payloads — deliberately not a valid-font builder. Deliberately-broken payloads then drive each parse stage into its `catch`. With that in place, `FontProgramFault.ExceptionType` is renamed `Detail` (a zero `UnitsPerEm` is a fault with no exception), and all three `UnitsPerEm` assignment sites route through one shared fallback helper.

**Tech Stack:** C# / .NET 10, xunit.v3 3.2.2, `PdfLibrary.Tests`.

**Specs:**
- `Docs/superpowers/specs/2026-08-04-minimal-sfnt-fixture-builder-design.md`
- `Docs/superpowers/specs/2026-08-04-units-per-em-zero-design.md`

## Global Constraints

- **Repo:** `C:\Users\jorda\RiderProjects\PdfLibrary`. Nothing here touches Pellucid.
- **Branch first.** `master` is pushed and clean. Create `feat/sfnt-fixtures-unitsperem` before Task 1; do not implement on `master`.
- **No font binary is vendored.** The synthetic builder is the fixture; real fonts were only ever the oracle. This is the Slice 2 decision and it stays untouched.
- **All new tests run on CI** — no `[Trait("Category", "LocalOnly")]` on anything in this plan. The corpus canary is local-only, so per-stage coverage is the part CI can actually enforce.
- **Tasks 1–3 change no engine behaviour.** Task 4 is the only behaviour change in this plan, and it is scoped to the `UnitsPerEm <= 0` case alone.
- **Every fault test asserts two things:** the stage is recorded, *and* the documented fallback is unchanged. The premise of the whole diagnostics line of work is that recording changed no behaviour; a test that only checks the fault does not defend that premise.
- **Exact observed values** (from the probe run 2026-08-04, against real `Alef-Regular.ttf` and synthetic sfnts): a table whose declared length is 4 throws for `head`, `maxp`, `hhea`, `name`. A 4-byte `cmap` does **not** throw — `cmap` needs `0xFF`-filled content. Every stage reports `ArgumentException`.

---

### Task 1: MinimalSfnt builder and five per-stage fault tests

**Files:**
- Create: `PdfLibrary.Tests/Fonts/Embedded/MinimalSfnt.cs`
- Modify: `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs` (append tests; leave the seven existing ones untouched)

**Interfaces:**
- Consumes: `FontProgramStage`, `EmbeddedFontMetrics.Faults` (both already on `master`).
- Produces, relied on by Tasks 2 and 4:
  - `MinimalSfnt.Build(params (string Tag, byte[] Data)[] tables)` → `byte[]`
  - `MinimalSfnt.TooShort()` → `byte[]` (4 zero bytes)
  - `MinimalSfnt.Garbage(int length)` → `byte[]` (`length` bytes of `0xFF`)
  - `MinimalSfnt.ZeroHead()` → `byte[]` (54 zero bytes — parses, yields `UnitsPerEm` 0)
  - `MinimalSfnt.Maxp(ushort numGlyphs)` → `byte[]` (6-byte maxp: 4-byte version + `numGlyphs`)

- [ ] **Step 1: Write the failing tests**

Append to `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs`, inside the existing class, after the last test:

```csharp
    // ---- Per-stage coverage via synthetic sfnts (see MinimalSfnt) ----------------------------
    // Each asserts BOTH that the stage is recorded AND that the documented fallback is unchanged.
    // The second assertion is the load-bearing one: this whole mechanism is only defensible if
    // recording a fault changed no behaviour.

    [Fact]
    public void ShortHeadTable_RecordsAHeadFaultAndKeepsTheUnitsPerEmFallback()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("head", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Head);
        Assert.Equal(1000, metrics.UnitsPerEm); // documented "Fallback default"
        Assert.False(metrics.IsValid);
    }

    [Fact]
    public void ShortMaxpTable_RecordsAMaxPFaultAndLeavesNumGlyphsZero()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("maxp", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.MaxP);
        Assert.Equal(0, metrics.NumGlyphs);
    }

    [Fact]
    public void ShortHheaTable_RecordsAnHheaFaultAndLeavesMetricsAtZero()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("hhea", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Hhea);
        Assert.Equal(0, metrics.Ascender);
        Assert.Equal(0, metrics.Descender);
        Assert.Equal(0, metrics.NumberOfHMetrics);
    }

    [Fact]
    public void ShortNameTable_RecordsANameFaultAndLeavesNamesNull()
    {
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("name", MinimalSfnt.TooShort())));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Name);
        Assert.Null(metrics.FamilyName);
        Assert.Null(metrics.PostScriptName);
    }

    [Fact]
    public void GarbageCmapTable_RecordsACmapFaultAndLeavesLookupsAtNotdef()
    {
        // A 4-byte cmap returns CLEANLY — the reader never runs off the end. cmap only throws on
        // garbage CONTENT. head/maxp/hhea/name fail the opposite way. Do not "simplify" this to
        // TooShort(): the test would then pass while asserting nothing.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("cmap", MinimalSfnt.Garbage(64))));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Cmap);
        Assert.False(metrics.HasCmapTable);
        Assert.Equal(0, metrics.GetGlyphId(65));
    }

    [Fact]
    public void ShortCmapTable_RecordsNothing_PinningTheAsymmetry()
    {
        // The counterpart guard for the comment above. If a future parser change makes a short cmap
        // throw, this test goes red and tells the next reader the asymmetry is gone.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("cmap", MinimalSfnt.TooShort())));

        Assert.DoesNotContain(metrics.Faults, f => f.Stage == FontProgramStage.Cmap);
    }

    [Fact]
    public void BrokenLocaTable_RecordsAGlyfLocaFaultOnlyAfterAnOutlineIsRequested()
    {
        // loca/glyf load lazily, so the fault cannot exist until something asks for an outline.
        // Requires a parseable head (all-zero parses) and a maxp with NumGlyphs > 0, or
        // LoadGlyphTables returns before reaching the loca reader.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(
            ("head", MinimalSfnt.ZeroHead()),
            ("maxp", MinimalSfnt.Maxp(4)),
            ("loca", MinimalSfnt.TooShort()),
            ("glyf", MinimalSfnt.Garbage(16))));

        Assert.DoesNotContain(metrics.Faults, f => f.Stage == FontProgramStage.GlyfLoca);

        Assert.Null(metrics.GetGlyphOutline(0));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.GlyfLoca);
    }
```

- [ ] **Step 2: Run to verify they fail**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontProgramFaultTests"
```

Expected: **compile failure** — `MinimalSfnt` does not exist.

- [ ] **Step 3: Write the builder**

Create `PdfLibrary.Tests/Fonts/Embedded/MinimalSfnt.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Builds synthetic sfnt programs: a header, a table directory, and whatever payloads the caller
/// hands over. Deliberately NOT a valid-font builder — it builds a DIRECTORY OVER PAYLOADS, and the
/// payloads are meant to be broken. A caller wanting a program that parses cleanly should use
/// <c>MinimalCff</c> or the corpus, not this.
/// <para>Validated against reality before being trusted: corrupting a real TrueType font
/// (Alef-Regular) one table at a time produced the same stage and the same exception type as the
/// synthetic equivalents. That check is why this file exists instead of a vendored font binary.</para>
/// <para>Not linked into FontParser.Tests the way MinimalCff is. MinimalCff earned that because
/// parser-level and metrics-level charset tests needed the same fixtures; nothing outside this
/// assembly needs this one yet. Add the Compile/Link item when a second consumer appears.</para>
/// </summary>
internal static class MinimalSfnt
{
    /// <summary>A table too short for its reader — the shape that throws for head, maxp, hhea, name.
    /// NOT the shape that throws for cmap, which returns cleanly when short.</summary>
    public static byte[] TooShort() => new byte[4];

    /// <summary>A table of plausible size but garbage content — the shape cmap needs.</summary>
    public static byte[] Garbage(int length)
    {
        var b = new byte[length];
        Array.Fill(b, (byte)0xFF);
        return b;
    }

    /// <summary>A 54-byte all-zero head. Parses SUCCESSFULLY and yields UnitsPerEm 0 — the defect
    /// Task 4 clamps. Used here because a parseable head is a precondition for reaching the lazy
    /// loca/glyf stage at all.</summary>
    public static byte[] ZeroHead() => new byte[54];

    /// <summary>A 6-byte maxp (version 0.5 + numGlyphs). NumGlyphs must be non-zero or
    /// LoadGlyphTables returns before it reaches the loca reader.</summary>
    public static byte[] Maxp(ushort numGlyphs) =>
        [0x00, 0x00, 0x50, 0x00, (byte)(numGlyphs >> 8), (byte)numGlyphs];

    /// <summary>Header + directory + payloads. Tables are sorted by tag, as the format requires.
    /// Checksums are written as zero; nothing in the reader validates them.</summary>
    public static byte[] Build(params (string Tag, byte[] Data)[] tables)
    {
        Array.Sort(tables, (a, b) => string.CompareOrdinal(a.Tag, b.Tag));

        var data = new List<byte>();
        U32(data, 0x00010000);        // sfntVersion: TrueType outlines
        U16(data, tables.Length);
        U16(data, 0); U16(data, 0); U16(data, 0); // searchRange/entrySelector/rangeShift: unread

        int offset = 12 + tables.Length * 16;
        foreach ((string tag, byte[] payload) in tables)
        {
            data.AddRange(Encoding.ASCII.GetBytes(tag));
            U32(data, 0);             // checksum: not validated
            U32(data, offset);
            U32(data, payload.Length);
            offset += payload.Length;
        }

        foreach ((_, byte[] payload) in tables) data.AddRange(payload);
        return data.ToArray();
    }

    private static void U16(List<byte> d, int v) { d.Add((byte)(v >> 8)); d.Add((byte)v); }

    private static void U32(List<byte> d, int v)
    {
        d.Add((byte)(v >> 24)); d.Add((byte)(v >> 16)); d.Add((byte)(v >> 8)); d.Add((byte)v);
    }
}
```

- [ ] **Step 4: Run to verify they pass**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontProgramFaultTests"
```

Expected: PASS, 14 tests (7 existing + 7 new).

If `BrokenLocaTable_...` fails because no `GlyfLoca` fault appears, the cause is almost certainly that `LoadGlyphTables` returned early — it bails when `locaData is null || _headTable is null || NumGlyphs == 0`. Confirm the `head` and `maxp` payloads actually parsed by asserting `metrics.UnitsPerEm == 0` and `metrics.NumGlyphs == 4` in a scratch run. Do **not** weaken the assertion to make it pass.

- [ ] **Step 5: Run the full CI-visible suite**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly"
```

Expected: 2842 passed, 0 failed (2835 on `master` + 7 new).

- [ ] **Step 6: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git add PdfLibrary.Tests/Fonts/Embedded/MinimalSfnt.cs PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs
git commit -m "test(fonts): add MinimalSfnt and per-stage fault coverage

Covers Head, MaxP, Hhea, Name, Cmap and GlyfLoca. Each test asserts the stage
is recorded AND the documented fallback is unchanged.

The builder was validated against a real font before being trusted: corrupting
Alef-Regular one table at a time produced the same stage and exception type as
the synthetic equivalents. No font binary is vendored.

Pins one asymmetry that cost a probe cycle to find: a SHORT cmap returns
cleanly and only garbage CONTENT throws, the opposite of head/maxp/hhea/name.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: CffTable stage coverage

**Files:**
- Modify: `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs`

**Interfaces:**
- Consumes: `MinimalSfnt.Build` from Task 1; `MinimalCff.Build(int? charsetOperand, int numGlyphs, ushort[]? customCharsetSids = null)` from `CffTestFixtures`.
- Produces: nothing downstream.

This is the stage that hid the Type1C bug, so it gets its own task rather than riding along with Task 1 — a reviewer should be able to reject it independently.

- [ ] **Step 1: Write the failing test**

Append inside the same class:

```csharp
    [Fact]
    public void BrokenCffTableInsideAnSfnt_RecordsACffTableFaultAndFallsBackToNonCff()
    {
        // The stage that hid the Type1C charset bug for months. An OpenType/CFF wrapper whose
        // 'CFF ' payload is truncated: the sfnt directory parses, the CFF parser throws, and the
        // font silently becomes "not a CFF font" — after which GlyphPathService sends it down the
        // TrueType path to read a glyf table it does not have, and draws nothing.
        byte[] wholeCff = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        byte[] truncatedCff = wholeCff[..(wholeCff.Length / 2)];

        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("CFF ", truncatedCff)));

        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.CffTable);
        Assert.False(metrics.IsCffFont);      // the silent-success fallback, unchanged
        Assert.False(metrics.IsValid);
        Assert.Null(metrics.GetCffGlyphOutlineDirect(0));
    }

    [Fact]
    public void IntactCffTableInsideAnSfnt_RecordsNothing()
    {
        // Scope guard: the fault must be caused by the breakage, not by the sfnt wrapper itself.
        // Without this, the test above would pass even if MinimalSfnt produced a wrapper the CFF
        // reader could never parse — the fixture-passes-for-the-wrong-reason failure mode.
        byte[] wholeCff = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);

        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("CFF ", wholeCff)));

        Assert.DoesNotContain(metrics.Faults, f => f.Stage == FontProgramStage.CffTable);
        Assert.True(metrics.IsCffFont);
    }
```

- [ ] **Step 2: Run to verify they fail**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~BrokenCffTableInsideAnSfnt|FullyQualifiedName~IntactCffTableInsideAnSfnt"
```

Expected: FAIL. The tag is `"CFF "` with a trailing space — a 4-character tag. If `Build` emits three bytes the directory is corrupt and you will get a `SfntDirectory` fault instead; that is the first thing to check.

- [ ] **Step 3: Make them pass**

No production change should be needed — the `CFF ` catch already records. If `IntactCffTableInsideAnSfnt_RecordsNothing` fails, the builder is at fault, not the engine: fix `MinimalSfnt`, not `EmbeddedFontMetrics`. Nothing in this plan may change engine behaviour before Task 4.

- [ ] **Step 4: Run to verify they pass**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly"
```

Expected: 2844 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git add PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs
git commit -m "test(fonts): cover the CffTable stage that hid the Type1C bug

A truncated 'CFF ' payload inside a synthetic sfnt, plus the intact-payload
counterpart so the fault is provably caused by the breakage and not by the
wrapper. Coverage is now 9 of 11 stages.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Rename FontProgramFault.ExceptionType to Detail

**Files:**
- Modify: `PdfLibrary/Fonts/Embedded/FontProgramFault.cs:35,38`
- Modify: `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs:59-60`
- Modify: `PdfLibrary.Tests/Fonts/FontFaultCanaryTests.cs:95` (comment text in the baseline header)
- Modify: `PdfLibrary.Tests/Fonts/FontFaultCompareTests.cs:58` (comment text)

**Interfaces:**
- Consumes: nothing.
- Produces, relied on by Task 4: `FontProgramFault(FontProgramStage Stage, string Detail)`.

Sequenced before the clamp so the clamp's diff is about the clamp. Mechanical and complete in one pass — six references total, verified by grep.

- [ ] **Step 1: Rename the field**

In `PdfLibrary/Fonts/Embedded/FontProgramFault.cs`, replace the record declaration and its `ToString`:

```csharp
/// <summary>
/// A single swallowed font-program parse failure: which stage went wrong, and a short stable
/// description of what.
/// <para><c>Detail</c> is an exception type name for a fault that threw (<c>ArgumentException</c>),
/// or a short PascalCase tag for one that did not (<c>UnitsPerEmZero</c>) — a program can be
/// unusable without anything throwing. It is deliberately NOT an exception message: these records
/// are compared against a committed corpus baseline, and messages vary across .NET versions and
/// locales. The message still reaches <c>PdfLogger</c>, which is not committed to anything.</para>
/// </summary>
internal readonly record struct FontProgramFault(FontProgramStage Stage, string Detail)
{
    /// <summary>Stable one-line form for the corpus baseline, e.g. <c>CffTable:ArgumentException</c>.</summary>
    public override string ToString() => $"{Stage}:{Detail}";
}
```

- [ ] **Step 2: Update the four references**

In `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs`, in `FaultsNeverCarryTheExceptionMessage`:

```csharp
        Assert.All(metrics.Faults, f =>
        {
            Assert.False(string.IsNullOrEmpty(f.Detail));
            Assert.DoesNotContain(" ", f.Detail); // a short tag, not a sentence
        });
```

In `PdfLibrary.Tests/Fonts/FontFaultCanaryTests.cs`, the baseline header line:

```csharp
            sb.AppendLine("# key = <corpus-relative path>\\t<BaseFont>, value = Stage:Detail");
```

In `PdfLibrary.Tests/Fonts/FontFaultCompareTests.cs`, the comment in `ADifferentFaultForTheSameFont_IsReportedAsChanged`:

```csharp
        // Both sides carry their stage: a baseline row is only meaningful as Stage:Detail.
```

- [ ] **Step 3: Verify no reference was missed**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
grep -rn "ExceptionType" --include=*.cs . | grep -v "/obj/\|/bin/"
```

Expected: **no output.**

- [ ] **Step 4: Run the full suite**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly"
```

Expected: 2844 passed, 0 failed — a pure rename moves no count.

- [ ] **Step 5: Confirm the baseline is untouched**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git diff --stat PdfLibrary.Tests/Fonts/font-program-fault-baseline.txt
```

Expected: **no output.** The wire format is unchanged, and the baseline body is empty, which is exactly why this rename is being done now rather than later.

- [ ] **Step 6: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git add PdfLibrary/Fonts/Embedded/FontProgramFault.cs PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs PdfLibrary.Tests/Fonts/FontFaultCanaryTests.cs PdfLibrary.Tests/Fonts/FontFaultCompareTests.cs
git commit -m "refactor(fonts): rename FontProgramFault.ExceptionType to Detail

A program can be unusable without anything throwing, so the field needs a name
that covers a non-exception tag. Wire format is unchanged (still Stage:Detail),
and the committed baseline body is empty, so zero committed rows churn — the
cheapest moment this rename will ever have.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Clamp a zero UnitsPerEm and record it

**Files:**
- Modify: `PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs` — add a `RecordFault` string overload and a `UnitsPerEmOrFallback` helper; apply at lines 271, 309, 505
- Modify: `PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs`

**Interfaces:**
- Consumes: `MinimalSfnt.ZeroHead()` (Task 1), `FontProgramFault.Detail` (Task 3).
- Produces: nothing downstream.

**This is the only behaviour change in the plan.** Per the spec's decision, this is Option A — containment. Option B (marking the font invalid) is the recorded end state but is inert until `TrueTypeFont.cs:113`, `Type1Font.cs:212` and `Type0Font.cs:182` honour `IsValid`, which is separate work gated on wider corpus measurement.

- [ ] **Step 1: Write the failing tests**

Append inside the same class:

```csharp
    [Fact]
    public void ZeroHeadTable_ParsesButYieldsUnitsPerEmZero_WhichIsClampedAndRecorded()
    {
        // A 54-byte all-zero head parses SUCCESSFULLY — nothing throws, so before this clamp there
        // was no fault and IsValid stayed true, while UnitsPerEm came out 0.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("head", MinimalSfnt.ZeroHead())));

        Assert.Equal(1000, metrics.UnitsPerEm);
        Assert.Contains(metrics.Faults, f => f.Stage == FontProgramStage.Head && f.Detail == "UnitsPerEmZero");
    }

    [Fact]
    public void AClampedUnitsPerEm_MakesScaleToUserUnitsFinite()
    {
        // The actual harm, asserted directly rather than inferred from the property. Seven
        // production sites divide by UnitsPerEm in double arithmetic — no DivideByZeroException,
        // just Infinity propagating into text positioning and into written /Widths arrays.
        var metrics = new EmbeddedFontMetrics(MinimalSfnt.Build(("head", MinimalSfnt.ZeroHead())));

        double scaled = metrics.ScaleToUserUnits(500, 12.0);

        Assert.True(double.IsFinite(scaled), $"expected a finite scale, got {scaled}");
    }

    [Fact]
    public void AHealthyProgram_IsNotClampedAndRecordsNoUnitsPerEmFault()
    {
        // Scope guard: the clamp must fire on zero alone, never blanket.
        var metrics = new EmbeddedFontMetrics(MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        Assert.Equal(1000, metrics.UnitsPerEm); // from the CFF FontMatrix default, not the clamp
        Assert.DoesNotContain(metrics.Faults, f => f.Detail == "UnitsPerEmZero");
    }
```

- [ ] **Step 2: Run to verify they fail**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~UnitsPerEm"
```

Expected: `ZeroHeadTable_...` fails with `UnitsPerEm` 0 vs 1000; `AClampedUnitsPerEm_...` fails on a non-finite value. `AHealthyProgram_...` should already pass — it is the scope guard.

- [ ] **Step 3: Add the RecordFault overload**

In `PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs`, immediately after the existing `RecordFault(FontProgramStage, Exception)`:

```csharp
    /// <summary>Records a fault that did NOT throw — a program can be unusable without any reader
    /// raising. <paramref name="detail"/> must be a short stable PascalCase tag; it lands in a
    /// committed baseline, so it must not carry runtime- or locale-varying text.</summary>
    private void RecordFault(FontProgramStage stage, string detail)
    {
        _faults.Add(new FontProgramFault(stage, detail));
        PdfLogger.Log(LogCategory.Text, $"[FONT-FAULT] stage={stage} detail={detail}");
    }
```

- [ ] **Step 4: Add the shared fallback helper**

Immediately after the new overload:

```csharp
    /// <summary>
    /// The single place a parsed units-per-em becomes the value the rest of the engine divides by.
    /// Zero is treated exactly as a missing head: fall back to 1000 and record it.
    /// <para>Seven production sites divide by <see cref="UnitsPerEm"/> in double arithmetic, so a
    /// zero raises nothing — it yields Infinity, and two of those sites write it into a produced
    /// PDF. Guarding each site is the pattern that produced this bug's neighbours; it holds until
    /// someone adds an eighth. This is the chokepoint instead.</para>
    /// <para>1000 is chosen for consistency: the two existing fallback paths already answer an
    /// unusable head with 1000, and it is the near-universal convention for CFF. A font whose true
    /// value was 2048 will render at half scale — wrong, but visibly wrong, and now carrying a
    /// fault row that says so.</para>
    /// </summary>
    private ushort UnitsPerEmOrFallback(ushort parsed, FontProgramStage stage)
    {
        if (parsed != 0) return parsed;
        RecordFault(stage, "UnitsPerEmZero");
        return 1000;
    }
```

- [ ] **Step 5: Apply it at all three assignment sites**

Line 271, the raw-CFF FontMatrix path — the cast stays outside the helper so overflow behaviour for a very small `FontMatrix[0]` is bit-identical to today:

```csharp
                    UnitsPerEm = UnitsPerEmOrFallback((ushort)Math.Round(1.0 / fontMatrix[0]), FontProgramStage.RawCff);
```

Line 309, the head-table path:

```csharp
                UnitsPerEm = UnitsPerEmOrFallback(_headTable.UnitsPerEm, FontProgramStage.Head);
```

Line 505, the Type1 path — **not named in the spec; found during planning.** The Type1 parser can report zero the same way:

```csharp
                UnitsPerEm = UnitsPerEmOrFallback((ushort)_type1Parser.UnitsPerEm, FontProgramStage.Type1Program);
```

Leave line 420 alone. It computes a local `calculatedUnitsPerEm` for a log message only and never assigns the property.

- [ ] **Step 6: Run the new tests**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~UnitsPerEm"
```

Expected: PASS.

- [ ] **Step 7: Re-check the Task 1 GlyfLoca test**

`BrokenLocaTable_...` uses `ZeroHead()`, so it now also carries a `Head:UnitsPerEmZero` fault. It asserts with `Contains`, not equality, so it should still pass — but confirm rather than assume, because this is exactly the kind of cross-task interaction that slips through:

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~BrokenLocaTable"
```

Expected: PASS.

- [ ] **Step 8: Run the full CI-visible suite**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly"
```

Expected: 2847 passed, 0 failed.

Any *other* failure is a real regression from the behaviour change — investigate it, do not re-baseline around it.

- [ ] **Step 9: Re-run the corpus canary**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontFaultCanaryTests" --logger "console;verbosity=detailed" 2>&1 | grep -E "programs examined|NEW |Passed!|Failed!"
```

Expected: PASS, still `136 programs examined across 51 files ... 0 faulting`.

If a `NEW` row appears carrying `UnitsPerEmZero`, that is a **real finding**: a GWG font whose units-per-em was silently zero all along. Report it explicitly — do not regenerate the baseline to absorb it without saying so.

- [ ] **Step 10: Commit**

```bash
cd /c/Users/jorda/RiderProjects/PdfLibrary
git add PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs PdfLibrary.Tests/Fonts/Embedded/FontProgramFaultTests.cs
git commit -m "fix(fonts): clamp a zero UnitsPerEm to 1000 and record it

A 54-byte all-zero head parses successfully and yields UnitsPerEm 0. Seven
production sites then divide by it in double arithmetic — no exception, just
Infinity — and two of those sites write the result into a produced PDF.

All three assignment sites now route through one fallback helper, treating zero
exactly as the two existing paths already treat a missing head. Option A from
the spec: containment. Option B (mark invalid) stays the recorded end state,
gated on the three call sites that ignore IsValid.

The Type1 site was not named in the spec; found during planning.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Done when

- `dotnet test PdfLibrary.slnx --filter "Category!=LocalOnly"` is green.
- `PdfLibrary.Tests` reports 2847 passing.
- The corpus canary passes with `136 programs examined`, and any new baseline row was reported rather than silently absorbed.
- `grep -rn "ExceptionType" --include=*.cs .` returns nothing outside `obj/`/`bin/`.
- Four commits on `feat/sfnt-fixtures-unitsperem`, working tree clean.

## Stage coverage after this plan

| Stage | Covered | By |
|---|---|---|
| `SfntDirectory` | yes | already on `master` |
| `RawCff` | yes | already on `master` |
| `Head` | yes | Task 1 |
| `MaxP` | yes | Task 1 |
| `Hhea` | yes | Task 1 |
| `Name` | yes | Task 1 |
| `Cmap` | yes | Task 1 |
| `GlyfLoca` | yes | Task 1 |
| `CffTable` | yes | Task 2 |
| `Type1Program` | **no** | different constructor; needs a malformed PostScript Type1 program |

**9 of 11**, against the spec's stated target of 10 of 11 — see the deviation below.

## Deviations from the specs

**1. Target is 9 of 11, not 10 of 11.** The `MinimalSfnt` spec counted `SfntDirectory`, `RawCff` and the nine sfnt-reachable stages and arrived at 10. The arithmetic was wrong: `Type1Program` and `SfntDirectory` cannot both be non-`Type1Program` gaps. The honest count is 9 covered, `Type1Program` the sole gap.

**2. The `FontMatrix` clamp site is not fixture-tested.** The `UnitsPerEm` spec asked for a test driving `FontMatrix[0]` large enough to round its reciprocal to zero. `Type1Table.FontMatrix` returns `entry?.Operand as List<double>`, and `MinimalCff` emits no `FontMatrix` operator at all — adding one means modifying a file linked into `FontParser.Tests` and hand-encoding CFF real operands, to exercise `FontMatrix[0] > 2`, a shape no real font has and the probe never observed.

The mitigation is structural rather than another fixture: all three sites call the *same* `UnitsPerEmOrFallback` helper, so the path the `head` test exercises is literally the code the other two run. If that helper is ever inlined back into the call sites, this coverage argument dies with it — which is the reason it is written down here.

**3. The Type1 assignment site is new.** Not in either spec; found by grepping every `UnitsPerEm` assignment while planning. Clamped for the same reason as the other two.
