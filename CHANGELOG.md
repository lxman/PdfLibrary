# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- **Embedded-files read API** — `PdfDocument.GetEmbeddedFiles()` returns read-only
  `EmbeddedFileDescriptor`s for the catalog's `/Names /EmbeddedFiles` name tree plus catalog-level
  `/AF` associated files (ISO 32000-2, 7.11.4 / 14.13): the name-tree key, `/F` and `/UF` file
  names, `/Desc`, `/AFRelationship`, the stream's MIME `/Subtype`, catalog-`/AF` membership, and
  the decoded file bytes. Content failures degrade per entry (`HasData = false`) — the reader
  never throws on malformed documents. First consumer: the EInvoice Factur-X bridge (extracting
  `factur-x.xml` from PDF/A-3 invoices); the API is generic to any embedded attachment.

## [2.4.0] - 2026-07-09

Minor release: a **read-only conformance preflight** for archival, print, and accessibility PDF
standards, plus a large batch of CMYK / colour-managed rendering fidelity work (validated against
Ghent Workgroup fixtures) and text-extraction correctness fixes. Additive and back-compatible —
the preflight is new public API and no existing behaviour changed.

### Added

- **Conformance preflight (read-only validation)** — a new public `PdfLibrary.Conformance` surface
  validates a loaded document against five ISO PDF standards and reports structured findings,
  without ever modifying the document:
  - `Preflighter.Check(PdfDocument | byte[] | string path, ConformanceProfile)` → `PreflightResult`
    (`Conforms`, `Findings`, `Errors`).
  - `ConformanceProfile` (`[Flags]`): `PdfA2b` / `PdfA2u` / `PdfA3b` (ISO 19005-2/3 archival),
    `PdfX4` (ISO 15930-7 print), and `PdfUA1` (ISO 14289-1 accessibility).
  - Each `Finding` carries a `FindingSeverity` (`Error` / `Warning` / `Info`), the governing ISO
    clause, a human-readable message, and the offending page index / object number where applicable.
  - **Honest boundaries:** this is a *structural* validator — a deliberately partial, machine-
    decidable subset of each standard, not a certification. A "conforms" result means "no violations
    among the checked rules." PDF/X-4 covers the structural core plus colour/version governance (not
    the full Ghent print profile); PDF/UA-1 covers the machine-decidable subset (tagging, structure
    nesting, tables, language identifiers) — reading order and meaningful alternative text remain
    human judgment. Rules are cross-checked against the veraPDF conformance corpus and tuned for zero
    false positives on conformant files.
- **16-bit-per-component images** render at full depth (GWG180–184).
- **Optional Content (layer) visibility** is honoured for marked content — content in a hidden OCG
  is not painted (GWG150–152).
- **Transparency-group rendering SPI** (`IRenderTarget.RenderTransparencyGroup`) so render targets
  can composite isolated / knockout transparency groups.
- **Type 6 and 7 mesh shadings** (Coons and tensor-product patch meshes) are decoded and tessellated.
- **Native DeviceCMYK samples** are surfaced for images and shadings, so colour-managed render
  targets receive real ink values instead of an RGB round-trip.
- **Read path:** `Link` annotation targets, and bookmark destinations resolved from GoTo actions and
  named destinations, are now exposed.

### Changed

- **Saving to a file path is now atomic.** `PdfDocument.Save(path)`, `PdfDocumentEditor.Save(path, …)`,
  `PdfDocumentBuilder.Save(path)`, and `PdfOptimizer.Optimize(document, path, …)` write to a temporary
  file in the destination's directory and rename it into place, instead of truncating the destination
  before writing. An interrupted or failed save no longer destroys the existing file, so overwriting a
  source — e.g. saving a merge back over one of its inputs — is safe. The stream overloads are unchanged
  (the library cannot make a caller-owned stream atomic).

### Fixed

- **Colour-managed CMYK image pipeline (Ghent Workgroup fidelity):**
  - DeviceCMYK images route through the SWOP ICC profile; ICCBased-CMYK images through their own
    source profile rather than raw ink (GWG130); DeviceGray images separate to the K plate instead
    of an RGB round-trip (GWG173).
  - JPXDecode (JPEG 2000) images take CMYK samples as native ink (GWG170) and honour the PDF
    ICCBased colour space (GWG172).
  - CMYK JPEG inversion is driven by `/Decode` rather than sniffing the Adobe marker; `/Decode` is
    honoured on Separation/DeviceN images; Separation/DeviceN and Indexed-DeviceCMYK palettes decode
    via the tint transform. Basic overprint applies to images by colorant set, and non-colorant
    plates are preserved for Separation/DeviceN overprint.
- **DeviceN fills** resolve correctly, and the sampled (`Type 0`) function's multi-input index order
  was corrected (it was reversed vs ISO 32000-1 §7.10.2, zeroing some tint transforms). Shading
  `/ColorSpace` is now resolved so spot-colour ramps render.
- **`v` / `y` cubic Bézier** path operators are implemented (were previously dropped).
- **Page content streams are concatenated before parsing**, so an operator split across a stream
  boundary still parses (ISO 32000-1 §7.8.2).
- **AES-256 decryption:** the file key no longer has PKCS#7 padding stripped, which had broken
  decryption of some AES-256 documents.
- **High character codes render in embedded Type1/CFF fonts with standard encodings.** The
  WinAnsi/MacRoman/Latin-1 encoding factories populated only the code→Unicode table, so
  `PdfFontEncoding.GetGlyphName` returned null for every code ≥ 127 and the renderer's name-based
  charstring lookup drew `.notdef` (nothing) — e.g. the ISO 32000-1 footer's `©` (0xA9) and en dash
  (0x96) extracted correctly but rendered blank. Setting a Unicode mapping now also derives the
  Adobe Glyph List name; explicit names (base tables, `/Differences`) still win.
