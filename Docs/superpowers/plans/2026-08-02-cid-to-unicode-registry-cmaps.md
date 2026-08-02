# CID→Unicode Registry CMaps (B-1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Type0Font.DecodeCharacter` produces real Unicode for registered Adobe CID collections (Japan1/Korea1/GB1/CNS1) with no `/ToUnicode` — via a new embedded-encoding-CMap parser (code→CID) and four bundled Adobe `*-UCS2` tables (CID→Unicode) — covering the corpus audit's entire 18-row reachable population.

**Architecture:** `CidCMap` (sibling of `ToUnicodeCMap`, parses `codespacerange`/`cidchar`/`cidrange`) + `AdobeCidToUnicode` (lazy, gzip-embedded Adobe UCS2 CMaps, **inverted** at load from the files' Unicode→CID direction into CID→Unicode) + one new step in `Type0Font.DecodeCharacter` between ToUnicode and the glyph-name fallback. Extraction only; rendering, widths, and conformance rules untouched.

**Tech Stack:** C# / .NET (engine repo `C:\Users\jorda\RiderProjects\PDF`), xunit, `GeneratedRegex` parsing idiom per `ToUnicodeCMap`, gzip embedded resources per the ICC-profile precedent in `PdfLibrary.csproj:51-58`.

**Spec:** `Docs/superpowers/specs/2026-08-02-cid-to-unicode-registry-cmaps-design.md`

## Global Constraints

- **Engine repo only** (`C:\Users\jorda\RiderProjects\PDF`, branch master). The one Pellucid-side change is the tracker flip in the final task. No Pellucid repin required.
- **Extraction only**: no rendering/width/glyph-selection change; no conformance-rule change (`FontUnicodeMapping` stays as-is). Conformance corpus agreement counts must be unchanged.
- **Fall-through resilience**: every failure (missing resource, malformed CMap, unknown ordering, unmappable code) silently falls through to today's `DecodeCharacter` chain — never an exception out of the new path.
- **Direction invariant**: the bundled `*-UCS2` files map Unicode→CID; `AdobeCidToUnicode` serves CID→Unicode. Tests pin the inversion against the bundled files' own content (no hand-remembered CID values).
- License: Adobe `cmap-resources` is BSD-3-Clause — the license text ships as an embedded `None Pack` file mirroring `NOTICE-cmyk-profile.txt` (`PdfLibrary.csproj:57`).
- Run engine tests with `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj` (+ `--filter` as given). LocalOnly corpus tests need the corpora present (they are, on this machine).
- Commit after each task; end commit messages with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Never push (the user authorizes pushes).

---

### Task 1: Fetch and bundle the four Adobe UCS2 CMaps

**Files:**
- Create: `PdfLibrary/Resources/CMaps/Adobe-Japan1-UCS2.gz`, `Adobe-Korea1-UCS2.gz`, `Adobe-GB1-UCS2.gz`, `Adobe-CNS1-UCS2.gz`
- Create: `PdfLibrary/Resources/CMaps/LICENSE-Adobe-CMaps.txt`
- Modify: `PdfLibrary/PdfLibrary.csproj` (ItemGroup at :51-58's pattern)
- Test: `PdfLibrary.Tests/Fonts/AdobeCidToUnicodeTests.cs` (create; resource-presence test only in this task)

**Interfaces:**
- Produces: four embedded resources with LogicalNames `PdfLibrary.Resources.CMaps.Adobe-<Ordering>-UCS2.gz` — Task 3 loads them by exactly those names.

- [ ] **Step 1: Download the four CMaps from Adobe's cmap-resources (BSD-3-Clause)**

```powershell
New-Item -ItemType Directory -Force PdfLibrary/Resources/CMaps
$base = "https://raw.githubusercontent.com/adobe-type-tools/cmap-resources/master"
$files = @{
  "Adobe-Japan1-UCS2" = "$base/Adobe-Japan1-7/CMap/Adobe-Japan1-UCS2"
  "Adobe-Korea1-UCS2" = "$base/Adobe-Korea1-2/CMap/Adobe-Korea1-UCS2"
  "Adobe-GB1-UCS2"    = "$base/Adobe-GB1-6/CMap/Adobe-GB1-UCS2"
  "Adobe-CNS1-UCS2"   = "$base/Adobe-CNS1-7/CMap/Adobe-CNS1-UCS2"
}
foreach ($n in $files.Keys) { Invoke-WebRequest $files[$n] -OutFile "PdfLibrary/Resources/CMaps/$n" }
Invoke-WebRequest "$base/LICENSE.md" -OutFile "PdfLibrary/Resources/CMaps/LICENSE-Adobe-CMaps.txt"
```

If any URL 404s (the versioned directory names move), list the repo tree via
`Invoke-RestMethod "https://api.github.com/repos/adobe-type-tools/cmap-resources/contents/"` and
take the current directory for that collection (the file name inside `<dir>/CMap/` is stable).
Sanity-check each downloaded file starts with `%!PS-Adobe-3.0 Resource-CMap` (first line) and
contains `begincidrange`. Record each file's byte size and SHA256
(`Get-FileHash`) in your report.

- [ ] **Step 2: Gzip them (and delete the uncompressed originals)**

```powershell
foreach ($n in @("Adobe-Japan1-UCS2","Adobe-Korea1-UCS2","Adobe-GB1-UCS2","Adobe-CNS1-UCS2")) {
  $src = "PdfLibrary/Resources/CMaps/$n"
  $in = [IO.File]::ReadAllBytes($src)
  $out = [IO.File]::Create("$src.gz")
  $gz = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal)
  $gz.Write($in, 0, $in.Length); $gz.Dispose(); $out.Dispose()
  Remove-Item $src
}
```

Record the compressed sizes in your report (expected: low hundreds of KB total).

- [ ] **Step 3: Wire into the csproj**, mirroring the ICC pattern at `PdfLibrary.csproj:51-58`:

```xml
    <ItemGroup>
        <!-- Adobe CID→Unicode CMaps (B-1 text extraction): the four Adobe-<Ordering>-UCS2 CMaps from
             github.com/adobe-type-tools/cmap-resources, BSD-3-Clause (license bundled alongside),
             gzip-compressed. Loaded lazily by AdobeCidToUnicode. -->
        <EmbeddedResource Include="Resources\CMaps\Adobe-Japan1-UCS2.gz">
            <LogicalName>PdfLibrary.Resources.CMaps.Adobe-Japan1-UCS2.gz</LogicalName>
        </EmbeddedResource>
        <EmbeddedResource Include="Resources\CMaps\Adobe-Korea1-UCS2.gz">
            <LogicalName>PdfLibrary.Resources.CMaps.Adobe-Korea1-UCS2.gz</LogicalName>
        </EmbeddedResource>
        <EmbeddedResource Include="Resources\CMaps\Adobe-GB1-UCS2.gz">
            <LogicalName>PdfLibrary.Resources.CMaps.Adobe-GB1-UCS2.gz</LogicalName>
        </EmbeddedResource>
        <EmbeddedResource Include="Resources\CMaps\Adobe-CNS1-UCS2.gz">
            <LogicalName>PdfLibrary.Resources.CMaps.Adobe-CNS1-UCS2.gz</LogicalName>
        </EmbeddedResource>
        <None Include="Resources\CMaps\LICENSE-Adobe-CMaps.txt" Pack="true" PackagePath="licenses\" />
    </ItemGroup>
```

- [ ] **Step 4: Write the resource-presence test (fails before Step 3's build, passes after)**

`PdfLibrary.Tests/Fonts/AdobeCidToUnicodeTests.cs`:

```csharp
using System.Reflection;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>B-1: the four Adobe *-UCS2 CMaps (CID→Unicode source data) ship as embedded resources.
/// The direction inversion and lookup behavior are pinned in the tests added with AdobeCidToUnicode
/// itself (Task 3); this class starts with the packaging pin.</summary>
public class AdobeCidToUnicodeTests
{
    [Theory]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-Japan1-UCS2.gz")]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-Korea1-UCS2.gz")]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-GB1-UCS2.gz")]
    [InlineData("PdfLibrary.Resources.CMaps.Adobe-CNS1-UCS2.gz")]
    public void Ucs2CMapResource_IsEmbedded(string logicalName)
    {
        using var s = typeof(ToUnicodeCMap).Assembly.GetManifestResourceStream(logicalName);
        Assert.NotNull(s);
        Assert.True(s!.Length > 1000, $"{logicalName} is implausibly small ({s.Length} bytes)");
    }
}
```

- [ ] **Step 5: Run it**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~AdobeCidToUnicodeTests"`
Expected: 4/4 PASS.

- [ ] **Step 6: Commit**

```powershell
git add PdfLibrary/Resources/CMaps PdfLibrary/PdfLibrary.csproj PdfLibrary.Tests/Fonts/AdobeCidToUnicodeTests.cs
git commit -m "feat(fonts): bundle Adobe *-UCS2 CMaps as embedded resources (B-1)"
```

---

### Task 2: `CidCMap` — embedded encoding CMap parser (code→CID)

**Files:**
- Create: `PdfLibrary/Fonts/CidCMap.cs`
- Test: `PdfLibrary.Tests/Fonts/CidCMapTests.cs` (create)

**Interfaces:**
- Consumes: nothing new.
- Produces: `public partial class CidCMap` with `static CidCMap Parse(byte[] data)`, `int? MapCodeToCid(int code)`, `string? UseCMapName`, `int MappingCount`, and `internal IEnumerable<KeyValuePair<int, int>> Entries` (parse-order enumeration; Task 3's inverter consumes it).

- [ ] **Step 1: Write the failing tests**

`PdfLibrary.Tests/Fonts/CidCMapTests.cs`:

```csharp
using System.Text;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>B-1: CidCMap parses the CID-keyed CMap dialect (cidchar/cidrange — CID operands are
/// DECIMAL, unlike the bf* dialect's hex destinations) used by embedded Type0 /Encoding streams.</summary>
public class CidCMapTests
{
    private static CidCMap Parse(string text) => CidCMap.Parse(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void CidChar_MapsSingleCodes()
    {
        CidCMap m = Parse("2 begincidchar\n<0041> 34\n<3042> 843\nendcidchar\n");
        Assert.Equal(34, m.MapCodeToCid(0x0041));
        Assert.Equal(843, m.MapCodeToCid(0x3042));
        Assert.Null(m.MapCodeToCid(0x0042));
    }

    [Fact]
    public void CidRange_IncrementsAcrossTheRange()
    {
        CidCMap m = Parse("1 begincidrange\n<0020> <0024> 1\nendcidrange\n");
        Assert.Equal(1, m.MapCodeToCid(0x20));
        Assert.Equal(3, m.MapCodeToCid(0x22));
        Assert.Equal(5, m.MapCodeToCid(0x24));
        Assert.Null(m.MapCodeToCid(0x25));
    }

    [Fact]
    public void MultipleBlocks_AllParsed()
    {
        CidCMap m = Parse(
            "1 begincidchar\n<00> 7\nendcidchar\n" +
            "1 begincidrange\n<10> <11> 100\nendcidrange\n" +
            "1 begincidchar\n<20> 200\nendcidchar\n");
        Assert.Equal(7, m.MapCodeToCid(0x00));
        Assert.Equal(101, m.MapCodeToCid(0x11));
        Assert.Equal(200, m.MapCodeToCid(0x20));
        Assert.Equal(4, m.MappingCount);
    }

    [Fact]
    public void UseCMap_IsRecordedNotFollowed()
    {
        CidCMap m = Parse("/Adobe-Japan1-UCS2 usecmap\n1 begincidchar\n<41> 34\nendcidchar\n");
        Assert.Equal("Adobe-Japan1-UCS2", m.UseCMapName);
        Assert.Equal(34, m.MapCodeToCid(0x41));   // local operators still parse
    }

    [Fact]
    public void MalformedInput_DegradesToEmpty()
    {
        Assert.Equal(0, Parse("not a cmap at all").MappingCount);
        Assert.Equal(0, Parse("begincidrange <zz> <yy> x endcidrange").MappingCount);
        Assert.Equal(0, CidCMap.Parse([]).MappingCount);
    }

    [Fact]
    public void AbsurdRange_IsSkippedNotMaterialized()
    {
        // A corrupt hi value must not allocate 16M entries; ranges wider than 0xFFFF are dropped.
        CidCMap m = Parse("1 begincidrange\n<000000> <FFFFFF> 1\nendcidrange\n");
        Assert.Equal(0, m.MappingCount);
    }
}
```

- [ ] **Step 2: Run — expect compile failure** (`CidCMap` does not exist).

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CidCMapTests"`

- [ ] **Step 3: Implement `PdfLibrary/Fonts/CidCMap.cs`**

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PdfLibrary.Fonts;

/// <summary>
/// Parses the CID-keyed operators of a CMap (ISO 32000-1:2008 §9.7.5.3): cidchar and cidrange
/// (code→CID; the CID operand is DECIMAL, unlike the bf* dialect's hex destinations — see
/// <see cref="ToUnicodeCMap"/> for that dialect). Used for an embedded Type0 /Encoding CMap
/// stream (B-1 CID→Unicode extraction). codespacerange is not needed for the fixed 2-byte
/// extraction loop and is not modeled. <c>usecmap</c> is recorded by name but NOT followed —
/// no predefined encoding bases are bundled (the measured corpus population never layers);
/// local operators still parse. Malformed input degrades to an empty map (the caller's decode
/// chain falls through) and Parse never throws.
/// </summary>
public partial class CidCMap
{
    // Widest legitimate range in a 2-byte codespace; anything wider is treated as corrupt
    // rather than materialized (a bogus 3-byte hi endpoint would otherwise allocate millions).
    private const int MaxRangeSpan = 0xFFFF;

    private readonly Dictionary<int, int> _codeToCid = new();

    /// <summary>The /Name operand of a <c>usecmap</c> directive, when present. Recorded for
    /// diagnostics; v1 does not resolve it (see class doc).</summary>
    public string? UseCMapName { get; private set; }

    public int MappingCount => _codeToCid.Count;

    /// <summary>Parse-order enumeration of (code, CID) — consumed by AdobeCidToUnicode's inverter.</summary>
    internal IEnumerable<KeyValuePair<int, int>> Entries => _codeToCid;

    public int? MapCodeToCid(int code) =>
        _codeToCid.TryGetValue(code, out int cid) ? cid : null;

    public static CidCMap Parse(byte[] data)
    {
        var cmap = new CidCMap();
        try
        {
            string content = Encoding.ASCII.GetString(data);
            ParseCidChar(cmap, content);
            ParseCidRange(cmap, content);
            Match use = UseCMapRegex().Match(content);
            if (use.Success) cmap.UseCMapName = use.Groups[1].Value;
        }
        catch
        {
            // Degrade to whatever parsed before the fault — same posture as ToUnicodeCMap.
        }
        return cmap;
    }

    // cidchar entry: <code> cid   (cid decimal)
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s+(\d+)")]
    private static partial Regex CidCharRegex();

    // cidrange entry: <lo> <hi> cid   (cid decimal)
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s+(\d+)")]
    private static partial Regex CidRangeRegex();

    [GeneratedRegex(@"/(\S+)\s+usecmap")]
    private static partial Regex UseCMapRegex();

    private static void ParseCidChar(CidCMap cmap, string content)
    {
        foreach (string block in FindBlocks(content, "begincidchar", "endcidchar"))
        foreach (Match match in CidCharRegex().Matches(block))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int code)) continue;
            if (!int.TryParse(match.Groups[2].Value, out int cid)) continue;
            cmap._codeToCid[code] = cid;
        }
    }

    private static void ParseCidRange(CidCMap cmap, string content)
    {
        foreach (string block in FindBlocks(content, "begincidrange", "endcidrange"))
        foreach (Match match in CidRangeRegex().Matches(block))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int lo) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out int hi) ||
                !int.TryParse(match.Groups[3].Value, out int cidStart))
                continue;
            if (hi < lo || hi - lo > MaxRangeSpan) continue;
            for (int code = lo; code <= hi; code++)
                cmap._codeToCid[code] = cidStart + (code - lo);
        }
    }

    // Same block scan as ToUnicodeCMap.FindBlocks (private there; the dialects stay independent).
    private static List<string> FindBlocks(string content, string beginMarker, string endMarker)
    {
        var blocks = new List<string>();
        var pos = 0;
        while (true)
        {
            int beginPos = content.IndexOf(beginMarker, pos, StringComparison.Ordinal);
            if (beginPos == -1) break;
            int endPos = content.IndexOf(endMarker, beginPos, StringComparison.Ordinal);
            if (endPos == -1) break;
            int blockStart = beginPos + beginMarker.Length;
            blocks.Add(content.Substring(blockStart, endPos - blockStart));
            pos = endPos + endMarker.Length;
        }
        return blocks;
    }
}
```

Note the `cidchar` regex also matches the two-hex prefix of a `cidrange` entry — that is why
cidchar parses only inside `begincidchar` blocks and cidrange only inside `begincidrange` blocks
(the block scan provides that isolation, as it does for `ToUnicodeCMap`).

- [ ] **Step 4: Run — all 6 tests PASS**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CidCMapTests"`

