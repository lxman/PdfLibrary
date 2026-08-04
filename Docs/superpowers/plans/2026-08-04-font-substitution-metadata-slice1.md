# Font Substitution — Metadata Index and Ladder (Slice 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve fonts the renderer cannot use from the PDF itself by matching the font's own metadata — PostScript name, family, style — instead of guessing at filenames.

**Architecture:** A new `FontMetadataIndex` parses only the `name` and `head` tables of every installed face (including each face inside a `.ttc`) and indexes them by PostScript name and by every localized family name. `SystemFontLocator` gains a `Resolve(FontRequest)` method implementing a three-step ladder — PostScript name, aliased family, synthetic standard-14 — and `SubstituteFontResolver` calls it instead of walking hardcoded filenames. Failing all three still returns null; slice 1 adds no fallback floor.

**Tech Stack:** C#, .NET (existing TFMs), xUnit. No new dependencies.

**Spec:** `Docs/superpowers/specs/2026-08-04-font-substitution-metadata-design.md`

## Global Constraints

- **No new NuGet dependencies.** The engine's value is being pure C# and dependency-free.
- **No disk writes.** No cache files, no temp files. The index is in-memory, built once per process.
- **Never read a whole font file** during indexing. Read the sfnt header, table directory, `name` and `head` only. Reading whole files measured 591 ms vs 42 ms for headers on 732 files.
- **Do not read `OS/2`.** The ladder scores on italic and bold only.
- **No Latin floor in this slice.** Failing every ladder step returns `null`, exactly as today. Task 5 has a test asserting this; slice 2 will invert it.
- **PostScript name (`name` ID 6) is the primary key.** ASCII by specification, no language variants, measured present on 100% of 1,964 faces across the three CI machines.
- **Index every localized family record** (`name` IDs 1 and 16, all languages) as a lookup alias. English (Windows `langID 0x409`, Mac `langID 0`) is used only to canonicalise and break ties — never to filter. Filtering to English makes CJK families unmatchable.
- **Public API changes must be additive.** `ISystemFontProvider` is public; new members get default implementations.
- **Existing behaviour must not move on Windows.** The GWG render-hash gate is 51/51 there and the baselines were captured on that machine.

---

## File Structure

| File | Responsibility |
|---|---|
| `PdfLibrary/Fonts/FontFaceRecord.cs` (new) | Immutable record of one face's identity and style |
| `PdfLibrary/Fonts/SfntNameReader.cs` (new) | Parse `name`/`head` out of sfnt bytes; enumerate `.ttc` faces |
| `PdfLibrary/Fonts/FontMetadataIndex.cs` (new) | Scan directories, build the lookups, pick best face by style |
| `PdfLibrary/Fonts/Base35Aliases.cs` (new) | `/BaseFont` splitting + the PostScript base-35 alias table |
| `PdfLibrary/Fonts/FontRequest.cs` (new) | `FontRequest` and `FontMatch` DTOs |
| `PdfLibrary/Fonts/ISystemFontProvider.cs` (modify) | Add `Resolve` as a default interface method |
| `PdfLibrary/Fonts/SystemFontLocator.cs` (modify) | Override `Resolve` with the ladder; back the legacy members with the new index |
| `PdfLibrary/Fonts/SubstituteFontResolver.cs` (modify) | Call `Resolve`; keep the existing face-selection scoring |

---

### Task 1: Parse a face's identity out of sfnt bytes

**Files:**
- Create: `PdfLibrary/Fonts/FontFaceRecord.cs`
- Create: `PdfLibrary/Fonts/SfntNameReader.cs`
- Test: `PdfLibrary.Tests/Fonts/SfntNameReaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `internal sealed record FontFaceRecord(string Path, int FaceIndex, string PostScriptName, IReadOnlyCollection<string> Families, string EnglishFamily, string Subfamily, bool Italic, bool Bold)`
  - `internal static class SfntNameReader` with `public static int FaceCount(byte[] data)` and `public static FontFaceRecord? ReadFace(byte[] data, int faceIndex, string path)`

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/SfntNameReaderTests.cs`:

