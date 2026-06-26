# Renderer SPI — Plan B2: Glyph→Path Converter + .ttc Face Selection

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the SkiaSharp-free core component that converts a glyph outline to an `IPathBuilder` path, and let `SfntFont` open a specific face of a `.ttc` collection — the two pieces the live text-pipeline rewire (Plan B3) will consume.

**Architecture:** Add an internal core `GlyphOutlineToPath` that ports the existing SkiaSharp `GlyphToSKPathConverter` algorithm to produce `IPathBuilder`/`PathSegment` geometry instead of `SKPath`. The geometry vocabulary is cubic-only (`CurveTo`), so TrueType quadratic curves are **degree-elevated to cubic** (exact). Separately, extend `SfntFont` with a `faceIndex` so a multi-face macOS `.ttc` (e.g. `Helvetica.ttc` = Regular/Bold/Oblique/…) can be opened at the right face. Neither change touches the live render path; Plan B3 wires them in.

**Tech Stack:** C# 12, .NET 8/9/10, xUnit. FontParser (internal project). No new package dependencies.

## Global Constraints

- Core `PdfLibrary` must remain SkiaSharp-free (no `using SkiaSharp`, no package ref).
- Multi-target `net8.0;net9.0;net10.0`.
- `GlyphOutlineToPath` is **internal** (the render target never sees glyphs — it receives paths; the core pipeline uses the converter). Tests reach it via `InternalsVisibleTo("PdfLibrary.Tests")`.
- New code in namespace `PdfLibrary.Rendering`; tests in `PdfLibrary.Tests/Rendering/` and `PdfLibrary.Tests/Fonts/`.
- xUnit via global usings (don't add `using Xunit;` unless the build complains).
- The fill rule is NOT carried on the path: the existing converter used `SKPathFillType.EvenOdd`; in this design the even-odd convention travels per-call via `FillPath(..., evenOdd: true)` (Plan B3). So `GlyphOutlineToPath` produces geometry only and sets no fill rule.
- The full suite (currently 1248 on the CI filter) must stay green after every task.

---

## File Structure

- `PdfLibrary/Rendering/GlyphOutlineToPath.cs` (create) — internal converter; `FromTrueType` (Task 1) + `FromCff` (Task 2).
- `FontParser/SfntFont.cs` (modify) — add a `faceIndex` constructor + `FaceCount` (Task 3).
- `PdfLibrary.Tests/Rendering/GlyphOutlineToPathTests.cs` (create) — Tasks 1 & 2.
- `PdfLibrary.Tests/Fonts/SfntFontFaceSelectionTests.cs` (create) — Task 3.

---

## Task 1: `GlyphOutlineToPath.FromTrueType` (quadratic → cubic elevation)

**Files:**
- Create: `PdfLibrary/Rendering/GlyphOutlineToPath.cs`
- Test: `PdfLibrary.Tests/Rendering/GlyphOutlineToPathTests.cs`

**Background:** Ports `PdfLibrary.Rendering.SkiaSharp.GlyphToSKPathConverter.ConvertToPath`/`ProcessContour` to emit `IPathBuilder`. TrueType `glyf` outlines are quadratic; `PathSegment` has only cubic `CurveToSegment`, so each quadratic `P0 →(control Q)→ P1` is elevated to the exact equivalent cubic with control points `C1 = P0 + ⅔(Q − P0)`, `C2 = P1 + ⅔(Q − P1)`. Points are scaled by `fontSize/unitsPerEm` and Y-flipped (font Y-up → render Y-down) exactly as the SkiaSharp converter did.

**Interfaces:**
- Produces: `internal static IPathBuilder GlyphOutlineToPath.FromTrueType(PdfLibrary.Fonts.Embedded.GlyphOutline outline, float fontSize, ushort unitsPerEm)` — returns a `PathBuilder` (as `IPathBuilder`) of the glyph in glyph space (scaled, Y-flipped), no fill rule set.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Rendering/GlyphOutlineToPathTests.cs`:

```csharp
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class GlyphOutlineToPathTests
{
    private static GlyphMetrics Metrics() => new(0, 0, 0, 0, 0, 0);

    // A triangle of three on-curve points; unitsPerEm == fontSize so scale == 1.
    [Fact]
    public void TrueType_OnCurveTriangle_ScalesAndYFlips()
    {
        var contour = new GlyphContour(
        [
            new ContourPoint(0, 0, onCurve: true),
            new ContourPoint(100, 0, onCurve: true),
            new ContourPoint(50, 100, onCurve: true),
        ]);
        var outline = new GlyphOutline(0, [contour], Metrics());

        IPathBuilder path = GlyphOutlineToPath.FromTrueType(outline, fontSize: 100, unitsPerEm: 100);
        IReadOnlyList<PathSegment> s = path.Segments;

        Assert.IsType<MoveToSegment>(s[0]);
        var m = (MoveToSegment)s[0];
        Assert.Equal(0, m.X, 3);
        Assert.Equal(0, m.Y, 3);
        // Y is flipped: (50,100) -> (50,-100)
        Assert.Contains(s, seg => seg is LineToSegment { X: 50, Y: -100 });
        Assert.IsType<ClosePathSegment>(s[^1]);
    }

    // One off-curve control point between two on-curve points -> one elevated cubic.
    [Fact]
    public void TrueType_QuadraticControl_ElevatesToCubic()
    {
        var contour = new GlyphContour(
        [
            new ContourPoint(0, 0, onCurve: true),     // P0
            new ContourPoint(50, 100, onCurve: false),  // Q (control)
            new ContourPoint(100, 0, onCurve: true),    // P1
        ]);
        var outline = new GlyphOutline(0, [contour], Metrics());

        IPathBuilder path = GlyphOutlineToPath.FromTrueType(outline, fontSize: 100, unitsPerEm: 100);
        IReadOnlyList<PathSegment> s = path.Segments;

        CurveToSegment c = Assert.IsType<CurveToSegment>(s[1]);
        // scaled+flipped: P0=(0,0) Q=(50,-100) P1=(100,0); cubic C1=P0+2/3(Q-P0), C2=P1+2/3(Q-P1)
        Assert.Equal(33.333, c.X1, 2);
        Assert.Equal(-66.667, c.Y1, 2);
        Assert.Equal(66.667, c.X2, 2);
        Assert.Equal(-66.667, c.Y2, 2);
        Assert.Equal(100, c.X3, 3);
        Assert.Equal(0, c.Y3, 3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphOutlineToPathTests"`
Expected: FAIL — `GlyphOutlineToPath` does not exist.

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Rendering/GlyphOutlineToPath.cs`:

```csharp
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Rendering;

/// <summary>
/// Converts a glyph outline to an <see cref="IPathBuilder"/> in glyph space (scaled by
/// fontSize/unitsPerEm and Y-flipped from font Y-up to render Y-down). Cubic-only: TrueType
/// quadratics are degree-elevated to exact cubics. No fill rule is set on the path — glyph
/// fills use even-odd, supplied by the caller at FillPath time.
/// </summary>
internal static class GlyphOutlineToPath
{
    public static IPathBuilder FromTrueType(GlyphOutline outline, float fontSize, ushort unitsPerEm)
    {
        if (outline is null) throw new ArgumentNullException(nameof(outline));
        if (unitsPerEm == 0) throw new ArgumentException("Units per em cannot be zero", nameof(unitsPerEm));

        var pb = new PathBuilder();
        float scale = fontSize / unitsPerEm;

        foreach (GlyphContour contour in outline.Contours)
        {
            if (contour.Points.Count == 0) continue;
            ProcessContour(pb, contour, scale);
        }

        return pb;
    }

    private static void ProcessContour(PathBuilder pb, GlyphContour contour, float scale)
    {
        List<ContourPoint> points = contour.Points;
        if (points.Count == 0) return;

        int startIndex = FindFirstOnCurvePoint(points);
        double curX, curY;

        if (startIndex == -1)
        {
            // All off-curve: start at the midpoint of the first two points.
            if (points.Count < 2) return;
            ContourPoint p0 = points[0], p1 = points[1];
            var midX = (short)((p0.X + p1.X) / 2);
            var midY = (short)((p0.Y + p1.Y) / 2);
            (curX, curY) = ScalePoint(midX, midY, scale);
            pb.MoveTo(curX, curY);
            startIndex = 0;
        }
        else
        {
            (curX, curY) = ScalePoint(points[startIndex].X, points[startIndex].Y, scale);
            pb.MoveTo(curX, curY);
        }

        int count = points.Count;
        for (var i = 1; i <= count; i++)
        {
            int currentIndex = (startIndex + i) % count;
            ContourPoint currentPoint = points[currentIndex];

            if (currentPoint.OnCurve)
            {
                (double px, double py) = ScalePoint(currentPoint.X, currentPoint.Y, scale);
                pb.LineTo(px, py);
                curX = px; curY = py;
            }
            else
            {
                int nextIndex = (currentIndex + 1) % count;
                ContourPoint nextPoint = points[nextIndex];
                (double ctrlX, double ctrlY) = ScalePoint(currentPoint.X, currentPoint.Y, scale);

                double endX, endY;
                if (nextPoint.OnCurve)
                {
                    (endX, endY) = ScalePoint(nextPoint.X, nextPoint.Y, scale);
                    i++; // consumed the next on-curve point
                }
                else
                {
                    // Implied on-curve point midway between two consecutive off-curve points.
                    var impliedX = (short)((currentPoint.X + nextPoint.X) / 2);
                    var impliedY = (short)((currentPoint.Y + nextPoint.Y) / 2);
                    (endX, endY) = ScalePoint(impliedX, impliedY, scale);
                }

                // Elevate the quadratic (curX,curY)->(ctrl)->(end) to an exact cubic.
                double c1x = curX + 2.0 / 3.0 * (ctrlX - curX);
                double c1y = curY + 2.0 / 3.0 * (ctrlY - curY);
                double c2x = endX + 2.0 / 3.0 * (ctrlX - endX);
                double c2y = endY + 2.0 / 3.0 * (ctrlY - endY);
                pb.CurveTo(c1x, c1y, c2x, c2y, endX, endY);
                curX = endX; curY = endY;
            }
        }

        pb.ClosePath();
    }

    private static int FindFirstOnCurvePoint(List<ContourPoint> points)
    {
        for (var i = 0; i < points.Count; i++)
            if (points[i].OnCurve) return i;
        return -1;
    }

    // Scale font units to render units and flip Y (font Y-up -> render Y-down).
    private static (double x, double y) ScalePoint(double x, double y, float scale)
        => (x * scale, -y * scale);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphOutlineToPathTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/GlyphOutlineToPath.cs PdfLibrary.Tests/Rendering/GlyphOutlineToPathTests.cs
git commit -m "feat(render): GlyphOutlineToPath.FromTrueType (quadratic->cubic elevation)"
```

---

## Task 2: `GlyphOutlineToPath.FromCff` (cubic)

**Files:**
- Modify: `PdfLibrary/Rendering/GlyphOutlineToPath.cs`
- Test: `PdfLibrary.Tests/Rendering/GlyphOutlineToPathTests.cs` (add a test)

**Background:** Ports `GlyphToSKPathConverter.ConvertCffToPath`. CFF/Type2 charstrings produce cubic Béziers already, so no elevation — each command maps directly. Same scale + Y-flip. The CFF outline type is `FontParser.Tables.Cff.GlyphOutline` with `Commands` of `MoveToCommand`/`LineToCommand`/`CubicBezierCommand`/`ClosePathCommand` (points are `System.Drawing.PointF`, float).

**Interfaces:**
- Produces: `internal static IPathBuilder GlyphOutlineToPath.FromCff(FontParser.Tables.Cff.GlyphOutline outline, float fontSize, ushort unitsPerEm)`.

- [ ] **Step 1: Write the failing test**

Add to `PdfLibrary.Tests/Rendering/GlyphOutlineToPathTests.cs` (and add `using FontParser.Tables.Cff;` at the top of the file):

```csharp
    [Fact]
    public void Cff_CubicCommands_ScaleAndYFlip()
    {
        var outline = new FontParser.Tables.Cff.GlyphOutline();
        outline.Commands.Add(new MoveToCommand(0f, 0f));
        outline.Commands.Add(new CubicBezierCommand(
            new System.Drawing.PointF(10f, 100f),
            new System.Drawing.PointF(90f, 100f),
            new System.Drawing.PointF(100f, 0f)));
        outline.Commands.Add(new ClosePathCommand());

        IPathBuilder path = GlyphOutlineToPath.FromCff(outline, fontSize: 100, unitsPerEm: 100);
        IReadOnlyList<PathSegment> s = path.Segments;

        Assert.IsType<MoveToSegment>(s[0]);
        CurveToSegment c = Assert.IsType<CurveToSegment>(s[1]);
        // scale 1, Y flipped
        Assert.Equal(10, c.X1, 3);  Assert.Equal(-100, c.Y1, 3);
        Assert.Equal(90, c.X2, 3);  Assert.Equal(-100, c.Y2, 3);
        Assert.Equal(100, c.X3, 3); Assert.Equal(0, c.Y3, 3);
        Assert.IsType<ClosePathSegment>(s[^1]);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphOutlineToPathTests.Cff_CubicCommands_ScaleAndYFlip"`
Expected: FAIL — `FromCff` does not exist.

- [ ] **Step 3: Write the implementation**

Add to `PdfLibrary/Rendering/GlyphOutlineToPath.cs`: add `using FontParser.Tables.Cff;` and an alias `using CffGlyphOutline = FontParser.Tables.Cff.GlyphOutline;` at the top (keep the existing `using PdfLibrary.Fonts.Embedded;`), then add this method to the class:

```csharp
    internal static IPathBuilder FromCff(CffGlyphOutline outline, float fontSize, ushort unitsPerEm)
    {
        if (outline is null) throw new ArgumentNullException(nameof(outline));
        if (unitsPerEm == 0) throw new ArgumentException("Units per em cannot be zero", nameof(unitsPerEm));

        var pb = new PathBuilder();
        float scale = fontSize / unitsPerEm;

        foreach (PathCommand command in outline.Commands)
        {
            switch (command)
            {
                case MoveToCommand m:
                {
                    (double x, double y) = ScalePoint(m.Point.X, m.Point.Y, scale);
                    pb.MoveTo(x, y);
                    break;
                }
                case LineToCommand l:
                {
                    (double x, double y) = ScalePoint(l.Point.X, l.Point.Y, scale);
                    pb.LineTo(x, y);
                    break;
                }
                case CubicBezierCommand c:
                {
                    (double c1x, double c1y) = ScalePoint(c.Control1.X, c.Control1.Y, scale);
                    (double c2x, double c2y) = ScalePoint(c.Control2.X, c.Control2.Y, scale);
                    (double ex, double ey) = ScalePoint(c.EndPoint.X, c.EndPoint.Y, scale);
                    pb.CurveTo(c1x, c1y, c2x, c2y, ex, ey);
                    break;
                }
                case ClosePathCommand:
                    pb.ClosePath();
                    break;
            }
        }

        return pb;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphOutlineToPathTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/GlyphOutlineToPath.cs PdfLibrary.Tests/Rendering/GlyphOutlineToPathTests.cs
git commit -m "feat(render): GlyphOutlineToPath.FromCff (cubic CFF commands)"
```

---

## Task 3: `SfntFont` face selection (`.ttc` face index)

**Files:**
- Modify: `FontParser/SfntFont.cs`
- Test: `PdfLibrary.Tests/Fonts/SfntFontFaceSelectionTests.cs` (create)

**Background:** Plan B's `.ttc` support always uses font 0 (= Regular). macOS ships base-14 faces as multi-face collections, so a Bold/Italic request must open the right face. This adds a `faceIndex` constructor that selects `offsetTable[faceIndex]`, plus `FaceCount`. (The logic to PICK the index by bold/italic style is Plan B3; this is the open-by-index mechanism.)

**Interfaces:**
- Produces:
  - `public SfntFont(byte[] data, int faceIndex)` — opens the given face of a collection; for a non-collection font only `faceIndex == 0` is valid.
  - `public int FaceCount { get; }` — number of faces (1 for a single font, `numFonts` for a `.ttc`).
  - The existing `SfntFont(byte[] data)` delegates to `faceIndex: 0`.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Fonts/SfntFontFaceSelectionTests.cs`:

```csharp
using FontParser;

namespace PdfLibrary.Tests.Fonts;

public class SfntFontFaceSelectionTests
{
    private static byte[] BareFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    // Build a 2-font TTC where BOTH faces point at the same shared font directory (legal in TTC).
    // Header = ttcf(4) major(2) minor(2) numFonts(4)=2 offset[0](4) offset[1](4) = 20 bytes; both offsets = 20.
    private static byte[] WrapAsTwoFaceTtc(byte[] font)
    {
        const int headerSize = 20;
        var ttc = new byte[headerSize + font.Length];
        // 'ttcf'
        ttc[0] = 0x74; ttc[1] = 0x74; ttc[2] = 0x63; ttc[3] = 0x66;
        ttc[4] = 0x00; ttc[5] = 0x01;            // major
        ttc[6] = 0x00; ttc[7] = 0x00;            // minor
        ttc[8] = 0x00; ttc[9] = 0x00; ttc[10] = 0x00; ttc[11] = 0x02; // numFonts = 2
        ttc[12] = 0x00; ttc[13] = 0x00; ttc[14] = 0x00; ttc[15] = 0x14; // offset[0] = 20
        ttc[16] = 0x00; ttc[17] = 0x00; ttc[18] = 0x00; ttc[19] = 0x14; // offset[1] = 20
        Array.Copy(font, 0, ttc, headerSize, font.Length);

        // Shift each table-directory offset by headerSize (file-absolute, as a real .ttc requires).
        int numTables = (ttc[headerSize + 4] << 8) | ttc[headerSize + 5];
        int firstRecord = headerSize + 12;
        for (int i = 0; i < numTables; i++)
        {
            int offPos = firstRecord + i * 16 + 8;
            uint off = ((uint)ttc[offPos] << 24) | ((uint)ttc[offPos + 1] << 16)
                     | ((uint)ttc[offPos + 2] << 8) | ttc[offPos + 3];
            off += headerSize;
            ttc[offPos] = (byte)(off >> 24); ttc[offPos + 1] = (byte)(off >> 16);
            ttc[offPos + 2] = (byte)(off >> 8); ttc[offPos + 3] = (byte)off;
        }
        return ttc;
    }

    [Fact]
    public void Ttc_FaceCount_AndBothFacesParse()
    {
        byte[] ttc = WrapAsTwoFaceTtc(BareFont());
        var bare = new SfntFont(BareFont());

        var face0 = new SfntFont(ttc, 0);
        var face1 = new SfntFont(ttc, 1);

        Assert.Equal(2, face0.FaceCount);
        Assert.Equal(bare.NumGlyphs, face0.NumGlyphs);
        Assert.Equal(bare.NumGlyphs, face1.NumGlyphs);
    }

    [Fact]
    public void Ttc_FaceIndexOutOfRange_Throws()
    {
        byte[] ttc = WrapAsTwoFaceTtc(BareFont());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SfntFont(ttc, 2));
    }

    [Fact]
    public void SingleFont_FaceCountIsOne_AndOnlyFaceZeroValid()
    {
        var single = new SfntFont(BareFont());
        Assert.Equal(1, single.FaceCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SfntFont(BareFont(), 1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntFontFaceSelectionTests"`
Expected: FAIL — no `SfntFont(byte[], int)` ctor / no `FaceCount`.

- [ ] **Step 3: Write the implementation**

In `FontParser/SfntFont.cs`:

a) Add the property (near `OutlineKind`):

```csharp
        /// <summary>Number of faces: 1 for a single font, or the font count for a TTC collection.</summary>
        public int FaceCount { get; }
```

b) Replace the existing single constructor with a delegating pair. The existing `public SfntFont(byte[] data)` becomes:

```csharp
        public SfntFont(byte[] data) : this(data, 0) { }

        public SfntFont(byte[] data, int faceIndex)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (faceIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(faceIndex), "Face index must be non-negative.");

            using var reader = new BigEndianReader(data);
            uint sfntVersion = reader.ReadUInt32();

            if (sfntVersion == 0x74746366) // 'ttcf'
            {
                reader.ReadUShort();            // majorVersion
                reader.ReadUShort();            // minorVersion
                uint numFonts = reader.ReadUInt32();
                if (numFonts == 0)
                    throw new InvalidDataException("TrueType collection (ttcf) declares zero fonts.");
                if (faceIndex >= numFonts)
                    throw new ArgumentOutOfRangeException(nameof(faceIndex),
                        $"Face {faceIndex} requested but the collection has {numFonts} font(s).");
                FaceCount = (int)numFonts;

                long offsetTablePos = reader.Position;          // start of the uint32 offset array
                reader.Seek(offsetTablePos + faceIndex * 4);
                uint fontOffset = reader.ReadUInt32();           // offset to this face's table directory
                reader.Seek(fontOffset);
                sfntVersion = reader.ReadUInt32();               // the real sfnt version of this face
            }
            else
            {
                if (faceIndex != 0)
                    throw new ArgumentOutOfRangeException(nameof(faceIndex),
                        "This is a single font program; only face 0 exists.");
                FaceCount = 1;
            }

            // 0x00010000 = TrueType outlines, 'true' = legacy TrueType, 'OTTO' = CFF outlines.
            if (sfntVersion != 0x00010000 && sfntVersion != 0x74727565 && sfntVersion != 0x4F54544F)
            {
                throw new InvalidDataException(
                    $"Not a supported sfnt font program (sfnt version 0x{sfntVersion:X8}). " +
                    "WOFF/WOFF2 containers are not supported.");
            }

            ushort numTables = reader.ReadUShort();
            reader.ReadUShort(); // searchRange
            reader.ReadUShort(); // entrySelector
            reader.ReadUShort(); // rangeShift

            for (var i = 0; i < numTables; i++)
            {
                string tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                reader.ReadUInt32();           // checksum (not validated)
                uint offset = reader.ReadUInt32();
                uint length = reader.ReadUInt32();
                _directory[tag] = (offset, length);
            }

            OutlineKind = _directory.ContainsKey("CFF ") ? SfntOutlineKind.Cff
                : _directory.ContainsKey("glyf") ? SfntOutlineKind.TrueType
                : SfntOutlineKind.Unknown;
        }
```

(This replaces the entire existing constructor body — the `'ttcf'` branch is folded into the new two-arg constructor, and `GetTableBytes` is unchanged, still using the raw file-absolute `entry.Offset`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntFontFaceSelectionTests"`
Expected: PASS (3 tests). Also run the existing TTC test to confirm no regression:
Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntFontTtcTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add FontParser/SfntFont.cs PdfLibrary.Tests/Fonts/SfntFontFaceSelectionTests.cs
git commit -m "feat(fontparser): SfntFont face selection (open a specific .ttc face by index)"
```

---

## Task 4: Verification gate

**Files:** none (verification only)

- [ ] **Step 1: Full suite (CI filter)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo`
Expected: PASS, 0 failures (≥ 1248 + the new tests).

- [ ] **Step 2: Core still SkiaSharp-free**

Run: `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/`
Expected: no output.

- [ ] **Step 3: Release builds warning-free (core + FontParser)**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo` and `dotnet build FontParser/FontParser.csproj -c Release --nologo`
Expected: `0 Warning(s)`, `0 Error(s)` for each.

- [ ] **Step 4: No commit (verification only).**

---

## Self-Review Notes

- **Spec coverage:** delivers the design's `GlyphOutlineToPath` (TrueType quad→cubic elevation + CFF cubic) and the `.ttc` face-selection mechanism flagged by Plan B's final review. Both are SkiaSharp-free and consumed only by the (not-yet-built) core text pipeline.
- **Fidelity:** `FromTrueType` is a faithful port of `GlyphToSKPathConverter.ProcessContour` — same on/off-curve handling, implied-midpoint logic, scale, and Y-flip — with the only deviation being the mandated quadratic→cubic elevation (`SKPath.QuadTo` had no cubic-only constraint). The elevation is exact, so curves are geometrically identical.
- **No live-path change:** nothing wires the converter into rendering; existing behavior and the suite are unaffected until Plan B3.

## Out of scope → Plan B3 (the live-path rewire)

- Move glyph resolution (charCode→glyphId→outline, the ~700-line SkiaSharp `TextRenderer` orchestration) into the core, calling `GlyphOutlineToPath` + the per-glyph path cache core-side.
- The core text pipeline emits `FillPath`/`StrokePath`/`SetClippingPath` per the PDF text-rendering mode (Tr), threading `evenOdd: true` for glyph fills.
- Remove `DrawText`/`MeasureTextWidth` from `IRenderTarget`; delete the SkiaSharp text/glyph code; the Skia adapter becomes paths+pixels only.
- The **style-driven** `.ttc` face picker (choose `faceIndex` by bold/italic from the resolved BaseFont) on top of this task's open-by-index mechanism.
- DejaVu/non-metric width fixup; `PdfGraphicsState` public-view leak audit.
- Gate the whole rewire on the 90 render tests staying pixel-identical.

### B3 design notes (from B2's final review — these protect the rewire)

- **Even-odd is the #1 trap.** The original set `FillType = EvenOdd` on EVERY glyph path (it compensates for the Y-flip winding reversal). The converter carries NO fill rule. If B3 fills a glyph path with default (nonzero) winding, every counter ('o','e','A','B'…) renders solid. Make it impossible to fill a glyph path without `evenOdd: true`.
- **Cache key MUST include font size.** The converter BAKES `fontSize/unitsPerEm` into the path coordinates, so a cache "keyed by font + glyphId" (spec §2) collides across sizes and returns the first-seen size. Either key on `(font, glyphId, fontSize)`, or build at em-scale (scale=1) and apply font size in the per-glyph transform.
- **Cache an immutable snapshot, not the mutable `PathBuilder`.** `PathBuilder` is mutable; a shared cached builder handed to multiple draws invites corruption (and there's a live thread-safety concern). Cache the `Segments` list or an immutable wrapper.
- **Positioning is B3's job.** Converter output is GLYPH SPACE (size-scaled, Y-flipped, origin-centered) — not user space. B3 applies the text matrix (Tm), CTM, char/word spacing, horizontal scaling (Tz), text rise, and per-glyph advance ("CTM on canvas, glyph transform separate").
- **Composite/compound TrueType glyphs** must be flattened into `GlyphOutline.Contours` with component transforms/offsets applied UPSTREAM of the converter (it only iterates `Contours`). Verify the moved resolution pipeline reproduces the Skia `TextRenderer`.
- **Type1 outlines:** confirm they reduce to `FontParser.Tables.Cff.GlyphOutline` cubic commands and can reuse `FromCff` (likely — Type1 charstrings are cubic), or add a dedicated path. The converter has no Type1 entry today.
- **QuadTo→CubicTo rasterization parity:** the elevation is geometrically exact, but Skia flattens a native `QuadTo` vs an elevated `CubicTo` with different tolerances, so sub-pixel AA-edge diffs are possible — this is the design's accepted, 90-render-test-gated risk, surfacing in the adapter.
