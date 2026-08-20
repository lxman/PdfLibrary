# Byte fidelity for hex strings and integer range — design

_2026-08-20. Closes PDF/A-2b clauses **6.1.6** (tests 1 and 2) and **6.1.13 test 1**. Target: veraPDF
verdict parity 972/986 → **976/986**, with the standing zero-false-positive invariant intact across
all 1316 corpus files._

## 1. Why this work exists

Both clauses constrain how a value was **written**, not what it means once parsed. The engine's
lexer and content parser each normalise the offending byte sequence into something well-formed and
then discard the evidence, so no rule downstream can see the violation. The fix is to preserve two
facts the parse currently throws away — and to preserve them *without* changing a single parsed
value.

That last constraint is the spine of this design. The repository holds byte-identical render
baselines across Windows, Linux and macOS. Any change that alters what the renderer sees for a
malformed operand can move those hashes. So every mechanism below is chosen to leave parsed values
exactly as they are today.

### The four whole-file misses this closes

Read from `PdfLibrary.Tests/Conformance/parity/verapdf-verdicts.json`, not from filenames — corpus
filenames do not name the rule that fires.

| File | veraPDF fails it on | What the engine does today |
|---|---|---|
| `6-1-6-t01-fail-a.pdf` | 6.1.6 / t1 | `<48455> Tj` — 5 non-whitespace digits; lexer pads to `<484550>` |
| `6-1-6-t02-fail-a.pdf` | 6.1.6 / t2 | `<484!> Tj` — `!` fails `byte.TryParse` and is silently dropped |
| `6-1-13-t01-fail-b.pdf` | 6.1.13 / t1 | `2157483648` in a content stream → `int.TryParse` fails → **0** |
| `6-1-13-t01-fail-c.pdf` | 6.1.13 / t1 | `2157483648` in a `/Dest` entry → survives as `PdfInteger.LongValue` |

Both 6.1.6 failures are **content-stream operands**, not object-level strings. This matters: a fix
aimed only at object parsing would close neither.

### A fifth file, currently agreeing by luck

`6-1-13-t01-fail-a.pdf` carries `-2157483648` in a `/Widths` entry. veraPDF fails it on 6.1.13/t1.
A `pellucid scan` of that file shows the engine failing it on a *different* clause entirely:

```
ruleId "font-program", clause "ISO 19005-2:2011, 6.2.11.5"
"The TrueType font 'TMJTIB+FreeMonoBold' declares a glyph width that differs from the
 embedded font program's advance width by 600 units (tolerance 10)."
```

veraPDF does not flag 6.2.11.5 on this file at all. The verdicts agree only because an unrelated
finding happens to fire. Implementing 6.1.13/t1 converts that coincidence into a real agreement, so
the file stops depending on a font finding that a future font fix could remove. This is not counted
in the +4; it is insurance on a verdict already banked.

## 2. What the profile actually requires

From the veraPDF PDF/A-2B profile XML (`PDF_A/PDFA-2B.xml`; `clause` is an attribute of `<id>`):

| Clause / test | Test expression | Description |
|---|---|---|
| 6.1.6 t1 | `(isHex != true) \|\| hexCount % 2 == 0` | Hexadecimal strings shall contain an even number of non-white-space characters |
| 6.1.6 t2 | `(isHex != true) \|\| containsOnlyHex == true` | A hexadecimal string is written as a sequence of hexadecimal digits (0–9, A–F, a–f) |
| 6.1.13 t1 | `(intValue <= 2147483647) && (intValue >= -2147483648)` | A conforming file shall not contain any integer outside ±2147483647 |

Note `hexCount` is the count of **non-white-space** characters — whitespace inside `<…>` is legal and
excluded. And 6.1.6 constrains the written form only; the two tests are independent, so `<48 4!>`
would violate t2 but not t1.

## 3. Current behaviour, verified

### 3.1 One lexer serves both parsers

