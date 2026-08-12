# XMP: honest round-trip

**Date:** 2026-08-12
**Status:** design approved, not implemented
**Scope:** two slices — extract an `Xmp` project, then fix the round-trip. The PDF/A XMP
*remediation* program is deliberately out of scope and gets its own spec once authoring exists.

## The bug

`XmpPacket.Parse` → `Serialize` destroys every structured XMP value. Proven by round-tripping a real
Illustrator packet through the shipped code:

**In**

```xml
<xmpMM:History><rdf:Seq>
  <rdf:li rdf:parseType="Resource">
    <stEvt:action>saved</stEvt:action>
    <stEvt:instanceID>xmp.iid:7acea5a3-d3b5-4e05-a570-0a5cf27dfe45</stEvt:instanceID>
    <stEvt:when>2021-06-04T14:38:59+09:00</stEvt:when>
    <stEvt:softwareAgent>Adobe Illustrator 25.2 (Macintosh)</stEvt:softwareAgent>
    <stEvt:changed>/</stEvt:changed>
  </rdf:li>
</rdf:Seq></xmpMM:History>
```

**Out**

```xml
<xmpMM:History><rdf:Seq>
  <rdf:li>
saved
xmp.iid:7acea5a3-d3b5-4e05-a570-0a5cf27dfe45
2021-06-04T14:38:59+09:00
Adobe Illustrator 25.2 (Macintosh)
/
 </rdf:li>
</rdf:Seq></xmpMM:History>
```

`stEvt:action`, `stEvt:when`, `softwareAgent` and `parseType` are all absent from the output. Five
typed fields collapse into one whitespace-separated blob, unrecoverably — nothing downstream can tell
which token was the timestamp.

**Cause.** `XmpPacket.Parse` reads array items with `li.Value`, which returns the concatenated text of
all descendants; `Serialize` writes them back as `<rdf:li>{text}</rdf:li>`. The write-side model has
only `XmpValueKind { Simple, Array, LangAlt }` and `XmpProperty { Value, Items, LangAlt }` — no struct
representation exists. The same `else` branch flattens non-array structs too
(`xmpTPg:MaxPageSize` → `"20.00000020.000000Millimeters"`).

**Not a regression.** `XmpPacket` was introduced 2026-06-20 as *"tolerant XMP parse with
attribute/element/Seq/Bag/Alt support"*. Structs were never in scope. It became data loss when the
packet started being re-serialized rather than only read.

**Why no test caught it.** 36 XMP tests across `XmpPacketParseTests`, `XmpPacketSerializeTests` and
`XmpPacketRegressionTests`. None mentions `parseType`, `stEvt`, or any struct. Every test uses flat
values, so every test passes.

### Blast radius

`PdfMetadata`'s setters (`Title`, `Author`, `Subject`, `Keywords`, `Creator`, `Producer`,
`CreationDate`, `ModificationDate`) each mutate the parsed packet and the save re-serializes it via
`WriteXmpStream() => WriteMetadataStream(Xmp.Serialize())`. So:

- **Library**: `PdfDocumentEditor.Metadata.Title = "…"` on a document with structured XMP mangles it.
  That is a headline 1.0.0 feature ("metadata with synced XMP").
- **Pellucid**: `DocumentPropertiesService.ApplyAndSave` sets those properties. Editing any field in
  Document Properties and saving flattens `xmpMM:History`, `xmpMM:DerivedFrom`, `xmpTPg:Fonts`.

Affected properties are ubiquitous in Adobe output: `xmpMM:History`, `xmpMM:DerivedFrom`,
`xmpMM:Versions`, `xmpTPg:Fonts`, `xmpTPg:SwatchGroups`, `xmpTPg:MaxPageSize`.

**Not affected:** Pellucid's `metadata` remediation. It calls `SetRawXmp` with a freshly built
minimal packet rather than round-tripping, so it *replaces* the packet wholesale. That discards the
same data by a different mechanism and is a separate decision, out of scope here (see Deferred).

## Why not a general RDF library

