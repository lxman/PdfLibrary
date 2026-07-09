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
        // PDF/A-2u Unicode delta (ISO 19005-2 6.2.11.7.2).
        new Rules.Pdfa2uToUnicodeRule(),
        new Rules.Pdfa2uToUnicodeValuesRule(),
        new Rules.OutputIntentProfileRule(),
        new Rules.OutputIntentSingleProfileRule(),
        new Rules.FontEmbeddingRule(),
        new Rules.DeviceColourRule(),
        // Slice 7 — annotations, interactive forms, actions.
        new Rules.AnnotationTypeRule(),
        new Rules.AnnotationFlagsRule(),
        new Rules.AnnotationAppearanceRule(),
        new Rules.FormFieldActionsRule(),
        new Rules.FormConfigRule(),
        new Rules.ActionTypeRule(),
        new Rules.AdditionalActionsRule(),
        // Slice 8 — embedded files, optional content, alternate presentations, document requirements.
        new Rules.EmbeddedFileSpecRule(),
        new Rules.OptionalContentRule(),
        new Rules.AlternatePresentationsRule(),
        new Rules.DocumentRequirementsRule(),
        // Slice 9 — PDF/X-4 structural core (ISO 15930-7).
        new Rules.PdfxOutputIntentRule(),
        new Rules.PageBoxesRule(),
        new Rules.TrappedRule(),
        // Slice 10 — PDF/X-4 version identification + colour governance (ISO 15930-7).
        new Rules.PdfxVersionRule(),
        new Rules.PdfxColourRule(),
        // Slice 11 — PDF/X-4 transparency + spot/DeviceN colour depth (ISO 15930-7).
        new Rules.PdfxTransparencyColourRule(),
        new Rules.PdfxBlendModeRule(),
        new Rules.PdfxNChannelColorantsRule(),
        new Rules.PdfxSeparationConsistencyRule(),
        // Slice 13 — PDF/UA-1 accessibility (ISO 14289-1). Phase 1: identification + catalog/metadata.
        new Rules.UaIdentificationRule(),
        new Rules.UaTaggedRule(),
        new Rules.UaDisplayDocTitleRule(),
        new Rules.UaTitleRule(),
        new Rules.UaXfaRule(),
        // Phase 2: reuse — text-to-Unicode (7.2); font embedding (7.21) is FontEmbeddingRule widened to UA.
        new Rules.UaTextUnicodeRule(),
        // Phase 3: structure-tree rules.
        new Rules.UaFigureAltRule(),
        // Phase 4: marked-content completeness (7.1) — real content tagged/artifact + artifact nesting.
        new Rules.UaContentTaggedRule(),
        new Rules.UaArtifactNestingRule(),
        // Phase 3b: structure-tree relationship semantics (ISO 32000-1 14.8.4).
        new Rules.UaStandardTypeRule(),
        new Rules.UaStructureNestingRule(),
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
        return LoadAndRun(pdfBytes, profile, password: null);
    }

    /// <summary>
    /// Loads a document from a file and checks it against a single conformance profile. The file bytes
    /// are retained so byte-level rules can run. A file the loader cannot read — including one protected by
    /// a user password other than <paramref name="password"/> — is reported as a non-conformant document
    /// (a <c>document-load</c> Error finding) rather than throwing out of the preflight.
    /// </summary>
    public static PreflightResult Check(string filePath, ConformanceProfile profile, string? password = null)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        return LoadAndRun(bytes, profile, password);
    }

    private static PreflightResult LoadAndRun(byte[] bytes, ConformanceProfile profile, string? password)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Load(new MemoryStream(bytes, writable: false), password ?? string.Empty);
        }
        catch (Exception ex)
        {
            // An unreadable file (encrypted with a user password, or structurally broken) does not conform;
            // surface it as a finding instead of letting the load exception escape the preflight.
            return new PreflightResult
            {
                Profile = profile,
                Findings =
                [
                    new Finding
                    {
                        RuleId = "document-load",
                        Severity = FindingSeverity.Error,
                        Clause = ConformanceClauses.For(profile, "6.1"),
                        Message = $"The document could not be loaded for preflight "
                                  + $"({ex.GetType().Name}: {ex.Message}); an encrypted or structurally invalid "
                                  + "file does not conform.",
                    },
                ],
            };
        }

        using (document)
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
               && (bits & (bits - 1)) == 0                             // exactly one bit set
               && (ConformanceProfile.AnyProfile & profile) == profile; // and it is a defined profile
    }
}
