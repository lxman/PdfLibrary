using System.Linq;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Every font used for rendering must have its font program embedded (ISO 19005-2, 6.2.11.4.1,
/// test 1). Type3 fonts (glyphs are content streams) and the Type0 composite wrapper are exempt —
/// a Type0's embedding is verified on its descendant CIDFont, which the rendering-tree walk reaches
/// and which is checked as its own font object.
/// <para>
/// Fonts are taken from <see cref="ConformanceContext.ReferencedFonts"/> — those reachable from the
/// rendering resource tree — so a font that is present but unreferenced (e.g. an unused AcroForm /DR
/// font) is not reported. The one remaining approximation is that a referenced-but-never-drawn font, or
/// text drawn only in rendering mode 3 (invisible), is still treated as used.
/// </para>
/// </summary>
internal sealed class FontEmbeddingRule : IConformanceRule
{
    private static readonly PdfName[] FontFileKeys =
        [new PdfName("FontFile"), new PdfName("FontFile2"), new PdfName("FontFile3")];

    public string RuleId => "font-embedded";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.All;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary font in context.ReferencedFonts)
        {
            string? subtype = context.ResolveName(font.Get("Subtype"));
            if (subtype is "Type3" or "Type0")
                continue; // exempt / checked via descendant CIDFont

            if (context.Resolve(font.Get("FontDescriptor")) is PdfDictionary descriptor
                && FontFileKeys.Any(key => context.Resolve(descriptor.Get(key)) is PdfStream))
                continue; // embedded

            string baseFont = (font.Get("BaseFont") as PdfName)?.Value ?? "(unnamed)";
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "6.2.11.4.1"),
                Message = $"The {subtype ?? "font"} font '{baseFont}' is not embedded.",
                ObjectNumber = font.IsIndirect ? font.ObjectNumber : null,
            };
        }
    }
}
