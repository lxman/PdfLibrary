# Embedded Font Coverage Audit

**Date**: 2025-11-17
**Purpose**: Assess current TrueType/OpenType table support in PdfLibrary vs FontManager.NET

---

## Executive Summary

**Current Status**: PdfLibrary has basic table support for font metrics and character mapping, but is missing critical components for glyph outline rendering and advanced Unicode support.

**Source Repository**: `C:\Users\jorda\source\repos\FontManager.NET\FontParser` contains complete implementations of all TrueType/OpenType tables.

**Action Required**: Copy missing table parsers from FontManager.NET to achieve comprehensive embedded font support.

---

## Table Coverage Comparison

### Currently Implemented in PdfLibrary ✅

| Table | Purpose | File Location | Status |
|-------|---------|---------------|--------|
| **cmap** | Character to glyph mapping | `Tables\CmapTable.cs` | Partial (Format 0, 4 only) |
| **head** | Font header | `Tables\HeadTable.cs` | Complete |
| **hhea** | Horizontal header | `Tables\HheaTable.cs` | Complete |
| **hmtx** | Horizontal metrics | `Tables\HmtxTable.cs` | Complete |
| **maxp** | Maximum profile | `Tables\MaxpTable.cs` | Complete |
| **name** | Naming table | `Tables\NameTable.cs` | Complete |
| **post** | PostScript information | `PostTable.cs` | Complete |

**Infrastructure**:
- ✅ `TrueTypeParser.cs` - Table directory parser and extractor
- ✅ `BigEndianReader.cs` - Binary data reader
- ✅ `EmbeddedFontMetrics.cs` - Font metrics aggregator

### Missing from PdfLibrary ❌

#### Critical for Rendering

