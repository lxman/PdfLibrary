using System;
using System.IO;

namespace ImageLibrary.Benchmarks;

internal static class BenchmarkAssets
{
    private static readonly string TestImagesRoot = LocateTestImagesRoot();

    public static byte[] Load(string relativePath)
    {
        string full = Path.Combine(TestImagesRoot, relativePath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Benchmark asset not found: {full}");
        return File.ReadAllBytes(full);
    }

    private static string LocateTestImagesRoot()
    {
        // Walk up from AppContext.BaseDirectory until we find ImageLibrary\TestImages.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "ImageLibrary", "TestImages");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate ImageLibrary\\TestImages by walking up from " + AppContext.BaseDirectory);
    }
}
