# Hex-String and Integer-Range Byte Fidelity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect PDF/A-2b clause 6.1.6 (tests 1 and 2) and clause 6.1.13 (test 1) by preserving two facts the lexer and content parser currently normalise away, lifting veraPDF verdict parity from 972/986 to 976/986.

**Architecture:** The lexer records, per hex-string token position, the non-white-space digit count and whether a non-hex character appeared; both parsers attach those facts to the `PdfString` they build. Separately, the content parser marks a `PdfInteger` whose source literal was outside Int32 range **without changing the parsed value** (still `0`), so nothing the renderer sees moves. Two rules then read those facts — a new detect-only rule for 6.1.6, and a new sub-check inside the existing `ImplementationLimitsRule` for 6.1.13 test 1.

**Tech Stack:** C#, .NET 10, xUnit. Engine repo `PdfLibrary` only — no Pellucid changes.

**Spec:** `Docs/superpowers/specs/2026-08-20-hex-and-integer-byte-fidelity-design.md`

## Global Constraints

- **Zero false positives across all 1316 corpus files.** This is a standing invariant of the parity harness and is never traded for coverage. If any arm of a new rule produces a finding veraPDF does not, narrow that arm — do not weaken the check for the invariant.
- **No parsed value may change.** An out-of-range content-stream integer operand still becomes `0`. Render baselines are byte-identical across Windows, Linux and macOS and must not move.
- **Equality semantics must not change.** `PdfString.Equals`/`GetHashCode` compare bytes only; `PdfInteger.Equals`/`GetHashCode` compare `LongValue` only. New members stay out of both.
- **`PdfToken` must not grow.** It is a by-value `readonly struct` on the hot content-stream path.
- **Detection only.** No remediation, no `pellucid fix` wiring, no Pellucid changes.
- Target clause strings come from `ConformanceClauses.For(context.Target, "<clause>")` — never hardcode `"ISO 19005-2:2011, …"`.
- Every guard test must be **probed**: delete the guard, confirm the test fails, restore. A previous slice shipped a guard test that passed without its guard.
- Work on branch `feat/parity-616-613-byte-fidelity`, already created off `master` at `7ec0413`.

---

### Task 1: Capture hex-string facts in the lexer

**Files:**
- Create: `PdfLibrary/Core/Primitives/HexStringFacts.cs`
- Modify: `PdfLibrary/Parsing/PdfLexer.cs` (add `using`, add field + accessor, extend `ReadHexStringOrDictionaryStart` at `:184-241`)
- Test: `PdfLibrary.Tests/Parsing/PdfLexerHexFactsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `PdfLibrary.Core.Primitives.HexStringFacts` — `internal readonly record struct HexStringFacts(int NonWhitespaceCount, bool HasNonHexDigit)`. And on `PdfLexer`: `internal bool TryTakeHexFacts(long position, out HexStringFacts facts)`.

`HexStringFacts` lives in `Core.Primitives`, not `Parsing`, because Task 2 hangs it off `PdfString`; `Parsing` already depends on `Core.Primitives`, and the reverse dependency would be a cycle.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Parsing/PdfLexerHexFactsTests.cs`:

```csharp
using System.IO;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;
using Xunit;

namespace PdfLibrary.Tests.Parsing;

/// <summary>
/// ISO 19005-2 clause 6.1.6 needs two facts about how a hexadecimal string was WRITTEN that
/// <see cref="PdfLexer.ReadHexStringOrDictionaryStart"/> normalises away: the count of
/// non-white-space characters between the angle brackets (test 1 wants it even) and whether any of
/// them was outside [0-9A-Fa-f] (test 2 forbids it). These pin the capture at the point of the read.
/// </summary>
public class PdfLexerHexFactsTests
{
    private static (PdfToken Token, HexStringFacts? Facts) Lex(string raw)
    {
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes(raw)));
        PdfToken token = lexer.NextToken();
        return (token, lexer.TryTakeHexFacts(token.Position, out HexStringFacts f) ? f : null);
    }

    [Theory]
    [InlineData("<48455>", 5)]              // corpus 6-1-6-t01-fail-a: odd
    [InlineData("<484550>", 6)]             // even
    [InlineData("<48 45 50>", 6)]           // whitespace is NOT counted
    [InlineData("<>", 0)]                   // empty is even, and legal
    public void The_non_whitespace_count_excludes_whitespace(string raw, int expected)
    {
        Assert.Equal(expected, Lex(raw).Facts!.Value.NonWhitespaceCount);
    }

    [Theory]
    [InlineData("<484!>", true)]            // corpus 6-1-6-t02-fail-a
    [InlineData("<48zz>", true)]
    [InlineData("<484550>", false)]
    [InlineData("<abcDEF09>", false)]       // both cases plus digits are all legal hex
    public void A_non_hex_character_is_detected(string raw, bool expected)
    {
        Assert.Equal(expected, Lex(raw).Facts!.Value.HasNonHexDigit);
    }

    [Fact]
    public void A_non_hex_character_in_the_second_half_of_a_pair_is_detected()
    {
        // The flag must come from testing each CHARACTER. Deriving it from byte.TryParse failing
        // would depend on pair alignment: "4!" fails as a unit, but "!4" is a different pair.
        Assert.True(Lex("<!4>").Facts!.Value.HasNonHexDigit);
    }

    [Fact]
    public void A_dictionary_start_records_no_facts()
    {
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes("<</Type /Catalog>>")));
        PdfToken token = lexer.NextToken();

        Assert.Equal(PdfTokenType.DictionaryStart, token.Type);
        Assert.False(lexer.TryTakeHexFacts(token.Position, out _));
    }

    [Fact]
    public void Facts_are_taken_only_once()
    {
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes("<48455>")));
        PdfToken token = lexer.NextToken();

        Assert.True(lexer.TryTakeHexFacts(token.Position, out _));
        Assert.False(lexer.TryTakeHexFacts(token.Position, out _));
    }

    [Fact]
    public void Each_hex_string_keeps_its_own_facts_across_an_interleaved_read()
    {
        // The reason the table is keyed by POSITION rather than "the last hex string read":
        // PdfParser lexes ahead (PeekToken) before turning a buffered token into a PdfString, so a
        // "last one wins" channel would hand the second string's facts to the first.
        var lexer = new PdfLexer(new MemoryStream(Encoding.ASCII.GetBytes("<48455> <484550>")));
        PdfToken first = lexer.NextToken();
        PdfToken second = lexer.NextToken();

        Assert.True(lexer.TryTakeHexFacts(second.Position, out HexStringFacts secondFacts));
        Assert.True(lexer.TryTakeHexFacts(first.Position, out HexStringFacts firstFacts));
        Assert.Equal(5, firstFacts.NonWhitespaceCount);
        Assert.Equal(6, secondFacts.NonWhitespaceCount);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfLexerHexFactsTests"`
Expected: FAIL to compile — `'PdfLexer' does not contain a definition for 'TryTakeHexFacts'` and `HexStringFacts` not found.

- [ ] **Step 3: Create the facts type**

Create `PdfLibrary/Core/Primitives/HexStringFacts.cs`:

```csharp
namespace PdfLibrary.Core.Primitives;

/// <summary>
/// How a hexadecimal string was WRITTEN — the two facts ISO 19005-2 clause 6.1.6 constrains and the
/// lexer would otherwise normalise away.
///
/// <para><see cref="NonWhitespaceCount"/> is the number of characters between the angle brackets
/// after white space is removed; test 1 requires it to be even. <see cref="HasNonHexDigit"/> is set
/// when any of those characters falls outside <c>[0-9A-Fa-f]</c>, which test 2 forbids.</para>
///
/// <para>Both are gone by the time a <see cref="PdfString"/> exists: the lexer strips white space,
/// pads an odd trailing nibble with '0' per ISO 32000-1 §7.3.4.3, and silently drops a pair it
/// cannot parse. They are captured at the point of the read instead.</para>
/// </summary>
internal readonly record struct HexStringFacts(int NonWhitespaceCount, bool HasNonHexDigit);
```

