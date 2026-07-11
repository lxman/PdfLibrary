using System;
using System.Collections.Generic;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 footnote/endnote identifiers (ISO 14289-1:2014, 7.9): every <c>&lt;Note&gt;</c> structure element
/// must have an <c>/ID</c> entry, and that identifier must be unique among all Note elements. Calibrated
/// against veraPDF's PDF_UA-1 rules for clause 7.9 (<c>SENote</c>):
/// <list type="bullet">
///   <item>test 1 — <c>noteID != null &amp;&amp; noteID != ''</c>: the <c>/ID</c> must be present and non-empty;</item>
///   <item>test 2 — <c>hasDuplicateNoteID == false</c>: each Note's <c>/ID</c> must be unique.</item>
/// </list>
/// Uniqueness is decided over the whole set, so the Note elements are collected in a first pass and judged in a
/// second. A Note whose <c>/ID</c> is missing or empty raises only the presence finding (not the uniqueness one);
/// identifiers are compared by their raw bytes.
/// </summary>
internal sealed class UaNoteIdRule : IConformanceRule
{
    public string RuleId => "ua-note-id";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        // Pass 1 — collect every Note element with its /ID (null when missing or empty).
        var notes = new List<(PdfDictionary Element, string? IdHex)>();
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PdfDictionary element in LogicalStructure.Elements(context.Document))
        {
            if (LogicalStructure.StandardType(context.Document, element) != "Note")
                continue;

            string? idHex = null;
            if (context.Resolve(element.Get("ID")) is PdfString { Bytes.Length: > 0 } id)
            {
                idHex = Convert.ToHexString(id.Bytes);
                idCounts[idHex] = idCounts.GetValueOrDefault(idHex) + 1;
            }
            notes.Add((element, idHex));
        }

        // Pass 2 — presence (7.9-t1) then uniqueness (7.9-t2), the latter over the whole collected set.
        foreach ((PdfDictionary element, string? idHex) in notes)
        {
            if (idHex is null)
                yield return Error(context, "A <Note> structure element has no /ID entry.", element);
            else if (idCounts[idHex] > 1)
                yield return Error(context, "A <Note> structure element has a non-unique /ID.", element);
        }
    }

    private Finding Error(ConformanceContext context, string message, PdfDictionary element) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "7.9"),
        Message = message,
        ObjectNumber = element.IsIndirect ? element.ObjectNumber : null,
    };
}
