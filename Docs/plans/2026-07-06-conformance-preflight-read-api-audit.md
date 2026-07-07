# Conformance Preflight — Read-side API Audit

_2026-07-06. Reference for the PDF/A + PDF/X preflight (read-only validator) built under
`PdfLibrary/Conformance/`. Records the exact engine read APIs each rule calls, and what is
missing and must be built. Verified against source, not assumed._

## Placement (decided)

The navigation layer is almost entirely `internal`: `PdfDictionary`, `PdfArray`, `PdfName`,
`PdfStream`, `PdfString`, `PdfIndirectReference`, `PdfCatalog`, `PdfPageTree`, `PdfResources`,
`PdfFontDescriptor`, `ColorSpaceResolver`, and `PdfDocument.Trailer` / `GetCatalog()` /
`ResolveReference()` / `GetObject()` are all internal. Public: `PdfDocument`, `PdfPage`,
`PdfFont`, `XmpPacket`/`XmpProperty`, `PdfDocumentEditor`, `PdfPageCollection`,
`PdfAnnotationInfo`.

⇒ The preflight lives **inside `PdfLibrary.dll`** (`PdfLibrary/Conformance/`). Tests live in
`PdfLibrary.Tests` (already on the `InternalsVisibleTo` list — `PdfLibrary.csproj:61-66`, which
also lists `PdfLibrary.Rendering.SkiaSharp`, `PdfLibrary.Rendering.Wpf.Tests`,
`PdfLibrary.Integration`).

There is **no** `PdfObject.Resolve()` extension. Resolve indirect refs with
`document.ResolveReference(PdfIndirectReference)` / `GetObject(int)`, or use the per-model
wrappers that resolve for you. Call `document.MaterializeAllObjects()` before a whole-document
sweep (object streams / on-demand objects load lazily otherwise).

## Read API map (by rule need)

| # | Rule need | API | File:line | Notes |
|---|---|---|---|---|
| — | Load a doc | `PdfDocument.Load(path/stream[, password])` (public) | `PdfDocument.cs:448-486` | |
| 1 | `/Encrypt` present? | `document.Trailer.Encrypt` → `PdfIndirectReference?` | `PdfTrailer.cs:70` | Presence = structural. `document.IsEncrypted` (public, `PdfDocument.cs:60`) is true only if a decryptor was **built** — use `Trailer.Encrypt` for the presence rule. |
| 2 | Trailer `/ID` | `document.Trailer.Id` → `PdfArray?` | `PdfTrailer.cs:92` | Each element a `PdfString`; bytes via `.Bytes`. Builder always emits `/ID` (`PdfDocumentWriter.cs:707` guarded by `_documentId`, set unconditionally at `:101`). |
| 3 | Catalog | `document.GetCatalog()` → `PdfCatalog?`; raw dict `document.CatalogDictionary` | `PdfDocument.cs:282`, `PdfDocument.Mutation.cs:13` | `PdfCatalog.Dictionary`, typed getters: `GetAcroForm()`, `GetMetadata()` (`/Metadata` XMP stream), `GetOutlines()`, `Language`. |
| 4 | Dict/array access | `PdfDictionary.Get(string/PdfName)`, `TryGetValue`, `ContainsKey`; `PdfArray` indexer/`Count`/`foreach` | `PdfDictionary.cs:55-119`, `PdfArray.cs` | `PdfName.Value`; value-based equality so `new PdfName("X")` keys work. |
| 5 | Pages + boxes | `document.GetPages()`/`Pages`; `page.GetMediaBox()`, `page.Rotate`, `page.Dictionary` (internal), `page.GetResources()` (internal), `page.GetAnnotations()` (internal) | `PdfPage.cs:92/118/42/52/183` | **TrimBox/ArtBox/BleedBox: no accessor** — read off `page.Dictionary`, walk `/Parent` for inheritance, `PdfRectangle.FromArray` (`:459`). |
| 6 | Fonts + embedding | `resources.GetFonts()`/`GetFontObject(name)` → `PdfFont`; `font.FontType`, `font.ToUnicode` (public, non-null ⇒ `/ToUnicode` present), `font.GetDescriptor()`; descriptor `GetFontFile()/2()/3()` | `PdfResources.cs:37/70`, `PdfFont.cs:27/71/124`, `PdfFontDescriptor.cs:257-336` | **Type0**: descriptor + FontFile live on the descendant CIDFont — `Type0Font.DescendantFont`/`DescendantDescriptor`. Cheap "embedded?" = descriptor dict `ContainsKey` FontFile/2/3. |
| 7 | XMP | `catalog.GetMetadata()` → `PdfStream?`; `.GetDecodedData(document.Decryptor)`; `XmpPacket.Parse(bytes)`; `packet.Get(nsUri, localName)` | `PdfCatalog.cs:112`, `XmpPacket.cs:41/88` | **No pdfaid/pdfxid namespace constants** — pass literal URIs (`http://www.aiim.org/pdfa/ns/id/`). |
| 8 | Color spaces | `resources.GetColorSpaces()` → `PdfDictionary?`; device names literal in `ColorSpaceResolver` | `PdfResources.cs:176`, `ColorSpaceResolver.cs:38` | No "list all colorspaces used" method — walk resources + content operands. |
| 10 | Annotations | `page.GetAnnotations()` (internal `PdfArray?`); per annot `/Subtype`, `/AP` presence via dict | `PdfPage.cs:183` | `PdfAnnotationInfo` (public) has no `/AP` field — read the annot dict directly. |
| 11 | Actions / JS | Read `catalog.Dictionary.Get("Names"/"OpenAction"/"AA")` yourself; name-tree walker `DestinationRepairer.NameTreeLookup` (internal static) | `DestinationRepairer.cs:78`, `PdfNamedDestinations.cs:143` | No `/JavaScript`, `/AA` accessors. |
| 12 | Embedded files | `catalog /Names /EmbeddedFiles` name tree; reuse `NameTreeLookup` | — | No accessor. |

