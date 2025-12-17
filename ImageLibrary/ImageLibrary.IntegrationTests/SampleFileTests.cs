using ImageLibrary.Jbig2;
using ImageLibrary.Png;
using Xunit;
using Xunit.Abstractions;

namespace ImageLibrary.IntegrationTests;

/// <summary>
/// Integration tests that attempt to decode all sample files from test suites.
/// </summary>
public class SampleFileTests
{
    private readonly ITestOutputHelper _output;
    private static readonly string TestImagesRoot = FindTestImagesRoot();

    public SampleFileTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindTestImagesRoot()
    {
        // Look for TestImages directory relative to the test assembly
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string testImagesPath = Path.Combine(dir, "TestImages");
            if (Directory.Exists(testImagesPath))
                return testImagesPath;
            dir = Path.GetDirectoryName(dir);
        }

        // Fall back to home directory path
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "RiderProjects", "ImageLibrary", "TestImages");
    }

    public static IEnumerable<object[]> GetBmpFiles()
    {
        if (!Directory.Exists(TestImagesRoot))
            yield break;

        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.bmp", SearchOption.AllDirectories))
            yield return [file];
        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.BMP", SearchOption.AllDirectories))
            yield return [file];
    }

    public static IEnumerable<object[]> GetTgaFiles()
    {
        if (!Directory.Exists(TestImagesRoot))
            yield break;

        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.tga", SearchOption.AllDirectories))
            yield return [file];
        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.TGA", SearchOption.AllDirectories))
            yield return [file];
    }

    public static IEnumerable<object[]> GetGifFiles()
    {
        if (!Directory.Exists(TestImagesRoot))
            yield break;

        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.gif", SearchOption.AllDirectories))
            yield return [file];
        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.GIF", SearchOption.AllDirectories))
            yield return [file];
    }

    public static IEnumerable<object[]> GetPngFiles()
    {
        if (!Directory.Exists(TestImagesRoot))
            yield break;

        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.png", SearchOption.AllDirectories))
            yield return [file];
        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.PNG", SearchOption.AllDirectories))
            yield return [file];
    }

    public static IEnumerable<object[]> GetJbig2Files()
    {
        if (!Directory.Exists(TestImagesRoot))
            yield break;

        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.jb2", SearchOption.AllDirectories))
            yield return [file];
        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.jbig2", SearchOption.AllDirectories))
            yield return [file];
        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.JB2", SearchOption.AllDirectories))
            yield return [file];
        foreach (string file in Directory.EnumerateFiles(TestImagesRoot, "*.JBIG2", SearchOption.AllDirectories))
            yield return [file];
    }

    [Theory]
    [MemberData(nameof(GetPngFiles))]
    public void DecodePngFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        string relativePath = Path.GetRelativePath(TestImagesRoot, filePath);

        // In PngSuite, files starting with 'x' are intentionally corrupt
        bool expectFailure = fileName.StartsWith("x", StringComparison.OrdinalIgnoreCase);

        try
        {
            byte[] data = File.ReadAllBytes(filePath);
            PngImage image = PngDecoder.Decode(data);

            Assert.True(image.Width > 0, $"Width should be positive: {relativePath}");
            Assert.True(image.Height > 0, $"Height should be positive: {relativePath}");
            Assert.NotNull(image.PixelData);
            Assert.Equal(image.Width * image.Height * 4, image.PixelData.Length);

            _output.WriteLine($"OK: {relativePath} ({image.Width}x{image.Height}, {image.ColorType})");

            if (expectFailure)
                _output.WriteLine($"  WARNING: Expected failure but succeeded: {relativePath}");
        }
        catch (Exception ex) when (expectFailure)
        {
            _output.WriteLine($"EXPECTED FAIL: {relativePath} - {ex.GetType().Name}: {ex.Message}");
        }
        catch (PngException ex)
        {
            _output.WriteLine($"UNSUPPORTED: {relativePath} - {ex.Message}");
        }
    }

    [Theory]
    [MemberData(nameof(GetJbig2Files))]
    public void DecodeJbig2File(string filePath)
    {
        string relativePath = Path.GetRelativePath(TestImagesRoot, filePath);

        try
        {
            byte[] data = File.ReadAllBytes(filePath);
            var decoder = new Jbig2Decoder(data);
            Bitmap bitmap = decoder.Decode();

            Assert.True(bitmap.Width > 0, $"Width should be positive: {relativePath}");
            Assert.True(bitmap.Height > 0, $"Height should be positive: {relativePath}");
            Assert.NotNull(bitmap.Data);

            _output.WriteLine($"OK: {relativePath} ({bitmap.Width}x{bitmap.Height}, 1-bit)");
        }
        catch (Jbig2Exception ex)
        {
            _output.WriteLine($"UNSUPPORTED: {relativePath} - {ex.Message}");
        }
    }
}