`PdfContentParser.Parse` instantiates the same type object parsing uses
(`PdfLibrary/Content/PdfContentParser.cs:51`):

```csharp
var lexer = new PdfLexer(stream);
```

There is no separate content-stream lexer. A fix inside `PdfLexer` therefore covers object strings
and content-stream operands at once. What differs above the tokenizer is only the parser:
`PdfParser` (`Parsing/`) versus `PdfContentParser` (`Content/`).

### 3.2 The hex reader discards both facts

`PdfLibrary/Parsing/PdfLexer.cs:184-241`, `ReadHexStringOrDictionaryStart()`. Three relevant
behaviours:

- Whitespace is stripped as digits are collected, so the non-white-space count exists in
  `hexDigits.Length` and is then discarded.
- An odd trailing nibble is padded with `'0'` (`hexString[i] + "0"`), per ISO 32000-1 §7.3.4.3. The
  oddness is not recorded.
- A non-hex character makes `byte.TryParse` fail; the pair is simply not appended to `bytes`. No
  error, no flag.

The method returns `new PdfToken(PdfTokenType.HexString, value, position)` (`:241`), where `value`
is the *decoded* bytes as Latin-1. The raw digits are gone by the time any parser sees the token.

### 3.3 Integer handling is asymmetric between the two parsers

**Object path — the value already survives.** `PdfParser.ParseIntegerOrReference` uses
`long.TryParse` (`Parsing/PdfParser.cs:141`), and `PdfInteger` is backed by `long`
(`Core/Primitives/PdfInteger.cs:14`). So `-2157483648` and `2157483648` both parse exactly and are
readable from `LongValue`. **No parser change is required for object-level detection.**

**Content path — the value is destroyed.** `Content/PdfContentParser.cs:19-20`:

```csharp
private static int ParseInt(string s) =>
    int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
```

`int.TryParse` fails for anything outside Int32, and the failure branch yields `0`. The method's own
comment (`:15-18`) names "an over-long integer" as one of the malformed cases it deliberately
absorbs, alongside a bare `-` or `.`.

**This conflation is the false-positive trap.** `ParseInt` returns `0` for two categorically
different inputs: an integer literal that is too large, and text that is not an integer at all. A
naive "did ParseInt return 0?" check would invent a 6.1.13 violation out of a bare `-` operand. The
project's standing invariant is zero false positives across 1316 files; this is exactly how it would
break.

### 3.4 Primitive shapes

`PdfString` (`Core/Primitives/PdfString.cs:9`) is an `internal sealed class` — a reference type with
two readonly fields. Adding properties costs nothing on the struct path. Two details constrain the
design:

- `Equals` and `GetHashCode` (`:101-110`) compare **`_bytes` only** — not `_format`. New fields must
  not join the comparison (see §4.4).
- `ToHexadecimalString` (`:87-99`) writes `{b:X2}` per byte, which is why a save already normalises
  `<48455>` to `<484550>`.

`PdfToken` (`Parsing/PdfToken.cs:6`) is a `readonly struct` carrying `Type`, `Value`, `Position`,
passed by value on the content-stream path that profiling flagged hot.

`PdfInteger` (`Core/Primitives/PdfInteger.cs:8`) is an `internal sealed class` with a primary
constructor `(long value)`, implicit conversions from `int`/`long`, and `Equals`/`GetHashCode` over
`LongValue` alone (`:26-28`).

## 4. Design

### 4.1 Rejected approaches for carrying the hex facts

**Widen `PdfToken`.** Rejected. It is a by-value `readonly struct` on the hot content-stream path.
Two extra fields tax every integer, name and operator token in every content stream in order to
serve two rules that fire on a handful of documents.

**New token types (`MalformedHexString`).** Rejected on two counts. It cannot carry the digit count
the finding message needs, and it would need *two* new types to keep "odd count" distinct from
"non-hex character". Worse, it turns every existing `switch` on `HexString` into a three-way
dispatch — the fix-one-arm-miss-its-twin shape that has bitten this repository repeatedly.

