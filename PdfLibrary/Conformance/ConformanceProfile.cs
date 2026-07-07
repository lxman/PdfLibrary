namespace PdfLibrary.Conformance;

/// <summary>
/// Identifies a PDF conformance standard and level. Values are flags so that a single
/// <see cref="IConformanceRule"/> can declare that it applies to several profiles at once
/// (see <see cref="IConformanceRule.AppliesToProfiles"/>). A preflight run targets exactly one
/// profile — a single-bit value — passed to <see cref="Preflighter.Check(Structure.PdfDocument, ConformanceProfile)"/>.
/// </summary>
[Flags]
public enum ConformanceProfile
{
    None = 0,

    /// <summary>PDF/A-2b — ISO 19005-2:2011, level B (visual reproduction).</summary>
    PdfA2b = 1 << 0,

    /// <summary>PDF/A-2u — ISO 19005-2:2011, level B plus Unicode mapping of all text.</summary>
    PdfA2u = 1 << 1,

    /// <summary>PDF/A-3b — ISO 19005-3:2012, level B (permits embedded file attachments).</summary>
    PdfA3b = 1 << 2,

    /// <summary>PDF/X-4 — ISO 15930-7:2010 (print production).</summary>
    PdfX4 = 1 << 3,

    /// <summary>All supported PDF/A profiles.</summary>
    AllPdfA = PdfA2b | PdfA2u | PdfA3b,

    /// <summary>Every supported profile.</summary>
    All = AllPdfA | PdfX4,
}