## Must-build (absent everywhere — confirmed by repo-wide grep)

- `/OutputIntents`, `DestOutputProfile`, `GTS_PDFA`/`GTS_PDFX` handling — **nothing** on the read
  side. ICC is used only for render-time color conversion (`PdfLibrary/Rendering/Icc/`). The
  bundled default CMYK profile (`IccResources.ReadDefaultCmykProfile`) is available for the later
  conversion phase.
- Page `/TrimBox` `/ArtBox` `/BleedBox` accessors (PDF/X-4).
- Catalog typed getters for `/OutputIntents` `/Names` `/OpenAction` `/AA` `/Version`.
- XMP `pdfaid`/`pdfxid` namespace constants.
- `/Names /JavaScript`, `/AA`, `/Names /EmbeddedFiles` accessors (reuse `NameTreeLookup`).

## Clause references

The ISO 19005 (PDF/A) and ISO 15930 (PDF/X) texts are **not** on this machine (only ISO 32000
base spec). Clause numbers in rule `Clause` fields are set from known values and cross-checked
against the veraPDF corpus (below). Human-facing `Message` text stays accurate regardless of
clause precision.

## Test corpus (veraPDF)

Cloned (shallow, `staging`) to the sibling directory **`../veraPDF-corpus`** — NOT vendored into
this repo (~74 MB). License **CC BY 4.0** (attribution required if a subset is later checked in).

- **Coverage: PDF/A + PDF/UA only — no PDF/X.** PDF/X-4 fixtures must come from the Ghent
  Workgroup separately.
- **Structure = the ISO clause catalog.** Folders are `PDF_A-2b/`, `PDF_A-2u/`, `PDF_A-3b/`, …
  each split into clause subfolders that name the exact ISO 19005 section, e.g.
  `PDF_A-2b/6.1 File structure/6.1.3 File trailer/`, `…/6.2 Graphics/6.2.3 Output intent/`,
  `…/6.2.11 Fonts/`, `…/6.6 Metadata/6.6.4 Version and conformance level identification/`.
  This tree refines the rule catalog with authoritative clause numbers.
- **Naming is machine-parseable:** `veraPDF test suite {clause-dashed}-t{NN}-{pass|fail}-{letter}.pdf`
  (e.g. `6-1-3-t01-fail-a.pdf`). Expected outcome is in the filename; each file is atomic and
  self-documents its intent in the document **outline** (not page text — `pdftotext` returns
  empty; read `/Title` in the outline). The harness walks the tree, parses clause + expected
  result from the filename, runs the preflighter for that profile, and asserts.

### Slice 1 mapping (verified against corpus files)

Both slice-1 rules live under clause **6.1.3 (File trailer)** — confirmed correct:

| Rule | Corpus test | Evidence |
|---|---|---|
| `file-id` (FileIdentifierRule) | `PDF_A-2b/6.1 File structure/6.1.3 File trailer/…6-1-3-t01-*` | `t01-fail-a` has a trailer with no `/ID`; outline says *"The file trailer dictionary not contain the ID keyword"*. |
| `encrypt` (NoEncryptionRule) | `…/6.1.3 File trailer/…6-1-3-t02-*` | `t02-fail-a` contains `/Encrypt`. |

Fixtures otherwise-valid (carry `pdfaid:part=2/conformance=B` + OutputIntent), so each isolates a
single violation — ideal oracles for the corpus-harness slice.
