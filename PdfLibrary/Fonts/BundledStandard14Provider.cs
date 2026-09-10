namespace PdfLibrary.Fonts;

/// <summary>
/// Answers standard-14 substitution requests from a bundled font set, ahead of the system font
/// ladder. With the default staged locator, an unknown requested face gets its exact/family chance
/// first and bundled policy then runs before the locator's synthetic system-font floor. Non-staged
/// providers receive non-alias requests unchanged.
///
/// <para><b>Why this exists.</b> Liberation is installed on both Windows and Linux, but
/// <see cref="Base35Aliases"/> ranks <c>Nimbus Sans</c> ahead of <c>Liberation Sans</c> for
/// <c>helvetica</c>, so Linux resolves a CFF-flavoured face where Windows resolves TrueType. The
/// defect is PRECEDENCE, not availability — which is why installing fonts cannot fix it and being
/// consulted first can.</para>
///
/// <para><b>This class holds no font bytes.</b> <paramref name="bytesForFace"/> supplies them, so
/// the engine ships none: the app reads them from its own resources, the test suite from test-only
/// assets. A host that supplies nothing degrades to exactly the previous behaviour.</para>
/// </summary>
public sealed class BundledStandard14Provider(
    Func<string, byte[]?> bytesForFace,
    ISystemFontProvider inner) : ISystemFontProvider
{
    /// <summary>The alias keys a bundled face may answer for, and the Liberation family it maps to.
    /// Deliberately NOT the whole base-35 set: <c>symbol</c> and <c>zapfdingbats</c> are excluded
    /// because Liberation has no such face and a Latin stand-in for symbol-encoded content is
    /// confident garbage; Palatino, Bookman, Avant Garde, New Century Schoolbook and Zapf Chancery
    /// are excluded because Liberation has no equivalent at all.</summary>
    private static readonly Dictionary<string, string> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["helvetica"] = "LiberationSans",
        ["arial"] = "LiberationSans",
        ["times"] = "LiberationSerif",
        ["timesroman"] = "LiberationSerif",
        ["timesnewroman"] = "LiberationSerif",
        ["courier"] = "LiberationMono",
        ["couriernew"] = "LiberationMono",
    };

    public FontMatch? Resolve(FontRequest request)
    {
        if (TryResolveBundled(request, out FontMatch? directBundled))
            return directBundled ?? inner.Resolve(request);

        // The default locator's public Resolve hides whether its answer came from the document's
        // explicit PostScript/family request or from its internally synthesized standard-14 floor.
        // Insert bundled policy at that exact boundary: preserve a real requested face when one
        // exists, but let the host's bundled Liberation face win before Nimbus/system fallback.
        // Non-staged third-party providers retain the original single delegation unchanged.
        if (inner is IStagedSystemFontProvider staged)
        {
            FontMatch? explicitMatch = staged.ResolveBeforeSyntheticFallback(request);
            if (explicitMatch is not null)
                return explicitMatch;

            (string family, _, _) = Base35Aliases.Split(request.BaseFont ?? "");
            if (!Base35Aliases.IsKnownFamily(family))
            {
                (bool nameSerif, bool nameMono, bool nameBold, bool nameItalic) =
                    SubstituteFontResolver.Classify(request.BaseFont ?? "", descriptor: null);
                string synthetic = SubstituteFontResolver.SyntheticStd14Name(
                    request.Serif || nameSerif,
                    request.Mono || nameMono,
                    request.Bold || nameBold,
                    request.Italic || nameItalic);

                if (TryResolveBundled(request with { BaseFont = synthetic }, out FontMatch? syntheticBundled)
                    && syntheticBundled is not null)
                    return syntheticBundled;
            }
        }

        return inner.Resolve(request);
    }

    /// <summary>Returns true when the request is within this provider's supported alias policy;
    /// <paramref name="match"/> is null when it is supported but the host supplied no bytes.</summary>
    private bool TryResolveBundled(FontRequest request, out FontMatch? match)
    {
        (string family, bool nameBold, bool nameItalic) = Base35Aliases.Split(request.BaseFont ?? "");
        string key = family.Replace(" ", string.Empty);

        if (!Families.TryGetValue(key, out string? liberationFamily))
        {
            match = null;
            return false;
        }

        bool bold = request.Bold || nameBold;
        bool italic = request.Italic || nameItalic;
        string style = (bold, italic) switch
        {
            (true, true) => "BoldItalic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "Regular",
        };

        byte[]? data = bytesForFace($"{liberationFamily}-{style}");
        match = data is null ? null : new FontMatch(data, 0);
        return true;
    }

    public IReadOnlyCollection<string> GetAvailableFontFamilies() => inner.GetAvailableFontFamilies();
    public bool IsFontAvailable(string familyName) => inner.IsFontAvailable(familyName);
    public string? FindFirstAvailable(IEnumerable<string> candidates) => inner.FindFirstAvailable(candidates);
    public void RefreshCache() => inner.RefreshCache();
    public IReadOnlyList<SystemFontFace> EnumerateFaces() => inner.EnumerateFaces();
    public byte[]? GetFontData(string baseFontName) => Resolve(
        new FontRequest(baseFontName, false, false, false, false, false, false))?.Data;
}
