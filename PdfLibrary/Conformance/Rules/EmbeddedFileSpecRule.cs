using System.Collections.Generic;
using System.Text.RegularExpressions;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Embedded-file requirements (ISO 19005, 6.8). Applies to file specifications reachable from the
/// catalog's /Names /EmbeddedFiles name tree:
/// <list type="bullet">
///   <item>test 2 (all profiles) — a file specification with an /EF entry must also carry /F and /UF;</item>
///   <item>test 1 (PDF/A-3 only) — the embedded file stream's /Subtype must be a valid MIME type;</item>
///   <item>test 3 (PDF/A-3 only) — the file specification must declare an /AFRelationship;</item>
///   <item>test 4 (PDF/A-3 only) — a file spec that declares /AFRelationship must be an associated file,
///     i.e. referenced from an /AF array (checked on the catalog and pages).</item>
/// </list>
/// Not implemented: for PDF/A-2, 6.8-t5 (the embedded file must itself be valid PDF/A — a recursive
/// validation the engine cannot yet perform).
/// </summary>
internal sealed class EmbeddedFileSpecRule : IConformanceRule
{
    // veraPDF 6.8-t1: /^[-\w+\.]+\/[-\w+\.]+$/ — a "type/subtype" MIME token.
    private static readonly Regex MimeType = new(@"^[-\w+.]+/[-\w+.]+$", RegexOptions.Compiled);

    private static readonly PdfName F = new("F");
    private static readonly PdfName UF = new("UF");
    private static readonly PdfName AFRelationship = new("AFRelationship");

    public string RuleId => "embedded-file";

    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA | ConformanceProfile.PdfUA1;

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        bool isPart3 = context.Target == ConformanceProfile.PdfA3b;
        bool isUa1 = context.Target == ConformanceProfile.PdfUA1;
        HashSet<int> associatedFiles = isPart3 ? CollectAssociatedFileRefs(context) : [];

        foreach (PdfDictionary spec in CollectFileSpecs(context, includeAnnotationSpecs: isUa1))
        {
            var ef = context.Resolve(spec.Get("EF")) as PdfDictionary;
            if (ef is null)
                continue; // a file spec with no /EF is not an embedded file — 6.8-t2 passes trivially

            // PDF/A 6.8-t2: an embedded-file spec must carry both /F and /UF. PDF/UA-1 7.11: the same, but
            // both keys must additionally be non-empty. Same reachable-filespec set, profile-aware predicate.
            bool fufOk = isUa1
                ? NonEmpty(context, spec, "F") && NonEmpty(context, spec, "UF")
                : spec.ContainsKey(F) && spec.ContainsKey(UF);
            if (!fufOk)
            {
                yield return Error(context, isUa1
                    ? "An embedded-file specification must contain non-empty /F and /UF keys (PDF/UA-1)."
                    : "An embedded-file specification with /EF must contain both /F and /UF keys.");
            }

            if (!isPart3)
                continue;

            // 6.8-t3 (PDF/A-3): the file spec must declare its associated-file relationship.
            if (!spec.ContainsKey(AFRelationship))
            {
                yield return Error(context,
                    "An embedded file specification must contain an /AFRelationship key (PDF/A-3).");
            }
            // 6.8-t4 (PDF/A-3): once it declares /AFRelationship it must actually be an associated file,
            // i.e. referenced from an /AF array (on the catalog or a page).
            else if (spec.IsIndirect && !associatedFiles.Contains(spec.ObjectNumber))
            {
                yield return Error(context,
                    "An embedded file declares /AFRelationship but is not referenced from any /AF "
                    + "associated-files array (PDF/A-3).");
            }

            // 6.8-t1 (PDF/A-3): the embedded file stream's /Subtype must be a valid MIME type.
            if (EmbeddedStream(context, ef) is { } stream)
            {
                string? subtype = context.ResolveName(stream.Dictionary.Get("Subtype"));
                if (subtype is null || !MimeType.IsMatch(subtype))
                {
                    yield return Error(context,
                        $"An embedded file's /Subtype '{subtype ?? "(none)"}' is not a valid MIME type.");
                }
            }
        }
    }

    /// <summary>
    /// Object numbers referenced by any /AF (associated-files) array in the document. An /AF array can
    /// hang off many object types (catalog, page, XObject, structure element, annotation…), so this scans
    /// every object rather than a fixed set of locations.
    /// </summary>
    private static HashSet<int> CollectAssociatedFileRefs(ConformanceContext context)
    {
        var refs = new HashSet<int>();
        context.Document.MaterializeAllObjects();
        foreach (PdfObject obj in context.Document.Objects.Values)
        {
            PdfDictionary? dict = obj as PdfDictionary ?? (obj as PdfStream)?.Dictionary;
            if (dict is null || context.Resolve(dict.Get("AF")) is not PdfArray af)
                continue;
            foreach (PdfObject entry in af)
                if (context.Resolve(entry) is PdfDictionary { IsIndirect: true } spec)
                    refs.Add(spec.ObjectNumber);
        }
        return refs;
    }

    private static IEnumerable<PdfDictionary> CollectFileSpecs(ConformanceContext context, bool includeAnnotationSpecs)
    {
        var seen = new HashSet<int>();

        if (context.Resolve(context.Catalog?.Dictionary.Get("Names")) is PdfDictionary names)
            foreach (PdfObject value in context.EnumerateNameTree(names.Get("EmbeddedFiles")))
                if (context.Resolve(value) is PdfDictionary spec && Fresh(spec))
                    yield return spec;

        // PDF/UA-1 7.11 also governs embedded files reached through a FileAttachment annotation's /FS, which
        // the catalog name tree does not include. Scoped to UA-1 so PDF/A 6.8 behaviour is unchanged.
        if (includeAnnotationSpecs)
            foreach (var page in context.Pages)
                if (context.Resolve(page.Dictionary.Get("Annots")) is PdfArray annots)
                    foreach (PdfObject a in annots)
                        if (context.Resolve(a) is PdfDictionary annot
                            && context.Resolve(annot.Get("FS")) is PdfDictionary spec && Fresh(spec))
                            yield return spec;

        bool Fresh(PdfDictionary spec) => !spec.IsIndirect || seen.Add(spec.ObjectNumber);
    }

    /// <summary>The embedded file stream referenced by an /EF dictionary (prefers /UF, falls back to /F).</summary>
    private static PdfStream? EmbeddedStream(ConformanceContext context, PdfDictionary ef) =>
        context.Resolve(ef.Get("UF")) as PdfStream ?? context.Resolve(ef.Get("F")) as PdfStream;

    /// <summary>True when <paramref name="key"/> resolves to a non-empty string on the file spec (PDF/UA-1 7.11).</summary>
    private static bool NonEmpty(ConformanceContext context, PdfDictionary spec, string key) =>
        context.Resolve(spec.Get(key)) is PdfString s && s.Value.Length > 0;

    private Finding Error(ConformanceContext context, string message) => new()
    {
        RuleId = RuleId,
        Severity = FindingSeverity.Error,
        // PDF/UA-1 governs the non-empty /F,/UF requirement at 7.11; the PDF/A rules live at 6.8.
        Clause = ConformanceClauses.For(context.Target,
            context.Target == ConformanceProfile.PdfUA1 ? "7.11" : "6.8"),
        Message = message,
    };
}
