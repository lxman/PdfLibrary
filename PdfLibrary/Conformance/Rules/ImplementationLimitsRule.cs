using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A implementation limits (ISO 19005-2, 6.1.13; the referenced limits are ISO 32000-1 Annex C).
/// Three tractable sub-checks — the CID &gt; 65535 (needs an embedded-CMap parser) limit is out of
/// scope for this slice:
/// <list type="number">
///   <item><b>Page boundary sizes</b> — every page's effective MediaBox and CropBox
///     (<see cref="PdfPage.GetMediaBox"/> / <see cref="PdfPage.GetCropBox"/> resolve page-tree
///     inheritance) must have normalized width and height in the range [3, 14400] units.</item>
///   <item><b>String length</b> — no string may exceed 32767 bytes.</item>
///   <item><b>Name length</b> — no name (a dictionary key or a name value) may exceed 127 bytes.</item>
/// </list>
/// Sub-checks 2 and 3 share one walk of the reachable object graph from the trailer (through
/// dictionaries, arrays, and stream dictionaries — never content streams), cycle-guarded on indirect
/// object number, reporting at most one finding per violation type. Name lengths use the decoded name
/// value (a <c>#XX</c>-escaped name is measured after decoding), matching the reference validator.
///
/// A fourth sub-check covers the string limit inside <b>page content streams</b> — a string used as a
/// content operator's operand (e.g. a huge <c>Tj</c> literal), which the object-graph walk never reaches.
/// It parses page content only (form/pattern/annotation content is a safe under-report), so the rule stays
/// a strict subset of the reference validator.
///
/// A fifth sub-check covers the INTEGER range limit (test 1) on both paths. The object parser keeps an
/// out-of-range value intact in <see cref="PdfInteger.LongValue"/>; the content parser clamps the operand
/// to 0 and records <see cref="PdfInteger.SourceOutOfInt32Range"/> instead, so the value the renderer sees
/// never moved. The q/Q-nesting (test 8) and CID (test 10) limits remain out of scope.
/// </summary>
internal sealed class ImplementationLimitsRule : IConformanceRule
{
    public string RuleId => "implementation-limits";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    private const double MinBoxSide = 3.0;
    private const double MaxBoxSide = 14400.0;
    private const int MaxStringBytes = 32767;
    private const int MaxNameBytes = 127;

    /// <summary>ISO 19005-2 6.1.13 test 1. Two parsers, two shapes of evidence: the object parser
    /// keeps the true value in <see cref="PdfInteger.LongValue"/>, while the content parser clamps
    /// the operand to 0 and records <see cref="PdfInteger.SourceOutOfInt32Range"/> instead.</summary>
    private static bool IsOutOfRange(PdfInteger i) =>
        i.SourceOutOfInt32Range || i.LongValue < int.MinValue || i.LongValue > int.MaxValue;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (Finding f in CheckPageBoxes(context))
            yield return f;

        // The object walk and the content walk can each turn up an out-of-range integer in the same
        // document. Either alone makes it non-conformant, so the second is noise.
        var integerReported = false;
        foreach (Finding f in CheckStringsAndNames(context))
        {
            integerReported |= IsIntegerFinding(f);
            yield return f;
        }

        foreach (Finding f in CheckContentStreamStrings(context))
            yield return f;

        if (integerReported)
            yield break;

