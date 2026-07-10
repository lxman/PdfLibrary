# PDF/UA (Matterhorn Protocol 1.1) — machine-checkability map & Focal coverage
> Derived 2026-07-10 from the **Matterhorn Protocol 1.1** (© 2021 PDF Association, PDF/UA TWG), licensed **CC-BY-4.0** (<http://creativecommons.org/licenses/by/4.0/>). This is Focal's own coverage matrix; the source PDF is not vendored. See <https://pdfa.org/resource/the-matterhorn-protocol/>.

**139 failure conditions** across 31 checkpoints — **87 machine-checkable (M)**, 48 human-judgment (H), 4 unclassified. M is the automatable ceiling for Focal (and veraPDF). `Focal` column: rule id if covered, else `—`.

Status legend — Focal rule that implements the condition, or `—` (not yet), `n/a` (human-only).

## Focal coverage — 32 of 87 machine-checkable conditions

Populated 2026-07-10 by mapping each condition to the rule whose logic actually detects that failure (not merely a matching ISO clause label). Per checkpoint (covered / machine):

| CP | Area | Covered | Rules |
|---|---|--:|---|
| 01 | Real content tagged | 3/4 | `ua-content-tagged`, `ua-artifact-nesting` |
| 02 | Role mapping | 1/3 | `ua-standard-type` |
| 06 | Metadata | 3/3 | `ua-title`, `ua-identification` |
| 07 | Dictionary | 2/2 | `ua-display-doc-title` |
| 09 | Appropriate tags | 3/5 | `ua-structure-nesting` (table/list/TOC; not Ruby/Warichu) |
| 10 | Character mappings | 1/1 | `ua-text-unicode` |
| 11 | Natural language | 2/6 | `ua-attribute-lang` (Alt/ActualText/E), `ua-object-lang` (outline) |
| 13 | Graphics | 1/1 | `ua-figure-alt` |
| 15 | Tables | 1/1 | `ua-table-regular` |
| 17 | Math | 1/2 | `ua-figure-alt` (Formula Alt) |
| 25 | XFA | 1/1 | `ua-xfa` |
| 31 | Fonts | 13/29 | `font-dictionary`, `font-embedded`, `font-program` |

**Detectors with no *discrete* Matterhorn M condition** (they detect real ISO 14289-1 failures veraPDF also flags, but Matterhorn does not enumerate them as numbered conditions, so they appear nowhere in the column): `ua-tagged` — the document-level Tagged-PDF gate (catalog `/StructTreeRoot` + `/MarkInfo /Marked true`); `ua-language-tag` — `/Lang` BCP-47 syntax validity wherever a `/Lang` appears.

**Known gaps left `—` on purpose:**
- **31-003** (7.21.3.1-1, CIDSystemInfo `/Supplement`): `font-dictionary` *does* compare Supplement, but in the **opposite direction** from this Matterhorn condition — it flags CIDFont Supplement *greater than* the CMap's, matching **veraPDF's** 6.2.11.3.1 / 7.21.3.1 rule (veraPDF's own pass fixture embeds a CMap whose Supplement exceeds a subset CIDFont's and is conformant; flagging Matterhorn's stated direction, CIDFont *less than* the CMap's, false-positives on that fixture — verified). Focal follows the reference validator, so 31-003 as literally worded is not what Focal detects; left `—`. This is a documented veraPDF-vs-Matterhorn discrepancy, not a Focal bug. Registry (31-001) and Ordering (31-002) equality checks are correct.
- **7.21.4.2** (31-012…015, CharSet/CIDSet), **7.21.7** (31-027…029, ToUnicode), and the program-cmap parts of 7.21.6 (31-017/018/023/025/026) need engine-side font-program inspection Focal does not yet do.

## Checkpoint 01 — Real content tagged  (4 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 01-001 | H | 7.1-1 | Artifact is tagged as real content. | n/a |
| 01-002 | H | 7.1-1 | Real content is marked as artifact. | n/a |
| 01-003 | M | 7.1-1 | Content marked as Artifact is present inside tagged content. | ua-artifact-nesting |
| 01-004 | M | 7.1-1 | Tagged content is present inside content marked as Artifact. | ua-artifact-nesting |
| 01-005 | M | 7.1-2 | Content is neither marked as Artifact nor tagged as real content. | ua-content-tagged |
| 01-006 | H | 7.1-2 | The structure type and attributes of a structure element are not | n/a |
| 01-007 | M | 7.1-11 | Suspects entry has a value of true. | — |

## Checkpoint 02 — Role Mapping  (3 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 02-001 | M | 7.1-3 | One or more non-standard tag’s mapping does not | ua-standard-type |
| 02-002 | H | 7.1-3 | The mapping of one or more non-standard types is | n/a |
| 02-003 | M | 7.1-3 | A circular mapping exists | — |
| 02-004 | M | 7.1-4 | One or more standard types are remapped. | — |

## Checkpoint 03 — Flickering  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 03-001 | H | 7.1-5 | One or more Actions lead to flickering. | n/a |
| 03-002 | H | 7.1-5 | One or more multimedia objects contain flickering content. | n/a |
| 03-003 | H | 7.1-5 | One or more JavaScript actions lead to flickering. | n/a |

## Checkpoint 04 — Color and Contrast  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 04-001 | H | 7.1-6 | Information is conveyed by contrast, color, format or | n/a |

## Checkpoint 05 — Sound  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 05-001 | H | 7.1-7 | Media annotation present, but audio content not | n/a |
| 05-002 | H | 7.1-7 | Audio annotation present, but content not | n/a |
| 05-003 | H | 7.1-7 | JavaScript uses beep function but does not | n/a |

## Checkpoint 06 — Metadata  (3 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 06-001 | M | 7.1-8 | Document does not contain an XMP metadata | ua-title |
| 06-002 | M | 5 | The XMP metadata stream in the Catalog | ua-identification |
| 06-003 | M | 7.1-8 | XMP metadata stream does not contain dc:title | ua-title |
| 06-004 | H | 7.1-8 | dc:title does not clearly identify the document | n/a |

## Checkpoint 07 — Dictionary  (2 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 07-001 | M | 7.1-9 | ViewerPreferences dictionary of the Catalog | ua-display-doc-title |
| 07-002 | M | 7.1-9 | ViewerPreferences dictionary of the Catalog | ua-display-doc-title |

## Checkpoint 08 — OCR Validation  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 08-001 | H | 7.1-10 | OCR-generated text contains significant errors. | n/a |
| 08-002 | H | 7.1-10 | OCR-generated text is not tagged | n/a |

## Checkpoint 09 — Appropriate Tags  (5 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 09-001 | H | 7.2-1 | Tags are not in logical reading order. | n/a |
| 09-002 | H | 7.2-1 | Structure elements are nested in a semantically | n/a |
| 09-003 | H | 7.2-1 | The structure type (after applying any role- | n/a |
| 09-004 | M | 7.2-1 | A table-related structure element is used in a way | ua-structure-nesting |
| 09-005 | M | 7.2-1 | A list-related structure element is used in a way | ua-structure-nesting |
| 09-006 | M | 7.2-1 | A TOC-related structure element is used in a way | ua-structure-nesting |
| 09-007 | M | 7.2-1 | A Ruby-related structure element is used in a way | — |
| 09-008 | M | 7.2-1 | A Warichu-related structure element is used in | — |

## Checkpoint 10 — Character Mappings  (1 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 10-001 | M | 7.2-2 | Character code cannot be mapped to Unicode. | ua-text-unicode |

## Checkpoint 11 — Declared Natural Language  (6 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 11-001 | M | 7.2-3 | Natural language for text in page content cannot be | — |
| 11-002 | M | 7.2-3 | Natural language for text in Alt, ActualText and | ua-attribute-lang |
| 11-003 | M | 7.2-3 | Natural language in the Outline entries cannot be | ua-object-lang |
| 11-004 | M | 7.2-3 | Natural language in the Contents entry for annotations | — |
| 11-005 | M | 7.2-3 | Natural language in the TU entry for form fields cannot | — |
| 11-006 | M | 7.2-3 | Natural language for document metadata cannot be | — |
| 11-007 | H | 7.2-3 | Natural language is not appropriate. | n/a |

## Checkpoint 12 — Stretchable Characters  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 12-001 | H | 7.2-4 | Stretched characters are not represented appropriately. | n/a |

## Checkpoint 13 — Graphics  (1 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 13-001 | H | 7.3-1 | Graphics objects other than text objects and artifacts | n/a |
| 13-002 | H | 7.3-1 | A link with a meaningful background does not include | n/a |
| 13-003 | H | 7.3-2 | A caption is not tagged with a <Caption> tag. | n/a |
| 13-004 | M | 7.3-3 | <Figure> tag alternative or replacement text missing. | ua-figure-alt |
| 13-005 | H | 7.3-4 | ActualText used for a <Figure> for which alternative | n/a |
| 13-006 | H | 7.3-5 | Graphics objects that possess semantic value only | n/a |
| 13-007 | H | 7.3-6 | A more accessible representation is not used. | n/a |
| 13-008 | H | 7.3-4 | ActualText not present when a <Figure> is intended to | n/a |

## Checkpoint 14 — Headings  (4 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 14-001 | H | 7.4-1 | Headings are not tagged. | n/a |
| 14-002 | M | 7.4.2-1 | Does use numbered headings, but the first | — |
| 14-003 | M | 7.4-1 | Numbered heading levels in descending | — |
| 14-004 | H | 7.4.3-1 | Numbered heading tags do not use Arabic | n/a |
| 14-005 | H | 7.4.3-1 | Content representing a 7th level (or higher) | n/a |
| 14-006 | M | 7.4.4-1 | A node contains more than one <H> tag. | — |
| 14-007 | M | 7.4.4-3 | Document uses both <H> and <H#> tags. | — |

## Checkpoint 15 — Tables  (1 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 15-001 | H | 7.5-1 | A row has a header cell, but that header cell is not | n/a |
| 15-002 | H | 7.5-1 | A column has a header cell, but that header cell is | n/a |
| 15-003 | M | 7.5-2 | In a table not organized with Headers attributes | ua-table-regular |
| 15-004 | H | 7.5-3 | Content is tagged as a table for information that is | n/a |
| 15-005 | H | 7.5-2 | A given cell’s header cannot be unambiguously | n/a |

## Checkpoint 16 — Lists  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 16-001 | H | 7.6-1 | List is an ordered list, but no value for the | n/a |
| 16-002 | H | 7.6-1 | List is an ordered list, but the ListNumbering | n/a |
| 16-003 | H | 7.6-2 | Content is a list but is not tagged as a list. | n/a |

## Checkpoint 17 — Mathematical Expressions  (2 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 17-001 | H | 7.7-1 | Content is a mathematical expression but is not | n/a |
| 17-002 | M | 7.7-1 | <Formula> tag is missing an Alt attribute. | ua-figure-alt |
| 17-003 | M | 7.7-2 | Unicode mapping requirements are not met. | — |

## Checkpoint 18 — Page Headers and Footers  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 18-001 | H | 7.8-1 | Headers and footers are not marked as pagination | n/a |
| 18-002 | H | 7.8-1 | Header or footer artifacts are not classified as | n/a |

## Checkpoint 19 — Notes and References  (2 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 19-001 | H | 7.9-1 | Footnotes or endnotes are not tagged as <Note>. | n/a |
| 19-002 | H | 7.9-1 | References are not tagged as <Reference>. | n/a |
| 19-003 | M | 7.9-2 | ID entry of the <Note> tag is not present. | — |
| 19-004 | M | 7.9-2 | ID entry of the <Note> tag is non-unique. | — |

## Checkpoint 20 — Optional Content  (3 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 20-001 | M | 7.10-1 | Name entry is missing or has an empty string as | — |
| 20-002 | M | 7.10-1 | Name entry is missing or has an empty string as | — |
| 20-003 | M | 7.10-2 | An AS entry appears in an Optional Content | — |

## Checkpoint 21 — Embedded Files  (1 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 21-001 | M | 7.11-1 | The file specification dictionary for an embedded | — |

## Checkpoint 22 — Article Threads  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 22-001 | H | 7.12-1 | Article threads do not reflect logical reading order. | n/a |

## Checkpoint 23 — Digital Signatures  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 23-001 | ? | 7.13-1 | No test specific to digital signatures is required, | — |

## Checkpoint 24 — Non-Interactive Forms  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 24-001 | H | 7.14-1 | Non-interactive forms are not tagged with the | n/a |

## Checkpoint 25 — XFA  (1 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 25-001 | M | 7.15-1 | File contains the dynamicRender element with | ua-xfa |

## Checkpoint 26 — Security  (2 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 26-001 | M | 7.16-1 | The file is encrypted but does not contain a P | — |
| 26-002 | M | 7.16-1 | The file is encrypted and does contain a P entry | — |

## Checkpoint 27 — Navigation  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 27-001 | ? | 7.17-1 | No tests specific to navigation are required; use | — |

## Checkpoint 28 — Annotations  (15 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 28-001 | H | 7.18.1-2 | An annotation is not in correct reading order. | n/a |
| 28-002 | M | 7.18.1-2 | An annotation, other than of subtype Widget, Link | — |
| 28-018 | ? |  |  | — |
| 28-003 | H | 7.18.1-3 | An annotation is used for visual formatting but is | n/a |
| 28-004 | M | 7.18.1-4 | An annotation, other than of subtype Widget, does | — |
| 28-005 | M | 7.18.1-4 | A form field does not have a TU entry and does not | — |
| 28-006 | M | 7.18.2-1 | An annotation with subtype undefined in ISO 32000 | — |
| 28-004 | ? |  |  | — |
| 28-007 | M | 7.18.2-2 | An annotation of subtype TrapNet exists. | — |
| 28-008 | M | 7.18.3-1 | A page containing an annotation does not contain a | — |
| 28-009 | M | 7.18.3-1 | A page containing an annotation has a Tabs entry | — |
| 28-010 | M | 7.18.4-1 | A widget annotation is not nested within a <Form> tag. | — |
| 28-011 | M | 7.18.5-1 | A link annotation is not nested within a <Link> tag. | — |
| 28-012 | M | 7.18.5-2 | A link annotation does not include an alternate | — |
| 28-013 | H | 7.18.5-3 | An IsMap entry is present with a value of true but | n/a |
| 28-014 | M | 7.18.6.2-1 | CT entry is missing from the media clip data | — |
| 28-015 | M | 7.18.6.2-1 | Alt entry is missing from the media clip data | — |
| 28-016 | M | 7.18.7-1 | File attachment annotations do not conform | — |
| 28-017 | M | 7.18.8-1 | A PrinterMark annotation is included in the logical | — |
| 28-018 | M | 7.18.8-2 | The appearance stream of a PrinterMark | — |

## Checkpoint 29 — Actions  (0 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 29-001 | H | 7.19-1 | A script requires specific timing for individual | n/a |

## Checkpoint 30 — XObjects  (2 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 30-001 | M | 7.20-1 | A reference XObject is present. | — |
| 30-002 | M | 7.20-2 | Form XObject contains MCIDs and is referenced | — |

## Checkpoint 31 — Fonts  (29 machine)

| ID | M/H | ISO 14289-1 | Failure condition | Focal |
|---|---|---|---|---|
| 31-001 | M | 7.21.3-1 | A Type 0 font dictionary with encoding other than | font-dictionary |
| 31-002 | M | 7.21.3.1-1 | A Type 0 font dictionary with encoding other than | font-dictionary |
| 31-003 | M | 7.21.3.1-1 | A Type 0 font dictionary with encoding other | — |
| 31-004 | M | 7.21.3.2-1 | A Type 2 CID font contains neither a stream nor the | font-dictionary |
| 31-005 | M | 7.21.3.2-1 | A Type 2 CID font does not contain a CIDToGIDMap | font-dictionary |
| 31-006 | M | 7.21.3.3-1 | A CMap is neither listed as described in ISO 32000- | font-dictionary |
| 31-007 | M | 7.21.3.3-1 | The WMode entry in a CMap dictionary is not | — |
| 31-008 | M | 7.21.3.3-2 | A CMap references another CMap which is not | — |
| 31-009 | M | 7.21.4.1-1 | For a font used by text intended to be rendered the | font-embedded |
| 31-010 | H | 7.21.4.1-2 | A font program is embedded that is not legally | n/a |
| 31-011 | M | 7.21.4.1-3 | For a font used by text the font program is | — |
| 31-012 | M | 7.21.4.2-1 | The FontDescriptor dictionary of an embedded | — |
| 31-013 | M | 7.21.4.2-2 | The FontDescriptor dictionary of an embedded | — |
| 31-014 | M | 7.21.4.2-3 | The FontDescriptor dictionary of an embedded | — |
| 31-015 | M | 7.21.4.2-4 | The FontDescriptor dictionary of an embedded | — |
| 31-016 | M | 7.21.5-1 | For one or more glyphs, the glyph width | font-program |
| 31-017 | M | 7.21.6-1 | A non-symbolic TrueType font is used for | — |
| 31-018 | M | 7.21.6-2 | A non-symbolic TrueType font is used for rendering, but | — |
| 31-019 | M | 7.21.6-3 | The font dictionary for a non-symbolic TrueType | font-dictionary |
| 31-020 | M | 7.21.6-4 | The font dictionary for a non-symbolic TrueType | font-dictionary |
| 31-021 | M | 7.21.6-5 | The value for either the Encoding entry or the | font-dictionary |
| 31-022 | M | 7.21.6-6 | The Differences array in the Encoding entry in a | font-dictionary |
| 31-023 | M | 7.21.6-7 | The Differences array is present in the Encoding | — |
| 31-024 | M | 7.21.6-8 | The Encoding entry is present in the font | font-dictionary |
| 31-025 | M | 7.21.6-9 | The embedded font program for a symbolic | — |
| 31-026 | M | 7.21.6-10 | The embedded font program for a symbolic | — |
| 31-027 | M | 7.21.7-1 | A font dictionary does not contain the ToUnicode | — |
| 31-028 | M | 7.21.7-2 | One or more Unicode values specified in the | — |
| 31-029 | M | 7.21.7-3 | One or more Unicode values specified in the | — |
| 31-030 | M | 7.21.8-1 | One or more characters used in text showing | font-program |
