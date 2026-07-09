using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/X-4 transparency-group colour (ISO 15930-7:2010): PDF/X-4 permits transparency (unlike PDF/X-1a/X-3),
/// but a transparency group's blending colour space must resolve through the file's output intent. When a
/// group (a page's or a Form XObject's <c>/Group</c> with <c>/S /Transparency</c>) specifies a device blend
/// space via <c>/CS</c>, that space must be consistent with the output intent: DeviceCMYK needs a CMYK
/// intent, DeviceRGB an RGB one, DeviceGray any. A device-independent group space (ICCBased/CalRGB/CalGray/
/// Lab) is always acceptable, and a group with no <c>/CS</c> inherits the current blending space and is not
/// flagged here. The blend space's device family is classified by <see cref="ColourSpaceClassifier"/>.
/// </summary>
internal sealed class PdfxTransparencyColourRule : IConformanceRule
{
    public string RuleId => "pdfx-transparency-colour";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfX4;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        OutputIntentColour intent = context.OutputIntentColourFamily;

        foreach (PdfDictionary group in CollectTransparencyGroups(context))
        {
            PdfObject? cs = group.Get("CS");
            if (cs is null)
                continue; // no explicit blend space — inherits the current one

            OutputIntentColour family = ColourSpaceClassifier.DeviceFamily(context, cs);
            string? mismatch = family switch
            {
                OutputIntentColour.Cmyk when intent != OutputIntentColour.Cmyk =>
                    "a DeviceCMYK blend space, but the PDF/X-4 output intent has no CMYK destination profile",
                OutputIntentColour.Rgb when intent != OutputIntentColour.Rgb =>
                    "a DeviceRGB blend space, but the PDF/X-4 output intent has no RGB destination profile",
                OutputIntentColour.Gray when intent == OutputIntentColour.None =>
                    "a DeviceGray blend space, but the file has no PDF/X-4 output intent",
                _ => null,
            };

            if (mismatch is not null)
            {
                yield return new Finding
                {
                    RuleId = RuleId,
                    Severity = FindingSeverity.Error,
                    Clause = ConformanceClauses.For(context.Target, "transparency colour"),
                    Message = $"A transparency group uses {mismatch}.",
                };
            }
        }
    }

    /// <summary>Every transparency group in the document: each page's /Group and each Form XObject's /Group
    /// whose /S is /Transparency. A group dictionary shared across objects is inspected once.</summary>
    private static IEnumerable<PdfDictionary> CollectTransparencyGroups(ConformanceContext context)
    {
        var seen = new HashSet<int>();

        IEnumerable<PdfDictionary> FromCarrier(PdfObject? groupObj)
        {
            if (context.Resolve(groupObj) is PdfDictionary group
                && context.ResolveName(group.Get("S")) == "Transparency"
                && (!group.IsIndirect || seen.Add(group.ObjectNumber)))
            {
                yield return group;
            }
        }

        foreach (PdfPage page in context.Pages)
            foreach (PdfDictionary group in FromCarrier(page.Dictionary.Get("Group")))
                yield return group;

        foreach (PdfStream stream in context.Streams)
            if (context.ResolveName(stream.Dictionary.Get("Subtype")) == "Form")
                foreach (PdfDictionary group in FromCarrier(stream.Dictionary.Get("Group")))
                    yield return group;
    }
}
