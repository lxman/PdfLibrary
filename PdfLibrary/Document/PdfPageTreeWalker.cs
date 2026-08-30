using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>A page leaf and the immediate page-tree node that owns its /Kids entry.</summary>
internal readonly record struct PdfPageTreeLeaf(
    PdfDictionary Dictionary,
    PdfDictionary Parent,
    PdfIndirectReference? Reference);

/// <summary>
/// The shared, read-only page-tree walk used by both the document and editing surfaces. It follows
/// intermediate /Pages nodes in document order, visits each dictionary identity once, and stops at
/// cycles instead of trusting malformed /Count values.
/// </summary>
internal static class PdfPageTreeWalker
{
    internal static IReadOnlyList<PdfPageTreeLeaf> Collect(PdfDictionary root, PdfDocument? document)
    {
        var leaves = new List<PdfPageTreeLeaf>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { root };
        CollectChildren(root);
        return leaves;

        void CollectChildren(PdfDictionary node)
        {
            if (node.Get("Kids") is not PdfArray kids)
                return;

            foreach (PdfObject entry in kids)
            {
                PdfIndirectReference? reference = entry as PdfIndirectReference;
                PdfObject? resolved = reference is not null && document is not null
                    ? document.ResolveReference(reference)
                    : entry;
                if (resolved is not PdfDictionary dictionary || !seen.Add(dictionary))
                    continue;

                string? type = dictionary.Get(PdfName.TypeName) is PdfName name ? name.Value : null;
                if (type == "Pages")
                    CollectChildren(dictionary);
                else if (type == "Page")
                    leaves.Add(new PdfPageTreeLeaf(dictionary, node, reference));
            }
        }
    }
}
