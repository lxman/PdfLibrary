# Text-String Encoding (PDFDocEncoding ⇄ UTF-16BE) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement
> this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. Full API contracts and behavior are in
> `Docs/SpecTextStringEncoding.md` — read it for the rationale.
> **Worktree:** branched from current `master` (`22129b1`). LOCAL/untracked plan — delete on merge.

**Goal:** Text strings round-trip arbitrary Unicode (create → serialize → parse → read) using true
PDFDocEncoding when representable and UTF-16BE-with-BOM otherwise, via one shared codec consumed by
both the `PdfString` primitive and the builder's string serializer.

**Architecture:** A new `PdfDocEncoding` static codec owns the policy. `PdfString` gains
`FromText`/`GetText` and loses the silent Latin-1 write path (implicit `string→PdfString` operator +
public `PdfString(string)` ctor removed, replaced by an explicitly-named `FromByteLiteral`). All
§7.9.2 text sites migrate to the text path; byte sites use `FromByteLiteral`/raw bytes. The builder
(`PdfDocumentWriter`) gets a codec-backed `PdfTextString` helper for its 3 text sites.

**Tech Stack:** C# / .NET 10, xUnit. `System.Text.Encoding` (in-box). No new dependencies.

## Global Constraints

- Target framework .NET 10; library types are `internal` unless already public.
- No new NuGet dependencies.
- All encode/decode is culture-independent — no `CultureInfo`-sensitive calls; tests must pass under
  `InvariantCulture`, `de-DE`, and `fr-FR`.
- Commit after each task (small, verified). Plan + spec stay LOCAL/untracked in `Docs/`, deleted on
  merge — never committed.
