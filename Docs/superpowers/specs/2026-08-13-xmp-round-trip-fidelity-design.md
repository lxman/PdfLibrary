# XMP: round-trip fidelity, second pass

**Date:** 2026-08-13
**Status:** design, not approved, not implemented
**Scope:** the nine parse/serialize/projection defects the 2026-08-13 standards audit found in
PdfLibrary's OWN XMP code. The conformance data tables are deliberately out of scope — the audit
proved them an exact port of veraPDF, and they get the opposite treatment in
`2026-08-13-xmp-reference-parity-design.md`.

**Source:** `Docs/superpowers/notes/2026-08-13-xmp-standards-audit.md`, audited against XMP
Specification Parts 1–3 (ISO 16684-1:2011 + 2022 Adobe editions).

## The problem

The 2026-08-12 round-trip program fixed struct and array flattening and was declared complete. It
was not. Nine further paths still lose or reshape content, and two of them destroy it outright.

They were missed because the earlier work was driven by a *reproduction* — a real Illustrator packet
that visibly broke — rather than by the specification's list of RDF productions. Anything that did
not appear in the sample survived unexamined. This pass is production-by-production instead.

### D1 — `xml:lang` on a plain literal is dropped

The widest-reaching defect, and it needs no struct, array or qualifier to trigger.

**In**

```xml
<dc:source xml:lang="en-us">Photo by A. Person</dc:source>
```

**Out**

```xml
<dc:source>Photo by A. Person</dc:source>
```

`XmpNode.cs:285` sets `IsSimple`/`Value` and never reads `xml:lang`. `HasXmlLang`/`XmlLang` exist but
are populated only in `SetArray` for `rdf:li` items (`:306`). The `RawXml` safety net engages only in
the `rdf:value` branch (`:276`), which a plain literal never enters. Part 1 treats `xml:lang` as a
qualifier legal on any value, not only on array items.

### D2 — every `rdf:Alt` is read as a lang-alt, collapsing multi-item Alts

**In**

```xml
<xmp:Thumbnails><rdf:Alt>
  <rdf:li>first</rdf:li>
  <rdf:li>second</rdf:li>
  <rdf:li>third</rdf:li>
</rdf:Alt></xmp:Thumbnails>
```

**Projected as** a one-entry `LangAlt` — `{"x-default": "third"}`. Two items gone.

`XmpProperty.cs:56` tests `IsArrayAlternate` and ignores the `IsArrayAltText` flag the node already
carries; `:60` then keys every item on `item.XmlLang ?? "x-default"`, so untagged items overwrite one
another. Part 1 §6.3.4 defines Alt as a general-purpose alternatives array — language is one use, not
the definition.

**This one has teeth beyond the projection.** `Pellucid.Core`'s `XmpDomain.ComparableValue` reads
exactly this projection to decide whether a rewrite would narrow a value. The fixer can therefore
judge — and rewrite — a value it has already silently truncated. The node keeps the real data, so
nothing is lost on a pure save; the damage happens when a *consumer of the projection* acts.

### D3–D5 — forbidden and unmodelled RDF productions lose content silently

| | Production | Behaviour |
|---|---|---|
| D3 | `rdf:parseType="Collection"` | `Element(Rdf+"Description")` returns only the first match; later items vanish with no `RawXml` trace |
| D4 | typed-node struct form (Part 1 §7.9.2.5) | misparsed as an extra struct nesting level; the implied `rdf:type` qualifier lost entirely |
| D5 | `rdf:parseType="Literal"` / `"Other"` | misclassified as struct, dropping mixed text content |

D3 and D5 are productions XMP *forbids* (Part 1 §C.2.10, §C.2.11), so the input is already invalid.
Silent truncation is still the wrong response: the packet should survive unchanged so a human can see
what the producer wrote.

### D6–D9 — form and diagnostics

- **D6** URI-valued properties lose the `rdf:resource` form. The parser reads it (`XmpNode.cs:376`,
  comment and all) but nothing preserves the distinction and the serializer never emits it, so
  `<xmp:BaseURL rdf:resource="http://…"/>` returns as element text. **The URI string survives** — this
  is a serialization non-conformance against Part 1 §7.5, not data loss, and is ranked accordingly.
- **D7** `RawXml` passthrough can reintroduce constructs XMP forbids (e.g. `rdf:ID`) captured inside a
  qualifier subtree, though the serializer never constructs one itself.
- **D8** Multi-island merge is silently last-wins (`XmpPacket.cs:44`). Part 1 §7.4 requires a single
  `rdf:RDF`; the tolerant read is correct for real ZUGFeRD "DWC FX Generator" files, but a property
  duplicated across islands disappears with no diagnostic.
