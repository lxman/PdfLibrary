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
