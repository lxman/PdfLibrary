# xmp-oracle — ask Adobe's XMPCore what a packet means

A second opinion on XMP round-trip questions, for shapes **no document in either corpus contains**.

```
tools/xmp-oracle/xmp-oracle.sh <file.xmp>              # properties + shape flags
tools/xmp-oracle/xmp-oracle.sh --serialize <file.xmp>  # ...and Adobe's own re-serialization
echo '<x:xmpmeta …>' | tools/xmp-oracle/xmp-oracle.sh -
```

First run fetches the jar from Maven Central into `lib/` and compiles the driver into `out/`; both are
git-ignored. Needs a JDK (a JRE cannot compile the driver). Works from Git Bash on Windows — it
converts paths for a Windows JVM, without which java reports the unhelpful "Could not find or load
main class" rather than anything about paths.

## Why this exists

The XMP round-trip program (`Docs/superpowers/specs/2026-08-13-xmp-round-trip-fidelity-design.md`)
closed ten defects. The last one, **D10, could not be justified by any gate we own**: the attribute
form of `rdf:value` appears in **0 of 2,907** veraPDF corpus documents and **0 of 701** real-world
ones, so every suite stayed green whichever way the code behaved. The only available evidence was the
spec text and, later, a second implementation's answer. This tool makes asking one routine instead of
an afternoon's work.

## Why Adobe's *Java* XMPCore and not the C++ toolkit

**Because it is the ancestor of the oracle we are actually held to.** veraPDF's XMP implementation is
a repackaged fork of this library — verified, not assumed: all 31 classes of
`com.adobe.internal.xmp` appear in veraPDF's `org.verapdf.xmp` (which adds 5 of its own). Since
parity with veraPDF outranks the published spec for this subsystem, the Java line is the closer
relative. The C++ toolkit (`adobe/XMP-Toolkit-SDK`) is a sibling, and buying it costs an afternoon:
VS2022 + CMake, and hand-vendoring pinned-old expat 2.5.0 and zlib 1.2.13, with no vcpkg port and no
prebuilt binaries.

`com.adobe.xmp:xmpcore` **6.1.11**, whose jar manifest declares `Bundle-License: BSD-3-Clause`. Note
its classes are dated 2020-11-17 and built for JDK 8 — the Java line has been dormant since then,
while the C++ line still ships (v2025.03). For grammar questions that is not a problem: Annex C has
not changed.

**Oracle only — never vendored, never shipped, not referenced by any project.** The jar is downloaded
on demand and git-ignored.

## Reading the output

One tab-separated line per property: path, value, then only the shape flags that bear on round-trip
questions (`URI`, `simple`/`struct`/`array`/`ordered`/`alt`/`altText`, `hasQualifiers`,
`isQualifier`, `xml:lang`).

A packet Adobe refuses prints `PARSE-ERROR<TAB>reason` and exits 0. **A refusal is an answer.** This
engine is deliberately tolerant — an unparseable packet yields an empty property list, never a throw
— so a disagreement here is often the finding itself rather than a tool failure.

## Two traps, learned the first time this was run

**Pick a neutral namespace for shape probes.** Adobe's schema registry knows the standard
vocabularies, so a fixture using `dc:relation` came back as an *array* — `dc:relation` is a bag in the
DC schema and the registry coerced it. The shape being probed was not the shape being reported. Use
`ex:` in a namespace nobody has registered.

**One bad property aborts the whole packet.** Adobe refuses the entire document over a single
malformed production, so a fixture holding several shapes reports only the first failure. Probe one
shape per packet.

## What it found on its first run (2026-08-14)

Adobe agrees with the post-D10 engine on C.2.12's mapping rules 1, 2, 3 and 4 — including rule 1 in
attribute position, the case the fix turned on. On the rule-ordering case, though, three
implementations give three answers:

| `<ex:Prop rdf:value="V" rdf:resource="U"/>` | Verdict |
|---|---|
| Adobe XMPCore 6.1.11 | **refuses the entire packet** — "Empty property element can't have both rdf:value and rdf:resource" |
| ExifTool 12.76 | `V` — rule 1 wins |
| PdfLibrary, after D10 | `V`, and the packet captured verbatim so neither attribute is lost |
| PdfLibrary, before D10 | `U` — rule 2 won, which no one else agrees with |

The old behaviour was wrong by every account. The new one is the most tolerant reading that still
honours C.2.12's stated rule ORDER, which is the right posture for a validator that must not lose a
document's remaining metadata over one malformed property.

A consequence worth remembering: **veraPDF runs this same code**, so it would refuse such a packet
outright and report the metadata as invalid where we parse it happily. That is under-reporting
relative to veraPDF — the safe direction under the no-false-positive contract, and the tolerant-parse
contract working exactly as designed.

Adobe also re-serializes `<ex:Prop rdf:value="V"/>` as a plain `ex:Prop="V"` attribute, dropping the
`rdf:value` spelling entirely. It agrees with this engine that the form the value ARRIVED in is not
itself information — we write element text where Adobe writes an attribute, but neither preserves the
`rdf:value` production, and both keep the value.
