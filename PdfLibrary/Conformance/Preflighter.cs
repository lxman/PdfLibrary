using PdfLibrary.Structure;

namespace PdfLibrary.Conformance;

/// <summary>
/// Runs the registered conformance rules against a document for a target profile and collects the
/// findings. This is the entry point for the read-only PDF/A + PDF/X preflight.
/// </summary>
public static class Preflighter
{
    /// <summary>
    /// The rule registry, in execution order. New rules are appended here as they are implemented;
    /// each rule self-selects the profiles it applies to via <see cref="IConformanceRule.AppliesToProfiles"/>.
    /// </summary>
    private static readonly IReadOnlyList<IConformanceRule> Rules =
    [
        new Rules.NoEncryptionRule(),
        new Rules.FileIdentifierRule(),
        new Rules.PostEofDataRule(),
        new Rules.StreamFiltersRule(),
        new Rules.StreamExternalFileRule(),
        new Rules.MetadataPresentRule(),
        new Rules.PdfaIdentificationRule(),
        new Rules.OutputIntentProfileRule(),
        new Rules.OutputIntentSingleProfileRule(),
        new Rules.FontEmbeddingRule(),
    ];

    /// <summary>
    /// Checks a loaded document against a single conformance profile.
    /// </summary>
    /// <param name="document">The document to inspect. It is read, never modified.</param>
    /// <param name="profile">A single profile to target (one flag value).</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="profile"/> is not a single supported profile.</exception>
    public static PreflightResult Check(PdfDocument document, ConformanceProfile profile) =>
        Run(document, profile, sourceBytes: null);

    /// <summary>
    /// Loads a document from raw PDF bytes and checks it. Byte-level rules (e.g. post-EOF data) can run
    /// because the source bytes are retained.
    /// </summary>
    public static PreflightResult Check(byte[] pdfBytes, ConformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        using PdfDocument document = PdfDocument.Load(new MemoryStream(pdfBytes, writable: false));
        return Run(document, profile, pdfBytes);
    }

    /// <summary>
    /// Loads a document from a file and checks it against a single conformance profile. The file bytes
    /// are retained so byte-level rules can run.
    /// </summary>
    /// <remarks>
    /// <see cref="PdfDocument.Load(System.IO.Stream, string, bool)"/> throws for a file it cannot read —
    /// including a PDF protected by a user password other than <paramref name="password"/> (itself an
    /// encryption violation). Callers that need to report such files instead of getting an exception
    /// should load the document themselves and call <see cref="Check(PdfDocument, ConformanceProfile)"/>.
    /// </remarks>
    public static PreflightResult Check(string filePath, ConformanceProfile profile, string? password = null)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        using PdfDocument document = PdfDocument.Load(new MemoryStream(bytes, writable: false), password ?? string.Empty);
        return Run(document, profile, bytes);
    }

    private static PreflightResult Run(PdfDocument document, ConformanceProfile profile, byte[]? sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsSingleProfile(profile))
        {
            throw new ArgumentException(
                $"A preflight run targets exactly one profile; got '{profile}'.", nameof(profile));
        }

        var context = new ConformanceContext(document, profile, sourceBytes);
        var findings = new List<Finding>();

        foreach (IConformanceRule rule in Rules)
        {
            if ((rule.AppliesToProfiles & profile) == 0)
                continue;

            // Isolate each rule: a rule that throws on a malformed document degrades to a single
            // warning rather than aborting the whole run and discarding every other rule's findings.
            try
            {
                findings.AddRange(rule.Check(context));
            }
            catch (Exception ex)
            {
                findings.Add(new Finding
                {
                    RuleId = rule.RuleId,
                    Severity = FindingSeverity.Warning,
                    Clause = "—",
                    Message = $"Rule '{rule.RuleId}' could not be evaluated and was skipped "
                              + $"({ex.GetType().Name}: {ex.Message}).",
                });
            }
        }

        return new PreflightResult { Profile = profile, Findings = findings };
    }

    /// <summary>True when exactly one defined profile bit is set (composite values are rejected).</summary>
    private static bool IsSingleProfile(ConformanceProfile profile)
    {
        var bits = (int)profile;
        return bits != 0
               && (bits & (bits - 1)) == 0                       // exactly one bit set
               && (ConformanceProfile.All & profile) == profile; // and it is a defined profile
    }
}
