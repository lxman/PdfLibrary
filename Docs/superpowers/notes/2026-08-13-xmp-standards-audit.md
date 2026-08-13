# XMP system audit against the primary standards (2026-08-13)

Audited the whole XMP subsystem — engine and app — against the XMP Specification Parts 1–3
(ISO 16684-1:2011 and the 2022 Adobe editions), which became available on this machine today and are
now indexed in the local `pdf-rag` server. Six parallel read-only workstreams. **No code was changed.**

## The headline

**Every line of the ported conformance data is a faithful reproduction of veraPDF. Zero defects.**
**Every real defect is in PdfLibrary's own parser, serializer, and projection — code Adobe never**
**wrote for us and no reference exists to check against. Nine findings, all silent round-trip data loss.**

That split is the useful result. It says the risk in this subsystem was never in the tables everyone
worried about; it is in the original code that surrounds them.

## Method, and the correction that made it work

The audit began against the XMP Specification and immediately started producing false positives,
because **the XMP Specification is the wrong ground truth for PDF/A**. PDF/A-2's governing list is
ISO 19005-2 Annex B, a 2005-era snapshot, and PdfLibrary's actual stated contract is *veraPDF parity*
(tuned across 1,316 files). The 2022 spec is newer than both.

Annex B is not on this machine. But the reference implementation is: veraPDF 1.28.1 ships in
`RiderProjects/EInvoice/tools/verapdf/bin/greenfield-apps-1.28.1.jar`, and its
`org.verapdf.model.tools.xmp.XMPConstants` holds the exact tables PdfLibrary was ported from. Dumping
those constants reflectively turned "undecidable without Annex B" into a direct diff.

The effect was decisive: **four reported bugs dissolved into parity findings**, and A1's undecidable
count went 9 → 0.

> **Methodology trap, recorded because it cost two false positives.** The first dump enumerated only
> `String[]` fields. veraPDF also holds two `String[][]` constants —
> `TIFF_YCBCRSUBSAMPLING_SEQ_CHOICE_COMMON` and `EXIF_COMPONENTS_CONFIGURATION_CLOSED_SEQ_CHOICE_COMMON`
> — reached through accessor *methods*, plus registrations made in `SchemasDefinitionCreator`'s own
> bytecode rather than in any constant. Those back the three manual `TryRegister` lines at
> `XmpPredefinedSchemas.cs:128,129,145`, which the dump made look unbacked. An oracle with a
> silent shape restriction reports absence as evidence. Verify against the *whole* artifact.

## Result by workstream

| # | Scope | Bugs | Parity / tolerated | Undecidable |
|---|---|---|---|---|
| A1 | `XmpPredefinedSchemas.cs` | **0** | 26/26 arrays + 3 manual registrations | 0 |
| A2 | `XmpStructTypes.cs` | **0** | 20/20 struct groups, byte-for-byte | 0 |
| A3 | `XmpTypeContainer.cs`, `XmpDate.cs` | **0** | regexes identical to reference | 2 |
| A4 | `XmpNode.cs` (parser), `XmpTreeSerializer.cs` | **8** | 2 tolerated | 0 |
| A5 | `XmpPacket.cs`, `XmpProperty.cs`, `XmpSchemas.cs` | **1** | 6 tolerated, 5 confirmed conformant | 1 |
| A6 | `XmpDomain.cs`, crosswalk, extension schemas | 0 (1 reclassified) | 3 confirmed conformant | 3 PDF/A-scoped |

### Verified by hand, not taken on report

- `ADOBE_PDF_COMMON`, `XMP_RIGHTS_COMMON`, `XMP_MEDIA_MANAGEMENT_COMMON`,
  `RESOURCE_EVENT_STRUCTURE`, `RESOURCE_REF_STRUCTURE` — all identical to the engine.
- veraPDF's `real` regex is `^[+-]?\d+\.?\d*|[+-]?\d*\.?\d+$` — character-for-character the engine's,
  unparenthesised alternation included. `boolean`, `integer`, `mimetype` likewise.
- The three manual `TryRegister` lines, resolved in `SchemasDefinitionCreator` bytecode.
- `XmpNode.cs:285` and `XmpProperty.cs:56` — the two worst round-trip findings, read directly.

## Findings that are NOT defects — do not "fix" these

Each is the engine faithfully reproducing veraPDF where the 2022 spec says otherwise. Changing any of
them **breaks the parity the engine is measured by**, which is the worst outcome available here.

| Apparent defect | Reality |
|---|---|
| `xmpRights:Certificate` / `WebStatement` typed `url`; Part 1 Table 6 says Text | veraPDF says `url` |
| `real` regex accepts `"123."` (fraction with no digits) | veraPDF's regex is identical |
| `mimetype` regex narrower than RFC 2046 `token` | veraPDF's regex is identical |
| `ResourceEvent` lacks `stEvt:changed`; `ResourceRef` lacks `originalDocumentID`/`filePath` | absent from veraPDF too |
| Marker `type` enum has `Beat`, spec has `Speech` | matches veraPDF; divergence is the spec's |
| `CuePointParam` and `Track` structs unmodelled | veraPDF models neither |
| ~40 "missing" `xmpDM:*` properties | false alarm — 57 registered DM properties match veraPDF 1:1 |
| ~110 TIFF/Exif entries | all match, including correct *exclusion* of PDF/A-1-only fields |
| Structs are closed (any unknown field invalidates the whole struct) | deliberate veraPDF mirroring; Part 1 §6.3.3 imposes no closedness |