- [ ] **Step 4: Add the side table to the lexer**

In `PdfLibrary/Parsing/PdfLexer.cs`, add the using at the top of the file (after `using System.Text;` on line 2):

```csharp
using PdfLibrary.Core.Primitives;
```

Then add the field and accessor immediately after the `Delimiters` set (currently ending line 36):

```csharp
    // Hex-string facts for ISO 19005-2 clause 6.1.6, keyed by the Position of the token they belong
    // to. Keyed rather than "the last hex string read" because PdfParser buffers tokens for
    // lookahead (PeekToken/PushBackToken, PdfParser.cs:93-121) and ParseIntegerOrReference lexes a
    // FURTHER token before pushing one back — so a hex token can be lexed well before the parser
    // turns it into a PdfString. TryTakeHexFacts REMOVES the entry, so this holds only the tokens
    // currently in flight (two at most in practice) rather than growing with the document.
    private readonly Dictionary<long, HexStringFacts> _hexFacts = [];

    /// <summary>
    /// Retrieves and REMOVES the facts recorded for the hexadecimal string token that starts at
    /// <paramref name="position"/>. Returns false when no hex string was read at that position, or
    /// when its facts have already been taken.
    /// </summary>
    internal bool TryTakeHexFacts(long position, out HexStringFacts facts) =>
        _hexFacts.Remove(position, out facts);
```

- [ ] **Step 5: Record the facts when reading a hex string**

In `ReadHexStringOrDictionaryStart`, find this line (currently `:220`, just after the collection loop and the closing `>` skip):

```csharp
        var hexString = hexDigits.ToString();
```

Insert immediately after it:

```csharp
        // ISO 19005-2 6.1.6: record what the rest of this method is about to normalise away — the
        // non-white-space digit count (test 1 wants it even) and whether any character fell outside
        // [0-9A-Fa-f] (test 2 forbids it). Tested per CHARACTER rather than by watching the
        // byte.TryParse below fail: TryParse works on PAIRS, so "4!" fails as a unit and a flag
        // derived from it would depend on where the pair boundaries happen to land.
        var hasNonHexDigit = false;
        foreach (char c in hexString)
        {
            if (Uri.IsHexDigit(c)) continue;
            hasNonHexDigit = true;
            break;
        }

        _hexFacts[position] = new HexStringFacts(hexString.Length, hasNonHexDigit);
```

`position` is the local already captured at the top of the method and passed to the returned `PdfToken`, so the key and the token agree by construction.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfLexerHexFactsTests"`
Expected: PASS, all tests.

- [ ] **Step 7: Probe the guard**

Temporarily change `if (Uri.IsHexDigit(c)) continue;` to `if (true) continue;` and re-run.
Expected: `A_non_hex_character_is_detected` and `A_non_hex_character_in_the_second_half_of_a_pair_is_detected` FAIL. Restore the line.

- [ ] **Step 8: Run the full lexer and parser suites for regressions**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Parsing"`
Expected: PASS — the change is additive and no existing behaviour moves.

- [ ] **Step 9: Commit**

```bash
git add PdfLibrary/Core/Primitives/HexStringFacts.cs PdfLibrary/Parsing/PdfLexer.cs PdfLibrary.Tests/Parsing/PdfLexerHexFactsTests.cs
git commit -m "feat(parsing): capture how a hex string was written (ISO 19005-2 6.1.6)

The lexer strips whitespace, pads an odd trailing nibble and drops an
unparseable pair, so by the time a PdfString exists the two facts clause
6.1.6 constrains are gone. Record them at the read, keyed by token
position so PdfParser's lookahead cannot hand one string's facts to
another. Nothing consumes them yet."
```

---

### Task 2: Attach the facts to `PdfString` at every construction site

**Files:**
- Modify: `PdfLibrary/Core/Primitives/PdfString.cs:9-13` (constructor + property), `:136-137` (`FromByteLiteral`)
- Modify: `PdfLibrary/Content/PdfContentParser.cs:25-29` (helper), `:87`, `:341`, `:394`, `:454`
- Modify: `PdfLibrary/Parsing/PdfParser.cs:223-253` (`ParseString`)
- Test: `PdfLibrary.Tests/Parsing/HexStringFactsPropagationTests.cs`

**Interfaces:**
- Consumes: `HexStringFacts`, `PdfLexer.TryTakeHexFacts` from Task 1.
- Produces: `PdfString.HexFacts` (`HexStringFacts?`, null for literal strings and for any string synthesised in memory); `PdfString.FromByteLiteral(string value, PdfStringFormat format = PdfStringFormat.Literal, HexStringFacts? hexFacts = null)`; `PdfString(byte[] bytes, PdfStringFormat format = PdfStringFormat.Literal, HexStringFacts? hexFacts = null)`.

**There are six construction sites**, four in `PdfContentParser` and two in `PdfParser`. The corpus only exercises one of them (the content-stream operand stack). Missing one of the other five would ship green and silently under-report — this repo has hit that seam four separate times, which is why Step 1 enumerates all of them.

> **Correction made during execution.** The Step 1 code below covers Site_1 through Site_5 only — it
> omits Site 6, the post-decryption reconstruction, which is precisely the kind of gap this task's own
> argument warns about. A `Site_6_object_parser_after_decryption` test was added in a fix round; it
> drives a real RC4-40 encrypt/decrypt round trip so the `_decryptor is not null` branch is genuinely
> entered. Anyone re-running this plan from scratch must write that seventh test too.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Parsing/HexStringFactsPropagationTests.cs`:

```csharp
using System.Linq;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;
using Xunit;

namespace PdfLibrary.Tests.Parsing;

/// <summary>
/// Every path that turns a hexadecimal token into a <see cref="PdfString"/> must carry the clause
/// 6.1.6 facts with it. There are SIX such sites — four in <see cref="PdfContentParser"/> (operand
/// stack, array, dictionary, inline image) and two in <see cref="PdfParser"/> (initial parse and
/// re-construction after decryption). The corpus exercises only the operand stack, so these tests
/// are the only thing standing between the other five and a silent under-report.
/// </summary>
public class HexStringFactsPropagationTests
{
    private static PdfObject[] ContentOperands(string content) =>
        PdfContentParser.Parse(Encoding.ASCII.GetBytes(content))
            .SelectMany(op => op.Operands)
            .ToArray();

    private static void AssertOdd(PdfObject? o)
    {
        PdfString s = Assert.IsType<PdfString>(o);
        Assert.NotNull(s.HexFacts);
        Assert.Equal(5, s.HexFacts!.Value.NonWhitespaceCount);
        Assert.False(s.HexFacts!.Value.HasNonHexDigit);
    }

    [Fact]
    public void Site_1_content_stream_operand_stack()
    {
        // Exactly corpus fixture 6-1-6-t01-fail-a.pdf: "<48455> Tj".
        AssertOdd(ContentOperands("BT <48455> Tj ET").FirstOrDefault(o => o is PdfString));
    }

    [Fact]
    public void Site_2_content_stream_array()
    {
        var array = Assert.IsType<PdfArray>(
            ContentOperands("BT [<48455> -100 (x)] TJ ET").First(o => o is PdfArray));
        AssertOdd(array.First(o => o is PdfString));
    }

    [Fact]
    public void Site_3_content_stream_dictionary()
    {
        var dict = Assert.IsType<PdfDictionary>(
            ContentOperands("/OC <</Name <48455>>> BDC EMC").First(o => o is PdfDictionary));
        AssertOdd(dict[new PdfName("Name")]);
    }

