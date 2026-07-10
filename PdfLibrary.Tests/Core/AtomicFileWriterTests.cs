using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Core;

public class AtomicFileWriterTests : IDisposable
{
    private readonly List<string> _dirs = [];

    private string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pdflibrary-atomic-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (string dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void Write_CreatesNewFile_WithPayloadContents()
    {
        string dir = NewTempDir();
        string path = Path.Combine(dir, "new.bin");

        AtomicFileWriter.Write(path, stream => stream.Write([1, 2, 3, 4]));

        Assert.True(File.Exists(path));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        string dir = NewTempDir();
        string path = Path.Combine(dir, "data.bin");
        File.WriteAllBytes(path, [1, 2, 3]);

        AtomicFileWriter.Write(path, stream => stream.Write([7, 8, 9, 10]));

        Assert.Equal(new byte[] { 7, 8, 9, 10 }, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(dir));            // no temp left behind
    }

    // The core guarantee: a payload that fails partway must not damage the previous file.
    [Fact]
    public void Write_PayloadThrows_LeavesOriginalIntact_AndCleansUpTemp()
    {
        string dir = NewTempDir();
        string path = Path.Combine(dir, "data.bin");
        byte[] original = [1, 2, 3, 4, 5];
        File.WriteAllBytes(path, original);

        var boom = new InvalidOperationException("boom");
        InvalidOperationException caught = Assert.Throws<InvalidOperationException>(() =>
            AtomicFileWriter.Write(path, stream =>
            {
                stream.Write([9, 9, 9]);   // partially write...
                throw boom;                // ...then fail
            }));

        Assert.Same(boom, caught);
        Assert.Equal(original, File.ReadAllBytes(path));   // previous file untouched
        Assert.Single(Directory.GetFiles(dir));            // only the original remains
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));    // temp was removed
    }

    [Fact]
    public void Write_PayloadThrows_OnNewFile_LeavesNoFileBehind()
    {
        string dir = NewTempDir();
        string path = Path.Combine(dir, "never.bin");

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFileWriter.Write(path, _ => throw new InvalidOperationException()));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public void Write_Generic_ReturnsPayloadResult()
    {
        string dir = NewTempDir();
        string path = Path.Combine(dir, "data.bin");

        int written = AtomicFileWriter.Write(path, stream =>
        {
            byte[] bytes = [1, 2, 3, 4];
            stream.Write(bytes);
            return bytes.Length;
        });

        Assert.Equal(4, written);
        Assert.Equal(4, new FileInfo(path).Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Write_RejectsNullOrEmptyPath(string? path)
    {
        // null → ArgumentNullException, "" → ArgumentException (both derive from ArgumentException).
        Assert.ThrowsAny<ArgumentException>(() => AtomicFileWriter.Write(path!, _ => { }));
    }

    // End-to-end: the scenario from the merge example — save the merged result over one of
    // the input files' original path. Works because Merge is self-contained and the save is
    // atomic (temp + rename), never truncating the destination mid-write.
    [Fact]
    public void PdfDocumentSave_OverAnInputPath_ReplacesWithMergedResult()
    {
        string dir = NewTempDir();
        string aPath = Path.Combine(dir, "a.pdf");
        string bPath = Path.Combine(dir, "b.pdf");
        File.WriteAllBytes(aPath, SamplePdf("A", 2));
        File.WriteAllBytes(bPath, SamplePdf("B", 3));

        PdfDocument merged;
        using (PdfDocument a = PdfDocument.Load(aPath))
        using (PdfDocument b = PdfDocument.Load(bPath))
            merged = PdfDocumentEditor.Merge([a, b]);

        using (merged)
            merged.Save(aPath);   // overwrite an input's original filename

        using PdfDocument reloaded = PdfDocument.Load(aPath);
        Assert.Equal(5, reloaded.PageCount);
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    private static byte[] SamplePdf(string title, int pages)
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().WithMetadata(m => m.SetTitle(title));
        for (int i = 0; i < pages; i++)
            builder.AddPage(p => p.AddText(title, 72, 700, "Helvetica", 12));
        return builder.ToByteArray();
    }
}