**A standalone byte scanner in the rule.** Rejected. Re-scanning raw bytes for `<…>` means
reimplementing enough of the tokenizer to skip literal strings, comments, dictionary `<<`, and
binary stream payloads. A `<` inside compressed stream data would produce a false positive, against
the invariant.

### 4.2 Chosen: a side-channel on the lexer instance

`ReadHexStringOrDictionaryStart` records what it already computed, on the lexer instance:

- `LastHexNonWhitespaceCount` — `hexDigits.Length`, the count after whitespace removal
- `LastHexHadNonHexDigit` — set when any collected character is outside `[0-9A-Fa-f]`

The parser reads them immediately after `NextToken()` returns a `HexString`. Cost is zero for every
other token; `PdfToken` is untouched. The lexer is already a stateful stream reader, so this adds no
new *kind* of state — only two fields whose lifetime is one token.

**Contract:** the values are defined only immediately after a `HexString` token is returned, and are
overwritten by the next hex string read. This is narrow and must be stated in the doc comment,
because it is the one way this design can be misused.

Note `LastHexHadNonHexDigit` must be computed from the **collected characters**, not inferred from
`byte.TryParse` failing. `TryParse` operates on pairs, so a pair like `4!` fails as a unit; deriving
the flag from it would be correct here but fragile. Test the character.

### 4.3 The facts land on `PdfString`

Two new properties, meaningful only when the format is `Hexadecimal`:

- `HexNonWhitespaceCount` (`int?`)
- `HexHasNonHexDigit` (`bool`)

Null / `false` for every literal string and for any hex string synthesised in memory rather than
parsed — an in-memory document has no written form to be wrong about, exactly as
`XrefTableSpacingRule` reports nothing without `SourceBytes`.

### 4.4 Equality must not change

`PdfString.Equals` compares bytes only. The new properties **must stay out of `Equals` and
`GetHashCode`.** Two strings with identical bytes must remain equal regardless of how they were
written, or dictionary keys and de-duplication shift underneath unrelated code. The same holds for
`PdfInteger` in §4.5: a flagged `0` must equal an unflagged `0`.

This is a deliberate constraint, not an oversight, and belongs in both doc comments.

### 4.5 Integer range: preserve the value, record the fact

Content-stream value semantics are unchanged — an out-of-range operand still becomes `0`. A new
property on `PdfInteger` records that the **source literal** was out of Int32 range. Because
`Equals`/`GetHashCode` continue to compare `LongValue` alone, and the value is still `0`, rendering
is provably unaffected.

The flag is set **only** for a well-formed integer literal that is too large:

1. `long.TryParse` succeeds and the result falls outside `[int.MinValue, int.MaxValue]`; **or**
2. `long.TryParse` fails **and** the text matches `^[+-]?[0-9]+$` — an integer literal beyond even
   `long`, which is still an integer and still out of range.

Anything else — a bare `-`, a lone `.`, a doubled sign, empty text — is malformed rather than
oversized and is **not** flagged. This distinction is the whole of the false-positive defence and
needs its own test for each shape.

Object-level detection needs no parser change: the rule reads `LongValue` directly.

### 4.6 Closing the construction-site seam

The facts must reach `PdfString`/`PdfInteger` at **every** construction site, not only the ones the
corpus exercises. Verified counts:

| Path | Sites | Locations |
|---|--:|---|
| Hex string — `PdfContentParser` | 4 | `:87` operand stack, `:341` array, `:394` dictionary, `:454` inline image |
| Hex string — `PdfParser` | 2 | `:241` initial parse, `:253` re-construction after decryption |
| Integer — `PdfContentParser` | 4 | `:75` operand stack, `:332` array, `:391` dictionary, `:451` inline image |

