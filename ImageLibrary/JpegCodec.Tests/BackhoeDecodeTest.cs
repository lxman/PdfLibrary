using ImageLibrary.Jpeg;
using Xunit;
using Xunit.Abstractions;

namespace JpegCodec.Tests;

/// <summary>
/// Special test for the backhoe JPEG from the PDF.
/// </summary>
public class BackhoeDecodeTest
{
    private readonly ITestOutputHelper _output;

    public BackhoeDecodeTest(ITestOutputHelper output)
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
    public void DecodeBackhoe_SaveAsPpm()
    {
        string jpegPath = Path.Combine(GetTestImagesPath(), "backhoe-006.jpg");
        string ppmPath = Path.Combine(GetTestImagesPath(), "backhoe-test-output.ppm");

        _output.WriteLine($"Input: {jpegPath}");
        _output.WriteLine($"Output: {ppmPath}");

        DecodedImage image = JpegDecoder.DecodeFile(jpegPath);

        _output.WriteLine($"Decoded: {image.Width}x{image.Height}");

        // Save as PPM (simple format that can be viewed/converted)
        using FileStream fs = File.Create(ppmPath);
        using var sw = new StreamWriter(fs);

        // PPM header
        sw.WriteLine("P3");
        sw.WriteLine($"{image.Width} {image.Height}");
        sw.WriteLine("255");

        // Pixel data
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                (byte r, byte g, byte b) = image.GetPixel(x, y);
                sw.Write($"{r} {g} {b} ");
            }
            sw.WriteLine();
        }

        _output.WriteLine($"Saved PPM to: {ppmPath}");

        // Verify file was created
        Assert.True(File.Exists(ppmPath));
        var fileInfo = new FileInfo(ppmPath);
        Assert.True(fileInfo.Length > 0);
        _output.WriteLine($"File size: {fileInfo.Length} bytes");
    }
}
