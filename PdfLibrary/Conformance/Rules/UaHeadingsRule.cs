using System.Linq;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 headings (ISO 14289-1:2014, 7.4) — the machine-checkable Matterhorn CP14 conditions, calibrated
/// against veraPDF's PDF_UA-1 profile rules for clause 7.4:
/// <list type="bullet">
///   <item><b>Numbered-heading sequence (7.4.2; Matterhorn 14-002 + 14-003)</b> — walking the numbered
///     headings <c>H1</c>..<c>H6</c> in document reading order (structure-tree pre-order, siblings in <c>/K</c>
///     order), the first heading must be <c>H1</c> and the sequence must not skip a level while ascending.
///     Repeating a level (H2 then H2), descending (H3 then H2), and re-incrementing without restarting at H1
///     are all permitted — matching veraPDF's <c>hasCorrectNestingLevel</c>.</item>
///   <item><b>At most one &lt;H&gt; per node (7.4.4; Matterhorn 14-006)</b> — no structure element may have
///     more than one immediate <c>&lt;H&gt;</c> child.</item>
///   <item><b>No mixing of &lt;H&gt; and numbered &lt;H#&gt; (7.4.4; Matterhorn 14-007)</b> — a document is
///     either weakly structured (uses <c>&lt;H&gt;</c>) or strongly structured (uses <c>&lt;H1&gt;</c>..
///     <c>&lt;H6&gt;</c>), never both.</item>
/// </list>
/// Types are resolved through <c>/RoleMap</c> (<see cref="LogicalStructure.StandardType"/>). The reading-order
/// walk is a dedicated pre-order DFS here — <see cref="LogicalStructure.Nodes"/> yields siblings stack-reversed,
/// so it cannot drive the sequence check — while conditions 2 and 3 (order-independent) ride the same pass.
/// </summary>
internal sealed class UaHeadingsRule : IConformanceRule
{
    public string RuleId => "ua-headings";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var document = context.Document;
        if (LogicalStructure.StructTreeRootDictionary(document) is not { } root)
            yield break; // not a Tagged PDF — nothing to check

        var visited = new HashSet<int>();
        var stack = new Stack<PdfDictionary>();
        PushReversed(stack, LogicalStructure.ChildElements(document, root).ToList());

        int prev = 0;               // level of the last numbered heading seen (0 = none yet)
        bool firstHeadingSeen = false;
        bool usesH = false, usesHn = false;

        for (int budget = 500_000; stack.Count > 0 && budget > 0; budget--)
        {
            PdfDictionary element = stack.Pop();
            if (element.IsIndirect && !visited.Add(element.ObjectNumber))
                continue; // guard indirect-node cycles

            string? type = LogicalStructure.StandardType(document, element);

            // Condition 3 presence tracking — does the document use <H>, numbered <H#>, or both?
            if (type == "H")
                usesH = true;
            int? level = HeadingLevel(type);
            if (level is not null)
                usesHn = true;

            // Condition 1 — the numbered-heading sequence in reading order (7.4.2).
            if (level is { } l)
            {
                if (!firstHeadingSeen)
                {
                    firstHeadingSeen = true;
                    if (l != 1)
                        yield return Structural(context, "7.4.2", $"The first heading is <H{l}>, not <H1>.", element);
                }
                else if (l > prev + 1)
                {
                    string skipped = l - prev == 2 ? $"H{prev + 1}" : $"H{prev + 1}–H{l - 1}";
                    yield return Structural(context, "7.4.2",
                        $"Heading level jumps from H{prev} to H{l}, skipping {skipped}.",
                        element);
                }

                prev = l;
            }

            // Condition 2 — at most one immediate <H> child per node (7.4.4).
            var children = LogicalStructure.ChildElements(document, element).ToList();
            int hChildren = children.Count(c => LogicalStructure.StandardType(document, c) == "H");
            if (hChildren > 1)
                yield return Structural(context, "7.4.4", "A structure element contains more than one <H> heading.", element);

            PushReversed(stack, children);
        }

        // Condition 3 — a document must not use both <H> and numbered <H#> headings (7.4.4).
        if (usesH && usesHn)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.4.4"),
                Message = "The document uses both <H> and numbered <H#> headings.",
            };
        }
    }

    /// <summary>A structure-level finding attributed to the offending heading element (no page index).</summary>
    private Finding Structural(ConformanceContext context, string clause, string message, PdfDictionary element) =>
        new()
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target, clause),
            Message = message,
            ObjectNumber = element.IsIndirect ? element.ObjectNumber : null,
        };

    /// <summary>Pushes children onto the DFS stack in reverse so they pop in forward /K (reading) order.</summary>
    private static void PushReversed(Stack<PdfDictionary> stack, IReadOnlyList<PdfDictionary> children)
    {
        for (int i = children.Count - 1; i >= 0; i--)
            stack.Push(children[i]);
    }

    /// <summary>The heading level 1..6 for a numbered heading type <c>H1</c>..<c>H6</c>, else null (including
    /// the unnumbered <c>H</c> and any non-heading type). Standard numbered headings are single-digit, so the
    /// type is exactly two characters: <c>'H'</c> followed by one digit 1..6.</summary>
    private static int? HeadingLevel(string? standardType)
    {
        if (standardType is not { Length: 2 } t || t[0] != 'H' || t[1] is < '1' or > '6')
            return null;
        return t[1] - '0';
    }
}