```csharp
using System.Text;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class SfntNameReaderTests
{
    /// <summary>Builds a minimal but structurally valid sfnt carrying ONLY a `name` and a `head`
    /// table. SfntNameReader reads nothing else, so this is sufficient and keeps the fixtures
    /// readable — a real font would bury the fields under a megabyte of glyph data.</summary>
    private static byte[] Sfnt(int macStyle, params (int platformId, int langId, int nameId, string value)[] names)
    {
        var storage = new List<byte>();
        var records = new List<byte>();
        foreach ((int pid, int lang, int nid, string v) in names)
        {
            byte[] bytes = pid == 3 ? Encoding.BigEndianUnicode.GetBytes(v) : Encoding.ASCII.GetBytes(v);
            AddU16(records, pid);
            AddU16(records, pid == 3 ? 1 : 0);   // encodingID
            AddU16(records, lang);
            AddU16(records, nid);
            AddU16(records, bytes.Length);
            AddU16(records, storage.Count);      // offset into storage
            storage.AddRange(bytes);
        }

        var name = new List<byte>();
        AddU16(name, 0);                          // format
        AddU16(name, names.Length);               // count
        AddU16(name, 6 + records.Count);          // stringOffset
        name.AddRange(records);
        name.AddRange(storage);

        var head = new byte[54];
        head[44] = (byte)(macStyle >> 8);
        head[45] = (byte)(macStyle & 0xFF);

        const int numTables = 2;
        int dirSize = 12 + numTables * 16;
        int headOff = dirSize;
        int nameOff = headOff + head.Length;

        var f = new List<byte>();
        f.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x00 });   // sfntVersion 1.0
        AddU16(f, numTables);
        AddU16(f, 0); AddU16(f, 0); AddU16(f, 0);            // searchRange/entrySelector/rangeShift
        f.AddRange(Encoding.ASCII.GetBytes("head")); AddU32(f, 0); AddU32(f, (uint)headOff); AddU32(f, (uint)head.Length);
        f.AddRange(Encoding.ASCII.GetBytes("name")); AddU32(f, 0); AddU32(f, (uint)nameOff); AddU32(f, (uint)name.Count);
        f.AddRange(head);
        f.AddRange(name);
        return f.ToArray();
    }

    private static void AddU16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void AddU32(List<byte> b, uint v)
    { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    [Fact]
    public void Reads_postscript_name_family_and_style()
    {
        byte[] data = Sfnt(0x0002,
            (3, 0x409, 1, "Test Family"),
            (3, 0x409, 2, "Italic"),
            (3, 0x409, 6, "TestFamily-Italic"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.NotNull(face);
        Assert.Equal("TestFamily-Italic", face!.PostScriptName);
        Assert.Equal("Test Family", face.EnglishFamily);
        Assert.True(face.Italic);
        Assert.False(face.Bold);
    }

    [Fact]
    public void Indexes_every_localized_family_not_just_english()
    {
        byte[] data = Sfnt(0,
            (3, 0x409, 1, "Hiragino Mincho ProN"),
            (3, 0x411, 1, "ヒラギノ明朝 ProN"),
            (3, 0x409, 6, "HiraMinProN-W3"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.NotNull(face);
        Assert.Contains("ヒラギノ明朝 ProN", face!.Families);
        Assert.Contains("Hiragino Mincho ProN", face.Families);
        Assert.Equal("Hiragino Mincho ProN", face.EnglishFamily);
    }

    [Fact]
    public void English_family_wins_regardless_of_record_order()
    {
        // The Spanish record comes FIRST. Taking "the first ID 1" would canonicalise to it and make
        // the index locale-dependent across machines — observed on a real box as "Times New Roman
        // cursiva".
        byte[] data = Sfnt(0,
            (3, 0x0C0A, 1, "Times New Roman cursiva"),
            (3, 0x409, 1, "Times New Roman"),
            (3, 0x409, 6, "TimesNewRomanPSMT"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.Equal("Times New Roman", face!.EnglishFamily);
    }

    [Fact]
    public void FaceCount_is_one_for_a_bare_sfnt()
    {
        Assert.Equal(1, SfntNameReader.FaceCount(Sfnt(0, (3, 0x409, 6, "X"))));
    }

    [Fact]
    public void Malformed_data_returns_null_rather_than_throwing()
    {
        Assert.Null(SfntNameReader.ReadFace([0x00, 0x01], 0, "truncated.ttf"));
        Assert.Null(SfntNameReader.ReadFace([], 0, "empty.ttf"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter SfntNameReaderTests`
Expected: FAIL — `SfntNameReader` and `FontFaceRecord` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/FontFaceRecord.cs`:

```csharp
namespace PdfLibrary.Fonts;

/// <summary>One face of one installed font file, identified by the fields a substitution decision
/// actually needs. <paramref name="PostScriptName"/> (name ID 6) is the primary key: ASCII by
/// specification, free of language variants, and exactly what a PDF's /BaseFont derives from.
/// <paramref name="Families"/> holds EVERY localized ID 1 / ID 16 record so a document naming a font
/// by its localized family still resolves; <paramref name="EnglishFamily"/> is only for
/// canonicalisation and deterministic tie-breaking.</summary>
internal sealed record FontFaceRecord(
    string Path,
    int FaceIndex,
    string PostScriptName,
    IReadOnlyCollection<string> Families,
    string EnglishFamily,
    string Subfamily,
    bool Italic,
    bool Bold);
```

Create `PdfLibrary/Fonts/SfntNameReader.cs`:

```csharp
using System.Text;

namespace PdfLibrary.Fonts;

/// <summary>Reads a face's identity from sfnt bytes using only the table directory, `name` and
/// `head`. Deliberately does NOT use FontParser.SfntFont: this runs over every installed font at
/// index time, and it must not pay for parsing glyph data it will never look at.</summary>
internal static class SfntNameReader
{
    /// <summary>Number of faces: the `ttcf` header's count for a collection, otherwise 1.</summary>
    public static int FaceCount(byte[] data)
    {
        if (data.Length < 12) return 0;
        if (!IsTtc(data)) return 1;
        var n = (int)U32(data, 8);
        return n is > 0 and < 0x10000 ? n : 0;
    }

    public static FontFaceRecord? ReadFace(byte[] data, int faceIndex, string path)
    {
        try
        {
            long b = 0;
            if (IsTtc(data))
            {
                if (faceIndex >= FaceCount(data)) return null;
                b = U32(data, 12 + faceIndex * 4);
            }
            else if (faceIndex != 0) return null;

            if (b + 12 > data.Length) return null;
            int numTables = U16(data, b + 4);

            long nameOff = 0, headOff = 0;
            for (var i = 0; i < numTables; i++)
            {
                long rec = b + 12 + i * 16;
                if (rec + 16 > data.Length) return null;
                // Table offsets inside a .ttc are FILE-absolute, not face-relative.
                long off = U32(data, rec + 8);
                if (Tag(data, rec) == "name") nameOff = off;
                else if (Tag(data, rec) == "head") headOff = off;
            }
            if (nameOff == 0 || nameOff + 6 > data.Length) return null;

            var macStyle = 0;
            if (headOff > 0 && headOff + 46 <= data.Length) macStyle = U16(data, headOff + 44);

            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ps = "", english = "", subfamily = "";
            int count = U16(data, nameOff + 2), storage = U16(data, nameOff + 4);
            for (var i = 0; i < count; i++)
            {
                long r = nameOff + 6 + i * 12;
                if (r + 12 > data.Length) break;
                int pid = U16(data, r), lang = U16(data, r + 4), nid = U16(data, r + 6);
                int len = U16(data, r + 8), off = U16(data, r + 10);
                if (nid is not (1 or 2 or 6 or 16 or 17)) continue;

                long s = nameOff + storage + off;
                if (len == 0 || s + len > data.Length) continue;
                string v = (pid == 3
                    ? Encoding.BigEndianUnicode.GetString(data, (int)s, len)
                    : Encoding.ASCII.GetString(data, (int)s, len)).Trim('\0').Trim();
                if (v.Length == 0) continue;

                bool isEnglish = (pid == 3 && lang == 0x409) || (pid == 1 && lang == 0);
                switch (nid)
                {
                    case 6 when ps.Length == 0 || isEnglish: ps = v; break;
                    case 1 or 16:
                        families.Add(v);
                        // An English record always wins, whatever the record order.
                        if (isEnglish || english.Length == 0) english = v;
                        break;
                    case 2 or 17 when subfamily.Length == 0 || isEnglish: subfamily = v; break;
                }
            }
            if (ps.Length == 0 && families.Count == 0) return null;

            bool italic = (macStyle & 0x2) != 0
                       || subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                       || subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
            bool bold = (macStyle & 0x1) != 0
                     || subfamily.Contains("Bold", StringComparison.OrdinalIgnoreCase);

            return new FontFaceRecord(path, faceIndex, ps, families, english, subfamily, italic, bold);
        }
        catch
        {
            // A malformed font must not break indexing of the other 700.
            return null;
        }
    }

