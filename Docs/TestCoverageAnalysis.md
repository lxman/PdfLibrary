# Test Coverage Analysis

This document provides a comprehensive analysis of current test coverage and recommendations for improvement.

## Current Test Coverage

### ✅ Well-Covered Areas

| Component | Test File | Coverage Level |
|-----------|-----------|----------------|
| **Core Primitives** | `CorePrimitivesTests.cs` | High - Tests all primitive types |
| **Parsing** | `PdfLexerTests.cs`, `PdfParserTests.cs` | High - Token and object parsing |
| **Content Parsing** | `PdfContentParserTests.cs` | Medium - Basic operator parsing |
| **Fonts** | `PdfFontTests.cs`, `FontEncodingTests.cs`, `GlyphListTests.cs`, `ToUnicodeCMapTests.cs` | High - Font handling |
| **Embedded Fonts** | `EmbeddedFontIntegrationTests.cs`, `GlyphExtractionIntegrationTests.cs`, `GlyphToSKPathConverterTests.cs` | High - Glyph extraction |
| **Images** | `PdfImageTests.cs` | Medium - Basic image handling |
| **Streams** | `PdfStreamDecodeTests.cs`, `StreamFilterTests.cs` | High - Filter chains |
| **Security** | `RC4Tests.cs`, `AesCipherTests.cs`, `PdfDecryptorTests.cs`, `PdfEncryptorTests.cs`, `PdfPermissionsTests.cs` | High - Encryption/decryption |
| **Annotations** | `PdfAnnotationTests.cs` | High - Links, notes, highlights |
| **Text Extraction** | `PdfTextExtractorTests.cs` | Medium - Text extraction |
| **Basic Rendering** | `PdfRendererTests.cs` | Medium - Path/color operators |

---

## 🔴 Critical Coverage Gaps

### 1. **Rendering Architecture (expand_api branch)**

**Priority: CRITICAL** - These are core components added in the current branch and have **NO TESTS**.

| Component | Location | Recommended Tests |
|-----------|----------|-------------------|
| **PathRenderer** | `Rendering/PathRenderer.cs` | Blend mode tests, isolated transparency group tests |
| **TextRenderer** | `Rendering/TextRenderer.cs` | Text rendering, font fallback, effective font size calculation |
| **ImageRenderer** | `Rendering/ImageRenderer.cs` | Image rendering, transformations, soft masks |
| **ColorConverter** | `Conversion/ColorConverter.cs` | PDF to SKColor conversion for all color spaces |
| **PathConverter** | `Conversion/PathConverter.cs` | PDF path to SKPath conversion |
| **BlendModeConverter** | `Conversion/BlendModeConverter.cs` | PDF blend mode to SKBlendMode mapping |
| **CanvasStateManager** | `State/CanvasStateManager.cs` | Save/restore stack management |
| **SoftMaskManager** | `State/SoftMaskManager.cs` | Soft mask application |

### 2. **Blend Modes (NEW FEATURE)**

**Priority: CRITICAL** - Core feature just fixed, needs comprehensive tests.

**Recommended Test File:** `PdfLibrary.Tests/Rendering/BlendModeTests.cs`

Test scenarios:
- All 16 blend modes render correctly
- Isolated transparency groups use transparent backdrop
- Blend mode compositing workflow (create group → draw backdrop → draw with blend mode → composite)
- Nested blend modes
- Blend modes with soft masks
- Blend modes with different color spaces

**Reference Rendering:**
- Use mutool renders as golden masters
- Pixel-by-pixel comparison with reference outputs

### 3. **Separation Color Space (NEW FEATURE)**

**Priority: HIGH** - New fluent API feature, needs validation.

**Recommended Test File:** `PdfLibrary.Tests/Builder/PdfColorTests.cs`

Test scenarios:
- `PdfColor.FromSeparation(colorantName, tint)` creates correct color
- Tint values clamp to 0-1 range
- ColorantName property set correctly
- Separation colors serialize to PDF correctly
- Builder integration tests

