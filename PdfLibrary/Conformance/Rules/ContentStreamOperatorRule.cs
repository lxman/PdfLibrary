using System;
using System.Collections.Generic;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A clause 6.2.2 (ISO 19005-2:2011 / -3:2012, calibrated against veraPDF's <c>Op_Undefined</c> rule,
/// test <c>false</c>): a content stream shall not contain any operator that is not defined in ISO 32000-1,
/// <b>even when bracketed by the BX/EX compatibility operators</b>. The engine's content parser already
/// preserves an unrecognised operator token as a <see cref="GenericOperator"/> carrying its name (and never
/// special-cases BX/EX), so an operator is undefined exactly when its name is not one of the 73 ISO 32000-1
/// operators below; inline-image binary is collapsed into a single <c>BI</c> operator, so it never leaks.
///
/// The walk is usage-sensitive — page content, then the content of any Form XObject actually invoked by a
/// <c>Do</c> (transitively) — mirroring veraPDF, which only models content it reaches. A stray operator in a
/// Form that is present in the resources but never invoked is therefore not reported, preserving the
/// 0-false-positive invariant.
///
/// KNOWN LIMITATION (clause 6.2.2 test t04): the shared content lexer recovers from a malformed run-together
/// operator by splitting it into valid operators (e.g. <c>ref</c> → <c>re</c> + <c>f</c>, <c>sc0</c> → <c>sc</c>
/// + <c>0</c>), so such a stream never surfaces an undefined token here even though veraPDF tokenises it as one
/// <c>Op_Undefined</c>. Catching that needs spec-strict content tokenisation, a lexer change with rendering
/// robustness trade-offs, tracked separately. It only ever under-reports (never a false positive).
/// </summary>
internal sealed class ContentStreamOperatorRule : IConformanceRule
{
    public string RuleId => "content-stream-operator";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    // The content-stream operators defined by ISO 32000-1:2008 (Annex A). Matches veraPDF's Operators set.
    private static readonly HashSet<string> Defined = new(StringComparer.Ordinal)
    {
        "b", "B", "b*", "B*", "BDC", "BI", "BMC", "BT", "BX", "c", "cm", "cs", "CS", "d", "d0", "d1", "Do", "DP",
        "EI", "EMC", "ET", "EX", "f", "F", "f*", "g", "G", "gs", "h", "i", "ID", "j", "J", "k", "K", "l", "m",
        "M", "MP", "n", "q", "Q", "re", "rg", "RG", "ri", "s", "S", "sc", "SC", "scn", "SCN", "sh", "T*", "Tc",
        "Td", "TD", "Tf", "Tj", "TJ", "TL", "Tm", "Tr", "Ts", "Tw", "Tz", "v", "w", "W", "W*", "y", "'", "\"",
    };

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal); // one finding per distinct undefined operator
        var findings = new List<Finding>();
        var activeForms = new HashSet<int>();                        // Do-recursion cycle guard (active path)

        foreach (PdfPage page in context.Pages)
        {
            var combined = new List<byte>();
            foreach (PdfStream content in page.GetContents())
            {
                byte[] data;
                try { data = content.GetDecodedData(context.Document.Decryptor); }
                catch { continue; }
                combined.AddRange(data);
                combined.Add((byte)'\n'); // a page's streams are one logical stream (ISO 32000-1, 7.8.2)
            }
            Walk(context, combined.ToArray(), page.GetResources(), 0, activeForms, reported, findings);
        }

        return findings;
    }

    private void Walk(ConformanceContext context, byte[] content, PdfResources? resources, int depth,
        HashSet<int> activeForms, HashSet<string> reported, List<Finding> findings)
    {
        if (depth > 24)
            return;

        List<PdfOperator> ops;
        try { ops = PdfContentParser.Parse(content); }
        catch { return; }

        foreach (PdfOperator op in ops)
        {
            if (!Defined.Contains(op.Name))
            {
                if (reported.Add(op.Name))
                    findings.Add(Error(context, op.Name));
            }
            else if (op is InvokeXObjectOperator invoke && resources is not null)
            {
                WalkForm(context, invoke.XObjectName, resources, depth, activeForms, reported, findings);
            }
        }
    }

    private void WalkForm(ConformanceContext context, string name, PdfResources resources, int depth,
        HashSet<int> activeForms, HashSet<string> reported, List<Finding> findings)
    {
        if (resources.GetXObject(name) is not { } form
            || context.ResolveName(form.Dictionary.Get("Subtype")) != "Form")
            return;
        if (form.IsIndirect && !activeForms.Add(form.ObjectNumber))
            return; // a form already on the active Do path — a cycle

        PdfResources? formResources =
            context.Resolve(form.Dictionary.Get("Resources")) is PdfDictionary rd
                ? new PdfResources(rd, context.Document)
                : resources; // a form without its own /Resources inherits the invoking scope's

        byte[] data;
        try { data = form.GetDecodedData(context.Document.Decryptor); }
        catch { data = []; }
        if (data.Length > 0)
            Walk(context, data, formResources, depth + 1, activeForms, reported, findings);

        if (form.IsIndirect)
            activeForms.Remove(form.ObjectNumber);
    }

    private Finding Error(ConformanceContext context, string operatorName) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.2.2"),
        Message = $"A content stream contains the operator '{operatorName}', which is not defined in "
                  + "ISO 32000-1 (PDF/A permits only standard operators, even inside BX/EX).",
    };
}