- [ ] **Step 5: Commit**

```powershell
git add PdfLibrary/Fonts/CidCMap.cs PdfLibrary.Tests/Fonts/CidCMapTests.cs
git commit -m "feat(fonts): CidCMap parser for embedded Type0 encoding streams (B-1)"
```

---

### Task 3: `AdobeCidToUnicode` — inverted lookup over the bundled tables

**Files:**
- Create: `PdfLibrary/Fonts/AdobeCidToUnicode.cs`
- Modify: `PdfLibrary.Tests/Fonts/AdobeCidToUnicodeTests.cs` (extend Task 1's class)

**Interfaces:**
- Consumes: Task 1's embedded resources (by LogicalName); Task 2's `CidCMap.Parse` + `Entries`.
- Produces: `public static class AdobeCidToUnicode` with `static bool IsSupportedOrdering(string? ordering)` and `static string? Lookup(string? ordering, int cid)`; `internal static Dictionary<int, string> BuildInverse(string cmapText)` (testable core).

- [ ] **Step 1: Write the failing tests** — add to `AdobeCidToUnicodeTests`:

```csharp
    // ---- direction inversion + lookup (Task 3) ---------------------------------------------

    [Fact]
    public void BuildInverse_InvertsUnicodeToCid_IntoCidToUnicode()
    {
        // Source dialect: cidrange maps UTF-16 code → CID. Inverse must serve CID → Unicode.
        Dictionary<int, string> inv = AdobeCidToUnicode.BuildInverse(
            "1 begincidrange\n<0041> <0043> 34\nendcidrange\n");
        Assert.Equal("A", inv[34]);
        Assert.Equal("B", inv[35]);
        Assert.Equal("C", inv[36]);
        Assert.False(inv.ContainsKey(0x0041));   // NOT keyed by Unicode — the direction pin
    }

    [Fact]
    public void BuildInverse_CollisionKeepsFirstInParseOrder()
    {
        Dictionary<int, string> inv = AdobeCidToUnicode.BuildInverse(
            "2 begincidchar\n<0041> 34\n<FF21> 34\nendcidchar\n");   // 'A' and fullwidth 'Ａ' → CID 34
        Assert.Equal("A", inv[34]);
    }

    [Fact]
    public void BuildInverse_SkipsSurrogateCodes()
    {
        Dictionary<int, string> inv = AdobeCidToUnicode.BuildInverse(
            "1 begincidchar\n<D800> 99\nendcidchar\n");
        Assert.False(inv.ContainsKey(99));
    }

    [Theory]
    [InlineData("Japan1")]
    [InlineData("Korea1")]
    [InlineData("GB1")]
    [InlineData("CNS1")]
    public void BundledTable_AgreesWithItsOwnFirstRangeEntry(string ordering)
    {
        // Ground truth from the shipped resource ITSELF: independently decompress + regex the
        // first cidrange entry, and assert Lookup serves its inversion. No hand-remembered CIDs.
        string text = DecompressResource($"PdfLibrary.Resources.CMaps.Adobe-{ordering}-UCS2.gz");
        Match m = Regex.Match(text, @"begincidrange\s*<([0-9A-Fa-f]{4})>\s*<[0-9A-Fa-f]{4}>\s+(\d+)");
        Assert.True(m.Success, "no cidrange entry found in bundled resource");
        int unicode = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber);
        int cid = int.Parse(m.Groups[2].Value);

        Assert.Equal(((char)unicode).ToString(), AdobeCidToUnicode.Lookup(ordering, cid));
    }

    [Fact]
    public void UnknownOrdering_And_UnknownCid_ReturnNull()
    {
        Assert.Null(AdobeCidToUnicode.Lookup("Identity", 34));
        Assert.Null(AdobeCidToUnicode.Lookup(null, 34));
        Assert.Null(AdobeCidToUnicode.Lookup("Japan1", int.MaxValue));
        Assert.True(AdobeCidToUnicode.IsSupportedOrdering("Japan1"));
        Assert.False(AdobeCidToUnicode.IsSupportedOrdering("Identity"));
    }

    private static string DecompressResource(string logicalName)
    {
        using Stream s = typeof(ToUnicodeCMap).Assembly.GetManifestResourceStream(logicalName)!;
        using var gz = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        return Encoding.ASCII.GetString(ms.ToArray());
    }
```

(Add `using System.Globalization;`, `using System.Text;`, `using System.Text.RegularExpressions;` as needed.)

- [ ] **Step 2: Run — expect compile failure** (`AdobeCidToUnicode` does not exist). Task 1's four resource tests still pass.

- [ ] **Step 3: Implement `PdfLibrary/Fonts/AdobeCidToUnicode.cs`**

```csharp
using System.IO.Compression;
using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>
/// CID→Unicode lookup for the registered Adobe CID collections (B-1 text extraction), backed by
/// Adobe's published <c>Adobe-&lt;Ordering&gt;-UCS2</c> CMaps (github.com/adobe-type-tools/
/// cmap-resources, BSD-3-Clause — Resources/CMaps/LICENSE-Adobe-CMaps.txt ships alongside),
/// bundled gzip-compressed and loaded lazily once per ordering.
/// <para>DIRECTION: the source files map UTF-16 code → CID; the table is INVERTED at load into
/// CID→Unicode. On collision (several Unicode points → one CID) the FIRST mapping in parse order
/// wins (cidchar blocks before cidrange blocks, file order within each) — stable per shipped
/// file. Surrogate code values are skipped (the UCS2 files are BMP-only by construction).</para>
/// Any load failure yields a null table and Lookup returns null — extraction falls through.
/// </summary>
public static class AdobeCidToUnicode
{
    private static readonly Dictionary<string, Lazy<Dictionary<int, string>?>> Tables =
        new(StringComparer.Ordinal)
        {
            ["Japan1"] = new(() => Load("Adobe-Japan1-UCS2")),
            ["Korea1"] = new(() => Load("Adobe-Korea1-UCS2")),
            ["GB1"] = new(() => Load("Adobe-GB1-UCS2")),
            ["CNS1"] = new(() => Load("Adobe-CNS1-UCS2")),
        };

    public static bool IsSupportedOrdering(string? ordering) =>
        ordering is not null && Tables.ContainsKey(ordering);

    public static string? Lookup(string? ordering, int cid)
    {
        if (ordering is null || !Tables.TryGetValue(ordering, out Lazy<Dictionary<int, string>?>? lazy))
            return null;
        return lazy.Value?.GetValueOrDefault(cid);
    }

    private static Dictionary<int, string>? Load(string name)
    {
        try
        {
            using Stream? s = typeof(AdobeCidToUnicode).Assembly
                .GetManifestResourceStream($"PdfLibrary.Resources.CMaps.{name}.gz");
            if (s is null) return null;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            return BuildInverse(Encoding.ASCII.GetString(ms.ToArray()));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Testable core: parse a UCS2-dialect CMap text (codes are UTF-16 values) with
    /// <see cref="CidCMap"/> and invert into CID→Unicode, first-in-parse-order wins.</summary>
    internal static Dictionary<int, string> BuildInverse(string cmapText)
    {
        CidCMap parsed = CidCMap.Parse(Encoding.ASCII.GetBytes(cmapText));
        var inverse = new Dictionary<int, string>();
        foreach ((int unicode, int cid) in parsed.Entries)
        {
            if ((unicode & 0xF800) == 0xD800 || unicode is < 0 or > 0xFFFF) continue;
            if (!inverse.ContainsKey(cid)) inverse[cid] = ((char)unicode).ToString();
        }
        return inverse;
    }
}
```

- [ ] **Step 4: Run the class — all tests PASS** (packaging + inversion + bundled-table agreement).

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~AdobeCidToUnicodeTests"`

**Note on `Entries` order:** `CidCMap` parses cidchar blocks before cidrange blocks; `Dictionary`
preserves insertion order for enumeration absent removals in practice, but the collision-policy
test constructs its collision *within a single cidchar block*, so it does not depend on that
implementation detail. If the bundled-table agreement test fails for exactly one ordering, check
whether the resource's first cidrange target CID is also mapped by an earlier cidchar entry
(first-wins would then serve the cidchar value) — pick the resource's second cidrange entry in
that case and note it in your report.

- [ ] **Step 5: Commit**

```powershell
git add PdfLibrary/Fonts/AdobeCidToUnicode.cs PdfLibrary.Tests/Fonts/AdobeCidToUnicodeTests.cs
git commit -m "feat(fonts): AdobeCidToUnicode - inverted CID-to-Unicode lookup over bundled UCS2 CMaps (B-1)"
```

---

### Task 4: Wire the registry path into `Type0Font.DecodeCharacter`

**Files:**
- Modify: `PdfLibrary/Fonts/Type0Font.cs` (`DecodeCharacter` at :69-87; new lazy registry context)
- Test: `PdfLibrary.Tests/Fonts/Type0FontRegistryDecodeTests.cs` (create)

**Interfaces:**
- Consumes: `CidCMap` (Task 2), `AdobeCidToUnicode` (Task 3); existing `_descendantFont`/`CidFont.RawDictionary`, `_dictionary`, `_document`, `PdfStream.GetDecodedData(_document?.Decryptor)` (the `LoadToUnicodeCMap` pattern, `PdfFont.cs:181-190`).
- Produces: the user-visible feature. No public API change.

- [ ] **Step 1: Write the failing tests**

`PdfLibrary.Tests/Fonts/Type0FontRegistryDecodeTests.cs`:

```csharp
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>B-1: a Type0 font with a registered Adobe ordering and no /ToUnicode decodes through
/// code→CID (embedded CMap / Identity / UCS2 shortcut) → CID→Unicode (bundled tables). ToUnicode,
/// when present, still wins; Adobe-Identity stays on the old fallback.</summary>
public class Type0FontRegistryDecodeTests
{
    private static PdfDictionary CidSystemInfo(string registry, string ordering) => new()
    {
        [new PdfName("Registry")] = new PdfString(registry),
        [new PdfName("Ordering")] = new PdfString(ordering),
        [new PdfName("Supplement")] = new PdfInteger(4),
    };

    private static PdfDictionary Descendant(string ordering) => new()
    {
        [new PdfName("Type")] = new PdfName("Font"),
        [new PdfName("Subtype")] = new PdfName("CIDFontType0"),
        [new PdfName("BaseFont")] = new PdfName("Test-" + ordering),
        [new PdfName("CIDSystemInfo")] = CidSystemInfo("Adobe", ordering),
    };

    private static Type0Font Build(string ordering, PdfObject encoding, PdfStream? toUnicode = null)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type0"),
            [new PdfName("BaseFont")] = new PdfName("Test-" + ordering),
            [new PdfName("Encoding")] = encoding,
            [new PdfName("DescendantFonts")] = new PdfArray { Descendant(ordering) },
        };
        if (toUnicode is not null) dict[new PdfName("ToUnicode")] = toUnicode;
        return (Type0Font)PdfFont.Create(dict)!;
    }

    // A CID with a real mapping in the bundled table, found dynamically — no hand-remembered CIDs.
    private static int FirstMappedCid(string ordering)
    {
        for (var cid = 1; cid < 1000; cid++)
            if (AdobeCidToUnicode.Lookup(ordering, cid) is not null) return cid;
        throw new InvalidOperationException($"no mapped CID under 1000 for {ordering}?");
    }

    [Fact]
    public void EmbeddedCMapEncoding_DecodesThroughCodeToCidToUnicode()
    {
        int cid = FirstMappedCid("Japan1");
        var encStream = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"1 begincidchar\n<0042> {cid}\nendcidchar\n"));
        Type0Font font = Build("Japan1", encStream);

        Assert.Equal(AdobeCidToUnicode.Lookup("Japan1", cid), font.DecodeCharacter(0x0042));
    }

    [Fact]
    public void IdentityH_UsesCodeAsCid()
    {
        int cid = FirstMappedCid("Korea1");
        Type0Font font = Build("Korea1", new PdfName("Identity-H"));

        Assert.Equal(AdobeCidToUnicode.Lookup("Korea1", cid), font.DecodeCharacter(cid));
    }

    [Fact]
    public void Ucs2Encoding_ReturnsTheCodeDirectly()
    {
        Type0Font font = Build("Japan1", new PdfName("UniJIS-UCS2-H"));
        Assert.Equal("あ", font.DecodeCharacter(0x3042));   // the code IS UCS-2
    }

    [Fact]
    public void ToUnicode_StillWins_OverTheRegistryPath()
    {
        int cid = FirstMappedCid("Japan1");
        var toUnicode = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"1 beginbfchar\n<{cid:X4}> <005A>\nendbfchar\n"));   // → "Z"
        Type0Font font = Build("Japan1", new PdfName("Identity-H"), toUnicode);

        Assert.Equal("Z", font.DecodeCharacter(cid));
    }

    [Fact]
    public void AdobeIdentityOrdering_KeepsTheOldFallback()
    {
        Type0Font font = Build("Identity", new PdfName("Identity-H"));
        Assert.Equal(char.ConvertFromUtf32(0x0041), font.DecodeCharacter(0x0041));
    }

    [Fact]
    public void UnmappableCode_FallsThroughToTheOldFallback()
    {
        var encStream = new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("1 begincidchar\n<0042> 5\nendcidchar\n"));
        Type0Font font = Build("Japan1", encStream);
        // 0x0999 has no entry in the embedded CMap → no CID → old fallback.
        Assert.Equal(char.ConvertFromUtf32(0x0999), font.DecodeCharacter(0x0999));
    }
}
```

(Adjust `new PdfString(...)`/`new PdfInteger(...)` construction to the primitives' actual
constructors if they differ — the conformance code reads `(x as PdfString)?.Value`, so a
string-accepting constructor exists; verify with one glance at `PdfString`.)

- [ ] **Step 2: Run — expect the four registry-path tests FAIL** (decode falls to `ConvertFromUtf32` today); `ToUnicode_StillWins` and `AdobeIdentityOrdering` pass already (pins).

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Type0FontRegistryDecodeTests"`

- [ ] **Step 3: Implement in `Type0Font.cs`**

(a) Fields + lazy context:

```csharp
    // B-1 registry CID→Unicode context (lazy; armed only when the descendant declares Registry
    // "Adobe" with a bundled ordering). _ordering non-null == the path is armed.
    private bool _registryContextLoaded;
    private string? _ordering;
    private CidCMap? _encodingCMap;     // parsed embedded /Encoding stream (stream-encoding case)
    private bool _identityEncoding;     // Identity-H / Identity-V
    private bool _ucs2Encoding;         // Uni*-UCS2-*: the code IS a UCS-2 value

    private void EnsureRegistryContext()
    {
        if (_registryContextLoaded) return;
        _registryContextLoaded = true;

        if ((_descendantFont as CidFont)?.RawDictionary is not { } cidDict) return;
        PdfObject? csiObj = cidDict.Get("CIDSystemInfo");
        if (csiObj is PdfIndirectReference csiRef && _document is not null)
            csiObj = _document.ResolveReference(csiRef);
        if (csiObj is not PdfDictionary csi) return;
        if (Resolve(csi.Get("Registry")) is not PdfString { Value: "Adobe" }) return;
        if (Resolve(csi.Get("Ordering")) is not PdfString ord
            || !AdobeCidToUnicode.IsSupportedOrdering(ord.Value)) return;

        if (!_dictionary.TryGetValue(new PdfName("Encoding"), out PdfObject? enc)) return;
        if (enc is PdfIndirectReference encRef && _document is not null)
            enc = _document.ResolveReference(encRef);
        switch (enc)
        {
            case PdfName { Value: "Identity-H" or "Identity-V" }:
                _identityEncoding = true;
                break;
            case PdfName n when n.Value.StartsWith("Uni", StringComparison.Ordinal)
                                && n.Value.Contains("-UCS2-", StringComparison.Ordinal):
                _ucs2Encoding = true;
                break;
            case PdfStream stream:
                _encodingCMap = CidCMap.Parse(stream.GetDecodedData(_document?.Decryptor));
                break;
            default:
                return;   // predefined non-Identity, non-UCS2 name: not bundled (spec non-goal)
        }
        _ordering = ord.Value;
        PdfLogger.Log(LogCategory.Text,
            $"Type0Font: registry CID→Unicode path active (Adobe-{_ordering})");
    }

    private PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference r && _document is not null ? _document.ResolveReference(r) : obj;

    // B-1 step 2 of the decode chain: registered-collection mapping. Null = fall through.
    private string? DecodeViaRegistry(int charCode)
    {
        EnsureRegistryContext();
        if (_ordering is null) return null;
        if (_ucs2Encoding)
        {
            // The character code IS a UCS-2 value. Exclude NUL and surrogate halves.
            if (charCode is <= 0 or > 0xFFFF || (charCode & 0xF800) == 0xD800) return null;
            return ((char)charCode).ToString();
        }
        int? cid = _identityEncoding ? charCode : _encodingCMap?.MapCodeToCid(charCode);
        return cid is null ? null : AdobeCidToUnicode.Lookup(_ordering, cid.Value);
    }
```

(b) In `DecodeCharacter`, after the ToUnicode step (`Type0Font.cs:72-75`) and before the
embedded-glyph-name step, insert:

```csharp
        // 2. Registered Adobe CID collection (B-1): code→CID (embedded /Encoding CMap, Identity,
        //    or UCS2 shortcut) → CID→Unicode (bundled Adobe-<Ordering>-UCS2 tables).
        string? registryUnicode = DecodeViaRegistry(charCode);
        if (registryUnicode is not null)
            return registryUnicode;
```

and renumber the existing comments (the old step 2 becomes step 3, the last-resort becomes 4).

- [ ] **Step 4: Run the new tests + the whole Fonts test area — all PASS**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Type0FontRegistryDecodeTests|FullyQualifiedName~PdfLibrary.Tests.Fonts"`

- [ ] **Step 5: Commit**

```powershell
git add PdfLibrary/Fonts/Type0Font.cs PdfLibrary.Tests/Fonts/Type0FontRegistryDecodeTests.cs
git commit -m "feat(fonts): Type0 CID-to-Unicode extraction for registered Adobe collections (B-1)"
```

---

### Task 5: Corpus audit assertion + whole-change verification

**Files:**
- Modify: `PdfLibrary.Tests/Fonts/Type0FallbackAuditTests.cs` (add one assertion-bearing LocalOnly test; the existing census test stays a census)

**Interfaces:**
- Consumes: everything above; the audit's existing corpus enumeration + font-walking helpers.

- [ ] **Step 1: Add the assertion test** to `Type0FallbackAuditTests` (reuse its enumeration/walk helpers verbatim — read the file first; the shape below adapts to its actual helper names):

```csharp
    /// <summary>B-1 acceptance: every registered-ordering (Japan1/Korea1/GB1/CNS1) Type0 font with
    /// no /ToUnicode in the corpus now decodes at least one code to something other than the
    /// raw-code fallback — and Adobe-Identity fonts are untouched (no code decodes differently).
    /// Sampled over the 2-byte code space's low range; LocalOnly like the census.</summary>
    [Fact]
    [Trait("Category", "LocalOnly")]
    public void RegisteredOrderings_DecodeRealUnicode_IdentityUnchanged()
    {
        // ... same corpus walk as the census test, collecting (font, ordering) for Type0 fonts
        //     with no ToUnicode and Registry "Adobe" ...
        // For each registered-ordering font:
        var mapped = false;
        for (var code = 0; code <= 0x7FFF && !mapped; code++)
            mapped = font.DecodeCharacter(code) != SafeRawFallback(code);
        Assert.True(mapped, $"{file}: {baseFont} ({ordering}) decoded nothing via the registry path");
        // For each Adobe-Identity font:
        for (var code = 0; code <= 0x3000; code++)
            Assert.Equal(SafeRawFallback(code), font.DecodeCharacter(code));
    }

    // char.ConvertFromUtf32 faults on surrogate-range ints; mirror Type0Font's last-resort arm.
    private static string SafeRawFallback(int code) =>
        (code & 0xF800) == 0xD800 ? "\uFFFD" : char.ConvertFromUtf32(code);
```

The skeleton above states the CONTRACT; transcribe it onto the census test's real enumeration and
font-construction code (`PdfFont.Create` over each font dict with the owning document). Note: fonts
whose registry path activates but whose embedded font also had glyph-name fallback may already
have decoded some codes differently — the assertion is "some code differs from the RAW fallback",
which both the registry path and a working glyph-name fallback satisfy; the census recorded that
the glyph-name fallback fires for 0 of these fonts (all CID-keyed), so the registry path is the
only possible source. State this in the test comment. If `DecodeCharacter`'s raw fallback throws
on any surrogate-range code (pre-existing behavior), skip those codes in both loops rather than
teaching the product code anything new.

- [ ] **Step 2: Run it (LocalOnly, corpora present)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~RegisteredOrderings_DecodeRealUnicode"`
Expected: PASS — all 18 registered-ordering rows decode, all 33 Identity rows unchanged. A
registered-ordering font that decodes nothing is a real gap: report it with its file/BaseFont
(do not weaken the assertion; the measured population says all 18 are embedded-CMap/Identity/UCS2).

- [ ] **Step 3: Run the census + the full engine suite (including LocalOnly conformance corpora)**

```powershell
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj
```

(10-minute timeout; this includes the conformance corpus gates.) Expected: all green, and the
conformance agreement counts unchanged from master's baselines — this feature adds no rule
behavior. Any conformance-count movement → STOP and report BLOCKED.

- [ ] **Step 4: Commit**

```powershell
git add PdfLibrary.Tests/Fonts/Type0FallbackAuditTests.cs
git commit -m "test(fonts): corpus assertion - registered orderings decode real Unicode (B-1)"
```

---

### Task 6: Close-out — engine docs + Pellucid tracker

**Files:**
- Modify (engine): `PdfLibrary/Fonts/EmbeddedFontExtractor.cs` — ONLY if its 072111d-era doc text describes the bundled-CMap feature as not-built; update that sentence. Check with a grep for `cid2unicode|bundled|Adobe CMap` in that file and `Conformance/FontUnicodeMapping.cs:34` ("which we do not bundle" — now false; reword to "which are bundled for extraction (AdobeCidToUnicode); this rule stays conservative regardless").
- Modify (Pellucid repo, `C:\Users\jorda\RiderProjects\Pellucid`): `docs/ISSUE-TRACKER.md` B-1 entry.

- [ ] **Step 1: Engine doc-comment sweep** — fix the two stale statements above (verbatim grep first; keep edits one-sentence minimal). Run `dotnet build PdfLibrary/PdfLibrary.csproj` (comments only; verify).

- [ ] **Step 2: Engine commit**

```powershell
git add -u
git commit -m "docs(fonts): B-1 landed - update the not-bundled-CMaps statements"
```

- [ ] **Step 3: Pellucid tracker** — in `docs/ISSUE-TRACKER.md`, flip B-1 to 🟢 Fixed+Verified: landed 2026-08-02 in the engine (spec `PDF/Docs/superpowers/specs/2026-08-02-cid-to-unicode-registry-cmaps-design.md`), resolution one-liner (embedded-encoding-CMap parser + four bundled Adobe UCS2 tables; measured population 18/18 rows decode; Identity rows unchanged; no Pellucid change needed — next routine repin inherits it). Match house style.

- [ ] **Step 4: Pellucid commit**

```powershell
git add docs/ISSUE-TRACKER.md
git commit -m "docs: B-1 closed - engine CID-to-Unicode registry CMaps landed"
```

(Neither repo is pushed — the user authorizes pushes.)

---

## Self-Review

- **Spec coverage:** bundling + license (T1 = spec §2 resources), `CidCMap` incl. usecmap/malformed/range-cap (T2 = spec §1), inversion + collision policy + per-ordering self-derived spot checks (T3 = spec §2 + risk 1), decode-chain wiring incl. UCS2 shortcut, Identity, ToUnicode-wins, Adobe-Identity-unchanged (T4 = spec §3 + tests a–e), corpus 18-row assertion + Identity-unchanged + full suite + conformance-floor check (T5 = spec acceptance 4–5), stale-docs sweep + tracker (T6). Spec's "no extractor 2-byte change / no rendering change" holds: no task touches `PdfTextExtractor` or rendering.
- **Placeholder scan:** T5's test body is explicitly a CONTRACT skeleton to be transcribed onto the census test's real helpers — bounded by named behaviors and named file, with a STOP rule; not an open TBD. All other code steps are complete.
- **Type consistency:** `CidCMap.Parse(byte[])`/`MapCodeToCid(int)`/`Entries` consistent across T2 (definition), T3 (`BuildInverse` consumes `Entries`), T4 (stream parse); `AdobeCidToUnicode.Lookup(string?, int)`/`IsSupportedOrdering(string?)` consistent across T3/T4; resource LogicalNames identical in T1 csproj, T1 test, and T3 loader; `PdfStream(new PdfDictionary(), bytes)` + `GetDecodedData(_document?.Decryptor)` match the engine's existing call shapes.
