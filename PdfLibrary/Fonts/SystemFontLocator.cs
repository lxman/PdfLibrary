namespace PdfLibrary.Fonts;

/// <summary>
/// The default, SkiaSharp-free <see cref="ISystemFontProvider"/>: locates metric-compatible
/// substitutes for standard-14 fonts among the fonts installed on the system and returns their
/// raw bytes. Reading installed fonts is not redistribution.
/// </summary>
public sealed partial class SystemFontLocator : ISystemFontProvider
{
    private readonly FontMetadataIndex _index;

    // Building the index recursively scans every OS font directory, so the default locator is a
    // process-wide shared singleton: the scan happens once per process, not once per PdfRenderer.
    // (Type3 fonts construct a sub-renderer per glyph, so a fresh scan per construction was ~86% of
    // page-record time.) The index is read-only after construction, so sharing it is thread-safe.
    private static readonly Lazy<SystemFontLocator> LazyDefault =
        new(static () => new SystemFontLocator(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The shared, lazily-built default locator over the system font directories. Reused
    /// everywhere a caller does not inject its own <see cref="ISystemFontProvider"/>.</summary>
    public static SystemFontLocator Default => LazyDefault.Value;

    /// <summary>Create a locator that scans the given directories (used for testing).</summary>
    public SystemFontLocator(IEnumerable<string> directories)
    {
        _index = new FontMetadataIndex(directories as string[] ?? directories.ToArray());
    }

    /// <inheritdoc/>
    public byte[]? GetFontData(string baseFontName)
    {
        foreach (string candidate in Standard14Fonts.SubstituteFileBaseNames(baseFontName))
        {
            string? path = _index.FindPath(candidate);
            if (path is null) continue;
            try { return File.ReadAllBytes(path); }
            catch { /* path exists but is unreadable — try the next candidate */ }
        }
        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns installed font file base-names (e.g. "LiberationSans-Regular"),
    /// not family display-names.
    /// </remarks>
    public IReadOnlyCollection<string> GetAvailableFontFamilies() => _index.FileBaseNames;

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="familyName"/> must be a font file base-name
    /// (e.g. "LiberationSans-Regular"), not a family display-name.
    /// </remarks>
    public bool IsFontAvailable(string familyName) => _index.FindPath(familyName) is not null;

    /// <inheritdoc/>
    public string? FindFirstAvailable(IEnumerable<string> candidates)
    {
        foreach (string c in candidates)
            if (IsFontAvailable(c)) return c;
        return null;
    }

    /// <inheritdoc/>
    public void RefreshCache() { /* Index is built at construction; create a new locator to refresh. */ }

    /// <summary>The metadata ladder. Step 1 PostScript name, step 2 aliased family, step 3 the
    /// synthetic standard-14 name — each matched against the font's OWN metadata rather than against
    /// a filename. Returns null when all three miss; slice 1 adds no fallback floor.</summary>
    public FontMatch? Resolve(FontRequest request)
    {
        (string family, bool nameBold, bool nameItalic) = Base35Aliases.Split(request.BaseFont);
        bool bold = request.Bold || nameBold;
        bool italic = request.Italic || nameItalic;

        string stripped = request.BaseFont.Length > 7 && request.BaseFont[6] == '+'
            ? request.BaseFont[7..]
            : request.BaseFont;

        // Step 1: exact PostScript name. ASCII by spec and language-free, so this is the one lookup
        // that cannot be confounded by localisation.
        FontFaceRecord? hit = _index.ByPostScriptName(stripped);

        // Step 2: aliased family, best style match.
        if (hit is null)
            hit = FirstFamilyHit(Base35Aliases.FamiliesFor(family), bold, italic);

        // Step 3: the synthetic standard-14 name, by PostScript name then by aliased family. This is
        // what keeps a machine with no base-35 clones on its own core serif/sans/mono.
        if (hit is null)
        {
            // Descriptor flags and name spelling are independent signals and either one alone decides
            // the family: a subset name is opaque while /Flags says Serif, and a descriptor can carry
            // no flags at all while the name says "Times". Hence the OR — dropping either side sends
            // the font to Helvetica.
            (bool nameSerif, bool nameMono, bool _, bool _) =
                SubstituteFontResolver.Classify(request.BaseFont, null);
            string synthetic = SubstituteFontResolver.SyntheticStd14Name(
                request.Serif || nameSerif, request.Mono || nameMono, bold, italic);
            (string synthFamily, bool _, bool _) = Base35Aliases.Split(synthetic);
            hit = _index.ByPostScriptName(synthetic)
               ?? FirstFamilyHit(Base35Aliases.FamiliesFor(synthFamily), bold, italic);
        }

        if (hit is null) return null;
        try { return new FontMatch(File.ReadAllBytes(hit.Path), hit.FaceIndex); }
        catch { return null; }
    }

    private FontFaceRecord? FirstFamilyHit(IReadOnlyList<string> families, bool bold, bool italic)
    {
        foreach (string family in families)
        {
            IReadOnlyList<FontFaceRecord> candidates = _index.ByFamily(family);
            if (candidates.Count == 0) continue;
            FontFaceRecord? best = FontMetadataIndex.PickBest(candidates, bold, italic);
            if (best is not null) return best;
        }
        return null;
    }
}