### 4. **Color Space Resolution**

**Priority: HIGH** - Critical for correct rendering.

**Recommended Test File:** `PdfLibrary.Tests/Rendering/ColorSpaceResolverTests.cs`

Test scenarios:
- DeviceGray → device colors
- DeviceRGB → device colors
- DeviceCMYK → device colors
- ICCBased → profile-based conversion
- Separation → tint transform function
- Indexed → lookup table
- CalGray, CalRGB, Lab → calibrated conversion
- Pattern color spaces

---

## 🟡 Moderate Coverage Gaps

### 5. **Builder Components**

Several builder components have minimal or no dedicated tests:

**Recommended Test Files:**

| Component | Test File | Priority | Test Scenarios |
|-----------|-----------|----------|----------------|
| **Bookmarks** | `PdfBookmarkTests.cs` | Medium | Nested bookmarks, destinations, styling |
| **Layers (OCG)** | `PdfLayerTests.cs` | Medium | Layer visibility, nesting, content assignment |
| **Page Labels** | `PdfPageLabelTests.cs` | Medium | All numbering styles, prefixes |
| **Metadata** | `PdfMetadataBuilderTests.cs` | Low | Title, author, subject, keywords, dates |
| **Encryption** | `PdfEncryptionSettingsTests.cs` | Medium | All encryption methods, permissions |
| **AcroForm** | `PdfAcroFormBuilderTests.cs` | High | Form settings, field organization |

### 6. **Form Fields**

**Priority: HIGH** - Interactive features need thorough testing.

**Recommended Test File:** `PdfLibrary.Tests/Builder/PdfFormFieldTests.cs`

Test scenarios:
- TextField: default value, required, multiline, password, max length
- Checkbox: checked/unchecked states, appearance
- RadioGroup: multiple options, selection, mutual exclusivity
- Dropdown: options, default selection, editable
- SignatureField: creation, placement
- Field validation and error handling
- Tab order and field flags

### 7. **PDF Functions**

**Priority: MEDIUM** - Used for shading and color transforms.

**Recommended Test File:** `PdfLibrary.Tests/Functions/PdfFunctionTests.cs`

Test scenarios:
- Type 0 (Sampled): interpolation
- Type 2 (Exponential): exponent calculation
- Type 3 (Stitching): subdomain mapping
- Type 4 (PostScript): calculator operations
- Function composition and nesting

---

## 🟢 Enhancement Opportunities

### 8. **Operator Coverage**

**Priority: LOW-MEDIUM** - Individual operator tests for completeness.

**Recommended Test File:** `PdfLibrary.Tests/Content/OperatorTests.cs`

