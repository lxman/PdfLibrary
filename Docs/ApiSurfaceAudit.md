# API Surface Audit — Consistency & Coverage

**Date:** 2026-06-25
**Audited version:** 1.0.1 (both `Lxman.PdfLibrary` and `Lxman.PdfLibrary.Rendering.SkiaSharp`)
**Method:** five parallel subsystem inventories (creation/Builder, Editing, reading model, supporting subsystems, SkiaSharp rendering), synthesized; the four highest-impact claims were verified by direct source read.

> Status tags: **[P0]** correctness / will-bite-users · **[P1]** consistency or coverage gap worth fixing · **[P2]** polish.
> Items marked **✅ 1.0.2** are fixed in the 1.0.2 patch; see CHANGELOG.

---

## Verdict

The surface is broadly coherent and well-documented, with one genuinely excellent decision: the low-level PDF object model (`PdfObject`, `PdfDictionary`, `PdfName`, `PdfStream`, …) is **uniformly `internal`**, so consumers never touch PDF primitives. Nullability is applied consistently, `[Flags]` zero-values are correct, and XML-doc coverage is above average.

Problems cluster in three areas:

1. **The top-level facade verbs diverge** — the four core operations (create / read / edit / optimize) use four different entry-point shapes and inconsistent verbs.
2. **The public/internal boundary leaks** — a lot of plumbing is `public`, several `public` types are uninstantiable or unreachable, and the two PDF-specific exception types are `internal`.
3. **Read/write coverage is asymmetric** — annotations are add-only, encryption is set-only-at-creation, several collection facades can write but not fully read, and a few obvious overloads are missing.

The audit also surfaced **two verified correctness bugs** (rendering background; `AddText` unit bypass), both fixed in 1.0.2.

**SemVer note:** 1.0.x is already published to NuGet, so the public surface is locked. That drives the triage: correctness patches now (no API change), additive coverage in 1.x, and naming/boundary cleanups via `[Obsolete]` bridges or a 2.0 (which will coincide with the SkiaSharp 4.x renderer rewrite).

---

## 1. Consistency findings (cross-cutting)

### 1.1 — Entry-point spine uses four shapes and divergent verbs **[P1]**

| Operation | How you invoke it | Shape |
|---|---|---|
| Create (fluent) | `PdfDocumentBuilder.Create()…Save()` | static factory → fluent builder |
| Create (empty) | `PdfDocument.CreateEmpty()` / `PdfDocumentEditor.CreateBlank()` | **two names, same op** |
| Read | `PdfDocument.Load(path/stream)` | static method |
| Edit | `PdfDocumentEditor.Open(path)` then `editor.Save()` | static factory → instance + `IDisposable` |
| Optimize | `PdfOptimizer.Optimize(doc, stream, options)` | external static utility |

- `Load` (document) vs `Open` (editor) name the same act of opening a file; `CreateEmpty` (document) vs `CreateBlank` (editor) likewise. — `Editing/PdfDocumentEditor.cs:30,39`
- **Three ways to serialize**, none integrated: `PdfDocument.Save(stream)` (no options), `PdfDocumentEditor.Save(stream, PdfSaveOptions?)`, `PdfOptimizer.Optimize(doc, stream, PdfOptimizationOptions?)`. No `editor.Optimize(...)`, no `Optimize` flag on `PdfSaveOptions`. — `Optimization/PdfOptimizer.cs:12`
- `Extract` is an **instance** method but `Merge` is **static** on the same class. — `Editing/PdfDocumentEditor.cs:66,78`
- `editor.Append(src)` is a one-line passthrough to `editor.Pages.Append(src)` — duplicate surface; `AppendRange` exists only on `Pages`. — `Editing/PdfDocumentEditor.cs:64`

### 1.2 — Method/property duplication on `PdfDocument` **[P2]**

| Method | Property | Note |
|---|---|---|
| `GetPageCount()` | `PageCount` | identical bodies |
| `GetPages()` → `List<PdfPage>` | `Pages` → `IEnumerable<PdfPage>` | **different return types for the same data** |
| `GetPage(0)` | `FirstPage` | wrapper |
| `GetPage(n-1)` | `LastPage` | wrapper |

`PdfPage.GetImageCount()` allocates and fills a whole `List<PdfImage>` just to return `.Count`. — `Structure/PdfDocument.cs:320,333,395,400`; `Document/PdfPage.cs:383`

### 1.3 — Configuration verbs are a soup **[P2]**

