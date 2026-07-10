using System;
using System.Collections.Generic;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 text (ISO 14289-1:2014, 7.2): all textual content must be represented in Unicode, so assistive
/// technology can read it. Reuses the PDF/A-2u machinery — the used-glyph collection
/// (<see cref="ConformanceContext.UsedTextGlyphs"/>) and the conservative Unicode-mapping predicate
/// (<see cref="FontUnicodeMapping"/>) — flagging only a used code with no reliable mapping. (The rule does
/// not yet credit an <c>/ActualText</c> substitution, which is a marked-content construct; the predicate's
/// conservatism keeps that from causing false positives.) One finding per font.
/// </summary>
internal sealed class UaTextUnicodeRule : IConformanceRule
{
    public string RuleId => "ua-text-unicode";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

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
                        Clause = ConformanceClauses.For(context.Target, "7.2"),
                        Message = $"Font '{usage.Font.BaseFont}' renders text with a character code that has no "
                                  + "Unicode mapping; PDF/UA requires all text to be represented in Unicode.",
                    };
                break;
            }
        }
    }
}