Organize by operator category:
- Graphics state operators (q, Q, cm, w, J, j, M, d, ri, i, gs)
- Path construction (m, l, c, v, y, h, re)
- Path painting (S, s, f, F, f*, B, b, B*, b*, n)
- Clipping (W, W*)
- Text operators (BT, ET, Tc, Tw, Tz, TL, Tf, Tr, Ts, Td, TD, Tm, T*, Tj, TJ, ', ")
- Color operators (CS, cs, SC, SCN, sc, scn, G, g, RG, rg, K, k)
- XObject (Do)
- Inline images (BI, ID, EI)
- Marked content (MP, DP, BMC, BDC, EMC)

### 9. **Graphics State Management**

**Priority: MEDIUM** - State stack edge cases.

**Recommended Test File:** `PdfLibrary.Tests/Content/GraphicsStateTests.cs`

Test scenarios:
- State stack push/pop
- CTM transformations and composition
- Text matrix transformations
- Color state resolution
- Line style properties
- Clipping path accumulation
- Extended graphics state (gs operator)

### 10. **Integration Tests**

**Priority: MEDIUM** - End-to-end scenarios.

**Recommended Test File:** `PdfLibrary.Tests/Integration/PdfIntegrationTests.cs`

Test scenarios:
- Load real-world PDFs → render → compare with reference
- Create complex PDFs → save → load → verify structure
- Encrypted PDFs → decrypt → render
- Forms → fill fields → save → verify
- Bookmarks → navigate → render
- Layers → toggle visibility → render

---

## Recommended Testing Strategy

### Phase 1: Critical Coverage (expand_api branch)

**Goal:** Test new features before merge to master.

1. **Blend Mode Tests** (`BlendModeTests.cs`)
   - 16 blend mode rendering tests
   - Isolated transparency group tests
   - Reference comparison tests

2. **Separation Color Tests** (`PdfColorTests.cs`)
   - API validation
   - Serialization tests

3. **Rendering Component Tests**
   - `PathRendererTests.cs` - Blend modes, isolated groups
   - `TextRendererTests.cs` - Font rendering, fallback
   - `ColorConverterTests.cs` - Color space conversions

### Phase 2: High-Priority Gaps

**Goal:** Cover interactive features and color handling.

4. **Form Field Tests** (`PdfFormFieldTests.cs`)
   - All field types
   - Validation and interaction

5. **Color Space Tests** (`ColorSpaceResolverTests.cs`)
   - All color space types
   - Resolution and conversion

### Phase 3: Moderate Coverage

**Goal:** Fill builder and function gaps.

6. **Builder Component Tests**
   - Bookmarks, layers, page labels
   - Metadata, encryption settings
   - AcroForm builder

7. **Function Tests** (`PdfFunctionTests.cs`)
   - All function types
   - Evaluation and composition

### Phase 4: Enhancement

**Goal:** Comprehensive operator and integration coverage.

8. **Operator Tests** (`OperatorTests.cs`)
9. **Graphics State Tests** (`GraphicsStateTests.cs`)
10. **Integration Tests** (`PdfIntegrationTests.cs`)

---

## Testing Infrastructure Recommendations

### Reference Rendering System

For rendering tests, establish a reference comparison framework:

```csharp
public class RenderingTestBase
{
    protected void AssertRendersLike(string pdfPath, string referencePath, double tolerance = 0.01)
    {
        // Load PDF and render
        var doc = PdfDocument.Load(pdfPath);
        var page = doc.GetPage(0);
        var rendered = page.Render(doc).WithScale(1.0).ToBitmap();

        // Load reference (mutool render)
        var reference = SKBitmap.Decode(referencePath);

        // Compare pixels
        double psnr = CalculatePSNR(rendered, reference);
        Assert.True(psnr > 30, $"PSNR {psnr:F2} below threshold");
    }
}
```

### Test Data Organization

```
PdfLibrary.Tests/
├── TestData/
│   ├── BlendModes/
│   │   ├── multiply.pdf
│   │   ├── multiply_reference.png
│   │   ├── screen.pdf
│   │   └── ...
│   ├── ColorSpaces/
│   │   ├── separation.pdf
│   │   ├── iccbased.pdf
│   │   └── ...
│   ├── Forms/
│   │   ├── textfield.pdf
│   │   ├── checkbox.pdf
│   │   └── ...
│   └── RealWorld/
│       ├── invoice.pdf
│       ├── scanned.pdf
│       └── ...
```

### Code Coverage Targets

- **Critical paths:** 90%+ coverage
- **Rendering pipeline:** 85%+ coverage
- **Builder APIs:** 80%+ coverage
- **Overall project:** 75%+ coverage

Use `dotnet test --collect:"XPlat Code Coverage"` and Coverlet for metrics.

---

## Summary

**Current Coverage:** ~50-60% (estimated)

**Critical Gaps:**
1. Rendering architecture components (0% coverage) - **8 components**
2. Blend modes (0% coverage) - **NEW FEATURE**
3. Separation color space (0% coverage) - **NEW FEATURE**
4. Color space resolution (0% coverage)
5. Form fields (minimal coverage)

**Recommendation:** Implement Phase 1 tests **before merging expand_api to master** to ensure critical blend mode fix and new features are validated.
