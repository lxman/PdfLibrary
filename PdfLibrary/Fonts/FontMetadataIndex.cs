namespace PdfLibrary.Fonts;

/// <summary>Every face of every font file found in the given directories, indexed by PostScript name
/// and by every localized family name.
///
/// <para>Supersedes the filename-only lookup that preceded it: on the Windows dev box 755 faces are
/// installed and only the ~40 hardcoded candidate filenames were ever reachable. Building this costs
/// ~42 ms serially for 732 files (measured 2026-08-04) because only the sfnt header, table directory,
/// `name` and `head` are read via <see cref="SfntNameReader.FaceCount(Stream)"/> /
/// <see cref="SfntNameReader.ReadFace(Stream, int, string)"/> — reading the files whole measured
/// 591 ms and destabilised an unrelated, timing-sensitive file-lock test running in parallel.</para>
///
/// <para>Constructed once per process via <see cref="SystemFontLocator.Default"/>. Rebuilding it per
/// renderer would repeat the mistake that once made directory scanning 86% of page-record time, since
/// Type3 fonts construct a sub-renderer per glyph.</para></summary>
internal sealed class FontMetadataIndex
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];

    private readonly List<FontFaceRecord> _faces = [];
    private readonly Dictionary<string, FontFaceRecord> _byPostScript = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<FontFaceRecord>> _byFamily = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byFileBaseName = new(StringComparer.OrdinalIgnoreCase);

    public FontMetadataIndex(IEnumerable<string> directories)
    {
        foreach (string dir in directories)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            // The foreach MUST stay inside the try: EnumerateFiles is lazy, so the call itself throws
            // nothing and every IO/permission fault of the recursive walk surfaces while iterating.
            // A fault escaping here is not merely one lost directory — the constructor runs inside a
            // Lazy<SystemFontLocator>(ExecutionAndPublication), which caches the exception and
            // rethrows it forever, so one transient fault would kill substitution process-wide.
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    if (Array.IndexOf(Extensions, Path.GetExtension(file).ToLowerInvariant()) < 0) continue;
                    // First writer wins, so earlier directories take precedence - as before.
                    _byFileBaseName.TryAdd(Path.GetFileNameWithoutExtension(file), file);

                    List<FontFaceRecord> faces = [];
                    try
                    {
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                        int faceCount = SfntNameReader.FaceCount(stream);
                        for (var i = 0; i < faceCount; i++)
                        {
                            FontFaceRecord? face = SfntNameReader.ReadFace(stream, i, file);
                            if (face is not null) faces.Add(face);
                        }
                    }
                    catch { continue; }

                    foreach (FontFaceRecord face in faces)
                    {
                        _faces.Add(face);

                        if (face.PostScriptName.Length > 0)
                            _byPostScript.TryAdd(face.PostScriptName, face);

                        foreach (string family in face.Families)
                        {
                            string key = Normalize(family);
                            if (!_byFamily.TryGetValue(key, out List<FontFaceRecord>? list))
                                _byFamily[key] = list = [];
                            list.Add(face);
                        }
                    }
                }
            }
            catch
            {
                // Permission or IO error during recursive traversal — skip the rest of this
                // directory, keep everything indexed so far, and go on to the next directory.
                continue;
            }
        }
    }

    public IReadOnlyList<FontFaceRecord> Faces => _faces;

    public FontFaceRecord? ByPostScriptName(string name) =>
        _byPostScript.TryGetValue(name, out FontFaceRecord? f) ? f : null;

    public IReadOnlyList<FontFaceRecord> ByFamily(string family) =>
        _byFamily.TryGetValue(Normalize(family), out List<FontFaceRecord>? list) ? list : [];

    /// <summary>Full path of the indexed font whose FILE base name matches. Retained because
    /// <see cref="ISystemFontProvider.IsFontAvailable"/> and
    /// <see cref="ISystemFontProvider.FindFirstAvailable"/> document their parameter as a file base
    /// name, and that contract must not change.</summary>
    public string? FindPath(string fileBaseName) =>
        _byFileBaseName.TryGetValue(fileBaseName, out string? p) ? p : null;

    public IReadOnlyCollection<string> FileBaseNames => _byFileBaseName.Keys;

    /// <summary>+1 for italic agreement, +1 for bold agreement. Scored rather than matched exactly so
    /// a family lacking the requested combination degrades to its nearest face instead of failing.</summary>
    internal static int StyleScore(FontFaceRecord f, bool bold, bool italic) =>
        (f.Italic == italic ? 1 : 0) + (f.Bold == bold ? 1 : 0);

    /// <summary>Best style match among <paramref name="candidates"/>.
    ///
    /// <para>Ties break via <see cref="SortsBefore"/>. The face index alone is not enough: it only
    /// discriminates WITHIN one file, and the family lookup that feeds this method draws candidates
    /// from different files that all have face index 0 — so the effective rule was
    /// Directory.EnumerateFiles order, which is not stable across machines.</para></summary>
    public static FontFaceRecord? PickBest(IEnumerable<FontFaceRecord> candidates, bool bold, bool italic)
    {
        FontFaceRecord? best = null;
        var bestScore = -1;
        foreach (FontFaceRecord f in candidates)
        {
            int score = StyleScore(f, bold, italic);
            if (best is not null && (score < bestScore || (score == bestScore && !SortsBefore(f, best))))
                continue;
            best = f;
            bestScore = score;
        }
        return best;
    }

    /// <summary>Deterministic ordering for equally-good candidates: (EnglishFamily, PostScriptName,
    /// FaceIndex, Path), every leg ordinal and never culture-aware, so the choice cannot vary with
    /// the host locale.
    ///
    /// <para>The single tie-break for the WHOLE ladder — step 1's sibling search calls this too, so
    /// two equally-scoring faces resolve identically whichever step meets them. They can genuinely
    /// differ on the leading key: <c>_byFamily</c> is keyed on both name ID 1 and ID 16, so one
    /// bucket can hold faces with different <c>EnglishFamily</c> values ("Foo" and "Foo Light" both
    /// under typographic family "Foo").</para>
    ///
    /// <para>The <c>Path</c> leg is the floor. The index walks system AND per-user font directories
    /// recursively and <c>_byFamily</c> — unlike <c>_byPostScript</c> — does not de-duplicate, so two
    /// installs of one face are both candidates and agree on every other leg. Without this the
    /// winner would be enumeration order, which is what the whole comparator exists to escape.</para></summary>
    internal static bool SortsBefore(FontFaceRecord a, FontFaceRecord b)
    {
        int byFamily = string.CompareOrdinal(a.EnglishFamily, b.EnglishFamily);
        if (byFamily != 0) return byFamily < 0;
        int byName = string.CompareOrdinal(a.PostScriptName, b.PostScriptName);
        if (byName != 0) return byName < 0;
        if (a.FaceIndex != b.FaceIndex) return a.FaceIndex < b.FaceIndex;
        return string.CompareOrdinal(a.Path, b.Path) < 0;
    }

    /// <summary>Best face index WITHIN one font file's bytes. Exists for
    /// <see cref="ISystemFontProvider.Resolve"/>'s default implementation, which has bytes from a
    /// third-party provider and no index: without this, any provider that implements only
    /// <c>GetFontData</c> would silently return face 0 of a collection and lose the requested style —
    /// the exact defect fixed in 6afbe7a. Returns 0 for a bare sfnt or unreadable bytes.</summary>
    public static int PickFaceIndex(byte[] data, bool bold, bool italic)
    {
        int count = SfntNameReader.FaceCount(data);
        if (count <= 1) return 0;

        var faces = new List<FontFaceRecord>(count);
        for (var i = 0; i < count; i++)
        {
            FontFaceRecord? face = SfntNameReader.ReadFace(data, i, string.Empty);
            if (face is not null) faces.Add(face);
        }
        return PickBest(faces, bold, italic)?.FaceIndex ?? 0;
    }

    private static string Normalize(string s) => s.Replace(" ", string.Empty);
}