XMP is RDF/XML, so dotNetRDF or RDFSharp look like a free answer. They are not, because what needs
preserving lives below RDF's semantic layer. A triple store discards `rdf:Seq` ordering (`History` is
an ordered edit trail — order *is* the data), flattens the `parseType="Resource"` vs nested
`rdf:Description` distinction, and re-encodes qualifiers. Output constraints are XMP-specific too:
the `<?xpacket?>` wrapper, padding for in-place update, and — for the later remediation — an
extension-schema block whose exact `pdfaSchema`/`pdfaProperty` prefixes veraPDF checks by name.
"Valid RDF/XML" is not sufficient; we need *this* RDF/XML. Adobe's own XMP Toolkit implements the
data model directly for the same reason.

The existing parser is also veraPDF-parity-verified across 1,316 corpus files. Replacing it carries
regression risk on the half that is not broken.

## Slice 1 — extract an `Xmp` project

A pure move. No logic changes, so it is reviewable as "all tests green, no behaviour diff", and it
gives slice 2 a clean home. Verified self-contained: the format-layer files reference **no**
PdfLibrary type (only `System.Text`, `System.Xml`, `System.Xml.Linq`, `Regex`, `Globalization`).

**Moves** (format — "what XMP is", no PDF knowledge):

| Type | Visibility | From |
|---|---|---|
| `XmpPacket`, `XmpProperty`, `XmpSchemas`, `XmpValueKind` | public | `PdfLibrary/Metadata/` |
| `XmpNode`, `XmpTreeParser`, `XmpDate` | internal | `PdfLibrary/Conformance/Xmp/` |