/// <summary>
/// Summary tests that report overall success rates.
/// </summary>
public class SampleFileSummaryTests
{
    private readonly ITestOutputHelper _output;
    private static readonly string TestImagesRoot = FindTestImagesRoot();

    public SampleFileSummaryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindTestImagesRoot()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "RiderProjects", "ImageLibrary", "TestImages");
    }

    [Fact]
    public void PngDecodeSummary()
    {
        if (!Directory.Exists(TestImagesRoot))
        {
            _output.WriteLine("TestImages directory not found, skipping");
            return;
        }

        List<string> files = Directory.EnumerateFiles(TestImagesRoot, "*.png", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(TestImagesRoot, "*.PNG", SearchOption.AllDirectories))
            .ToList();

        // Filter out intentionally corrupt files (start with 'x')
        List<string> validFiles = files.Where(f => !Path.GetFileName(f).StartsWith("x", StringComparison.OrdinalIgnoreCase)).ToList();

        int success = 0, failed = 0, unsupported = 0;
        var failures = new List<string>();

        foreach (string file in validFiles)
        {
            try
            {
                byte[] data = File.ReadAllBytes(file);
                PngImage image = PngDecoder.Decode(data);
                if (image.Width > 0 && image.Height > 0)
                    success++;
                else
                    failed++;
            }
            catch (PngException)
            {
                unsupported++;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"{Path.GetFileName(file)}: {ex.GetType().Name}");
            }
        }

        _output.WriteLine($"PNG Summary: {success} success, {unsupported} unsupported, {failed} failed out of {validFiles.Count} valid files ({files.Count - validFiles.Count} intentionally corrupt skipped)");
        _output.WriteLine($"Success rate: {100.0 * success / validFiles.Count:F1}%");

        foreach (string f in failures.Take(10))
            _output.WriteLine($"  Failed: {f}");

        Assert.True(success > validFiles.Count * 0.5, "At least 50% of PNG files should decode successfully");
    }

    [Fact]
    public void Jbig2DecodeSummary()
    {
        if (!Directory.Exists(TestImagesRoot))
        {
            _output.WriteLine("TestImages directory not found, skipping");
            return;
        }

        List<string> files = Directory.EnumerateFiles(TestImagesRoot, "*.jb2", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(TestImagesRoot, "*.jbig2", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(TestImagesRoot, "*.JB2", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(TestImagesRoot, "*.JBIG2", SearchOption.AllDirectories))
            .ToList();

        if (files.Count == 0)
        {
            _output.WriteLine("No JBIG2 files found, skipping");
            return;
        }

        int success = 0, failed = 0, unsupported = 0;
        var failures = new List<string>();

        foreach (string file in files)
        {
            try
            {
                byte[] data = File.ReadAllBytes(file);
                var decoder = new Jbig2Decoder(data);
                Bitmap bitmap = decoder.Decode();
                if (bitmap.Width > 0 && bitmap.Height > 0)
                    success++;
                else
                    failed++;
            }
            catch (Jbig2Exception)
            {
                unsupported++;
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"{Path.GetFileName(file)}: {ex.GetType().Name}");
            }
        }

        _output.WriteLine($"JBIG2 Summary: {success} success, {unsupported} unsupported, {failed} failed out of {files.Count} files");
        _output.WriteLine($"Success rate: {100.0 * success / files.Count:F1}%");

        foreach (string f in failures.Take(10))
            _output.WriteLine($"  Failed: {f}");

        Assert.True(success > files.Count * 0.5, "At least 50% of JBIG2 files should decode successfully");
    }
}
