using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JpegCodec.Tests.Corpus;

internal static class CorpusFiles
{
    public static string CorpusRoot
    {
        get
        {
            string baseDir = System.AppContext.BaseDirectory;
            return Path.Combine(baseDir, "Corpus");
        }
    }

    public static IEnumerable<object[]> EveryJpeg()
    {
        if (!Directory.Exists(CorpusRoot))
            yield break;
        foreach (var file in Directory.EnumerateFiles(CorpusRoot, "*.*", SearchOption.AllDirectories)
                              .Where(f => f.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase) ||
                                          f.EndsWith(".jpeg", System.StringComparison.OrdinalIgnoreCase))
                              .OrderBy(f => f, System.StringComparer.Ordinal))
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
