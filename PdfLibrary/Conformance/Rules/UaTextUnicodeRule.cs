using System;
using System.Collections.Generic;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 Unicode character maps (ISO 14289-1:2014, 7.21.7 test 1): the font dictionary of every used font
/// shall map all rendered character codes to Unicode — via a <c>/ToUnicode</c> entry or a mappable encoding.
/// This is the <em>font-dictionary</em> requirement; it is the UA-1 analogue of PDF/A-2u's 6.2.11.7.2 and,
/// like veraPDF's sole toUnicode rule, lives under 7.21.7 (not the text/structure clause 7.2, which a
/// content-level <c>/ActualText</c> can satisfy but this font-level check cannot). Reuses the A-2u machinery
/// — the used-glyph collection (<see cref="ConformanceContext.UsedTextGlyphs"/>) and the conservative
/// Unicode-mapping predicate (<see cref="FontUnicodeMapping"/>) — flagging only a used code with no reliable
/// mapping. One finding per font.
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
                        Clause = ConformanceClauses.For(context.Target, "7.21.7"),
                        Message = $"Font '{usage.Font.BaseFont}' renders a character code with no Unicode "
                                  + "mapping (no /ToUnicode entry and no mappable encoding); ISO 14289-1 "
                                  + "7.21.7 requires the font to map all used character codes to Unicode.",
                    };
                break;
            }
        }
    }
}