- **D9** `rdf:RDF` is not found when wrapped in outer XML that is not `x:xmpmeta` — consistent with
  the tolerant-parse contract, but a false-negative surface.
  **DROPPED 2026-08-13, deliberately — not deferred, not forgotten.** Three reasons, in order of
  weight. (1) The input is not a conformant XMP packet: Part 3 defines the packet as an `x:xmpmeta`
  wrapper, and a bare `<outer><rdf:RDF>` is not one, so declining to parse it is arguably the correct
  reading rather than a defect — which is why the audit classified it TOLERATED, not BUG. (2) The fix
  can only ever ADD findings, because it makes the engine see properties it currently does not. That
  is the false-positive direction, the one thing the engine's contract forbids, and it is the
  direction no test can clear for us: the veraPDF parity snapshot cannot vouch for a document shape
  the corpus does not contain, so a green snapshot would prove nothing here. (3) No real document is
  known to do this — the reproduction was synthetic. Reopen only with a real document that fails, and
  with veraPDF's own verdict on it.

## Design

### The governing constraint

**Any change to the parser can move a conformance verdict.** The rules read `ConformanceContext.XmpTree`,
which is this parser's output, and the engine's value is its veraPDF parity — a committed snapshot
over 1,316 files with *zero false positives*, PdfLibrary being a strict subset of veraPDF.

So every slice below carries the same gate, and it is not optional:

> Re-run the veraPDF parity snapshot. Agreement counts may rise; **false positives must remain zero**.
> A slice that makes PdfLibrary report something veraPDF does not is rejected, however correct it
> looks against the specification.

The second gate is the app: `Pellucid.App.Tests`' XMP corpus test over local-708, because D2 changes
what the fixer sees.

### D1 — carry `xml:lang` on any node

`XmpNode` already has `HasXmlLang`/`XmlLang`; the fix is to populate them on the simple-property path
and emit them. Widening their meaning from "array item language" to "this node's language qualifier"
is safe for existing consumers — `IsArrayAltText` reads `HasXmlLang` on *children* only, and
`XmpProperty.FromNode` reads it on *items* only — but the doc comments on both members say "array
item" and must be rewritten, or the next reader will re-narrow them.

Serializer emits `xml:lang` whenever `HasXmlLang`.

### D2 — project as LangAlt only when it really is one

In `XmpProperty.FromNode`, project an `rdf:Alt` as `LangAlt` when **either**:

- `IsArrayAltText` is true (all items carry `xml:lang` — a genuine lang-alt), **or**
- the array has exactly one item.

Otherwise project it as an ordinary ordered `Array`.

The single-item clause is load-bearing and must not be "simplified" away: a `dc:title` written without
`xml:lang` has to keep reaching `PdfMetadata.Title` and `UaTitleRule`, which is why the current
behaviour exists at all. What it never intended was to let sibling items overwrite each other.

Consequence to verify, not assume: a multi-item untagged Alt stops being a `LangAlt` and starts being
an `Array`, which changes `XmpValueKind` for that property. Every `switch` on `XmpValueKind` in both
repos must be reviewed — the app has several, including `ComparableValue`, `InferValueType` and
`ContainerOf`.

### D3–D5 — widen the `RawXml` safety net

These need no new model. The parser already has the right mechanism for "a shape this model cannot
express": capture the element verbatim into `RawXml` so the serializer re-emits it untouched. Extend
that capture to `parseType="Collection"`, `parseType="Literal"`, `parseType="Other"`, and the
typed-node form.

For the typed-node form, additionally classify the node as the struct it is, so conformance rules
keep seeing a struct — the same "capture in addition to, not instead of, normal classification"
contract the `rdf:value` branch already documents.

### D6 — a URI-value flag

Add `IsUriValue` to `XmpNode`, set it when the value came from `rdf:resource`, and have the serializer
emit the attribute form when set. Lowest priority of the nine: no information is lost today.

### D7 — refuse to launder forbidden constructs

On capture, strip `rdf:ID` attributes from the snapshot. This deliberately makes `RawXml` not quite
verbatim; the alternative — re-emitting a construct the spec forbids — is worse. Document it at the
capture site, because "verbatim" is asserted in several comments that will otherwise become false.

### D8 — report island collisions

Keep the merge (real files depend on it). Record collisions where a property name appears in more than
one island with different values, and surface them. Do **not** change the merge semantics in this
slice: which island should win is a separate question with no obvious answer, and guessing it would
change what saved documents contain.

### D9 — find `rdf:RDF` anywhere

