using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A-2/3 clause 6.2.8 — image XObject restrictions, calibrated against veraPDF's PDFA-2 rules:
/// an image dictionary shall not contain <c>/Alternates</c> or <c>/OPI</c>; if <c>/Interpolate</c> is
/// present its value shall be false; <c>/BitsPerComponent</c> shall be 1, 2, 4, 8, or 16 — and exactly
/// 1 for an image mask. One finding per offending image.
/// </summary>
internal sealed class ImageDictionaryRule : IConformanceRule
{
    public string RuleId => "image-dictionary";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA;

    private static readonly long[] AllowedBitsPerComponent = [1, 2, 4, 8, 16];

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfStream stream in context.Streams)
        {
            if (context.ResolveName(stream.Dictionary.Get("Subtype")) != "Image")
                continue;

            PdfDictionary dict = stream.Dictionary;
            var violations = new List<string>();

            if (dict.Get("Alternates") is not null)
                violations.Add("/Alternates");
            if (dict.Get("OPI") is not null)
                violations.Add("/OPI");
            if ((context.Resolve(dict.Get("Interpolate")) as PdfBoolean)?.Value == true)
                violations.Add("/Interpolate true");

            bool isMask = (context.Resolve(dict.Get("ImageMask")) as PdfBoolean)?.Value == true;
            if (context.Resolve(dict.Get("BitsPerComponent")) is PdfInteger bpc)
            {
                long v = bpc.Value;
                bool ok = isMask ? v == 1 : AllowedBitsPerComponent.Contains(v);
                if (!ok)
                    violations.Add(isMask
                        ? $"/BitsPerComponent {v} (an image mask requires 1)"
                        : $"/BitsPerComponent {v} (must be 1, 2, 4, 8, or 16)");
            }

            if (violations.Count == 0)
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.2.8"),
                Message = "An image XObject dictionary violates PDF/A image restrictions: "
                          + string.Join(", ", violations) + ".",
                ObjectNumber = stream.IsIndirect ? stream.ObjectNumber : null,
            };
        }
    }
}
