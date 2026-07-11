using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 Form XObject MCID reuse (ISO 14289-1:2014, 7.20; Matterhorn checkpoint 30-002). A Form
/// XObject whose own content stream carries a marked-content sequence with an <c>/MCID</c> maps that
/// content to a single structure content item. If such a form is invoked (<c>Do</c>) more than once, its
/// one tagged content item is drawn in several places at once, so the structure tree can no longer point at
/// a unique location for it (ISO 32000-1, 14.7.2 — a form drawn in two places breaks the structure↔content
/// correspondence). Calibrated against veraPDF's <c>PDLayer</c> model (<c>isUniqueSemanticParent</c>: false
/// when a Form XObject contains MCIDs and is referenced more than once).
/// <para>
/// A form is flagged only when BOTH hold: (a) its own content contains an <c>/MCID</c>-bearing <c>BDC</c>,
/// and (b) the document contains more than one <c>Do</c> reference edge to it. A form drawn exactly once, or
/// one with no MCIDs (e.g. an artifact-only header/footer reused on every page), is conformant. One finding
/// per offending XObject.
/// </para>
/// </summary>
internal sealed class UaXObjectMcidRule : IConformanceRule
{
    public string RuleId => "ua-xobject-mcid";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // (a) Do-reference-edge count per Form XObject, across the whole document.
        Dictionary<int, int> counts = CountReferences(context);

