using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 media clip data requirements (ISO 14289-1:2014, 7.18.6.2). A media clip data dictionary is
/// reached from an annotation's Rendition action: <c>annotation → /A (or an /AA additional action, or a
/// /Next chain) → action /S /Rendition → /R (rendition) → /C (the media clip)</c>. Following veraPDF's model
/// (<c>PDMediaClip</c>) every dictionary at that <c>/R → /C</c> position is treated as a media clip, and the
/// clause requires:
/// <list type="number">
///   <item><b>7.18.6.2</b> (t1) — a <c>/CT</c> content-type string (an RFC 2045 media type identifying the
///     data in <c>/D</c>). veraPDF reads it with <c>getStringKey(CT)</c>, so the entry must be present and a
///     string; a present-but-empty string satisfies the check. Under ISO 14289-1:2014 a media clip's <c>/D</c>
///     is always a file/stream (the PDF 2.0 form-XObject option is out of the normative ISO 32000-1:2008
///     reference), so <c>/CT</c> is always applicable.</item>
///   <item><b>7.18.6.2</b> (t2) — a correct <c>/Alt</c> multi-language text array (see 14.9.2): an array of
///     even length whose every element is a string and whose every text value (the odd-indexed entries) is
///     non-empty. An empty language KEY (an even-indexed entry) is allowed — it denotes the default text.</item>
/// </list>
/// The annotation-action traversal is a subset of veraPDF's document-wide action graph, so it never reports a
/// media clip veraPDF would not.
/// </summary>
internal sealed class UaMediaClipRule : IConformanceRule
{
    public string RuleId => "ua-media-clip";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var visitedActions = new HashSet<int>(); // /Next-cycle and shared-action guard (indirect actions)
        var reportedClips = new HashSet<int>();   // one report per indirect clip reached from several actions

        foreach (PdfPage page in context.Pages)
        {
            if (page.GetAnnotations() is not { } annots)
                continue;

            foreach (PdfObject entry in annots)
            {
                if (context.Resolve(entry) is not PdfDictionary annot)
                    continue;

                foreach (PdfDictionary action in RenditionActions(context, annot, visitedActions))
                {
                    if (context.Resolve(action.Get("R")) is not PdfDictionary rendition
                        || context.Resolve(rendition.Get("C")) is not PdfDictionary clip)
                        continue;

                    if (clip.IsIndirect && !reportedClips.Add(clip.ObjectNumber))
                        continue;

                    // t1 — a present content-type string.
                    if (context.Resolve(clip.Get("CT")) is not PdfString)
                        yield return Error(context,
                            "A media clip data dictionary has no /CT content-type string identifying the type "
                            + "of its media data.");

                    // t2 — a correct multi-language /Alt array.
                    if (!HasCorrectAlt(context, clip))
                        yield return Error(context,
                            "A media clip data dictionary has no correct /Alt entry (a multi-language text "
                            + "array whose text values are non-empty strings) giving an alternate description.");
                }
            }
        }
    }

    /// <summary>The Rendition actions (<c>/S /Rendition</c>) reachable from an annotation: its <c>/A</c>, each
    /// value of its <c>/AA</c> additional-actions dictionary, and every action along their <c>/Next</c> chains.
    /// Cycle- and duplicate-guarded on indirect action object numbers.</summary>
    private static IEnumerable<PdfDictionary> RenditionActions(
        ConformanceContext context, PdfDictionary annot, HashSet<int> visited)
    {
        var stack = new Stack<PdfObject?>();
        stack.Push(annot.Get("A"));
        if (context.Resolve(annot.Get("AA")) is PdfDictionary aa)
            foreach (PdfObject value in aa.Values)
                stack.Push(value);

        while (stack.Count > 0)
        {
            if (context.Resolve(stack.Pop()) is not PdfDictionary action)
                continue;
            if (action.IsIndirect && !visited.Add(action.ObjectNumber))
                continue;

            // /Next is a single action or an array of actions (ISO 32000-1:2008, 12.6.1).
            switch (context.Resolve(action.Get("Next")))
            {
                case PdfArray next:
                    foreach (PdfObject n in next) stack.Push(n);
                    break;
                case PdfDictionary:
                    stack.Push(action.Get("Next"));
                    break;
            }

            if (context.ResolveName(action.Get("S")) == "Rendition")
                yield return action;
        }
    }

    /// <summary>veraPDF's <c>hasCorrectAlt</c>: <c>/Alt</c> is an array of even length, every element is a
    /// string, and every odd-indexed element (the text value of each language/text pair) is non-empty.</summary>
    private static bool HasCorrectAlt(ConformanceContext context, PdfDictionary clip)
    {
        if (context.Resolve(clip.Get("Alt")) is not PdfArray alt || alt.Count % 2 != 0)
            return false;
        for (int i = 0; i < alt.Count; i++)
        {
            if (context.Resolve(alt[i]) is not PdfString s)
                return false;
            if (i % 2 == 1 && s.Bytes.Length == 0) // an odd index is a text value; it must be non-empty
                return false;
        }
        return true;
    }

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "7.18.6.2"),
        Message = message,
    };
}
