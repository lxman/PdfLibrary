using System;
using System.Collections.Generic;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A-2u text-to-Unicode (ISO 19005-2, 6.2.11.7.2, first requirement): every character code actually
/// used to render text must map to Unicode — through a <c>/ToUnicode</c> CMap, or a simple font's encoding
/// glyph names via the Adobe Glyph List / <c>uniXXXX</c> convention (<see cref="FontUnicodeMapping"/>). This
/// is the sole delta of level U over level B. Only codes actually drawn are checked (from
/// <see cref="ConformanceContext.UsedTextGlyphs"/>), so a font is not faulted for an unused, unmapped code.
/// One finding per font.
/// </summary>
internal sealed class Pdfa2uToUnicodeRule : IConformanceRule
{
    public string RuleId => "pdfa2u-tounicode";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfA2u;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (UsedFontCodes usage in context.UsedTextGlyphs)
        {
            foreach (int code in usage.Codes)
            {
                if (FontUnicodeMapping.HasReliableUnicode(context, usage.Font, code))
                    continue;

                if (reported.Add(usage.Font.BaseFont))
                    yield return new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, "6.2.11.7.2"),
                        Message = $"Font '{usage.Font.BaseFont}' renders text with a character code that has "
                                  + "no Unicode mapping (no /ToUnicode entry and no mappable encoding); "
                                  + "PDF/A-2u requires all rendered text to map to Unicode.",
                    };
                break; // this font instance is already accounted for
            }
        }
    }
}
