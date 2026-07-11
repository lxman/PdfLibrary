using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// PDF/UA-1 standard structure types (ISO 14289-1:2014, 7.1; ISO 32000-1 14.8.4): every structure element's
/// type must be one of the standard structure types, or be mapped to a standard type through the structure
/// tree root's <c>/RoleMap</c>. A custom type that role-maps to nothing standard (e.g. <c>/RoleMap</c> mapping
/// <c>Standard → p</c>, where <c>p</c> is not the standard <c>P</c>) leaves the content's role opaque to
/// assistive technology. <see cref="LogicalStructure.StandardType"/> resolves the mapping.
/// </summary>
internal sealed class UaStandardTypeRule : IConformanceRule
{
    public string RuleId => "ua-standard-type";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (StructureNode node in LogicalStructure.Nodes(context.Document))
        {
            if (LogicalStructure.IsStandardType(node.StandardType))
                continue;

            string raw = context.ResolveName(node.Element.Get("S")) ?? "(none)";
            yield return new Finding
            {
                RuleId = RuleId,
                Severity = FindingSeverity.Error,
                Clause = ConformanceClauses.For(context.Target, "7.1"),
                Message = $"Structure type '{raw}' is not a standard structure type and is not mapped through "
                          + "/RoleMap to one; PDF/UA requires every structure type to be standard or role-mapped "
                          + "to a standard type.",
                ObjectNumber = node.Element.IsIndirect ? node.Element.ObjectNumber : null,
            };
        }
    }
}