| Table | Purpose | Priority | FontManager.NET Location |
|-------|---------|----------|--------------------------|
| **glyf** | Glyph outlines (TrueType contours) | **CRITICAL** | `Tables\TtTables\Glyf\` |
| **loca** | Glyph location index | **CRITICAL** | `Tables\TtTables\LocaTable.cs` |
| **cmap formats 2,6,8,10,12,13,14** | Extended character mapping | **HIGH** | `Tables\Cmap\SubTables\` |

#### Important for Advanced Features

| Table | Purpose | Priority | FontManager.NET Location |
|-------|---------|----------|--------------------------|
| **CFF** | Compact Font Format (PostScript outlines) | HIGH | `Tables\Cff\` |
| **GPOS** | Glyph positioning | MEDIUM | `Tables\Gpos\` |
| **GSUB** | Glyph substitution | MEDIUM | `Tables\Gsub\` |
| **kern** | Kerning | MEDIUM | `Tables\Kern\` |
| **OS/2** | OS/2 and Windows metrics | MEDIUM | `Tables\Os2Table.cs` |

#### Nice-to-Have for Full Spec Compliance

| Table | Purpose | Priority | FontManager.NET Location |
|-------|---------|----------|--------------------------|
| **COLR/CPAL** | Color fonts | LOW | `Tables\Colr\`, `Tables\Cpal\` |
| **SVG** | SVG glyphs | LOW | `Tables\Svg\` |
| **MATH** | Math layout | LOW | `Tables\Math\` |
| **BASE** | Baseline data | LOW | `Tables\Base\` |
| **GDEF** | Glyph definition | LOW | `Tables\Gdef\` |

---

## Detailed Gap Analysis

### 1. cmap Table - Partial Implementation

**Current Coverage**:
- ✅ Format 0: Byte encoding (0-255 character codes)
- ✅ Format 4: Segment mapping (Unicode BMP, U+0000-U+FFFF)

**Missing Formats**:
- ❌ **Format 2**: High-byte mapping (legacy CJK fonts)
- ❌ **Format 6**: Trimmed table mapping (continuous ranges)
- ❌ **Format 8**: Mixed 16-bit and 32-bit coverage
- ❌ **Format 10**: Trimmed array (32-bit)
- ❌ **Format 12**: Segmented coverage (modern Unicode, emoji) - **CRITICAL**
- ❌ **Format 13**: Many-to-one mapping (fallback glyphs)
- ❌ **Format 14**: Unicode variation sequences (emoji variants)

**Impact of Missing Formats**:
- ❌ Cannot render modern emoji or variant characters (Format 14)
- ❌ Cannot support supplementary planes beyond U+FFFF (Format 12)
- ❌ Some legacy Asian fonts may fail (Format 2)
- ⚠️ Inefficient for simple continuous character ranges (Format 6)

**Priority**: Add Format 12 immediately for modern PDF support

**Available in FontManager.NET**:
```
C:\Users\jorda\source\repos\FontManager.NET\FontParser\Tables\Cmap\SubTables\
├── CmapSubtablesFormat2.cs
├── CmapSubtablesFormat6.cs
├── CmapSubtablesFormat8.cs
├── CmapSubtablesFormat10.cs
├── CmapSubtablesFormat12.cs
├── CmapSubtablesFormat13.cs
└── CmapSubtablesFormat14.cs
```

### 2. glyf Table - Not Implemented

**Purpose**: Contains TrueType glyph outline data (vector paths)

**Critical For**:
- Rendering glyphs at any size
- Converting glyphs to WPF PathGeometry
- Achieving 99%+ rendering accuracy vs PDFium
- WYSIWYG editing with precise glyph manipulation

**Current Situation**:
- ❌ No glyf table parser
- ❌ Cannot extract glyph outlines
- ⚠️ Currently relying on OS font rendering via TextBlock

**Available in FontManager.NET**:
```
C:\Users\jorda\source\repos\FontManager.NET\FontParser\Tables\TtTables\Glyf\
├── GlyphTable.cs - Main parser
├── GlyphData.cs - Glyph data container
├── GlyphHeader.cs - Glyph header parser
├── SimpleGlyph.cs - Simple glyph (single contour)
├── CompositeGlyph.cs - Composite glyph (multiple components)
├── SimpleGlyphCoordinate.cs - Coordinate data
└── SimpleGlyphFlagsExtensions.cs - Flag parsing helpers
```

**Implementation Components**:
1. **Simple Glyphs**: Single contour with on-curve and off-curve points
2. **Composite Glyphs**: References to other glyphs with transformations
3. **Quadratic Bézier Curves**: TrueType uses quadratic, not cubic
4. **Coordinate Compression**: Flags indicate delta vs absolute coordinates

### 3. loca Table - Not Implemented

**Purpose**: Index of glyph locations within the glyf table

**Critical For**:
- Finding where each glyph's outline data starts in glyf table
- Required to use glyf table at all

**Current Situation**:
- ❌ No loca table parser
- ❌ Cannot locate individual glyphs

**Available in FontManager.NET**:
```
C:\Users\jorda\source\repos\FontManager.NET\FontParser\Tables\TtTables\LocaTable.cs
```

**Format**: Two variants based on head.indexToLocFormat:
- Short format (16-bit offsets divided by 2)
- Long format (32-bit offsets)

### 4. CFF Table - Not Implemented

**Purpose**: Compact Font Format - PostScript Type 2 glyph outlines (alternative to glyf/loca)

**Used By**: OpenType fonts with PostScript outlines (CFF-flavored OpenType)

**Current Situation**:
- ❌ No CFF parser
- ❌ Cannot render OpenType/CFF fonts (have "OTTO" signature)

**Available in FontManager.NET**:
```
C:\Users\jorda\source\repos\FontManager.NET\FontParser\Tables\Cff\
├── CFF parser implementation
```

**Priority**: HIGH - Many modern fonts use CFF instead of TrueType outlines

### 5. GPOS Table - Not Implemented

**Purpose**: Glyph positioning - kerning, mark positioning, cursive attachment

**Impact**:
- ⚠️ Cannot apply proper kerning pairs beyond simple kern table
- ⚠️ Accent marks may be mispositioned
- ⚠️ Arabic/Indic scripts may render incorrectly

**Available in FontManager.NET**:
```
C:\Users\jorda\source\repos\FontManager.NET\FontParser\Tables\Gpos\
```

**Priority**: MEDIUM - Important for professional typography, not critical for basic rendering

### 6. GSUB Table - Not Implemented

**Purpose**: Glyph substitution - ligatures, contextual alternates, stylistic sets

**Impact**:
- ⚠️ "fi", "fl" ligatures won't substitute
- ⚠️ Arabic/Indic contextual forms won't work
- ⚠️ Missing OpenType features

**Available in FontManager.NET**:
```
C:\Users\jorda\source\repos\FontManager.NET\FontParser\Tables\Gsub\
```

**Priority**: MEDIUM - Important for advanced typography

---

## Implementation Roadmap

### Phase 1: Critical Tables (Weeks 1-2)

**Goal**: Enable glyph outline extraction and modern Unicode support

1. **Copy glyf/loca tables from FontManager.NET**
   - `TtTables\Glyf\*.cs` → `PdfLibrary\Fonts\Embedded\Tables\Glyf\`
   - `TtTables\LocaTable.cs` → `PdfLibrary\Fonts\Embedded\Tables\`
   - Update namespace references
   - Test with simple glyphs from test PDFs

2. **Copy missing cmap formats**
   - Priority: Format 12 (segmented coverage for extended Unicode)
   - Then: Formats 6, 10, 2, 13, 14
   - `Cmap\SubTables\*.cs` → `PdfLibrary\Fonts\Embedded\Tables\`

3. **Integration Testing**
   - Test glyph extraction from embedded fonts
   - Verify Format 12 emoji support
   - Validate composite glyph assembly

### Phase 2: PostScript Outlines (Weeks 3-4)

**Goal**: Support OpenType/CFF fonts

1. **Copy CFF table parser**
   - `Tables\Cff\*.cs` → `PdfLibrary\Fonts\Embedded\Tables\Cff\`
   - CFF Type 2 CharString decoder
   - CFF font dictionary parser

2. **Implement CFF → PathGeometry converter**
   - Convert PostScript path operators to WPF paths
   - Handle subroutines and hints

### Phase 3: Advanced Typography (Weeks 5-6)

**Goal**: Professional typography features

1. **Copy GPOS table**
   - Kerning pair positioning
   - Mark-to-base attachment
   - Cursive attachment

2. **Copy GSUB table**
   - Ligature substitution
   - Contextual alternates
   - OpenType features

3. **Copy OS/2 table**
   - Windows metrics
   - Unicode ranges
   - Font classification

### Phase 4: Modern Font Features (Weeks 7-8)

**Goal**: Color fonts and special features

1. **COLR/CPAL tables** - Color layer fonts
2. **SVG table** - SVG glyph support
3. **Variation fonts** (fvar, gvar, avar, cvar, hvar, mvar)

---

## File Copy Checklist

### Immediate Priority (Copy This Week)

**glyf/loca Tables**:
- [ ] `FontManager.NET\FontParser\Tables\TtTables\Glyf\GlyphTable.cs`
- [ ] `FontManager.NET\FontParser\Tables\TtTables\Glyf\GlyphData.cs`
- [ ] `FontManager.NET\FontParser\Tables\TtTables\Glyf\GlyphHeader.cs`
- [ ] `FontManager.NET\FontParser\Tables\TtTables\Glyf\SimpleGlyph.cs`
- [ ] `FontManager.NET\FontParser\Tables\TtTables\Glyf\CompositeGlyph.cs`
- [ ] `FontManager.NET\FontParser\Tables\TtTables\Glyf\SimpleGlyphCoordinate.cs`
- [ ] `FontManager.NET\FontParser\Tables\TtTables\Glyf\SimpleGlyphFlagsExtensions.cs`
- [ ] `FontManager.NET\FontParser\Tables\TtTables\LocaTable.cs`

**cmap Formats**:
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\CmapSubtablesFormat12.cs` **(PRIORITY)**
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\CmapSubtablesFormat6.cs`
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\CmapSubtablesFormat10.cs`
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\CmapSubtablesFormat2.cs`
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\CmapSubtablesFormat8.cs`
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\CmapSubtablesFormat13.cs`
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\CmapSubtablesFormat14.cs`
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\SequentialMapGroup.cs` (Format 12/13 support)
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\VariationSelectorRecord.cs` (Format 14 support)
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\DefaultUvsTableHeader.cs` (Format 14)
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\NonDefaultUvsTableHeader.cs` (Format 14)
- [ ] `FontManager.NET\FontParser\Tables\Cmap\SubTables\UvsMappingRecord.cs` (Format 14)

