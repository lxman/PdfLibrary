using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>Test-only helper for exercising a real TrueType Collection (<c>*.ttc</c>) installed on
/// the host machine. Windows ships several (e.g. <c>Cambria.ttc</c>); macOS core families
/// (<c>Times.ttc</c> etc.) live in collections too. Returns null so a machine that genuinely has
/// none can skip honestly rather than fail.</summary>
internal static class SystemFontLocatorTestHelpers
{
    /// <summary>Scans the platform's default font directories for the first <c>*.ttc</c> file and
    /// resolves a non-trivial face within it (the last face when there is more than one, so the
    /// extraction path is actually exercised against a non-zero <see cref="FontMatch.FaceIndex"/>
    /// rather than merely tolerating one).</summary>
    public static FontMatch? FirstCollectionFace()
    {
        foreach (string dir in SystemFontLocator.DefaultFontDirectories())
        {
            if (!Directory.Exists(dir)) continue;

            string[] ttcFiles;
            try { ttcFiles = Directory.GetFiles(dir, "*.ttc", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (string path in ttcFiles)
            {
                byte[] data;
                try { data = File.ReadAllBytes(path); }
                catch { continue; }

                int faceCount = SfntNameReader.FaceCount(data);
                if (faceCount <= 0) continue;

                int faceIndex = faceCount > 1 ? faceCount - 1 : 0;
                return new FontMatch(data, faceIndex);
            }
        }

        return null;
    }
}
