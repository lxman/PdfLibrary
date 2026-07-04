# Brief: FreeText appearance stream does not break lines

**Date:** 2026-06-29
**Target repo:** PDF (Lxman.PdfLibrary)
**Requested by:** Focal project (Phase 3 annotations)
**Severity:** Functional bug — multi-line FreeText renders as a single overlapping line.

---

## Summary

When a FreeText annotation has multi-line `/Contents`, the generated `/AP /N` appearance
stream emits the entire string in **one `Tj`** with the raw line separators left inside the
string literal. PDF text-showing operators do **not** break lines on `\r`/`\n`, so every line
is painted on the same baseline and the overflow runs off the right edge of the box.

The annotation dictionary itself is correct — only the appearance-stream text layout is wrong.

---

## Reproduction

Focal added a FreeText with content `Hello my friend.\nHow are you getting along today?`,
Times-BoldItalic 12pt, blue, left-justified, then saved. The library produced:

### Annotation object (correct)
```
<</Type /Annot /Subtype /FreeText
  /Rect [27.915789 534.315796 236.210495 653.709473]
  /P 41 0 R
  /Contents (Hello my friend.\r\nHow are you getting along today?)
  /DA (/TiBI 12 Tf 0.08 0.25 0.7 rg)
  /Q 0
  /AP <</N 265 0 R>>>>
```

### Appearance stream 265 0 R (BUG)
```
/Tx BMC
q
BT
0.08 0.25 0.7 rg
/TiBI 12 Tf
1 0 0 1 2 105.3937 Tm
(Hello my friend.\r\nHow are you getting along today?) Tj   <-- whole string, one Tj
ET
Q
EMC
```

`BBox [0.0 0.0 208.294706 119.393677]`, font resource `/TiBI -> 264 0 R` (`/Times-BoldItalic`).

---

## Why it is wrong

- Inside a content-stream string literal, `\r` and `\n` are escape sequences for the **bytes**
  0x0D / 0x0A. They are passed to `Tj` as character codes, not as layout commands.
- `Tj` never advances to a new line. Times-BoldItalic has no glyphs at 0x0D/0x0A, so those
  bytes render as nothing (or `.notdef`), and both sentences collapse onto a single baseline:
  `Hello my friend.How are you getting along today?` — the tail overflowing the box.
- Storing `\r\n` in `/Contents` is correct and required (ISO 32000-1 §12.7.3.3 — the FreeText
  line separator is CR, LF, or CRLF). The fix belongs entirely in **appearance generation**,
  which must split `/Contents` on the line separators and lay each line out itself.

---

## Required fix

In the FreeText AP builder:

1. Split `/Contents` on the line separators (handle `\r\n`, `\r`, and `\n`).
2. Emit one show operator per line with a leading-based newline between lines.

### Expected output shape
```
/Tx BMC
q
BT
0.08 0.25 0.7 rg
/TiBI 12 Tf
14 TL                       % leading; see note below
2 105.3937 Td               % start position (use Td, not Tm, so T* works off it)
(Hello my friend.) Tj
T*                          % advance one line down by TL
(How are you getting along today?) Tj
ET
Q
EMC
```

### Details / acceptance criteria

- **Leading (`TL`):** use ~1.2 × font size (e.g. `14.4` for 12pt) unless the FreeText carries
  an explicit leading. Keep it consistent for all lines.
- **Start Y:** first baseline near the top inset of the BBox (current `105.3937` is fine as the
  first-line baseline); subsequent lines descend via `T*`.
- **Justification `/Q`:** continue to honor it. `/Q 0` left (current), `/Q 1` centered,
  `/Q 2` right — for centered/right, measure each line's width and offset its `Td`/start X
  per line. Left-justified (this case) needs no per-line measurement.
- **Empty lines:** a blank line between paragraphs must still consume one `T*` advance so
  vertical spacing is preserved.
- **String escaping:** each per-line literal must still escape `(`, `)`, and `\` — just no
  longer the newline bytes, which are now consumed as layout.
- **Single-line content:** unchanged behavior (one `Tj`, no `T*`).

---

## Round-trip test to add (xUnit, matches existing FreeText test idiom)

1. Create document, `AddFreeText` with content `"Line one\nLine two"`, save to bytes, reopen.
2. Assert `/Contents` round-trips with the CR/LF separator preserved.
3. Parse the `/AP /N` stream and assert it contains **two** show operators (`Tj`/`TJ`) and at
   least one line-advance (`T*` or a `Td`/`TD` with non-zero negative Y) between them — i.e.
   no single `Tj` carrying an embedded 0x0A/0x0D byte.
4. Render the annotation rect and assert non-background pixels appear on **two** distinct
   vertical bands (line one and line two are on separate baselines), not one.

---

## Out of scope

- Word-wrapping long single lines to fit the BBox width (Focal currently inserts explicit
  breaks). Only honor existing line separators in `/Contents` for this fix.
- Vertical centering of the text block within the BBox.
