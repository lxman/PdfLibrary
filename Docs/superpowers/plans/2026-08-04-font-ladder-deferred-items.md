# Font Ladder Deferred Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make ladder step 1 style-aware without letting a `StemV` inference veto a face the document named, make `PickBest`'s tie-break deterministic across machines, and collapse the duplicated `SfntNameReader` overloads.

**Architecture:** `FontRequest` gains a second, narrower style pair (`ExplicitBold` / `ExplicitItalic`) sourced only from descriptor flags and explicit name style tokens — never from the `StemV >= 120` inference. Step 1 of `SystemFontLocator.Resolve` consults that pair alone and, on disagreement, looks for a better face among the faces indexed under the hit record's **own** `Families`; steps 2 and 3 keep using today's merged pair unchanged. The other two items are local: a tie-break comparison in `FontMetadataIndex.PickBest`, and replacing two duplicated `byte[]` bodies in `SfntNameReader` with `MemoryStream` wrappers.

**Tech Stack:** C# / .NET (netstandard2.1 library, multi-targeted net8.0/net9.0/net10.0 test runs), xUnit, `dotnet test`.

## Global Constraints

- **Never read a whole font file into memory during indexing.** No `File.ReadAllBytes` in any index path — indexing is seek-based via `Stream`. (`File.ReadAllBytes` in `SystemFontLocator.Resolve`/`GetFontData` is fine: a font has already been chosen for loading at that point.)
- **Item 3 must be byte-identical in behaviour.** If it moves a render hash, that is a bug in the collapse, not a baseline to re-pin.
- **Step 1 must never return null where it returns non-null today, and never return a lower-scoring face than today.**
- **The `StemV >= 120` inference must never reach the explicit style pair.** It continues to feed the merged pair used by steps 2 and 3.
- **Ordinal string comparison, never culture-aware,** in any tie-break or lookup — the result must not vary with host locale.
- Match surrounding style: file-scoped namespaces, expression-bodied members where neighbours use them, comments that explain WHY at the density of the surrounding code.
- Run all builds and tests in the **foreground**. Background/nohup runs have stalled implementers permanently in this repo.
- Measure the test baseline yourself before changing anything. Do not call a failure a pre-existing flake without evidence — a previous implementer made exactly that claim and it was a real regression they had caused.
- Known genuinely-intermittent, not yours: `SkiaSharpRenderPipelineTests.NewAes256Encryption_DecryptsAndRenders` (test-helper `SKBitmap.FromImage` fragility under load); `Jp2Codec.Tests` sometimes reports 639 instead of 640 discovered tests.

---

### Task 1: Carry an explicit-only style pair on `FontRequest`

**Files:**
- Modify: `PdfLibrary/Fonts/FontRequest.cs:8`
- Modify: `PdfLibrary/Fonts/SubstituteFontResolver.cs:20-39`
- Test: `PdfLibrary.Tests/Fonts/SubstituteFontDescriptorStyleTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `FontRequest(string BaseFont, bool Bold, bool Italic, bool Serif = false, bool Mono = false, bool ExplicitBold = false, bool ExplicitItalic = false)`. Task 2 reads `ExplicitBold` / `ExplicitItalic`.

**Context the implementer needs:** `SubstituteFontResolver.Classify` returns a merged `(serif, mono, bold, italic)` in which `bold` is true if `descriptor.IsBold` **or** `descriptor.StemV >= 120` **or** the name contains "Bold" anywhere. That merge is correct for steps 2 and 3 and must not change. This task adds a second pair that carries only the two descriptor *flags* — `PdfFontDescriptor.IsBold` (`Flags & 0x40000`) and `IsItalic` (`Flags & 0x40`). Explicit style tokens in the *name* are deliberately NOT added here: `SystemFontLocator` re-derives those itself via `Base35Aliases.Split` in Task 2, exactly as it already re-derives serif/mono from the name.

- [ ] **Step 1: Write the failing test**

Append to `PdfLibrary.Tests/Fonts/SubstituteFontDescriptorStyleTests.cs`. Follow the existing capturing-fake-provider pattern already in that file; if its fake is named differently, reuse it rather than adding a second one.

```csharp
[Fact]
public void A_StemV_inference_does_not_reach_the_explicit_style_pair()
{
    // StemV >= 120 makes Classify report bold, which is right for the guessing steps of the ladder
    // but must never gain the power to reject a face the document named outright.
    var descriptor = new PdfFontDescriptor { Flags = 0, StemV = 140 };
    var provider = new CapturingProvider();

    new SubstituteFontResolver(provider).Resolve("ABCDEF+XYZ123", descriptor);

    Assert.True(provider.Last!.Bold);           // merged pair: the inference counts
    Assert.False(provider.Last!.ExplicitBold);  // explicit pair: it does not
}

