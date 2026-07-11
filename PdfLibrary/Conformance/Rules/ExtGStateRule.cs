using System;
using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Extended graphics state (ISO 19005-2 6.2.5, shared verbatim by ISO 19005-3). An ExtGState dictionary in a
/// PDF/A-2/3 file must not defeat device-independent rendering. This rule object-scans every ExtGState
/// dictionary (a materialised indirect object with <c>/Type /ExtGState</c>) and reports:
/// <list type="bullet">
///   <item>a <c>/TR</c> (transfer function) key — forbidden outright;</item>
///   <item>a <c>/HTP</c> key (a halftone-phase key from PDF ≤1.3) — forbidden;</item>
///   <item>a <c>/TR2</c> key whose value is not the name <c>Default</c>;</item>
///   <item>a <c>/RI</c> key whose value is not one of the four standard rendering intents;</item>
///   <item>a halftone reached through <c>/HT</c> — and, for a Type 5 composite, each component halftone —
///     whose <c>HalftoneType</c> is not 1 or 5, or that carries a <c>HalftoneName</c>.</item>
/// </list>
/// <para>
/// The Type 5 "a non-primary colourant component shall carry a TransferFunction" constraint (also 6.2.5) is
/// deliberately not implemented: its corpus fixture (6-2-5-t03-fail-b) treats Red/Green/Blue as non-primary,
/// which contradicts ISO 32000-1's list of DeviceRGB primaries, so pinning it needs the veraPDF profile's exact
/// primary-colourant set. It is left unreported to avoid a rule that could false-positive on a conformant Type 5
/// halftone — the clause's only remaining gap here.
/// </para>
/// <para>
/// PDF/UA-1 and PDF/X-4 are excluded (no equivalent constraint / a separate colour regime). Detection matches
/// <see cref="PdfxBlendModeRule"/> — it scans materialised indirect objects, so a direct (inline) ExtGState
/// inside a resource dictionary, or an ExtGState omitting the optional <c>/Type</c>, is an under-report, never a
/// false positive. Each distinct violation message is reported once per document.
/// </para>
/// </summary>
internal sealed class ExtGStateRule : IConformanceRule
{
    public string RuleId => "graphics-state";

    // PDF/A-2 and PDF/A-3 only; the clause has no PDF/UA-1 or PDF/X-4 equivalent.
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    // ISO 32000-1 8.6.5.8 — the four standard rendering intents.
    private static readonly HashSet<string> StandardIntents = new(StringComparer.Ordinal)
    {
        "RelativeColorimetric", "AbsoluteColorimetric", "Perceptual", "Saturation",
    };

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        context.Document.MaterializeAllObjects();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (PdfDictionary gs in context.Document.Objects.Values.OfType<PdfDictionary>())
        {
            if (context.ResolveName(gs.Get("Type")) != "ExtGState")
                continue;

            int? obj = gs.IsIndirect ? gs.ObjectNumber : null;
            foreach (string message in Violations(context, gs))
                if (reported.Add(message))
                    yield return Make(context, obj, message);
        }
    }

    private static IEnumerable<string> Violations(ConformanceContext context, PdfDictionary gs)
    {
        if (gs.Get("TR") is not null)
            yield return "The ExtGState dictionary contains the TR (transfer function) key, which is not permitted.";

        if (gs.Get("HTP") is not null)
            yield return "The ExtGState dictionary contains the HTP key, which is not permitted.";

        if (gs.Get("TR2") is not null && context.ResolveName(gs.Get("TR2")) != "Default")
            yield return "The ExtGState dictionary contains a TR2 key with a value other than Default.";

        if (context.ResolveName(gs.Get("RI")) is { } ri && !StandardIntents.Contains(ri))
            yield return $"The ExtGState dictionary specifies the non-standard rendering intent '{ri}'.";

        foreach (string m in HalftoneViolations(context, gs.Get("HT"), new HashSet<PdfObject>()))
            yield return m;
    }

    /// <summary>
    /// Reports HalftoneType / HalftoneName violations for the halftone at <paramref name="htObj"/>, recursing
    /// into the per-colourant component halftones of a Type 5 composite. A <c>/HT</c> that resolves to a name
    /// (e.g. <c>/Default</c>) has no dictionary to inspect. <paramref name="visited"/> guards against a cyclic
    /// Type 5 reference.
    /// </summary>
    private static IEnumerable<string> HalftoneViolations(
        ConformanceContext context, PdfObject? htObj, HashSet<PdfObject> visited)
    {
        PdfObject? resolved = context.Resolve(htObj);
        PdfDictionary? ht = resolved switch
        {
            PdfDictionary d => d,
            PdfStream s => s.Dictionary,
            _ => null,
        };
        if (ht is null || !visited.Add(resolved!))
            yield break;

        int? type = (context.Resolve(ht.Get("HalftoneType")) as PdfInteger)?.Value;
        if (type is not null and not 1 and not 5)
            yield return $"A halftone in the ExtGState has HalftoneType {type}, but PDF/A permits only 1 or 5.";

        if (ht.Get("HalftoneName") is not null)
            yield return "A halftone in the ExtGState contains a HalftoneName key, which is not permitted.";

        if (type == 5)
            foreach (PdfName key in ht.Keys.ToList())
            {
                // Structural keys aside, every entry of a Type 5 halftone is a colourant component (or Default).
                if (key.Value is "Type" or "HalftoneType" or "HalftoneName")
                    continue;
                foreach (string m in HalftoneViolations(context, ht.Get(key), visited))
                    yield return m;
            }
    }

    private Finding Make(ConformanceContext context, int? objectNumber, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "6.2.5"),
        Message = message,
        ObjectNumber = objectNumber,
    };
}
