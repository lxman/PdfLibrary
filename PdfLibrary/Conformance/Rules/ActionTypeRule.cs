using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Permitted action types (ISO 19005-2, 6.5.1):
/// <list type="bullet">
///   <item>test 1 — an action's /S type must be one of GoTo, GoToR, GoToE, Thread, URI, Named or
///     SubmitForm (this excludes Launch, JavaScript, Sound, Movie, ResetForm, ImportData, Hide,
///     SetOCGState, Rendition, Trans and GoTo3DView);</item>
///   <item>test 2 — a Named action's /N must be NextPage, PrevPage, FirstPage or LastPage.</item>
/// </list>
/// Actions are gathered from every reachable trigger: annotation and field /A and /AA, the catalog
/// /OpenAction and /AA, page /AA, outline-item /A, the /Names /JavaScript name tree, and chained
/// /Next actions.
/// </summary>
internal sealed class ActionTypeRule : IConformanceRule
{
    private static readonly HashSet<string> AllowedActions =
        ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm"];

    private static readonly HashSet<string> AllowedNamedActions =
        ["NextPage", "PrevPage", "FirstPage", "LastPage"];

    public string RuleId => "action-type";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary action in CollectActions(context))
        {
            string? type = context.ResolveName(action.Get("S"));
            if (type is null || !AllowedActions.Contains(type))
            {
                yield return Error(context, action, type is null
                    ? "An action dictionary has no /S action type."
                    : $"Action type '{type}' is not permitted in PDF/A.");
                continue;
            }

            if (type == "Named")
            {
                string? name = context.ResolveName(action.Get("N"));
                if (name is null || !AllowedNamedActions.Contains(name))
                {
                    yield return Error(context, action,
                        $"Named action '{name ?? "(none)"}' is not one of NextPage, PrevPage, FirstPage or LastPage.");
                }
            }
        }
    }

    private static IEnumerable<PdfDictionary> CollectActions(ConformanceContext context)
    {
        var indirectSeen = new HashSet<int>();
        var directSeen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var result = new List<PdfDictionary>();
        var queue = new Queue<PdfObject?>();

        foreach (PdfDictionary annot in context.Annotations)
        {
            queue.Enqueue(annot.Get("A"));
            EnqueueTriggerActions(context, queue, annot.Get("AA"));
        }
        foreach (PdfDictionary field in context.FormFields)
        {
            queue.Enqueue(field.Get("A"));
            EnqueueTriggerActions(context, queue, field.Get("AA"));
        }
        if (context.Catalog?.Dictionary is { } catalog)
        {
            queue.Enqueue(catalog.Get("OpenAction")); // may be a destination array — filtered out below
            EnqueueTriggerActions(context, queue, catalog.Get("AA"));
            EnqueueJavaScriptNames(context, queue, catalog);
        }
        foreach (PdfPage page in context.Pages)
            EnqueueTriggerActions(context, queue, page.Dictionary.Get("AA"));
        EnqueueOutlineActions(context, queue);

        while (queue.Count > 0)
        {
            // Destination arrays, names and nulls are not action dictionaries; a dictionary that reaches
            // here is an action — including a malformed one with no /S, which 6.5.1-t1 rejects (S == null).
            if (context.Resolve(queue.Dequeue()) is not PdfDictionary action)
                continue;

            bool fresh = action.IsIndirect ? indirectSeen.Add(action.ObjectNumber) : directSeen.Add(action);
            if (!fresh)
                continue;

            result.Add(action);

            // Follow the /Next chain (a single action or an array of actions).
            PdfObject? next = context.Resolve(action.Get("Next"));
            if (next is PdfArray chain)
                foreach (PdfObject link in chain)
                    queue.Enqueue(link);
            else if (next is PdfDictionary)
                queue.Enqueue(next);
        }

        return result;
    }

    private static void EnqueueTriggerActions(ConformanceContext context, Queue<PdfObject?> queue, PdfObject? additionalActions)
    {
        if (context.Resolve(additionalActions) is PdfDictionary aa)
            foreach (PdfObject action in aa.Values)
                queue.Enqueue(action);
    }

    /// <summary>Enqueues each outline item's /A action, walking the /First + /Next tree (cycle-guarded).</summary>
    private static void EnqueueOutlineActions(ConformanceContext context, Queue<PdfObject?> queue)
    {
        if (context.Catalog?.GetOutlines() is not { } outlines)
            return;

        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        stack.Push(outlines.Get("First"));
        while (stack.Count > 0)
        {
            if (context.Resolve(stack.Pop()) is not PdfDictionary item)
                continue;
            if (item.IsIndirect && !visited.Add(item.ObjectNumber))
                continue;

            queue.Enqueue(item.Get("A"));
            stack.Push(item.Get("Next"));  // sibling
            stack.Push(item.Get("First")); // child
        }
    }

    /// <summary>Enqueues every action in the catalog's /Names /JavaScript name tree (document-level scripts).</summary>
    private static void EnqueueJavaScriptNames(ConformanceContext context, Queue<PdfObject?> queue, PdfDictionary catalog)
    {
        if (context.Resolve(catalog.Get("Names")) is not PdfDictionary names)
            return;
        foreach (PdfObject action in context.EnumerateNameTree(names.Get("JavaScript")))
            queue.Enqueue(action);
    }

    private Finding Error(ConformanceContext context, PdfDictionary action, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.5.1"),
        Message = message,
        ObjectNumber = action.IsIndirect ? action.ObjectNumber : null,
    };
}