For "set a property": `Set*` (metadata, acroform), `With*` (document builder, encryption), `Define*` (layers — the only user of `Define`), bare adjectives (`Hidden`/`Visible`/`Locked`), and `Add*` (everything else). — `Builder/PdfMetadataBuilder.cs:27`, `Builder/PdfDocumentBuilder.cs:30,126`

Four field types express "set the initial value" four ways: `PdfTextFieldBuilder.Value()`, `PdfCheckboxBuilder.Checked()`, `PdfDropdownBuilder.Select()`, `PdfRadioGroupBuilder.Select()`. And `PdfTextBuilder` ships both `Color()` and `WithColor()` as documented aliases. — `Builder/Page/PdfTextBuilder.cs:39,48`

### 1.4 — Options vs Settings: three shapes **[P2]**

| Type | Suffix | Init style | Setters | `.Default` |
|---|---|---|---|---|
| `PdfOptimizationOptions` | Options | object-initializer | public | yes |
| `PdfSaveOptions` | Options | object-initializer | public | **no** |
| `PdfEncryptionSettings` | **Settings** | fluent `With*`/`Allow*` | private | no |

— `Optimization/PdfOptimizationOptions.cs:6`, `Editing/PdfSaveOptions.cs:5`, `Builder/PdfEncryptionSettings.cs:7`

### 1.5 — Collection return discipline is inconsistent **[P1]**

Mutable `List<T>` (and a mutable array) leak out of getters, letting callers bypass builder validation:

- `PdfDocument.GetPages()` → `List<PdfPage>`; `PdfBookmark.Children`, `PdfLayerContent.Content`, `PdfHighlightAnnotation.QuadPoints`, `PdfPathContent.Segments` → mutable `List<T>`.
- `PdfColor.Components` returns the internal `double[]` from a `readonly struct` — `color.Components[0] = 9` mutates a supposedly-immutable value.
- Meanwhile form-field `Options` correctly use `IReadOnlyList<T>` and `PdfDocument.Pages` uses `IEnumerable<T>`. No consistent rule.

### 1.6 — Argument-validation style varies by vintage **[P2]**

`ArgumentNullException.ThrowIfNull` in newer code (editing, optimization, metadata, security); `x ?? throw` in fixups; `if (x==null) throw` in a few spots; and nothing at all across most builder methods (`AddText(null,…)` etc. surface as `NullReferenceException` from deep in serialization). — `Editing/PdfDocumentEditor.cs:47`, `Fixups/FixupManager.cs:27`

### 1.7 — Fluent terminals use four patterns; one builder is a dead end **[P2]**

Return-to-parent is variously `Done()` + `implicit operator` (text, path), a named side-exit property (`.Annotation`, `.Layer`, `.Range`), or lambda fall-off (annotation `configure:` delegates). **`PdfImageBuilder` has neither `Done()` nor an implicit conversion** — `AddImage(...).Opacity(.5).Done()` won't compile, and you can't fluently get back to the page. — `Builder/Page/PdfImageBuilder.cs`

### 1.8 — Naming scope **[P2]**

Public enums live in the global namespace (deliberate, but unconventional), and `ImageFormat` is the one public type with no `Pdf` prefix — collision-prone in consumer projects. — `Enums.cs`

---

## 2. Coverage & boundary findings (cross-cutting)

### 2.1 — The public/internal boundary leaks badly **[P1]**

**Public but uninstantiable / unreachable (dead surface):**
- `PdfRenderer` — `public class`, `internal` ctor; `RenderPage` can never be called externally. — `Rendering/PdfRenderer.cs:20,40`
- `PdfTextExtractor` — `public`, `internal` ctor.
- `ContourPoint`, `GlyphContour`, `GlyphMetrics`, `GlyphOutline` — `public`, but the only producer (`EmbeddedFontMetrics`) is `internal`; they ship and autocomplete but can't be obtained. — `Fonts/Embedded/*`

**Plumbing that should be `internal`:**
- Rendering package: `SkiaSharpRenderTarget` (23 public members), `GlyphToSKPathConverter`, `BlendModeConverter`, `SystemFontResolver`, `SkiaFontProvider`, `FontCategory`. Only `PdfPageExtensions` + `PageRenderBuilder` are genuinely consumer-facing.
- `RC4` and `AesCipher` (incl. raw `Sha256/384/512`) are `public` — PDF-internal crypto with no consumer use case. — `Security/RC4.cs:8`, `Security/Aescipher.cs:9`
- `PdfDocumentWriter` — a public parallel to `PdfDocumentBuilder.Save`.