Fixing five of six and missing one is the failure mode this repository has hit four separate times.
Mitigation is structural, not vigilance:

- **One helper per concern**, called from every site. `PdfContentParser` already centralises format
  selection in `FormatOf` (`:28`); it grows into a string-construction helper, and `ParseInt` is
  replaced by an integer-construction helper returning a `PdfInteger`.
- **A test that enumerates the sites** — asserting a malformed hex string is detected when it appears
  as a bare operand, inside an array, inside a dictionary, and inside an inline-image dictionary;
  and the equivalent for integers. The corpus only exercises the bare-operand path, so without this
  the other three could ship broken and green.

`PdfParser:253` deserves explicit note: it re-constructs the string after decryption. It must carry
the facts forward from the pre-decryption parse. In practice Pellucid refuses to save encrypted
documents, but the preflighter reads them, so the path is live for detection.

### 4.7 Rules

**6.1.13 t1 — extend `ImplementationLimitsRule`.** No new rule id. The rule's doc comment
(`Conformance/Rules/ImplementationLimitsRule.cs:26-29`) already records test 1 as out of scope
because "the content lexer normalises an out-of-range integer operand away"; this work deletes that
limitation and the comment must be updated to match. A single predicate covers both paths: a
`PdfInteger` whose `LongValue` falls outside Int32 range, **or** whose out-of-range flag is set.

The rule already owns both walks it needs — `CheckStringsAndNames` walks the reachable object graph
from the trailer (cycle-guarded on object number), and `CheckContentStreamStrings` walks
`context.PageContentOperators(page)`. The integer check follows those existing shapes: report at
most one finding per violation type, and treat an unparseable or absent content stream as nothing to
report.

One gap to respect rather than close: the object walk pushes `PdfStream.Dictionary` but never
content bytes, and the content walk covers **page** content only — form, pattern and annotation
streams are not visited. That is a deliberate under-report which keeps the engine a strict subset of
veraPDF. This work does not widen it.

**6.1.6 — a new detect-only rule.** No existing rule covers the clause (current coverage: 0 of 2).
Registration cost was checked, not assumed: a detect-only rule needs exactly one line in
`Preflighter.cs`'s `Rules` list. `XrefTableSpacingRule` and `XmpPacketHeaderRule` are both registered
that way, and Pellucid routes neither rule id anywhere. The six-place checklist for rule ids applies
to *remediation domain* wiring, which this rule does not enter (§5).

The rule emits a distinct message per test — odd count (reporting the count) versus a non-hex
character — because they are separate profile tests and a shared message would obscure which fired.

### 4.8 Where each corpus file gets caught

| File | Mechanism | Path |
|---|---|---|
| `6-1-6-t01-fail-a` | `HexNonWhitespaceCount` is odd | content operand, `PdfContentParser:87` |
| `6-1-6-t02-fail-a` | `HexHasNonHexDigit` | content operand, `PdfContentParser:87` |
| `6-1-13-t01-fail-b` | out-of-range flag on `PdfInteger` | content operand, `PdfContentParser:75` |
| `6-1-13-t01-fail-c` | `LongValue` outside Int32 | object graph walk (`/Dest`) |
| `6-1-13-t01-fail-a` | `LongValue` outside Int32 | object graph walk (`/Widths`) — converts a coincidental agreement |

## 5. Scope boundaries

**Detection only.** Both 6.1.6 violations are arguably remediable by re-serialisation — a save
already rewrites `<48455>` as `<484550>` via `ToHexadecimalString`. But `<484!>` re-serialises as
`<48>`, silently **dropping a byte** of the string's value. Whether that is an acceptable repair is a
remediation decision with its own risk profile and its own user-facing surface. The parity
programme's unit of work is detection; remediation is out of scope here.

**Not in scope, deliberately:**

- 6.1.13 t10 (max CID ≤ 65535) — the other half of clause 6.1.13, worth +1 verdict, living in the
  CMap/CID subsystem. Separate item.
