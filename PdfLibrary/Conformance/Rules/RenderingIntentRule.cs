using System;
using System.Collections.Generic;
using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Rendering intents (ISO 19005-2 6.2.6, shared verbatim by ISO 19005-3). Every rendering intent named in a
/// PDF/A-2/3 file shall be one of the four defined in ISO 32000-1 Table 70 — <c>RelativeColorimetric</c>,
/// <c>AbsoluteColorimetric</c>, <c>Perceptual</c> or <c>Saturation</c>. This mirrors veraPDF's
/// <c>CosRenderingIntent</c> rule, whose object is reached from every intent site: an ExtGState <c>/RI</c>
/// entry, an image (XObject or inline) <c>/Intent</c> entry, and the content-stream <c>ri</c> operator.
/// <para>
/// Object-scanning covers ExtGState <c>/RI</c> and image XObject <c>/Intent</c>; a page-content scan covers the
/// <c>ri</c> operator and inline-image <c>/Intent</c>. An intent inside a form-XObject or pattern content
/// stream is an under-report (the engine's content analyses walk page content only), never a false positive.
/// PDF/UA-1 and PDF/X-4 are excluded. Each distinct non-standard intent value is reported once.
/// </para>
/// </summary>
internal sealed class RenderingIntentRule : IConformanceRule
{
    public string RuleId => "rendering-intent";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    // ISO 32000-1 Table 70 (8.6.5.8).
    private static readonly HashSet<string> StandardIntents = new(StringComparer.Ordinal)
    {
        "RelativeColorimetric", "AbsoluteColorimetric", "Perceptual", "Saturation",
    };

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (string intent in FindIntents(context))
            if (!StandardIntents.Contains(intent) && reported.Add(intent))
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "6.2.6"),
                    Message = $"A rendering intent with the non-standard value '{intent}' is used; PDF/A permits "
                        + "only RelativeColorimetric, AbsoluteColorimetric, Perceptual or Saturation.",
                };
    }

    private static IEnumerable<string> FindIntents(ConformanceContext context)
    {
        // Object level: an ExtGState /RI entry, and an image XObject /Intent entry.
        context.Document.MaterializeAllObjects();
        foreach (PdfObject obj in context.Document.Objects.Values)
            switch (obj)
            {
                case PdfDictionary d when context.ResolveName(d.Get("Type")) == "ExtGState"
                                          && context.ResolveName(d.Get("RI")) is { } ri:
                    yield return ri;
                    break;
                case PdfStream s when context.ResolveName(s.Dictionary.Get("Subtype")) == "Image"
                                      && context.ResolveName(s.Dictionary.Get("Intent")) is { } it:
                    yield return it;
                    break;
            }

        // Content level: the ri operator and inline-image /Intent, over page content.
        foreach (string intent in ContentIntents(context))
            yield return intent;
    }

    private static IEnumerable<string> ContentIntents(ConformanceContext context)
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = context.Pages; }
        catch (Exception) { yield break; } // no navigable page tree

        foreach (PdfPage page in pages)
        {
            List<PdfOperator> operators;
            try
            {
                // Concatenate the page's content streams before parsing so an operator split across a stream
                // boundary still parses (ISO 32000-1 7.8.2), matching the other content analyses.
                var combined = new List<byte>();
                foreach (PdfStream content in page.GetContents())
                {
                    combined.AddRange(content.GetDecodedData(context.Document.Decryptor));
                    combined.Add((byte)'\n');
                }
                operators = PdfContentParser.Parse(combined.ToArray());
            }
            catch (Exception) { continue; } // unparseable content: skip this page (FP-safe)

            foreach (PdfOperator op in operators)
                switch (op)
                {
                    case SetRenderingIntentOperator ri:
                        yield return ri.Intent;
                        break;
                    case InlineImageOperator img when img.Parameters.Get("Intent") is PdfName n:
                        yield return n.Value;
                        break;
                }
        }
    }
}
