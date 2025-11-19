# Appligent Labs PDF Technical Summary

Source: https://labs.appligent.com/appligent-labs/tag/learning-pdf

This document summarizes key technical insights from Appligent Labs blog posts on PDF internals.

---

## Transformation Matrices in PDFs

### Matrix Structure
PDFs use a 3x3 transformation matrix represented as six values `[a b c d e f]`:

```
[a  b  0]
[c  d  0]
[e  f  1]
```

**Parameter functions:**
- **a, d**: Scale factors for X and Y axes
- **b, c**: Skewing and rotation parameters
- **e, f**: Translation (X, Y displacement)
- Third column remains constant for homogeneous coordinates

### Common Transformation Matrices

**Identity (no transformation):**
```
[1 0 0 1 0 0]
```

**Scaling:**
```
[Sx  0  0]
[ 0 Sy  0]
[ 0  0  1]
```

**Rotation (angle theta in radians):**
```
[cos(theta)  -sin(theta)  0]
[sin(theta)   cos(theta)  0]
[    0            0       1]
```

**Translation:**
```
[1  0  0]
[0  1  0]
[e  f  1]
```

### Combining Matrices
Matrices multiply in application order: scaling -> rotation -> translation.

**Benefits of pre-combining:**
- Fewer operations during rendering
- Avoids compounding floating-point errors from multiple operations

---

## Position Calculation in PDFs

### Coordinate System
- Origin (0,0) is at **bottom-left corner** of the page
- X-axis extends rightward, Y-axis extends upward
- Units are in **points** (1/72 inch)

**Standard page dimensions:**
- US Letter: 612 x 792 points (8.5" x 11")
- A4: 595 x 842 points
- Legal: 612 x 1008 points

### Key Operators

**`cm` (Concatenate Matrix):** Modifies the Current Transformation Matrix (CTM), affecting all subsequent graphics operations.

**`Tm` (Set Text Matrix):** Modifies only the text matrix, governing text placement and transformation.

Both operators **accumulate** transformations sequentially rather than replacing previous values.

### Graphics State Management
Use `q` (save) and `Q` (restore) operators to isolate transformations and prevent unintended cumulative effects.

### CTM and Text Matrix Interaction
Text rendering depends on **both** the CTM and text matrix (`Tm`). A CTM scaled by 50% will reduce both text size and positioning proportionally, even if `Tm` specifies different values.

**Final text position = CTM * TextMatrix * GlyphPosition**

---

## Resource Sharing

### Image XObjects
Rather than embedding the same image multiple times, PDFs store it once as an Image XObject and reference it.

**Structure:**
```
5 0 obj
<<
  /Type /XObject
  /Subtype /Image
  /Width 200
  /Height 100
  /ColorSpace /DeviceRGB
  /BitsPerComponent 8
  /Filter /DCTDecode
  /Length 12345
>>
stream
... binary image data ...
endstream
endobj
```

### Transformation at Placement
Resources are manipulated through transformation matrices during placement rather than modifying the original:
- **Scaling**: `[2 0 0 2 0 0]` for 2x enlargement
- **Rotation**: `[0 1 -1 0 0 0]` for 90-degree rotation

### Resource Dictionary
Shared resources are defined in the resource dictionary:
```
/Resources <<
  /XObject << /Logo 5 0 R >>
  /Font << /F1 10 0 R >>
>>
```

---

## PDF Object Types

PDF files contain eight core object types (CosObjects):

| Type | Description | Example |
|------|-------------|---------|
| **Null** | Represents null/empty value | `null` |
| **Boolean** | True or false | `true`, `false` |
| **Integer** | Whole numbers | `1`, `100`, `208` |
| **Real** | Decimal numbers | `0.05`, `130.23` |
| **Name** | Key identifiers | `/Type`, `/Page` |
| **String** | Text data | `(Hello)`, `<48656C6C6F>` |
| **Array** | Ordered collection | `[0 0 612 792]` |
| **Dictionary** | Key-value pairs | `<< /Type /Page >>` |
| **Stream** | Data + dictionary | Content streams, images |

### Direct vs Indirect Objects
- **Direct**: Inline values
- **Indirect**: Referenced by object ID and generation number (e.g., `5 0 R`)

---

## Compression Options

### Compression Filters

**Flate (Deflate/Zip):**
- Modern standard for content streams
- Good compression ratio with reasonable speed

**LZW:**
- Legacy format, phased out due to patent concerns
- Should be replaced with Flate

**ASCII-85:**
- Legacy 7-bit encoding for file transfer
- Actually **expands** file size
- Should be removed from modern files

### Optimization Techniques

**Font Optimization:**
- Consolidates identical FontDescriptors
- Merges duplicate encodings across pages

**XObject Optimization:**
- Merges identical forms and images using MD5 hash comparison

**Content Optimization:**
- Identifies common operator sequences
- Generates shareable substreams

**Object Stream Compression (PDF 1.5+):**
- Compresses entire collections of PDF objects into single streams
- Provides significant file size reduction

### Linearization (Fast Web View)
Reorganizes file structure for streaming display:
- First page data appears early in file
- Allows viewing before complete download

---

## Interactive PDF Features

### Bookmarks
- Hierarchical navigation reflecting document structure
- Can be expanded/collapsed
- Generated automatically from heading styles in Word/InDesign

### Hyperlinks
- **Internal**: Navigate within document
- **External**: Web URLs
- **Email**: Pre-filled message composition

### Annotations
- Link annotations define clickable regions
- Can specify destination page, position, and zoom level
- Useful for cross-references, citations, and navigation

---

## Practical Implications for PDF Rendering

1. **Matrix multiplication order matters** - Apply in correct sequence: scale, rotate, translate

2. **Pre-calculate combined matrices** - Reduces operations and floating-point errors during rendering

3. **Text position requires both CTM and Tm** - Cannot calculate text position from Tm alone

4. **Use q/Q for state isolation** - Prevents transformation accumulation across objects

5. **Resource sharing reduces memory** - Same font/image used multiple times references single object

6. **Coordinate system is bottom-left origin** - Y increases upward (opposite of most screen coordinates)
