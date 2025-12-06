using Melville.CSJ2K;
using Melville.CSJ2K.Util;
using Xunit.Abstractions;

namespace Compressors.Jpeg2000.Tests;

/// <summary>
/// Test to verify pixel data at byte boundaries (256, 512) in CoreJ2K decoder
/// </summary>
public class CoreJ2kBoundaryTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public CoreJ2kBoundaryTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void TestPixelBoundaries_NoWrapping()
    {
        // Load a test JP2 file that exhibits the pixel wrapping corruption
        string testFilePath = @"C:\Users\jorda\Downloads\Automotive\Ford Edge\pdf\image6.jp2";

        if (!File.Exists(testFilePath))
        {
            throw new FileNotFoundException($"Test file not found: {testFilePath}");
        }

        byte[] jp2Data = File.ReadAllBytes(testFilePath);

        // Decode using CoreJ2K
        using var stream = new MemoryStream(jp2Data);
        PortableImage portableImage = J2kReader.FromStream(stream);

        int width = portableImage.Width;
        int height = portableImage.Height;
        int components = portableImage.NumberOfComponents;

        _testOutputHelper.WriteLine($"Decoded image: {width}x{height}, components={components}");

        // Get raw component data (before any rendering)
        int[] component0 = portableImage.GetComponent(0);

        if (width <= 512 || height <= 0) return;
        // Check pixels at boundaries
        int pixel0 = component0[0];           // Pixel at x=0
        int pixel255 = component0[255];       // Pixel at x=255
        int pixel256 = component0[256];       // Pixel at x=256
        int pixel512 = component0[512];       // Pixel at x=512

        _testOutputHelper.WriteLine($"Component 0 values at row 0:");
        _testOutputHelper.WriteLine($"  Pixel(0): {pixel0}");
        _testOutputHelper.WriteLine($"  Pixel(255): {pixel255}");
        _testOutputHelper.WriteLine($"  Pixel(256): {pixel256}");
        _testOutputHelper.WriteLine($"  Pixel(512): {pixel512}");

        // Check for wrapping (pixel values repeating at byte boundaries)
        bool wraps256 = (pixel0 == pixel256);
        bool wraps512 = (pixel0 == pixel512);

        if (wraps256 || wraps512)
        {
            _testOutputHelper.WriteLine($"CORRUPTION DETECTED: Pixel wrapping at boundaries!");
            _testOutputHelper.WriteLine($"  256 boundary: {(wraps256 ? "WRAPS" : "OK")}");
            _testOutputHelper.WriteLine($"  512 boundary: {(wraps512 ? "WRAPS" : "OK")}");
        }
        else
        {
            _testOutputHelper.WriteLine("No wrapping detected - CoreJ2K decoder output looks correct");
        }

        // Fail test if corruption detected
        Assert.False(wraps256, "Pixel wrapping detected at x=256 boundary");
        Assert.False(wraps512, "Pixel wrapping detected at x=512 boundary");
    }
}
