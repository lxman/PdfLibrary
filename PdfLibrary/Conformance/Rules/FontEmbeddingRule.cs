using System.Linq;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Every font used for rendering must have its font program embedded (ISO 19005-2, 6.2.11.4.1,
/// test 1). Type3 fonts (glyphs are content streams) and the Type0 composite wrapper are exempt —
/// a Type0's embedding is verified on its descendant CIDFont, which is checked as its own font
/// object.
/// <para>
/// Scope caveat: this enumerates every /Type /Font object (see <see cref="ConformanceContext.FontDictionaries"/>)
/// rather than walking the rendering resource tree, so it (a) may over-report a non-embedded font that
/// is present but unreferenced, and (b) does not see a font written as a direct (inline) dictionary.
/// It also cannot honour the standard's rendering-mode-3 (invisible text) exemption. All three are
/// resolved once the shared resource/content walk lands (see the colour/content slice).
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
        foreach (PdfDictionary font in context.FontDictionaries)
        {
            string? subtype = (font.Get("Subtype") as PdfName)?.Value;
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
