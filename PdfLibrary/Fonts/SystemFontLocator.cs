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

    /// <inheritdoc/>
    /// <remarks>Projects <see cref="FontMetadataIndex.Faces"/> — every face already indexed at
    /// construction — into the public DTO. No new scanning.</remarks>
    public IReadOnlyList<SystemFontFace> EnumerateFaces() =>
        _index.Faces.Select(f => new SystemFontFace(
            f.EnglishFamily.Length > 0 ? f.EnglishFamily : f.Families.FirstOrDefault() ?? "",
            f.PostScriptName,
            f.Bold, f.Italic,
            f.Path,
            f.FaceIndex)).ToList();

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

        // The explicit pair: what the document stated outright, descriptor flags merged with the
        // name's own style tokens. Deliberately excludes the StemV inference behind `bold`/`italic`.
        bool explicitBold = request.ExplicitBold || nameBold;
        bool explicitItalic = request.ExplicitItalic || nameItalic;

        // Step 1: exact PostScript name. ASCII by spec and language-free, so this is the one lookup
        // that cannot be confounded by localisation. An exact hit is the document naming a FACE, so
        // it is the incumbent — but a face whose own style bits contradict what the document stated
        // outright is the "file whose name looked right" failure this ladder exists to end, so we
        // look for a better-styled sibling. Siblings come from the hit's OWN Families and nowhere
        // else: the alias table is not consulted, because the document named this typeface and an
        // upright Arial beats some other typeface's italic. Note the family index is keyed on
        // name-table families ("Arial"), NOT PostScript names ("ArialMT") — re-splitting the request
        // string here would look up a key that cannot exist and silently do nothing.
        FontFaceRecord? hit = _index.ByPostScriptName(stripped);
        if (hit is not null && (explicitBold || explicitItalic))
            hit = BetterStyledSibling(hit, explicitBold, explicitItalic, bold, italic);

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

    /// <summary>The face among <paramref name="hit"/>'s own family that matches the requested style
    /// STRICTLY better than <paramref name="hit"/> does, or <paramref name="hit"/> itself. Strictly,
    /// not weakly: a sibling that fixes italic while breaking bold ties, and a tie is not evidence —
    /// the named face stays. Never returns null, so step 1 can only ever improve on today under the
    /// explicit metric.
    ///
    /// <para><paramref name="mergedBold"/>/<paramref name="mergedItalic"/> are the StemV-inference-
    /// inclusive pair used by steps 2 and 3. Explicit implies merged pointwise, but not the reverse —
    /// a descriptor can set StemV without an explicit bold flag — so a sibling that scores higher on
    /// the explicit pair can still score LOWER than the hit on the merged pair. Guard against that:
    /// a candidate must not regress the merged score either, or step 1 would violate its own "never
    /// return a lower-scoring face than today" contract under the metric that steps 2/3 use.</para>
    ///
    /// <para>Ties among equally-best siblings break via <see cref="FontMetadataIndex.SortsBefore"/> —
    /// literally the comparator <see cref="FontMetadataIndex.PickBest"/> uses, not merely one of the
    /// same shape. One comparator because the two can genuinely disagree: a family bucket is keyed on
    /// both name ID 1 and ID 16, so it can hold faces whose EnglishFamily order is opposite to their
    /// PostScriptName order, and a second copy of the rule would resolve those differently in step 1
    /// than in step 2. Either way the winner does not depend on <c>Directory.EnumerateFiles</c> order,
    /// which is not stable across machines and would otherwise break the Windows/Linux bit-identical
    /// render goal.</para></summary>
    private FontFaceRecord BetterStyledSibling(
        FontFaceRecord hit, bool bold, bool italic, bool mergedBold, bool mergedItalic)
    {
        int hitScore = FontMetadataIndex.StyleScore(hit, bold, italic);
        if (hitScore == 2) return hit;

        int hitMergedScore = FontMetadataIndex.StyleScore(hit, mergedBold, mergedItalic);

        FontFaceRecord? best = null;
        var bestScore = hitScore;
        foreach (string family in hit.Families)
            foreach (FontFaceRecord f in _index.ByFamily(family))
            {
                int score = FontMetadataIndex.StyleScore(f, bold, italic);
                if (score < bestScore) continue;
                if (FontMetadataIndex.StyleScore(f, mergedBold, mergedItalic) < hitMergedScore) continue;

                if (score > bestScore)
                {
                    best = f;
                    bestScore = score;
                    continue;
                }
                // score == bestScore here. If best is still null, bestScore == hitScore, i.e. this
                // candidate only ties the hit rather than beating it — not an improvement, skip. Once
                // best is non-null, tie-break deterministically instead of keeping first-seen, which
                // would otherwise depend on Directory.EnumerateFiles order (unstable across machines).
                if (best is null) continue;
                if (FontMetadataIndex.SortsBefore(f, best)) best = f;
            }
        return best ?? hit;
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