- Run tests with: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo`. The 4 pre-existing
  `PdfDocumentTests` failures for missing `PDFs/pdf20examples/*.pdf` fixtures are environmental in a
  worktree (they pass in the main tree) — they are NOT regressions; ignore them and gate on everything
  else being green.

---

### Task 1: `PdfDocEncoding` codec

**Files:**
- Create: `PdfLibrary/Core/Primitives/PdfDocEncoding.cs`
- Test: `PdfLibrary.Tests/Core/PdfDocEncodingTests.cs`

**Interfaces:**
- Produces: `internal static class PdfDocEncoding` with
  `static byte[] Encode(string text)`, `static string Decode(ReadOnlySpan<byte> bytes)`,
  `static bool IsRepresentable(string text)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Globalization;
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Core;

public class PdfDocEncodingTests
{
    [Theory]
    [InlineData("Hello, World!")]          // pure ASCII
    [InlineData("Café René")]              // Latin-1-range accents (single-byte PDFDocEncoding)
    [InlineData("a•b—c")]        // bullet + em dash → PDFDocEncoding 0x80/0x84
    public void Encode_RepresentableText_StaysSingleByte_AndRoundTrips(string text)
    {
        byte[] bytes = PdfDocEncoding.Encode(text);
        Assert.False(bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF); // not UTF-16BE
        Assert.Equal(text, PdfDocEncoding.Decode(bytes));
    }

    [Theory]
    [InlineData("日本語のタイトル")]        // CJK → UTF-16BE
    [InlineData("emoji \U0001F600 here")]  // astral plane → UTF-16BE surrogate pair
    [InlineData("Zoë Ā")]             // Ā (U+0100) not in PDFDocEncoding → UTF-16BE
    public void Encode_NonRepresentableText_UsesUtf16BeWithBom_AndRoundTrips(string text)
    {
        byte[] bytes = PdfDocEncoding.Encode(text);
        Assert.True(bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF);
        Assert.Equal(text, PdfDocEncoding.Decode(bytes));
    }

    [Theory]
    [InlineData(0x80, '•')] // bullet
    [InlineData(0x84, '—')] // em dash
    [InlineData(0x8D, '“')] // left double quote
    [InlineData(0x92, '™')] // trade mark
    [InlineData(0x93, 'ﬁ')] // fi ligature
    [InlineData(0xA0, '€')] // Euro (DIFFERS from Latin-1 NBSP)
    [InlineData(0x18, '˘')] // breve (DIFFERS from Latin-1 control 0x18)
    [InlineData(0xA1, '¡')] // identity with Latin-1 above 0xA0
    public void Decode_DivergentCodePoints_MatchAnnexD(byte b, char expected)
    {
        Assert.Equal(expected.ToString(), PdfDocEncoding.Decode(new[] { b }));
        Assert.Equal(new[] { b }, PdfDocEncoding.Encode(expected.ToString()));
    }

    [Fact]
    public void Decode_DetectsBomVariants()
    {
        // UTF-16BE BOM
        Assert.Equal("Hi", PdfDocEncoding.Decode(new byte[] { 0xFE, 0xFF, 0x00, (byte)'H', 0x00, (byte)'i' }));
        // UTF-8 BOM (PDF 2.0 read-only)
        Assert.Equal("Hi", PdfDocEncoding.Decode(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'H', (byte)'i' }));
        // empty UTF-16BE (BOM only)
        Assert.Equal("", PdfDocEncoding.Decode(new byte[] { 0xFE, 0xFF }));
    }

    [Fact]
    public void RoundTrip_IsCultureIndependent()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "fr-FR", "" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal("Café €", PdfDocEncoding.Decode(PdfDocEncoding.Encode("Café €")));
                Assert.Equal("日本語", PdfDocEncoding.Decode(PdfDocEncoding.Encode("日本語")));
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter PdfDocEncodingTests`
Expected: FAIL — `PdfDocEncoding` does not exist.

- [ ] **Step 3: Implement the codec**

Create `PdfLibrary/Core/Primitives/PdfDocEncoding.cs`. The byte→char table is identity for
`0x00–0x17`, `0x20–0x7E`, and `0xA1–0xFF`; the non-identity points (ISO 32000 Annex D.3) are listed
explicitly below. Bytes `0x7F` and `0x9F` are undefined → decode to `U+FFFD`.

```csharp
using System.Text;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// PDFDocEncoding ⇄ Unicode codec for PDF text strings (ISO 32000-2 §7.9.2, Annex D.3).
/// Encodes to single-byte PDFDocEncoding when every character is representable, otherwise to
/// UTF-16BE with a leading FE FF byte-order mark. Decoding sniffs the BOM (FE FF → UTF-16BE,
/// EF BB BF → UTF-8 for PDF 2.0 inputs) and otherwise decodes PDFDocEncoding.
/// This is for TEXT strings only — byte strings (the /ID, content-stream show-strings, encryption
/// values, name-tree keys) must NOT pass through here.
/// </summary>
internal static class PdfDocEncoding
{
    // Non-identity PDFDocEncoding points: byte → Unicode (Annex D.3). Everything not listed and not
    // in the undefined set is identity (byte value == code point) within 0x00–0xFF.
    private static readonly (byte B, char C)[] Special =
    {
        (0x18, '˘'), (0x19, 'ˇ'), (0x1A, 'ˆ'), (0x1B, '˙'),
        (0x1C, '˝'), (0x1D, '˛'), (0x1E, '˚'), (0x1F, '˜'),
        (0x80, '•'), (0x81, '†'), (0x82, '‡'), (0x83, '…'),
        (0x84, '—'), (0x85, '–'), (0x86, 'ƒ'), (0x87, '⁄'),
        (0x88, '‹'), (0x89, '›'), (0x8A, '−'), (0x8B, '‰'),
        (0x8C, '„'), (0x8D, '“'), (0x8E, '”'), (0x8F, '‘'),
        (0x90, '’'), (0x91, '‚'), (0x92, '™'), (0x93, 'ﬁ'),
        (0x94, 'ﬂ'), (0x95, 'Ł'), (0x96, 'Œ'), (0x97, 'Š'),
        (0x98, 'Ÿ'), (0x99, 'Ž'), (0x9A, 'ı'), (0x9B, 'ł'),
        (0x9C, 'œ'), (0x9D, 'š'), (0x9E, 'ž'), (0xA0, '€'),
    };
    private static readonly byte[] UndefinedBytes = { 0x7F, 0x9F };

    private static readonly char[] ByteToChar = BuildByteToChar();
    private static readonly Dictionary<char, byte> CharToByte = BuildCharToByte();

    private const char Undefined = '￿'; // sentinel in ByteToChar for undefined bytes

    private static char[] BuildByteToChar()
    {
        var map = new char[256];
        for (var i = 0; i < 256; i++) map[i] = (char)i;       // identity baseline
        foreach ((byte b, char c) in Special) map[b] = c;     // Annex D overrides
        foreach (byte b in UndefinedBytes) map[b] = Undefined; // undefined sentinel
        return map;
    }

    private static Dictionary<char, byte> BuildCharToByte()
    {
        var map = new Dictionary<char, byte>(256);
        for (var i = 0; i < 256; i++)
        {
            char c = ByteToChar[i];
            if (c != Undefined) map.TryAdd(c, (byte)i); // first writer wins (identity before specials can't collide)
        }
        return map;
    }

    public static bool IsRepresentable(string text)
    {
        foreach (char c in text)
            if (!CharToByte.ContainsKey(c)) return false;
        return true;
    }

    public static byte[] Encode(string text)
    {
        if (IsRepresentable(text))
        {
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++) bytes[i] = CharToByte[text[i]];
            return bytes;
        }

        byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        Array.Copy(utf16, 0, withBom, 2, utf16.Length);
        return withBom;
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);

        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            char c = ByteToChar[b];
            sb.Append(c == Undefined ? '�' : c);
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter PdfDocEncodingTests`
Expected: PASS (all theories + facts).

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Core/Primitives/PdfDocEncoding.cs PdfLibrary.Tests/Core/PdfDocEncodingTests.cs
git commit -m "feat(text): add PdfDocEncoding codec (PDFDocEncoding ⇄ UTF-16BE)"
```

---

### Task 2: `PdfString.FromText` / `GetText` / `FromByteLiteral` (additive)

**Files:**
- Modify: `PdfLibrary/Core/Primitives/PdfString.cs`
- Test: `PdfLibrary.Tests/Core/PdfStringTextTests.cs`

**Interfaces:**
- Consumes: `PdfDocEncoding.Encode/Decode` (Task 1).
- Produces: `static PdfString PdfString.FromText(string)`, `string PdfString.GetText()`,
  `static PdfString PdfString.FromByteLiteral(string)`. (Old `PdfString(string)` ctor + implicit
  `string→PdfString` operator still present after this task — removed in Task 4.)

- [ ] **Step 1: Write the failing tests**

```csharp
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Core;

public class PdfStringTextTests
{
    [Fact]
    public void FromText_Ascii_RoundTripsViaGetText_AndIsLiteral()
    {
        PdfString s = PdfString.FromText("Hello");
        Assert.Equal("Hello", s.GetText());
        Assert.StartsWith("(", s.ToPdfString());   // literal form
    }

    [Fact]
    public void FromText_Cjk_RoundTripsViaGetText_AndIsHex()
    {
        PdfString s = PdfString.FromText("日本語");
        Assert.Equal("日本語", s.GetText());
        Assert.StartsWith("<FEFF", s.ToPdfString()); // hex form, UTF-16BE BOM
    }

    [Fact]
    public void FromByteLiteral_MatchesLatin1Bytes()
    {
        PdfString s = PdfString.FromByteLiteral("ID-token");
        Assert.Equal(System.Text.Encoding.Latin1.GetBytes("ID-token"), s.Bytes);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter PdfStringTextTests`
Expected: FAIL — `FromText`/`GetText`/`FromByteLiteral` do not exist.

- [ ] **Step 3: Add the three members**

In `PdfLibrary/Core/Primitives/PdfString.cs`, add (do NOT yet remove the existing `PdfString(string)`
ctor or implicit operator):

```csharp
    /// <summary>
    /// Creates a PDF TEXT string (ISO 32000 §7.9.2): single-byte PDFDocEncoding when representable,
    /// otherwise UTF-16BE with a FE FF BOM. UTF-16BE serializes as hex; PDFDocEncoding as a literal.
    /// Use this for Info values, outline titles, annotation /Contents, etc. — NOT for byte strings.
    /// </summary>
    public static PdfString FromText(string text)
    {
        byte[] bytes = PdfDocEncoding.Encode(text);
        bool utf16 = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF;
        return new PdfString(bytes, utf16 ? PdfStringFormat.Hexadecimal : PdfStringFormat.Literal);
    }

    /// <summary>Decodes this string as a PDF text string (BOM-sniffed PDFDocEncoding/UTF-16BE/UTF-8).</summary>
    public string GetText() => PdfDocEncoding.Decode(_bytes);

    /// <summary>
    /// Creates a string from raw byte-literal text via Latin-1 (byte == char). For BYTE strings and
    /// ASCII tokens only (the /ID, PDF date strings, etc.) — never for human-facing text.
    /// </summary>
    public static PdfString FromByteLiteral(string value) =>
        new(Encoding.Latin1.GetBytes(value), PdfStringFormat.Literal);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter PdfStringTextTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Core/Primitives/PdfString.cs PdfLibrary.Tests/Core/PdfStringTextTests.cs
git commit -m "feat(text): add PdfString.FromText/GetText/FromByteLiteral"
```

---

### Task 3: Migrate primitive-side TEXT sites to the text path

**Files:**
- Modify: `PdfLibrary/Editing/PdfMetadata.cs` (write + read of Title/Author/Subject/Keywords/Creator/Producer)
- Modify: `PdfLibrary/Editing/PdfOutlineItem.cs` (replace inlined UTF-16BE codec)
- Modify: `PdfLibrary/Editing/PdfPageLabels.cs` (`/P` prefix write + read)
- Modify: `PdfLibrary/Editing/Annotations/PdfPageAnnotator.cs` (`/Contents`)
- Test: `PdfLibrary.Tests/Editing/TextStringRoundTripTests.cs`

**Interfaces:**
- Consumes: `PdfString.FromText`, `PdfString.GetText` (Task 2).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Globalization;
using PdfLibrary.Editing;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class TextStringRoundTripTests
{
    [Theory]
    [InlineData("Simple Title")]
    [InlineData("Café Ω — 日本語 \U0001F600")] // mixed: forces UTF-16BE
    public void Metadata_Title_RoundTrips_AcrossSaveAndReload(string title)
    {
        using var ms = new MemoryStream();
        using (PdfDocumentEditor edit = PdfDocumentEditor.CreateBlank())
        {
            edit.Pages.InsertBlank(0, 612, 792);
            edit.Metadata.Title = title;
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms); // see note below if Open(Stream) absent
        Assert.Equal(title, reopened.Metadata.Title);
    }

    [Fact]
    public void Outline_Title_RoundTrips_NonAscii()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            using var ms = new MemoryStream();
            using (PdfDocumentEditor edit = PdfDocumentEditor.CreateBlank())
            {
                edit.Pages.InsertBlank(0, 612, 792);
                edit.Outlines.Add("Kapitel — 日本語", PdfDestination.ToPage(0));
                edit.Save(ms);
            }
            ms.Position = 0;
            using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms);
            Assert.Equal("Kapitel — 日本語", reopened.Outlines[0].Title);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
```

> **Note for implementer:** if `PdfDocumentEditor.Open(Stream)` does not exist, use the existing
> file-based `Open(path)` against a temp file (`Path.GetTempFileName()`), and delete the temp file in
> a `finally`. Match whatever `edit.Save`/`Open` overloads the codebase already exposes; do not invent
> new public API for the test. Confirm the exact `edit.Outlines.Add` and `edit.Metadata.Title`
> signatures against the merged #3 facades before writing the test.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter TextStringRoundTripTests`
Expected: FAIL — non-ASCII titles come back corrupted (Latin-1 `?`) or mismatched.

- [ ] **Step 3: Migrate the four sites**

In `PdfMetadata.cs` `SetInfoString` (the text setter), change the write:

```csharp
        else
            info[new PdfName(key)] = PdfString.FromText(value);   // was: new PdfString(value)
```

Find the matching Info getters in `PdfMetadata.cs` (Title/Author/Subject/Keywords/Creator/Producer
`get`) and decode via `GetText()`:

```csharp
        // pattern: read the Info entry then
        return info.Get(new PdfName(key)) is PdfString s ? s.GetText() : null;
```

In `PdfOutlineItem.cs`, delete the inlined UTF-16BE encode/decode helpers (the `Encoding.BigEndianUnicode`
+ `0xFE/0xFF` block around lines 127–144) and route through the codec:

```csharp
    // encode (set Title):
    title is null ? /* remove */ : PdfString.FromText(title)
    // decode (GetTitle):
    return titleObj is PdfString s ? s.GetText() : string.Empty;
