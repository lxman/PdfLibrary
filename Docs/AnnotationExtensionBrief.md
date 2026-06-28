# Feature Brief: Annotation subsystem extension (ink, shapes, free-text) + `/AP` generation + richer reader

**Status:** Open — feature brief (2026-06-28; not yet implemented)
**Requested by:** Focal (consumer app) for its Phase 3 annotation toolset. Focal will be built against the published `Lxman.PdfLibrary` once this lands — please **version-bump + publish** when done.
**Goal:** Let consumers add, render, read, and delete a fuller set of markup annotations on existing PDFs: **Ink (freehand), Square, Circle, Line, Free-Text** — plus make all library-added annotations actually **render** (generate `/AP` appearance streams), and enrich the read model with per-type data + a stable identity for edit/delete.

---

## TL;DR — the three things that matter most

1. **`/AP` generation is the linchpin.** The renderer (`PdfRenderer.RenderAnnotations`) draws annotations **only** from their `/AP /N` appearance stream and ignores `/Subtype` entirely (it's used only for a log line). The library currently writes **no `/AP` for any annotation** (highlight/text/link included). So **library-created annotations do not render in this library's own renderer** (they only show in external viewers like Acrobat that synthesize their own appearances). Every annotation this feature adds — and ideally the existing highlight/note — must get an `/AP /N` content stream generated at add-time. Build a new **`AnnotationAppearanceGenerator`** (modeled on `Editing/Forms/FieldAppearanceGenerator.cs`, the only existing programmatic content-stream builder).
2. **New types via the editing-add path** (`PdfPageCollection.Annotations` / `PdfPageAnnotator`) — that's the path the consumer uses to annotate a loaded document. Mirror in the builder path for new-document authoring.
3. **Richer `PdfAnnotationInfo` + stable identity.** The read model is `Subtype`/`Rect`/`Contents` only and deletion is positional (`RemoveAnnotationAt(index, annotationIndex)`). The UI needs per-type data (color, ink paths, line endpoints, border width, free-text DA) **and a stable annotation id** (the PDF object number) to render/select/edit/delete reliably.

---

## Current architecture (where to extend — file:line)

| Layer | File | Existing pattern | Extension point |
|---|---|---|---|
| Builder model | `Builder/Annotation/Pdf{Highlight,Text,Link}Annotation.cs` (+ `PdfAnnotation` base: `Id`, `Subtype`, `Rect`, `Border`, `Flags`) | each type extends `PdfAnnotation` + has a fluent `…Builder` | add `Pdf{Ink,Square,Circle,Line,FreeText}Annotation(.cs)` + builders |
| Builder add | `Builder/Page/PdfPageBuilder.cs` (~L675 `AddNote`, ~L700 `AddHighlight`) | per-type `Add…` appends to `_annotations` | add `AddInk/AddSquare/AddCircle/AddLine/AddFreeText` |
| Writer | `Builder/PdfDocumentWriter.cs` — `WriteAnnotationObject` (L2472) writes common keys, switch (L2504) → `WriteLinkAnnotation`/`WriteTextAnnotation`/`WriteHighlightAnnotation`. **No `/AP` written.** | per-subtype key writers | add `case Pdf{Ink,Square,Circle,Line,FreeText}Annotation` → write type keys **+ `/AP /N`** |
| Editing-add | `Editing/PdfPageCollection.Annotations.cs` (public `AddNote`/`AddLink`/`AddExternalLink`/`AddHighlight`/`GetAnnotations`/`RemoveAnnotationAt`) → `Editing/Annotations/PdfPageAnnotator.cs` (`NewAnnot` L52, `AppendToAnnots` L66). **No `/AP` generated.** | per-type dict construction + append to page `/Annots` | add `AddInk/AddSquare/AddCircle/AddLine/AddFreeText` here **+ `/AP /N`**; this is the consumer's primary path |
| Reader | `Editing/PdfAnnotationInfo.cs` (`Subtype`,`Rect`,`Contents`) + `GetAnnotations` (Annotations.cs L40) | reads 3 keys | enrich (below) + add stable id |
| Renderer | `Rendering/PdfRenderer.cs` `RenderAnnotations` (L205–406): reads `/AP` (L264), `/AP /N` (L278), BBox→Rect transform (L314), `ProcessOperators` (L397). `/Subtype` unused for rendering. | — | **NO CHANGE NEEDED** if `/AP /N` is generated at add-time |
| Appearance gen | none for annotations; `Editing/Forms/FieldAppearanceGenerator.cs` is the template (font/`Tf`/`Tj`/`re`/`f`/color ops) | — | **new** `Editing/Annotations/AnnotationAppearanceGenerator.cs` |

**Key consequence:** because the renderer is `/AP`-only and needs no change, the entire feature reduces to: (a) write the right annotation dict keys, and (b) generate a correct `/AP /N` stream for each type. Get those two right and it renders in this library AND in Acrobat/browsers.

---

## Work item 1 — `AnnotationAppearanceGenerator` (the linchpin)

New `Editing/Annotations/AnnotationAppearanceGenerator.cs`. Given an annotation dict (subtype + its geometry/color/width keys) produce a Form XObject `/AP /N` appearance stream whose content draws the annotation, with `/BBox` matching `/Rect` and the identity matrix (so the renderer's BBox→Rect transform places it correctly). Content streams are plain graphics operators (same toolbox as `FieldAppearanceGenerator`):

- **Ink:** for each path in `/InkList`, `m` to first point, `l` through the rest, set stroke color (`RG`) + width (`w`), `S` (stroke). Round joins/caps (`j`/`J`).
- **Square:** `re` + stroke (`S`) and/or fill (`f`/`B`) using `/C` (border `RG`) and `/IC` (interior `rg`), width from `/BS /W`.
- **Circle:** ellipse via 4 bézier `c` ops inscribed in `/Rect`; stroke/fill as Square.
- **Line:** `m`/`l` between `/L` endpoints; optional arrowheads per `/LE`; stroke color/width.
- **FreeText:** `BT`/`Tf`/`Td`/`Tj`/`ET` rendering `/Contents` per `/DA` (font, size, color) and `/Q` (quadding); optional background/border rect.

Provide a single entry like `AnnotationAppearanceGenerator.Generate(PdfDocument doc, PdfDictionary annotDict)` that builds + attaches `/AP /N`. **Also call it for the existing Highlight and Text(note) add paths** so those render in this library's renderer too (today they don't). Highlight appearance = filled quads in `/C` with Multiply blend (or simple fill) over `/QuadPoints`.

---

## Work item 2 — new annotation types (editing-add + builder + writer)

For each type: editing-add method (consumer path) + builder method (authoring) + writer case, all generating `/AP /N`. Required PDF dict keys:

| Type | Keys (besides `/Type /Annot`, `/Rect`, `/P`, `/AP`) | Add-API shape (suggested) |
|---|---|---|
| **Ink** | `/Subtype /Ink`, `/InkList [[x y x y …] …]`, `/C [r g b]`, `/BS << /W n >>` | `AddInk(int page, IReadOnlyList<IReadOnlyList<PdfPoint>> paths, PdfColor color, double width)` |
| **Square** | `/Subtype /Square`, `/C [r g b]`, `/IC [r g b]`? (interior), `/BS << /W n >>` | `AddSquare(int page, PdfRect rect, PdfColor stroke, PdfColor? fill, double width)` |
| **Circle** | `/Subtype /Circle`, `/C`, `/IC`?, `/BS` | `AddCircle(int page, PdfRect rect, PdfColor stroke, PdfColor? fill, double width)` |
| **Line** | `/Subtype /Line`, `/L [x1 y1 x2 y2]`, `/C`, `/BS`, `/LE [/None /None]` (or arrowheads) | `AddLine(int page, double x1,y1,x2,y2, PdfColor color, double width, lineEndings?)` |
| **FreeText** | `/Subtype /FreeText`, `/Contents`, `/DA "/Helv 12 Tf 0 0 0 rg"`, `/Q 0` | `AddFreeText(int page, PdfRect rect, string text, double fontSize, PdfColor color, int quadding=0)` |

`/Rect` for Ink/Line should be computed as the bounding box of the geometry (+ half border width). Reuse a `PdfPoint`-style struct if one exists; else accept `(double x, double y)` pairs. Coordinates are PDF user space (origin bottom-left), consistent with the existing `Add*` methods.

---

## Work item 3 — richer reader + stable identity

Enrich the read path so a UI can render/select/edit/delete:

- Extend `PdfAnnotationInfo` (subtype-specific subclasses preferred, or nullable per-type fields) to expose, per type: `/C` color, `/IC` interior color, `/BS` width, `/QuadPoints` (highlight), `/InkList` paths (ink), `/L` endpoints + `/LE` (line), `/Contents` + `/DA` + `/Q` (free-text), `/Name` icon (text note), `/A`/`/Dest` (link). Keep `Subtype`/`Rect`/`Contents` for back-compat.
- **Add a stable `AnnotationId`** — the PDF object number of the annotation (from the xref). `GetAnnotations` should populate it. Add identity-based edit/delete:
  - `RemoveAnnotation(int page, int annotationId)` (keep the positional `RemoveAnnotationAt` for back-compat, or deprecate).
  - Optionally `UpdateAnnotation…` for in-place property edits (color/contents/geometry) that regenerate `/AP`; if out of scope, the consumer can remove+re-add, but document that.

---

## Testing (xunit, in `PdfLibrary.Tests`)
For each new type: **round-trip** — add via the editing-add API to a built page → `Save` → reopen → `GetAnnotations` returns the type with correct geometry/color/contents + a non-null `AnnotationId`; **`/AP` present** — assert the saved annotation dict has `/AP /N` (and, ideally, rasterize the page via the renderer and assert non-background ink in the annotation's rect, mirroring the form appearance-render test approach). **Render regression** — a highlight/note added via the library now renders (has `/AP`). **Delete** — `RemoveAnnotation(page, id)` removes the right one and survives reopen. Keep existing annotation tests green.

## Acceptance
- Ink/Square/Circle/Line/FreeText can be added to an existing PDF via the editing API, saved, reopened, and read back with full per-type data + stable id.
- All added annotations carry an `/AP /N` stream and **render in this library's `PdfRenderer`** (verified by rasterization) and in external viewers.
- Existing highlight/text annotations also gain `/AP` so they render here too.
- Richer `PdfAnnotationInfo` + identity-based delete shipped; existing API back-compatible.
- **Version bump + publish** the package so Focal can consume it (note the new minimum version in the changelog).

---

## Notes / coordination
- Renderer needs **no changes** — do not modify `PdfRenderer`; rely on add-time `/AP /N`.
- This corrects an earlier assumption in Focal that "the renderer draws existing annotation appearances" — it only draws from `/AP`, which the library wasn't generating.
- Out of scope (Focal will not need these yet): text-selection multi-quad highlight (Focal lacks text selection), polygon/polyline, stamp/redaction annotations, JS actions. (Multi-quad highlight is a natural later add if `AddHighlight` grows a quads overload.)
- Separate from the radio-builder fix (already FIXED) — different files, no overlap.
