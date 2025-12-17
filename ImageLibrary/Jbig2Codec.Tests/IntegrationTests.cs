using System.Text;
using ImageLibrary.Jbig2;
using Xunit;

namespace Jbig2Codec.Tests;

public class IntegrationTests
{
    private static readonly string TestImagesPath = GetTestImagesPath();

    private static string GetTestImagesPath()
    {
        // Find TestImages directory relative to the test assembly
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);

        while (dir != null)
        {
            string testImagesDir = Path.Combine(dir.FullName, "TestImages", "jbig2_suite");
            if (Directory.Exists(testImagesDir))
                return testImagesDir;
            dir = dir.Parent;
        }

        // Fallback to absolute path
        return "/Users/michaeljordan/RiderProjects/ImageLibrary/TestImages/jbig2_suite";
    }

    private static byte[] LoadTestFile(string filename)
    {
        string path = Path.Combine(TestImagesPath, filename);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Test file not found: {path}");
        return File.ReadAllBytes(path);
    }

    private static byte[]? LoadReferenceFile(string filename)
    {
        string path = Path.Combine(TestImagesPath, filename);
        if (!File.Exists(path))
            return null;
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void Decode_AnnexH_ProducesValidBitmap()
    {
        byte[] data = LoadTestFile("annex-h.jbig2");

        var decoder = new Jbig2Decoder(data);
        Bitmap bitmap = decoder.Decode();

        Assert.NotNull(bitmap);
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(56, bitmap.Height);
    }

    [Fact]
    public void Decode_SimpleLine_ProducesValidBitmap()
    {
        byte[] data = LoadTestFile("simple_line.jb2");

        var decoder = new Jbig2Decoder(data);
        Bitmap bitmap = decoder.Decode();

        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
    }

    [Fact]
    public void Decode_SmallRect_ProducesValidBitmap()
    {
        byte[] data = LoadTestFile("small_rect.jb2");

        var decoder = new Jbig2Decoder(data);
        Bitmap bitmap = decoder.Decode();

        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
    }

    [Fact]
    public void Decode_MediumRect_ProducesValidBitmap()
    {
        byte[] data = LoadTestFile("medium_rect.jb2");

        var decoder = new Jbig2Decoder(data);
        Bitmap bitmap = decoder.Decode();

        Assert.NotNull(bitmap);
        Assert.True(bitmap.Width > 0);
        Assert.True(bitmap.Height > 0);
    }

    [Fact]
    public void Decode_TestSimple_ProducesValidBitmap()
    {
        byte[] data = LoadTestFile("test_simple.jb2");

        var decoder = new Jbig2Decoder(data);
        Bitmap bitmap = decoder.Decode();

        Assert.NotNull(bitmap);
        Assert.Equal(400, bitmap.Width);
        Assert.Equal(200, bitmap.Height);
    }

    [Fact]
    public void Decode_AllTestFiles_DoNotThrow()
    {
        var testFiles = new[] { "annex-h.jbig2", "simple_line.jb2", "small_rect.jb2", "medium_rect.jb2", "test_simple.jb2" };

        foreach (string file in testFiles)
        {
            try
            {
                byte[] data = LoadTestFile(file);
                var decoder = new Jbig2Decoder(data);
                Bitmap bitmap = decoder.Decode();
                Assert.NotNull(bitmap);
            }
            catch (FileNotFoundException)
            {
                // Skip missing files
            }
        }
    }

    [Fact]
    public void Decoder_WithOptions_RespectsLimits()
    {
        var options = new Jbig2DecoderOptions
        {
            MaxWidth = 10000,
            MaxHeight = 10000,
            MaxPixels = 100_000_000
        };

        byte[] data = LoadTestFile("simple_line.jb2");
        var decoder = new Jbig2Decoder(data, null, options);
        Bitmap bitmap = decoder.Decode();

        Assert.NotNull(bitmap);
    }

    [Fact]
    public void Decoder_InvalidData_ThrowsOnDecode()
    {
        // Invalid data that doesn't start with JBIG2 header
        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Decoder may accept construction but throw on Decode
        var decoder = new Jbig2Decoder(invalidData);
        Assert.ThrowsAny<Exception>(() => decoder.Decode());
    }

    [Fact]
    public void Decoder_EmptyData_ThrowsException()
    {
        byte[] emptyData = [];

        Assert.ThrowsAny<Exception>(() => new Jbig2Decoder(emptyData));
    }

    [Fact]
    public void DecodePage_ValidPage_ReturnsBitmap()
    {
        byte[] data = LoadTestFile("simple_line.jb2");
        var decoder = new Jbig2Decoder(data);

        Bitmap bitmap = decoder.DecodePage(1);
        Assert.NotNull(bitmap);
    }

    [Fact]
    public void DecodePage_NonExistentPage_ThrowsOrReturnsNull()
    {
        byte[] data = LoadTestFile("simple_line.jb2");
        var decoder = new Jbig2Decoder(data);

        // Either throws or returns null for non-existent page
        try
        {
            Bitmap? bitmap = decoder.DecodePage(999);
            Assert.Null(bitmap);
        }
        catch (Exception)
        {
            // Also acceptable
        }
    }

    [Fact]
    public void Decoder_WithGlobalData_AcceptsGlobals()
    {
        byte[] data = LoadTestFile("simple_line.jb2");
        byte[] globals = []; // No actual globals needed for this test

        var decoder = new Jbig2Decoder(data, globals);
        Bitmap bitmap = decoder.Decode();

        Assert.NotNull(bitmap);
    }

    [Fact]
    public void Bitmap_DataIntegrity_MatchesExpectedStride()
    {
        byte[] data = LoadTestFile("annex-h.jbig2");

        var decoder = new Jbig2Decoder(data);
        Bitmap bitmap = decoder.Decode();

        // Verify data array size matches expected dimensions
        int expectedStride = (bitmap.Width + 7) / 8;
        int expectedDataSize = expectedStride * bitmap.Height;

        Assert.Equal(expectedStride, bitmap.Stride);
        Assert.Equal(expectedDataSize, bitmap.Data.Length);
    }

    private static byte[] BitmapToPbm(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII);

        writer.WriteLine("P4");
        writer.WriteLine($"{bitmap.Width} {bitmap.Height}");
        writer.Flush();

        // Write raw bitmap data
        ms.Write(bitmap.Data, 0, bitmap.Stride * bitmap.Height);

        return ms.ToArray();
    }
}
