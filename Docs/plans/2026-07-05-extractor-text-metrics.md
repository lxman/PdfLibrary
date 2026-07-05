# Extractor Text Metrics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PdfTextExtractor`'s fragment geometry agree with rendered glyphs on documents using char-spacing/word-spacing/horizontal-scaling (`Tc`/`Tw`/`Tz`) and on text hosted inside placed Form XObjects — closing the two remaining known extraction-vs-render drift classes after the Tm-scale and Type0-width fixes.

**Architecture:** Two engine-side changes in `PdfLibrary` (repo C:\Users\jorda\RiderProjects\PDF). (1) The extractor's advance math adopts the ISO 32000 §9.4.4 displacement formula `tx = (w0×Tfs + Tc + Tw) × Th`, and the renderer's inline advance loop is reordered to the same formula (it currently applies Th before Tc/Tw, disagreeing with both the spec and `PdfGraphicsState`'s own helper). (2) Form-XObject-hosted fragments are transformed into page space by the form's placement matrix (`/Matrix` × Do-time CTM) at copy-out, and a seam separator prevents outer text gluing onto form text.

**Tech Stack:** C# / .NET (net8.0/9.0/10.0 multi-target), xUnit v3 (`PdfLibrary.Tests`), System.Numerics Matrix3x2.

## Global Constraints

- Branch: `feature/extractor-text-metrics` off PDF `master` (currently @ 824e8c7, 3 ahead of origin — do NOT push).
- Engine suite must stay green: `dotnet test C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Tests\PdfLibrary.Tests.csproj --framework net10.0 --nologo` — baseline 1428 passed.
- Every regression test red-checked: temporarily defeat the fix, show the failure output in the report, restore.
- Matrix convention: this engine composes with row-vector semantics — `Ctm = matrix * Ctm` on concat, `Vector2.Transform(point, m)` applies `m` to a row vector. A point in form space maps to page space via `formMatrix * ctmAtDoTime`.
- The extractor and renderer MUST agree on advance math — that agreement is the entire point (highlights sit on glyphs). Where the spec and existing renderer behavior conflict, the spec governs, and BOTH sides change.
- Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Ledger: append per-task lines to `C:\Users\jorda\RiderProjects\Focal\.superpowers\sdd\progress.md` (the session ledger lives in the Focal repo).

---

### Task 1: Tc/Tw/Tz in extraction advances (and renderer formula alignment)

`PdfTextExtractor.CalculateTextWidth` (PdfTextExtractor.cs:~373) computes `Σ w×fs/1000` and ignores `CurrentState.CharacterSpacing` (Tc), `WordSpacing` (Tw), and `HorizontalScaling` (Tz/Th) entirely; the TJ kern handler (~187-205) likewise omits Th. The renderer applies all three, so on justified text (per-line Tw is how most producers justify) the extractor's pen drifts off the glyphs cumulatively along each line — same failure family as the 2026-07-05 Type0-width bug. ISO 32000-1 §9.4.4: `tx = ((w0 − Tj/1000)×Tfs + Tc + Tw) × Th`, where Tw applies only to single-byte code 32 (simple fonts; not 2-byte Type0 codes).

The renderer's inline loop (PdfRenderer.cs:810-822) applies Th BEFORE adding Tc/Tw — wrong per the spec and inconsistent with `PdfGraphicsState`'s own advance helper (PdfGraphicsState.cs:357-360, which is spec-ordered). Align the renderer to the spec formula too, and change its word-spacing trigger from `decoded == " "` to single-byte code 32 (spec); extractor uses the identical rule so the two agree.

**Files:**
- Modify: `PdfLibrary/Content/PdfTextExtractor.cs` (CalculateTextWidth ~373, OnShowText call site ~157, TJ handler ~187-205)
- Modify: `PdfLibrary/Rendering/PdfRenderer.cs` (advance loop ~810-822)
- Test: `PdfLibrary.Tests/PdfTextExtractorTests.cs` (Text Fragment Width Tests region)

**Interfaces:**
- Consumes: `CurrentState.CharacterSpacing` / `.WordSpacing` / `.HorizontalScaling` (already parsed from Tc/Tw/Tz operators by `PdfContentProcessor`); `TextMatrixScaleX()` (existing).
- Produces: `CalculateTextWidth(byte[] bytes, PdfFont? font, double fontSize, double charSpacing, double wordSpacing, double horizontalScaling)` — new signature, still private static.

- [ ] **Step 1: Write the failing tests**

Add to `PdfTextExtractorTests.cs` (Text Fragment Width Tests region). All use the string-content pattern already in the file (`Encoding.ASCII.GetBytes` + `ExtractTextWithFragments(bytes)` with no resources → fallback glyph width `fontSize × 0.5` per byte, which makes expected values exact).

```csharp
/// <summary>ISO 32000 §9.4.4: tx = (w0×Tfs + Tc + Tw) × Th. Tc adds per SHOWN GLYPH.
/// Fallback width (no resources) is fontSize×0.5 per byte, so "Test" at 12pt = 24.0 base;
/// with Tc=2 → 24 + 4×2 = 32.0.</summary>
[Fact]
public void Fragment_Width_IncludesCharacterSpacing()
{
    var content = @"
BT
/F1 12 Tf
2 Tc
100 700 Td
(Test) Tj
ET";
    (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(Encoding.ASCII.GetBytes(content));
    Assert.Single(fragments);
    Assert.Equal(32.0, fragments[0].Width, precision: 6);
}

/// <summary>Tw applies to single-byte code 32 only. "a b" at 12pt fallback = 18.0 base;
/// with Tw=5 → 23.0 (one space).</summary>
[Fact]
public void Fragment_Width_IncludesWordSpacing_OnSpaces()
{
    var content = @"
BT
/F1 12 Tf
5 Tw
100 700 Td
(a b) Tj
ET";
    (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(Encoding.ASCII.GetBytes(content));
    Assert.Single(fragments);
    Assert.Equal(23.0, fragments[0].Width, precision: 6);
}

/// <summary>Tz scales the whole displacement. "Test" at 12pt fallback = 24.0; Tz=50 → 12.0.
/// Th multiplies AFTER Tc per the spec: with 2 Tc as well, (24 + 8) × 0.5 = 16.0.</summary>
[Fact]
public void Fragment_Width_ScaledByHorizontalScaling_AfterCharSpacing()
{
    var content = @"
BT
/F1 12 Tf
50 Tz
2 Tc
100 700 Td
(Test) Tj
ET";
    (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(Encoding.ASCII.GetBytes(content));
    Assert.Single(fragments);
    Assert.Equal(16.0, fragments[0].Width, precision: 6);
}

/// <summary>The pen advance between runs carries Tc/Tz too: second fragment's X = first X +
/// first Width (the width already includes spacing/scaling). And TJ kern adjustments scale by
/// Th: [(A) -1000 (B)] TJ at 12pt, Tz=50 → kern = 1000/1000×12×0.5 = 6.0 between the runs.</summary>
[Fact]
public void Fragment_PenAdvance_And_TJKern_CarryTcTz()
{
    var content = @"
BT
/F1 12 Tf
50 Tz
2 Tc
100 700 Td
[(A) -1000 (B)] TJ
ET";
    (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(Encoding.ASCII.GetBytes(content));
    Assert.Equal(2, fragments.Count);
    // "A": (12×0.5 + 2) × 0.5 = 4.0 wide; kern: 1000/1000 × 12 × 0.5 = 6.0
    Assert.Equal(100.0, fragments[0].X, precision: 6);
    Assert.Equal(4.0, fragments[0].Width, precision: 6);
    Assert.Equal(110.0, fragments[1].X, precision: 6);   // 100 + 4 + 6
}
```

- [ ] **Step 2: Run to verify all four fail**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Fragment_Width_IncludesCharacterSpacing|Fragment_Width_IncludesWordSpacing_OnSpaces|Fragment_Width_ScaledByHorizontalScaling_AfterCharSpacing|Fragment_PenAdvance_And_TJKern_CarryTcTz" --framework net10.0 --nologo`
Expected: 4 FAIL (widths come back without spacing/scaling: 24.0, 18.0, 24.0, and X₂=112 respectively).

NOTE: the fallback path (`font is null`) currently returns `bytes.Length × fontSize × 0.5` — the new formula must run the same per-code loop over the fallback per-byte width so Tc/Tw/Th apply there too (the tests above use the fallback path). Keep the "no font AND no spacing" result identical to today (24.0 for "Test" at 12pt) so the existing `Fragment_Width_PopulatedFromFallback_WhenNoFontResources` test stays green.

- [ ] **Step 3: Implement the extractor formula**

Replace `CalculateTextWidth` with (keeping the Type0 2-byte decode from 824e8c7):

```csharp
/// <summary>Per-code displacement per ISO 32000-1 §9.4.4: tx = (w0×Tfs + Tc + Tw)×Th, where
/// Tw applies only to single-byte code 32 (never to 2-byte Type0 codes). Without a font the
/// per-code base width falls back to fontSize×0.5 per byte (unchanged), but spacing and
/// scaling still apply — Tc/Tw/Th displacement is font-independent.</summary>
private static double CalculateTextWidth(byte[] bytes, PdfFont? font, double fontSize,
    double charSpacing, double wordSpacing, double horizontalScaling)
{
    bool isType0 = font is { FontType: PdfFontType.Type0 };
    double total = 0;
    var i = 0;
    while (i < bytes.Length)
    {
        int code;
        if (isType0 && i + 1 < bytes.Length)
        {
            code = (bytes[i] << 8) | bytes[i + 1];
            i += 2;
        }
        else
        {
            code = bytes[i];
            i++;
        }
        double baseWidth = font is not null ? font.GetCharacterWidth(code) * fontSize / 1000.0
                                            : fontSize * 0.5;
        double advance = baseWidth + charSpacing;
        if (!isType0 && code == 32) advance += wordSpacing;
        total += advance;
    }
    return total * horizontalScaling / 100.0;
}
```

Call site in `OnShowText` (~157):

```csharp
double advance = CalculateTextWidth(text.Bytes, font, CurrentState.FontSize,
    CurrentState.CharacterSpacing, CurrentState.WordSpacing, CurrentState.HorizontalScaling) * TextMatrixScaleX();
```

TJ handlers (both `PdfInteger` and `PdfReal` cases, ~187-205): multiply by Th as well —

```csharp
double adjustment = -intVal.Value / 1000.0 * CurrentState.FontSize
    * (CurrentState.HorizontalScaling / 100.0) * TextMatrixScaleX();
```

(and identically with `realVal.Value`).

- [ ] **Step 4: Run the four new tests — green; run the whole extractor test file — green** (the fallback test's no-spacing case must still be 24.0).

- [ ] **Step 5: Align the renderer's advance loop to the spec formula**

In `PdfRenderer.cs` ~810-822, reorder to match §9.4.4 and switch the Tw trigger to single-byte code 32:

```csharp
double glyphWidth = font.GetCharacterWidth(charCode);
double advance = glyphWidth * CurrentState.FontSize / 1000.0;

// ISO 32000-1 §9.4.4: tx = (w0×Tfs + Tc + Tw) × Th — character and word spacing are added
// BEFORE horizontal scaling (this loop previously scaled first, disagreeing with the spec,
// with PdfGraphicsState's advance helper, and now with the extractor). Tw applies only to
// single-byte code 32, never to 2-byte Type0 codes.
if (CurrentState.CharacterSpacing != 0)
    advance += CurrentState.CharacterSpacing;
if (!isType0 && charCode == 32 && CurrentState.WordSpacing != 0)
    advance += CurrentState.WordSpacing;
advance *= CurrentState.HorizontalScaling / 100.0;

glyphWidths.Add(advance);
```

(`isType0` is already in scope in that method.)

- [ ] **Step 6: Full engine suite**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --framework net10.0 --nologo`
Expected: all green (1428 baseline + 4 new). If any rendering/pixel test fails on the reorder, STOP and report DONE_WITH_CONCERNS with the failing test names — do not "fix" pixel baselines yourself.

- [ ] **Step 7: Red-check** — revert the Th reorder factor in the extractor's TJ handler only (drop `× Th`), confirm `Fragment_PenAdvance_And_TJKern_CarryTcTz` fails (X₂ = 116 instead of 110... compute: kern without Th = 12 → X₂ = 100+4+12 = 116), restore. Record output.

- [ ] **Step 8: Commit**

```bash
git add PdfLibrary/Content/PdfTextExtractor.cs PdfLibrary/Rendering/PdfRenderer.cs PdfLibrary.Tests/PdfTextExtractorTests.cs
git commit -m "fix(text): extractor advances honor Tc/Tw/Tz; renderer aligned to spec formula"
```

---

### Task 2: Form-XObject fragments transformed to page space + seam separator

`ExtractTextFromFormXObject` (PdfTextExtractor.cs:~264-311) runs a nested extractor over the form's content and copies its fragments through with X/Y/Width/FontSize in the FORM'S LOCAL space — a form placed with a matrix (letterheads, stamps, whole-page wrappers) reports wrong positions, so Focal's search/selection highlights land elsewhere on the page. It also appends the form's text directly onto the outer builder with no separator (documented deferral at ~292-295), gluing words across the seam.

Fix at copy-out: transform each nested fragment by `placement = formMatrix * ctmAtDoTime` (row-vector convention; page-level extraction starts at CTM = identity, so the Do-time CTM is exactly the page-relative transform — consistent with outer fragments, which never apply the CTM). Scale `Width` by the placement's horizontal scale and `FontSize` by its vertical scale. Insert one `' '` separator between non-empty outer text and non-empty form text when the outer builder doesn't already end in whitespace; compute `baseOffset` AFTER the separator so rebased `TextOffset`s stay exact.

**Files:**
- Modify: `PdfLibrary/Content/PdfTextExtractor.cs` (`ExtractTextFromFormXObject` ~264-311)
- Test: `PdfLibrary.Tests/PdfTextExtractorTests.cs` (XObject region, next to `TextOffset_XObjectHostedFragments_RebaseOntoOuterAssembledText`)

**Interfaces:**
- Consumes: `CurrentState.Ctm` (Matrix3x2, tracked by the base processor's `cm` handling); the form's `/Matrix` from `formStream.Dictionary` (PdfArray of 6 numbers; default identity).
- Produces: nothing new — fragment values change meaning from form-local to page space.

- [ ] **Step 1: Write the failing tests**

```csharp
/// <summary>Deferred from the search slice: nested Form-XObject fragments were copied through
/// in the FORM'S local coordinates — a form placed with /Matrix (letterhead, stamp, page
/// wrapper) reported wrong positions and consumers' highlight boxes landed elsewhere on the
/// page. Fragments must be transformed by formMatrix × Do-time CTM at copy-out: X/Y mapped,
/// Width scaled by the horizontal scale, FontSize by the vertical scale.</summary>
[Fact]
public void XObjectFragments_TransformedByFormMatrixAndCtm()
{
    var formContent = @"
BT
/F1 10 Tf
10 20 Td
(Inside) Tj
ET";
    var formDict = new PdfDictionary
    {
        [new PdfName("Type")] = new PdfName("XObject"),
        [new PdfName("Subtype")] = new PdfName("Form"),
        // /Matrix [2 0 0 2 5 7]: scale ×2, translate (5,7)
        [new PdfName("Matrix")] = new PdfArray
        {
            new PdfInteger(2), new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(2), new PdfInteger(5), new PdfInteger(7),
        },
    };
    var formStream = new PdfStream(formDict, Encoding.ASCII.GetBytes(formContent));
    var resources = new PdfResources(new PdfDictionary
    {
        [new PdfName("XObject")] = new PdfDictionary { [new PdfName("Fm1")] = formStream },
    });

    // Outer content places the form with an additional cm translate (100, 50).
    var outer = @"
q
1 0 0 1 100 50 cm
/Fm1 Do
Q";
    (_, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(Encoding.ASCII.GetBytes(outer), resources);

    TextFragment f = Assert.Single(fragments);
    // Form-local (10,20) → ×2 + (5,7) → (25,47) → cm translate → (125, 97)
    Assert.Equal(125.0, f.X, precision: 4);
    Assert.Equal(97.0, f.Y, precision: 4);
    Assert.Equal(20.0, f.FontSize, precision: 4);   // 10pt × vertical scale 2
    // "Inside" fallback width = 6×10×0.5 = 30 local → ×2 horizontal = 60
    Assert.Equal(60.0, f.Width, precision: 4);
}

/// <summary>Deferred from the search slice: no separator was inserted between outer text and
/// form-hosted text, so words glued across the seam ("OuterInside") and a query straddling it
/// could spuriously match. One ' ' separates non-empty outer text from non-empty form text;
/// rebased TextOffsets must still index the assembled string exactly.</summary>
[Fact]
public void XObjectSeam_SeparatorInserted_OffsetsStayExact()
{
    var formContent = @"
BT
/F1 12 Tf
50 50 Td
(Inside) Tj
ET";
    var formDict = new PdfDictionary
    {
        [new PdfName("Type")] = new PdfName("XObject"),
        [new PdfName("Subtype")] = new PdfName("Form"),
    };
    var formStream = new PdfStream(formDict, Encoding.ASCII.GetBytes(formContent));
    var resources = new PdfResources(new PdfDictionary
    {
        [new PdfName("XObject")] = new PdfDictionary { [new PdfName("Fm1")] = formStream },
    });

    var outer = @"
BT
/F1 12 Tf
100 700 Td
(Outer) Tj
ET
/Fm1 Do";
    (string text, List<TextFragment> fragments) = PdfTextExtractor.ExtractTextWithFragments(Encoding.ASCII.GetBytes(outer), resources);

    Assert.DoesNotContain("OuterInside", text);
    Assert.Contains("Outer", text);
    Assert.Contains("Inside", text);
    foreach (TextFragment f in fragments)
        Assert.Equal(f.Text, text.Substring(f.TextOffset, f.Text.Length));
}
```

- [ ] **Step 2: Run both — expect FAIL** (first: X=10 local; second: text contains "OuterInside").

- [ ] **Step 3: Implement in `ExtractTextFromFormXObject`**

After the nested extractor runs, replace the copy-out block:

```csharp
// Placement transform: a fragment in the form's local space maps to the invoker's space via
// the form's /Matrix then the CTM at Do time (row-vector convention, matching the `matrix *
// Ctm` concat above). Page-level extraction starts at CTM == identity, so ctmAtDo IS the
// page-relative placement — consistent with outer fragments, which never apply the CTM.
Matrix3x2 placement = ReadFormMatrix(formStream) * CurrentState.Ctm;
double hScale = Math.Sqrt(placement.M11 * placement.M11 + placement.M21 * placement.M21);
double vScale = Math.Sqrt(placement.M12 * placement.M12 + placement.M22 * placement.M22);

// Seam separator: without it, outer text glues directly onto form-hosted text ("OuterInside")
// and a query straddling the seam can spuriously match (deferral from the search slice, now
// closed). The separator belongs to no fragment, like every other heuristic separator here.
string formText = formExtractor.GetText();
if (_textBuilder.Length > 0 && formText.Length > 0 && !char.IsWhiteSpace(_textBuilder[^1]))
    _textBuilder.Append(' ');

int baseOffset = _textBuilder.Length;
_textBuilder.Append(formText);
foreach (TextFragment fragment in formExtractor.GetTextFragments())
{
    var mapped = Vector2.Transform(new Vector2((float)fragment.X, (float)fragment.Y), placement);
    _fragments.Add(new TextFragment
    {
        Text = fragment.Text,
        X = mapped.X,
        Y = mapped.Y,
        FontName = fragment.FontName,
        FontSize = fragment.FontSize * vScale,
        Width = fragment.Width * hScale,
        TextOffset = baseOffset + fragment.TextOffset
    });
}
```

Add the helper:

```csharp
/// <summary>The form's /Matrix (default identity): six numbers [a b c d e f].</summary>
private static Matrix3x2 ReadFormMatrix(PdfStream formStream)
{
    if (!formStream.Dictionary.TryGetValue(new PdfName("Matrix"), out PdfObject? obj) ||
        obj is not PdfArray { Count: 6 } m)
        return Matrix3x2.Identity;
    float N(PdfObject o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => (float)r.Value,
        _ => 0f,
    };
    return new Matrix3x2(N(m[0]), N(m[1]), N(m[2]), N(m[3]), N(m[4]), N(m[5]));
}
```

Also update (do not delete silently) the DEFERRED comment block at ~292-295 — both halves of that deferral are now closed; remove the note.

- [ ] **Step 4: Run both new tests — green. Red-check the transform test** by replacing `placement` with `Matrix3x2.Identity` temporarily → X back to 10 → restore. Record output.

- [ ] **Step 5: Full engine suite green** (`dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --framework net10.0 --nologo`). The existing `TextOffset_XObjectHostedFragments_RebaseOntoOuterAssembledText` test asserts offset exactness through the seam — it must pass with the separator in place (its per-fragment substring assertion is separator-agnostic). If any existing test pinned the glued "no separator" text, report it rather than weakening the new behavior.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Content/PdfTextExtractor.cs PdfLibrary.Tests/PdfTextExtractorTests.cs
git commit -m "fix(text): map Form-XObject fragments to page space; separate the text seam"
```

---

## Completion

1. Final whole-branch review (superpowers:requesting-code-review conventions, full branch diff off master).
2. Merge `feature/extractor-text-metrics` to PDF `master` (fast-forward), delete branch. Do NOT push (user-gated).
3. Re-pack the local feed: `& C:\Users\jorda\RiderProjects\PDF\pack-local.ps1` (pins Focal to the new dev build).
4. Verify Focal against the new engine: `dotnet test C:\Users\jorda\RiderProjects\Focal\Focal.slnx --nologo` — expect Core 166 / App 433 / Rendering 167 (Focal's reading-order assembly consumes fragment X/Width; improved values must not break its builder-PDF fixtures, which use no Tc/Tw/Tz and no placed XObjects).
5. Update the CHANGELOG's `[Unreleased]` section with both fixes (they ride into 2.3.1).
6. Ledger + report to user for smoke (justified-text document + a letterhead/stamped document are the interesting cases).

Out of scope (re-triage separately): the "/DR bootstrap, degenerate-rect AP" ledger note — /DR registration already exists in `FieldAuthor`/`AppearanceFontResolver`; the original finding behind "degenerate-rect AP" needs to be re-located before it is actionable.
