using System.Collections.Generic;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance;

/// <summary>
/// The usage-sensitive content traversal shared by the conformance rules that inspect page content:
/// every operator in content the document actually REACHES — each page's content streams as one logical
/// stream (ISO 32000-1, 7.8.2), then transitively the content of any Form XObject invoked by a <c>Do</c>.
///
/// Reaching is the point. veraPDF only models content it reaches, so an operator or inline image sitting
/// in a form that is present in the resources but never invoked is not a violation. Reporting it would
/// break the corpus-wide zero-false-positive invariant, which is the property the whole preflighter is
/// calibrated to hold.
///
/// A form already on the active <c>Do</c> path is skipped (cycle guard) and recursion is depth-capped;
/// a stream that fails to decode or parse contributes nothing rather than aborting the walk.
/// </summary>
internal static class ContentWalk
{
    private const int MaxFormDepth = 24;

    /// <summary>Every operator in reachable content, in document order.</summary>
    public static IEnumerable<PdfOperator> ReachableOperators(ConformanceContext context)
    {
        var activeForms = new HashSet<int>();

        foreach (PdfPage page in context.Pages)
        {
            var combined = new List<byte>();
            foreach (PdfStream content in page.GetContents())
            {
                byte[] data;
                try { data = content.GetDecodedData(context.Document.Decryptor); }
                catch { continue; }
                combined.AddRange(data);
                combined.Add((byte)'\n');
            }

            foreach (PdfOperator op in Walk(context, combined.ToArray(), page.GetResources(), 0, activeForms))
                yield return op;
        }
    }

    private static IEnumerable<PdfOperator> Walk(
        ConformanceContext context, byte[] content, PdfResources? resources, int depth, HashSet<int> activeForms)
    {
        if (depth > MaxFormDepth)
            yield break;

        List<PdfOperator> ops;
        try { ops = PdfContentParser.Parse(content); }
        catch { yield break; }

        foreach (PdfOperator op in ops)
        {
            yield return op;

            if (op is InvokeXObjectOperator invoke && resources is not null)
                foreach (PdfOperator inner in WalkForm(context, invoke.XObjectName, resources, depth, activeForms))
                    yield return inner;
        }
    }

    private static IEnumerable<PdfOperator> WalkForm(
        ConformanceContext context, string name, PdfResources resources, int depth, HashSet<int> activeForms)
    {
        if (resources.GetXObject(name) is not { } form
            || context.ResolveName(form.Dictionary.Get("Subtype")) != "Form")
            yield break;
        if (form.IsIndirect && !activeForms.Add(form.ObjectNumber))
            yield break; // a form already on the active Do path — a cycle

        PdfResources? formResources =
            context.Resolve(form.Dictionary.Get("Resources")) is PdfDictionary rd
                ? new PdfResources(rd, context.Document)
                : resources; // a form without its own /Resources inherits the invoking scope's

        byte[] data;
        try { data = form.GetDecodedData(context.Document.Decryptor); }
        catch { data = []; }

        if (data.Length > 0)
            foreach (PdfOperator op in Walk(context, data, formResources, depth + 1, activeForms))
                yield return op;

        if (form.IsIndirect)
            activeForms.Remove(form.ObjectNumber);
    }
}
