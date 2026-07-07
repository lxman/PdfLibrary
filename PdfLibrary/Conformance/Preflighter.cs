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
    ];

    /// <summary>
    /// Checks a loaded document against a single conformance profile.
    /// </summary>
    /// <param name="document">The document to inspect. It is read, never modified.</param>
    /// <param name="profile">A single profile to target (one flag value).</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="profile"/> is not a single supported profile.</exception>
    public static PreflightResult Check(PdfDocument document, ConformanceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!IsSingleProfile(profile))
        {
            throw new ArgumentException(
                $"A preflight run targets exactly one profile; got '{profile}'.", nameof(profile));
        }

        var context = new ConformanceContext(document, profile);
        var findings = new List<Finding>();

        foreach (IConformanceRule rule in Rules)
        {
            if ((rule.AppliesToProfiles & profile) == 0)
                continue;

            findings.AddRange(rule.Check(context));
        }

        return new PreflightResult { Profile = profile, Findings = findings };
    }

    /// <summary>
    /// Loads a document from a file and checks it against a single conformance profile.
    /// </summary>
    public static PreflightResult Check(string filePath, ConformanceProfile profile)
    {
        using PdfDocument document = PdfDocument.Load(filePath);
        return Check(document, profile);
    }

    /// <summary>True when exactly one supported profile bit is set.</summary>
    private static bool IsSingleProfile(ConformanceProfile profile) =>
        profile is ConformanceProfile.PdfA2b
            or ConformanceProfile.PdfA2u
            or ConformanceProfile.PdfA3b
            or ConformanceProfile.PdfX4;
}
