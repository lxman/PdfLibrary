namespace PdfLibrary.Conformance;

/// <summary>
/// Maps a target profile + PDF/A clause number to the standard/clause reference used in
/// <see cref="Finding.Clause"/>. Clause numbers are the ISO 19005 (PDF/A) values and are cross-checked
/// against the veraPDF profiles; for PDF/X-4 only the standard is cited (its subclauses are mapped in
/// the dedicated X-4 slice). See <c>Docs/plans/2026-07-06-conformance-rule-catalog.md</c>.
/// </summary>
internal static class ConformanceClauses
{
    /// <summary>Reference for a rule at the given PDF/A clause (e.g. <c>"6.1.7.2"</c>).</summary>
    public static string For(ConformanceProfile profile, string pdfaClause) => profile switch
    {
        ConformanceProfile.PdfA2b or ConformanceProfile.PdfA2u => $"ISO 19005-2:2011, {pdfaClause}",
        ConformanceProfile.PdfA3b => $"ISO 19005-3:2012, {pdfaClause}",
        ConformanceProfile.PdfX4 => "ISO 15930-7:2010",
        ConformanceProfile.PdfUA1 => $"ISO 14289-1:2014, {pdfaClause}",
        _ => pdfaClause,
    };

    /// <summary>The trailer / file-structure clause (6.1.3) governing /Encrypt and /ID.</summary>
    public static string FileStructure(ConformanceProfile profile) => For(profile, "6.1.3");
}