    [Fact]
    public void Site_4_content_stream_inline_image()
    {
        PdfOperator inline = PdfContentParser
            .Parse(Encoding.ASCII.GetBytes("BI /W 1 /H 1 /BPC 8 /CS /G /Foo <48455> ID \u0000 EI"))
            .First(op => op is InlineImageOperator);

        var image = Assert.IsType<InlineImageOperator>(inline);
        AssertOdd(image.Parameters[new PdfName("Foo")]);
    }

    [Fact]
    public void Site_5_object_parser()
    {
        var parser = new PdfParser(new System.IO.MemoryStream(Encoding.ASCII.GetBytes("<48455>")));
        AssertOdd(parser.ReadObject());
    }

    [Fact]
    public void A_literal_string_carries_no_facts()
    {
        var parser = new PdfParser(new System.IO.MemoryStream(Encoding.ASCII.GetBytes("(HEP)")));
        Assert.Null(Assert.IsType<PdfString>(parser.ReadObject()).HexFacts);
    }

    [Fact]
    public void An_in_memory_string_carries_no_facts()
    {
        // Nothing synthesised in memory has a written form to be wrong about, so 6.1.6 cannot
        // constrain it and the rule in Task 3 must stay silent.
        Assert.Null(PdfString.FromText("hello").HexFacts);
        Assert.Null(PdfString.FromByteLiteral("hello").HexFacts);
        Assert.Null(new PdfString([0x41, 0x42]).HexFacts);
    }

