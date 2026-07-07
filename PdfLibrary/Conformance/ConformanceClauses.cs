namespace PdfLibrary.Conformance;

/// <summary>
/// Maps a target profile to the governing standard/clause references used in
/// <see cref="Finding.Clause"/>. Clause numbers are provisional and will be reconciled against the
/// veraPDF (PDF/A) and Ghent Workgroup (PDF/X) corpora in a later slice — see
/// <c>Docs/plans/2026-07-06-conformance-preflight-read-api-audit.md</c>.
/// </summary>
internal static class ConformanceClauses
{
    /// <summary>
    /// The "file structure" clause that governs trailer-level requirements such as the prohibition
    /// on <c>/Encrypt</c> and the mandatory <c>/ID</c> file identifier.
    /// </summary>
    public static string FileStructure(ConformanceProfile profile) => profile switch
    {
        ConformanceProfile.PdfA2b or ConformanceProfile.PdfA2u => "ISO 19005-2:2011, 6.1.3",
        ConformanceProfile.PdfA3b => "ISO 19005-3:2012, 6.1.3",
        ConformanceProfile.PdfX4 => "ISO 15930-7:2010, 6.2",
        _ => "—",
    };
}