    private static bool IsTtc(byte[] d) =>
        d.Length >= 4 && d[0] == 't' && d[1] == 't' && d[2] == 'c' && d[3] == 'f';

    private static string Tag(byte[] d, long i) => Encoding.ASCII.GetString(d, (int)i, 4);
    private static int U16(byte[] d, long i) => (d[i] << 8) | d[i + 1];
    private static uint U32(byte[] d, long i) =>
        ((uint)d[i] << 24) | ((uint)d[i + 1] << 16) | ((uint)d[i + 2] << 8) | d[i + 3];
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter SfntNameReaderTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/FontFaceRecord.cs PdfLibrary/Fonts/SfntNameReader.cs PdfLibrary.Tests/Fonts/SfntNameReaderTests.cs
git commit -m "feat(fonts): read face identity from sfnt name and head tables"
```

---

### Task 2: Index every installed face

**Files:**
- Create: `PdfLibrary/Fonts/FontMetadataIndex.cs`
- Test: `PdfLibrary.Tests/Fonts/FontMetadataIndexTests.cs`

**Interfaces:**
- Consumes: `FontFaceRecord`, `SfntNameReader.FaceCount`, `SfntNameReader.ReadFace` (Task 1).
- Produces: `internal sealed class FontMetadataIndex` with
  - `public FontMetadataIndex(IEnumerable<string> directories)`
  - `public IReadOnlyList<FontFaceRecord> Faces { get; }`
  - `public FontFaceRecord? ByPostScriptName(string name)`
  - `public IReadOnlyList<FontFaceRecord> ByFamily(string family)`
  - `public string? FindPath(string fileBaseName)`
  - `public IReadOnlyCollection<string> FileBaseNames { get; }`
  - `public static FontFaceRecord? PickBest(IEnumerable<FontFaceRecord> candidates, bool bold, bool italic)`

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/FontMetadataIndexTests.cs`:

```csharp
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontMetadataIndexTests
{
    private static FontFaceRecord Face(string ps, string family, bool italic, bool bold, int index = 0) =>
        new("f.ttf", index, ps, [family], family, italic ? "Italic" : "Regular", italic, bold);

    [Fact]
    public void PickBest_prefers_the_face_matching_both_style_bits()
    {
        FontFaceRecord[] faces =
        [
            Face("F-Regular", "F", italic: false, bold: false, index: 0),
            Face("F-Bold", "F", italic: false, bold: true, index: 1),
            Face("F-Italic", "F", italic: true, bold: false, index: 2),
            Face("F-BoldItalic", "F", italic: true, bold: true, index: 3),
        ];

        Assert.Equal("F-Italic", FontMetadataIndex.PickBest(faces, bold: false, italic: true)!.PostScriptName);
        Assert.Equal("F-BoldItalic", FontMetadataIndex.PickBest(faces, bold: true, italic: true)!.PostScriptName);
        Assert.Equal("F-Regular", FontMetadataIndex.PickBest(faces, bold: false, italic: false)!.PostScriptName);
    }

    [Fact]
    public void PickBest_degrades_rather_than_failing_when_the_style_is_absent()
    {
        // Italic requested, none available: keep the regular rather than returning nothing.
        FontFaceRecord[] faces =
        [
            Face("F-Regular", "F", italic: false, bold: false, index: 0),
            Face("F-Bold", "F", italic: false, bold: true, index: 1),
        ];

        Assert.Equal("F-Regular", FontMetadataIndex.PickBest(faces, bold: false, italic: true)!.PostScriptName);
    }

    [Fact]
    public void PickBest_breaks_ties_on_lowest_face_index()
    {
        // Indistinguishable faces must resolve the way they did before this index existed.
        FontFaceRecord[] faces = [Face("B", "F", false, false, index: 3), Face("A", "F", false, false, index: 1)];

        Assert.Equal("A", FontMetadataIndex.PickBest(faces, bold: false, italic: false)!.PostScriptName);
    }

    [Fact]
    public void PickBest_of_nothing_is_null()
    {
        Assert.Null(FontMetadataIndex.PickBest([], bold: false, italic: false));
    }

    [Fact]
    public void Indexes_the_real_system_fonts_by_postscript_name()
    {
        var index = new FontMetadataIndex(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(index.Faces.Count == 0, "no system fonts on this machine");

        // Measured on all three CI machines: 100% of faces carry a PostScript name.
        Assert.All(index.Faces, f => Assert.NotEmpty(f.PostScriptName));

        FontFaceRecord first = index.Faces[0];
        Assert.Same(first, index.ByPostScriptName(first.PostScriptName));
    }

    [Fact]
    public void Missing_directories_are_skipped_not_thrown()
    {
        var index = new FontMetadataIndex(["/definitely/not/a/real/path", ""]);
        Assert.Empty(index.Faces);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter FontMetadataIndexTests`
Expected: FAIL — `FontMetadataIndex` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/FontMetadataIndex.cs`:

```csharp
namespace PdfLibrary.Fonts;

/// <summary>Every face of every font file found in the given directories, indexed by PostScript name
/// and by every localized family name.
///
/// <para>Supersedes the filename-only lookup that preceded it: on the Windows dev box 755 faces are
/// installed and only the ~40 hardcoded candidate filenames were ever reachable. Building this costs
/// ~42 ms serially for 732 files (measured 2026-08-04) because only the sfnt header, table directory,
/// `name` and `head` are read — reading the files whole measured 591 ms.</para>
///
/// <para>Constructed once per process via <see cref="SystemFontLocator.Default"/>. Rebuilding it per
/// renderer would repeat the mistake that once made directory scanning 86% of page-record time, since
/// Type3 fonts construct a sub-renderer per glyph.</para></summary>
internal sealed class FontMetadataIndex
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];

    private readonly List<FontFaceRecord> _faces = [];
    private readonly Dictionary<string, FontFaceRecord> _byPostScript = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FontFaceRecord>> _byFamily = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byFileBaseName = new(StringComparer.OrdinalIgnoreCase);

    public FontMetadataIndex(IEnumerable<string> directories)
    {
        foreach (string dir in directories)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (string file in files)
            {
                if (Array.IndexOf(Extensions, Path.GetExtension(file).ToLowerInvariant()) < 0) continue;
                // First writer wins, so earlier directories take precedence - as before.
                _byFileBaseName.TryAdd(Path.GetFileNameWithoutExtension(file), file);

                byte[] data;
                try { data = File.ReadAllBytes(file); }
                catch { continue; }

                int faceCount = SfntNameReader.FaceCount(data);
                for (var i = 0; i < faceCount; i++)
                {
                    FontFaceRecord? face = SfntNameReader.ReadFace(data, i, file);
                    if (face is null) continue;
                    _faces.Add(face);

                    if (face.PostScriptName.Length > 0)
                        _byPostScript.TryAdd(face.PostScriptName, face);

                    foreach (string family in face.Families)
                    {
                        string key = Normalize(family);
                        if (!_byFamily.TryGetValue(key, out List<FontFaceRecord>? list))
                            _byFamily[key] = list = [];
                        list.Add(face);
                    }
                }
            }
        }
    }

    public IReadOnlyList<FontFaceRecord> Faces => _faces;

    public FontFaceRecord? ByPostScriptName(string name) =>
        _byPostScript.TryGetValue(name, out FontFaceRecord? f) ? f : null;

    public IReadOnlyList<FontFaceRecord> ByFamily(string family) =>
        _byFamily.TryGetValue(Normalize(family), out List<FontFaceRecord>? list) ? list : [];

    /// <summary>Full path of the indexed font whose FILE base name matches. Retained because
    /// <see cref="ISystemFontProvider.IsFontAvailable"/> and
    /// <see cref="ISystemFontProvider.FindFirstAvailable"/> document their parameter as a file base
    /// name, and that contract must not change.</summary>
    public string? FindPath(string fileBaseName) =>
        _byFileBaseName.TryGetValue(fileBaseName, out string? p) ? p : null;

    public IReadOnlyCollection<string> FileBaseNames => _byFileBaseName.Keys;

    /// <summary>Best style match: +1 for italic agreement, +1 for bold agreement. Scored rather than
    /// matched exactly so a family lacking the requested combination degrades to its nearest face
    /// instead of failing. Ties keep the LOWEST face index, so a set of indistinguishable faces
    /// resolves exactly as it did before this index existed.</summary>
    public static FontFaceRecord? PickBest(IEnumerable<FontFaceRecord> candidates, bool bold, bool italic)
    {
        FontFaceRecord? best = null;
        var bestScore = -1;
        foreach (FontFaceRecord f in candidates)
        {
            int score = (f.Italic == italic ? 1 : 0) + (f.Bold == bold ? 1 : 0);
            if (best is not null && (score < bestScore || (score == bestScore && f.FaceIndex >= best.FaceIndex)))
                continue;
            best = f;
            bestScore = score;
        }
        return best;
    }

    private static string Normalize(string s) => s.Replace(" ", string.Empty);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter FontMetadataIndexTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/FontMetadataIndex.cs PdfLibrary.Tests/Fonts/FontMetadataIndexTests.cs