## The real defects — all in PdfLibrary's own code

Ranked. The first two destroy content; the rest change form or lose structure.

1. **`xml:lang` on a plain literal is silently dropped.** `XmpNode.cs:285` — the simple-property path
   never reads `xml:lang`. `HasXmlLang`/`XmlLang` are populated only in `SetArray` for `rdf:li` items
   (`:306`), and the `RawXml` safety net engages only in the `rdf:value` branch (`:276`), which a plain
   literal never enters. `<dc:source xml:lang="en-us">foo</dc:source>` round-trips with no language.
   Widest blast radius of anything found — needs no struct, array or qualifier to trigger.
2. **Every `rdf:Alt` is read as a lang-alt.** `XmpProperty.cs:56` tests `IsArrayAlternate`, ignoring
   the `IsArrayAltText` flag the node already carries. A multi-item Alt whose items carry no
   `xml:lang` collapses onto the single key `"x-default"` — last wins, the rest discarded. The
   single-item behaviour is deliberate and documented (dc:title without xml:lang must still reach
   `PdfMetadata.Title`); the multi-item collapse is an unintended consequence. **This one has teeth:**
   `XmpDomain.ComparableValue` reads exactly this projection to decide whether a rewrite would narrow
   a value, so the fixer can act on a value it has already silently truncated.
3. **`parseType="Collection"` truncates to its first item.** `Element(Rdf+"Description")` returns only
   the first match; the rest vanish with no `RawXml` trace. (The production is forbidden in XMP, so
   the input is already invalid — but silent truncation is the wrong response to it.)
4. **RDF typed-node struct form (7.9.2.5) unrecognised** — misparsed as an extra struct nesting level;
   the implied `rdf:type` qualifier is lost entirely, not even caught by `RawXml`.
5. **`parseType="Literal"` / `"Other"` misclassified as struct**, dropping mixed text content.
6. **URI-valued properties lose the `rdf:resource` form.** The parser reads it (`XmpNode.cs:376`) but
   nothing preserves the distinction and the serializer never emits it, so
   `<xmp:BaseURL rdf:resource="…"/>` returns as element text. *Severity nuance:* the URI string
   survives, so this is a serialization non-conformance against Part 1 §7.5, not data loss.
7. **`RawXml` passthrough can reintroduce forbidden constructs** (e.g. `rdf:ID`) captured in a
   qualifier subtree, though the serializer never constructs one itself.
8. **Multi-island merge is silently last-wins.** `XmpPacket.cs:44` — Part 1 §7.4 requires a single
   `rdf:RDF`; the tolerant read is right for real ZUGFeRD files, but a property duplicated across
   islands disappears with no diagnostic.
9. **`rdf:RDF` not found when wrapped in non-`xmpmeta` outer XML** — tolerated by the no-false-positive
   contract, but a false-negative surface.

Confirmed clean on the packet side: all five `XmpSchemas.cs` URIs byte-exact with correct terminators;
the xpacket wrapper (PI form, BOM, GUID, ~2KB padding, single `rdf:RDF` on write) fully conformant.

## Open design question, not a bug

`XmpDomain` declares `xmpMM:OriginalDocumentID` through `pdfaExtension:schemas` with a type drawn from
`InferValueType`'s four strings, while Part 2 Table 25 defines it as **GUID**. The engine's table
omitting it is correct parity (veraPDF omits it too — it appears nowhere in the oracle). The question
is whether the *fixer* should assert a private `Text` definition for a property Adobe has already
defined, which is the exact failure mode Part 2 §1.3.2 warns about when extending an existing
namespace. This is a judgement call about honesty, not a conformance failure.

## What this changes about the struct-type backlog item

`stRef:originalDocumentID` is not a `ResourceRef` field in the 2022 spec **or** in veraPDF. Documents
carrying it are using a field with no standing anywhere, while `xmpMM:OriginalDocumentID`
(Part 2 Table 25) is its documented home — which is where 13 of 14 measured documents already carry
the identical value. That materially strengthens the case that stripping it is lossless.

Separately, Part 2 Table 8 settles the `stEvt:changed` question, and **not** the way it was assumed:
absent is *not* equivalent to `"/"`. `"/"` asserts the whole resource changed; absent is "presumed to
be undefined … assume anything might have changed". Same conservative effect, different kind of claim.

## Not checked

- ISO 19005-2 Annex B itself — still unavailable. No longer blocking: veraPDF is the operative
  authority for every question it was wanted for.
- `pdfaExtension` / `pdfaSchema` / `pdfaProperty` / `pdfaType` structure, and the exact
  `pdfaProperty:valueType` spelling — ISO 19005 vocabulary, outside the XMP spec.
- Camera Raw / Exif value-type grammars the 2022 spec defers to external CIPA documents; resolved
  against veraPDF instead where it mattered.