- **Text extraction honors `Tc`/`Tw`/`Tz`.** Fragment advances and `TJ` kern adjustments now use the
  full ISO 32000-1 §9.4.4 displacement — `tx = (w0×Tfs + Tc + Tw) × Th`, with word spacing applied
  only to single-byte code 32 — so extraction geometry stays on the rendered glyphs on documents
  using character/word spacing (e.g. justified text) or horizontal scaling. The renderer's advance
  loops (`Tj`, `TJ`, and the four Type3 paths) were aligned to the same spec ordering; previously
  they scaled by `Th` before adding `Tc`/`Tw` and triggered word spacing on the decoded glyph
  instead of code 32.
- **Text extraction pen advances honor the text-matrix horizontal scale.** Documents that select a
  font at size 1 and scale via `Tm` (e.g. `/F1 1 Tf` + `28 0 0 28 x y Tm`) produced fragment maps
  compressed by the scale factor — fragment widths, run-to-run pen positions, and `TJ` kern
  adjustments were all unscaled while `FontSize` itself was reported scaled. This broke
  consumer-built highlight rects and hit-testing. Advances and `TJ` adjustments now multiply by the
  text matrix's horizontal scale.
- **Form-XObject-hosted fragments are reported in page space.** Nested fragments are transformed by
  the form's `/Matrix` composed with the CTM at `Do` time (widths/font sizes scaled accordingly)
  instead of leaking the form's local coordinates, and a separator now prevents outer text gluing
  directly onto form-hosted text in the assembled string. Cyclic Form XObject recursion during text
  extraction is guarded against.
- **Type0 code width** — the extractor read Type0 (CID) codes as single bytes when computing widths;
  it now reads full multi-byte codes.

### Removed

- `PdfGraphicsState.GetCharacterAdvance` — dead public helper with no callers (its formula had
  drifted from the renderer); removed rather than left as a trap.

## [2.3.0] - 2026-07-04

Minor release: AcroForm field **authoring** on existing documents, text-extraction fixes that make
search viable on real-world PDFs, and a field-tree-aware page import. Additive and back-compatible.

### Added

- **Forms authoring API** — create, remove, and reshape AcroForm fields on any loaded document via
  `PdfDocumentEditor.Forms`: `AddTextField`, `AddCheckbox`, `AddRadioGroup` (per-option widget
  placement via `PdfRadioOptionPlacement`), `AddDropdown`, `AddSignatureField`, and `Remove(fullName)`
  (field + widget removal with AcroForm pruning). The AcroForm dictionary is bootstrapped on documents
  that have no form, without setting `/NeedAppearances` — created fields carry real appearance streams.
- **Field mutations** — `PdfFormField.Rename` (collision-validated) and `SetWidgetRect` (with
  appearance regeneration), plus settable `IsReadOnly`, `IsRequired`, `FontName`, `FontSize`
  (`/Ff` and `/DA` writes), text `MaxLength` / `IsMultiline` / `Quadding`, and choice `Options`
  (dropping stale selections).
- **`TextFragment.Width`** — total advance width, so consumers can build highlight rects.

### Fixed

- **Text extraction positions** — fragments now track an advancing pen cursor (positions were
  previously reported at the line start for every fragment on kern-split lines) and carry a
  `TextOffset` into the assembled page text; XObject-hosted fragments rebase their offsets onto the
  outer text. Also corrected Y-flip compensation and `Tz` double-scaling in glyph placement.
- **Page import brings only the page's own fields.** Importing a page from an XFA-style form (all
  fields under one root, e.g. the IRS W-2) used to drag the source's entire field forest — and orphan
  clones of its pages — into the target via widget `/Parent` → root → `/Kids`. The cloner now defers
  widget `/Parent` edges and rebuilds only the ancestor spine with `/Kids` filtered to the imported
  children; names, flags, values, and inheritance are preserved.
- **`/Ff` writes seed from effective inherited flags** (a naive seed could silently drop inherited
  bits, degrading radio groups to checkboxes); the field-mutation surface is guarded against
  dynamic-XFA documents.

### Performance

- Type3 fonts: shared system font index, cached char-proc operations, lazy hot-path logging.

## [2.2.0] - 2026-06-30

Minor release: an ICC-based CMYK color pipeline. Additive and back-compatible — the ICC CMYK path
is **opt-in** (`PdfColorToRgb.UseIccForDeviceCmyk` defaults `false`), so default-configured consumers
render byte-identically to 2.1.0.

### Added

- **CMYK ICC color primitives.** A bidirectional `DeviceCmykConverter` (CMYK↔sRGB) with a pluggable
  `CmykProfileProvider`, bundling an unencumbered CC0 default profile (`SWOP_TR003_coated_3`). Consumers
  can override the profile via `CmykProfileProvider.OverrideProfileBytes` (bytes > path > bundled).