**Supporting Types**:
- [ ] Check if `IGlyphSpec.cs` interface is needed
- [ ] Check if additional helper classes are referenced

### Next Priority (Copy Next Week)

**CFF Tables**:
- [ ] `FontManager.NET\FontParser\Tables\Cff\*.cs` (entire directory)

**Advanced Typography**:
- [ ] `FontManager.NET\FontParser\Tables\Gpos\*.cs`
- [ ] `FontManager.NET\FontParser\Tables\Gsub\*.cs`
- [ ] `FontManager.NET\FontParser\Tables\Os2Table.cs`
- [ ] `FontManager.NET\FontParser\Tables\Kern\*.cs`

---

## Code Integration Notes

### Namespace Changes Required

All FontManager.NET files use namespace `FontParser.*`, which needs to be changed to `PdfLibrary.Fonts.Embedded.*`:

```csharp
// BEFORE (FontManager.NET):
namespace FontParser.Tables.Cmap.SubTables

// AFTER (PdfLibrary):
namespace PdfLibrary.Fonts.Embedded.Tables
```

### Dependency Verification

FontManager.NET tables may reference:
1. `FontParser.Reader.BigEndianReader` → Already have `PdfLibrary.Fonts.Embedded.BigEndianReader`
2. `FontParser.IFontTable` interface → Check if needed
3. Helper extension methods → Verify availability