    [Fact]
    public void Facts_stay_out_of_equality()
    {
        // Two strings with the same bytes must remain equal however they were written, or dictionary
        // keys and de-duplication shift underneath unrelated code.
        var withFacts = new PdfString([0x48], PdfStringFormat.Hexadecimal, new HexStringFacts(5, true));
        var without = new PdfString([0x48]);

        Assert.Equal(withFacts, without);
        Assert.Equal(withFacts.GetHashCode(), without.GetHashCode());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~HexStringFactsPropagationTests"`
Expected: FAIL to compile — `'PdfString' does not contain a definition for 'HexFacts'`.

- [ ] **Step 3: Add the property to `PdfString`**

In `PdfLibrary/Core/Primitives/PdfString.cs`, replace the class declaration and fields (`:9-13`):

```csharp
internal sealed class PdfString(
    byte[] bytes,
    PdfStringFormat format = PdfStringFormat.Literal,
    HexStringFacts? hexFacts = null)
    : PdfObject
{
    private readonly byte[] _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    private readonly PdfStringFormat _format = format;

    /// <summary>
    /// How this string was written between the angle brackets, when it was PARSED from a hexadecimal
    /// source — the facts ISO 19005-2 clause 6.1.6 constrains. Null for a literal string and for any
    /// string synthesised in memory, which has no written form to be wrong about.
    ///
    /// <para>Deliberately NOT part of <see cref="Equals(object?)"/> or
    /// <see cref="GetHashCode"/>: two strings with the same bytes are the same string however they
    /// were written, and folding this in would move dictionary keys and de-duplication.</para>
    /// </summary>
    public HexStringFacts? HexFacts { get; } = hexFacts;
```

Then replace `FromByteLiteral` (`:136-137`) with:

```csharp
    public static PdfString FromByteLiteral(
        string value,
        PdfStringFormat format = PdfStringFormat.Literal,
        HexStringFacts? hexFacts = null) =>
        new(Encoding.Latin1.GetBytes(value), format, hexFacts);
```

Leave `Equals` and `GetHashCode` (`:101-110`) untouched.

- [ ] **Step 4: Route all four content-stream sites through one helper**

In `PdfLibrary/Content/PdfContentParser.cs`, replace the `FormatOf` helper (`:25-29`) with:

```csharp
    /// <summary>How a string token was WRITTEN, so re-serializing preserves it (issue 57). The decoded
    /// bytes are identical for both token types — the lexer has already turned <c>&lt;48656C6C6F&gt;</c>
    /// into the same Latin-1 text a literal would produce.</summary>
    private static PdfStringFormat FormatOf(PdfTokenType type) =>
        type == PdfTokenType.HexString ? PdfStringFormat.Hexadecimal : PdfStringFormat.Literal;

    /// <summary>
    /// Builds a string operand, carrying the clause 6.1.6 facts the lexer recorded for this token.
    /// EVERY string construction in this parser goes through here — there are four (operand stack,
    /// array, dictionary, inline image) and a site that bypassed it would stop reporting 6.1.6 for
    /// that position alone, which no corpus fixture would catch.
    /// </summary>
    private static PdfString MakeString(PdfLexer lexer, PdfToken token)
    {
        HexStringFacts? facts =
            token.Type == PdfTokenType.HexString
            && lexer.TryTakeHexFacts(token.Position, out HexStringFacts f)
                ? f
                : null;

        return PdfString.FromByteLiteral(token.Value, FormatOf(token.Type), facts);
    }
```

Now replace each of the four call sites. At `:87` (operand stack, inside `Parse(Stream)`):

```csharp
                    operands.Push(MakeString(lexer, token));
```

At `:341` (inside `ParseArray`):

```csharp
                    array.Add(MakeString(lexer, token));
```

At `:394` (inside `ParseDictionary`) and `:454` (inside `ParseInlineImage`), both switch-expression arms of the form `PdfTokenType.String or PdfTokenType.HexString => PdfString.FromByteLiteral(...)`:

```csharp
                        MakeString(lexer, token),
```

Every one of these four methods already has `lexer` in scope — `Parse(Stream stream)` declares it at `:51`, and `ParseArray`, `ParseDictionary` and `ParseInlineImage` each take `PdfLexer lexer` as a parameter.

- [ ] **Step 5: Carry the facts through the object parser**

In `PdfLibrary/Parsing/PdfParser.cs`, in `ParseString` (`:223`), replace the block that computes `format` and builds the string:

```csharp
        PdfStringFormat format = token.Type == PdfTokenType.HexString
            ? PdfStringFormat.Hexadecimal
            : PdfStringFormat.Literal;

        // Clause 6.1.6: how the hex digits were written, which the lexer normalised away. Taken by
        // token POSITION, because this parser buffers tokens for lookahead and may have lexed
        // further before reaching here.
        HexStringFacts? hexFacts =
            token.Type == PdfTokenType.HexString
            && _lexer.TryTakeHexFacts(token.Position, out HexStringFacts f)
                ? f
                : null;

        PdfString pdfString = PdfString.FromByteLiteral(token.Value, format, hexFacts);
```

And in the decryption branch, replace the re-construction (`:253`):

```csharp
            pdfString = new PdfString(decryptedBytes, format, hexFacts);
```

Decryption changes the bytes, never how the string was written — the facts carry forward for the same reason `format` already does.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~HexStringFactsPropagationTests"`
Expected: PASS, all eight tests.

- [ ] **Step 7: Probe the seam guard**

Temporarily revert the `:341` array site to `PdfString.FromByteLiteral(token.Value, FormatOf(token.Type))` and re-run.
Expected: `Site_2_content_stream_array` FAILS while the other sites still pass — proving each site is independently covered. Restore.

- [ ] **Step 8: Run the full suite for regressions**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`
Expected: PASS. Pay particular attention to issue-57 round-trip tests — `format` handling is untouched, so `<…>` strings must still re-serialize as hex.

- [ ] **Step 9: Commit**

```bash
git add PdfLibrary/Core/Primitives/PdfString.cs PdfLibrary/Content/PdfContentParser.cs PdfLibrary/Parsing/PdfParser.cs PdfLibrary.Tests/Parsing/HexStringFactsPropagationTests.cs
git commit -m "feat(parsing): carry hex-string facts onto PdfString at all six sites

Four content-stream sites and two object-parser sites, each routed
through one helper so a future site cannot quietly skip the capture.
Equality is untouched: two strings with the same bytes stay equal
however they were written."
```

---

### Task 3: The 6.1.6 rule

**Files:**
- Create: `PdfLibrary/Conformance/Rules/HexStringFormatRule.cs`
- Modify: `PdfLibrary/Conformance/Preflighter.cs` (one line in the `Rules` list, after `XrefTableSpacingRule` at `:24`)
- Test: `PdfLibrary.Tests/Conformance/HexStringFormatRuleTests.cs`

**Interfaces:**
- Consumes: `PdfString.HexFacts` from Task 2.
- Produces: rule id `"hex-string-format"`, emitting `Finding`s on clause `6.1.6`.

Detect-only: a rule needs exactly one line in `Preflighter.cs` and no Pellucid routing. `XrefTableSpacingRule` and `XmpPacketHeaderRule` are both registered this way and Pellucid routes neither.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Conformance/HexStringFormatRuleTests.cs`:

```csharp
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A clause 6.1.6 (<see cref="HexStringFormatRule"/>), calibrated against veraPDF's PDFA-2B
/// rules: test 1 <c>(isHex != true) || hexCount % 2 == 0</c> and test 2
/// <c>(isHex != true) || containsOnlyHex == true</c>. Corpus fixtures
/// "veraPDF test suite 6-1-6-t01-fail-a.pdf" (<c>&lt;48455&gt; Tj</c>) and "…-t02-fail-a.pdf"
/// (<c>&lt;484!&gt; Tj</c>) are the end-to-end proof; these pin the logic without the corpus, which
/// the <c>Category=Parity</c> tests skip when it is absent.
/// </summary>
public class HexStringFormatRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static Finding[] Findings(PdfDocument doc) =>
        [.. new HexStringFormatRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))];

    /// <summary>A one-page document whose /Contents is <paramref name="pageContent"/>, parsed from
    /// BYTES so the lexer actually runs — an in-memory PdfString carries no written form.</summary>
    private static PdfDocument ContentDoc(string pageContent)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary(),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void An_odd_digit_count_in_page_content_is_flagged()
    {
        Finding f = Assert.Single(Findings(ContentDoc("BT <48455> Tj ET")));

        Assert.Equal("hex-string-format", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("6.1.6", f.Clause, System.StringComparison.Ordinal);
        Assert.Contains("odd", f.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_hex_character_in_page_content_is_flagged()
    {
        Finding f = Assert.Single(Findings(ContentDoc("BT <484!> Tj ET")));
        Assert.Contains("hexadecimal", f.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("BT <484550> Tj ET")]
    [InlineData("BT <48 45 50> Tj ET")]   // interior white space is legal and not counted
    [InlineData("BT <> Tj ET")]           // empty is even
    [InlineData("BT (HEP) Tj ET")]        // a literal string is not constrained at all
    public void A_well_formed_string_is_accepted(string content)
    {
        Assert.Empty(Findings(ContentDoc(content)));
    }

    [Fact]
    public void An_in_memory_document_reports_nothing()
    {
        // No written form to be wrong about. Same shape as XrefTableSpacingRule with no SourceBytes.
        var doc = new PdfDocument();
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Junk")] = new PdfString([0x48], PdfStringFormat.Hexadecimal),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        Assert.Empty(Findings(doc));
    }

    [Fact]
    public void Both_violations_are_reported_at_most_once_each()
    {
        Finding[] findings = Findings(ContentDoc("BT <48455> Tj <484!> Tj <9AB> Tj ET"));
        Assert.Equal(2, findings.Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~HexStringFormatRuleTests"`
Expected: FAIL to compile — `HexStringFormatRule` does not exist.

- [ ] **Step 3: Write the rule**

Create `PdfLibrary/Conformance/Rules/HexStringFormatRule.cs`:

```csharp
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A (ISO 19005-2/3, clause 6.1.6): a hexadecimal string shall contain an EVEN number of
/// non-white-space characters (test 1), all of which shall be hexadecimal digits (test 2). Mirrors
/// veraPDF's <c>CosString</c> rules <c>hexCount % 2 == 0</c> and <c>containsOnlyHex == true</c>.
///
/// <para>The clause constrains how a string was WRITTEN, and the lexer normalises both violations
/// away — it pads an odd trailing nibble with '0' and silently drops an unparseable digit pair. The
/// facts are captured at the read instead and ride on
/// <see cref="PdfString.HexFacts"/>; a string with no facts (a literal, or anything synthesised in
/// memory) is simply not constrained, so an in-memory document reports nothing.</para>
///
/// <para>Two walks, matching <see cref="ImplementationLimitsRule"/>: the reachable object graph from
/// the trailer, and page content operands. Form, pattern and annotation streams are NOT visited —
/// a deliberate under-report that keeps the preflighter a strict subset of the reference validator.
/// At most one finding per test, since either makes the document non-conformant.</para>
/// </summary>
internal sealed class HexStringFormatRule : IConformanceRule
{
    public string RuleId => "hex-string-format";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var oddReported = false;
        var nonHexReported = false;

        foreach (PdfString s in HexStrings(context))
        {
            if (s.HexFacts is not { } facts)
                continue;

            if (!oddReported && facts.NonWhitespaceCount % 2 != 0)
            {
                oddReported = true;
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "6.1.6"),
                    Message = $"A hexadecimal string contains an odd number "
                            + $"({facts.NonWhitespaceCount}) of non-white-space characters; a "
                            + "hexadecimal string must contain an even number.",
                };
            }

            if (!nonHexReported && facts.HasNonHexDigit)
            {
                nonHexReported = true;
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "6.1.6"),
                    Message = "A hexadecimal string contains a non-white-space character outside "
                            + "the range 0 to 9, A to F or a to f.",
                };
            }

            if (oddReported && nonHexReported)
                yield break;
        }
    }

    /// <summary>Every string the rule can see: the reachable object graph, then page content
    /// operands (which the object walk never reaches — it pushes a stream's dictionary, never its
    /// content bytes).</summary>
    private static IEnumerable<PdfString> HexStrings(ConformanceContext context)
    {
        foreach (PdfString s in ReachableStrings(context))
            yield return s;

        foreach (PdfString s in ContentStrings(context))
            yield return s;
    }

    private static IEnumerable<PdfString> ReachableStrings(ConformanceContext context)
    {
        var seen = new HashSet<int>();          // indirect object numbers already visited (cycle guard)
        var stack = new Stack<PdfObject>();

        if (context.Document.Trailer?.Dictionary is { } trailer)
            stack.Push(trailer);

        while (stack.Count > 0)
        {
            if (context.Resolve(stack.Pop()) is not { } current)
                continue;
            if (current.IsIndirect && !seen.Add(current.ObjectNumber))
                continue; // guards indirect-object cycles (e.g. an outline item's /Parent back-reference)

            switch (current)
            {
                case PdfDictionary dict:
                    foreach (KeyValuePair<PdfName, PdfObject> entry in dict)
                        stack.Push(entry.Value);
                    break;

                case PdfArray array:
                    foreach (PdfObject item in array)
                        stack.Push(item);
                    break;

                case PdfStream stream:
                    stack.Push(stream.Dictionary); // the stream dictionary only — never its content bytes
                    break;

                case PdfString str:
                    yield return str;
                    break;
            }
        }
    }

    private static IEnumerable<PdfString> ContentStrings(ConformanceContext context)
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch { yield break; } // no navigable page tree — a different clause's concern

        foreach (PdfPage page in pages)
        {
            // Shared per-document parse; empty covers no content, undecodable streams and
            // unparseable content alike, each of which this rule then skips (FP-safe).
            foreach (PdfOperator op in context.PageContentOperators(page))
                foreach (PdfObject operand in op.Operands)
                    foreach (PdfString s in StringsIn(operand))
                        yield return s;
        }
    }

    /// <summary>An operand may be a string, or an array or dictionary containing one — a
    /// <c>TJ</c> array and a <c>BDC</c> property dictionary both carry strings the top level does
    /// not expose.
    ///
    /// <para>An inline image's parameter dictionary is NOT reachable here:
    /// <c>InlineImageOperator</c> passes an EMPTY operand list to its base constructor and keeps its
    /// dictionary on a separate <c>Parameters</c> property. A hex string written inside a
    /// <c>BI … ID</c> dictionary therefore goes unreported — a further under-report on top of the
    /// form/pattern/annotation one, and safe for the same reason.</para></summary>
    private static IEnumerable<PdfString> StringsIn(PdfObject operand)
    {
        switch (operand)
        {
            case PdfString s:
                yield return s;
                break;

            case PdfArray array:
                foreach (PdfObject item in array)
                    foreach (PdfString s in StringsIn(item))
                        yield return s;
                break;

            case PdfDictionary dict:
                foreach (KeyValuePair<PdfName, PdfObject> entry in dict)
                    foreach (PdfString s in StringsIn(entry.Value))
                        yield return s;
                break;
        }
    }
}
```

- [ ] **Step 4: Register the rule**

In `PdfLibrary/Conformance/Preflighter.cs`, immediately after the `XrefTableSpacingRule()` entry (`:24`), insert:

```csharp
        // Hexadecimal strings: even digit count, hex digits only (ISO 19005-2/3 6.1.6 t1/t2). The
        // facts ride on PdfString.HexFacts, captured by the lexer at the read.
        new Rules.HexStringFormatRule(),
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~HexStringFormatRuleTests"`
Expected: PASS, all tests.

- [ ] **Step 6: Probe the guard**

Temporarily change `if (s.HexFacts is not { } facts) continue;` to also skip odd counts (`if (s.HexFacts is not { } facts || facts.NonWhitespaceCount % 2 != 0) continue;`) and re-run.
Expected: `An_odd_digit_count_in_page_content_is_flagged` FAILS. Restore.

- [ ] **Step 7: Run the full suite for regressions**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`
Expected: PASS. A newly registered rule runs against every existing conformance fixture — any new failure here is a false positive and must be investigated before proceeding, not suppressed.

- [ ] **Step 8: Commit**

```bash
git add PdfLibrary/Conformance/Rules/HexStringFormatRule.cs PdfLibrary/Conformance/Preflighter.cs PdfLibrary.Tests/Conformance/HexStringFormatRuleTests.cs
git commit -m "feat(conformance): detect malformed hexadecimal strings (6.1.6 t1/t2)

Even non-whitespace digit count, hex digits only. Reads the facts the
lexer now captures, over the reachable object graph and page content
operands. Detect-only: one Preflighter line, no remediation wiring."
```

---

### Task 4: Mark an out-of-range integer literal without changing its value

**Files:**
- Modify: `PdfLibrary/Core/Primitives/PdfInteger.cs:8-20`
- Modify: `PdfLibrary/Content/PdfContentParser.cs:15-20` (replace `ParseInt`), `:75`, `:332`, `:391`, `:451`
- Test: `PdfLibrary.Tests/Content/IntegerRangeFactTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `PdfInteger(long value, bool sourceOutOfInt32Range = false)` and `PdfInteger.SourceOutOfInt32Range` (`bool`).

**The false-positive trap.** The current `ParseInt` returns `0` for two categorically different inputs: an integer literal too large for Int32, and text that is not an integer at all (a bare `-`, a lone `.`, a doubled sign). Its own comment at `:15-18` says so. Marking both would invent a 6.1.13 violation out of malformed-but-not-oversized input and break the zero-false-positive invariant. Only a well-formed integer literal that is too large gets marked.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Content/IntegerRangeFactTests.cs`:

```csharp
using System.Linq;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using Xunit;

namespace PdfLibrary.Tests.Content;

/// <summary>
/// ISO 19005-2 clause 6.1.13 test 1 forbids an integer outside ±2147483647. The content parser
/// clamps such an operand to 0, which erases the evidence — so the FACT is recorded while the VALUE
/// is left exactly as it was. Leaving the value alone is what keeps the cross-platform render
/// baselines still.
/// </summary>
public class IntegerRangeFactTests
{
    private static PdfInteger FirstInteger(string content) =>
        PdfContentParser.Parse(Encoding.ASCII.GetBytes(content))
            .SelectMany(op => op.Operands)
            .OfType<PdfInteger>()
            .First();

    [Theory]
    [InlineData("0 2157483648 Td", true)]        // corpus 6-1-13-t01-fail-b, above Int32
    [InlineData("0 -2157483648 Td", true)]       // below Int32
    [InlineData("0 +2157483648 Td", true)]       // signed, above Int32
    [InlineData("0 99999999999999999999 Td", true)]  // beyond even long, still an integer literal
    public void An_oversized_integer_literal_is_marked(string content, bool expected)
    {
        PdfInteger last = PdfContentParser.Parse(Encoding.ASCII.GetBytes(content))
            .SelectMany(op => op.Operands).OfType<PdfInteger>().Last();

        Assert.Equal(expected, last.SourceOutOfInt32Range);
    }

    [Theory]
    [InlineData("0 2147483647 Td")]              // exactly int.MaxValue — in range
    [InlineData("0 -2147483648 Td")]             // exactly int.MinValue — in range
    [InlineData("0 42 Td")]
    public void An_in_range_integer_is_not_marked(string content)
    {
        Assert.All(
            PdfContentParser.Parse(Encoding.ASCII.GetBytes(content))
                .SelectMany(op => op.Operands).OfType<PdfInteger>(),
            i => Assert.False(i.SourceOutOfInt32Range));
    }

    [Theory]
    [InlineData("- 5 Td")]
    [InlineData("-- 5 Td")]
    [InlineData("+- 5 Td")]
    public void Malformed_text_that_is_not_an_integer_is_never_marked(string content)
    {
        // This is the false-positive guard. ParseInt returned 0 for these too, and marking them
        // would invent a 6.1.13 violation the reference validator does not report.
        Assert.All(
            PdfContentParser.Parse(Encoding.ASCII.GetBytes(content))
                .SelectMany(op => op.Operands).OfType<PdfInteger>(),
            i => Assert.False(i.SourceOutOfInt32Range));
    }

    [Fact]
    public void The_clamped_value_is_unchanged()
    {
        // The whole point: rendering must not move. An out-of-range operand is still 0.
        PdfInteger last = PdfContentParser.Parse(Encoding.ASCII.GetBytes("0 2157483648 Td"))
            .SelectMany(op => op.Operands).OfType<PdfInteger>().Last();

        Assert.Equal(0L, last.LongValue);
        Assert.Equal(0, last.Value);
    }

    [Fact]
    public void The_mark_stays_out_of_equality()
    {
        Assert.Equal(new PdfInteger(0), new PdfInteger(0, sourceOutOfInt32Range: true));
        Assert.Equal(new PdfInteger(0).GetHashCode(), new PdfInteger(0, true).GetHashCode());
    }

    [Fact]
    public void Site_2_array_operand()
    {
        var array = Assert.IsType<PdfArray>(
            PdfContentParser.Parse(Encoding.ASCII.GetBytes("BT [(x) 2157483648 (y)] TJ ET"))
                .SelectMany(op => op.Operands).First(o => o is PdfArray));

        Assert.True(array.OfType<PdfInteger>().Single().SourceOutOfInt32Range);
    }

    [Fact]
    public void Site_3_dictionary_operand()
    {
        var dict = Assert.IsType<PdfDictionary>(
            PdfContentParser.Parse(Encoding.ASCII.GetBytes("/OC <</N 2157483648>> BDC EMC"))
                .SelectMany(op => op.Operands).First(o => o is PdfDictionary));

        Assert.True(Assert.IsType<PdfInteger>(dict[new PdfName("N")]).SourceOutOfInt32Range);
    }

    [Fact]
    public void Site_4_inline_image_dictionary()
    {
        PdfOperator inline = PdfContentParser
            .Parse(Encoding.ASCII.GetBytes("BI /W 1 /H 1 /BPC 8 /CS /G /Foo 2157483648 ID \u0000 EI"))
            .First(op => op is InlineImageOperator);

        var image = Assert.IsType<InlineImageOperator>(inline);
        Assert.True(Assert.IsType<PdfInteger>(image.Parameters[new PdfName("Foo")]).SourceOutOfInt32Range);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~IntegerRangeFactTests"`
Expected: FAIL to compile — `'PdfInteger' does not contain a definition for 'SourceOutOfInt32Range'`.

- [ ] **Step 3: Add the mark to `PdfInteger`**

In `PdfLibrary/Core/Primitives/PdfInteger.cs`, replace the class declaration and the `LongValue` property (`:8-14`):

```csharp
internal sealed class PdfInteger(long value, bool sourceOutOfInt32Range = false) : PdfObject
{
    /// <summary>
    /// Gets the full long value of this integer.
    /// Use this when the value might exceed int.MaxValue.
    /// </summary>
    public long LongValue { get; } = value;

    /// <summary>
    /// True when the SOURCE literal this was parsed from was a well-formed integer outside
    /// [int.MinValue, int.MaxValue] — the violation ISO 19005-2 clause 6.1.13 test 1 describes — and
    /// the parser therefore clamped it. Only the content-stream parser clamps; the object parser
    /// keeps the true value in <see cref="LongValue"/>, so a rule checks both.
    ///
    /// <para>Deliberately NOT part of <see cref="Equals(object?)"/> or <see cref="GetHashCode"/>:
    /// the VALUE is what identifies an integer, and a marked 0 must stay interchangeable with an
    /// unmarked one so that nothing downstream — rendering above all — can observe the difference.</para>
    /// </summary>
    public bool SourceOutOfInt32Range { get; } = sourceOutOfInt32Range;
```

Leave `Value`, `Equals`, `GetHashCode` and every implicit operator untouched.

- [ ] **Step 4: Replace `ParseInt` with a marking helper**

In `PdfLibrary/Content/PdfContentParser.cs`, replace `ParseInt` (`:19-20`) with:

```csharp
    /// <summary>
    /// Builds an integer operand, preserving the existing lenient behaviour EXACTLY — anything that
    /// is not a valid Int32 still becomes 0 — while recording whether the source text was a
    /// well-formed integer literal that simply did not fit (ISO 19005-2 6.1.13 test 1).
    ///
    /// <para>The distinction matters: the old <c>ParseInt</c> returned 0 both for an oversized
    /// integer and for genuine garbage such as a bare "-" or a doubled sign. Marking garbage as
    /// out-of-range would invent a conformance violation the reference validator does not report,
    /// breaking the preflighter's zero-false-positive invariant.</para>
    /// </summary>
    private static PdfInteger MakeInteger(string s)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            return new PdfInteger(v);

        // int.TryParse has already failed, so a successful long parse means the literal is
        // well-formed but outside Int32. IsIntegerLiteral catches the rarer case of a literal too
        // large for long as well, which is still an integer and still out of range.
        bool outOfRange =
            long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || IsIntegerLiteral(s);

        return new PdfInteger(0, outOfRange);
    }

    /// <summary>An optional sign followed by at least one digit, and nothing else.</summary>
    private static bool IsIntegerLiteral(string s)
    {
        int i = s.Length > 0 && s[0] is '+' or '-' ? 1 : 0;
        if (i >= s.Length)
            return false; // "", "-", "+"

        for (; i < s.Length; i++)
            if (s[i] is < '0' or > '9')
                return false;

        return true;
    }
```

Keep `ParseReal` exactly as it is — clause 6.1.13 tests 2 and 5 (real range) are not in this work.

- [ ] **Step 5: Route all four integer sites through the helper**

Replace each `new PdfInteger(ParseInt(token.Value))` with `MakeInteger(token.Value)`.

At `:75` (operand stack, inside `Parse(Stream)`):

```csharp
                    operands.Push(MakeInteger(token.Value));
```

At `:332` (inside `ParseArray`):

```csharp
                    array.Add(MakeInteger(token.Value));
```

At `:391` (inside `ParseDictionary`) and `:451` (inside `ParseInlineImage`), both switch-expression arms:

```csharp
                    PdfTokenType.Integer => MakeInteger(token.Value),
```

After this there are no callers of `ParseInt` left; delete the old method so no site can regress to it.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~IntegerRangeFactTests"`
Expected: PASS, all tests.

- [ ] **Step 7: Probe the false-positive guard**

Temporarily change `bool outOfRange = …` to `const bool outOfRange = true;` and re-run.
Expected: `Malformed_text_that_is_not_an_integer_is_never_marked` FAILS. This is the guard that protects the zero-false-positive invariant, so confirm it genuinely fires. Restore.

- [ ] **Step 8: Run the full suite for regressions**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`
Expected: PASS. Rendering tests matter most here — the value semantics are unchanged, so any render assertion that moves means the helper is not behaving like the old `ParseInt` and must be fixed before proceeding.

- [ ] **Step 9: Commit**

```bash
git add PdfLibrary/Core/Primitives/PdfInteger.cs PdfLibrary/Content/PdfContentParser.cs PdfLibrary.Tests/Content/IntegerRangeFactTests.cs
git commit -m "feat(content): mark an out-of-range integer literal, keep its value at 0

Clause 6.1.13 test 1 needs to know the source literal did not fit; the
renderer needs the value not to move. Record the fact, clamp exactly as
before. Garbage that merely failed to parse is NOT marked -- that
distinction is what keeps the zero-false-positive invariant."
```

---

### Task 5: Extend `ImplementationLimitsRule` for 6.1.13 test 1

**Files:**
- Modify: `PdfLibrary/Conformance/Rules/ImplementationLimitsRule.cs` — doc comment `:8-30`, `Check` `:42-50`, the object walk `:93-146`, plus a new content-stream sub-check
- Test: `PdfLibrary.Tests/Conformance/ImplementationLimitsIntegerRangeTests.cs`

**Interfaces:**
- Consumes: `PdfInteger.SourceOutOfInt32Range` from Task 4.
- Produces: additional `Finding`s on clause `6.1.13` from the existing rule id `"implementation-limits"`. No new rule id, no `Preflighter.cs` change.

Both corpus shapes were traced before writing this task. `6-1-13-t01-fail-b.pdf` puts `2157483648` as a **top-level `Td` operand** in an uncompressed content stream. `6-1-13-t01-fail-c.pdf` puts it inside `/Dest [8 0 R /XYZ 0 0 2157483648]` on outline item object 14, which **is** reachable from `/Root` (verified by tracing the reference graph), and the object walk already pushes array items.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Conformance/ImplementationLimitsIntegerRangeTests.cs`:

```csharp
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// ISO 19005-2 clause 6.1.13 test 1 (<see cref="ImplementationLimitsRule"/>): no integer outside
/// ±2147483647. Two paths, because the two parsers differ — the object parser keeps the true value
/// in <see cref="PdfInteger.LongValue"/>, while the content parser clamps to 0 and records the fact.
///
/// <para>Corpus fixtures "…6-1-13-t01-fail-b.pdf" (a <c>Td</c> operand) and "…-fail-c.pdf" (inside a
/// <c>/Dest</c> array on an outline item) are the end-to-end proof; these pin the logic without the
/// corpus.</para>
/// </summary>
public class ImplementationLimitsIntegerRangeTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static Finding[] IntegerFindings(PdfDocument doc) =>
        [.. new ImplementationLimitsRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Where(f => f.Message.Contains("integer", System.StringComparison.OrdinalIgnoreCase))];

    /// <summary>A one-page document; <paramref name="extraCatalogEntry"/> is hung off the catalog so
    /// the object-graph walk reaches it, mirroring fail-c's outline /Dest array.</summary>
    private static PdfDocument Doc(string pageContent, PdfObject? extraCatalogEntry = null)
    {
        var doc = new PdfDocument();
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Contents")] = Ref(4),
            [N("MediaBox")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Resources")] = new PdfDictionary(),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        if (extraCatalogEntry is not null)
            catalog[N("Dest")] = extraCatalogEntry;
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void An_out_of_range_integer_in_the_object_graph_is_flagged()
    {
        // fail-c's shape: /Dest [… /XYZ 0 0 2157483648]. The object parser keeps the true value.
        Finding f = Assert.Single(IntegerFindings(Doc(
            "BT ET",
            new PdfArray(N("XYZ"), new PdfInteger(0), new PdfInteger(0), new PdfInteger(2157483648L)))));

        Assert.Equal("implementation-limits", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
        Assert.Contains("6.1.13", f.Clause, System.StringComparison.Ordinal);
        Assert.Contains("2157483648", f.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void An_out_of_range_integer_in_page_content_is_flagged()
    {
        // fail-b's shape exactly: "0 2157483648 Td". The content parser clamped it to 0, so this
        // can only be found via the recorded fact.
        Finding f = Assert.Single(IntegerFindings(Doc("q BT 50 150 Td 0 2157483648 Td ET Q")));
        Assert.Contains("6.1.13", f.Clause, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2147483647L)]
    [InlineData(-2147483648L)]
    [InlineData(0L)]
    public void An_in_range_integer_is_accepted(long value)
    {
        Assert.Empty(IntegerFindings(Doc("BT ET", new PdfArray(new PdfInteger(value)))));
    }

    [Fact]
    public void A_negative_out_of_range_integer_is_flagged()
    {
        // fail-a's shape: -2157483648 in a /Widths array.
        Assert.Single(IntegerFindings(Doc("BT ET", new PdfArray(new PdfInteger(-2157483648L)))));
    }

    [Fact]
    public void At_most_one_integer_finding_is_reported()
    {
        Assert.Single(IntegerFindings(Doc(
            "BT 0 2157483648 Td 0 2157483649 Td ET",
            new PdfArray(new PdfInteger(2157483650L)))));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~ImplementationLimitsIntegerRangeTests"`
Expected: FAIL — the assertions find no findings (`Assert.Single` gets an empty collection).

- [ ] **Step 3: Fold the integer check into the existing object walk**

In `PdfLibrary/Conformance/Rules/ImplementationLimitsRule.cs`, add the shared predicate next to the other constants (after `MaxNameBytes` at `:40`):

```csharp
    /// <summary>ISO 19005-2 6.1.13 test 1. Two parsers, two shapes of evidence: the object parser
    /// keeps the true value in <see cref="PdfInteger.LongValue"/>, while the content parser clamps
    /// the operand to 0 and records <see cref="PdfInteger.SourceOutOfInt32Range"/> instead.</summary>
    private static bool IsOutOfRange(PdfInteger i) =>
        i.SourceOutOfInt32Range || i.LongValue < int.MinValue || i.LongValue > int.MaxValue;
```

In `CheckStringsAndNames`, add a third flag and extend the loop condition. Replace `:98`:

```csharp
        bool stringReported = false, nameReported = false, integerReported = false;
```

Replace the `while` condition at `:103`:

```csharp
        while (stack.Count > 0 && !(stringReported && nameReported && integerReported))
```

Add a case to the `switch` (after the `PdfString` case at `:138-141`):

```csharp
                case PdfInteger integer when !integerReported && IsOutOfRange(integer):
                    findings.Add(IntegerFinding(context, integer.LongValue));
                    integerReported = true;
                    break;
```

Add the finding factory next to the others (after `NameFinding` at `:181-187`):

```csharp
    private Finding IntegerFinding(ConformanceContext context, long value) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.1.13"),
        Message = $"The integer {value} is outside the permitted range "
                + "-2147483648 to 2147483647.",
    };
```

- [ ] **Step 4: Add the content-stream sub-check**

Add a new method after `CheckContentStreamStrings` (`:149-171`):

```csharp
    // ── Sub-check 5 — out-of-range integer operand in page content (6.1.13 test 1) ──────────────────
    private IEnumerable<Finding> CheckContentStreamIntegers(ConformanceContext context)
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch { yield break; } // no navigable page tree — a different clause's concern

        foreach (PdfPage page in pages)
        {
            // Shared per-document parse (already cached by PageContentOperators), so walking the
            // operators a second time for integers costs no reparse.
            foreach (PdfOperator op in context.PageContentOperators(page))
                foreach (PdfObject operand in op.Operands)
                    foreach (PdfInteger integer in IntegersIn(operand))
                        if (IsOutOfRange(integer))
                        {
                            yield return IntegerFinding(context, integer.LongValue);
                            yield break; // one finding is enough to mark the document non-conformant
                        }
        }
    }

    /// <summary>An operand may be an integer, or an array or dictionary containing one — a
    /// <c>TJ</c> array and a <c>BDC</c> property dictionary both carry numbers the top level does
    /// not expose.
    ///
    /// <para>As in <see cref="HexStringFormatRule"/>, an inline image's parameter dictionary is not
    /// reachable: <c>InlineImageOperator</c> passes an empty operand list to its base constructor.
    /// A safe under-report.</para></summary>
    private static IEnumerable<PdfInteger> IntegersIn(PdfObject operand)
    {
        switch (operand)
        {
            case PdfInteger i:
                yield return i;
                break;

            case PdfArray array:
                foreach (PdfObject item in array)
                    foreach (PdfInteger i in IntegersIn(item))
                        yield return i;
                break;

            case PdfDictionary dict:
                foreach (KeyValuePair<PdfName, PdfObject> entry in dict)
                    foreach (PdfInteger i in IntegersIn(entry.Value))
                        yield return i;
                break;
        }
    }
```

Wire it into `Check` (`:42-50`) by adding a fourth loop:

```csharp
        foreach (Finding f in CheckContentStreamIntegers(context))
            yield return f;
```

Note the content sub-check can report an integer finding that the object walk also reported. `At_most_one_integer_finding_is_reported` pins that this does not happen — if it fails, gate the content sub-check on the object walk not having reported one, by hoisting the flag as the string sub-checks would need too.

- [ ] **Step 5: Update the doc comment**

The class comment currently claims test 1 is out of scope. Replace the sentence at `:26-29`:

```
/// It parses page content only (form/pattern/annotation content is a safe under-report), so the rule stays
/// a strict subset of the reference validator. The integer (test 1) and q/Q-nesting (test 8) content
/// limits are out of scope — the content lexer normalises an out-of-range integer operand away, so they
/// need byte-level content tokenisation, tracked separately.
```

with:

```
/// It parses page content only (form/pattern/annotation content is a safe under-report), so the rule stays
/// a strict subset of the reference validator.
///
/// A fifth sub-check covers the INTEGER range limit (test 1) on both paths. The object parser keeps an
/// out-of-range value intact in <see cref="PdfInteger.LongValue"/>; the content parser clamps the operand
/// to 0 and records <see cref="PdfInteger.SourceOutOfInt32Range"/> instead, so the value the renderer sees
/// never moved. The q/Q-nesting (test 8) and CID (test 10) limits remain out of scope.
```

Also update the summary line at `:10-11` — "Three tractable sub-checks — the integer-range (needs content-stream operands) and CID > 65535 (needs an embedded-CMap parser) limits are out of scope for this slice" — to name only the CID limit as out of scope.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~ImplementationLimitsIntegerRangeTests"`
Expected: PASS, all tests.

- [ ] **Step 7: Probe the guard**

Temporarily change `IsOutOfRange` to `=> false;` and re-run.
Expected: every flagging test FAILS while the in-range tests still pass. Restore.

- [ ] **Step 8: Run the full suite for regressions**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add PdfLibrary/Conformance/Rules/ImplementationLimitsRule.cs PdfLibrary.Tests/Conformance/ImplementationLimitsIntegerRangeTests.cs
git commit -m "feat(conformance): detect integers outside Int32 (6.1.13 t1)

Folded into the existing object walk plus a new page-content sub-check.
Two shapes of evidence because the two parsers differ: the object parser
keeps the true value, the content parser keeps only the mark. Deletes
the doc comment's claim that test 1 is out of scope."
```

---

### Task 6: Verify parity, re-baseline, and file the cloner issue

**Files:**
- Modify: `PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md` (regenerated)
- Modify: `Pellucid.App.Tests/oi-corpus-baseline.txt` in the **Pellucid** repo (hand-edited)
- Modify: `Pellucid` repo `ci/dependencies.json` (engine pin) and `docs/ISSUE-TRACKER.md`

**Interfaces:**
- Consumes: everything from Tasks 1-5.
- Produces: the verified parity numbers this plan exists to deliver.

This task has **no new code**. It is the measurement that decides whether the work landed, and it is where a false positive would surface. Do not skip it or fold it into Task 5.

- [ ] **Step 1: Confirm the corpus is present**

Run: `ls ../veraPDF-corpus/PDF_A-2b`
Expected: the clause folders listed. If absent, the `Category=Parity` tests SKIP and this task cannot be completed — say so rather than reporting success.

- [ ] **Step 2: Run the parity gate**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category=Parity"`
Expected: PASS. Read the **SKIP count**, not just the PASS count — a skipped parity leg proves nothing.

- [ ] **Step 3: Regenerate the parity report**

Run (PowerShell):

```powershell
$env:PARITY_REPORT = "PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md"
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~ParityReportTests.Generate_parity_report"
```

- [ ] **Step 4: Check the numbers against the definition of done**

Read the regenerated `PARITY-REPORT.md` and confirm every one of these:

- PDF/A-2b agreement is **976/986**, up from 972/986.
- **PdfLibrary FP is 0** on every profile — the standing invariant. A non-zero value means a new rule over-reports; fix the rule, do not adjust the expectation.
- Clause 6.1.6 shows **2/2, full**.
- Clause 6.1.13 shows **13/15** (the remaining two are test 10, CID).
- A-2u 22/22, A-3b 12/12, UA-1 296/296 all unchanged.
- The verdict-leverage section no longer lists 6.1.6, and lists 6.1.13 only against `6-1-13-t08-fail-b.pdf` and `6-1-13-t10-fail-a.pdf`.

- [ ] **Step 5: Confirm the render baselines did not move**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Rendering"`
Expected: PASS with no baseline diffs — `SpotPageByteIdentityTests` in particular. This is the check that Task 4's "value semantics unchanged" claim was true in practice, not just in argument.

Note: the render and byte-identity tests live inside `PdfLibrary.Tests/Rendering/`. There is a
`PdfLibrary.Rendering.Skia.Tests/` directory in the repo root, but it holds only stale `bin`/`obj`
output and **no `.csproj`** — it is a build fossil, not a project. Do not try to run it.

- [ ] **Step 6: Commit the engine work**

```bash
git add PdfLibrary.Tests/Conformance/parity/PARITY-REPORT.md
git commit -m "test(parity): A-2b 972/986 -> 976/986, clause 6.1.6 to full

6.1.6 t1/t2 and 6.1.13 t1 now detected. Zero false positives across all
1316 files. 6.1.13 sits at 13/15 -- the remaining two are test 10 (CID),
tracked separately."
```

- [ ] **Step 7: Merge to `master` and record the SHA**

```bash
git checkout master
git merge --no-ff feat/parity-616-613-byte-fidelity -m "Merge branch 'feat/parity-616-613-byte-fidelity' — +4 parity, clause 6.1.6 to full"
git rev-parse HEAD
```

Do **not** push yet — Step 9 pins this SHA, and the two repos must move together.

- [ ] **Step 8: Re-baseline the oi-corpus gate in the Pellucid repo**

In `C:\Users\jorda\RiderProjects\Pellucid`, branch, then **hand-edit** the data line in `Pellucid.App.Tests/oi-corpus-baseline.txt` to the new counts.

Do **not** set `PELLUCID_OI_CORPUS_REGEN=1` — it wipes the file's decomposition history.

Reading the gate: a **falling `conforms` count is what a detection gain looks like** and is expected here. `fixed` and `needsDecision` staying flat is the tell that it is a gain and not damage. If either of those moves, stop and investigate.

- [ ] **Step 9: Pin the engine and run the full gate**

Update `ci/dependencies.json` in Pellucid to the SHA from Step 7, then run:

```bash
bash tools/gate.sh
```

Expected: PASS. Read the SKIP count as well as PASS.

- [ ] **Step 10: File the `ObjectGraphCloner` issue**

Add an entry to `docs/ISSUE-TRACKER.md` in the Pellucid repo:

> `Editing/ObjectGraphCloner.cs:108` clones a string as `new PdfString(str.Bytes)`, taking the default `Literal` format and dropping the original. A hex string that passes through the cloner loses the round-trip fidelity issue 57 established — and now also loses its 6.1.6 facts. Found while implementing the 6.1.6/6.1.13 byte-fidelity work; not fixed there because it is a save-path concern, not a detection one.

- [ ] **Step 11: Commit the Pellucid side**

```bash
git add Pellucid.App.Tests/oi-corpus-baseline.txt ci/dependencies.json docs/ISSUE-TRACKER.md
git commit -m "ci: pin the engine at <SHA> (+4 parity, clause 6.1.6 to full)"
```

- [ ] **Step 12: Report, do not push**

Report the final numbers and both SHAs. Pushing is the user's call — and when it happens, **the engine must be pushed before Pellucid**, because CI clones the engine at the SHA in `ci/dependencies.json`.

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task: §4.2 side table → Task 1; §4.3/§4.4/§4.6 propagation and equality → Task 2; §4.7 6.1.6 rule → Task 3; §4.5 integer mark and the false-positive rule → Task 4; §4.7 `ImplementationLimitsRule` extension → Task 5; §6 verification, §5's cloner issue → Task 6. §4.8's per-file table is realised by the corpus-shaped tests in Tasks 3 and 5 and confirmed by Task 6 Step 4.

**Type consistency.** `HexStringFacts(int NonWhitespaceCount, bool HasNonHexDigit)` is defined in Task 1 and consumed with those exact member names in Tasks 2 and 3. `TryTakeHexFacts(long, out HexStringFacts)` is defined in Task 1 and called in Task 2 only. `PdfString.HexFacts` is produced in Task 2 and read in Task 3. `PdfInteger.SourceOutOfInt32Range` is produced in Task 4 and read in Task 5 via `IsOutOfRange`. `MakeString` and `MakeInteger` are both private to `PdfContentParser` and never referenced across tasks.

**Known risk, deliberately left to measurement.** Task 3's rule walks the object graph as well as page content, which is broader than the two corpus files need. That is the more correct reading of the clause, but it is also where a false positive would appear. Task 6 Step 4 is the check, and the instruction there is explicit: narrow the arm, never adjust the expectation.