        // (b) + emit: only a form referenced more than once can offend, and only if its own content is
        //     tagged (carries an /MCID). Iterating context.Streams gives one finding per Form XObject in a
        //     stable order (the indirect object table).
        var findings = new List<Finding>();
        foreach (PdfStream stream in context.Streams)
        {
            if (context.ResolveName(stream.Dictionary.Get("Subtype")) != "Form")
                continue;
            if (!stream.IsIndirect)
                continue; // a reference target must be an indirect object to be Do-invoked
            if (counts.GetValueOrDefault(stream.ObjectNumber) <= 1)
                continue; // referenced 0 or 1 time — its MCIDs still map to a unique place
            if (!FormContainsMcid(stream, context))
                continue; // no tagged content of its own — safe to reuse

            findings.Add(new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.20"),
                Message = "A Form XObject contains tagged content (MCIDs) but is referenced more than once, "
                          + "so its content cannot map to a unique place in the structure tree.",
                ObjectNumber = stream.ObjectNumber,
            });
        }
        return findings;
    }

    // ── (a) Do-reference-edge counting ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Counts <c>Do</c> reference edges to every Form XObject, keyed by object number. Every page's content
    /// stream and every reached Form XObject's content stream is walked; each <c>Do</c> to a form counts one
    /// edge (a form drawn twice counts 2 — the count is <em>not</em> deduplicated). Form bodies are parsed
    /// once each (cycle-guarded on object number) to discover their inner <c>Do</c>s without recursing
    /// forever, so an edge is always counted while a body is only descended into once.
    /// </summary>
    private static Dictionary<int, int> CountReferences(ConformanceContext context)
    {
        var counts = new Dictionary<int, int>();
        var descended = new HashSet<int>(); // form bodies already parsed — shared across the whole walk

        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch (Exception) { return counts; } // no navigable page tree

        foreach (PdfPage page in pages)
        {
            // Concatenate the page's content streams so a Do split across a stream boundary still parses
            // (ISO 32000-1 7.8.2), matching the renderer and the other content collectors.
            var combined = new List<byte>();
            foreach (PdfStream content in page.GetContents())
            {
                combined.AddRange(content.GetDecodedData(context.Document.Decryptor));
                combined.Add((byte)'\n');
            }

            var counter = new ReferenceCounter(page.GetResources(), context.Document, counts, descended);
            try { counter.ProcessOperators(PdfContentParser.Parse(combined.ToArray())); }
            catch (Exception) { /* unparseable page content — skip this page, not the whole rule */ }
        }
        return counts;
    }

    // ── (b) contains-MCID (a form's OWN content only) ───────────────────────────────────────────────────

    private static bool FormContainsMcid(PdfStream form, ConformanceContext context)
    {
        var scanner = new FormMcidScanner(ResolveFormResources(form, context.Document), context.Document);
        try { scanner.ProcessOperators(PdfContentParser.Parse(form.GetDecodedData(context.Document.Decryptor))); }
        catch (Exception) { return false; } // undecodable body — cannot confirm an MCID, so do not flag
        return scanner.ContainsMcid;
    }

    /// <summary>The form's own /Resources (used only to resolve a named BDC property list); null when it has
    /// none — inline <c>&lt;&lt;/MCID n&gt;&gt;</c> property lists still resolve without any resources.</summary>
    private static PdfResources? ResolveFormResources(PdfStream form, PdfDocument? document)
    {
        if (!form.Dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? resObj))
            return null;
        if (resObj is PdfIndirectReference reference && document is not null)
            resObj = document.ResolveReference(reference);
        return resObj is PdfDictionary resDict ? new PdfResources(resDict, document) : null;
    }

    private static bool IsFormXObject(PdfStream stream) =>
        stream.Dictionary.TryGetValue(new PdfName("Subtype"), out PdfObject obj) && obj is PdfName { Value: "Form" };

    // ── walkers ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tallies <c>Do</c> reference edges to Form XObjects. Every invocation counts an edge; the shared
    /// cycle-guard set ensures each form body is parsed exactly once so nested <c>Do</c>s are discovered
    /// without infinite recursion. Modelled on <see cref="MarkedContentCollector"/>'s Form XObject recursion.
    /// </summary>
    private sealed class ReferenceCounter : PdfContentProcessor
    {
        private readonly PdfResources? _resources;
        private readonly PdfDocument? _document;
        private readonly Dictionary<int, int> _counts;   // shared across the recursion
        private readonly HashSet<int> _descended;         // shared cycle guard — a form body is parsed once

        public ReferenceCounter(PdfResources? resources, PdfDocument? document,
            Dictionary<int, int> counts, HashSet<int> descended)
        {
            _resources = resources;
            _document = document;
            _counts = counts;
            _descended = descended;
        }

        protected override void OnInvokeXObject(string name)
        {
            PdfStream? xobject = _resources?.GetXObject(name);
            if (xobject is null || !IsFormXObject(xobject) || !xobject.IsIndirect)
                return; // only an indirect Form XObject is a reference target (images are irrelevant here)

            int number = xobject.ObjectNumber;
            _counts[number] = _counts.GetValueOrDefault(number) + 1; // COUNT every Do edge

            if (!_descended.Add(number))
                return; // body already walked — count this edge, but do not DESCEND again (cycle guard)

            var nested = new ReferenceCounter(
                ResolveFormResources(xobject, _document), _document, _counts, _descended);
            try { nested.ProcessOperators(PdfContentParser.Parse(xobject.GetDecodedData(_document?.Decryptor))); }
            catch (Exception) { /* undecodable form body — keep the edge count, skip its inner Do's */ }
        }
    }

    /// <summary>
    /// Detects whether a single content stream carries an <c>/MCID</c>-bearing <c>BDC</c>. It inspects only
    /// the stream it is run over — <c>OnInvokeXObject</c> is deliberately not overridden, so the MCIDs of any
    /// nested forms (which belong to those forms) are not attributed to this one. Adapted from
    /// <see cref="MarkedContentCollector"/>'s BDC property-list / MCID resolution.
    /// </summary>
    private sealed class FormMcidScanner : PdfContentProcessor
    {
        private readonly PdfResources? _resources;
        private readonly PdfDocument? _document;

        public FormMcidScanner(PdfResources? resources, PdfDocument? document)
        {
            _resources = resources;
            _document = document;
        }

        public bool ContainsMcid { get; private set; }

        private protected override void OnGenericOperator(GenericOperator op)
        {
            if (op.Name != "BDC")
                return; // only BDC binds a property list; BMC/EMC/MP/DP cannot carry an /MCID
            PdfDictionary? props = ResolveProperties(op);
            if (props is not null && HasMcid(props))
                ContainsMcid = true;
        }

        // A BDC property list is either an inline dictionary operand or a name resolved through the
        // resource dictionary's /Properties sub-dictionary (ISO 32000-1, 14.6.1).
        private PdfDictionary? ResolveProperties(GenericOperator op)
        {
            if (op.Operands.Count < 2)
                return null;
            switch (op.Operands[1])
            {
                case PdfDictionary inline:
                    return inline;
                case PdfName name when _resources?.GetProperties() is { } properties
                                       && properties.TryGetValue(new PdfName(name.Value), out PdfObject? value):
                    if (value is PdfIndirectReference reference && _document is not null)
                        value = _document.ResolveReference(reference);
                    return value as PdfDictionary;
                default:
                    return null;
            }
        }

        private bool HasMcid(PdfDictionary props)
        {
            PdfObject? value = props.Get("MCID");
            if (value is PdfIndirectReference reference && _document is not null)
                value = _document.ResolveReference(reference);
            return value is PdfInteger;
        }
    }
}
