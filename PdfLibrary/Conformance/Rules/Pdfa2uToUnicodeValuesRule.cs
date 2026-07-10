using System;
using System.Collections.Generic;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/A-2u ToUnicode values (ISO 19005-2, 6.2.11.7.2, second requirement): where a used character has a
/// <c>/ToUnicode</c> mapping, its value must be a usable Unicode value — not empty and not U+0000 or U+FFFF.
/// Only codes actually drawn are checked (<see cref="ConformanceContext.UsedTextGlyphs"/>). One finding per font.
/// </summary>
internal sealed class Pdfa2uToUnicodeValuesRule : IConformanceRule
{
    public string RuleId => "pdfa2u-tounicode-values";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfA2u;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (UsedFontCodes usage in context.UsedTextGlyphs)
        {
            foreach (int code in usage.Codes)
            {
                string? value = FontUnicodeMapping.ToUnicodeValue(usage.Font, code);
                if (value is null || !FontUnicodeMapping.IsForbiddenUnicodeValue(value))
                    continue;

                if (reported.Add(usage.Font.BaseFont))
                    yield return new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, "6.2.11.7.2"),
                        Message = $"Font '{usage.Font.BaseFont}' has a /ToUnicode entry mapping a rendered "
                                  + "character to U+0000 or U+FFFF, which PDF/A-2u forbids.",
                    };
                break;
            }
        }
    }
}
