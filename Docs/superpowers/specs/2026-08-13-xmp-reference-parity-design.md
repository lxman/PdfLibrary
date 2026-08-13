# XMP: pinning veraPDF parity

**Date:** 2026-08-13
**Status:** slice 1 IMPLEMENTED (fixture + `XmpParityTests` + regeneration README); slice 2
(divergence documentation) not started

**Slice 1 outcome:** the fixture is built by calling veraPDF's own
`SchemasDefinitionCreator.getPredefinedSchemaDefinitionForPDFA_2_3(false)` and reading the assembled
map, rather than reassembling `XMPConstants` — which sidesteps the `String[][]` trap entirely instead
of documenting a way around it. 277 properties and 20 structured types matched on the first run.

**The test earned its cost immediately: it found a real parity break.** veraPDF registers an `xpath`
simple type and the engine did not. Unreachable through the tables (nothing is typed `xpath`), but
`IsKnownType`'s one consumer is `XmpExtensionSchemas`' decision whether an extension-schema-declared
property registers — so a document declaring a property as XPath registered in veraPDF and not here,
leaving `XmpPropertyPredefinedRule` firing where the reference is silent. A false positive, the one
thing the contract forbids. Fixed in the same change; see `XmpTypeContainer.RegisterBaseSimpleTypes`.
**Scope:** make the engine's XMP conformance tables provably equal to veraPDF's, and make the places
where they knowingly disagree with the XMP Specification legible — so nobody "fixes" them.

**Source:** `Docs/superpowers/notes/2026-08-13-xmp-standards-audit.md`.

## The problem this prevents

The 2026-08-13 audit compared every XMP conformance table against the XMP Specification and found
what looked like a pile of bugs: `xmpRights:Certificate` typed `url` where Part 1 Table 6 says Text; a
`real` regex that accepts `"123."`; a `mimetype` regex narrower than RFC 2046; struct types that
reject any unknown field though Part 1 §6.3.3 imposes no closedness; `Marker.type` enumerating `Beat`
where the spec says `Speech`; forty apparently-missing `xmpDM:*` properties.

**Every one was the engine faithfully reproducing veraPDF.** The specification is simply not the
standard this code answers to: PDF/A-2's governing list is ISO 19005-2 Annex B, a 2005-era snapshot,
and PdfLibrary's operative contract is *veraPDF parity* — a committed snapshot over 1,316 files in
which PdfLibrary is a strict subset with zero false positives.

Acting on that first-pass report would have made the engine disagree with the validator it is
measured by. That is the worst outcome available here, and it was one review away.

It will recur. The tables read like data anyone can check against a published spec, there is nothing
in the files saying which spec, and the next person to hold the XMP Specification will reach the same
wrong conclusion the audit did. This spec makes that impossible instead of unlikely.

## What the audit established

Verified by dumping veraPDF 1.28.1's `org.verapdf.model.tools.xmp.XMPConstants` and diffing
element-for-element, in order:

| Engine file | Result |
|---|---|
| `XmpPredefinedSchemas.cs` | 26/26 arrays exact, plus 3 manual registrations traced to `SchemasDefinitionCreator` |
| `XmpStructTypes.cs` | 20/20 struct groups exact, byte-for-byte, including closed-choice regexes |
| `XmpTypeContainer.cs` | `real`, `boolean`, `integer`, `mimetype` regexes character-for-character identical |

Zero defects across the entire ported surface.

> **The trap that cost two false findings, recorded so the regeneration procedure avoids it.** The
> first dump enumerated only `String[]` fields. veraPDF also holds two `String[][]` constants —
> `TIFF_YCBCRSUBSAMPLING_SEQ_CHOICE_COMMON` and `EXIF_COMPONENTS_CONFIGURATION_CLOSED_SEQ_CHOICE_COMMON`
> — reached via accessor methods, plus registrations made directly in `SchemasDefinitionCreator`'s
> bytecode. Those back `XmpPredefinedSchemas.cs:128,129,145`, which the partial dump made look
> unbacked and invented. **An oracle with a silent shape restriction reports absence as evidence.**

## Design

Three artifacts, in decreasing order of value.

### 1. A pinned parity fixture and test — the load-bearing piece

Commit a snapshot of veraPDF's tables as a test resource, and assert the engine's tables equal it.

- **Resource:** `PdfLibrary.Tests/Resources/verapdf-xmp-constants-1.28.1.txt` — every table, in array
  order, in a stable text format, with the veraPDF version in the filename.
- **Test:** enumerates the engine's registered predefined properties and struct field tables and
  asserts equality against the fixture, reporting the first divergence with both sides named.