git commit -m "feat(fonts): index every installed face by PostScript name and family"
```

---

### Task 3: The base-35 alias table

**Files:**
- Create: `PdfLibrary/Fonts/Base35Aliases.cs`
- Test: `PdfLibrary.Tests/Fonts/Base35AliasesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class Base35Aliases` with
  - `public static (string Family, bool Bold, bool Italic) Split(string baseFont)`
  - `public static IReadOnlyList<string> FamiliesFor(string family)`

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/Base35AliasesTests.cs`:

```csharp
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class Base35AliasesTests
{
    [Fact]
    public void Split_strips_the_subset_tag()
    {
        Assert.Equal("MyriadPro", Base35Aliases.Split("BOXDGO+MyriadPro-Regular").Family);
    }

    [Fact]
    public void Split_reads_style_from_the_name()
    {
        Assert.True(Base35Aliases.Split("NewCenturySchlbk-Italic").Italic);
        Assert.False(Base35Aliases.Split("NewCenturySchlbk-Italic").Bold);
        Assert.True(Base35Aliases.Split("Helvetica-BoldOblique").Bold);
        Assert.True(Base35Aliases.Split("Helvetica-BoldOblique").Italic);
        Assert.False(Base35Aliases.Split("Times-Roman").Italic);
    }

    [Fact]
    public void Split_treats_a_comma_as_a_style_separator()
    {
        // Windows-authored PDFs use "Arial,Bold" rather than "Arial-Bold".
        Assert.True(Base35Aliases.Split("Arial,BoldItalic").Bold);
        Assert.True(Base35Aliases.Split("Arial,BoldItalic").Italic);
        Assert.Equal("Arial", Base35Aliases.Split("Arial,BoldItalic").Family);
    }

    [Fact]
    public void NewCenturySchlbk_aliases_to_C059()
    {
        // Ghostscript Fontmap.GS: /NewCenturySchlbk-Italic /C059-Italic ;
        Assert.Contains("C059", Base35Aliases.FamiliesFor("NewCenturySchlbk"));
    }

    [Fact]
    public void Standard14_families_alias_to_their_clones_in_preference_order()
    {
        IReadOnlyList<string> times = Base35Aliases.FamiliesFor("Times");
        Assert.Contains("Nimbus Roman", times);
        Assert.Contains("Liberation Serif", times);
        Assert.Contains("Times New Roman", times);
    }

    [Fact]
    public void An_unknown_family_aliases_to_itself()
    {
        Assert.Equal(["Garamond"], Base35Aliases.FamiliesFor("Garamond"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter Base35AliasesTests`