**Misleading `public` on `internal` classes:** Filters, Functions, Parsing are 100% internal yet riddled with `public` members (harmless, noise). `PageRenderContext.Page` is typed `object`, forcing every `IPdfFixup` to cast to `PdfPage`. — `Fixups/PageRenderContext.cs:18`

### 2.2 — PDF-specific exceptions are `internal` **[P1]** *(verified)*

`PdfParseException` and `PdfSecurityException` are both `internal class`. — `Parsing/PdfParseException.cs:6`, `Security/PdfSecurityException.cs:6`

Consumers cannot write `catch (PdfParseException)` or distinguish a malformed-PDF failure from a wrong-password failure — everything escapes as bare `Exception`. There is no `PdfException` base.

### 2.3 — `PdfContentProcessor` advertises an extension point it can't honor **[P1]**

It's `public abstract`, but the text hooks (`OnShowText`, `OnShowTextWithPositioning`, `OnInlineImage`) are `private protected` with `internal` parameter types (`PdfString`, `PdfArray`). An external subclass can override graphics-state hooks but not the text hooks — exactly the reason most people subclass a content processor. — `Content/PdfContentProcessor.cs:374`

### 2.4 — Read/write asymmetries **[P1/P2]**

| Area | Can write | Can read/remove | Gap |
|---|---|---|---|
| Annotations (`PdfPageCollection`) | `AddNote/AddLink/AddExternalLink/AddHighlight` | — | **add-only**: no enumerate / remove / update |
| Encryption & permissions | set at creation (builder) | `PdfDocument.Permissions` (read-only) | **can't re-encrypt or change perms on a loaded doc** |
| Viewer settings | 4 keys | 4 keys | only 4 of ~14 ISO keys; `bool? = null` is **silently ignored** — `Editing/PdfViewerSettings.cs:140` |
| Outlines | `Add` (append only), `item.Remove()` | indexer | no `RemoveAt(int)` / `Insert(int,…)` — removal is two-step |
| Named destinations | `Set/Remove/Rename` | `Get(name)`, enumerate names | no `Contains`, no `this[string]`, can't enumerate `(name, dest)` pairs |
| Forms | field values | per-field | no `Count`, no add-field; `SelectedOption` **throws** on wrong `Kind` — `Editing/Forms/PdfFormField.cs:106` |
| Doc-level extraction | — | `ExtractAllText` only | no `ExtractAllTextWithFragments`; `GetAllImages()` is `internal` despite public `PdfPage.GetImages()` |

### 2.5 — Missing overloads (parity gaps) **[P1/P2]**

- `PdfDocumentEditor.Open` — path only, **no `Open(Stream)`**; a `MemoryStream` consumer must drop to `PdfDocument.Load(stream).Edit()`. — `Editing/PdfDocumentEditor.cs:30`
- `PdfOptimizer.Optimize` — `Stream` only, **no path overload** (asymmetric with `Save(string)`).
- `PdfDocumentBuilder.LoadFont` — path only, no `byte[]`/`Stream`. — `Builder/PdfDocumentBuilder.cs:323`
- Rendering doc-level: only `SavePageAs(index, file, scale)` — no bytes/image/stream at document level, no batch/range render, and it takes `scale` while the builder takes `WithDpi` (vocabulary split). — `PdfLibrary.Rendering.SkiaSharp/PdfPageExtensions.cs:35`
- `AddLine` has no `PdfLength`/`PdfRect` overload; form-field placement has no `PdfLength` overload (everything else does).

### 2.6 — `PdfOptimizer` returns `void` **[P2]**

No result/stats object: a consumer who opts into the lossy `RecompressImages` or edit-destructive `SubsetFonts` (both default-off) gets no feedback — no before/after byte counts, objects removed, images touched. — `Optimization/PdfOptimizer.cs:14`

---

## 3. Correctness issues surfaced (verified)

### 3.1 — Rendering builder ignores its own white-background contract **[P0] — ✅ 1.0.2**

`PageRenderBuilder.ToImage()` returned `SkiaSharpRenderTarget.GetImage()`, a raw transparent `_surface.Snapshot()`. The white composite existed **only** in `SkiaSharpRenderTarget.SaveToFile()`, which the builder never calls. So `ToFile`/`ToStream`/`ToBytes`/`ToImage` **and** `doc.SavePageAs(...)` all emitted transparent backgrounds, contradicting the documented "pages render with a white background," and `.ToFile("x.jpg")` (no alpha) composited to black. `WithTransparentBackground()` was observably a no-op.