**Stays** (PDF/A's rules *about* XMP, not XMP itself): `XmpPredefinedSchemas`, `XmpStructTypes`,
`XmpTypeContainer`, `XmpExtensionSchemas`. These take a one-way reference to the new project.

The current split is an accident of history — the parser lives under `Conformance` only because
conformance needed it first, which is how the editing side ended up with a second, weaker model.

- New project targets `netstandard2.1`, matching `ICCSharp` and `FontParser`.
- **Public** types keep their namespace (`PdfLibrary.Metadata`) so source compatibility is
  unaffected — assembly names need not match namespaces.
- **Internal** types (`XmpNode`, `XmpTreeParser`, `XmpDate`) move to a clean namespace such as
  `PdfLibrary.Xmp`. Nothing outside the repo can see them, and leaving the parser under
  `…Conformance.Xmp` once it is no longer conformance-owned would re-create the confusion this
  slice exists to remove.
- Fan-in is 21 files (13 source, 8 test), all in-repo, all mechanical `using` additions. Pellucid
  consumes none of this — it defines its own `MinimalXmpPacket`.

### Binary compatibility — required, easy to forget

`PdfLibrary` ships as **`Lxman.PdfLibrary` 2.5.2** and four moved types are public. Moving a public
type across an assembly boundary is a binary-breaking change: anything compiled against 2.5.2 gets a
`TypeLoadException`, even though source still compiles.

`PdfLibrary` must therefore carry a type forwarder for each moved public type:

```csharp
[assembly: TypeForwardedTo(typeof(XmpPacket))]
[assembly: TypeForwardedTo(typeof(XmpProperty))]
[assembly: TypeForwardedTo(typeof(XmpSchemas))]
[assembly: TypeForwardedTo(typeof(XmpValueKind))]
```

**No test in this repo would catch their absence** — everything here recompiles from source. A test
must assert the forwarders exist (e.g. reflect over `PdfLibrary`'s
`GetForwardedTypes()`) so a later refactor cannot silently drop them.

## Slice 2 — one model, and the missing serializer

`XmpNode` is already recursive (`Children`, `IsStruct`, `IsArray`, `IsArrayOrdered`,
`IsArrayAlternate`, `IsArrayAltText`, `HasXmlLang`) and handles exactly what `XmpPacket` cannot. It
is parse-only.

**Do not build a second faithful model.** Two XMP parsers in one library will diverge, and the
failure mode is bad: conformance rules validating one interpretation of a packet while the writer
serializes another — "Pellucid says this conforms, then Pellucid's own save makes it not conform."

1. **`XmpNode` becomes the shared model.** `XmpPacket` keeps its entire public API — `SetSimple`,
   `SetArray`, `SetLangAlt`, `Remove`, `Serialize` — but is re-implemented as a facade over the node
   tree instead of the flat dictionary. `Kind`, `Value`, `Items` and `LangAlt` survive as computed
   projections. `UaTitleRule` is the only other consumer of those members and is untouched.
2. **Write the serializer** — the actual new code. Recursive emit: structs as
   `rdf:parseType="Resource"`, arrays as `rdf:Seq`/`Bag`/`Alt`, `xml:lang` on alt-text items. It must
   collect every namespace used anywhere in the tree, **including struct-field namespaces such as
   `stEvt:`**, and declare them on the `rdf:Description` — a field namespace can appear that no
   top-level property uses.
3. **Verbatim fallback.** Any shape the model cannot represent is retained as its raw subtree and
   re-emitted unchanged. A model that loses data on meeting the unfamiliar is what caused this bug.
   Preserved subtrees must carry their own namespace declarations, so prefix rewriting elsewhere in
   the packet cannot leave them dangling.
4. **Struct authoring** — setters for structs and arrays-of-structs, sufficient for a later
   `pdfaExtension:schemas` block.

### Known limitation, accepted

Neither model represents general XMP **qualifiers**; both special-case `xml:lang`. The conformance
side reaches veraPDF parity without them, so they are rare enough in practice. Recorded rather than
built. The verbatim fallback covers any qualifier-bearing shape encountered.

## Testing

The gap that let this ship is that no test used a struct.

- **Round-trip fidelity**: `parse → serialize → parse` yields an equal tree, over real Adobe packets
  exercising `xmpMM:History`, `xmpMM:DerivedFrom`, `xmpTPg:Fonts` and `xmpTPg:MaxPageSize`.
  Compare **trees, not bytes** — attribute-form and element-form are equivalent RDF, so a byte
  comparison would fail for correct output.
- **Golden test** on the packet from `0000_0000007.pdf` (Illustrator 25.2), asserting `stEvt:action`,
  `stEvt:when` and `softwareAgent` survive *by name*. This is the assertion the diagnostic probe
  failed against current code.
- **All 36 existing XMP tests keep their assertions unchanged.** Slice 1 may add or rewrite `using`
  directives in those files — that is the mechanical cost of the move. What must not change is a
  single assertion or fixture: if slice 2 needs one relaxed, the facade is wrong and the projection
  is not faithful.
- **Sabotage check**: remove the struct branch and confirm the new tests go red, so they cannot pass
  for the wrong reason.
- **Type-forwarder test** (slice 1), per above.
- Conformance suite stays green throughout — the parser is veraPDF-parity-verified and slice 1 must
  not perturb it.

## Deferred, deliberately

- **`MetadataDomain`'s wholesale packet replacement** (Pellucid). Once the engine round-trips
  honestly, the right behaviour is likely "load the packet, set the fields, save" rather than "build a
  minimal packet". Pellucid-side decision with its own reasoning; bundling it here would muddy the
  engine slice.
- **The PDF/A XMP remediation program.** Its open questions — the real-description table for the
  head of the distribution, boilerplate for the tail, and strip-vs-declare for
  `pdfa-xmp-property-type` — are independent of how the model is housed. Own spec, after authoring
  exists.
- **Unifying `XmpNode` with a general qualifier model.** Not required by anything today.

## Definition of done

1. `Xmp` project exists; format layer moved; namespaces unchanged; type forwarders in place and
   tested.
2. A real Illustrator packet survives `parse → serialize` with every struct field intact, by name.
3. All 36 pre-existing XMP tests pass with **assertions unchanged** (`using` edits allowed); the
   conformance suite stays green.
4. New tests fail when the struct handling is reverted.
