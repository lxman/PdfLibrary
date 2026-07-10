namespace PdfLibrary.Conformance;

/// <summary>
/// The page-content marked-content facts the PDF/UA-1 rules need, gathered in one walk of every page
/// (and the Form XObjects they invoke) by <see cref="Content.MarkedContentCollector"/> and cached on the
/// <see cref="ConformanceContext"/>. See ISO 14289-1, 7.1 (real content must be tagged or an artifact) and
/// 7.3 (a figure's text alternative may come from a content-stream <c>/ActualText</c>).
/// </summary>
internal sealed record MarkedContentAnalysis(
    bool HasUntaggedContent,
    int UntaggedPageIndex,          // zero-based page of the first untagged content, or -1 if none
    bool HasArtifactNesting,
    int NestingPageIndex,           // zero-based page of the first artifact/tagged nesting, or -1 if none
    IReadOnlySet<int> ActualTextMcids)
{
    /// <summary>An analysis of a document with no page content (or none that could be walked).</summary>
    public static readonly MarkedContentAnalysis Empty =
        new(false, -1, false, -1, new HashSet<int>());
}
