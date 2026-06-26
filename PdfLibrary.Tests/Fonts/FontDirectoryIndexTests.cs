using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontDirectoryIndexTests
{
    private static string FixtureBytesPath =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf");

    [Fact]
    public void FindPath_ReturnsFile_CaseInsensitive()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string target = Path.Combine(dir, "LiberationSans-Regular.ttf");
            File.Copy(FixtureBytesPath, target);

            var index = new FontDirectoryIndex([dir]);

            Assert.Equal(target, index.FindPath("liberationsans-regular"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenAbsent()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var index = new FontDirectoryIndex([dir]);
            Assert.Null(index.FindPath("NoSuchFont"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Constructor_IgnoresMissingDirectories()
    {
        var index = new FontDirectoryIndex([Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())]);
        Assert.Null(index.FindPath("anything"));
    }
}