        foreach (Finding f in CheckContentStreamIntegers(context))
            yield return f;
    }

    /// <summary>Distinguishes this rule's integer findings from its box/string/name findings, which
    /// share the rule id and clause.</summary>
    private static bool IsIntegerFinding(Finding f) =>
        f.Message.Contains("integer", StringComparison.OrdinalIgnoreCase);

    // ── Sub-check 1 — page boundary sizes ─────────────────────────────────────────────────────────────
    private IEnumerable<Finding> CheckPageBoxes(ConformanceContext context)
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch { yield break; } // no navigable page tree — a different clause's concern

        for (int i = 0; i < pages.Count; i++)
        {
            PdfRectangle media, crop;
            try
            {
                // GetMediaBox() throws when the MediaBox is absent (a separate clause), and GetCropBox()
                // falls back to it — so an absent/malformed box is caught and the page skipped here.
                media = pages[i].GetMediaBox();
                crop = pages[i].GetCropBox();
            }
            catch { continue; }

            if (!InRange(media))
                yield return BoxFinding(context, i, "MediaBox", media);
            if (!InRange(crop))
                yield return BoxFinding(context, i, "CropBox", crop);
        }
    }

    // PdfRectangle.Width/Height are already absolute (normalized), so reversed coordinates are handled.
    private static bool InRange(PdfRectangle r) =>
        r.Width >= MinBoxSide && r.Width <= MaxBoxSide && r.Height >= MinBoxSide && r.Height <= MaxBoxSide;

    private Finding BoxFinding(ConformanceContext context, int pageIndex, string box, PdfRectangle r) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.1.13"),
        Message = $"Page {pageIndex + 1} {box} is {r.Width:0.##}×{r.Height:0.##} units; page boundary width "
                + "and height must each be between 3 and 14400 units.",
        PageIndex = pageIndex,
    };

    // ── Sub-checks 2 & 3 — over-long strings and names (shared reachable-object walk) ────────────────────
    private IEnumerable<Finding> CheckStringsAndNames(ConformanceContext context)
    {
        var findings = new List<Finding>();
        var seen = new HashSet<int>();          // indirect object numbers already visited (cycle guard)
        var stack = new Stack<PdfObject>();
        bool stringReported = false, nameReported = false, integerReported = false;

        if (context.Document.Trailer?.Dictionary is { } trailer)
            stack.Push(trailer);

        while (stack.Count > 0 && !(stringReported && nameReported && integerReported))
        {
            if (context.Resolve(stack.Pop()) is not { } current)
                continue;
            if (current.IsIndirect && !seen.Add(current.ObjectNumber))
                continue; // guards indirect-object cycles (e.g. an outline item's /Parent back-reference)

            switch (current)
            {
                case PdfDictionary dict:
                    foreach (KeyValuePair<PdfName, PdfObject> entry in dict)
                    {
                        if (!nameReported && entry.Key.Value.Length > MaxNameBytes)
                        {
                            findings.Add(NameFinding(context, entry.Key.Value.Length));
                            nameReported = true;
                        }
                        stack.Push(entry.Value);
                    }
                    break;

                case PdfArray array:
                    foreach (PdfObject item in array)
                        stack.Push(item);
                    break;

                case PdfStream stream:
                    stack.Push(stream.Dictionary); // the stream dictionary only — never its content bytes
                    break;

                case PdfName name when !nameReported && name.Value.Length > MaxNameBytes:
                    findings.Add(NameFinding(context, name.Value.Length));
                    nameReported = true;
                    break;

                case PdfString str when !stringReported && str.Bytes.Length > MaxStringBytes:
                    findings.Add(StringFinding(context, str.Bytes.Length));
                    stringReported = true;
                    break;

                case PdfInteger integer when !integerReported && IsOutOfRange(integer):
                    findings.Add(IntegerFinding(context, integer.LongValue));
                    integerReported = true;
                    break;
            }
        }

        return findings;
    }

    // ── Sub-check 4 — over-long string operand in page content (6.1.13 test 3) ──────────────────────────
    private IEnumerable<Finding> CheckContentStreamStrings(ConformanceContext context)
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch { yield break; } // no navigable page tree — a different clause's concern

        foreach (PdfPage page in pages)
        {
            // Shared per-document parse; empty covers no content, undecodable streams and
            // unparseable content alike, each of which this rule already skipped (FP-safe).
            IReadOnlyList<PdfOperator> operators = context.PageContentOperators(page);
            if (operators.Count == 0)
                continue;

            foreach (PdfOperator op in operators)
                foreach (PdfObject operand in op.Operands)
                    if (operand is PdfString s && s.Bytes.Length > MaxStringBytes)
                    {
                        yield return StringFinding(context, s.Bytes.Length);
                        yield break; // one finding is enough to mark the document non-conformant
                    }
        }
    }

    // ── Sub-check 5 — out-of-range integer operand in page content (6.1.13 test 1) ──────────────────
    private IEnumerable<Finding> CheckContentStreamIntegers(ConformanceContext context)
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch { yield break; } // no navigable page tree — a different clause's concern

        foreach (PdfPage page in pages)
        {
            // Top-level operands: the parser reports these, because a typed operator like Td has
            // already rebuilt them as PdfReal by the time they reach op.Operands.
            if (context.PageContentHasOutOfRangeInteger(page))
            {
                yield return IntegerFinding(context);
                yield break; // one finding is enough to mark the document non-conformant
            }

            // Nested operands: a TJ array (and a BDC property dictionary) passes through
            // CreateOperator intact, so integers inside one are still observable per-object.
            // Shared per-document parse, so this costs no reparse.
            foreach (PdfOperator op in context.PageContentOperators(page))
                foreach (PdfObject operand in op.Operands)
                    foreach (PdfInteger integer in IntegersIn(operand))
                        if (IsOutOfRange(integer))
                        {
                            yield return IntegerFinding(context, integer.LongValue);
                            yield break;
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

    private Finding StringFinding(ConformanceContext context, int length) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.1.13"),
        Message = $"A string of {length} bytes exceeds the maximum permitted string length of 32767 bytes.",
    };

    private Finding NameFinding(ConformanceContext context, int length) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.1.13"),
        Message = $"A name of {length} bytes exceeds the maximum permitted name length of 127 bytes.",
    };

    private Finding IntegerFinding(ConformanceContext context, long value) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.1.13"),
        Message = $"The integer {value} is outside the permitted range "
                + "-2147483648 to 2147483647.",
    };

    /// <summary>The content-stream arm's finding. It carries no value because the parser reports only
    /// THAT an out-of-range literal occurred, not which one — the operand it belonged to has already
    /// been rebuilt as a <see cref="PdfReal"/> by the time any rule sees it.</summary>
    private Finding IntegerFinding(ConformanceContext context) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.1.13"),
        Message = "A content stream contains an integer outside the permitted range "
                + "-2147483648 to 2147483647.",
    };
}
