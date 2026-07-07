using System;
using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 page geometry (ISO 15930-7): every page must define a TrimBox or an ArtBox — but not both —
/// and that box must lie within the page's MediaBox.
/// </summary>
internal sealed class PageBoxesRule : IConformanceRule
{
    public string RuleId => "pdfx-page-boxes";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        int pageIndex = 0;
        foreach (PdfPage page in context.Pages)
        {
            PdfRectangle? trim = Box(context, page, "TrimBox");
            PdfRectangle? art = Box(context, page, "ArtBox");

            if (trim is null && art is null)
            {
                yield return Error(context, pageIndex, "must define a TrimBox or an ArtBox");
            }
            else if (trim is not null && art is not null)
            {
                yield return Error(context, pageIndex, "must not define both a TrimBox and an ArtBox");
            }
            else if (!Within((trim ?? art)!.Value, page.GetMediaBox()))
            {
                yield return Error(context, pageIndex, "has a TrimBox/ArtBox that extends beyond the MediaBox");
            }

            pageIndex++;
        }
    }

    /// <summary>Reads a rectangle key off the page, resolving indirect coordinate elements and tolerating a
    /// malformed (non-numeric or wrong-length) array by returning null rather than throwing.</summary>
    private static PdfRectangle? Box(ConformanceContext context, PdfPage page, string key)
    {
        if (context.Resolve(page.Dictionary.Get(key)) is not PdfArray array || array.Count < 4)
            return null;

        double? x1 = Number(context.Resolve(array[0]));
        double? y1 = Number(context.Resolve(array[1]));
        double? x2 = Number(context.Resolve(array[2]));
        double? y2 = Number(context.Resolve(array[3]));
        return x1 is null || y1 is null || x2 is null || y2 is null
            ? null
            : new PdfRectangle(x1.Value, y1.Value, x2.Value, y2.Value);
    }

    private static double? Number(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null,
    };

    private static bool Within(PdfRectangle inner, PdfRectangle outer)
    {
        const double tolerance = 0.001;
        (double il, double ib, double ir, double it) = Normalize(inner);
        (double ol, double ob, double or, double ot) = Normalize(outer);
        return il >= ol - tolerance && ib >= ob - tolerance && ir <= or + tolerance && it <= ot + tolerance;
    }

    private static (double Left, double Bottom, double Right, double Top) Normalize(PdfRectangle r) =>
        (Math.Min(r.X1, r.X2), Math.Min(r.Y1, r.Y2), Math.Max(r.X1, r.X2), Math.Max(r.Y1, r.Y2));

    private Finding Error(ConformanceContext context, int pageIndex, string what) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        Clause = ConformanceClauses.For(context.Target, "page boxes"),
        Message = $"Page {pageIndex + 1} {what} (PDF/X-4).",
        PageIndex = pageIndex,
    };
}
