using ImageLibrary.Jpeg;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace JpegCodec.Tests;

/// <summary>
/// Compare all test images against ImageSharp.
/// </summary>
public class CompareAllImages
{
    private readonly ITestOutputHelper _output;

    public CompareAllImages(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetTestImagesPath()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "TestImages", "jpeg_test")))
        {
            dir = dir.Parent;
        }

        return dir != null
            ? Path.Combine(dir.FullName, "TestImages", "jpeg_test")
            : "/Users/michaeljordan/RiderProjects/ImageLibrary/TestImages/jpeg_test";
    }

    [Fact]
    public void CompareAllTestImages_AgainstImageSharp()
    {
        string testPath = GetTestImagesPath();
        List<string> jpegFiles = Directory.GetFiles(testPath, "*.jpg", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        _output.WriteLine($"Found {jpegFiles.Count} JPEG files");
        _output.WriteLine("");

        var passed = 0;
        var failed = 0;
        var failures = new List<string>();

        foreach (string jpegPath in jpegFiles)
        {
            string relativePath = Path.GetRelativePath(testPath, jpegPath);

            try
            {
                DecodedImage ourImage = JpegDecoder.DecodeFile(jpegPath);

                // Load with ImageSharp - use Rgba32 for color images
                using Image<Rgba32> isImage = Image.Load<Rgba32>(jpegPath);

                if (ourImage.Width != isImage.Width || ourImage.Height != isImage.Height)
                {
                    failures.Add($"{relativePath}: Size mismatch - ours {ourImage.Width}x{ourImage.Height}, IS {isImage.Width}x{isImage.Height}");
                    failed++;
                    continue;
                }

                // Compare pixels
                int totalPixels = ourImage.Width * ourImage.Height;
                var matchingPixels = 0;
                var maxDiff = 0;
                long diffSum = 0;

                for (var y = 0; y < ourImage.Height; y++)
                {
                    for (var x = 0; x < ourImage.Width; x++)
                    {
                        (byte ourR, byte ourG, byte ourB) = ourImage.GetPixel(x, y);
                        Rgba32 isPixel = isImage[x, y];

                        int diffR = Math.Abs(ourR - isPixel.R);
                        int diffG = Math.Abs(ourG - isPixel.G);
                        int diffB = Math.Abs(ourB - isPixel.B);
                        int maxChannelDiff = Math.Max(diffR, Math.Max(diffG, diffB));

                        diffSum += diffR + diffG + diffB;
                        maxDiff = Math.Max(maxDiff, maxChannelDiff);

                        if (maxChannelDiff <= 2)
                        {
                            matchingPixels++;
                        }
                    }
                }

                double matchPercent = 100.0 * matchingPixels / totalPixels;
                double avgDiff = (double)diffSum / (totalPixels * 3);

                // Consider it a pass if 99%+ pixels match within tolerance of 2
                if (matchPercent >= 99.0 && maxDiff <= 10)
                {
                    _output.WriteLine($"PASS: {relativePath} ({matchPercent:F1}% match, max diff {maxDiff})");
                    passed++;
                }
                else
                {
                    failures.Add($"{relativePath}: {matchPercent:F1}% match, max diff {maxDiff}, avg diff {avgDiff:F2}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{relativePath}: Exception - {ex.Message}");
                failed++;
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"Results: {passed} passed, {failed} failed out of {jpegFiles.Count}");

        if (failures.Any())
        {
            _output.WriteLine("");
            _output.WriteLine("Failures:");
            foreach (string f in failures)
            {
                _output.WriteLine($"  FAIL: {f}");
            }
        }

        Assert.True(failed == 0, $"{failed} images failed comparison");
    }
}