Expected: FAIL — `Base35Aliases` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/Base35Aliases.cs`:

```csharp
namespace PdfLibrary.Fonts;

/// <summary>Maps a PDF /BaseFont family onto the internal family names that could satisfy it.
///
/// <para>The table is the PostScript base-35 alias set, taken from Ghostscript's
/// <c>Resource/Init/Fontmap.GS</c> — the de-facto reference every renderer follows. Without it
/// <c>NewCenturySchlbk-Italic</c> falls through to a Times italic even on machines that have the real
/// New Century Schoolbook clone installed, which was measured on two of the three CI boxes.</para></summary>
internal static class Base35Aliases
{
    private static readonly Dictionary<string, string[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["times"] = ["Nimbus Roman", "Liberation Serif", "Times New Roman", "Times", "Tinos"],
        ["timesroman"] = ["Nimbus Roman", "Liberation Serif", "Times New Roman", "Times", "Tinos"],
        ["timesnewroman"] = ["Times New Roman", "Liberation Serif", "Nimbus Roman", "Tinos"],
        ["helvetica"] = ["Nimbus Sans", "Liberation Sans", "Arial", "Helvetica", "Arimo"],
        ["arial"] = ["Arial", "Liberation Sans", "Nimbus Sans", "Arimo"],
        ["courier"] = ["Nimbus Mono PS", "Liberation Mono", "Courier New", "Courier", "Cousine"],
        ["couriernew"] = ["Courier New", "Liberation Mono", "Nimbus Mono PS", "Cousine"],
        ["newcenturyschlbk"] = ["C059", "Century Schoolbook L", "New Century Schoolbook", "Century Schoolbook"],
        ["centuryschoolbook"] = ["C059", "Century Schoolbook L", "Century Schoolbook"],
        ["palatino"] = ["P052", "URW Palladio L", "Palatino Linotype", "Palatino"],
        ["bookman"] = ["URW Bookman", "Bookman Old Style", "Bookman"],
        ["avantgarde"] = ["URW Gothic", "Century Gothic", "AvantGarde"],
        ["zapfchancery"] = ["Z003", "URW Chancery L", "Zapf Chancery"],
        ["symbol"] = ["Symbol", "Standard Symbols PS", "StandardSymbolsPS"],
        ["zapfdingbats"] = ["D050000L", "Dingbats", "ZapfDingbats"],
    };

    /// <summary>Splits a /BaseFont into family and style. Handles the <c>ABCDEF+</c> subset tag and
    /// both the PostScript (<c>Arial-Bold</c>) and Windows (<c>Arial,Bold</c>) style separators.</summary>
    public static (string Family, bool Bold, bool Italic) Split(string baseFont)
    {
        string n = baseFont ?? "";
        if (n.Length > 7 && n[6] == '+') n = n[7..];

        var style = "";
        int sep = n.IndexOfAny(['-', ',']);
        if (sep > 0) { style = n[(sep + 1)..]; n = n[..sep]; }

        bool bold = style.Contains("Bold", StringComparison.OrdinalIgnoreCase);
        bool italic = style.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                   || style.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        return (n, bold, italic);
    }

