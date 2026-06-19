# PDF Editing API

This document covers the **editing / mutation** API — loading an existing PDF, changing it (pages, merging, splitting), and saving the result. It is the middle leg of the library's *load → edit → optimize* story. Creating PDFs from scratch is documented separately in [FluentApi.md](FluentApi.md); shrinking them is in the [Optimization](#see-also) section of the README.

## Table of Contents

- [Overview](#overview)
- [Entering edit mode](#entering-edit-mode)
- [Page operations](#page-operations)
- [Merging, splitting, importing](#merging-splitting-importing)
- [Saving](#saving)
- [Encrypted input](#encrypted-input)
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

## Encrypted input

You can edit an encrypted PDF — pass the password to `Load`. On `.Edit()` the document is decrypted in place, and **the saved output is unencrypted**:

```csharp
using var doc = PdfDocument.Load("protected.pdf", "password");
var edit = doc.Edit();           // decrypted here
edit.Pages.RemoveAt(0);
edit.Save("plain.pdf");          // unencrypted
```

Re-encrypting on save is not yet supported. (The creation builder *can* produce encrypted PDFs — see [FluentApi.md](FluentApi.md#encryption).)

## API reference

### `PdfDocument`

| Member | Description |
|--------|-------------|
| `PdfDocumentEditor Edit()` | Enter edit mode and return the editor. |

### `PdfDocumentEditor : IDisposable`

| Member | Description |
|--------|-------------|
| `static PdfDocumentEditor Open(string path, string? password = null)` | Load + edit. Editor owns the document. |
| `static PdfDocumentEditor CreateBlank()` | New empty editable document. |
| `static PdfDocument Merge(IEnumerable<PdfDocument> sources)` | Combine documents into a new one. |
| `PdfPageCollection Pages` | Page view + operations. |
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

### `PdfSaveOptions`

| Member | Default | Description |
|--------|---------|-------------|
| `bool RemoveOrphans` | `true` | Reachability garbage collection. |
| `bool UseObjectStreams` | `false` | Object-stream packing. |

## Limitations

These are deliberate boundaries of the current edit API:

- **Full-rewrite save only** — there's no incremental update, so editing a digitally signed PDF invalidates the signature, and the saved file's byte layout changes.
- **Encrypted → unencrypted** — editing decrypts; re-encrypting on save isn't supported yet.
- **The page tree is flattened** on edit (valid, but not byte-identical to a balanced original).
- **Page labels** are not re-indexed after structural edits.
- **Content-level editing / redaction** (changing text or graphics *inside* a page's content stream) is not part of this API.

## See also

- [FluentApi.md](FluentApi.md) — creating PDFs from scratch.
- [GettingStarted.md](GettingStarted.md) — Part 7 (Editing) and Part 8 (Optimizing) walk-throughs.
- [Architecture.md](Architecture.md) — how the `Editing/` and `Optimization/` components fit together.