```

In `PdfPageLabels.cs`, the `/P` prefix write becomes `PdfString.FromText(prefix)` and the prefix read
becomes `s.GetText()`.

In `PdfPageAnnotator.cs`, the `/Contents` (note text) write becomes `PdfString.FromText(contents)`.

> **Implementer:** grep the four files for `new PdfString(` and `.Value` on these specific text fields
> and convert each. Do NOT touch `SetInfoDate` (PDF date strings stay `FromByteLiteral` — they are
> ASCII) or any name-tree key. Run `grep -rn "Encoding.BigEndianUnicode" PdfLibrary/Editing` afterward
> and confirm zero hits remain (the only one was the now-deleted `PdfOutlineItem` codec).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter TextStringRoundTripTests`
Then the existing edit suites: `--filter "OutlineEditTests|PageLabelEditTests|PdfMetadataTests"`
Expected: PASS, and the pre-existing edit tests stay green.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Editing/PdfMetadata.cs PdfLibrary/Editing/PdfOutlineItem.cs \
        PdfLibrary/Editing/PdfPageLabels.cs PdfLibrary/Editing/Annotations/PdfPageAnnotator.cs \
        PdfLibrary.Tests/Editing/TextStringRoundTripTests.cs
git commit -m "feat(text): route edit-side text strings through PdfDocEncoding"
```

---

### Task 4: Remove the silent write path; reclassify byte sites

**Files:**
- Modify: `PdfLibrary/Core/Primitives/PdfString.cs` (remove ctor + implicit operator)
- Modify: every file the compiler flags (mechanical reclassification to `FromByteLiteral`)
- Test: `PdfLibrary.Tests/Core/PdfStringNoImplicitTests.cs`

**Interfaces:**
- Produces: `PdfString` with NO public `string` ctor and NO implicit `string→PdfString` operator.

- [ ] **Step 1: Write the failing guard test**

```csharp
using System.Linq;
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Core;

public class PdfStringNoImplicitTests
{
    [Fact]
    public void PdfString_HasNoPublicStringConstructor()
    {
        bool hasStringCtor = typeof(PdfString)
            .GetConstructors()
            .Any(c => c.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        Assert.False(hasStringCtor, "Public PdfString(string) ctor must be removed (use FromText/FromByteLiteral).");
    }

    [Fact]
    public void PdfString_HasNoImplicitStringConversion()
    {
        bool hasImplicit = typeof(PdfString)
            .GetMethods()
            .Any(m => m.Name == "op_Implicit" && m.ReturnType == typeof(PdfString)
                      && m.GetParameters() is [{ ParameterType: var t }] && t == typeof(string));
        Assert.False(hasImplicit, "Implicit string→PdfString operator must be removed.");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter PdfStringNoImplicitTests`
Expected: FAIL — both the ctor and the operator still exist.

- [ ] **Step 3: Remove the two members, then fix every compile error**

In `PdfString.cs` delete:

```csharp
    public PdfString(string value, PdfStringFormat format = PdfStringFormat.Literal)
        : this(Encoding.Latin1.GetBytes(value), format) { }
    // ...
    public static implicit operator PdfString(string value) => new(value);
```

(Keep the `implicit operator string(PdfString)` read operator and everything else.)

Build: `dotnet build PdfLibrary/PdfLibrary.csproj --nologo`. For EACH compile error:
- If the string is **human-facing text** that escaped the Task 3 audit → `PdfString.FromText(x)`.
- If it is an **ASCII token / byte literal** (PDF date, a fixed keyword, a name-as-string, the `/ID`,
  an encryption value) → `PdfString.FromByteLiteral(x)`.

Repeat build-fix until the library compiles. Then build the test project and fix any test-side uses
the same way.

> **Implementer guidance:** the overwhelming majority of flagged sites are byte/ASCII →
> `FromByteLiteral`. When unsure whether a site is text or bytes, check what PDF key it populates: keys
> in the §7.9.2 text-string list (Info, /Contents, /T, /TU, outline /Title, bookmark titles) are text;
> everything else (/ID, /Type-ish strings, name-tree keys, content operands) is bytes. Do not change
> behavior — `FromByteLiteral` reproduces the old Latin-1 ctor exactly, so byte sites are byte-for-byte
> unchanged.

- [ ] **Step 4: Run the guard test + full suite**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo`
Expected: `PdfStringNoImplicitTests` PASS; whole suite green (modulo the 4 environmental fixture
failures noted in Global Constraints).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(text): remove silent Latin-1 string path; reclassify byte sites to FromByteLiteral"
```

---

### Task 5: Builder-side text strings (`PdfDocumentWriter`)

**Files:**
- Modify: `PdfLibrary/Builder/PdfDocumentWriter.cs` (add `PdfTextString` helper; route Info Title,
  bookmark Title, page-label `/P` prefix)
- Test: `PdfLibrary.Tests/Builder/BuilderTextStringTests.cs`

**Interfaces:**
- Consumes: `PdfDocEncoding.Encode` (Task 1). The builder is a string serializer (writes tokens to a
  `StreamWriter`), so it uses the codec directly, not `PdfString`.

- [ ] **Step 1: Write the failing test**

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Builder;

public class BuilderTextStringTests
{
    [Fact]
    public void Builder_NonAsciiTitle_RoundTripsThroughParser()
    {
        var builder = new PdfDocumentBuilder();           // confirm exact builder entry point/API
        builder.AddPage(p => { });                        // minimal one-page doc
        builder.Metadata.Title = "Café — 日本語";          // confirm metadata-set API on the builder

        byte[] pdf = builder.Build();                     // confirm Build()/byte[] output API

        using PdfDocument doc = PdfDocument.Load(pdf);     // or via a temp file if no byte[] Load
        Assert.Equal("Café — 日本語", doc.Edit().Metadata.Title);
    }
}
```

> **Note for implementer:** the builder's public API names (`PdfDocumentBuilder`, how a page is added,
> how Title is set, how bytes are produced) must be confirmed against the actual builder before writing
> this test — use the real API, do not invent it. If the builder cannot set Title directly, set it via
> whatever metadata/Info entry point the builder exposes. If no in-memory `Build()`/`Load(byte[])`
> exists, write to a temp file and delete it in a `finally`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter BuilderTextStringTests`
Expected: FAIL — title comes back corrupted (Latin-1/ASCII mangling).

- [ ] **Step 3: Add the codec-backed helper and route the three text sites**

In `PdfDocumentWriter.cs` add a helper that produces a complete PDF string token (literal or hex),
encrypting when an encryptor is present:

```csharp
    private string PdfTextString(string text, int objectNumber)
    {
        byte[] bytes = PdfDocEncoding.Encode(text); // PDFDocEncoding or UTF-16BE+BOM
        if (_encryptor != null)
        {
            byte[] encrypted = EncryptString(bytes, objectNumber);
            return $"<{BytesToHexString(encrypted)}>";
        }
        bool utf16 = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF;
        // UTF-16BE (or any non-ASCII byte) → hex; pure-ASCII → literal (stable, human-readable output)
        bool asciiOnly = !utf16 && Array.TrueForAll(bytes, b => b is >= 0x20 and <= 0x7E);
        return asciiOnly
            ? $"({EscapePdfString(text)})"
            : $"<{BytesToHexString(bytes)}>";
    }
```

Route the three TEXT sites to it (replace the `PdfEncryptedString(...)` calls at these sites):
- Info `/Title` (≈ line 369): `writer.WriteLine($"   /Title {PdfTextString(meta.Title, infoObj)}");`
- Bookmark `/Title` (≈ line 2039): `writer.WriteLine($"   /Title {PdfTextString(bookmark.Title, objNum)}");`
- Page-label `/P` prefix (≈ line 2186): `writer.Write($"/P {PdfTextString(range.Prefix, <objNum>)} ");`
  — if the page-label number tree is written unencrypted/without an object context, pass the object
  number that owns the tree; if there is none in scope, add an unencrypted overload
  `PdfTextString(string text)` that skips the `_encryptor` branch and use it here.

Leave `PdfEncryptedString`/`PdfString`/`PdfDate` in place for the remaining ASCII/byte/date sites
(Producer/CreationDate etc. that are ASCII; dates stay as-is).

> **Implementer:** confirm `BytesToHexString` and `EscapePdfString` signatures (both already exist in
> this file). Verify the exact line numbers/fields before editing — line numbers above are approximate.
> Only the three human-facing text fields move to `PdfTextString`; do not reroute the `/ID`, stream
> data, or content operands.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo --filter BuilderTextStringTests`
Then the builder regression suite: `--filter "Builder"`
Expected: PASS; existing builder tests stay green.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Builder/PdfDocumentWriter.cs PdfLibrary.Tests/Builder/BuilderTextStringTests.cs
git commit -m "feat(text): route builder Info/bookmark/page-label text through PdfDocEncoding"
```

---

### Task 6: Full-suite verification + adversarial review

**Files:** none new (fixes only, if findings).

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --nologo`
Expected: green except the 4 environmental `pdf20examples` fixture failures (Global Constraints).

- [ ] **Step 2: Byte-string non-regression spot checks**

Add/confirm tests that:
- a loaded doc's trailer `/ID` round-trips byte-identical through `edit.Save` (no text re-encoding),
- content-stream `Tj` text extraction is unchanged (still via `Value`),
- a named-destination name with non-ASCII bytes is preserved as bytes (not text-re-encoded).

Run them; expected PASS.

- [ ] **Step 3: Adversarial review (opus subagent)**

Dispatch an adversarial code review (model: opus) focused on: Annex D table fidelity (esp. `0x80–0x9F`,
`0xA0` Euro, `0x18–0x1F`, undefined `0x7F`/`0x9F`); that NO byte-string site was wrongly routed to
`FromText`; encryption path still encrypts the post-encoding bytes; culture-independence; GC/serializer
unaffected. Fix any findings (each as its own minimal change + test).

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "test(text): byte-string non-regression + review fixes"
```

---

## Self-Review (filled in by author)

- **Spec coverage:** §Decisions 1–4 → Tasks 1,2,5 (encoding model + format), Task 2/4 (A+ API), Tasks
  3/5 (audit), Task 4 (remove foot-gun). §Components `PdfDocEncoding` → Task 1; `PdfString` → Tasks 2/4;
  call-site migration → Tasks 3/5; encryption ordering → Task 5 helper + Task 6 review. §Testing → the
  per-task tests + Task 6. All spec sections map to a task.
- **Type consistency:** `PdfDocEncoding.Encode/Decode/IsRepresentable`, `PdfString.FromText/GetText/
  FromByteLiteral` used identically in every task that references them.
- **Excluded (byte) sites** never routed to `FromText`; `FromByteLiteral` reproduces old Latin-1 bytes
  exactly, so byte sites are behavior-identical.
