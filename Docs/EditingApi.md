# PDF Editing API

This document covers the **editing / mutation** API — loading an existing PDF, changing it, and saving the result. It is the middle leg of the library's *load → edit → optimize* story. Creating PDFs from scratch is documented separately in [FluentApi.md](FluentApi.md); shrinking them is in the [Optimization](#see-also) section of the README.

## Table of Contents

- [Overview](#overview)
- [Entering edit mode](#entering-edit-mode)
- [Page operations](#page-operations)
- [Merging, splitting, importing](#merging-splitting-importing)
- [Stamping & overlays](#stamping--overlays)
- [Annotations](#annotations)
- [Document metadata](#document-metadata)
- [Outlines (bookmarks)](#outlines-bookmarks)
- [Page labels](#page-labels)
- [Named destinations](#named-destinations)
- [Viewer preferences](#viewer-preferences)
- [Form filling](#form-filling)
- [Saving](#saving)
- [Encrypted input](#encrypted-input)
- [Error handling](#error-handling)
- [Text & Unicode](#text--unicode)
- [API reference](#api-reference)
- [Limitations](#limitations)

---

## Overview

`PdfDocument.Load(...)` gives you a read-oriented document. Calling `.Edit()` on it returns a `PdfDocumentEditor` — the mutation facade:

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Editing;

using var doc = PdfDocument.Load("in.pdf");
var edit = doc.Edit();

edit.Pages.RemoveAt(2);          // delete the 3rd page
edit.Pages.Rotate(0, 90);        // rotate the 1st page 90°
edit.Pages.Move(4, 0);           // move the 5th page to the front

edit.Save("out.pdf");
```

`.Edit()` does three things before handing you the editor:

1. **Materializes** the whole object graph into memory.
2. If the document is encrypted, **decrypts it in place** — the saved output is unencrypted (see [Encrypted input](#encrypted-input)).
3. **Flattens** the page tree to a single level and pushes inherited page attributes (Resources, MediaBox, CropBox, Rotate) down onto each page, so page operations are simple and predictable.

`Save` is a **full rewrite**: the entire in-memory document is repacked. Deleted pages and any objects only they referenced are dropped automatically.

## Entering edit mode

| Call | Description |
|------|-------------|
| `doc.Edit()` | Enter edit mode on an already-loaded `PdfDocument`. You own `doc` — dispose it. |
| `PdfDocumentEditor.Open(path, password?)` | Load a file and enter edit mode in one step. The editor owns the document — dispose the editor. |
| `PdfDocumentEditor.Open(stream, password?, leaveOpen?)` | Same, from a stream (an in-memory or network PDF). The editor owns the document; `leaveOpen` (default `false`) controls whether disposing it also disposes the stream. |
| `PdfDocumentEditor.CreateBlank()` | Start a new, empty (zero-page) editable document — a target you build up with `InsertBlank` / `Import`. |

`PdfDocumentEditor` is `IDisposable`. It disposes the underlying document **only when it created it** (`Open` / `CreateBlank`); for `doc.Edit()` you remain the owner.

## Page operations

`edit.Pages` is a live, ordered view of the document's pages (`IReadOnlyList<PdfPage>`) plus mutators. All indices are 0-based.

```csharp
int count   = edit.Pages.Count;
PdfPage p   = edit.Pages[0];               // inspect size, extract text, render…
foreach (PdfPage page in edit.Pages) { /* ... */ }
```

| Method | Description |
|--------|-------------|
| `Rotate(index, degrees)` | Set absolute rotation (multiple of 90). |
| `RotateBy(index, delta)` | Rotate relative to the current angle (multiple of 90). |
| `Move(fromIndex, toIndex)` | Reorder a page. |
| `RemoveAt(index)` | Delete a page. Throws if it's the last remaining page. Also strips bookmarks, named destinations, and link annotations that pointed at the deleted page. |
| `InsertBlank(at, width, height)` | Insert a new empty page (size in points) and return it. |
| `Import(source, sourceIndex, at)` | Deep-copy a page from another loaded document into this one at `at`; returns the imported page. |
| `Duplicate(index, at)` | Copy an existing page within this document. |
| `Append(source)` | Append every page of another document. |
| `AppendRange(source, start, count)` | Append a slice of another document. |

```csharp
using var src = PdfDocument.Load("cover.pdf");
edit.Pages.Import(src, sourceIndex: 0, at: 0);        // put cover's first page at the front
edit.Pages.InsertBlank(edit.Pages.Count, 612, 792);   // append a US-Letter blank page
```

### Deleting and navigation

Reorder and insert keep page objects intact, so bookmarks and links that target them stay valid. **Delete** is the only operation that can orphan a destination — so when you `RemoveAt`, the editor automatically removes outline (bookmark) entries, named destinations, and link annotations that resolved to the deleted page. Bookmarks with surviving children have those children promoted up a level rather than discarded.

### Importing pages with form fields

`Import` / `Append` / `Merge` bring a page's interactive form fields across as working fields — they're registered in the target's AcroForm. If a field name collides with one already in the target, the imported field is renamed (`name` → `name#2`) so values don't cross-contaminate.

## Merging, splitting, importing

```csharp
using PdfLibrary.Editing;

// Merge several documents into one new document
using var a = PdfDocument.Load("part1.pdf");
using var b = PdfDocument.Load("part2.pdf");
using PdfDocument merged = PdfDocumentEditor.Merge([a, b]);
merged.Save("combined.pdf");

// Split: extract a page range into a new document
using var doc = PdfDocument.Load("report.pdf");
var edit = doc.Edit();
using PdfDocument chapter = edit.Extract(start: 4, count: 6);   // pages 5–10
chapter.Save("chapter.pdf");
```

`Merge` and `Extract` return a **new** `PdfDocument`; call `Save` on it.

## Stamping & overlays

`edit.Pages.Stamp`, `StampRange`, and `StampAll` paint a reusable XObject over (or under) one or more pages. The stamp is authored with the same fluent builder used to create pages from scratch.

```csharp
// Diagonal "DRAFT" watermark across every page, 50% transparent, behind existing content
edit.Pages.StampAll(s => s
    .Watermark("DRAFT")
    .Diagonal()
    .Opacity(0.5)
    .Underlay());

// Custom stamp in the top-right corner of page 0
edit.Pages.Stamp(0, s => s
    .Content(p => p.AddText("CONFIDENTIAL", 10, 10, "Helvetica", 9))
    .Size(100, 20)
    .TopRight()
    .Overlay());
```

| Method | Description |
|--------|-------------|
| `Stamp(index, configure)` | Stamp a single page (0-based). |
| `StampRange(start, count, configure)` | Stamp a contiguous range of pages. |
| `StampAll(configure)` | Stamp every page in the document. |

### `PdfStampBuilder` — placement and appearance

| Method | Description |
|--------|-------------|
| `Content(Action<PdfPageBuilder>)` | Author the stamp using the fluent page builder. Required unless `Watermark` or `Image` is used. |
| `Watermark(string text)` | Sugar: bold 48 pt Helvetica-Bold text stamp sized to the text. Pair with a placement preset. |
| `Image(byte[] data, double width, double height)` | Sugar: image stamp filling the given bounding box. |
| `Size(double width, double height)` | Override the bounding box in points (default: full page). |
| `Center()` | Place stamp at page centre. |
| `TopLeft()` | Place stamp at top-left corner. |
| `TopRight()` | Place stamp at top-right corner. |
| `BottomLeft()` | Place stamp at bottom-left corner. |
| `BottomRight()` | Place stamp at bottom-right corner. |
| `At(double x, double y)` | Place stamp at explicit coordinates (points, origin bottom-left). |
| `Diagonal()` | Rotate stamp diagonally across the page. |
| `Tiled(double spacing)` | Repeat stamp in a grid with the given spacing. Throws `ArgumentOutOfRangeException` if `spacing <= 0`. |
| `Scale(double factor)` | Scale factor applied after placement (default `1.0`). |
| `Rotate(double degrees)` | Additional rotation in degrees applied to the stamp. |
| `Opacity(double alpha)` | Opacity 0.0–1.0 (clamped; default `1.0`). Values below 1.0 add a graphics-state resource. |
| `Overlay()` | Paint on top of existing page content (default). |
| `Underlay()` | Paint behind existing page content. |

## Annotations

All annotation methods are on `edit.Pages`. Coordinates are in PDF points with the origin at the bottom-left corner of the page. Use `PdfRect` to specify rectangles.

```csharp
using PdfLibrary.Builder;        // PdfRect
using PdfLibrary.Builder.Page;   // PdfColor

// Sticky-note annotation at a point
edit.Pages.AddNote(0, x: 72, y: 720, "Review this paragraph.");

// Internal link: clicking rect on page 0 navigates to page 5
edit.Pages.AddLink(0, new PdfRect(72, 600, 250, 620), targetPageIndex: 5);

// External hyperlink
edit.Pages.AddExternalLink(0, new PdfRect(72, 560, 300, 580), "https://example.com");

// Yellow highlight (default colour)
edit.Pages.AddHighlight(0, new PdfRect(72, 700, 400, 715));

// Custom colour highlight
edit.Pages.AddHighlight(0, new PdfRect(72, 680, 400, 695), PdfColor.Cyan);
```

| Method | Description |
|--------|-------------|
| `AddNote(int index, double x, double y, string contents)` | Add a text (sticky-note) annotation at `(x, y)` on page `index`. |
| `AddLink(int index, PdfRect rect, int targetPageIndex)` | Add an internal link annotation on page `index` navigating to `targetPageIndex`. |
| `AddExternalLink(int index, PdfRect rect, string url)` | Add a URI action link. |
| `AddHighlight(int index, PdfRect rect, PdfColor? color = null)` | Add a highlight annotation; defaults to `PdfColor.Yellow`. |

`PdfRect` is constructed with bottom-left origin coordinates: `new PdfRect(left, bottom, right, top)`. Convenience factories `PdfRect.FromInches(...)`, `PdfRect.FromMillimeters(...)`, and `PdfRect.FromPoints(x, y, width, height)` are also available.

### Reading and removing annotations

`GetAnnotations` returns a read-only snapshot of a page's annotations; `RemoveAnnotationAt` deletes one by index.

```csharp
IReadOnlyList<PdfAnnotationInfo> annots = edit.Pages.GetAnnotations(0);
foreach (PdfAnnotationInfo a in annots)
    Console.WriteLine($"{a.Subtype} @ {a.Rect} — {a.Contents}");

edit.Pages.RemoveAnnotationAt(0, 0);   // remove the first annotation on page 0
```

| Method | Description |
|--------|-------------|
| `IReadOnlyList<PdfAnnotationInfo> GetAnnotations(int index)` | Snapshot of the annotations on page `index` (empty if none). |
| `void RemoveAnnotationAt(int index, int annotationIndex)` | Remove the annotation at `annotationIndex` (order matches `GetAnnotations`). Throws `ArgumentOutOfRangeException` if out of range. |

`PdfAnnotationInfo` exposes `string Subtype` (e.g. `"Text"`, `"Link"`, `"Highlight"`), `PdfRect Rect`, and `string? Contents`.

## Document metadata

`edit.Metadata` exposes the document's Info dictionary with automatic XMP sync. Setting any property writes to both the Info dict (`/Info` object) and the `/Catalog /Metadata` XMP packet.

```csharp
edit.Metadata.Title    = "Q3 Report";
edit.Metadata.Author   = "Finance Team";
edit.Metadata.Keywords = "quarterly, finance, 2024";
edit.Metadata.Creator  = "ReportGen 2.0";
edit.Metadata.ModificationDate = DateTimeOffset.UtcNow;
```

| Property | Info key | XMP mapping |
|----------|----------|-------------|
| `string? Title` | `/Title` | `dc:title` (LangAlt) |
| `string? Author` | `/Author` | `dc:creator` (Seq) |
| `string? Subject` | `/Subject` | `dc:description` (LangAlt) |
| `string? Keywords` | `/Keywords` | `pdf:Keywords` + `dc:subject` (Bag) |
| `string? Creator` | `/Creator` | `xmp:CreatorTool` |
| `string? Producer` | `/Producer` | `pdf:Producer` |
| `DateTimeOffset? CreationDate` | `/CreationDate` | `xmp:CreateDate` |
| `DateTimeOffset? ModificationDate` | `/ModDate` | `xmp:ModifyDate` |
| `XmpPacket Xmp` | — | Full XMP packet (lazily loaded; modify directly for non-standard schemas) |

## Outlines (bookmarks)

`edit.Outlines` is a mutable tree of `PdfOutlineItem` objects. Changes are reflected immediately in the in-memory document and are persisted on `Save`.

```csharp
// Build an outline from scratch
edit.Outlines.Clear();

var ch1 = edit.Outlines.Add("Chapter 1", PdfDestination.FitPage(0));
ch1.Add("Section 1.1", PdfDestination.At(1, left: 72, top: 700, zoom: null));
ch1.Add("Section 1.2", PdfDestination.FitWidth(2, top: 792));

var ch2 = edit.Outlines.Add("Chapter 2", PdfDestination.ToPage(5), children: item =>
{
    item.Add("Section 2.1", PdfDestination.FitPage(5));
});
ch2.IsOpen = false;   // collapsed in viewer

// Move an item
ch1.Children[0].MoveTo(newParent: ch2, index: 0);
```

### `PdfOutlineCollection`

| Member | Description |
|--------|-------------|
| `int Count` | Number of top-level items. |
| `PdfOutlineItem this[int index]` | Access a top-level item by index. |
| `PdfOutlineItem Add(string title, int page)` | Add a top-level item pointing to `page` (whole-page destination). |
| `PdfOutlineItem Add(string title, PdfDestination destination, Action<PdfOutlineItem>? children = null)` | Add a top-level item with an explicit destination and optional inline child setup. |
| `PdfOutlineItem Insert(int index, string title, int page)` | Insert a top-level item at `index` (page destination). |
| `PdfOutlineItem Insert(int index, string title, PdfDestination destination, Action<PdfOutlineItem>? children = null)` | Insert a top-level item at `index` with an explicit destination and optional children. |
| `void RemoveAt(int index)` | Remove the top-level item at `index` (and its subtree). |
| `void Clear()` | Remove the entire outline tree and the `/Outlines` catalog reference. |

### `PdfOutlineItem`

| Member | Description |
|--------|-------------|
| `string Title` | Bookmark label. Non-ASCII text is stored as UTF-16BE. |
| `PdfDestination? Destination` | Navigation target; `null` if unresolvable. |
| `bool IsOpen` | Whether the item is expanded in the viewer (default `true`). |
| `IReadOnlyList<PdfOutlineItem> Children` | Child items (read-only view; mutate via `Add`/`Remove`). |
| `PdfOutlineItem Add(string title, int page)` | Add a child pointing to `page`. |
| `PdfOutlineItem Add(string title, PdfDestination destination, Action<PdfOutlineItem>? children = null)` | Add a child with an explicit destination. |
| `void Remove()` | Remove this item and its entire subtree. |
| `void MoveTo(PdfOutlineItem? newParent, int index)` | Re-parent this item. Pass `null` for `newParent` to promote to top level. |

### `PdfDestination` factories

| Factory | PDF operator | Description |
|---------|--------------|-------------|
| `ToPage(int pageIndex)` | `/XYZ null null null` | Navigate to page, preserving scroll position and zoom. |
| `FitPage(int pageIndex)` | `/Fit` | Fit the entire page in the viewer. |
| `FitWidth(int pageIndex, double? top)` | `/FitH top` | Fit page width; scroll to `top`. |
| `FitHeight(int pageIndex, double? left)` | `/FitV left` | Fit page height; scroll to `left`. |
| `At(int pageIndex, double? left, double? top, double? zoom)` | `/XYZ left top zoom` | Explicit position and zoom (`null` = preserve current; `0` zoom = fit). |
| `FitRect(int pageIndex, double left, double bottom, double right, double top)` | `/FitR` | Fit the specified rectangle in the viewer. |

## Page labels

`edit.PageLabels` controls the viewer's page-number display. Labels are defined as ranges: each range specifies a starting page, a numbering style, an optional prefix, and a starting number.

```csharp
// Roman numerals for the front matter (pages 0–2), then Arabic from page 3
edit.PageLabels.Set(startIndex: 0, PdfPageLabelStyle.LowercaseRoman);
edit.PageLabels.Set(startIndex: 3, PdfPageLabelStyle.Decimal, prefix: null, start: 1);

// Prefixed section
edit.PageLabels.Set(startIndex: 10, PdfPageLabelStyle.Decimal, prefix: "A-", start: 1);

// Inspect ranges
foreach (PdfPageLabelRange r in edit.PageLabels.Ranges)
    Console.WriteLine($"{r.StartPageIndex}: {r.Style} '{r.Prefix}' from {r.StartNumber}");
```

| Method | Description |
|--------|-------------|
| `void Set(int startIndex, PdfPageLabelStyle style, string? prefix = null, int start = 1)` | Define or replace the labeling range starting at `startIndex`. |
| `PdfPageLabelRange? Get(int index)` | Return the range covering `index` (latest range starting at or before it), or `null`. |
| `bool Remove(int startIndex)` | Remove the range starting exactly at `startIndex`. Returns `false` if none exists. |
| `void Clear()` | Remove the entire `/PageLabels` tree. |
| `IReadOnlyList<PdfPageLabelRange> Ranges` | All ranges, sorted by start page index. |

### `PdfPageLabelStyle`

| Value | Display |
|-------|---------|
| `None` | No numeric portion — prefix only. |
| `Decimal` | 1, 2, 3, … |
| `UppercaseRoman` | I, II, III, IV, … |
| `LowercaseRoman` | i, ii, iii, iv, … |
| `UppercaseLetters` | A, B, … Z, AA, AB, … |
| `LowercaseLetters` | a, b, … z, aa, ab, … |

**Contract:** page operations (`RemoveAt`, `Move`, `InsertBlank`, etc.) do **not** automatically adjust label ranges. After rearranging pages, call `PageLabels.Set` / `Remove` to renumber. This is intentional — the editor cannot know which logical numbering scheme the caller intends.

## Named destinations

`edit.NamedDestinations` is an `IReadOnlyCollection<string>` of destination names. Under the hood it writes to the modern `/Names /Dests` name tree, but also patches legacy `/Dests` dictionaries when updating an existing named destination that lives there.

```csharp
// Create a named destination
edit.NamedDestinations.Set("cover", PdfDestination.FitPage(0));
edit.NamedDestinations.Set("toc",   PdfDestination.FitWidth(1, top: 792));

// Rename
edit.NamedDestinations.Rename("toc", "table-of-contents");

// Resolve
PdfDestination? dest = edit.NamedDestinations.Get("cover");

// Remove
edit.NamedDestinations.Remove("cover");
```

| Member | Description |
|--------|-------------|
| `int Count` | Total number of named destinations. |
| `void Set(string name, PdfDestination destination)` | Create or update a named destination. |
| `PdfDestination? Get(string name)` | Return the destination for `name`, or `null` if not found. |
| `PdfDestination? this[string name]` | Indexer sugar for `Get`. |
| `IEnumerable<KeyValuePair<string, PdfDestination>> Entries()` | Enumerate `(name, destination)` pairs. |
| `bool Rename(string oldName, string newName)` | Rename a destination. Returns `false` if `oldName` not found. |
| `bool Remove(string name)` | Delete a named destination. Returns `false` if not found. |

## Viewer preferences

`edit.ViewerSettings` controls how the viewer presents the document on open.

```csharp
edit.ViewerSettings.PageMode    = PdfPageMode.UseOutlines;   // show bookmarks panel
edit.ViewerSettings.PageLayout  = PdfPageLayout.TwoPageLeft;
edit.ViewerSettings.OpenAction  = PdfDestination.FitPage(0); // go to page 1 on open
edit.ViewerSettings.HideToolbar = true;
edit.ViewerSettings.HideMenubar = true;
edit.ViewerSettings.FitWindow   = true;
edit.ViewerSettings.DisplayDocTitle = true;
edit.ViewerSettings.Direction   = PdfReadingDirection.LeftToRight;
edit.ViewerSettings.Duplex      = PdfDuplex.DuplexFlipLongEdge;   // print preference
```

| Property | PDF key | Description |
|----------|---------|-------------|
| `PdfPageMode? PageMode` | `/Catalog /PageMode` | Panel shown when the document opens. |
| `PdfPageLayout? PageLayout` | `/Catalog /PageLayout` | Page arrangement in the viewer. |
| `PdfDestination? OpenAction` | `/Catalog /OpenAction` | Destination navigated to on open (destination-array form). |
| `bool? HideToolbar` | `/ViewerPreferences /HideToolbar` | Hide the viewer toolbar. |
| `bool? FitWindow` | `/ViewerPreferences /FitWindow` | Resize the viewer window to fit the first page. |
| `bool? CenterWindow` | `/ViewerPreferences /CenterWindow` | Centre the viewer window on screen. |
| `bool? DisplayDocTitle` | `/ViewerPreferences /DisplayDocTitle` | Show the document title (from metadata) in the title bar instead of the filename. |
| `bool? HideMenubar` | `/ViewerPreferences /HideMenubar` | Hide the viewer's menu bar. |
| `bool? HideWindowUI` | `/ViewerPreferences /HideWindowUI` | Hide scrollbars and navigation controls, showing page content only. |
| `PdfPageMode? NonFullScreenPageMode` | `/ViewerPreferences /NonFullScreenPageMode` | Panel shown when leaving full-screen (only `UseNone`/`UseOutlines`/`UseThumbs`/`UseOC` are valid). |
| `PdfReadingDirection? Direction` | `/ViewerPreferences /Direction` | Predominant reading order for spreads. |
| `PdfPrintScaling? PrintScaling` | `/ViewerPreferences /PrintScaling` | Default page-scaling option in the print dialog. |
| `PdfDuplex? Duplex` | `/ViewerPreferences /Duplex` | Default duplex (two-sided) printing mode. |

Setting any of these to `null` removes the corresponding key.

### `PdfPageMode`

`UseNone` · `UseOutlines` · `UseThumbs` · `FullScreen` · `UseOC` · `UseAttachments`

### `PdfPageLayout`

`SinglePage` · `OneColumn` · `TwoColumnLeft` · `TwoColumnRight` · `TwoPageLeft` · `TwoPageRight`

### `PdfReadingDirection`

`LeftToRight` (`/L2R`) · `RightToLeft` (`/R2L`)

### `PdfPrintScaling`

`AppDefault` · `None`

### `PdfDuplex`

`Simplex` · `DuplexFlipShortEdge` · `DuplexFlipLongEdge`

## Form filling

`edit.Forms` lets you enumerate, read, and fill AcroForm fields. Fields are identified by their fully-qualified name (ancestor partial names joined by `.`).

```csharp
// Enumerate all fields
foreach (PdfFormField field in edit.Forms)
    Console.WriteLine($"{field.FullName} ({field.Type})");

// Fill a text field
if (edit.Forms["address.street"] is PdfTextField txt)
    txt.Value = "123 Main St";

// Check a checkbox
if (edit.Forms["agree"] is PdfButtonField cb && cb.Kind == ButtonKind.Checkbox)
    cb.Check();

// Select a radio button
if (edit.Forms["payment"] is PdfButtonField radio && radio.Kind == ButtonKind.Radio)
    radio.SelectedOption = "creditcard";

// Select list items
if (edit.Forms["country"] is PdfChoiceField choice)
    choice.SelectedValues = ["US"];

// Flatten everything into static content
edit.Forms.Flatten();

// Flatten one field only
edit.Forms.Flatten("address.street");
```

Setting a value on `PdfTextField`, `PdfButtonField`, or `PdfChoiceField` regenerates the field's appearance stream immediately and marks the widget annotation printable.

### `PdfFormFields`

| Member | Description |
|--------|-------------|
| `int Count` | Number of fields (`PdfFormFields` is `IReadOnlyCollection<PdfFormField>`). |
| `PdfFormField? this[string fullName]` | Return field by fully-qualified name, or `null`. |
| `bool TryGet(string fullName, out PdfFormField? field)` | Try-get by name. |
| `void Flatten()` | Bake all field appearances into page content and remove `/AcroForm`. |
| `void Flatten(string fullName)` | Bake one named field. Throws `KeyNotFoundException` if not found. Removes `/AcroForm` when the last field is flattened. |

### `PdfFormField` (base)

| Member | Description |
|--------|-------------|
| `string FullName` | Fully-qualified field name. |
| `string PartialName` | The field's own `/T` value. |
| `PdfFormFieldType Type` | `Text`, `Checkbox`, `Radio`, `PushButton`, `ComboBox`, `ListBox`, `Signature`, or `Unknown`. |
| `bool IsReadOnly` | Set via `/Ff` bit 1. |
| `bool IsRequired` | Set via `/Ff` bit 2. |

### `PdfTextField`

| Member | Description |
|--------|-------------|
| `string? Value` | Current text value. Setting regenerates the appearance stream. |
| `int? MaxLength` | Maximum character count from `/MaxLen`, or `null`. |
| `bool IsMultiline` | `/Ff` bit 13. |
| `bool IsComb` | `/Ff` bit 25 — comb layout (requires `MaxLength`). |
| `bool IsPassword` | `/Ff` bit 14. |
| `int Quadding` | Alignment: `0` = left, `1` = centre, `2` = right. |

### `PdfButtonField`

| Member | Description |
|--------|-------------|
| `ButtonKind Kind` | `Checkbox`, `Radio`, or `Push`. |
| `bool IsChecked` | Checkbox state (`true` when `/V` or `/AS` is not `/Off`). |
| `IReadOnlyList<string> Options` | Radio button: union of non-Off on-state names across all widgets. |
| `string? SelectedOption` | Radio button: current `/V`. Throws `InvalidOperationException` on checkbox or push button. |
| `void Check()` | Check the checkbox (and regenerate appearance if absent). Throws for non-checkbox kinds. |
| `void Uncheck()` | Uncheck the checkbox. Throws for non-checkbox kinds. |

### `PdfChoiceField`

| Member | Description |
|--------|-------------|
| `IReadOnlyList<(string Export, string Display)> Options` | All available options as export/display pairs. |
| `bool IsCombo` | `true` for a combo box, `false` for a list box. |
| `bool IsMultiSelect` | Whether multiple selections are permitted. |
| `IReadOnlyList<string> SelectedValues` | Selected export values. Setting regenerates appearance, updates `/V` and `/I`. |
| `IReadOnlyList<int> SelectedIndices` | Selected 0-based indices. Setting regenerates appearance, updates `/I` and `/V`. |

### `PdfSignatureField`

| Member | Description |
|--------|-------------|
| `bool IsSigned` | `true` when a `/V` signature dictionary is present. Signing is not part of this API — `IsSigned` is read-only. |

## Saving

```csharp
edit.Save("out.pdf");                                  // to a file
edit.Save(stream);                                     // to a stream
edit.Save("out.pdf", new PdfSaveOptions
{
    RemoveOrphans    = true,   // drop unreachable objects (default true)
    UseObjectStreams = true,   // pack with object streams for smaller output (default false)
});
```

| `PdfSaveOptions` | Default | Description |
|------------------|---------|-------------|
| `RemoveOrphans` | `true` | Garbage-collect objects no longer reachable (e.g. deleted pages). |
| `UseObjectStreams` | `false` | Pack the output using object streams + a cross-reference stream (PDF 1.5+) — smaller files. |

`PdfSaveOptions.Default` returns a fresh instance with these defaults.

## Encrypted input

You can edit an encrypted PDF — pass the password to `Load`. On `.Edit()` the document is decrypted in place, and **the saved output is unencrypted**:

```csharp
using var doc = PdfDocument.Load("protected.pdf", "password");
var edit = doc.Edit();           // decrypted here
edit.Pages.RemoveAt(0);
edit.Save("plain.pdf");          // unencrypted
```

Re-encrypting on save is not yet supported. (The creation builder *can* produce encrypted PDFs — see [FluentApi.md](FluentApi.md#encryption).)

## Error handling

Loading or parsing a malformed PDF throws `PdfParseException`; a wrong or missing password on an encrypted file throws `PdfSecurityException`. Both derive from the public base `PdfException` (in the `PdfLibrary` namespace), so you can catch all PDF-specific failures with one handler:

```csharp
using PdfLibrary;            // PdfException
using PdfLibrary.Security;   // PdfSecurityException
using PdfLibrary.Parsing;    // PdfParseException

try
{
    using var doc = PdfDocument.Load("maybe-broken.pdf", password);
    var edit = doc.Edit();
    // ...
}
catch (PdfSecurityException) { /* wrong/missing password */ }
catch (PdfException ex)      { /* malformed or otherwise invalid PDF */ }
```

## Text & Unicode

All text-valued properties in the editing API (`Metadata.Title`, `Metadata.Author`, outline `Title`, form field `Value`, annotation `contents`, etc.) accept and round-trip arbitrary Unicode. Internally, text strings use PDFDocEncoding when the content is representable in that encoding, and UTF-16BE (with a `FE FF` BOM, stored as a hex string) otherwise. You neither choose nor observe the encoding — set and get plain C# `string`.

> **Form field appearance note:** When the library regenerates a field's appearance stream it uses the standard-14 fonts. Non-Latin glyphs in the generated visual appearance may not render unless the field's `/DR` resource dictionary contains an embedded font with the required glyph coverage. The **value** (and round-trip fidelity) is not affected — only the rendered appearance.

## API reference

### `PdfDocument`

| Member | Description |
|--------|-------------|
| `PdfDocumentEditor Edit()` | Enter edit mode and return the editor. |

### `PdfDocumentEditor : IDisposable`

| Member | Description |
|--------|-------------|
| `static PdfDocumentEditor Open(string path, string? password = null)` | Load + edit. Editor owns the document. |
| `static PdfDocumentEditor Open(Stream stream, string? password = null, bool leaveOpen = false)` | Load + edit from a stream. Editor owns the document. |
| `static PdfDocumentEditor CreateBlank()` | New empty editable document. |
| `static PdfDocument Merge(IEnumerable<PdfDocument> sources)` | Combine documents into a new one. |
| `PdfPageCollection Pages` | Page view + operations. |
| `PdfMetadata Metadata` | Document Info dict + XMP metadata. |
| `PdfOutlineCollection Outlines` | Bookmark tree. |
| `PdfPageLabels PageLabels` | Page label ranges. |
| `PdfNamedDestinations NamedDestinations` | Named destination table. |
| `PdfViewerSettings ViewerSettings` | Viewer preference flags and open action. |
| `PdfFormFields Forms` | AcroForm field access and flattening. |
| `void Append(PdfDocument source)` | Append all of `source`'s pages. |
| `PdfDocument Extract(int start, int count)` | Copy a page range into a new document. |
| `void Save(string path, PdfSaveOptions? = null)` | Write to a file. |
| `void Save(Stream stream, PdfSaveOptions? = null)` | Write to a stream. |

### `PdfPageCollection : IReadOnlyList<PdfPage>`

| Member | Description |
|--------|-------------|
| `int Count` / `this[int index]` | Live page view. |
| `void Rotate(int index, int degrees)` | Absolute rotation (×90). |
| `void RotateBy(int index, int delta)` | Relative rotation (×90). |
| `void Move(int fromIndex, int toIndex)` | Reorder. |
| `void RemoveAt(int index)` | Delete (with navigation cleanup). |
| `PdfPage InsertBlank(int at, double width, double height)` | Insert a blank page. |
| `PdfPage Import(PdfDocument source, int sourceIndex, int at)` | Cross-document page copy. |
| `PdfPage Duplicate(int index, int at)` | In-document page copy. |
| `void Append(PdfDocument source)` | Append all source pages. |
| `void AppendRange(PdfDocument source, int start, int count)` | Append a slice. |
| `void Stamp(int index, Action<PdfStampBuilder> configure)` | Stamp a single page. |
| `void StampRange(int start, int count, Action<PdfStampBuilder> configure)` | Stamp a range of pages. |
| `void StampAll(Action<PdfStampBuilder> configure)` | Stamp every page. |
| `void AddNote(int index, double x, double y, string contents)` | Add a text annotation. |
| `void AddLink(int index, PdfRect rect, int targetPageIndex)` | Add an internal link annotation. |
| `void AddExternalLink(int index, PdfRect rect, string url)` | Add a URI link annotation. |
| `void AddHighlight(int index, PdfRect rect, PdfColor? color = null)` | Add a highlight annotation (default yellow). |
| `IReadOnlyList<PdfAnnotationInfo> GetAnnotations(int index)` | Read-only snapshot of a page's annotations. |
| `void RemoveAnnotationAt(int index, int annotationIndex)` | Remove an annotation by index. |

### `PdfMetadata`

| Member | Description |
|--------|-------------|
| `string? Title` | Document title. |
| `string? Author` | Author name. |
| `string? Subject` | Subject. |
| `string? Keywords` | Keywords (space or comma separated). |
| `string? Creator` | Creating application. |
| `string? Producer` | PDF producer. |
| `DateTimeOffset? CreationDate` | Creation timestamp. |
| `DateTimeOffset? ModificationDate` | Modification timestamp. |
| `XmpPacket Xmp` | Full XMP packet (lazily loaded). |

### `PdfOutlineCollection : IReadOnlyList<PdfOutlineItem>`

| Member | Description |
|--------|-------------|
| `int Count` / `this[int index]` | Top-level items. |
| `PdfOutlineItem Add(string title, int page)` | Add top-level item (page dest). |
| `PdfOutlineItem Add(string title, PdfDestination, Action<PdfOutlineItem>? children = null)` | Add top-level item with destination. |
| `PdfOutlineItem Insert(int index, string title, int page)` | Insert top-level item at `index` (page dest). |
| `PdfOutlineItem Insert(int index, string title, PdfDestination, Action<PdfOutlineItem>? children = null)` | Insert top-level item at `index` with destination. |
| `void RemoveAt(int index)` | Remove the top-level item at `index`. |
| `void Clear()` | Remove entire outline tree. |

### `PdfOutlineItem`

| Member | Description |
|--------|-------------|
| `string Title` | Bookmark label. |
| `PdfDestination? Destination` | Navigation target. |
| `bool IsOpen` | Expanded/collapsed state. |
| `IReadOnlyList<PdfOutlineItem> Children` | Child items. |
| `PdfOutlineItem Add(string title, int page)` | Add a child (page dest). |
| `PdfOutlineItem Add(string title, PdfDestination, Action<PdfOutlineItem>? children = null)` | Add a child with destination. |
| `void Remove()` | Remove this item and subtree. |
| `void MoveTo(PdfOutlineItem? newParent, int index)` | Re-parent this item. |

### `PdfDestination`

| Member | Description |
|--------|-------------|
| `static PdfDestination ToPage(int pageIndex)` | Navigate to page, preserve scroll/zoom. |
| `static PdfDestination FitPage(int pageIndex)` | Fit entire page. |
| `static PdfDestination FitWidth(int pageIndex, double? top)` | Fit page width. |
| `static PdfDestination FitHeight(int pageIndex, double? left)` | Fit page height. |
| `static PdfDestination At(int pageIndex, double? left, double? top, double? zoom)` | Explicit position and zoom. |
| `static PdfDestination FitRect(int pageIndex, double left, double bottom, double right, double top)` | Fit a rectangle. |
| `int PageIndex` | 0-based page index. |
| `PdfDestinationType Type` | Destination type. |

### `PdfPageLabels`

| Member | Description |
|--------|-------------|
| `IReadOnlyList<PdfPageLabelRange> Ranges` | All ranges sorted by start index. |
| `void Set(int startIndex, PdfPageLabelStyle style, string? prefix = null, int start = 1)` | Define or replace a range. |
| `PdfPageLabelRange? Get(int index)` | Range covering `index`, or `null`. |
| `bool Remove(int startIndex)` | Remove range at `startIndex`. |
| `void Clear()` | Remove all page labels. |

### `PdfPageLabelRange`

| Member | Description |
|--------|-------------|
| `int StartPageIndex` | First page covered by this range (0-based). |
| `PdfPageLabelStyle Style` | Numbering style. |
| `string? Prefix` | Prefix string, or `null`. |
| `int StartNumber` | First number in the sequence (default `1`). |

### `PdfNamedDestinations : IReadOnlyCollection<string>`

| Member | Description |
|--------|-------------|
| `int Count` | Total named destinations. |
| `void Set(string name, PdfDestination destination)` | Create or update. |
| `PdfDestination? Get(string name)` | Resolve, or `null`. |
| `PdfDestination? this[string name]` | Indexer sugar for `Get`. |
| `IEnumerable<KeyValuePair<string, PdfDestination>> Entries()` | Enumerate name → destination pairs. |
| `bool Rename(string oldName, string newName)` | Rename; returns `false` if `oldName` not found. |
| `bool Remove(string name)` | Delete; returns `false` if not found. |

### `PdfViewerSettings`

| Member | Description |
|--------|-------------|
| `PdfPageMode? PageMode` | Panel shown on open. |
| `PdfPageLayout? PageLayout` | Page layout mode. |
| `PdfDestination? OpenAction` | Navigate here on open. |
| `bool? HideToolbar` | Hide viewer toolbar. |
| `bool? FitWindow` | Resize window to first page. |
| `bool? CenterWindow` | Centre window on screen. |
| `bool? DisplayDocTitle` | Show document title in title bar. |
| `bool? HideMenubar` | Hide the menu bar. |
| `bool? HideWindowUI` | Hide scrollbars / navigation controls. |
| `PdfPageMode? NonFullScreenPageMode` | Page mode when leaving full-screen. |
| `PdfReadingDirection? Direction` | Predominant reading order. |
| `PdfPrintScaling? PrintScaling` | Print-dialog page scaling. |
| `PdfDuplex? Duplex` | Duplex printing mode. |

### `PdfFormFields : IReadOnlyCollection<PdfFormField>`

| Member | Description |
|--------|-------------|
| `int Count` | Number of fields. |
| `PdfFormField? this[string fullName]` | Field by name, or `null`. |
| `bool TryGet(string fullName, out PdfFormField? field)` | Try-get by name. |
| `void Flatten()` | Bake all fields into page content. |
| `void Flatten(string fullName)` | Bake one named field. |

### `PdfSaveOptions`

| Member | Default | Description |
|--------|---------|-------------|
| `bool RemoveOrphans` | `true` | Reachability garbage collection. |
| `bool UseObjectStreams` | `false` | Object-stream packing. |
| `static PdfSaveOptions Default` | — | A fresh instance with the defaults above. |

## Limitations

These are deliberate boundaries of the current edit API:

- **Full-rewrite save only** — there's no incremental update, so editing a digitally signed PDF invalidates the signature, and the saved file's byte layout changes.
- **Encrypted → unencrypted** — editing decrypts; re-encrypting on save isn't supported yet.
- **The page tree is flattened** on edit (valid, but not byte-identical to a balanced original).
- **Page label ranges are not auto-adjusted** after structural edits — this is intentional. After rearranging pages the caller is responsible for renumbering via `PageLabels.Set` / `Remove`.
- **Content-level editing / redaction** (changing text or graphics *inside* a page's content stream) is not part of this API.
- **Form field appearance uses standard-14 fonts** — non-Latin glyphs in a generated appearance may not render unless the field's `/DR` contains an embedded font with the required glyph coverage. The stored value is always correct.

## See also

- [FluentApi.md](FluentApi.md) — creating PDFs from scratch.
- [GettingStarted.md](GettingStarted.md) — Part 7 (Editing) and Part 8 (Optimizing) walk-throughs.
- [Architecture.md](Architecture.md) — how the `Editing/` and `Optimization/` components fit together.
