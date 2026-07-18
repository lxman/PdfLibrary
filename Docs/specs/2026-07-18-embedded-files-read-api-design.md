# Embedded-Files Read API — Design

**Date:** 2026-07-18
**Status:** Approved (design review with user)
**Consumer driving this:** the EInvoice Factur-X bridge (`EInvoice.FacturX`, a future package in the
EInvoice repo) needs to extract `factur-x.xml` from PDF/A-3 invoices. Today every piece required —
catalog access, the `/Names /EmbeddedFiles` name tree, `/Filespec` → `/EF` streams, filter decode —
is `internal`. This design adds the missing **public, generic, read-only** embedded-files surface.
Nothing Factur-X-specific lands in PdfLibrary; "find the file named factur-x.xml" stays in the bridge.

## 1. Shape & placement

- New public method on the document facade:
  `PdfDocument.GetEmbeddedFiles()` → `IReadOnlyList<EmbeddedFileDescriptor>`.
- Implemented by `internal static class EmbeddedFileReader` in namespace `PdfLibrary.Document`,
  new file `PdfLibrary/Document/EmbeddedFiles.cs` — the established reader pattern
  (`OutputIntentReader` / `AcroFormReader`: internal reader, public immutable model, `PdfDocument`
  accessor).
- Like `OutputIntentReader`, the reader performs its **own** catalog walk and does not reuse
  `ConformanceContext`. That duplication is deliberate and documented precedent (see the class
  comment in `OutputIntents.cs`): the public reader must never perturb the load-bearing
  conformance suite.

## 2. Model: `EmbeddedFileDescriptor`

Sealed, internal constructor, all members read-only:

| Member | Source | Notes |
|---|---|---|
| `string? Name` | name-tree key | What Acrobat lists. Null for `/AF`-only entries. |
| `string? FileName` | filespec `/F` | |
| `string? UnicodeFileName` | filespec `/UF` | |
| `string? Description` | filespec `/Desc` | |
| `string? AfRelationship` | filespec `/AFRelationship` | Raw name value, e.g. `"Alternative"`, `"Data"`. Null when absent. |
| `string? MimeType` | embedded stream `/Subtype` | e.g. `"text/xml"`. |
| `bool IsAssociated` | catalog `/AF` membership | True when the filespec is referenced from the **catalog's** `/AF` array. |
| `bool HasData` | | True iff the embedded stream resolved and decoded. |
| `byte[]? GetDataBytes()` | decoded `/EF` stream | Defensive copy, mirroring `OutputIntentDescriptor.GetDestProfileBytes()`. Null when `HasData` is false. |

## 3. Traversal & semantics

1. **Name tree:** resolve catalog → `/Names` → `/EmbeddedFiles`; enumerate with an **iterative**
   walker (explicit stack), cycle-guarded by indirect object number, node-budgeted — mirroring
   `ConformanceContext.EnumerateNameTree` (100 000-node budget), not the recursive
   `PdfNamedDestinations` walker. Leaf `/Names` arrays are `[key value …]` pairs; descend `/Kids`.
2. **Catalog `/AF` union:** additionally read the catalog-level `/AF` array; any filespec found
   there and not already yielded by the name tree is appended (with `Name = null`). Dedup is by
   indirect object number — Factur-X files reference the *same* filespec from both places, which
   must yield **one** descriptor with `IsAssociated = true`. Page/annotation/XObject-level `/AF`
   arrays are out of scope (no `MaterializeAllObjects` full-document scan on this path).
3. **Stream selection:** resolve the filespec's `/EF` dictionary; prefer the `/UF` stream, fall
   back to `/F` (same preference as `EmbeddedFileSpecRule.EmbeddedStream`).
4. **Decode:** eagerly at `Read()` time via `PdfStream.GetDecodedData(document.Decryptor)` —
   encrypted documents work for free, matching the `OutputIntentReader` precedent.

## 4. Error handling

Never throw for document content. Per entry, any failure — missing `/EF`, unresolvable stream,
unknown/broken filter, decode exception — is caught per-entry: the descriptor is still returned
with whatever metadata resolved and `HasData = false`. A missing/`null` name tree, a catalog
without `/Names`, or a junk tree node yields an empty (or partial) list, silently. Cycles and
oversized trees terminate via the guard/budget.

## 5. Testing

Unit tests (in `PdfLibrary.Tests`, hand-built documents in the `PreflightSlice8Tests.AddEmbeddedFile`
style — `new PdfDocument()` + `AddObject` + trailer `/Root`):

- flat `/Names` leaf; `/Kids`-nested tree; cycle in `/Kids` (terminates, yields reachable entries);
- `/UF`-over-`/F` stream preference; filespec without `/EF` (metadata-only, `HasData = false`);
- undecodable filter (entry kept, `HasData = false`);
- `/AF`-only filespec (yielded with `Name = null`, `IsAssociated = true`);
- filespec referenced from both name tree and catalog `/AF` (one descriptor, `IsAssociated = true`);
- no name tree at all (empty list); metadata fields (`/F`, `/UF`, `/Desc`, `/AFRelationship`,
  stream `/Subtype`) round-trip.

Integration fixture: one official ZUGFeRD 2.5 example, `MINIMUM_Rechnung_fx.pdf`, added under
`TestPDFs/` (FeRD-licensed distribution — same provenance as the CII XML samples already committed
in the EInvoice repo). Test asserts `GetEmbeddedFiles()` yields an entry named `factur-x.xml`
whose decoded bytes parse as XML with a `CrossIndustryInvoice` document element and
`AfRelationship`/`IsAssociated` set.

## 6. Version & release

- CHANGELOG entry under `[Unreleased]` (Keep a Changelog format).
- `PdfLibrary.csproj` `<Version>`: 2.4.1 → **2.5.0** — new public API means a minor bump; 2.4.1
  was never published, so it is simply renamed.
- The pending uncommitted `pack-local.ps1` InnerText robustness fix rides along on the
  implementation branch (its own commit).
- No nuget.org publish and no push as part of this work (standing gate: only on explicit user
  instruction). How the EInvoice bridge consumes this (pack-local dev feed vs waiting for a 2.5.0
  publish) is deliberately deferred to a follow-up discussion once the API is built.

## 7. Out of scope

- Any write/attach/Builder API (`/AF` authoring, embedding files) — that is the Factur-X phase-2
  writer's concern and a separate design.
- FileAttachment annotations and non-catalog `/AF` sites.
- Factur-X-specific selection/naming logic — lives in the EInvoice bridge.
- Changes to `Preflighter` / conformance rules.
