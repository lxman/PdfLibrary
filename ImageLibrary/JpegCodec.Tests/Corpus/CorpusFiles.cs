namespace JpegCodec.Tests.Corpus;

internal static class CorpusFiles
{
    public static string CorpusRoot
    {
        get
        {
            string baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "Corpus");
        }
    }

    public static IEnumerable<object[]> EveryJpeg()
    {
        if (!Directory.Exists(CorpusRoot))
            yield break;
        foreach (var file in Directory.EnumerateFiles(CorpusRoot, "*.*", SearchOption.AllDirectories)
                              .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                          f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                              .OrderBy(f => f, StringComparer.Ordinal))
        {
            // xUnit MemberData wants object[]. Pass the path relative to
            // the Corpus root so test display names stay short.
            string relative = Path.GetRelativePath(CorpusRoot, file);
            yield return [relative];
        }
    }

    public static byte[] Load(string relativePath)
    {
        return File.ReadAllBytes(Path.Combine(CorpusRoot, relativePath));
    }
}