Widen `FindRdf` to search descendants when no `x:xmpmeta` is present. Tolerant-parse contract is
unchanged: still no throw, still an empty list when nothing parses.

## Slices

> **RE-RANKED 2026-08-13 after a reachability probe. The ordering below is the corrected one; the
> original is recorded underneath because the correction is the useful part.**
>
> A throwaway probe round-tripped every defect's documented fragment through the shipped code before
> any fix was written. **All nine reproduce — none was speculative.** Three findings changed the plan:
>
> - **D2 does NOT destroy content.** The serializer works from the node, so a multi-item untagged
>   `rdf:Alt` re-serializes with all three items intact. Only the PROJECTION collapses
>   (`Kind=LangAlt, LangAlt.Count=1`). It is a consumer-side defect — dangerous because
>   `XmpDomain.ComparableValue` reads that projection and can write back — but the document is fine.
>   This spec ranked it top-tier on the assumption it destroyed content. It does not.
> - **D5 destroys text**, which the audit under-stated: `rich <b>text</b>` returns as
>   `<ns1:b>text</ns1:b>` — the word "rich" is gone, and the literal has become a struct.
> - **D3 does more than truncate**: it drops item two AND silently re-emits `parseType="Collection"`
>   as `parseType="Resource"`, changing the production on the way out.
>
> Evidence-based severity:
>
> | Tier | Defects |
> |---|---|
> | Destroys content on save | D1, D3, D5 |
> | Loses content at parse | D8 (island collision), D9 (never found) |
> | Corrupts structure, content survives | D4 |
> | Changes form / emits a forbidden construct | D6, D7 |
> | Projection only — document round-trips fine | D2 |

1. **D1** — `xml:lang` on plain literals. **DONE** (`9f6b43a`). Parser-only: the serializer's
   `EmitShape` already emitted the attribute for any node carrying it and was waiting on the read side.
2. **D3, D5** — the two remaining content-destroying defects. Both are `RawXml` widening.
3. **D2** — Alt projection. Still the only slice that changes `XmpValueKind` for existing documents,
   so it keeps the app-side corpus gate and the review of every `XmpValueKind` switch in both repos.
   Demoted on damage, not on importance: it is the one defect a *fixer* can act on destructively.
4. **D4** — typed-node struct form. **DONE** (`a55c5db`). Preserved, not reinterpreted: deciding a
   single element child names a TYPE rather than a FIELD is ambiguous in real packets, and guessing
   wrong destroys a field name — a worse loss than the one being repaired.
5. **D6** — `rdf:resource` URI form. **DONE**. Round-trip fidelity only: the attribute form is
   restored for values that ARRIVED in it, never synthesised for uri-typed properties that did not.
6. **D7** — `rdf:ID` laundering through `RawXml`.
7. **D8** — collision diagnostics.

*(**D9 dropped** — see its entry above. Not part of any remaining slice.)*

*Original ordering, superseded: D1 → D2 → D3–D5 → D6,D7,D9 → D8.*

## Testing

- **Round-trip fixtures, one per defect**, asserting parse→serialize preserves the data model. Each
  fixture is the *concrete XML in this document*, so the spec and the tests cannot drift.
- **A production-coverage test** enumerating Part 1 Annex C's productions with the engine's handling
  of each, so a future reader can see at a glance what is modelled, what is captured verbatim, and
  what is rejected. This is the artifact whose absence let nine defects survive the first pass.
- **veraPDF parity snapshot** — the gate above. Zero false positives, non-negotiable.
- **local-708 XMP corpus gate** in `Pellucid.App.Tests` for slice 2.
- Fixtures must be **sub-second and NOT `LocalOnly`** — a `LocalOnly` category silently drops out of
  CI (`ci.yml` filters `Category!=LocalOnly`), which has bitten this repo before.

## Out of scope

- The conformance data tables — proven exact against veraPDF; see the parity spec.
- General qualifier *modelling*. `RawXml` preserves qualifiers today and parity does not require them.
  Modelling them properly is a larger change with no demonstrated need.
- Which island should win on a multi-island collision (D8 reports, does not resolve).
- `pdfaExtension` structure — ISO 19005 vocabulary, not XMP.

## Risks

- **Parity drift is the real risk, not correctness.** Six of these defects make the engine more
  spec-correct; if veraPDF shares a defect, "fixing" it breaks the subset property that makes
  PdfLibrary trustworthy. Where the parity snapshot moves, stop and decide deliberately rather than
  updating the baseline.
- **D2 changes a public projection.** `XmpValueKind` is consumed across two repos.
- The audit found these by reading, not by running. Every defect above should be **reproduced with a
  failing test before it is fixed** — a defect nobody has demonstrated may not be reachable.