What it catches: **engine drift**. Any future edit to `XmpPredefinedSchemas.cs` or `XmpStructTypes.cs`
that moves away from the reference fails loudly, whether it came from a well-meaning spec reading or a
typo. That is exactly the failure mode this spec exists to prevent, and it is the majority of the risk.

What it does not catch: veraPDF changing in a later release. Accepted deliberately — the fixture is
version-stamped, and bumping veraPDF is a conscious act that should regenerate it.

The test must compare **semantic content, not file bytes**, so reformatting the engine tables does not
fail it while a changed type name does.

### 2. A regeneration procedure

The fixture is only trustworthy if regenerating it is mechanical. Document, next to the fixture:

- where the jar comes from (`RiderProjects/EInvoice/tools/verapdf/bin/greenfield-apps-1.28.1.jar`,
  with the caveat that this path is outside this repo and may move);
- that `String[]` fields alone are **not sufficient** — `String[][]` accessors and
  `SchemasDefinitionCreator`'s own registrations must be included, with the two known `String[][]`
  constants named;
- the exact commands (JDK `javap`/reflection) used, so the next person does not re-derive them.

### 3. Documentation at the divergence sites

A short, factual note at each place where the engine knowingly differs from the XMP Specification,
saying: *this matches veraPDF; the current spec says otherwise; do not change it without moving the
parity fixture first.*

Sites, from the audit:

| Site | Engine | Current XMP Specification |
|---|---|---|
| `XmpPredefinedSchemas.cs` xmpRights | `Certificate`/`WebStatement` = `url` | Part 1 Table 6: Text |
| `XmpTypeContainer.cs` `real` | accepts `"123."` | Part 1 §8.2.1.4: fraction needs ≥1 digit |
| `XmpTypeContainer.cs` `mimetype` | `[-\w+\.]+/[-\w+\.]+` | RFC 2046 `token` is wider |
| `XmpTypeContainer.cs` `MakeStructValidator` | structs are closed | Part 1 §6.3.3 imposes no closedness |
| `XmpStructTypes.cs` `MarkerRestricted` | `type` enumerates `Beat` | Part 2 Table 16: `Speech` |
| `XmpStructTypes.cs` `ResourceEvent` | no `stEvt:changed` | Part 2 Table 8 defines it |
| `XmpStructTypes.cs` `ResourceRef` | no `originalDocumentID`, no `filePath` | Part 1 Table 3 / Part 2 Table 9 |
| `XmpPredefinedSchemas.cs` xmpMM | no `OriginalDocumentID` | Part 2 Table 25 defines it (GUID) |
| `XmpPredefinedSchemas.cs` pdf | no `Trapped` | Part 2 Table 30 defines it (Boolean) |

One class-level comment stating the doctrine once, plus one-line pointers at each site. Not nine
essays — the audit note holds the detail and is linked from the class comment.

**`pdf:Trapped` closes a standing backlog item.** It has sat unresolved pending Annex B. veraPDF does
not treat it as predefined — it appears nowhere in the oracle, and the jar's only `Trapped` strings
are Info-dictionary feature classes. The ~10 corpus findings are **correct**, and the property must
**not** be added to the predefined table. Record that where the question will next be asked.

## Slices

1. **Fixture + parity test + regeneration procedure.** The whole point; ship it alone if nothing else.
2. **Divergence documentation**, including the `pdf:Trapped` ruling.

## Testing

The parity test *is* the test. Verify it by deliberately mutating one engine table entry in a scratch
edit and confirming a clear failure naming both sides — a pinning test nobody has seen fail is not
known to work.

## Out of scope

- Changing any engine table. Nothing here is a fix; the tables are correct.
- The nine genuine defects — separate spec,
  `2026-08-13-xmp-round-trip-fidelity-design.md`.
- Whether veraPDF is itself right against Annex B. Unanswerable here and not worth answering: veraPDF
  is the reference PDF/A validator, so matching it is the goal regardless.
- Vendoring the veraPDF jar into this repo.

## Risks

- **A parity fixture can entrench a genuine veraPDF bug.** Accepted knowingly: the engine's value is
  being a trustworthy subset of the reference, and a divergence — even a correct one — costs more than
  it gains. If a veraPDF defect ever needs deviating from, that should be a deliberate, documented,
  single-site exception, not a silent table edit.
- **The oracle lives in another repo.** If `EInvoice/tools/verapdf` moves, regeneration breaks. The
  committed fixture keeps working; only regeneration is affected, which is why the procedure records
  the version explicitly.
