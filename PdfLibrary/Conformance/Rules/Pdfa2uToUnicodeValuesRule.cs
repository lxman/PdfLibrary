using System;
using System.Collections.Generic;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// ToUnicode value validity: where a used character has a <c>/ToUnicode</c> mapping, its value must be a
/// usable Unicode value. One profile-aware rule per shared clause, with a profile-specific forbidden set:
/// <list type="bullet">
///   <item><b>PDF/A-2u</b> (ISO 19005-2, 6.2.11.7.2, second requirement): the value must not be empty and
///     must not map to U+0000, U+FEFF, U+FFFE or U+FFFF (<see cref="FontUnicodeMapping.IsForbiddenUnicodeValue"/>).</item>
///   <item><b>PDF/UA-1</b> (ISO 14289-1, 7.21.7, test 2): the value must not <em>contain</em> U+0000, U+FFFE
///     or U+FEFF (<see cref="FontUnicodeMapping.PdfUa1ForbiddenCodePoints"/>). Note the UA set is distinct —
///     it excludes U+FFFF and does not fault an empty value (that is the text-to-Unicode rule 7.2's concern).</item>
/// </list>
/// Only codes actually drawn are checked (<see cref="ConformanceContext.UsedTextGlyphs"/>). One finding per font.
/// </summary>
internal sealed class Pdfa2uToUnicodeValuesRule : IConformanceRule
{
    public string RuleId => "pdfa2u-tounicode-values";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfA2u | ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        bool isUa = context.Target == ConformanceProfile.PdfUA1;
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (UsedFontCodes usage in context.UsedTextGlyphs)
        {
            foreach (int code in usage.Codes)
            {
                string? value = FontUnicodeMapping.ToUnicodeValue(usage.Font, code);
                bool forbidden = value is not null && (isUa
                    ? FontUnicodeMapping.ContainsForbiddenCodePoint(value, FontUnicodeMapping.PdfUa1ForbiddenCodePoints)
                    : FontUnicodeMapping.IsForbiddenUnicodeValue(value));
                if (!forbidden)
                    continue;

                if (reported.Add(usage.Font.BaseFont))
                    yield return new Finding
                    {
                        RuleId = RuleId,
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(context.Target, isUa ? "7.21.7" : "6.2.11.7.2"),
                        Message = isUa
                            ? $"Font '{usage.Font.BaseFont}' has a /ToUnicode entry mapping a rendered "
                              + "character to U+0000, U+FFFE or U+FEFF, which PDF/UA-1 forbids."
                            : $"Font '{usage.Font.BaseFont}' has a /ToUnicode entry mapping a rendered "
                              + "character to U+0000 or U+FFFF, which PDF/A-2u forbids.",
                    };
                break;
            }
        }
    }
}