- **ICC-accurate DeviceCMYK / Separation rendering** behind `PdfColorToRgb.UseIccForDeviceCmyk`. When
  enabled, DeviceCMYK and Separation colors convert through the ICC profile using the **Perceptual**
  rendering intent (matching how design apps display press CMYK: a realistic black point and tone
  mapping, rather than RelativeColorimetric's media-white clipping that renders darks too light).

### Changed

- `DeviceCmykConverter` builds its ICC transforms with **Perceptual** intent (was the ICCSharp default
  RelativeColorimetric). Only affects consumers that opt into `UseIccForDeviceCmyk`; the naive-formula
  default path is unchanged. Falls back to naive CMYK math if the ICC transform cannot be built.

## [2.1.0] - 2026-06-28

Minor release: markup-annotation authoring/editing with real appearance generation, a richer annotation reader, and two annotation/forms correctness fixes. Additive and back-compatible.

### Added

- **Markup annotations on existing documents** via `PdfDocumentEditor.Pages` (the editing-add path): `AddSquare`, `AddCircle`, `AddLine`, `AddInk`, `AddFreeText`. Each returns a stable annotation id and generates a real `/AP /N` appearance stream, so the annotation renders in this library's `PdfRenderer` and in external viewers.
- **Markup annotations when authoring new documents** via `PdfPageBuilder`: `AddSquare`, `AddCircle`, `AddLine`, `AddInk`, `AddFreeText` — mirrored through `PdfDocumentWriter`, which now emits an `/AP` appearance stream per annotation (both paths share one content generator so the drawn appearance is identical).
- **`/AP` for Highlight and Note (Text) annotations** — the existing `AddHighlight` / `AddNote` add paths now generate appearance streams too, so they render in this library's renderer (previously they only showed in viewers that synthesize their own appearances).
- **Richer annotation reader.** `PdfAnnotationInfo` gains `AnnotationId` (stable PDF object number), `StrokeColor` (`/C`), `InteriorColor` (`/IC`), `BorderWidth` (`/BS /W`), `LineEndpoints` (`/L`), `InkPaths` (`/InkList`), `Quadding` (`/Q`), and `DefaultAppearance` (`/DA`).
- **`PdfPageCollection.RemoveAnnotation(int page, int annotationId)`** — identity-based deletion (the positional `RemoveAnnotationAt` remains for back-compat).

### Fixed

- **`PdfRadioGroupBuilder` emitted unusable radio fields.** The builder wrote a single zero-rect object with no `/FT`, `/Ff`, `/Kids`, `/V`, or `/AP`, so radio groups read back as `Unknown`. The writer now emits a parent `/Btn` field plus one widget per option, each with a real vector-circle `/AP` appearance (ring for off, ring + dot for on), and no rectangular `/BS` border.
- **Flattening lost radios/checkboxes in Acrobat.** `FormFlattener` only baked single-stream `/AP /N`; for state-keyed button appearances it skipped painting AND removing the widget, leaving orphaned widgets that Adobe pruned on resave. It now bakes the stream named by each widget's `/AS` and removes the widget.

## [2.0.0] - 2026-06-27

Major release: SkiaSharp-free core, geometry-only renderer SPI, WPF render target, fillable-forms viewer, and a set of breaking API cleanups that could not be done in a 1.x patch.

### Added

- **`Lxman.PdfLibrary.Rendering.Wpf` package** (Windows-only) — new published NuGet package. Renders PDF pages to a retained WPF `DrawingGroup` (vector geometry, crisp at any zoom) via `page.RenderToDrawing(double scale)`. `DrawingGroup.ToPageImage(pixelWidth, pixelHeight)` extension wraps the group into a frozen `DrawingImage` with correct page-rect bounds for `<Image Stretch="Uniform"/>`.
- **Forms geometry API**: `PdfPage.GetGeometry(double scale)` → `PageGeometry` — maps between PDF user space and rendered-image pixels. Use it to place native UI controls (text boxes, checkboxes) exactly over form fields.
  - `PageGeometry`: `PdfToImage` / `ImageToPdf` (`Matrix3x2`); `PixelWidth` / `PixelHeight`; `MapRectToImage(PdfRect)` → `ImageRect`.
  - `ImageRect`: axis-aligned rect in image-pixel space (`X`, `Y`, `Width`, `Height`).
  - `PdfFieldWidget`: per-widget record on a form field — `Rect` (PDF user space), `PageIndex`, `OnState` (for radio/checkbox).
  - `PdfFormField.Widgets` (`IReadOnlyList<PdfFieldWidget>`) — the widget annotations for a field.
  - `PdfFormField.FontName` / `FontSize` — the field's `/DA` appearance font name and size, exposed for host-side text rendering at the correct size.
- **SVG render target** (`PdfLibrary.Rendering.Svg.SvgRenderTarget`) — `page.RenderToSvg()` reference implementation; demonstrates implementing `IRenderTarget` with a non-SkiaSharp backend.
- **`PdfImageBuilder.Done()`** — terminal method that returns the parent `PdfPageBuilder`, consistent with `PdfTextBuilder.Done()` and `PdfPathBuilder.Done()`.
- **`PdfColor.Components`** and **`PdfColor.GrayValue`** — public accessors for the raw component array and gray channel value respectively (previously accessible only through the typed channel properties).

### Changed

- **Rendering architecture — SkiaSharp removed from core.** `Lxman.PdfLibrary` is now SkiaSharp-free. The rendering pipeline (`PdfLibrary.Rendering`) defines a geometry-only `IRenderTarget` SPI: all page content — including text — is resolved to glyph-outline paths by the core before any render target method is called. Render targets receive only path geometry, image data, and state-management calls; they never touch font files, glyph tables, or filter decoding.
- **Std-14 font substitute system.** `SystemFontLocator` maps standard-14 BaseFont names to system font files and caches them; `SubstituteFontResolver` classifies embedded fonts, locates system substitutes, and exposes `GetFontData`. `GlyphPathService` + `GlyphPlacement` handle cached glyph-space path building and Y-flip-corrected placement. The core text pipeline (`CoreTextRenderer`) uses these to resolve all embedded and non-embedded fonts without SkiaSharp.
- **`PdfRenderer` text pipeline** flipped to core: embedded text is rendered via `CoreTextRenderer` (glyph outlines → `FillPath`); non-embedded text falls back to system-substitute outlines with the CTM baked in. `DrawText`/`MeasureTextWidth` removed from `IRenderTarget`; there is no text-draw SPI.
- **`IRenderTarget`** now has 17 required members and 2 optional no-op members (`PaintShading`, `FillPathWithShadingPattern`); the old `DrawText` / `MeasureTextWidth` members are gone.
- **SkiaSharp migrated to 4.x** (in the in-repo `PdfLibrary.Rendering.SkiaSharp` test gate): full API migration to the SkiaSharp 4 surface (`SKSamplingOptions`, `SKFont`, etc.).
- **`WpfRenderTarget`** — page initial transform unified via shared `PageTransform.Build`; `PdfImageToRgba` hoist moves RGBA conversion to core so WPF and SkiaSharp targets share the same decoded pixels.
- **Viewer app** (`PdfLibrary.Wpf.Viewer`) rewired to pure WPF: renders via `WpfRenderTarget` → `DrawingGroup` displayed in a `<DrawingImage>`; export/print via `RenderTargetBitmap`; no SkiaSharp dependency. Overlays native WPF controls (TextBox, CheckBox, RadioButton, ComboBox, ListBox) over form fields with write-back; honors field quadding (`TextAlignment`) and `/DA` font/size (scaled to zoom). Save and Flatten commands commit field values and bake them into page content.
- **Test projects** — all projects migrated from xUnit v2 to xUnit v3 (`xunit.v3`, `xunit.v3.assert`).

### Breaking

- **`IRenderBuilder<TImage>` removed.** Dead interface with no in-tree implementation; callers referencing it must be updated.
- **`ImageFormat` enum removed** (the one in `PdfLibrary.Rendering`, not `FontParser.BlocImageFormat`). No replacement — this type had no documented purpose and no callers outside internal code.
- **`ButtonKind.Push` renamed to `ButtonKind.PushButton`** to match the PDF specification term. Update any `== ButtonKind.Push` comparisons.
- **`IRenderTarget.DrawText` / `MeasureTextWidth` removed.** Text rendering is now handled entirely by the core. Implementations of `IRenderTarget` that forwarded these calls must remove them; they are no longer invoked.
- **`SkiaSharpRenderTarget.SetSoftMask` / `EnablePerfTrace` internalized.** These were public only by accident; no stable consumer API existed.
- **`Lxman.PdfLibrary.Rendering.SkiaSharp` not published.** Any project that consumed this NuGet package must switch to `Lxman.PdfLibrary.Rendering.Wpf` (Windows) or a custom `IRenderTarget` implementation.

### Removed

- **`IRenderBuilder<TImage>` interface** — removed from `PdfLibrary.Rendering`.
- **`ImageFormat` enum** — removed from `PdfLibrary.Rendering`.
- **`DrawText` / `MeasureTextWidth`** from `IRenderTarget`.
- **`SkiaSharpRenderTarget` (published NuGet package)** — the `Lxman.PdfLibrary.Rendering.SkiaSharp` package is no longer published. The in-repo project remains as a test-only pixel-fidelity gate.
- **SkiaSharp dependency from the core** (`PdfLibrary.csproj`) — the main package no longer transitively pulls in SkiaSharp native binaries.
- **Generator logo SkiaSharp call** — `PdfLibrary.Examples` generator dropped SkiaSharp; logo loaded from a static asset.

### Fixed

- **`WpfRenderTarget` page-rect bounds** — `ToPageImage` now wraps the drawn content at the correct page bounds, preventing `Stretch` distortion on pages whose drawn content is smaller than the media box.
- **`WpfRenderTarget.RenderToDrawing` resource resolution** — passes `page.Document` so `DrawImage` correctly resolves indirect-reference image streams.
- **Viewer: in-progress text-field edit committed before Save/Flatten** — prevents losing the current edit if focus has not moved.
- **Viewer: `SkiaSharpRenderTarget` v4 migration** — `DrawImage` `SKSamplingOptions` overload fix.
- **`FontParser` `.ttc` table offsets** — table offsets in TrueType Collections are file-absolute; the earlier font-base adjustment was incorrect and has been removed.
- **SVG target: stroke-width and dash arrays** scaled by the CTM linear factor — figure lines were rendering at the wrong thickness inside scaled coordinate systems.
- **`SubstituteFontResolver`** — faux-bold is not applied to substitute faces (the weight is already encoded in the face selection).

## [1.1.0] - 2026-06-25

The final 1.x release — additive API-surface cleanup plus the accumulated correctness fixes. No breaking changes; those are reserved for the 2.0 line.

### Added
- **Public exception hierarchy.** `PdfParseException` and `PdfSecurityException` are now `public` and derive from a new `public abstract PdfLibrary.PdfException` base. Consumers can `catch (PdfException)` to handle any PDF-specific failure, or catch the specific subtype to distinguish a malformed document from a decryption/password failure. Previously both were `internal`, so callers had to catch bare `Exception`.
- **`PdfDocumentEditor.Open(Stream, password?, leaveOpen?)`** — enter edit mode directly over an in-memory or network stream, matching `PdfDocument.Load(Stream)`. Previously only a file-path overload existed, forcing stream callers through `PdfDocument.Load(stream).Edit()`.
- **`PdfOptimizer.Optimize(document, string outputPath, options?)`** — optimize straight to a file path, matching `PdfDocument.Save(string)`. Previously only a `Stream` overload existed.
- **Collection-facade read/remove parity.** `PdfOutlineCollection.RemoveAt(int)` removes a top-level outline item by index (previously required `outlines[i].Remove()`); `PdfFormFields` now implements `IReadOnlyCollection<PdfFormField>`, exposing a `Count` property (previously `.Count` was only the LINQ extension method); `PdfNamedDestinations` gains a `this[string]` indexer (sugar for `Get`, parallel to `PdfFormFields[name]`).
- **`PdfOptimizer.Optimize` now returns a `PdfOptimizationResult`** (both the `Stream` and file-path overloads), reporting objects before/after/removed, output byte count, and per-pass counts (streams compressed, images recompressed, fonts subsetted). The return value is additive — existing call sites that ignore it still compile.
- **`PdfDocumentBuilder.LoadFont(byte[], alias)` and `LoadFont(Stream, alias)`** — embed a custom TrueType/OpenType font from in-memory bytes or a stream (an embedded resource, a network download, etc.), not just a file path. All three overloads share one validation/registration path.
- **`PdfSaveOptions.Default`** — a new instance with the standard defaults, parallel to `PdfOptimizationOptions.Default`.
- **Annotation read/remove on `PdfPageCollection`.** `GetAnnotations(int)` returns a read-only `PdfAnnotationInfo` snapshot (subtype, rect, contents) and `RemoveAnnotationAt(int, int)` removes one by index — the editing API was previously annotation-add-only.
- **Navigation facade completeness.** `PdfNamedDestinations.Entries()` enumerates `(name, destination)` pairs (the facade previously enumerated names only); `PdfOutlineCollection.Insert(int, …)` inserts a top-level outline item at a specific position (it previously only appended).
- **More `PdfViewerSettings` keys.** Added the commonly-used remaining `/ViewerPreferences` entries: `HideMenubar`, `HideWindowUI`, `NonFullScreenPageMode`, `Direction` (`PdfReadingDirection`), `PrintScaling` (`PdfPrintScaling`), and `Duplex` (`PdfDuplex`). Previously only four boolean keys were exposed.
- **`PdfPageBuilder.AddLine(PdfLength, …)`** — an explicit-unit overload for lines, matching `AddText`/`AddRectangle`.

### Changed
- **Documentation consolidated.** The separate creation, editing, and getting-started guides are merged into a single coherent `Docs/Guide.md`, with every example compile-checked against the library as an external consumer — fixing long-standing non-compiling snippets that the old guides and README carried.

### Fixed
- **Rendered pages now have the documented white background.** The fluent render path (`page.RenderTo().ToImage()/ToBytes()/ToStream()/ToFile()` and the `doc.SavePageAs(...)` shortcut) returned the raw transparent render instead of compositing onto white, so PNGs came out transparent and JPEGs black — even though `RenderTo()` documents a white default and `WithTransparentBackground()` is the opt-in for transparency. `SkiaSharpRenderTarget.GetImage()` now composites onto opaque white unless transparent output was explicitly requested, matching what the `SaveToFile` path already did. Soft-mask rendering is unaffected (it renders to a transparent target by design).
- **`PdfPageBuilder.AddText(text, x, y, fontName, fontSize)` now honors the page's unit and origin.** The five-argument (font-bearing) overload deposited raw coordinates, ignoring `WithInches()` / `WithUnit(...)` / `FromTopLeft()`, so text placed through it landed at the wrong position (e.g. at 1 point instead of 72 on an inches page). It now applies the same conversion as the `AddText(text, x, y)` overload.
- **`PdfPageBuilder.AddLine(double, …)` now honors the page unit and origin.** It deposited raw coordinates, ignoring `WithInches()` / `WithUnit(...)` / `FromTopLeft()` (unlike `AddText` and `AddRectangle`), so lines were mispositioned on non-point pages. It now applies the same `ConvertToPoints` conversion.
- **`PdfViewerSettings` boolean preferences can now be cleared.** Setting `HideToolbar` / `FitWindow` / `CenterWindow` / `DisplayDocTitle` to `null` now removes the preference (matching the `PageMode` / `PageLayout` setters). Previously a `null` assignment was silently ignored, so a preference could never be unset once written.

## [1.0.1] - 2026-06-24

Packaging and discoverability patch. No API or runtime behavior changes.

### Added
- **Package icon** on both `Lxman.PdfLibrary` and `Lxman.PdfLibrary.Rendering.SkiaSharp`.
- **SourceLink + symbol packages** — both packages now publish `.snupkg` symbols with embedded SourceLink metadata, so consumers can step into library source while debugging. The publish workflow pushes the symbol packages alongside the main packages.

### Changed
- **NuGet metadata** — broadened `PackageTags` (now includes `pdf-editor`, `csharp`, `cross-platform`, `pure-csharp`, `managed`, `pdf2.0`, `pdf-to-image`) and clarified the descriptions to lead with the "pure C#, no native/third-party codec dependencies, cross-platform" differentiator. `Authors` set to `Michael Jordan`.

### Fixed
- **README** clone URL corrected (`github.com/lxman/PDF` → `github.com/lxman/PdfLibrary`).
- **Build warnings** — resolved `CS0108` (`Type0Font.Encoding` renamed to `EncodingName` to stop hiding the base `PdfFont.Encoding`) and `CS8600` (nullable `out` in `GlyphUsageCollector`). Library now builds warning-free.

## [1.0.0] - 2026-06-21

First stable release. Completes the *load → edit → optimize* story: a loaded PDF can now be modified in place and shrunk, in addition to being parsed, rendered, and created from scratch.

### Added
- **Editing / mutation API** — `doc.Edit()` returns a `PdfDocumentEditor` over a loaded document (see `Docs/Guide.md`):
  - **Page operations** — rotate, move, delete, insert blank, import/duplicate pages, append, plus document `Merge` and `Extract`. Deleting a page strips outline entries, named destinations, and link annotations that resolved to it.
  - **Stamping & overlays** — `edit.Pages.Stamp/StampRange/StampAll` with a fluent `PdfStampBuilder`: watermarks, image stamps, placement presets (center/corners/diagonal/tiled/explicit), scale, rotation, opacity, overlay/underlay.
  - **Annotations** — `AddNote`, `AddLink`, `AddExternalLink`, `AddHighlight`.
  - **Document metadata** — `edit.Metadata` writes the Info dictionary and a synced XMP packet (`/Catalog /Metadata`).
  - **Navigation** — `edit.Outlines` (mutable bookmark tree), `edit.PageLabels`, `edit.NamedDestinations`, `edit.ViewerSettings` (page mode/layout, open action, viewer preference flags).
  - **Form filling** — `edit.Forms` exposes typed `PdfTextField` / `PdfButtonField` / `PdfChoiceField` / `PdfSignatureField`. Setting a value regenerates the field's appearance stream (single-line, multiline, comb, combo, list; checkbox/radio appearances are generated when absent), marks the widget printable, and `Flatten()` bakes fields into static page content.
  - **Save** — full-rewrite save with reachability garbage collection; optional object-stream + xref-stream packing (`UseObjectStreams`).
- **Optimization API** — `PdfOptimizer.Optimize`: Flate stream compression, unreachable-object GC, object-stream/xref-stream packing, opt-in lossy image recompression, and opt-in font subsetting (TrueType, CFF Type1C, and CIDFontType0C).
- **Multi-targeting** — the shippable packages (`Lxman.PdfLibrary`, `Lxman.PdfLibrary.Rendering.SkiaSharp`) now target **.NET 8, 9, and 10** (previously .NET 10 only).
- **Unicode text** — all text-valued API (metadata, outline titles, field values, annotation contents) round-trips arbitrary Unicode: single-byte PDFDocEncoding when representable, UTF-16BE otherwise.

### Changed
- **All NuGet dependencies pinned to stable releases** — no preview/dev builds: SkiaSharp `3.119.4`, Microsoft.Extensions.Caching.Memory `10.0.9`, Serilog `4.3.1`, Serilog.Sinks.File `7.0.0`. (SkiaSharp 4.x is deferred: it has no stable release and obsoletes the mutable `SKPath` API the renderer is built on.)

### Fixed
- **Culture invariance (library-wide).** The creation writer emitted decimals with the thread culture and the parser read numbers without `InvariantCulture`, so creating and parsing PDFs broke (or silently corrupted) under non-`.`-decimal cultures (de-DE, fr-FR, etc.). Both sites are now invariant.
- **String round-trip corruption.** `PdfString` literal escapes were written in decimal but read back as octal, corrupting every byte ≥ 64 (UTF-16 titles, URLs, indexed palettes) on a save/optimize round-trip.
- **Form-XObject text extraction.** The extractor dropped the `PdfDocument` when descending into Form XObjects, losing/garbling text whose body lives in a form (and the same bug in soft-mask rendering).
- **Annotation-appearance rendering.** Text inside an annotation appearance rendered at the page origin (CTM not pushed to the target); per-annotation CTM accumulated so only the first widget landed; Form XObjects didn't inherit ExtGState transparency (watermarks painted at full opacity); widget appearances were skipped entirely.
- **Form-fill output.** Filled values now set the `/F` Print flag so they print (not just display); multiline honors explicit newlines; checkbox/radio marks are drawn as vector paths so they render in any viewer.
- **Parser robustness.** Fixed a content-stream parser infinite loop on a zero-width token, added an image-recompression memory guard, and made `Optimize` accept encrypted input (decrypts to an unencrypted equivalent).

## [1.0.0-rc.4] - 2026-06-10

### Added
- **Axial/radial shading rendering.** The `sh` operator and PatternType 2 shading patterns now paint as gradients (ShadingType 2 axial / 3 radial). Previously `sh` was an unimplemented no-op, so gradients were silently dropped and only the flat base fill showed.
- **Complete PDF function support.** Implemented Type 3 (stitching) and Type 4 (PostScript calculator) functions. PdfLibrary now evaluates every PDF function type (0, 2, 3, 4), so shadings and Separation/DeviceN tint transforms that rely on them render correctly.

### Fixed
- **Page `/Rotate` normalization.** `/Rotate` is normalized into `[0, 360)`, so negative or out-of-range angles (e.g. `-90` from Distiller, equivalent to 270) render correctly instead of producing a blank page.
- **WinAnsiEncoding `/Widths`** calculation in `PdfDocumentWriter`.
- **Character spacing** on long lines.

### Changed
- **FontParser consolidated into a single embedded-font layer.** Pruned FontParser to the PDF-relevant core and routed embedded-font parsing through a new `SfntFont` entry point. Corrected CFF charstring subroutine execution (no state reset on recursion — previously corrupted subroutinized glyphs), the flex-family operators (`hflex`/`flex`/`hflex1`/`flex1`), `seac` accented composites, and cmap format 2/4 parsing; hardened `BigEndianReader` bounds checks.

## [1.0.0-rc.1] - 2026-06-06

### Added
- **Performance and Memory Optimizations**:
  - Integrated `ArrayPool<byte>` in `ImageRenderer` (SkiaSharp) to reduce GC pressure during image decoding.
  - Optimized `FlateDecodeFilter` and PNG/TIFF predictor functions with pre-allocated buffer capacities.
  - Improved `TextRenderer` with granular glyph path caching and more efficient matrix transformations.
  - Enhanced path rendering accuracy in `TextRenderer` and `PathRenderer` by correctly applying transformations during drawing.
  - Removed document-level cache clearing in `SkiaSharpRenderTarget` in favor of more granular caching, improving the safety of concurrent rendering.
- **In-house JPEG codec** (`ImageLibrary/JpegCodec`) — pure C# baseline + progressive JPEG encoder and decoder; replaces the vendored `JpegLibrary` git submodule. Used by `PdfLibrary.Filters.DctDecodeFilter` and `ImageLibrary.TiffCodec` for TIFF's JPEG sub-format.
- **In-house Netpbm codec** (`ImageLibrary/PbmCodec`) — pure C# decoder for `P1`–`P6` (ASCII + binary bitmap/graymap/pixmap) and binary encoder (`P4`/`P5`/`P6`). Wired into ImageUtility as `CustomPbmCodec` for `.pbm` / `.pgm` / `.ppm` / `.pnm`.
- **`CustomJpegCodec`, `CustomPngCodec`, `CustomBmpCodec`, `CustomGifCodec`, `CustomTgaCodec`, `CustomTiffCodec`** wrappers in `PdfLibrary.Utilities/ImageUtility/Codecs/`, making the in-tree `ImageLibrary` codecs the primary implementations in ImageUtility's `CodecRegistry`.
- **Thread safety — concurrent rendering (one document per thread)**: the library is now safe for multi-threaded, document-per-request rendering (e.g. ASP.NET Core), validated by a stress harness that hash-compares concurrent output against a single-threaded baseline (zero divergence) with managed memory bounded across thousands of renders. No process-wide render lock is required; the previously documented global `SemaphoreSlim(1,1)` render lock is obsolete. Constraint: a `PdfDocument` and a `SkiaSharpRenderTarget` must each be used by a single thread (load one document per request; one render target per render). Specific fixes:
  - Made the CFF charstring subroutine stack (`FontParser` `SubroutineNester`) per-parse instance state instead of a shared `static` — it was on the live CFF glyph render path and could silently corrupt glyph outlines under concurrent rendering.
  - Fixed `SystemFontResolver.GetTypeface` to resolve typefaces under the cache lock, preventing duplicate (leaked) native `SKTypeface` handles on concurrent first-use.
  - Added `volatile` to the double-checked-locking fields in `BuiltInProfiles` (sRGB), `LabToSrgb`, `AdobeGlyphList`, and `PdfLogger` (`PdfLogger` also snapshots its logger reference before use).
  - Synchronized `CodecRegistry` (lock + snapshot-on-read) and made `FontParser.Table.Types` and `FontParser.Models.Language.Ids` immutable.
  - Made `PanoseInterpreter._values` instance state (was an instance-shadowing `static`) and `SkiaSharpRenderTarget.EnablePerfTrace` a `volatile` flag.
  - Removed the process-wide glyph-cache clearing (`_lastDocument`) in `SkiaSharpRenderTarget` that caused a cache stampede when different documents rendered concurrently; the glyph cache is now keyed per font instance with a bounded size and sliding expiration.
- **ImageLibrary reorganized into per-codec projects.** The monolithic `ImageLibrary/ImageLibrary` project has been split. Each codec now lives in its own project under `ImageLibrary/`:
  - `ImageLibrary/CcittCodec` (was `ImageLibrary/ImageLibrary/Compression/Ccitt`)
  - `ImageLibrary/LzwCodec` (was `ImageLibrary/ImageLibrary/Compression/Lzw`)
  - `ImageLibrary/JpegCodec` (new, replaces `JpegLibrary` submodule)
  - `ImageLibrary/BmpCodec` (was `ImageLibrary/ImageLibrary/Container/Bmp`)
  - `ImageLibrary/GifCodec` (was `ImageLibrary/ImageLibrary/Container/Gif`)
  - `ImageLibrary/PngCodec` (was `ImageLibrary/ImageLibrary/Container/Png`)
  - `ImageLibrary/TgaCodec` (was `ImageLibrary/ImageLibrary/Container/Tga`)
  - `ImageLibrary/TiffCodec` (was `ImageLibrary/ImageLibrary/Container/Tiff`)
  - `ImageLibrary/PbmCodec` (new)
  - `ImageLibrary/Jbig2Decoder` (unchanged)
- Test projects renamed accordingly: `Compression.Ccitt.Tests` → `CcittCodec.Tests`, `Compression.Lzw.Tests` → `LzwCodec.Tests`.
- The legacy `Container/Jp2` decoder has been removed from `ImageLibrary` and replaced by the in-house `ImageLibrary/Jp2Codec` (pure C# JPEG 2000 decoder). `PdfLibrary.Filters.JpxDecodeFilter` now calls `Jp2Codec` directly. The earlier `Compressors/Compressors.Jpeg2000` wrapper (over `Melville.CSJ2K`) has been deleted; `Melville.CSJ2K` is retained only as a test-time differential reference in `ImageLibrary/Jp2Codec.Tests`.
- **Solution-wide warning cleanup**: build now produces 0 warnings, 0 errors. Real fixes for null-safety (`CS8602`/`CS8603`/`CS8604`/`CS8600`/`CS8625`), unreachable code (`CS0162`), obsolete SkiaSharp API usage (`CS0618` — `SKPaint.TextSize`/`Typeface`/`FilterQuality` migrated to `SKFont` + `SKSamplingOptions`), malformed XML doc comments, unused variables/fields, and several xUnit lint suggestions (`xUnit2013`/`xUnit2017`/`xUnit2002`/`xUnit1026`). Policy suppressions (documented in csproj) for `CS1591` in the two NuGet-published libraries and `NU1701` in `PdfLibrary.Wpf.Viewer`.
- **FontParser**: renamed the AAT-bloc `ImageFormat` enum to `BlocImageFormat` to resolve a `CS0436` type-conflict with `PdfLibrary`'s rendering `ImageFormat` enum (both were in the global namespace). External callers using the bloc-table image format must update accordingly.

### Removed
- **`JpegLibrary` git submodule.** The vendored `JpegLibrary` fork has been removed from the tree; cloning no longer requires `--recurse-submodules`. The `PdfLibrary.Filters.JpegLibraryAdapter` wrapper has also been deleted — `DctDecodeFilter` now uses `ImageLibrary.JpegCodec` directly.
- Removed the legacy aggregated `ImageLibrary.ImageLibrary` project; consumers should reference the specific codec project(s) they need.
- **ImageSharp dependency.** `SixLabors.ImageSharp` has been removed from `ImageUtility.csproj`. Every supported format is now backed by an in-tree codec; WebP support has been intentionally dropped (no in-house VP8/VP8L implementation is planned — WebP is not a PDF image filter).
- **Codec Settings UI in ImageUtility.** The Tools → Codec Settings dialog (`CodecSettingsWindow`) and the supporting `CodecConfiguration` preference store have been removed. With one codec per format there is no preference to express; `CodecRegistry.FindByExtension` now resolves directly without consulting user preferences.

## [0.0.10-beta] - 2025-01-13

### Added
- **Fluent Builder API** - Complete API for programmatic PDF creation
  - Document creation with `PdfDocumentBuilder.Create()`
  - Page content: text, images, shapes, and paths
  - Interactive forms: text fields, checkboxes, radio buttons, dropdowns, signature fields
  - Annotations: internal links, external links, notes (sticky notes), highlights
  - Bookmarks/outlines with hierarchical navigation
  - Page labels with multiple numbering styles (decimal, roman, letters)
  - Layers (Optional Content Groups) with visibility and print control
  - Document encryption (RC4, AES-128, AES-256)
  - Custom font embedding (TrueType, OpenType)
  - Metadata support (title, author, subject, keywords)
  - Flexible coordinate systems (points, inches, mm, cm) and origins

- **Integration Testing Framework** - Automated testing infrastructure
  - Visual comparison testing
  - Golden file generation and comparison

### Changed
- Split rendering framework into separate `PdfLibrary.Rendering.SkiaSharp` project
- Improved font resolution and fallback logic
- Enhanced text positioning accuracy

### Fixed
- Font widths calculation issues
- Text rotation rendering
- JPEG image rendering
- Clipping path issues with transformations
- Percentage calculation in color spaces

---

## [0.9.0] - 2024-XX-XX

### Added
- **PDF Rendering Engine**
  - Full PDF 1.x and 2.0 parsing support
  - High-quality rendering using SkiaSharp
  - Comprehensive graphics state handling

- **Color Space Support**
  - DeviceGray, DeviceRGB, DeviceCMYK
  - CalGray, CalRGB, Lab
  - ICCBased profiles
  - Indexed color spaces
  - Separation and DeviceN
  - Tiling and shading patterns

- **Image Decompression**
  - DCTDecode (JPEG)
  - FlateDecode (zlib/deflate)
  - LZWDecode
  - CCITTFaxDecode (Group 3 and 4)
  - JBIG2Decode
  - JPXDecode (JPEG2000)
  - RunLengthDecode
  - ASCII85Decode, ASCIIHexDecode

- **Font Support**
  - Type1 and Type1C (CFF) fonts
  - TrueType fonts
  - Type0 (CID) fonts for CJK
  - Type3 user-defined fonts
  - Embedded font extraction
  - System font fallback

- **Security**
  - RC4 40-bit and 128-bit decryption
  - AES 128-bit and 256-bit decryption
  - Permission handling

- **Content Stream Processing**
  - Graphics state operators
  - Path construction and painting
  - Text positioning and rendering
  - Color operators
  - XObject handling
  - Inline images
  - Marked content

### Infrastructure
- Modular compressor libraries (LZW, CCITT, JBIG2, JPEG2000)
- FontParser library for TrueType/OpenType parsing
- Centralized logging with Serilog
- Command-line tool (PdfTool) for testing

---

## Version History Summary

| Version | Date | Highlights |
|---------|------|------------|
| 0.9.0 | TBD | Core rendering engine, font support, image decompression |
| 0.0.10-beta | 2025-01-13 | Fluent builder API, annotations, bookmarks, page labels, encryption |
| 1.0.0-rc.1 | 2026-06-06 | Release candidate for 1.0: thread-safe concurrent rendering, pure-C# in-house image codecs (no third-party deps), performance + memory optimizations. |
| 1.0.0 | 2026-06-21 | First stable release: editing/mutation API, optimization API, multi-targets .NET 8/9/10, all dependencies on stable releases (SkiaSharp 3.119.4). |
| 1.1.0 | 2026-06-25 | Final 1.x: additive API cleanup (public exception hierarchy, stream/path overloads, optimizer result object, `LoadFont` byte[]/Stream, annotation read/remove, expanded viewer prefs) + correctness fixes (white-background rendering, `AddText`/`AddLine` unit handling); usage docs consolidated into a single verified guide. |
| 2.0.0 | 2026-06-27 | BREAKING: SkiaSharp-free core; geometry-only `IRenderTarget` SPI; WPF render target published (`Lxman.PdfLibrary.Rendering.Wpf`); SkiaSharp backend sunset (test-only); forms geometry API (`PageGeometry`, `PdfFieldWidget`); fillable-forms viewer (pure WPF); API cleanups (`IRenderBuilder`/`ImageFormat` removed, `ButtonKind.PushButton`); xUnit v3 migration. |
| 2.1.0 | 2026-06-28 | Markup-annotation authoring/editing with real appearance generation; richer annotation reader; radio-group + flatten fixes. |
| 2.2.0 | 2026-06-30 | Opt-in ICC-based CMYK colour pipeline (`UseIccForDeviceCmyk`); default render path unchanged. |
| 2.3.0 | 2026-07-04 | AcroForm field authoring on existing documents; text-extraction fixes; field-tree-aware page import. |
| 2.4.0 | 2026-07-09 | Read-only conformance preflight (PDF/A-2b/2u/3b, PDF/X-4, PDF/UA-1); CMYK / colour-managed render fidelity batch (Ghent Workgroup); 16-bit images; optional content; mesh shadings; transparency-group SPI; text-extraction fixes. |

---

## Migration Guide

### Upgrading to Fluent API

The new fluent API provides a simpler way to create PDFs:

**Before (manual object creation):**
```csharp
// Complex manual object construction
var doc = new PdfDocument();
var page = new PdfPage(612, 792);
var stream = new PdfContentStream();
stream.AddOperator("BT");
stream.AddOperator("/F1 12 Tf");
// ... etc
```

**After (fluent API):**
```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Hello, World!", 100, 700)
            .WithFont("Helvetica")
            .WithSize(12))
    .Save("output.pdf");
```

### Rendering API Changes

The rendering pipeline was refactored for better separation:

**Before:**
```csharp
// SkiaSharp directly in PdfLibrary
var renderer = new PdfRenderer(document);
renderer.RenderToSkia(canvas);
```

**After:**
```csharp
// Separate render target
using var target = new SkiaSharpRenderTarget(width, height);
var renderer = new PdfRenderer(document, target);
renderer.RenderPage(0);
target.SavePng("output.png");
```