**Root cause:** `GetImage()` ignored `_transparentOutput`, the very flag `SaveToFile()` branches on. **Fix:** `GetImage()` now composites onto white when `!_transparentOutput` (masks set `transparentBackground: true`, so they remain transparent). One change fixes all four builder outputs and `SavePageAs`. — `PageRenderBuilder.cs:83`, `SkiaSharpRenderTarget.cs:416`

### 3.2 — `AddText` 5-arg overload bypasses unit conversion **[P0] — ✅ 1.0.2**

`AddText(text, x, y, fontName, fontSize)` wrote `X = x, Y = y` raw, with no `ConvertToPoints()`, while the 3-arg overload converts. Under `.WithInches()`/`.WithUnit(...)` the 5-arg form silently placed text at point coordinates (off by 72×). **Fix:** the 5-arg overload now runs the same `ConvertToPoints` conversion as its 3-arg sibling. — `Builder/Page/PdfPageBuilder.cs:176`

### 3.3 — Lower-severity correctness/polish

- `PdfColor.Components` mutable array on a `readonly struct` (§1.5). **[P2]**
- `PdfColor.Gray()` makes a DeviceRGB gray while `FromGray()` makes DeviceGray — different operators, "legacy" but not `[Obsolete]`. **[P2]**
- Nullable `SKData` from `image.Encode()` dereferenced without a guard in all three builder output paths → opaque `NullReferenceException` on encode failure. — `PageRenderBuilder.cs:106,121,134` **[P2]** *(deferred — see note)*

---

## 4. Documentation gaps

XML docs are broadly good, but: **`PdfPageCollection` — the highest-traffic editing type — has zero member-level docs** (only a class summary), across all three partials. The public content-element classes (`PdfTextContent`, `PdfPathContent`, …) have no property docs, and several enums (`PdfFormFieldType`, `ButtonKind`, `PdfPageMode`, `PdfPageLayout`) have no per-value docs.

---

## 5. Prioritized punch list (SemVer-aware)

**Patch now — 1.0.2 (no API change):**
1. ✅ Composite white in the render output path (§3.1).
2. ✅ Run `ConvertToPoints` in the 5-arg `AddText` (§3.2).
3. `SKData` encode null-guard (§3.3) — **deferred**: defensive only, not user-visible, and lives in the SkiaSharp package slated for the 4.x rewrite.

**Additive in 1.x (non-breaking):**
4. ✅ Promote `PdfParseException`/`PdfSecurityException` to `public` under a new `public abstract PdfException` base (§2.2).
5. Add overloads: `PdfDocumentEditor.Open(Stream)`, `PdfOptimizer.Optimize(…, string path)`, `LoadFont(byte[]/Stream)`, doc-level render-to-bytes/stream + range, `PdfLength`/`PdfRect` for `AddLine` & form placement (§2.5).
6. Return a stats object from `Optimize` (§2.6); add `PdfSaveOptions.Default`.
7. Close read/remove gaps: annotation enumerate/remove, `Outlines.RemoveAt/Insert`, `NamedDestinations.Contains/this[string]`/pair enumeration, `Forms.Count`, remaining viewer-pref keys, fix the `bool? = null` no-op (§2.4).
8. Add `Done()`/implicit operator to `PdfImageBuilder` (§1.7).
9. Backfill `PdfPageCollection` and content-element XML docs (§4).

**Breaking — `[Obsolete]` bridge now, remove at 2.0 (coincides with the SkiaSharp 4.x renderer rewrite):**
10. Unify entry verbs (`Load`/`CreateEmpty` vs `Open`/`CreateBlank`); make `Extract`/`Merge` both instance; drop duplicate `editor.Append` (§1.1).
11. Demote plumbing to `internal`: render targets/converters/font resolver, `PdfRenderer`, `PdfTextExtractor`, `PdfDocumentWriter`, `RC4`/`AesCipher`, stranded glyph types (§2.1).
12. Fix `PdfColor.Components` type; namespace the enums and `Pdf`-prefix `ImageFormat` (§1.8, §3.3).
13. Resolve `PdfContentProcessor` — make the text hooks `protected` with public parameter types, or drop `public` from the class (§2.3).
14. Converge the options style (suffix + init paradigm) (§1.4).
