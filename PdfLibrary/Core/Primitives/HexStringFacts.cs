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
