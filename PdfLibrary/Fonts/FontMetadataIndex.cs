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
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (string file in files)
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

    /// <summary>Best style match: +1 for italic agreement, +1 for bold agreement. Scored rather than
    /// matched exactly so a family lacking the requested combination degrades to its nearest face
    /// instead of failing. Ties keep the LOWEST face index, so a set of indistinguishable faces
    /// resolves exactly as it did before this index existed.</summary>
    public static FontFaceRecord? PickBest(IEnumerable<FontFaceRecord> candidates, bool bold, bool italic)
    {
        FontFaceRecord? best = null;
        var bestScore = -1;
        foreach (FontFaceRecord f in candidates)
        {
            int score = (f.Italic == italic ? 1 : 0) + (f.Bold == bold ? 1 : 0);
            if (best is not null && (score < bestScore || (score == bestScore && f.FaceIndex >= best.FaceIndex)))
                continue;
            best = f;
            bestScore = score;
        }
        return best;
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