    /// <summary>Internal family names that could satisfy <paramref name="family"/>, best first. An
    /// unknown family aliases to itself, so a document asking for an installed font gets it.</summary>
    public static IReadOnlyList<string> FamiliesFor(string family)
    {
        string key = (family ?? "").Replace(" ", string.Empty);
        return Table.TryGetValue(key, out string[]? aliases) ? aliases : [family ?? ""];
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter Base35AliasesTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/Base35Aliases.cs PdfLibrary.Tests/Fonts/Base35AliasesTests.cs
git commit -m "feat(fonts): add the PostScript base-35 alias table"
```

---

### Task 4: The resolution ladder on the provider

**Files:**
- Create: `PdfLibrary/Fonts/FontRequest.cs`
- Modify: `PdfLibrary/Fonts/ISystemFontProvider.cs`
- Modify: `PdfLibrary/Fonts/SystemFontLocator.cs`
- Test: `PdfLibrary.Tests/Fonts/FontResolutionLadderTests.cs`

**Interfaces:**
- Consumes: `FontMetadataIndex` (Task 2), `Base35Aliases` (Task 3), and the existing
  `SubstituteFontResolver.Classify` / `SyntheticStd14Name`.
- Produces:
  - `public sealed record FontRequest(string BaseFont, bool Bold, bool Italic)`
  - `public sealed record FontMatch(byte[] Data, int FaceIndex)`
  - `ISystemFontProvider.Resolve(FontRequest) → FontMatch?` (default implementation)
  - `SystemFontLocator.Resolve` override implementing steps 1–3.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/FontResolutionLadderTests.cs`:

```csharp
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontResolutionLadderTests
{
    [Fact]
    public void An_unmatchable_request_returns_null_and_adds_no_floor()
    {
        // SLICE 1 BOUNDARY. Slice 2 introduces a bundled Liberation floor and will deliberately
        // invert this assertion. Until then, failing every step must behave exactly as the engine
        // does today: return null, and let the caller draw nothing.
        var locator = new SystemFontLocator(["/definitely/not/a/real/path"]);

        Assert.Null(locator.Resolve(new FontRequest("NoSuchFontAnywhere", Bold: false, Italic: false)));
    }

    [Fact]
    public void Resolves_a_standard14_request_against_the_real_system_fonts()
    {
        var locator = new SystemFontLocator(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(locator.GetAvailableFontFamilies().Count == 0, "no system fonts on this machine");

        FontMatch? match = locator.Resolve(new FontRequest("Helvetica", Bold: false, Italic: false));

        Assert.NotNull(match);
        Assert.NotEmpty(match!.Data);
        Assert.True(match.FaceIndex >= 0);
    }

    [Fact]
    public void An_italic_request_resolves_to_a_face_whose_style_bit_is_set()
    {
        var locator = new SystemFontLocator(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(locator.GetAvailableFontFamilies().Count == 0, "no system fonts on this machine");

        FontMatch? match = locator.Resolve(new FontRequest("Times-Italic", Bold: false, Italic: true));
        Assert.NotNull(match);

        // The whole point of the ladder: the returned FACE must itself be italic, not merely a file
        // whose name looked right. This is the defect class that shipped on macOS.
        var metrics = new PdfLibrary.Fonts.Embedded.EmbeddedFontMetrics(match!.Data, match.FaceIndex);
        Assert.True(metrics.IsItalic);
    }

    [Fact]
    public void The_default_interface_implementation_keeps_existing_providers_working()
    {
        // An ISystemFontProvider written before Resolve existed must still function.
        ISystemFontProvider legacy = new LegacyProvider();

        FontMatch? match = legacy.Resolve(new FontRequest("Anything", Bold: false, Italic: false));

        Assert.NotNull(match);
        Assert.Equal(new byte[] { 1, 2, 3 }, match!.Data);
        Assert.Equal(0, match.FaceIndex);
    }

    private sealed class LegacyProvider : ISystemFontProvider
    {
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public byte[]? GetFontData(string baseFontName) => [1, 2, 3];
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter FontResolutionLadderTests`
Expected: FAIL — `FontRequest`, `FontMatch` and `Resolve` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Fonts/FontRequest.cs`:

```csharp
namespace PdfLibrary.Fonts;

/// <summary>A request to substitute a font the renderer could not use from the PDF itself.</summary>
public sealed record FontRequest(string BaseFont, bool Bold, bool Italic);

/// <summary>A resolved substitute. <paramref name="FaceIndex"/> matters: on macOS the core families
/// live in .ttc collections where Regular/Bold/Italic/BoldItalic share one file, so bytes alone
/// cannot express which face was chosen.</summary>
public sealed record FontMatch(byte[] Data, int FaceIndex);
```

Modify `PdfLibrary/Fonts/ISystemFontProvider.cs` — append inside the interface, after `GetFontData`:

```csharp
    /// <summary>
    /// Resolves a substitute for <paramref name="request"/>, including which face of a collection to
    /// use. The default implementation delegates to <see cref="GetFontData"/> and face 0, so
    /// providers written before this member existed keep working unchanged.
    /// </summary>
    FontMatch? Resolve(FontRequest request) =>
        GetFontData(request.BaseFont) is { } bytes ? new FontMatch(bytes, 0) : null;
```

Modify `PdfLibrary/Fonts/SystemFontLocator.cs` — replace the `_index` field, the constructor body and `GetFontData`/`GetAvailableFontFamilies`/`IsFontAvailable` with the index-backed versions, and add `Resolve`:

```csharp
    private readonly FontMetadataIndex _index;

    /// <summary>Create a locator that scans the given directories (used for testing).</summary>
    public SystemFontLocator(IEnumerable<string> directories)
    {
        _index = new FontMetadataIndex(directories as string[] ?? directories.ToArray());
    }

    /// <inheritdoc/>
    public byte[]? GetFontData(string baseFontName)
    {
        foreach (string candidate in Standard14Fonts.SubstituteFileBaseNames(baseFontName))
        {
            string? path = _index.FindPath(candidate);
            if (path is null) continue;
            try { return File.ReadAllBytes(path); }
            catch { /* path exists but is unreadable — try the next candidate */ }
        }
        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetAvailableFontFamilies() => _index.FileBaseNames;

    /// <inheritdoc/>
    public bool IsFontAvailable(string familyName) => _index.FindPath(familyName) is not null;

    /// <summary>The metadata ladder. Step 1 PostScript name, step 2 aliased family, step 3 the
    /// synthetic standard-14 name — each matched against the font's OWN metadata rather than against
    /// a filename. Returns null when all three miss; slice 1 adds no fallback floor.</summary>
    public FontMatch? Resolve(FontRequest request)
    {
        (string family, bool nameBold, bool nameItalic) = Base35Aliases.Split(request.BaseFont);
        bool bold = request.Bold || nameBold;
        bool italic = request.Italic || nameItalic;

        string stripped = request.BaseFont.Length > 7 && request.BaseFont[6] == '+'
            ? request.BaseFont[7..]
            : request.BaseFont;

        // Step 1: exact PostScript name. ASCII by spec and language-free, so this is the one lookup
        // that cannot be confounded by localisation.
        FontFaceRecord? hit = _index.ByPostScriptName(stripped);

        // Step 2: aliased family, best style match.
        if (hit is null)
            hit = FirstFamilyHit(Base35Aliases.FamiliesFor(family), bold, italic);

        // Step 3: the synthetic standard-14 name, by PostScript name then by aliased family. This is
        // what keeps a machine with no base-35 clones on its own core serif/sans/mono.
        if (hit is null)
        {
            (bool serif, bool mono, bool _, bool _) = SubstituteFontResolver.Classify(request.BaseFont, null);
            string synthetic = SubstituteFontResolver.SyntheticStd14Name(serif, mono, bold, italic);
            (string synthFamily, bool _, bool _) = Base35Aliases.Split(synthetic);
            hit = _index.ByPostScriptName(synthetic)
               ?? FirstFamilyHit(Base35Aliases.FamiliesFor(synthFamily), bold, italic);
        }

        if (hit is null) return null;
        try { return new FontMatch(File.ReadAllBytes(hit.Path), hit.FaceIndex); }
        catch { return null; }
    }

    private FontFaceRecord? FirstFamilyHit(IReadOnlyList<string> families, bool bold, bool italic)
    {
        foreach (string family in families)
        {
            IReadOnlyList<FontFaceRecord> candidates = _index.ByFamily(family);
            if (candidates.Count == 0) continue;
            FontFaceRecord? best = FontMetadataIndex.PickBest(candidates, bold, italic);
            if (best is not null) return best;
        }
        return null;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter FontResolutionLadderTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Run the whole engine suite for regressions**

Run: `dotnet test PdfLibrary.Tests --framework net10.0`
Expected: PASS. Baseline before this plan: 2802 passed, 0 failed, 3 skipped, plus the tests added in Tasks 1–3.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Fonts/FontRequest.cs PdfLibrary/Fonts/ISystemFontProvider.cs PdfLibrary/Fonts/SystemFontLocator.cs PdfLibrary.Tests/Fonts/FontResolutionLadderTests.cs
git commit -m "feat(fonts): resolve substitutes through a metadata ladder"
```

---

### Task 5: Route the substitute resolver through the ladder

**Files:**
- Modify: `PdfLibrary/Fonts/SubstituteFontResolver.cs:20-34` (the `Load` method)
- Test: `PdfLibrary.Tests/Fonts/SubstituteFontResolverLadderTests.cs`

**Interfaces:**
- Consumes: `ISystemFontProvider.Resolve`, `FontRequest`, `FontMatch` (Task 4).
- Produces: no new public surface. `SubstituteFontResolver.Resolve(string, PdfFontDescriptor?)` keeps its signature.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/SubstituteFontResolverLadderTests.cs`:

```csharp
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts;

public class SubstituteFontResolverLadderTests
{
    /// <summary>Returns a fixed face for any request, recording what it was asked for.</summary>
    private sealed class RecordingProvider(byte[] data, int faceIndex) : ISystemFontProvider
    {
        public FontRequest? LastRequest { get; private set; }
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public byte[]? GetFontData(string baseFontName) => null;
        public FontMatch? Resolve(FontRequest request)
        {
            LastRequest = request;
            return new FontMatch(data, faceIndex);
        }
    }

    private sealed class NullProvider : ISystemFontProvider
    {
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public byte[]? GetFontData(string baseFontName) => null;
        public FontMatch? Resolve(FontRequest request) => null;
    }

    private static byte[] RealFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    [Fact]
    public void Load_asks_the_provider_to_resolve_rather_than_fetching_raw_bytes()
    {
        var provider = new RecordingProvider(RealFont(), 0);
        var resolver = new SubstituteFontResolver(provider);

        EmbeddedFontMetrics? metrics = resolver.Resolve("NewCenturySchlbk-Italic", null);

        Assert.NotNull(metrics);
        Assert.NotNull(provider.LastRequest);
        Assert.Equal("NewCenturySchlbk-Italic", provider.LastRequest!.BaseFont);
        Assert.True(provider.LastRequest.Italic);
    }

    [Fact]
    public void A_provider_that_resolves_nothing_yields_null()
    {
        var resolver = new SubstituteFontResolver(new NullProvider());

        Assert.Null(resolver.Resolve("NoSuchFont", null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter SubstituteFontResolverLadderTests`
Expected: FAIL — `Load` still calls `GetFontData`, so `LastRequest` is null.

- [ ] **Step 3: Write the implementation**

In `PdfLibrary/Fonts/SubstituteFontResolver.cs`, replace the body of `Load` with:

```csharp
    private EmbeddedFontMetrics? Load(string baseFont, PdfFontDescriptor? descriptor)
    {
        // Style comes from the descriptor AND the name; Classify already merges both. The provider
        // owns the ladder from here — this method no longer knows anything about filenames.
        (bool _, bool _, bool bold, bool italic) = Classify(baseFont, descriptor);

        FontMatch? match = provider.Resolve(new FontRequest(baseFont, bold, italic));
        if (match is null) return null;

        var metrics = new EmbeddedFontMetrics(match.Data, match.FaceIndex);
        return metrics.IsValid ? metrics : null;
    }
```

Delete the now-unused `SelectFace` and `Score` methods from `SubstituteFontResolver` — face selection has moved into the ladder, which picks the face from metadata rather than by re-parsing every face of the chosen file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests --framework net10.0 --filter SubstituteFontResolverLadderTests`
Expected: PASS, 2 tests.

- [ ] **Step 5: Run the whole engine suite**

Run: `dotnet test PdfLibrary.Tests --framework net10.0`
Expected: PASS, 0 failed. `SubstituteFontFaceSelectionTests` (the collection face-selection tests from `6afbe7a`) must still pass — they exercise the same behaviour through the new path.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Fonts/SubstituteFontResolver.cs PdfLibrary.Tests/Fonts/SubstituteFontResolverLadderTests.cs
git commit -m "refactor(fonts): route substitution through the metadata ladder"
```

---

### Task 6: Verify the gates on all three platforms

**Files:**
- Modify: `Pellucid.Rendering.Avalonia.Tests/Cmyk/gwg-render-hash-baseline.txt` (in the **Pellucid** repo, only if the crop justifies it)
- Modify: `Pellucid.Rendering.Avalonia.Tests/Cmyk/ghent-scoreboard-baseline.txt` (same)
- Modify: `Pellucid/ci/dependencies.json` (engine pin)

**Interfaces:**
- Consumes: everything above.
- Produces: re-pinned baselines, or evidence that none were needed.

- [ ] **Step 1: Run the render-hash gates on Windows**

```bash
cd C:/Users/jorda/RiderProjects/Pellucid
dotnet test Pellucid.Rendering.Avalonia.Tests --framework net10.0 --filter "GwgRenderHashGate|NChannelRenderHashGate|GhentScoreboard"
```

Expected: exactly ONE fixture moves — `1-CMYK/GWG090_Font-Support_x3.pdf` — and exactly ONE panel — `p3/s0 [9.0]`. The measured prediction is that it resolves to `C059-Italic` rather than `timesi.ttf`. **Any other movement blocks this task**: report it, do not re-pin.

- [ ] **Step 2: Dump and VIEW the crop**

```bash
SCOREBOARD_DUMP=/tmp/crops dotnet test Pellucid.Rendering.Avalonia.Tests --framework net10.0 --filter GhentScoreboard
```

Open `/tmp/crops/p3-s0-9.0.png` and compare the "Type1 PostScript:" row against the "Expected result:" row directly beneath it — that embedded reference row is the page's own oracle. The sample text must be italic. Per `.claude/skills/render-verify`, a status is never changed without viewing a crop.

- [ ] **Step 3: Re-pin the baselines with the status preserved**

Only if Step 2 confirms the render is correct. The panel keeps `status=PASS`; only its hash changes.

```bash
PELLUCID_GWG_HASH_REGEN=1 dotnet test Pellucid.Rendering.Avalonia.Tests --framework net10.0 --filter GwgRenderHashGate
PELLUCID_GHENT_SCOREBOARD_REGEN=1 dotnet test Pellucid.Rendering.Avalonia.Tests --framework net10.0 --filter GhentScoreboard
```

Both fail on purpose. Read the generated lines out of the assertion message, hand-restore the statuses, and paste over the baseline file.

- [ ] **Step 4: Run the gates on Linux and macOS**

```bash
ssh lxman@192.168.0.136 'cd ~/RiderProjects/Pellucid && ~/.dotnet/dotnet test Pellucid.Rendering.Avalonia.Tests --framework net10.0 --filter "GwgRenderHashGate|GhentScoreboard"'
```

For macmini use the `ssh-mcp` `macmini` profile (password auth) and `/usr/local/share/dotnet/dotnet`.

**Pull both repos on each box first and verify the SHAs match Windows.** A silent `git pull` failure behind macOS keychain errors produced a wrong measurement twice during this investigation.

Expected: GWG090 and p3/s0 still diverge from the Windows baseline on both boxes — they substitute different faces — but nothing else moves. Windows and Linux are predicted to converge on `C059-Italic`.

- [ ] **Step 5: Commit**

```bash
git add Pellucid.Rendering.Avalonia.Tests/Cmyk/gwg-render-hash-baseline.txt Pellucid.Rendering.Avalonia.Tests/Cmyk/ghent-scoreboard-baseline.txt
git commit -m "test: re-pin GWG090 and panel 9.0 after metadata font resolution"
```

---

## Self-Review

**Spec coverage**

| Spec requirement | Task |
|---|---|
| `FontMetadataIndex`, per-face, `.ttc` enumerated | 1, 2 |
| PostScript name (ID 6) primary key | 1, 2, 4 |
| All localized ID 1/16 as aliases; English only to canonicalise | 1 (two tests) |
| Read only header + `name` + `head`; never `OS/2`, never whole files | 1 |
| Built once per process in the existing singleton | 2 (`SystemFontLocator.Default` is unchanged and still `Lazy`) |
| Base-35 alias table from `Fontmap.GS` | 3 |
| Ladder steps 1–3 | 4 |
| No Latin floor; null on miss | 4 (explicit boundary test), 5 |
| Style scoring, ties on lowest face index | 2 |
| `ISystemFontProvider` additive via default method | 4 (legacy-provider test) |
| Legacy members keep their file-base-name contract | 2 (`FindPath`, `FileBaseNames`), 4 |
| Gates on three platforms, one fixture and one panel expected | 6 |

Slice 2 items (bundled Liberation, symbolic guard) are correctly absent.

**Placeholder scan:** no TBD/TODO, no "similar to Task N", no "add error handling" — every code step carries the actual code, and every test step the actual assertions.

**Type consistency:** `FontFaceRecord` fields are used with identical names in Tasks 2 and 4. `FontMetadataIndex.PickBest(IEnumerable<FontFaceRecord>, bool, bool)` is defined in Task 2 and called in Task 4 with that signature. `FontRequest(string, bool, bool)` and `FontMatch(byte[], int)` are defined in Task 4 and used in Task 5 with those shapes. `Base35Aliases.Split` returns a named tuple `(Family, Bold, Italic)` in Task 3 and is destructured positionally in Task 4 — consistent. `SubstituteFontResolver.Classify` and `SyntheticStd14Name` are existing public statics, called in Task 4 with their current signatures.

**One gap found and closed during review:** Task 5 deletes `SelectFace`/`Score`, added in `6afbe7a`. Without that instruction an implementer would leave dead code behind, and worse, a reviewer might assume face selection still happens there. The face-selection *tests* from that commit stay and must keep passing — they are the regression proof that moving the logic did not lose it.