[Fact]
public void Descriptor_style_flags_do_reach_the_explicit_style_pair()
{
    var descriptor = new PdfFontDescriptor { Flags = 0x40 | 0x40000 };   // Italic | ForceBold
    var provider = new CapturingProvider();

    new SubstituteFontResolver(provider).Resolve("ABCDEF+XYZ123", descriptor);

    Assert.True(provider.Last!.ExplicitBold);
    Assert.True(provider.Last!.ExplicitItalic);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SubstituteFontDescriptorStyleTests"`
Expected: compile failure — `FontRequest` has no `ExplicitBold` / `ExplicitItalic` member.

- [ ] **Step 3: Widen `FontRequest`**

Replace `PdfLibrary/Fonts/FontRequest.cs:8` with:

```csharp
/// <summary>A request to substitute a font the renderer could not use from the PDF itself.
/// <paramref name="Serif"/> and <paramref name="Mono"/> carry the /FontDescriptor's Serif and
/// FixedPitch flags, which for a subset name like "ABCDEF+XYZ123" are the ONLY family signal there
/// is — the name spells nothing.
///
/// <para><paramref name="ExplicitBold"/> and <paramref name="ExplicitItalic"/> are a NARROWER pair
/// than <paramref name="Bold"/> and <paramref name="Italic"/>: they carry only what the document
/// stated outright — the descriptor's style flags, and (merged in by the provider) explicit style
/// tokens in the name. They deliberately exclude the StemV >= 120 inference, which is a guess about
/// a number rather than a statement of intent. Ladder steps that are already guessing use the merged
/// pair; the step that can override an exact PostScript-name match uses this one, so a heavy stem
/// width can never swap out a face the document named.</para>
///
/// <para>All five style members default to false so a provider constructing a request from a bare
/// /BaseFont keeps compiling and keeps its previous meaning.</para></summary>
public sealed record FontRequest(
    string BaseFont,
    bool Bold,
    bool Italic,
    bool Serif = false,
    bool Mono = false,
    bool ExplicitBold = false,
    bool ExplicitItalic = false);
```

- [ ] **Step 4: Populate it in `SubstituteFontResolver.Load`**

Replace the body of `Load` (`PdfLibrary/Fonts/SubstituteFontResolver.cs:20-39`) with:

```csharp
    private EmbeddedFontMetrics? Load(string baseFont, PdfFontDescriptor? descriptor)
    {
        // Style comes from the descriptor AND the name; Classify already merges both. The provider
        // owns the ladder from here — this method no longer knows anything about filenames.
        (bool serif, bool mono, bool bold, bool italic) = Classify(baseFont, descriptor);

        // The explicit pair is the descriptor's flags alone. Name tokens are NOT merged here: the
        // provider re-derives those itself from the /BaseFont it is handed, which is the same string
        // — and doing it there means the synthetic retry below gets its own name read correctly.
        bool explicitBold = descriptor?.IsBold ?? false;
        bool explicitItalic = descriptor?.IsItalic ?? false;

        // Second attempt under the synthetic standard-14 name. A provider that implements only
        // GetFontData keys off the /BaseFont string, so an opaque subset name ("ABCDEF+FooSans")
        // misses where the standard face it stands in for would have hit — without this retry such
        // providers resolve nothing, contradicting ISystemFontProvider.Resolve's own "keep working
        // unchanged" contract. It costs SystemFontLocator nothing: step 3 of its ladder already
        // tries the same name, so the first call has covered it and this one never fires.
        FontMatch? match =
            provider.Resolve(new FontRequest(
                baseFont, bold, italic, serif, mono, explicitBold, explicitItalic))
            ?? provider.Resolve(new FontRequest(
                SyntheticStd14Name(serif, mono, bold, italic),
                bold, italic, serif, mono, explicitBold, explicitItalic));
        if (match is null) return null;

        var metrics = new EmbeddedFontMetrics(match.Data, match.FaceIndex);
        return metrics.IsValid ? metrics : null;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SubstituteFontDescriptorStyleTests"`
Expected: PASS, including the six tests already in that file.

- [ ] **Step 6: Run the full solution suite**

Run: `dotnet test` (foreground, from the repo root)
Expected: 0 failed. Record the counts.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Fonts/FontRequest.cs PdfLibrary/Fonts/SubstituteFontResolver.cs PdfLibrary.Tests/Fonts/SubstituteFontDescriptorStyleTests.cs
git commit -m "feat(fonts): carry an explicit-only style pair on FontRequest"
```

---

### Task 2: Share the synthetic sfnt fixture builder

**Files:**
- Create: `PdfLibrary.Tests/Fonts/SfntFixtures.cs`
- Modify: `PdfLibrary.Tests/Fonts/SfntNameReaderTests.cs:11-105` (delete the private builders, call the shared ones)

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class SfntFixtures` with
  `public static byte[] Sfnt(int macStyle, params (int platformId, int langId, int nameId, string value)[] names)`
  and `public static byte[] Ttc(params byte[][] faces)`. Task 3 uses `SfntFixtures.Sfnt`.

**Why this is its own task:** Task 3's fixtures need the builder, and moving it is a pure, separately-reviewable refactor with a hard success criterion — the existing `SfntNameReaderTests` must pass **unchanged**. Bundling it into Task 3 would mix a refactor with a behaviour change in one diff.

**Context the implementer needs:** the builders currently live as `private static` members of `SfntNameReaderTests` at lines 11-105: `Sfnt`, `AddU16`, `AddU32`, `Ttc`. `Sfnt` writes a minimal sfnt carrying only `name` and `head` tables; `head` bytes 44-45 are `macStyle`, where bit 0 is bold and bit 1 is italic. `Ttc` wraps bare faces into a `ttcf` collection, rebasing each face's table-directory offsets because table offsets inside a `.ttc` are file-absolute. Move all four verbatim — do not "improve" them; any behaviour change here silently invalidates the existing reader tests.

- [ ] **Step 1: Create the shared class**

Create `PdfLibrary.Tests/Fonts/SfntFixtures.cs` containing `internal static class SfntFixtures` in namespace `PdfLibrary.Tests.Fonts`, holding the four members moved **verbatim** from `SfntNameReaderTests` lines 11-105, with `Sfnt` and `Ttc` changed from `private` to `public` and `AddU16` / `AddU32` left private. Carry their XML doc comments across unchanged.

- [ ] **Step 2: Delete the originals and re-point the callers**

Delete lines 11-105 from `PdfLibrary.Tests/Fonts/SfntNameReaderTests.cs` and replace every call to `Sfnt(` with `SfntFixtures.Sfnt(` and `Ttc(` with `SfntFixtures.Ttc(` in that file.

- [ ] **Step 3: Run the reader tests to verify nothing moved**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntNameReaderTests"`
Expected: PASS with exactly the same test count as before the move. If any test now fails, the move was not verbatim — revert and redo it.

- [ ] **Step 4: Commit**

```bash
git add PdfLibrary.Tests/Fonts/SfntFixtures.cs PdfLibrary.Tests/Fonts/SfntNameReaderTests.cs
git commit -m "test(fonts): share the synthetic sfnt fixture builder"
```

---

### Task 3: Make ladder step 1 style-aware

**Files:**
- Modify: `PdfLibrary/Fonts/FontMetadataIndex.cs:99-115` (extract `StyleScore`, use it in `PickBest`)
- Modify: `PdfLibrary/Fonts/SystemFontLocator.cs:70-102` (step 1)
- Create: `PdfLibrary.Tests/Fonts/LadderStep1StyleTests.cs`

**Interfaces:**
- Consumes: `FontRequest.ExplicitBold` / `ExplicitItalic` (Task 1); `SfntFixtures.Sfnt` (Task 2).
- Produces: `FontMetadataIndex.StyleScore(FontFaceRecord f, bool bold, bool italic) → int` (internal static). Task 4 does not use it.

**Context the implementer needs.** Current step 1 (`SystemFontLocator.cs:80-86`):

```csharp
        // Step 1: exact PostScript name. ASCII by spec and language-free, so this is the one lookup
        // that cannot be confounded by localisation.
        FontFaceRecord? hit = _index.ByPostScriptName(stripped);

        // Step 2: aliased family, best style match.
        if (hit is null)
            hit = FirstFamilyHit(Base35Aliases.FamiliesFor(family), bold, italic);
```

The defect: a `/BaseFont /ArialMT` with an italic descriptor takes the upright `ArialMT` face and never reaches style-aware step 2.

**The obvious fix does not work, and the implementer must not "simplify" back to it.** Falling through to `FirstFamilyHit(Base35Aliases.FamiliesFor(family), …)` is a no-op here: `Base35Aliases.Split("ArialMT")` finds no `-` or `,`, so `family` is `"ArialMT"`; `FamiliesFor` does not know it and aliases it to itself; and `_byFamily` is keyed on **name-table family names** (ID 1 / ID 16), where Arial's key is `"Arial"`. `ByFamily("ArialMT")` misses. The correct source of sibling faces is `hit.Families` — the record's own family names, which is how `ArialMT` reaches `Arial-ItalicMT`.

`FontFaceRecord` is `(string Path, int FaceIndex, string PostScriptName, IReadOnlyCollection<string> Families, string EnglishFamily, string Subfamily, bool Italic, bool Bold)`.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Fonts/LadderStep1StyleTests.cs`:

```csharp
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class LadderStep1StyleTests
{
    /// <summary>Two faces of one family in a temp directory: an upright and an italic, with distinct
    /// PostScript names. Synthesised rather than taken from the system on purpose — the slice-1
    /// ladder tests used DefaultFontDirectories() and so could never reach step 1's short-circuit on
    /// a CI box, which is exactly why this defect survived them.</summary>
    private static string WriteFamily(string dir)
    {
        Directory.CreateDirectory(dir);
        // macStyle bit 1 = italic. Name IDs: 1 = family, 2 = subfamily, 6 = PostScript name.
        File.WriteAllBytes(Path.Combine(dir, "upright.ttf"), SfntFixtures.Sfnt(0,
            (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "ArialMT")));
        File.WriteAllBytes(Path.Combine(dir, "italic.ttf"), SfntFixtures.Sfnt(0x2,
            (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Arial-ItalicMT")));
        return dir;
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "ladder-step1-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void An_explicit_italic_request_gets_the_italic_sibling_of_an_exact_upright_hit()
    {
        string dir = WriteFamily(TempDir());
        try
        {
            var locator = new SystemFontLocator([dir]);
            FontMatch? m = locator.Resolve(
                new FontRequest("ArialMT", false, true, ExplicitItalic: true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "italic.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_exact_hit_that_already_agrees_is_returned_unchanged()
    {
        string dir = WriteFamily(TempDir());
        try
        {
            var locator = new SystemFontLocator([dir]);
            FontMatch? m = locator.Resolve(new FontRequest("ArialMT", false, false));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "upright.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_exact_hit_whose_family_has_no_better_face_is_kept_not_dropped()
    {
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "upright.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "ArialMT")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(
                new FontRequest("ArialMT", false, true, ExplicitItalic: true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "upright.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_StemV_style_inference_cannot_displace_an_exact_hit()
    {
        // The whole point of the two-pair design. Bold is set in the MERGED pair (as a StemV >= 120
        // inference would set it) but not in the explicit pair; the named face must survive.
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "upright.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "ArialMT")));
            File.WriteAllBytes(Path.Combine(dir, "bold.ttf"), SfntFixtures.Sfnt(0x1,
                (3, 0x409, 1, "Arial"), (3, 0x409, 2, "Bold"), (3, 0x409, 6, "Arial-BoldMT")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(new FontRequest("ArialMT", true, false));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "upright.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_explicit_style_token_in_the_name_alone_counts_as_explicit()
    {
        // No descriptor at all — the request's explicit flags are both false. The signal comes only
        // from the "-Italic" token in the /BaseFont, which SystemFontLocator re-derives via
        // Base35Aliases.Split. The fixture is a MISLABELLED font, which is what makes this test
        // bite: "Fam-Italic" is an exact PostScript hit, but that face's own head macStyle says
        // upright, so name-derived explicit style is the only thing that can reject it. Mislabelled
        // style bits are common enough in the wild to be worth honouring the name over.
        string dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            // PostScript name claims Italic; macStyle 0 and subfamily "Regular" say otherwise.
            File.WriteAllBytes(Path.Combine(dir, "mislabelled.ttf"), SfntFixtures.Sfnt(0,
                (3, 0x409, 1, "Fam"), (3, 0x409, 2, "Regular"), (3, 0x409, 6, "Fam-Italic")));
            // A genuinely italic sibling in the same family.
            File.WriteAllBytes(Path.Combine(dir, "trueitalic.ttf"), SfntFixtures.Sfnt(0x2,
                (3, 0x409, 1, "Fam"), (3, 0x409, 2, "Italic"), (3, 0x409, 6, "Fam-Oblique")));
            var locator = new SystemFontLocator([dir]);

            FontMatch? m = locator.Resolve(new FontRequest("Fam-Italic", false, true));

            Assert.NotNull(m);
            Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "trueitalic.ttf")), m!.Data);
        }
        finally { Directory.Delete(dir, true); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~LadderStep1StyleTests"`
Expected: TWO failures — `An_explicit_italic_request_gets_the_italic_sibling_of_an_exact_upright_hit` and `An_explicit_style_token_in_the_name_alone_counts_as_explicit`, both returning the upright/mislabelled bytes instead of the italic ones. The other three should already PASS — they are the guards that the change does not break today's behaviour. If any of those three fails at this point, stop and report: the fixture, not the production code, is wrong.

- [ ] **Step 3: Extract `StyleScore` in `FontMetadataIndex`**

Replace `PickBest` (`PdfLibrary/Fonts/FontMetadataIndex.cs:99-115`) with:

```csharp
    /// <summary>+1 for italic agreement, +1 for bold agreement. Scored rather than matched exactly so
    /// a family lacking the requested combination degrades to its nearest face instead of failing.</summary>
    internal static int StyleScore(FontFaceRecord f, bool bold, bool italic) =>
        (f.Italic == italic ? 1 : 0) + (f.Bold == bold ? 1 : 0);

    /// <summary>Best style match among <paramref name="candidates"/>. Ties break on
    /// (EnglishFamily, PostScriptName) ordinal, then face index — see the tie-break note on the
    /// comparison below.</summary>
    public static FontFaceRecord? PickBest(IEnumerable<FontFaceRecord> candidates, bool bold, bool italic)
    {
        FontFaceRecord? best = null;
        var bestScore = -1;
        foreach (FontFaceRecord f in candidates)
        {
            int score = StyleScore(f, bold, italic);
            if (best is not null && (score < bestScore || (score == bestScore && f.FaceIndex >= best.FaceIndex)))
                continue;
            best = f;
            bestScore = score;
        }
        return best;
    }
```

(The tie-break itself is Task 4's job — leave the comparison alone here. This step only extracts the scoring so step 1 can reuse it.)

- [ ] **Step 4: Make step 1 style-aware**

In `PdfLibrary/Fonts/SystemFontLocator.cs`, replace lines 80-86 with:

```csharp
        // The explicit pair: what the document stated outright, descriptor flags merged with the
        // name's own style tokens. Deliberately excludes the StemV inference behind `bold`/`italic`.
        bool explicitBold = request.ExplicitBold || nameBold;
        bool explicitItalic = request.ExplicitItalic || nameItalic;

        // Step 1: exact PostScript name. ASCII by spec and language-free, so this is the one lookup
        // that cannot be confounded by localisation. An exact hit is the document naming a FACE, so
        // it is the incumbent — but a face whose own style bits contradict what the document stated
        // outright is the "file whose name looked right" failure this ladder exists to end, so we
        // look for a better-styled sibling. Siblings come from the hit's OWN Families and nowhere
        // else: the alias table is not consulted, because the document named this typeface and an
        // upright Arial beats some other typeface's italic. Note the family index is keyed on
        // name-table families ("Arial"), NOT PostScript names ("ArialMT") — re-splitting the request
        // string here would look up a key that cannot exist and silently do nothing.
        FontFaceRecord? hit = _index.ByPostScriptName(stripped);
        if (hit is not null && (explicitBold || explicitItalic))
            hit = BetterStyledSibling(hit, explicitBold, explicitItalic);

        // Step 2: aliased family, best style match.
        if (hit is null)
            hit = FirstFamilyHit(Base35Aliases.FamiliesFor(family), bold, italic);
```

Then add this method next to `FirstFamilyHit`:

```csharp
    /// <summary>The face among <paramref name="hit"/>'s own family that matches the requested style
    /// STRICTLY better than <paramref name="hit"/> does, or <paramref name="hit"/> itself. Strictly,
    /// not weakly: a sibling that fixes italic while breaking bold ties, and a tie is not evidence —
    /// the named face stays. Never returns null, so step 1 can only ever improve on today.</summary>
    private FontFaceRecord BetterStyledSibling(FontFaceRecord hit, bool bold, bool italic)
    {
        int hitScore = FontMetadataIndex.StyleScore(hit, bold, italic);
        if (hitScore == 2) return hit;

        FontFaceRecord? best = null;
        var bestScore = hitScore;
        foreach (string family in hit.Families)
            foreach (FontFaceRecord f in _index.ByFamily(family))
            {
                int score = FontMetadataIndex.StyleScore(f, bold, italic);
                if (score <= bestScore) continue;
                best = f;
                bestScore = score;
            }
        return best ?? hit;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~LadderStep1StyleTests"`
Expected: all five PASS.

- [ ] **Step 6: Run the font tests, then the full solution suite**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Fonts"` then `dotnet test` (foreground, repo root)
Expected: 0 failed. Record the counts. `FontResolutionLadderTests` in particular must still pass.

- [ ] **Step 7: Commit**

```bash
git add PdfLibrary/Fonts/FontMetadataIndex.cs PdfLibrary/Fonts/SystemFontLocator.cs PdfLibrary.Tests/Fonts/LadderStep1StyleTests.cs
git commit -m "fix(fonts): let an explicit style override a style-blind exact name hit"
```

---

### Task 4: Deterministic tie-break in `PickBest`

**Files:**
- Modify: `PdfLibrary/Fonts/FontMetadataIndex.cs` (`PickBest`, as left by Task 3)
- Test: `PdfLibrary.Tests/Fonts/FontMetadataIndexTests.cs`

**Interfaces:**
- Consumes: `FontMetadataIndex.StyleScore` (Task 3).
- Produces: nothing new.

**Context the implementer needs:** `PickBest` breaks ties on `f.FaceIndex >= best.FaceIndex`, documented as "ties keep the LOWEST face index." Within one file that is meaningful. But `SystemFontLocator.FirstFamilyHit` feeds it candidates from *different files*, every one with `FaceIndex == 0`, so the comparison is always true and the effective rule is "first indexed wins" — i.e. `Directory.EnumerateFiles` order, which is not stable across machines or filesystems. `FontFaceRecord`'s own doc comment already names the intended remedy: `EnglishFamily` exists "only for canonicalisation and deterministic tie-breaking."

- [ ] **Step 1: Write the failing test**

Append to `PdfLibrary.Tests/Fonts/FontMetadataIndexTests.cs`:

```csharp
[Fact]
public void PickBest_breaks_ties_the_same_way_regardless_of_candidate_order()
{
    // Both score 1 against a Regular request: one is bold-only, one is italic-only. Today the
    // winner is whichever was enumerated first, which is filesystem order and not portable.
    var boldOnly = new FontFaceRecord("/b.ttf", 0, "Fam-Bold", ["Fam"], "Fam", "Bold", false, true);
    var italicOnly = new FontFaceRecord("/i.ttf", 0, "Fam-Italic", ["Fam"], "Fam", "Italic", true, false);

    FontFaceRecord? forward = FontMetadataIndex.PickBest([boldOnly, italicOnly], false, false);
    FontFaceRecord? reverse = FontMetadataIndex.PickBest([italicOnly, boldOnly], false, false);

    Assert.Equal(1, FontMetadataIndex.StyleScore(forward!, false, false));
    Assert.Equal(1, FontMetadataIndex.StyleScore(reverse!, false, false));
    Assert.Equal(forward!.PostScriptName, reverse!.PostScriptName);
}
```

If `FontMetadataIndexTests.cs` does not already have a `using PdfLibrary.Fonts;`, add it. `FontFaceRecord` and `FontMetadataIndex` are `internal`, and the test project already has access via `InternalsVisibleTo`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PickBest_breaks_ties"`
Expected: FAIL — `forward` is `Fam-Bold` and `reverse` is `Fam-Italic`.

- [ ] **Step 3: Implement the deterministic tie-break**

Replace `PickBest`'s loop body and doc comment in `PdfLibrary/Fonts/FontMetadataIndex.cs` with:

```csharp
    /// <summary>Best style match among <paramref name="candidates"/>.
    ///
    /// <para>Ties break on (EnglishFamily, PostScriptName) ordinal, then face index. The face index
    /// alone is not enough: it only discriminates WITHIN one file, and the family lookup that feeds
    /// this method draws candidates from different files that all have face index 0 — so the
    /// effective rule was Directory.EnumerateFiles order, which is not stable across machines.
    /// Ordinal, never culture-aware, so the choice cannot vary with the host locale.</para></summary>
    public static FontFaceRecord? PickBest(IEnumerable<FontFaceRecord> candidates, bool bold, bool italic)
    {
        FontFaceRecord? best = null;
        var bestScore = -1;
        foreach (FontFaceRecord f in candidates)
        {
            int score = StyleScore(f, bold, italic);
            if (best is not null && (score < bestScore || (score == bestScore && !SortsBefore(f, best))))
                continue;
            best = f;
            bestScore = score;
        }
        return best;
    }

    /// <summary>Deterministic ordering for equally-good candidates.</summary>
    private static bool SortsBefore(FontFaceRecord a, FontFaceRecord b)
    {
        int byFamily = string.CompareOrdinal(a.EnglishFamily, b.EnglishFamily);
        if (byFamily != 0) return byFamily < 0;
        int byName = string.CompareOrdinal(a.PostScriptName, b.PostScriptName);
        if (byName != 0) return byName < 0;
        return a.FaceIndex < b.FaceIndex;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FontMetadataIndexTests"`
Expected: all PASS.

- [ ] **Step 5: Run the full solution suite**

Run: `dotnet test` (foreground, repo root)
Expected: 0 failed. Record the counts.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Fonts/FontMetadataIndex.cs PdfLibrary.Tests/Fonts/FontMetadataIndexTests.cs
git commit -m "fix(fonts): break PickBest ties deterministically across machines"
```

---

### Task 5: Collapse the duplicated `SfntNameReader` overloads

**Files:**
- Modify: `PdfLibrary/Fonts/SfntNameReader.cs:11-113` (delete the `byte[]` bodies and helpers, keep the signatures)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: unchanged public signatures `FaceCount(byte[])`, `ReadFace(byte[], int, string)`.

**Context the implementer needs:** `SfntNameReader` carries two implementations of the same logic — a `byte[]` one (`FaceCount` at line 11, `ReadFace` at 19, helpers `IsTtc`/`Tag`/`U16`/`U32` at 106-113) and a `Stream` one (`FaceCount` at 117, `ReadFace` at 127, helpers at 211-230). Roughly 70 lines are duplicated verbatim. The platform-0 UTF-16BE fix had to be applied twice, which is exactly the drift pattern this removes.

The only production caller of the `byte[]` overloads is `FontMetadataIndex.PickFaceIndex`, which operates on an already-in-memory array handed over by a third-party provider. Wrapping that array in a `MemoryStream` reads nothing new from disk, so the Global Constraint on whole-file reads is untouched.

**This task must be behaviour-preserving.** The cross-implementation tests in `SfntNameReaderTests` assert the two paths return equal records; after this change they guard a wrapper, which is fine — keep them.

- [ ] **Step 1: Record the baseline**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntNameReaderTests"`
Expected: PASS. Write down the exact test count — it must be identical at the end of this task.

- [ ] **Step 2: Replace the `byte[]` bodies with wrappers**

In `PdfLibrary/Fonts/SfntNameReader.cs`, replace the two `byte[]` methods at lines 11-104 with:

```csharp
    /// <summary>Number of faces: the `ttcf` header's count for a collection, otherwise 1. Wraps the
    /// stream implementation — the two used to be separate copies of one algorithm, and the
    /// platform-0 decode fix had to be made twice before they were collapsed.</summary>
    public static int FaceCount(byte[] data) => FaceCount(new MemoryStream(data, writable: false));

    /// <summary>In-memory twin of <see cref="ReadFace(Stream, int, string)"/>, for callers holding
    /// bytes a third-party provider handed them rather than a file they can seek. Wrapping an array
    /// already in memory reads nothing new, so the never-read-a-whole-font-file rule that governs
    /// indexing is not in play here.</summary>
    public static FontFaceRecord? ReadFace(byte[] data, int faceIndex, string path) =>
        ReadFace(new MemoryStream(data, writable: false), faceIndex, path);
```

Then delete the now-unused `byte[]` helpers `IsTtc(byte[])`, `Tag(byte[], long)`, `U16(byte[], long)` and `U32(byte[], long)` (lines 106-113 before the edit). Delete the phrase "Stream-based twin of ReadFace(byte[], int, string). Mirrors its logic exactly, seeking for each field instead of indexing into an in-memory buffer." from the `Stream` overload's doc comment and replace it with a plain description, since it is no longer a twin of anything:

```csharp
    /// <summary>Reads one face's identity — PostScript name, families, subfamily and style bits —
    /// from the `name` and `head` tables, seeking to each field rather than loading the file.</summary>
```

- [ ] **Step 3: Verify the build has no unused-member or ambiguity warnings**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release`
Expected: build succeeds. If the compiler reports an ambiguous call at any `ReadFace`/`FaceCount` call site (a `byte[]` is not implicitly a `Stream`, so it should not), resolve it at the call site rather than renaming the methods.

- [ ] **Step 4: Run the reader tests and confirm the count is unchanged**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntNameReaderTests"`
Expected: PASS with the exact count recorded in Step 1.

- [ ] **Step 5: Run the full solution suite**

Run: `dotnet test` (foreground, repo root)
Expected: 0 failed. Record the counts.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary/Fonts/SfntNameReader.cs
git commit -m "refactor(fonts): collapse the duplicated byte[] sfnt reader onto the stream one"
```

---

### Task 6: Render gates across all three boxes

**Files:**
- Possibly modify: Pellucid baseline fixtures (only if a hash moves and the crop is verified correct)
- Modify: `C:/Users/jorda/RiderProjects/Pellucid/ci/dependencies.json` (engine SHA pin)

**Interfaces:**
- Consumes: the merged result of Tasks 1-5.
- Produces: a green three-box gate, or a documented, crop-verified re-pin.

**This task is run by the controller, not delegated.** The `render-verify` skill requires that a moved fixture's crop be **viewed** and compared against the page's embedded reference; that cannot be judged by proxy, and hash agreement between two boxes is evidence of agreement, not of correctness.

**Context:** Item 3 (Task 5) must be byte-identical — any movement attributable to it is a bug, not a baseline. Items in Tasks 3 and 4 may legitimately move hashes.

- [ ] **Step 1: Pack the engine into Pellucid's local feed**

Run from `C:/Users/jorda/RiderProjects/PdfLibrary`: `.\pack-local.ps1`
Use the `.ps1`, never the `.sh` — the shell version writes a broken `/c/...` feed path into Pellucid's `nuget.config`.

- [ ] **Step 2: Run the Windows gates**

Run from `C:/Users/jorda/RiderProjects/Pellucid`:
`dotnet test Pellucid.Rendering.Avalonia.Tests\Pellucid.Rendering.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~GwgRenderHashGate|FullyQualifiedName~GhentScoreboard"`
Expected if nothing moved: 5 passed, 0 failed, 1 skipped (GWG 51/51, Ghent 48/48).

- [ ] **Step 3: If any fixture moved, identify and view it**

For each moved fixture, render the page and **view the crop** against the page's embedded reference before deciding anything. Record which fixture moved, what the crop shows, and which of Tasks 3/4 explains it. If a move traces to Task 5, stop — that is a defect in the collapse.

- [ ] **Step 4: Run the Linux and macOS gates**

llmbox: ssh-mcp profile `llmbox`, dotnet at `~/.dotnet/dotnet`. macmini: ssh-mcp profile `macmini` (password auth), dotnet at `/usr/local/share/dotnet/dotnet`.
**Verify the checked-out SHA on each box before trusting any measurement** — a silent `git pull` failure behind macOS keychain errors (-25308) produced a wrong reading twice in an earlier session. Launch with `nohup … </dev/null & disown`, then poll with an until-grep loop.
macOS baseline for comparison: 19 fixture differences (18 pre-existing ARM64 float divergences at ±1-2 of 255, plus GWG090, which picks a different face there because no Century Schoolbook family is installed).

- [ ] **Step 5: Re-pin only crop-verified moves, and land atomically**

If baselines need re-pinning, the engine merge, the Pellucid baseline commit, and the `ci/dependencies.json` full-40-char SHA bump are **one atomic unit** and must land in the same push. Slice 1 nearly shipped broken on exactly this.

- [ ] **Step 6: Report**

Report the three-box results, any moved fixture with what its crop showed, and the final CI conclusion per job. `package-path` failing is expected and pre-existing: it is `continue-on-error: true` by design and fails on `CS0246 ColourantComponent/ColorantPlacement` because the app uses engine types newer than the last published NuGet release.

---

## Self-Review

**Spec coverage.** Item 1 → Tasks 1-3 (explicit pair, shared fixtures, step 1). Item 2 → Task 4. Item 3 → Task 5. Spec's testing section → the test steps in Tasks 1, 3, 4, 5. Spec's render-gate-risk section → Task 6. Spec's atomic-landing note → Task 6 Step 5. No gaps.

**Placeholder scan.** No TBD/TODO/"similar to Task N"/"add error handling". Every code step carries the actual code.

**Type consistency.** `StyleScore(FontFaceRecord, bool, bool) → int` is defined in Task 3 Step 3 and used in Task 3 Step 4, Task 4 Step 1 and Task 4 Step 3 under that exact name. `FontRequest`'s two new members are named `ExplicitBold`/`ExplicitItalic` in Task 1 and referenced under those names in Tasks 1 and 3. `SfntFixtures.Sfnt`/`SfntFixtures.Ttc` are produced in Task 2 and consumed in Task 3. `BetterStyledSibling` is defined and called only within Task 3.

**One ordering note found in review:** Task 3 extracts `StyleScore` but deliberately leaves `PickBest`'s tie-break comparison alone, and Task 4 then rewrites that comparison. Both tasks touch `PickBest`, so Task 4 must run after Task 3 — its Step 3 code block assumes `StyleScore` already exists.
