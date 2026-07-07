# Conformance Rule Catalog — PDF/A-2b / 2u / 3b (+ PDF/X-4)

_2026-07-06. The source of truth for the preflight's rules. Section 2 is the **complete** rule set
for our three PDF/A flavours, reconciled 1:1 with the veraPDF combined validation profiles
(`../veraPDF-validation-profiles/PDF_A/PDFA-{2B,2U,3B}.xml`). Work the structural slices from this
document, not from memory._

Provenance: rule identity (clause + test number), applicability, and the requirement each rule
encodes are **facts from ISO 19005**, surfaced via veraPDF's machine-readable profiles (CC BY 4.0).
We reimplement each independently against our own engine and write our own messages — we do not
copy veraPDF code or ship their strings. See the read-API map in
`2026-07-06-conformance-preflight-read-api-audit.md`.

## How to read Section 2

- **Rule** = `clause-tN`, the ISO 19005 clause and veraPDF test number (stable identifier).
- **Object** = the veraPDF model type the rule targets (`CosTrailer`, `PDFont`, …); it tells you
  *what* to inspect. We map it to our engine (trailer, `PdfFont`, resources, …) — see the read-API map.
- **Profiles** = which flavours include the rule. `2b/2u/3b` = all three; `3b` = added by PDF/A-3;
  `2b/2u` = dropped/relaxed in PDF/A-3.
- **Test expr** = veraPDF's boolean condition for *conformance* (true = passes). It documents the
  exact check; we express the equivalent over our model.
- **Merge caveat:** where a rule's literal differs by flavour, the Test expr shows the **2b** value.
  Known cases: `6.6.4-t2` (`pdfaid:part` = 2 for 2b/2u, **3** for 3b) and `6.6.4-t3`
  (`pdfaid:conformance` = B/U for A-2, B/U for A-3). The clause/requirement is otherwise identical.

## Reconciliation: original 15-rule hand catalog → veraPDF set

The first-pass hand catalog captured only the headline structural rules. The veraPDF set is far more
granular (149 rules). Mapping, so nothing is lost:

| Original hand rule | veraPDF clause(s) | Note |
|---|---|---|
| 1 No `/Encrypt` | **6.1.3-t2** | ✅ slice 1 (`encrypt`) |
| 2 Trailer `/ID` | **6.1.3-t1** | ✅ slice 1 (`file-id`) — **refine**: veraPDF is "present & non-empty" (`lastID != null && length>0`), not "exactly two strings". Relax our rule to match; demote the two-strings/type check to a Warning. |
| 3 Fonts embedded | 6.2.11.4.x | in 6.2 (fonts subgroup) |
| 4 ≥1 OutputIntent w/ profile | 6.2.3-t1, **conditional via 6.2.4** | Not a blanket rule — output-intent presence is required only when device colour is used (6.2.4); 6.2.3 governs intent *contents*. |
| 5 Device colours need intent | 6.2.4.3-tX | uncalibrated Device colour spaces |
| 6 No JavaScript / bad actions | 6.5-tX (Actions) | + 6.6 metadata for actions in some cases |
| 7 XMP `/Metadata` present | 6.6.x | metadata well-formedness subgroup |
| 8 `pdfaid` part+conformance | **6.6.4-t1..t7** | full pdfaid schema (part, conformance, prefixes) |
| 9 No LZW / forbidden filters | **6.1.7.2-t1** (stream filters) + **6.1.10-t1** (inline image) | filter whitelist; LZW/Crypt excluded |
| 10 Annotation appearances | 6.3.x | annotation types / dicts / appearances |
| 11 No embedded files (2b/2u) | **6.8-t5** (must itself be PDF/A) + 6.8-t2 (F/UF) | 3b relaxes: 6.8-t1/t3/t4 instead |
| 12 Text → Unicode (2u) | **6.2.11.7.2-t1/t2** | the A-2u delta |
| 13 OutputIntent subtype GTS_PDFX | **6.2.3-t3** | PDF/A rule about PDF/X output intents |
| 14 Page TrimBox/ArtBox | — | **PDF/X-4 only** — see §3, not a PDF/A rule |
| 15 `/Trapped` set | — | **PDF/X-4 only** — see §3 |

**Net new from veraPDF (not in the hand catalog):** file header format (6.1.2), xref/indirect
formatting (6.1.4/6.1.9), hex-string & UTF-8-name well-formedness (6.1.6/6.1.8), stream
`Length`/EOL/`F`,`FFilter`,`FDecodeParms` (6.1.7), **post-EOF data** (6.1.3-t3 — the refinement to
fold in), permissions dict (6.1.12), implementation limits (6.1.13: int/real ranges, string/name
length, q/Q nesting, DeviceN colourants, CID, page-box size), plus the whole depth of 6.2 Graphics
(58 rules: fonts, colour spaces, transparency, ExtGState, XObjects) and 6.6 Metadata (33 rules).

## 2. Full rule set (149 rules)
_149 distinct rules across PDF/A-2b/2u/3b (union of the combined veraPDF profiles). Profiles column: which flavours include the rule._


