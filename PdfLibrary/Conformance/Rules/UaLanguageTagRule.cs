using System.Text.RegularExpressions;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 language identifier syntax (ISO 14289-1:2014, 7.2; ISO 32000-1 14.9; the veraPDF
/// <c>CosLangtags</c> rule): wherever a <c>/Lang</c> entry appears — the document catalog, a structure element,
/// or a marked-content property list — its value must be a valid language identifier, i.e. a sequence of
/// hyphen-separated subtags where the first is 1–8 letters and each further subtag is 1–8 letters or digits
/// (RFC 3066 / BCP 47 form). This rule scans every object that carries a <c>/Lang</c> and flags a malformed
/// value (an over-long subtag, a leading hyphen, a digit-led primary subtag, an empty value, or non-ASCII
/// letters in a UTF-16 value).
/// </summary>
internal sealed class UaLanguageTagRule : IConformanceRule
{
    public string RuleId => "ua-language-tag";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    // ISO 32000-1 14.9 language identifier: a primary 1–8 letter subtag, then any number of 1–8
    // letter-or-digit subtags separated by hyphens. (Matches the veraPDF CosLangtags test verbatim.)
    private static readonly Regex LanguageIdentifier =
        new(@"^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        context.Document.MaterializeAllObjects();

        foreach (PdfObject obj in context.Document.Objects.Values)
        {
            PdfDictionary? dict = obj as PdfDictionary ?? (obj as PdfStream)?.Dictionary;
            if (dict?.Get("Lang") is not { } langObj)
                continue;
            if (context.Resolve(langObj) is not PdfString lang)
                continue;

            // GetText decodes a UTF-16BE (FE FF) value to real Unicode, so non-ASCII letters are seen as such.
            if (LanguageIdentifier.IsMatch(lang.GetText()))
                continue;

            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.2"),
                Message = "A /Lang entry is not a valid language identifier (ISO 32000-1 14.9 / RFC 3066): its "
                          + "value must be hyphen-separated subtags, a 1-8 letter primary subtag followed by "
                          + "1-8 letter-or-digit subtags.",
                ObjectNumber = dict.IsIndirect ? dict.ObjectNumber : null,
            };
        }
    }
}
