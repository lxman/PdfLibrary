# Bug: `PdfRadioGroupBuilder` emits broken radio fields

**Status:** FIXED 2026-06-28 — `PdfDocumentWriter` now emits a parent `/Btn` field + one widget per option (see Resolution below). Regression tests in `PdfLibrary.Tests/RadioGroupBuilderBugRepro.cs` now assert the correct behavior and pass.
**Severity:** High for form authoring — builder-created radio groups are unreadable as radios by any consumer (including this library's own reader).
**Scope:** Builder *write* path only. Reading/filling **existing** radio fields (`PdfDocumentEditor.Forms` → `PdfButtonField` Kind=Radio, `SelectedOption`, widget on-states) works correctly. A hand-built/correct radio PDF round-trips fine. Only `PdfRadioGroupBuilder` → `PdfDocumentWriter` emission is broken.
**Found by:** PdfLibrary (the consumer app) Phase 2 form-filling work — radio test fixtures had to be hand-crafted `/Btn` PDFs because the builder's output was unusable.

---

## Symptom

```csharp
byte[] pdf = PdfDocumentBuilder.Create()
    .WithAcroForm(f => f.SetNeedAppearances(true))
    .AddPage(p =>
    {
        p.AddRadioGroup("myRadio")
            .AddOptionInches("Option1", 1.0, 9.0)
            .AddOptionInches("Option2", 1.0, 8.5)
            .Select("Option1");
    })
    .ToByteArray();

using var editor = PdfDocumentEditor.Open(new MemoryStream(pdf));
PdfFormField f = editor.Forms.Single();
// EXPECTED: PdfButtonField, Kind == ButtonKind.Radio, Options == ["Option1","Option2"]
// ACTUAL:   PdfUnknownField (Type == PdfFormFieldType.Unknown); cast to PdfButtonField throws.
```

The builder allocates **one** PDF object for the whole radio group and writes only the common widget preamble — no `/FT`, no `/Ff`, no `/Kids`, no per-option widgets, no `/AP`. The reader sees an object with `/T` but no `/Kids` and no `/FT`, classifies it `Unknown`, and returns `PdfUnknownField`.

### What the builder emits (actual)
```
8 0 obj
<< /Type /Annot /Subtype /Widget /Rect [0.00 0.00 0.00 0.00] /P 5 0 R /T (myRadio)
   /BS << /W 1.0 /S /S >> >>
endobj
```
(8 objects total for the whole document; zero rect; not a field; no children.)

### What a correct 2-option radio group requires
A **parent field object** (referenced from AcroForm `/Fields`, NOT in page `/Annots`):
```
<< /FT /Btn
   /Ff 32768            % Radio flag (bit 16); optionally | 16384 (NoToggleToOff)
   /T (myRadio)
   /V /Option1          % selected on-state name (or /Off)
   /DV /Off
   /Kids [ <w1> 0 R  <w2> 0 R ] >>
```
One **widget annotation per option** (in page `/Annots`, NOT in AcroForm `/Fields`):
```
<< /Type /Annot /Subtype /Widget
   /Parent <parentField> 0 R
   /Rect [left bottom right top]
   /AS /Option1                          % this widget's current appearance state
   /AP << /N << /Option1 << >> /Off << >> >> >> >>   % on-state name == export value
```
Minimum object count for 2 options ≈ 10 (catalog, pages, info, font, page, content, AcroForm, **parent field**, **widget 1**, **widget 2**). The builder produces 8.

---

## Root causes (file:line in `PdfLibrary`)

All defects are in the **writer**; the builder data model (`PdfRadioGroupBuilder` / `PdfRadioOption`) mostly holds the right data but isn't consumed.

1. **No `PdfRadioGroupBuilder` case in `PdfDocumentWriter.WriteFormField` (~L1231–1312).**
   The switch has branches for `PdfTextFieldBuilder`, `PdfCheckboxBuilder`, `PdfDropdownBuilder`, `PdfSignatureFieldBuilder` — **none for `PdfRadioGroupBuilder`**. It falls through to the common preamble only (`/Type /Annot /Subtype /Widget /Rect /P /T`). No `/FT`, `/Ff`, `/Kids`, `/V`, `/AP`.

2. **Object allocation reserves 1 object per field regardless of type — `PdfDocumentWriter.cs` ~L292:**
   ```csharp
   fieldObjects.AddRange(page.FormFields.Select(_ => _nextObjectNumber++));
   ```
   A radio group needs `1 + Options.Count` object numbers (parent + one widget per option). There are no slots to emit the per-option widget annotations.

3. **Parent field is treated as a widget annotation.**
   `WriteFormField` unconditionally writes `/Type /Annot /Subtype /Widget` and the single object is placed in the page `/Annots` (~L548–553). For a radio group the parent must be a pure field dict (in AcroForm `/Fields` only); the **child widgets** are the page annotations.

4. **Zero rect on the builder base — `PdfRadioGroupBuilder.cs` L6:**
   ```csharp
   public class PdfRadioGroupBuilder(string name, double pageHeight)
       : PdfFormFieldBuilder(name, default)   // default => Rect [0 0 0 0]
   ```
   Per-option rects live on each `PdfRadioOption`; the base `Rect` (used by `WriteFormField`) is always zero. The writer must use each option's rect for its widget, not the base rect.

---

## How the reader reacts (confirms the diagnosis)
`FormFieldTree.WalkField` reads the object, finds `/T` but no `/Kids` → "no kids" branch → no `/FT` → `PdfFormFieldType.Unknown` → `PdfUnknownField`. So `Kind`/`Options`/`SelectedOption` are unavailable.

---

## Reproduction
Repro tests already added at **`PdfLibrary.Tests/RadioGroupBuilderBugRepro.cs`** (4 tests, currently passing because they assert the *broken* behavior + a correct hand-built baseline):
- `BuilderRadioGroup_RoundTrip_FieldType_IsUnknown_NotRadio` — reader returns `Unknown` from builder output.
- `BuilderRadioGroup_EmitsOnlyOneObject_ZeroRect_NoKids` — raw PDF: no `/FT`, no `/AP`, no Radio `/Ff`, zero `/Rect`, only 8 objects.
- `HandBuilt_RadioGroup_Baseline_ReadsCorrectly` — reader is correct for a properly-structured radio group.
- `HandBuilt_RadioGroup_SetSelectedOption_RoundTrips` — write/edit path is correct; only builder emission is broken.

**After the fix:** invert the first two tests to assert the CORRECT behavior (builder output reads as `PdfButtonField`, Kind=Radio, Options match, `SelectedOption` round-trips, and selecting each option persists), so they become regression tests. Keep the two hand-built baselines.

---

## Fix direction (confined to `PdfDocumentWriter.cs`)

1. **Object allocation (~L292):** when a field is a `PdfRadioGroupBuilder`, reserve `1 + radioGroup.Options.Count` object numbers (parent + one widget each). Keep a mapping (parent obj# → list of widget obj#) keyed by the builder instance for use in steps 2–3.

2. **`WriteFormField` switch (~L1231):** add `case PdfRadioGroupBuilder radioGroup:` that
   - writes the **parent field dict** WITHOUT `/Type /Annot /Subtype /Widget /Rect /P`: `/FT /Btn`, `/Ff 32768` (set radio flag; consider `| 16384` for NoToggleToOff), `/T (name)`, `/V /<selected or Off>`, `/DV /Off`, `/Kids [ widget refs ]`;
   - then writes **one widget annotation object per option**: `/Type /Annot /Subtype /Widget /Parent <parent> 0 R /Rect [option.Rect]`, `/AS /<onStateName or Off>`, `/AP << /N << /<onStateName> << >> /Off << >> >> >>`. The on-state name must equal the option's export/value used in `/V` and in `Select(...)`.
   - Optionally emit `/MK`/border from the builder's border settings, and real appearance streams (or rely on `/AcroForm /NeedAppearances true` so viewers regenerate — the symptom example already sets `SetNeedAppearances(true)`).

3. **Page `/Annots` (~L548–553):** for a radio group, add the **widget child** object numbers to `/Annots`, NOT the parent. The parent goes only in AcroForm `/Fields`.

4. **(L6, optional)** `PdfRadioGroupBuilder` base `Rect` is irrelevant once the writer uses per-option rects; leave as-is or document that the base rect is unused for radio groups.

### Acceptance
A radio group built via `AddRadioGroup` + options, saved and reopened through `PdfDocumentEditor.Forms`, must yield a `PdfButtonField` with `Kind == Radio`, `Options` equal to the option names, a readable `SelectedOption`, and the ability to set `SelectedOption` to each option and have it persist on re-save (mirror the existing `HandBuilt_RadioGroup_*` baselines). Validate the output opens cleanly in Acrobat/another viewer.

---

## Resolution (2026-06-28)

Fixed entirely in `PdfLibrary/Builder/PdfDocumentWriter.cs`, as scoped above:

1. **Per-field object plan.** The flat one-object-per-field list was replaced by a `FieldObjectPlan` (field object + list of annotation objects). For a radio group the allocator reserves `1 + Options.Count` objects: the parent field plus one widget per option. All other field types keep a single object that serves as both field and widget (`AnnotObjects = [fieldObject]`).
2. **`WriteRadioGroupField`.** New method writes the parent as a pure `/FT /Btn` dict — `/Ff 32768` (Radio; `| 16384` when `NoToggleToOff`; plus ReadOnly/Required bits), `/T`, `/V /<selected or Off>`, `/DV /Off`, `/Kids [widgets]` — then one `/Subtype /Widget` annotation per option with the option's own `/Rect`, `/AS`, and `/AP << /N << /<onState> << >> /Off << >> >> >>`. Option export values are name-escaped via a new `EscapePdfName` helper (`#xx` per ISO 32000-1 §7.3.5).
3. **`/Annots` vs `/Fields` split.** Page `/Annots` now lists each field's `AnnotObjects` (the widget children for a radio group); AcroForm `/Fields` lists each plan's `FieldObject` (the parent). The parent is no longer a widget annotation.
4. The builder base `Rect` is left untouched (unused for radio groups; the writer uses per-option rects).

**Tests** (`PdfLibrary.Tests/RadioGroupBuilderBugRepro.cs`): the two bug-documenting tests were inverted into regression guards (`BuilderRadioGroup_RoundTrip_ReadsAsRadio_WithOptions`, `BuilderRadioGroup_EmitsParentPlusWidgets`); added `BuilderRadioGroup_SelectEachOption_RoundTrips` (Theory over both options, incl. set-and-resave) and `BuilderRadioGroup_AlongsideTextField_BothReadCorrectly` (guards the `/Annots` index arithmetic when a radio coexists with another field). The two `HandBuilt_RadioGroup_*` baselines are unchanged. All pass, plus the full form/builder/writer/annotation suites stay green.

### Follow-up: real appearance streams (2026-06-28)

The initial fix wrote empty `/AP /N << /<on> << >> /Off << >> >>` placeholder sub-dicts and relied on `/NeedAppearances true`. That rendered as **square** buttons in Chrome/PDFium (no circle geometry → PDFium drew the bare widget rect) and **flickered** in Adobe (empty dicts aren't valid XObject streams, so Acrobat regenerated lazily on blur). Fixed by emitting real Form-XObject appearance streams per widget:

- Allocation reserves two extra objects per option (on + off appearance); tracked in `FieldObjectPlan.WidgetApObjects`.
- `WriteRadioAppearanceStream` draws a vector circle (4-Bézier approximation): a stroked ring for `/Off`, ring + filled centre dot for the on-state. The widget's `/AP /N` now references these indirect streams, and `/F 4` (Print) is set so filled radios print.
- Widgets render statically and correctly in both engines without `NeedAppearances`. Note the *builder* still relies on `NeedAppearances` for text/checkbox/choice (no appearance generation for those yet) — making the builder generate appearances for all field types so the flag can default off is a separate, larger effort. Regression: `BuilderRadioGroup_EmitsRealAppearanceStreams`.
- **Flattener fix for state-keyed `/AP /N` (found while diagnosing this).** `FormFlattener.FlattenField` only handled `/AP /N` when it was a single Form-XObject stream (text fields). For check boxes and radios `/AP /N` is a *state sub-dictionary* (`<< /<on> <stream> /Off <stream> >>`), so the old code `continue`d — never painting the appearance and never removing the widget, yet still stripping the AcroForm field. That left orphaned `/Widget` annotations whose `/Parent` referenced a deleted field: Chrome still rendered them (and, with no AcroForm, drew no field-highlight, so flattened radios *looked* great), but **Adobe prunes orphaned widgets on resave, so the radios vanished.** Fixed by selecting the stream named by each widget's `/AS` (the visible state), baking that into page content, and removing the widget. Regression: `FlattenTests.FlattenAll_RadioGroup_BakesAppearance_AndRemovesWidgets`.
- **No `/BS` on radio widgets.** The base builder defaults `BorderWidth = 1`, so radios were emitting `/BS << /W 1 /S /S >>` — a *rectangular* border that viewers draw as a square box around the widget rect (Acrobat then tints it with its field-highlight, giving a "purplish square"). A radio's visible border is the CIRCLE in the appearance stream, so `WriteRadioGroupField` no longer emits `/BS`. Regression: `BuilderRadioGroup_EmitsRealAppearanceStreams` asserts no `/BS`. Verified by flattening the form (`Forms.Flatten()`): the baked-in page content shows three clean circles (one filled), confirming the appearances are correct and the residual square seen in interactive viewers is the viewer's own field-highlight overlay (standard, user-toggleable), not PDF content.