### Testing Strategy

After copying each table parser:

1. **Unit Test**: Parse table from known test font
2. **Integration Test**: Extract from embedded PDF font
3. **Rendering Test**: Use extracted data in renderer
4. **Comparison Test**: Verify against PDFium reference

---

## Expected Outcomes

### After Phase 1 (glyf/loca + cmap formats)

**Capabilities Gained**:
- ✅ Extract TrueType glyph outlines from embedded fonts
- ✅ Convert glyphs to WPF PathGeometry
- ✅ Support modern Unicode (emoji, supplementary planes)
- ✅ Foundation for 99%+ rendering accuracy

**Rendering Quality**:
- Current: 96.54% (TextBlock approach)
- Target: 99%+ (PathGeometry with extracted glyphs)

### After Phase 2 (CFF support)

**Capabilities Gained**:
- ✅ Support OpenType/CFF fonts (PostScript outlines)
- ✅ Broader PDF font compatibility

### After Phase 3 (GPOS/GSUB)

**Capabilities Gained**:
- ✅ Professional kerning
- ✅ Ligature support
- ✅ Complex script shaping (Arabic, Indic)
- ✅ OpenType feature activation

### After Phase 4 (Color fonts)

**Capabilities Gained**:
- ✅ Color emoji rendering
- ✅ SVG glyph support
- ✅ Variable font support

---

## Risk Assessment

### Technical Risks

**Risk 1: Namespace/Dependency Conflicts**
- **Impact**: MEDIUM - Code may not compile without modifications
- **Mitigation**: Careful namespace updates, dependency mapping
- **Probability**: HIGH (expected)

**Risk 2: Complex Glyph Rendering**
- **Impact**: HIGH - Composite glyphs with transformations are complex
- **Mitigation**: Start with simple glyphs, test incrementally
- **Probability**: MEDIUM

**Risk 3: CFF vs TrueType Outline Differences**
- **Impact**: MEDIUM - Different curve types (cubic vs quadratic)
- **Mitigation**: WPF PathGeometry supports both
- **Probability**: LOW (WPF handles conversion)

### Integration Risks

**Risk 4: Performance Impact**
- **Impact**: MEDIUM - Glyph extraction may be slower than TextBlock
- **Mitigation**: Implement caching, lazy loading
- **Probability**: MEDIUM

**Risk 5: Memory Usage**
- **Impact**: MEDIUM - Full glyph outline storage uses more memory
- **Mitigation**: Cache management, on-demand extraction
- **Probability**: LOW (modern systems have sufficient memory)

---

## Success Criteria

### Phase 1 Success:
- [ ] Successfully extract simple glyphs from test PDFs
- [ ] Successfully extract composite glyphs
- [ ] Format 12 cmap working with emoji test
- [ ] Rendering quality improves to 98%+

### Phase 2 Success:
- [ ] Parse CFF OpenType fonts
- [ ] Render PostScript outlines correctly
- [ ] Support both TrueType and CFF in same codebase

### Phase 3 Success:
- [ ] Kerning applied correctly
- [ ] Ligatures substitute automatically
- [ ] Complex scripts (Arabic) render correctly

### Phase 4 Success:
- [ ] Color emoji render with proper colors
- [ ] SVG glyphs display correctly
- [ ] Variable fonts adjust with settings

---

## Next Actions

1. **Today**: Start copying glyf/loca table parsers
2. **This Week**: Complete Phase 1 (critical tables)
3. **Next Week**: Test glyph extraction, begin Phase 2
4. **Week 3-4**: Complete CFF support
5. **Week 5-6**: Advanced typography features

---

**Document Owner**: Jordan
**Last Updated**: 2025-11-17
**Next Review**: After Phase 1 completion