### 6.1 File structure (27 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.1.2-t1 | CosDocument | 2b/2u/3b | The file header shall begin at byte zero and shall consist of "%PDF-1.n" followed by a si… | `headerOffset == 0 && /^%PDF-1\.[0-7]$/.test(header)` |
| 6.1.2-t2 | CosDocument | 2b/2u/3b | The aforementioned EOL marker shall be immediately followed by a % (25h) character follow… | `headerByte1 > 127 && headerByte2 > 127 && headerByte3 > 127…` |
| 6.1.3-t1 | CosDocument | 2b/2u/3b | The file trailer dictionary shall contain the ID keyword whose value shall be File Identi… | `lastID != null && lastID.length() > 0` |
| 6.1.3-t2 | CosTrailer | 2b/2u/3b | The keyword Encrypt shall not be used in the trailer dictionary | `isEncrypted != true` |
| 6.1.3-t3 | CosDocument | 2b/2u/3b | No data can follow the last end-of-file marker except a single optional end-of-line marke… | `postEOFDataSize == 0` |
| 6.1.4-t2 | CosXRef | 2b/2u/3b | The xref keyword and the cross-reference subsection header shall be separated by a single… | `xrefEOLMarkersComplyPDFA` |
| 6.1.6-t1 | CosString | 2b/2u/3b | Hexadecimal strings shall contain an even number of non-white-space characters | `(isHex != true) \|\| hexCount % 2 == 0` |
| 6.1.6-t2 | CosString | 2b/2u/3b | A hexadecimal string is written as a sequence of hexadecimal digits (0–9 and either A–F o… | `(isHex != true) \|\| containsOnlyHex == true` |
| 6.1.7.1-t1 | CosStream | 2b/2u/3b | The value of the Length key specified in the stream dictionary shall match the number of … | `Length == realLength` |
| 6.1.7.1-t2 | CosStream | 2b/2u/3b | The stream keyword shall be followed either by a CARRIAGE RETURN (0Dh) and LINE FEED (0Ah… | `streamKeywordCRLFCompliant == true && endstreamKeywordEOLCo…` |
| 6.1.7.1-t3 | CosStream | 2b/2u/3b | A stream dictionary shall not contain the F, FFilter, or FDecodeParms keys | `F == null && FFilter == null && FDecodeParms == null` |
| 6.1.7.2-t1 | CosFilter | 2b/2u/3b | All standard stream filters listed in ISO 32000-1:2008, 7.4, Table 6 may be used, with th… | `internalRepresentation == "ASCIIHexDecode" \|\| internalRep…` |
| 6.1.8-t1 | CosUnicodeName | 2b/2u/3b | Font names, names of colourants in Separation and DeviceN colour spaces, and structure ty… | `isValidUtf8 == true` |
| 6.1.9-t1 | CosIndirect | 2b/2u/3b | The object number and generation number shall be separated by a single white-space charac… | `spacingCompliesPDFA` |
| 6.1.10-t1 | CosIIFilter | 2b/2u/3b | The value of the F key in the Inline Image dictionary shall not be LZW, LZWDecode, Crypt,… | `internalRepresentation == "ASCIIHexDecode" \|\| internalRep…` |
| 6.1.12-t1 | PDPerms | 2b/2u/3b | No keys other than UR3 and DocMDP shall be present in a permissions dictionary (ISO 32000… | `entries.split('&').filter(elem => elem != 'UR3' && elem != …` |
| 6.1.12-t2 | PDSigRef | 2b/2u/3b | If DocMDP is present, then the Signature References dictionary (ISO 32000-1:2008, 12.8.1,… | `permsContainDocMDP == false \|\| entries.split('&').filter(…` |
| 6.1.13-t1 | CosInteger | 2b/2u/3b | A conforming file shall not contain any integer greater than 2147483647. A conforming fil… | `(intValue <= 2147483647) && (intValue >= -2147483648)` |
| 6.1.13-t2 | CosReal | 2b/2u/3b | A conforming file shall not contain any real number outside the range of +/-3.403 x 10^38 | `(realValue >= -3.403e+38) && (realValue <= 3.403e+38)` |
| 6.1.13-t3 | CosString | 2b/2u/3b | A conforming file shall not contain any string longer than 32767 bytes | `value.length() < 32768` |
| 6.1.13-t4 | CosName | 2b/2u/3b | A conforming file shall not contain any name longer than 127 bytes | `internalRepresentation.length() <= 127` |
| 6.1.13-t5 | CosReal | 2b/2u/3b | A conforming file shall not contain any real number closer to zero than +/-1.175 x 10^(-3… | `realValue == 0.0 \|\| (realValue <= -1.175e-38) \|\| (realV…` |
| 6.1.13-t7 | CosDocument | 2b/2u/3b | A conforming file shall not contain more than 8388607 indirect objects | `nrIndirects <= 8388607` |
| 6.1.13-t8 | Op_q_gsave | 2b/2u/3b | A conforming file shall not nest q/Q pairs by more than 28 nesting levels | `nestingLevel <= 28` |
| 6.1.13-t9 | PDDeviceN | 2b/2u/3b | A conforming file shall not contain a DeviceN colour space with more than 32 colourants | `nrComponents <= 32` |
| 6.1.13-t10 | CMapFile | 2b/2u/3b | A conforming file shall not contain a CID value greater than 65535 | `maximalCID <= 65535` |
| 6.1.13-t11 | CosBBox | 2b/2u/3b | The size of any of the page boundaries described in ISO 32000-1:2008, 14.11.2 shall not b… | `Math.abs(top - bottom) >= 3 && Math.abs(top - bottom) <= 14…` |

### 6.2 Graphics (58 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.2.2-t1 | Op_Undefined | 2b/2u/3b | Content streams shall not contain any operators not defined in ISO 32000-1 even if such o… | `false` |
| 6.2.2-t2 | PDContentStream | 2b/2u/3b | A content stream that references other objects, such as images and fonts that are necessa… | `inheritedResourceNames == ''` |
| 6.2.3-t1 | ICCOutputProfile | 2b/2u/3b | The profile stream that is the value of the DestOutputProfile key shall either be an outp… | `(deviceClass == "prtr" \|\| deviceClass == "mntr") && (colo…` |
| 6.2.3-t2 | OutputIntents | 2b/2u/3b | If a file's OutputIntents array contains more than one entry, as might be the case where … | `sameOutputProfileIndirect == true` |
| 6.2.3-t3 | PDOutputIntent | 2b/2u/3b | In addition, the DestOutputProfileRef key, as defined in ISO 15930-7:2010, Annex A, shall… | `S != 'GTS_PDFX' \|\| containsDestOutputProfileRef == false` |
| 6.2.4.2-t1 | ICCInputProfile | 2b/2u/3b | The profile that forms the stream of an ICCBased colour space shall conform to ICC.1:1998… | `(deviceClass == "prtr" \|\| deviceClass == "mntr" \|\| devi…` |
| 6.2.4.2-t2 | PDICCBasedCMYK | 2b/2u/3b | Overprint mode (as set by the OPM value in an ExtGState dictionary) shall not be one (1) … | `overprintFlag == false \|\| OPM == 0` |
| 6.2.4.3-t2 | PDDeviceRGB | 2b/2u/3b | DeviceRGB shall only be used if a device independent DefaultRGB colour space has been set… | `gOutputCS != null && gOutputCS == "RGB "` |
| 6.2.4.3-t3 | PDDeviceCMYK | 2b/2u/3b | DeviceCMYK shall only be used if a device independent DefaultCMYK colour space has been s… | `gOutputCS != null && gOutputCS == "CMYK"` |
| 6.2.4.3-t4 | PDDeviceGray | 2b/2u/3b | DeviceGray shall only be used if a device independent DefaultGray colour space has been s… | `gOutputCS != null` |
| 6.2.4.4-t1 | PDDeviceN | 2b/2u/3b | For any spot colour used in a DeviceN or NChannel colour space, an entry in the Colorants… | `areColorantsPresent == true` |
| 6.2.4.4-t2 | PDSeparation | 2b/2u/3b | All Separation arrays within a single PDF/A-2 file (including those in Colorants dictiona… | `areTintAndAlternateConsistent == true` |
| 6.2.5-t1 | PDExtGState | 2b/2u/3b | An ExtGState dictionary shall not contain the TR key | `containsTR == false` |
| 6.2.5-t2 | PDExtGState | 2b/2u/3b | An ExtGState dictionary shall not contain the TR2 key with a value other than Default | `containsTR2 == false \|\| TR2NameValue == "Default"` |
| 6.2.5-t3 | PDExtGState | 2b/2u/3b | An ExtGState dictionary shall not contain the HTP key | `containsHTP == false` |
| 6.2.5-t4 | PDHalftone | 2b/2u/3b | All halftones in a conforming PDF/A-2 file shall have the value 1 or 5 for the HalftoneTy… | `HalftoneType != null && (HalftoneType == 1 \|\| HalftoneTyp…` |
| 6.2.5-t5 | PDHalftone | 2b/2u/3b | Halftones in a conforming PDF/A-2 file shall not contain a HalftoneName key | `HalftoneName == null` |
| 6.2.5-t6 | PDHalftone | 2b/2u/3b | The TransferFunction key in a halftone dictionary shall be used only as required by ISO 3… | `colorantName == 'Default' \|\| ((colorantName == null \|\| …` |
| 6.2.6-t1 | CosRenderingIntent | 2b/2u/3b | Where a rendering intent is specified, its value shall be one of the four values defined … | `internalRepresentation == "RelativeColorimetric" \|\| inter…` |
| 6.2.8-t1 | PDXImage | 2b/2u/3b | An Image dictionary shall not contain the Alternates key | `containsAlternates == false` |
| 6.2.8-t2 | PDXImage | 2b/2u/3b | An Image dictionary shall not contain the OPI key | `containsOPI == false` |
| 6.2.8-t3 | PDXImage | 2b/2u/3b | If an Image dictionary contains the Interpolate key, its value shall be false. For an inl… | `Interpolate == false` |
| 6.2.8-t4 | PDXImage | 2b/2u/3b | If an Image dictionary contains the BitsPerComponent key, its value shall be 1, 2, 4, 8 o… | `isMask == true \|\| BitsPerComponent == null \|\| BitsPerCo…` |
| 6.2.8-t5 | PDMaskImage | 2b/2u/3b | If an image mask dictionary contains the BitsPerComponent key, its value shall be 1 | `BitsPerComponent == null \|\| BitsPerComponent == 1` |
| 6.2.8.3-t1 | JPEG2000 | 2b/2u/3b | The number of colour channels in the JPEG2000 data shall be 1, 3 or 4 | `nrColorChannels == 1 \|\| nrColorChannels == 3 \|\| nrColor…` |
| 6.2.8.3-t2 | JPEG2000 | 2b/2u/3b | If the number of colour space specifications in the JPEG2000 data is greater than 1, ther… | `hasColorSpace == true \|\| nrColorSpaceSpecs == 1 \|\| nrCo…` |
| 6.2.8.3-t3 | JPEG2000 | 2b/2u/3b | The value of the METH entry in its 'colr' box shall be 0x01, 0x02 or 0x03. A conforming r… | `hasColorSpace == true \|\| colrMethod == 1 \|\| colrMethod …` |
| 6.2.8.3-t4 | JPEG2000 | 2b/2u/3b | JPEG2000 enumerated colour space 19 (CIEJab) shall not be used | `hasColorSpace == true \|\| colrEnumCS == null \|\| colrEnum…` |
| 6.2.8.3-t5 | JPEG2000 | 2b/2u/3b | The bit-depth of the JPEG2000 data shall have a value in the range 1 to 38. All colour ch… | `bpccBoxPresent == false && (bitDepth >= 1 && bitDepth <= 38)` |
| 6.2.9-t1 | PDXForm | 2b/2u/3b | A form XObject dictionary shall not contain any of the following: - the OPI key; - the Su… | `(Subtype2 == null \|\| Subtype2 != "PS") && containsPS == f…` |
| 6.2.9-t2 | PDXForm | 2b/2u/3b | A conforming file shall not contain any reference XObjects | `containsRef == false` |
| 6.2.9-t3 | PDXObject | 2b/2u/3b | A conforming file shall not contain any PostScript XObjects | `Subtype != "PS"` |
| 6.2.10-t1 | CosBM | 2b/2u/3b | Only blend modes that are specified in ISO 32000-1:2008 shall be used for the value of th… | `internalRepresentation == "Normal" \|\| internalRepresentat…` |
| 6.2.10-t2 | PDPage | 2b/2u/3b | If the document does not contain a PDF/A OutputIntent, then all Page objects that contain… | `gOutputCS != null \|\| containsGroupCS == true \|\| contain…` |
| 6.2.11.2-t1 | PDFont | 2b/2u/3b | All fonts and font programs used in a conforming file, regardless of rendering mode usage… | `Type == "Font"` |
| 6.2.11.2-t2 | PDFont | 2b/2u/3b | All fonts and font programs used in a conforming file, regardless of rendering mode usage… | `Subtype == "Type1" \|\| Subtype == "MMType1" \|\| Subtype =…` |
| 6.2.11.2-t3 | PDFont | 2b/2u/3b | All fonts and font programs used in a conforming file, regardless of rendering mode usage… | `Subtype == "Type3" \|\| fontName != null` |
| 6.2.11.2-t4 | PDSimpleFont | 2b/2u/3b | All fonts and font programs used in a conforming file, regardless of rendering mode usage… | `isStandard == true \|\| FirstChar != null` |
| 6.2.11.2-t5 | PDSimpleFont | 2b/2u/3b | All fonts and font programs used in a conforming file, regardless of rendering mode usage… | `isStandard == true \|\| LastChar != null` |
| 6.2.11.2-t6 | PDSimpleFont | 2b/2u/3b | All fonts and font programs used in a conforming file, regardless of rendering mode usage… | `isStandard == true \|\| (Widths_size != null && Widths_size…` |
| 6.2.11.2-t7 | PDFont | 2b/2u/3b | All fonts used in a conforming file shall conform to the font specifications defined in P… | `fontFileSubtype == null \|\| fontFileSubtype == "Type1C" \|…` |
| 6.2.11.3.1-t1 | PDType0Font | 2b/2u/3b | For any given composite (Type 0) font within a conforming file, the CIDSystemInfo entry i… | `cmapName == "Identity-H" \|\| cmapName == "Identity-V" \|\|…` |
| 6.2.11.3.2-t1 | PDCIDFont | 2b/2u/3b | ISO 32000-1:2008, 9.7.4, Table 117 requires that all embedded Type 2 CIDFonts in the CIDF… | `Subtype != "CIDFontType2" \|\| CIDToGIDMap != null \|\| con…` |
| 6.2.11.3.3-t1 | PDCMap | 2b/2u/3b | All CMaps used within a PDF/A-2 file, except those listed in ISO 32000-1:2008, 9.7.5.2, T… | `CMapName == "Identity-H" \|\| CMapName == "Identity-V" \|\|…` |
| 6.2.11.3.3-t2 | CMapFile | 2b/2u/3b | For those CMaps that are embedded, the integer value of the WMode entry in the CMap dicti… | `WMode == dictWMode` |
| 6.2.11.3.3-t3 | PDReferencedCMap | 2b/2u/3b | A CMap shall not reference any other CMap except those listed in ISO 32000-1:2008, 9.7.5.… | `CMapName == "Identity-H" \|\| CMapName == "Identity-V" \|\|…` |
| 6.2.11.4.1-t1 | PDFont | 2b/2u/3b | The font programs for all fonts used for rendering within a conforming file shall be embe… | `Subtype == "Type3" \|\| Subtype == "Type0" \|\| renderingMo…` |
| 6.2.11.4.1-t2 | Glyph | 2b/2u/3b | Embedded fonts shall define all glyphs referenced for rendering within the conforming fil… | `renderingMode == 3 \|\| isGlyphPresent == null \|\| isGlyph…` |
| 6.2.11.4.2-t1 | PDType1Font | 2b/2u/3b | If the FontDescriptor dictionary of an embedded Type 1 font contains a CharSet string, th… | `containsFontFile == false \|\| fontName.search(/[A-Z]{6}\+/…` |
| 6.2.11.4.2-t2 | PDCIDFont | 2b/2u/3b | If the FontDescriptor dictionary of an embedded CID font contains a CIDSet stream, then i… | `containsFontFile == false \|\| fontName.search(/[A-Z]{6}\+/…` |
| 6.2.11.5-t1 | Glyph | 2b/2u/3b | For every font embedded in a conforming file and used for rendering, the glyph width info… | `renderingMode == 3 \|\| widthFromFontProgram == null \|\| w…` |
| 6.2.11.6-t1 | TrueTypeFontProgram | 2b/2u/3b | For all non-symbolic TrueType fonts used for rendering, the embedded TrueType font progra… | `isSymbolic == true \|\| (cmap30Present == true ? nrCmaps > …` |
| 6.2.11.6-t2 | PDTrueTypeFont | 2b/2u/3b | All non-symbolic TrueType fonts shall have either MacRomanEncoding or WinAnsiEncoding as … | `isSymbolic == true \|\| ((Encoding == "MacRomanEncoding" \|…` |
| 6.2.11.6-t3 | PDTrueTypeFont | 2b/2u/3b | Symbolic TrueType fonts shall not contain an Encoding entry in the font dictionary | `isSymbolic == false \|\| Encoding == null` |
| 6.2.11.6-t4 | TrueTypeFontProgram | 2b/2u/3b | The 'cmap' table in the embedded font program for a symbolic TrueType font shall contain … | `isSymbolic == false \|\| nrCmaps == 1 \|\| cmap30Present ==…` |
| 6.2.11.7.2-t1 | Glyph | 2u | The Font dictionary of all fonts shall define the map of all used character codes to Unic… | `toUnicode != null` |
| 6.2.11.7.2-t2 | Glyph | 2u | The Unicode values specified in the ToUnicode CMap shall all be greater than zero (0), bu… | `toUnicode == null \|\| (toUnicode.indexOf("\u0000") == -1 &…` |
| 6.2.11.8-t1 | Glyph | 2b/2u/3b | A PDF/A-2 compliant document shall not contain a reference to the .notdef glyph from any … | `name != ".notdef"` |

### 6.3 Annotations (7 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.3.1-t1 | PDAnnot | 2b/2u/3b | Annotation types not defined in ISO 32000-1 shall not be permitted. Additionally, the 3D,… | `Subtype == "Text" \|\| Subtype == "Link" \|\| Subtype == "F…` |
| 6.3.2-t1 | PDAnnot | 2b/2u/3b | Except for annotation dictionaries whose Subtype value is Popup, all annotation dictionar… | `Subtype == "Popup" \|\| F != null` |
| 6.3.2-t2 | PDAnnot | 2b/2u/3b | If present, the F key's Print flag bit shall be set to 1 and its Hidden, Invisible, Toggl… | `F == null \|\| ((F & 1) == 0 && (F & 2) == 0 && (F & 4) == …` |
| 6.3.3-t1 | PDAnnot | 2b/2u/3b | Every annotation (including those whose Subtype value is Widget, as used for form fields)… | `(width == 0 && height == 0) \|\| Subtype == "Popup" \|\| Su…` |
| 6.3.3-t2 | PDAnnot | 2b/2u/3b | For all annotation dictionaries containing an AP key, the appearance dictionary that it d… | `AP == null \|\| AP == "N"` |
| 6.3.3-t3 | PDAnnot | 2b/2u/3b | If an annotation dictionary's Subtype key has a value of Widget and its FT key has a valu… | `AP != "N" \|\| Subtype != "Widget" \|\| FT != "Btn" \|\| (N…` |
| 6.3.3-t4 | PDAnnot | 2b/2u/3b | If an annotation dictionary's Subtype key has value other than Widget, or if FT key assoc… | `AP != "N" \|\| (Subtype == "Widget" && FT == "Btn") \|\| N_…` |

### 6.4 Interactive forms (8 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.4.1-t1 | PDWidgetAnnot | 2b/2u/3b | A Widget annotation dictionary shall not contain the A or AA keys | `containsA == false && containsAA == false` |
| 6.4.1-t2 | PDFormField | 2b/2u/3b | A Field dictionary shall not contain the A or AA keys | `containsAA == false` |
| 6.4.1-t3 | PDAcroForm | 2b/2u/3b | The NeedAppearances flag of the interactive form dictionary shall either not be present o… | `NeedAppearances == null \|\| NeedAppearances == false` |
| 6.4.2-t1 | PDAcroForm | 2b/2u/3b | The document's interactive form dictionary that forms the value of the AcroForm key in th… | `containsXFA == false` |
| 6.4.2-t2 | CosDocument | 2b/2u/3b | A document's Catalog shall not contain the NeedsRendering key | `NeedsRendering == false` |
| 6.4.3-t1 | PDSignature | 2b/2u/3b | When computing the digest for the file, it shall be computed over the entire file, includ… | `doesByteRangeCoverEntireDocument == true` |
| 6.4.3-t2 | PKCSDataObject | 2b/2u/3b | The PDF Signature (a DER-encoded PKCS#7 binary data object) shall be placed into the Cont… | `signingCertificatePresent == true` |
| 6.4.3-t3 | PKCSDataObject | 2b/2u/3b | The PDF Signature (a DER-encoded PKCS#7 binary data object) shall be placed into the Cont… | `SignerInfoCount == 1` |

### 6.5 Actions (4 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.5.1-t1 | PDAction | 2b/2u/3b | The Launch, Sound, Movie, ResetForm, ImportData, Hide, SetOCGState, Rendition, Trans, GoT… | `S == "GoTo" \|\| S == "GoToR" \|\| S == "GoToE" \|\| S == "…` |
| 6.5.1-t2 | PDNamedAction | 2b/2u/3b | Named actions other than NextPage, PrevPage, FirstPage, and LastPage shall not be permitt… | `N == "NextPage" \|\| N == "PrevPage" \|\| N == "FirstPage" …` |
| 6.5.2-t1 | PDDocument | 2b/2u/3b | The document's Catalog shall not include an AA entry for an additional-actions dictionary | `containsAA == false` |
| 6.5.2-t2 | PDPage | 2b/2u/3b | The Page dictionary shall not include an AA entry for an additional-actions dictionary | `containsAA == false` |

### 6.6 Metadata (33 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.6.2.1-t1 | PDDocument | 2b/2u/3b | The Catalog dictionary of a conforming file shall contain the Metadata key whose value is… | `containsMetadata == true` |
| 6.6.2.1-t2 | XMPPackage | 2b/2u/3b | The bytes attribute shall not be used in the header of an XMP packet | `bytes == null` |
| 6.6.2.1-t3 | XMPPackage | 2b/2u/3b | The encoding attribute shall not be used in the header of an XMP packet | `encoding == null` |
| 6.6.2.1-t4 | XMPPackage | 2b/2u/3b | All metadata streams present in the PDF shall conform to the XMP Specification. All conte… | `isSerializationValid` |
| 6.6.2.1-t5 | XMPPackage | 2b/2u/3b | All metadata streams present in the PDF shall conform to the XMP Specification. The XMP p… | `actualEncoding == "UTF-8"` |
| 6.6.2.3.1-t1 | XMPProperty | 2b/2u/3b | All properties specified in XMP form shall use either the predefined schemas defined in t… | `isPredefinedInXMP2005 == true \|\| isDefinedInMainPackage =…` |
| 6.6.2.3.1-t2 | XMPProperty | 2b/2u/3b | All properties specified in XMP form shall use either the predefined schemas defined in t… | `isValueTypeCorrect == true` |
| 6.6.2.3.2-t1 | ExtensionSchemaObject | 2b/2u/3b | Extension schemas shall be specified using the PDF/A extension schema container schema de… | `containsUndefinedFields == false` |
| 6.6.2.3.3-t1 | ExtensionSchemasConta… | 2b/2u/3b | The extension schema container schema uses the namespace URI "http://www.aiim.org/pdfa/ns… | `isValidBag == true && prefix == "pdfaExtension"` |
| 6.6.2.3.3-t2 | ExtensionSchemaDefini… | 2b/2u/3b | Field 'schema' of the PDF/A Schema value type in the PDF/A extension schema shall be pres… | `isSchemaValidText == true && schemaPrefix == "pdfaSchema"` |
| 6.6.2.3.3-t3 | ExtensionSchemaDefini… | 2b/2u/3b | Field 'namespaceURI' of the PDF/A Schema value type in the PDF/A extension schema shall b… | `isNamespaceURIValidURI == true && namespaceURIPrefix == "pd…` |
| 6.6.2.3.3-t4 | ExtensionSchemaDefini… | 2b/2u/3b | Field 'prefix' of the PDF/A Schema value type in the PDF/A extension schema shall be pres… | `isPrefixValidText == true && prefixPrefix == "pdfaSchema"` |
| 6.6.2.3.3-t5 | ExtensionSchemaDefini… | 2b/2u/3b | Field 'property' of the PDF/A Schema value type in the PDF/A extension schema shall have … | `isPropertyValidSeq == true && (propertyPrefix == null \|\| …` |
| 6.6.2.3.3-t6 | ExtensionSchemaDefini… | 2b/2u/3b | Field 'valueType' of the PDF/A Schema value type in the PDF/A extension schema shall have… | `isValueTypeValidSeq == true && (valueTypePrefix == null \|\…` |
| 6.6.2.3.3-t7 | ExtensionSchemaProper… | 2b/2u/3b | Field 'name' of the PDF/A Property value type in the PDF/A extension schema shall be pres… | `isNameValidText == true && namePrefix == "pdfaProperty"` |
| 6.6.2.3.3-t8 | ExtensionSchemaProper… | 2b/2u/3b | Field 'valueType' of the PDF/A Property value type in the PDF/A extension schema shall be… | `isValueTypeValidText == true && isValueTypeDefined == true …` |
| 6.6.2.3.3-t9 | ExtensionSchemaProper… | 2b/2u/3b | Field 'category' of the PDF/A Property value type in the PDF/A extension schema shall be … | `isCategoryValidText == true && (category == "external" \|\|…` |
| 6.6.2.3.3-t10 | ExtensionSchemaProper… | 2b/2u/3b | Field 'description' of the PDF/A Property value type in the PDF/A extension schema shall … | `isDescriptionValidText == true && descriptionPrefix == "pdf…` |
| 6.6.2.3.3-t11 | ExtensionSchemaValueT… | 2b/2u/3b | Field 'type' of the PDF/A ValueType value type in the PDF/A extension schema shall be pre… | `isTypeValidText == true && typePrefix == "pdfaType"` |
| 6.6.2.3.3-t12 | ExtensionSchemaValueT… | 2b/2u/3b | Field 'namespaceURI' of the PDF/A ValueType value type in the PDF/A extension schema shal… | `isNamespaceURIValidURI == true && namespaceURIPrefix == "pd…` |
| 6.6.2.3.3-t13 | ExtensionSchemaValueT… | 2b/2u/3b | Field 'prefix' of the PDF/A ValueType value type in the PDF/A extension schema shall be p… | `isPrefixValidText == true && prefixPrefix == "pdfaType"` |
| 6.6.2.3.3-t14 | ExtensionSchemaValueT… | 2b/2u/3b | Field 'description' of the PDF/A ValueType value type in the PDF/A extension schema shall… | `isDescriptionValidText == true && descriptionPrefix == "pdf…` |
| 6.6.2.3.3-t15 | ExtensionSchemaValueT… | 2b/2u/3b | Field 'field' of the PDF/A ValueType value type in the PDF/A extension schema shall have … | `isFieldValidSeq == true && (fieldPrefix == null \|\| fieldP…` |
| 6.6.2.3.3-t16 | ExtensionSchemaField | 2b/2u/3b | Field 'name' of the PDF/A Field value type in the PDF/A extension schema shall have type … | `isNameValidText == true && namePrefix == "pdfaField"` |
| 6.6.2.3.3-t17 | ExtensionSchemaField | 2b/2u/3b | Field 'valueType' of the PDF/A Field value type in the PDF/A extension schema shall have … | `isValueTypeValidText == true && isValueTypeDefined == true …` |
| 6.6.2.3.3-t18 | ExtensionSchemaField | 2b/2u/3b | Field 'description' of the PDF/A Field value type in the PDF/A extension schema shall hav… | `isDescriptionValidText == true && descriptionPrefix == "pdf…` |
| 6.6.4-t1 | MainXMPPackage | 2b/2u/3b | The PDF/A version and conformance level of a file shall be specified using the PDF/A Iden… | `containsPDFAIdentification == true` |
| 6.6.4-t2 | PDFAIdentification | 2b/2u/3b | The value of "pdfaid:part" shall be the part number of ISO 19005 to which the file confor… | `part == 2` |
| 6.6.4-t3 | PDFAIdentification | 2b/2u/3b | A Level A conforming file shall specify the value of "pdfaid:conformance" as A. A Level B… | `conformance == "B" \|\| conformance == "U" \|\| conformance…` |
| 6.6.4-t4 | PDFAIdentification | 2b/2u/3b | Property "part" of the PDF/A Identification Schema shall have namespace prefix "pdfaid" | `partPrefix == null \|\| partPrefix == "pdfaid"` |
| 6.6.4-t5 | PDFAIdentification | 2b/2u/3b | Property "conformance" of the PDF/A Identification Schema shall have namespace prefix "pd… | `conformancePrefix == null \|\| conformancePrefix == "pdfaid"` |
| 6.6.4-t6 | PDFAIdentification | 2b/2u/3b | Property "amd" of the PDF/A Identification Schema shall have namespace prefix "pdfaid" | `amdPrefix == null \|\| amdPrefix == "pdfaid"` |
| 6.6.4-t7 | PDFAIdentification | 2b/2u/3b | Property "corr" of the PDF/A Identification Schema shall have namespace prefix "pdfaid" | `corrPrefix == null \|\| corrPrefix == "pdfaid"` |

### 6.8 Embedded files (5 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.8-t1 | EmbeddedFile | 3b | The MIME type of an embedded file, or a subset of a file, shall be specified using the Su… | `Subtype != null && /^[-\w+\.]+\/[-\w+\.]+$/.test(Subtype)` |
| 6.8-t2 | CosFileSpecification | 2b/2u/3b | The file specification dictionary for an embedded file shall contain the F and UF keys | `containsEF == false \|\| (F != null && UF != null)` |
| 6.8-t3 | CosFileSpecification | 3b | The file specification dictionary shall contain key AFRelationship of type Name identifyi… | `AFRelationship != null` |
| 6.8-t4 | CosFileSpecification | 3b | The additional information provided for associated files as well as the usage requirement… | `isAssociatedFile == true` |
| 6.8-t5 | EmbeddedFile | 2b/2u | A file specification dictionary, as defined in ISO 32000-1:2008, 7.11.3, may contain the … | `isValidPDFA12 == true` |

### 6.9 Optional content (4 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.9-t1 | PDOCConfig | 2b/2u/3b | Each optional content configuration dictionary that forms the value of the D key, or that… | `Name != null && Name.length() > 0` |
| 6.9-t2 | PDOCConfig | 2b/2u/3b | Each optional content configuration dictionary shall contain the Name key, whose value sh… | `hasDuplicateName == false` |
| 6.9-t3 | PDOCConfig | 2b/2u/3b | If an optional content configuration dictionary contains the Order key, the array which i… | `OCGsNotContainedInOrder == null` |
| 6.9-t4 | PDOCConfig | 2b/2u/3b | The AS key shall not appear in any optional content configuration dictionary | `AS == null` |

### 6.10 Alternate presentations & transitions (2 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.10-t1 | PDDocument | 2b/2u/3b | There shall be no AlternatePresentations entry in the document's name dictionary | `containsAlternatePresentations == false` |
| 6.10-t2 | PDPage | 2b/2u/3b | There shall be no PresSteps entry in any Page dictionary | `containsPresSteps == false` |

### 6.11 Document requirements (1 rules)

| Rule | Object | Profiles | Requirement | Test expr |
|---|---|---|---|---|
| 6.11-t1 | CosDocument | 2b/2u/3b | The document catalog shall not contain the Requirements key | `Requirements == null` |

## 3. PDF/X-4 (separate source — not in veraPDF)

veraPDF covers PDF/A and PDF/UA only. PDF/X-4 (ISO 15930-7:2010) rules must come from the standard
and the **Ghent Workgroup** test suite. PDF/X-4 shares much of PDF/A's structural core (no
encryption, embedded fonts, valid output intent) but adds print-production rules with **no PDF/A
equivalent**. Initial X-4 rule list (to validate against GWG files):

| Rule | Requirement |
|---|---|
| X4 output intent | Exactly one `GTS_PDFX` OutputIntent; `OutputConditionIdentifier` registered or `DestOutputProfile` embedded. |
| X4 trim/art box | Every page has a `TrimBox` **or** `ArtBox` (not both), within `MediaBox`. |
| X4 trapped | Document info / XMP `Trapped` is `True` or `False` (explicitly set, not `Unknown`). |
| X4 no transparency restriction | X-4 permits transparency (unlike X-1a/X-3) — but blend spaces must resolve via the output intent. |
| X4 fonts embedded | All fonts embedded (shared with PDF/A 6.2.11.4). |
| X4 no encryption / valid ID | Shared with PDF/A 6.1.3. |

## 4. Slice plan (structural rules)

Work the structural batch by clause group, cheapest/highest-leverage first. Each slice implements a
group, verified against the matching corpus clause folder (`../veraPDF-corpus/PDF_A-2b/<clause>/`).

| Slice | Clause groups | Notes |
|---|---|---|
| **1 (done)** | 6.1.3-t1/t2 | `file-id`, `encrypt`. Shipped in slice 1. |
| **2 File structure** | rest of 6.1 (header 6.1.2, xref 6.1.4, hex strings 6.1.6, streams 6.1.7, filter whitelist incl. LZW 6.1.7.2/6.1.10, UTF-8 names 6.1.8, indirect 6.1.9, permissions 6.1.12, impl. limits 6.1.13) | Mostly byte/structural checks; impl. limits + filters need a stream/object sweep. **Refinements folded in here:** add **6.1.3-t3 post-EOF** (`postEOFDataSize == 0`); relax `file-id` (6.1.3-t1) to present-&-non-empty per veraPDF and demote the strict two-strings/type check to a Warning. |
| **3 Metadata** | 6.6 (XMP present/well-formed, **6.6.4 pdfaid**, schema conformance) | Reuses the XMP subsystem. pdfaid part/conformance are the identity rules. |
| **4 Colour + output intent** | 6.2.3 (output intent), 6.2.4 (colour spaces → device-colour-needs-intent) | Needs the new `/OutputIntents` reader + ICC checks (ICCSharp). |
| **5 Fonts** | 6.2.11 (embedding, encodings; **6.2.11.7 = the 2u Unicode delta**) | Gated by Type1/CFF descriptor reads; 2u adds ToUnicode coverage. |
| **6 Graphics rest** | 6.2.2 (content-stream operators), 6.2.5 (ExtGState), 6.2.9 (XObjects), 6.2.10 (transparency) | Content-stream walk. |
| **7 Annotations + forms + actions** | 6.3, 6.4, 6.5 | Annotation appearances, form field constraints, permitted actions (no JS). |
| **8 Embedded files + OC + misc** | 6.8 (2b/2u restriction vs **3b allowances**), 6.9, 6.10, 6.11 | 3b is where embedded-file rules diverge. |
| **9 X-4** | §3 above | Separate source (GWG). |
| **10 Corpus harness** | — | Walk corpus tree, parse expected outcome from filenames, assert per rule; wire veraPDF CLI as external oracle. |

---

_Table generated from `../veraPDF-validation-profiles` (CC BY 4.0) via
`scratchpad/extract_catalog.py`; requirements paraphrased, not copied._
