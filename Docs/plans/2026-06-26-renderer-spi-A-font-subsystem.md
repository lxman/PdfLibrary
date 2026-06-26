# Renderer SPI — Plan A: Font Subsystem Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the core a SkiaSharp-free way to obtain font-program *bytes* for non-embedded (standard-14) fonts by locating metric-compatible substitutes already installed on the system.

**Architecture:** Add one method to `ISystemFontProvider` (`GetFontData`) and a default in-core implementation, `SystemFontLocator`, that scans the OS font directories, indexes files by base name, maps each standard-14 face to an ordered list of candidate substitute file names, and returns the bytes of the first match. The locator returns raw bytes only — parsing (FontParser) is out of scope and belongs to Plan B (the text pipeline). Reading installed fonts is not redistribution, so nothing is bundled and there is no SkiaSharp dependency.

**Tech Stack:** C# 12, .NET 8/9/10 (multi-target), `System.IO`, xUnit. No new package dependencies.

## Global Constraints

- Core project `PdfLibrary` must remain **SkiaSharp-free**: no `using SkiaSharp`, no SkiaSharp package reference. (Verify with the SkiaSharp-free check in Task 6.)
- Multi-target: code must compile under `net8.0;net9.0;net10.0`. `Directory.CreateTempSubdirectory()` (net6+), default interface methods (C# 8+), and `OperatingSystem.IsWindows()/IsLinux()/IsMacOS()` (net5+) are all available on these TFMs.
- New public types live in namespace `PdfLibrary.Fonts`. New tests live in `PdfLibrary.Tests/Fonts/`, namespace `PdfLibrary.Tests.Fonts`.
- xUnit conventions: `[Fact]`, `Assert.*`. Read the vendored fixture with `File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"))`.
- This plan is purely additive — it must not change any existing behavior. The full suite (1233 tests) must stay green after every task.

---

## File Structure

- `PdfLibrary/Fonts/ISystemFontProvider.cs` (modify) — add the `GetFontData` default interface method.
- `PdfLibrary/Fonts/Standard14Fonts.cs` (create) — normalizes a PDF `/BaseFont` name to a standard-14 face and maps it to an ordered list of candidate substitute file base-names.
- `PdfLibrary/Fonts/FontDirectoryIndex.cs` (create) — scans directories for font files and indexes them by case-insensitive base-name → full path.
- `PdfLibrary/Fonts/SystemFontLocator.cs` (create) — the default `ISystemFontProvider`; composes `Standard14Fonts` + `FontDirectoryIndex` to return font-program bytes; supplies the OS default directories.
- `PdfLibrary.Tests/Fonts/Standard14FontsTests.cs` (create)
- `PdfLibrary.Tests/Fonts/FontDirectoryIndexTests.cs` (create)
- `PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs` (create)

---

## Task 1: Extend `ISystemFontProvider` with `GetFontData`

**Files:**
- Modify: `PdfLibrary/Fonts/ISystemFontProvider.cs`
- Test: `PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs`

**Interfaces:**
- Produces: `byte[]? ISystemFontProvider.GetFontData(string baseFontName)` — default interface method returning `null`; implementations override to supply font-program bytes for a PDF `/BaseFont` name, or `null` if unavailable.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs`:

```csharp
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class SystemFontLocatorTests
{
    // A minimal provider that does not override GetFontData — proves the default is null.
    private sealed class BareProvider : ISystemFontProvider
    {
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => Array.Empty<string>();
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
    }

    [Fact]
    public void GetFontData_DefaultInterfaceImplementation_ReturnsNull()
    {
        ISystemFontProvider provider = new BareProvider();
        Assert.Null(provider.GetFontData("Helvetica"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SystemFontLocatorTests.GetFontData_DefaultInterfaceImplementation_ReturnsNull"`
Expected: FAIL — compile error, `ISystemFontProvider` has no `GetFontData`.

- [ ] **Step 3: Add the default interface method**

In `PdfLibrary/Fonts/ISystemFontProvider.cs`, add inside the interface body (after `RefreshCache`):

```csharp
    /// <summary>
    /// Returns the raw font-program bytes for a PDF <c>/BaseFont</c> name (e.g. a standard-14
    /// face like "Helvetica-Bold"), located among the fonts installed on the system, or
    /// <c>null</c> if no suitable substitute is available. The default returns <c>null</c>.
    /// </summary>
    byte[]? GetFontData(string baseFontName) => null;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SystemFontLocatorTests.GetFontData_DefaultInterfaceImplementation_ReturnsNull"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/ISystemFontProvider.cs PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs
git commit -m "feat(fonts): add ISystemFontProvider.GetFontData (default null)"
```

---

## Task 2: Standard-14 → substitute file-name mapping

**Files:**
- Create: `PdfLibrary/Fonts/Standard14Fonts.cs`
- Test: `PdfLibrary.Tests/Fonts/Standard14FontsTests.cs`

**Interfaces:**
- Produces: `static IReadOnlyList<string> Standard14Fonts.SubstituteFileBaseNames(string baseFontName)` — returns an ordered list of case-insensitive file base-names (no extension) to try for the given `/BaseFont`, or an empty list if the name is not a recognized standard-14 face. Normalizes subset prefixes (`ABCDEF+Helvetica`), common aliases (Arial→Helvetica, TimesNewRoman→Times, CourierNew→Courier), and `Family-Style`/`Family,Style` style suffixes.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/Standard14FontsTests.cs`:

```csharp
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class Standard14FontsTests
{
    [Fact]
    public void Helvetica_MapsToSansCandidates_InPriorityOrder()
    {
        IReadOnlyList<string> c = Standard14Fonts.SubstituteFileBaseNames("Helvetica");
        Assert.Equal("arial", c[0]);
        Assert.Contains("LiberationSans-Regular", c);
        Assert.Contains("NimbusSans-Regular", c);
        Assert.Contains("DejaVuSans", c);
    }

    [Fact]
    public void BoldStyle_SelectsBoldFiles()
    {
        IReadOnlyList<string> c = Standard14Fonts.SubstituteFileBaseNames("Helvetica-Bold");
        Assert.Equal("arialbd", c[0]);
        Assert.Contains("LiberationSans-Bold", c);
    }

    [Fact]
    public void SubsetPrefix_IsStripped()
    {
        IReadOnlyList<string> c = Standard14Fonts.SubstituteFileBaseNames("ABCDEF+Times-Italic");
        Assert.Contains("LiberationSerif-Italic", c);
    }

    [Fact]
    public void ArialAlias_MapsToHelveticaSubstitutes()
    {
        Assert.Equal(
            Standard14Fonts.SubstituteFileBaseNames("Helvetica"),
            Standard14Fonts.SubstituteFileBaseNames("Arial"));
    }

    [Fact]
    public void Symbol_And_ZapfDingbats_MapToUrwFiles()
    {
        Assert.Contains("StandardSymbolsPS", Standard14Fonts.SubstituteFileBaseNames("Symbol"));
        Assert.Contains("D050000L", Standard14Fonts.SubstituteFileBaseNames("ZapfDingbats"));
    }

    [Fact]
    public void UnknownFont_ReturnsEmpty()
    {
        Assert.Empty(Standard14Fonts.SubstituteFileBaseNames("Wingdings3"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Standard14FontsTests"`
Expected: FAIL — `Standard14Fonts` does not exist.

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/Standard14Fonts.cs`:

```csharp
namespace PdfLibrary.Fonts;

/// <summary>
/// Maps a PDF <c>/BaseFont</c> name to an ordered list of metric-compatible substitute font
/// file base-names (no extension) to look for among installed system fonts. Ordering is by
/// preference: the OS-native metric clone first (Arial/Times New Roman/Courier New), then the
/// common libre families (Liberation, URW/Nimbus, Arimo/Tinos/Cousine, DejaVu).
/// </summary>
public static class Standard14Fonts
{
    private enum Family { Sans, Serif, Mono, Symbol, Dingbats }

    private static readonly IReadOnlyList<string> Empty = Array.Empty<string>();

    public static IReadOnlyList<string> SubstituteFileBaseNames(string baseFontName)
    {
        if (string.IsNullOrWhiteSpace(baseFontName))
            return Empty;

        // Strip subset tag "ABCDEF+Name".
        string name = baseFontName;
        int plus = name.IndexOf('+');
        if (plus == 6) name = name[(plus + 1)..];

        // Split "Family-Style" or "Family,Style".
        string core = name;
        string style = string.Empty;
        int sep = name.IndexOfAny(['-', ',']);
        if (sep >= 0)
        {
            core = name[..sep];
            style = name[(sep + 1)..];
        }

        string c = core.Replace(" ", string.Empty).ToLowerInvariant();
        Family? family = c switch
        {
            "helvetica" or "arial" or "arialmt" or "helv" => Family.Sans,
            "times" or "timesroman" or "timesnewroman" or "timesnewromanpsmt" => Family.Serif,
            "courier" or "couriernew" or "couriernewpsmt" => Family.Mono,
            "symbol" => Family.Symbol,
            "zapfdingbats" or "dingbats" => Family.Dingbats,
            _ => null
        };
        if (family is null) return Empty;

        string s = style.Replace(" ", string.Empty).ToLowerInvariant();
        bool bold = s.Contains("bold");
        bool italic = s.Contains("italic") || s.Contains("oblique");

        return family switch
        {
            Family.Sans => Pick(bold, italic,
                ["arial", "LiberationSans-Regular", "NimbusSans-Regular", "Arimo-Regular", "DejaVuSans"],
                ["arialbd", "LiberationSans-Bold", "NimbusSans-Bold", "Arimo-Bold", "DejaVuSans-Bold"],
                ["ariali", "LiberationSans-Italic", "NimbusSans-Italic", "Arimo-Italic", "DejaVuSans-Oblique"],
                ["arialbi", "LiberationSans-BoldItalic", "NimbusSans-BoldItalic", "Arimo-BoldItalic", "DejaVuSans-BoldOblique"]),
            Family.Serif => Pick(bold, italic,
                ["times", "LiberationSerif-Regular", "NimbusRoman-Regular", "Tinos-Regular", "DejaVuSerif"],
                ["timesbd", "LiberationSerif-Bold", "NimbusRoman-Bold", "Tinos-Bold", "DejaVuSerif-Bold"],
                ["timesi", "LiberationSerif-Italic", "NimbusRoman-Italic", "Tinos-Italic", "DejaVuSerif-Italic"],
                ["timesbi", "LiberationSerif-BoldItalic", "NimbusRoman-BoldItalic", "Tinos-BoldItalic", "DejaVuSerif-BoldItalic"]),
            Family.Mono => Pick(bold, italic,
                ["cour", "LiberationMono-Regular", "NimbusMonoPS-Regular", "Cousine-Regular", "DejaVuSansMono"],
                ["courbd", "LiberationMono-Bold", "NimbusMonoPS-Bold", "Cousine-Bold", "DejaVuSansMono-Bold"],
                ["couri", "LiberationMono-Italic", "NimbusMonoPS-Italic", "Cousine-Italic", "DejaVuSansMono-Oblique"],
                ["courbi", "LiberationMono-BoldItalic", "NimbusMonoPS-BoldItalic", "Cousine-BoldItalic", "DejaVuSansMono-BoldOblique"]),
            Family.Symbol => ["Symbol", "StandardSymbolsPS", "StandardSymbolsL"],
            Family.Dingbats => ["ZapfDingbats", "D050000L", "Dingbats"],
            _ => Empty
        };
    }

    private static IReadOnlyList<string> Pick(bool bold, bool italic,
        string[] regular, string[] boldArr, string[] italicArr, string[] boldItalic)
        => (bold, italic) switch
        {
            (true, true) => boldItalic,
            (true, false) => boldArr,
            (false, true) => italicArr,
            _ => regular
        };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Standard14FontsTests"`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/Standard14Fonts.cs PdfLibrary.Tests/Fonts/Standard14FontsTests.cs
git commit -m "feat(fonts): map standard-14 BaseFont names to substitute file names"
```

---

## Task 3: Font-directory index

**Files:**
- Create: `PdfLibrary/Fonts/FontDirectoryIndex.cs`
- Test: `PdfLibrary.Tests/Fonts/FontDirectoryIndexTests.cs`

**Interfaces:**
- Produces:
  - `FontDirectoryIndex(IEnumerable<string> directories)` — constructor; scans the given directories (recursively, ignoring ones that don't exist or throw) for `*.ttf`, `*.otf`, `*.ttc` files.
  - `string? FontDirectoryIndex.FindPath(string fileBaseName)` — case-insensitive lookup by file base-name (no extension); returns the full path of the first match, or `null`.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/FontDirectoryIndexTests.cs`:

```csharp
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontDirectoryIndexTests
{
    private static string FixtureBytesPath =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf");

    [Fact]
    public void FindPath_ReturnsFile_CaseInsensitive()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string target = Path.Combine(dir, "LiberationSans-Regular.ttf");
            File.Copy(FixtureBytesPath, target);

            var index = new FontDirectoryIndex([dir]);

            Assert.Equal(target, index.FindPath("liberationsans-regular"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenAbsent()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var index = new FontDirectoryIndex([dir]);
            Assert.Null(index.FindPath("NoSuchFont"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Constructor_IgnoresMissingDirectories()
    {
        var index = new FontDirectoryIndex([Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())]);
        Assert.Null(index.FindPath("anything"));
    }
}
```

(Note: `Guid.NewGuid()` is only used to name a directory that must NOT exist — it is test-only and never reached by production code.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontDirectoryIndexTests"`
Expected: FAIL — `FontDirectoryIndex` does not exist.

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/FontDirectoryIndex.cs`:

```csharp
namespace PdfLibrary.Fonts;

/// <summary>
/// Indexes font files found in a set of directories, keyed by case-insensitive file base-name
/// (no extension). Scanning is best-effort: missing or unreadable directories are skipped.
/// </summary>
public sealed class FontDirectoryIndex
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];
    private readonly Dictionary<string, string> _byBaseName = new(StringComparer.OrdinalIgnoreCase);

    public FontDirectoryIndex(IEnumerable<string> directories)
    {
        foreach (string dir in directories)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                // Permission or IO error on a directory — skip it.
                continue;
            }

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file);
                if (Array.IndexOf(Extensions, ext.ToLowerInvariant()) < 0)
                    continue;

                string baseName = Path.GetFileNameWithoutExtension(file);
                // First writer wins, so earlier directories take precedence.
                _byBaseName.TryAdd(baseName, file);
            }
        }
    }

    /// <summary>Full path of the indexed font whose file base-name matches, or null.</summary>
    public string? FindPath(string fileBaseName) =>
        _byBaseName.TryGetValue(fileBaseName, out string? path) ? path : null;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontDirectoryIndexTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/FontDirectoryIndex.cs PdfLibrary.Tests/Fonts/FontDirectoryIndexTests.cs
git commit -m "feat(fonts): index font files by base-name across directories"
```

---

## Task 4: `SystemFontLocator` — bytes for a BaseFont

**Files:**
- Create: `PdfLibrary/Fonts/SystemFontLocator.cs`
- Test: `PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs` (extend from Task 1)

**Interfaces:**
- Consumes: `Standard14Fonts.SubstituteFileBaseNames` (Task 2), `FontDirectoryIndex` (Task 3), `ISystemFontProvider.GetFontData` (Task 1).
- Produces:
  - `SystemFontLocator(IEnumerable<string> directories)` — testable constructor taking explicit scan directories.
  - `byte[]? SystemFontLocator.GetFontData(string baseFontName)` — overrides the interface method; maps the name to candidates, finds the first present file, returns its bytes, or `null`.
  - Implements the legacy `ISystemFontProvider` members (`GetAvailableFontFamilies`/`IsFontAvailable`/`FindFirstAvailable`/`RefreshCache`) as best-effort over the indexed base-names.

- [ ] **Step 1: Write the failing test**

Add to `PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs`:

```csharp
    private static byte[] Fixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    [Fact]
    public void GetFontData_FindsSubstitute_ForHelvetica()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // A file named like one of Helvetica's candidates; content is the fixture's bytes.
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"),
                      Path.Combine(dir, "LiberationSans-Regular.ttf"));

            var locator = new SystemFontLocator([dir]);
            byte[]? data = locator.GetFontData("Helvetica");

            Assert.NotNull(data);
            Assert.Equal(Fixture(), data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GetFontData_ReturnsNull_WhenNoSubstitutePresent()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var locator = new SystemFontLocator([dir]);
            Assert.Null(locator.GetFontData("Helvetica"));
        }
        finally { Directory.Delete(dir, true); }
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SystemFontLocatorTests.GetFontData_FindsSubstitute_ForHelvetica"`
Expected: FAIL — `SystemFontLocator` does not exist.

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/SystemFontLocator.cs` (the OS-default constructor is added in Task 5; for now only the explicit-directories constructor):

```csharp
namespace PdfLibrary.Fonts;

/// <summary>
/// The default, SkiaSharp-free <see cref="ISystemFontProvider"/>: locates metric-compatible
/// substitutes for standard-14 fonts among the fonts installed on the system and returns their
/// raw bytes. Reading installed fonts is not redistribution.
/// </summary>
public sealed partial class SystemFontLocator : ISystemFontProvider
{
    private readonly FontDirectoryIndex _index;
    private readonly List<string> _baseNames;

    /// <summary>Create a locator that scans the given directories (used for testing).</summary>
    public SystemFontLocator(IEnumerable<string> directories)
    {
        string[] dirs = directories as string[] ?? directories.ToArray();
        _index = new FontDirectoryIndex(dirs);
        _baseNames = EnumerateBaseNames(dirs);
    }

    /// <inheritdoc/>
    public byte[]? GetFontData(string baseFontName)
    {
        foreach (string candidate in Standard14Fonts.SubstituteFileBaseNames(baseFontName))
        {
            string? path = _index.FindPath(candidate);
            if (path is null) continue;
            try { return File.ReadAllBytes(path); }
            catch { return null; }
        }
        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetAvailableFontFamilies() => _baseNames;

    /// <inheritdoc/>
    public bool IsFontAvailable(string familyName) => GetFontData(familyName) is not null;

    /// <inheritdoc/>
    public string? FindFirstAvailable(IEnumerable<string> candidates)
    {
        foreach (string c in candidates)
            if (IsFontAvailable(c)) return c;
        return null;
    }

    /// <inheritdoc/>
    public void RefreshCache() { /* Index is built at construction; create a new locator to refresh. */ }

    private static List<string> EnumerateBaseNames(IEnumerable<string> directories)
    {
        var names = new List<string>();
        foreach (string dir in directories)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".ttf" or ".otf" or ".ttc")
                        names.Add(Path.GetFileNameWithoutExtension(f));
                }
            }
            catch { /* skip unreadable dir */ }
        }
        return names;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SystemFontLocatorTests"`
Expected: PASS (3 tests: the A1 default-null test + the two new ones)

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/SystemFontLocator.cs PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs
git commit -m "feat(fonts): SystemFontLocator returns substitute font bytes for a BaseFont"
```

---

## Task 5: OS default font directories

**Files:**
- Create: `PdfLibrary/Fonts/SystemFontLocator.Directories.cs` (the `partial` companion)
- Test: `PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs` (extend)

**Interfaces:**
- Produces:
  - `SystemFontLocator()` — parameterless constructor scanning the platform's standard font directories.
  - `static IReadOnlyList<string> SystemFontLocator.DefaultFontDirectories()` — the platform's standard font directories.

- [ ] **Step 1: Write the failing test**

Add to `PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs`:

```csharp
    [Fact]
    public void DefaultFontDirectories_AreNonEmpty_OnThisPlatform()
    {
        Assert.NotEmpty(SystemFontLocator.DefaultFontDirectories());
    }

    // Integration: depends on system-installed fonts, so it is opt-in (not run in CI).
    [Fact]
    [Trait("Category", "LocalOnly")]
    public void GetFontData_ResolvesHelvetica_OnRealSystem()
    {
        var locator = new SystemFontLocator();
        Assert.NotNull(locator.GetFontData("Helvetica"));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SystemFontLocatorTests.DefaultFontDirectories_AreNonEmpty_OnThisPlatform"`
Expected: FAIL — no parameterless constructor / no `DefaultFontDirectories`.

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/SystemFontLocator.Directories.cs`:

```csharp
namespace PdfLibrary.Fonts;

public sealed partial class SystemFontLocator
{
    /// <summary>Create a locator that scans the platform's standard font directories.</summary>
    public SystemFontLocator() : this(DefaultFontDirectories()) { }

    /// <summary>The standard OS font directories for the current platform.</summary>
    public static IReadOnlyList<string> DefaultFontDirectories()
    {
        var dirs = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            string winFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(winFonts)) dirs.Add(winFonts);

            // Per-user fonts (Windows 10+): %LOCALAPPDATA%\Microsoft\Windows\Fonts
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApp))
                dirs.Add(Path.Combine(localApp, "Microsoft", "Windows", "Fonts"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add("/System/Library/Fonts");
            dirs.Add("/Library/Fonts");
            if (!string.IsNullOrEmpty(home)) dirs.Add(Path.Combine(home, "Library", "Fonts"));
        }
        else // Linux and other Unix
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add("/usr/share/fonts");
            dirs.Add("/usr/local/share/fonts");
            if (!string.IsNullOrEmpty(home))
            {
                dirs.Add(Path.Combine(home, ".local", "share", "fonts"));
                dirs.Add(Path.Combine(home, ".fonts"));
            }
        }

        return dirs;
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SystemFontLocatorTests.DefaultFontDirectories_AreNonEmpty_OnThisPlatform"`
Expected: PASS

Run (opt-in integration, on a dev machine with fonts): `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SystemFontLocatorTests.GetFontData_ResolvesHelvetica_OnRealSystem"`
Expected: PASS on this system (URW/Liberation present). It is excluded from CI by the `Category!=LocalOnly` filter the publish workflow already uses.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/SystemFontLocator.Directories.cs PdfLibrary.Tests/Fonts/SystemFontLocatorTests.cs
git commit -m "feat(fonts): default SystemFontLocator scans platform font directories"
```

---

## Task 6: Full-suite + SkiaSharp-free verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo`
Expected: PASS — 1233 prior tests + the new font tests, 0 failures.

- [ ] **Step 2: Confirm the core is still SkiaSharp-free**

Run: `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/`
Expected: no output (no SkiaSharp anywhere in the core).

- [ ] **Step 3: Confirm Release build is warning-free across TFMs**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 4: Commit (if any cleanup was needed; otherwise skip)**

```bash
git commit -am "test(fonts): verify font subsystem; core remains SkiaSharp-free"
```

---

## Self-Review Notes

- **Spec coverage:** implements the spec's "Fonts: locate installed substitutes" section — managed OS-dir scan (Tasks 3/5), base-14 → candidate mapping (Task 2), font bytes via the provider (Tasks 1/4). Bundled Symbol/ZapfDingbats fallback is deferred to the spec's optional fallback and is **not** in Plan A. The resolution order's "embedded → located substitute" is realized here for the "located" half; the "embedded" half lives in Plan B (the text pipeline that calls `GetFontData`).
- **No behavior change:** nothing in Plan A is wired into the render or build pipeline yet, so existing behavior and the 1233 tests are untouched until Plan B consumes `GetFontData`.
- **Out of scope (Plan B):** parsing located bytes with FontParser, glyph→path conversion, and using the locator in the text pipeline.