- 6.2.2 t2 (explicitly-associated Resources) — worth +3, a resource-scope problem with no lexing
  content. Separate item.
- Widening the content walk to form/pattern/annotation streams (§4.7).
- `PdfParser.ParseIntegerOrReference` throwing `PdfParseException` when a literal exceeds `long`
  (`:142`). Pre-existing; a document containing one may fail to load entirely. Noted, not changed.

**To be filed as an issue, not fixed here:** `Editing/ObjectGraphCloner.cs:108` clones a string as
`new PdfString(str.Bytes)`, taking the default `Literal` format and dropping the original. A hex
string that passes through the cloner loses the round-trip fidelity issue 57 established.

## 6. Testing

**Corpus-free unit tests** — the pattern `XrefTableSpacingRuleTests` and `XmpPacketHeaderRuleTests`
use, so they run everywhere including CI, which has no corpus.

- Lexer: non-white-space count with and without interior whitespace; odd versus even; non-hex
  character present versus absent; `<<` still lexes as `DictionaryStart` and does not disturb the
  side-channel; an empty `<>`.
- Construction sites: malformed hex string as bare operand, in an array, in a dictionary, in an
  inline-image dictionary (§4.6) — and the integer equivalents.
- Integer classification, one test per shape: in-range; above Int32; below Int32; beyond `long`;
  bare `-`; lone `.`; doubled sign; empty. The malformed shapes must assert **no** flag.
- Equality: `PdfString` instances with equal bytes but different hex facts compare equal and hash
  equal; a flagged `PdfInteger(0)` equals an unflagged one (§4.4).
- Rules: a conforming hex string produces no finding; an in-memory document produces no finding.

**Guard probing.** Every guard test gets probed by deleting the guard and confirming the test fails.
A previous slice shipped a guard test that passed without its guard.

**Parity gate** (`Category=Parity`, needs the corpus checkout):

- Verdict parity **972/986 → 976/986** on PDF/A-2b.
- **Zero false positives across all 1316 files** — the standing invariant, never broken.
- A-2u 22/22, A-3b 12/12, UA-1 296/296 unchanged.
- Clause coverage: 6.1.6 from 0/2 to 2/2; 6.1.13 from 10/15 to **13/15** (only t10's two files remain).
  Note the asymmetry: t1 spans three corpus files, so all three become clause matches, but only two
  of them flip a **verdict** — the third (`t01-fail-a`) already agreed via the coincidental font
  finding described in §1. Clause coverage rises by 3; verdict parity by 2.

**Render-hash guard.** Because §4.5 leaves parsed values untouched, the render baselines must not
move. Confirming that is part of the definition of done, not an assumption — the existing
cross-platform baselines are the check.

**Re-baseline `oi-corpus`.** A newly-firing rule changes decomposition counts. Hand-edit the data
line in `Pellucid.App.Tests/oi-corpus-baseline.txt`; do **not** set `PELLUCID_OI_CORPUS_REGEN=1`,
which wipes the file's decomposition history. On that gate, `conforms` falling is what a detection
gain looks like; `fixed`/`needsDecision` staying flat is the tell that it is a gain and not damage.

## 7. Definition of done

1. PDF/A-2b verdict parity is 976/986, with 0 false positives across 1316 files.
2. Clause 6.1.6 is at full parity (2/2); clause 6.1.13 is at 13/15, the remaining two being t10.
3. All four new-detection corpus files are caught by the mechanism §4.8 names for them — verified
   individually, not just in aggregate.
4. Render baselines are unmoved on all three platforms.
5. The `ImplementationLimitsRule` doc comment no longer claims test 1 is out of scope.
6. Every construction site in the §4.6 table is covered by a test.
7. `oi-corpus-baseline.txt` is hand-updated and the gate is green.
8. The `ObjectGraphCloner` format-loss issue is filed.
