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

    // Windows file-lock race, seen as rotating test flakes (2026-07-29): the replace-rename in
    // File.Move throws IOException (sharing violation) or UnauthorizedAccessException when an
    // external scanner (Defender, Search indexer) transiently holds the destination. The writer
    // must absorb a TRANSIENT hold by retrying the rename — this test holds the destination
    // open briefly on another thread and releases it well inside the retry budget.
    // FLAKE FIX (tracker issue 55, 2026-08-20). This test used to race the very budget it was
    // measuring, and lost about half the time under a full-suite run while passing 10/10 in
    // isolation. Two independent causes, both fixed here — neither needed a production change:
    //
    //   1. The releaser ran on the THREAD POOL. Thread.Sleep(50) bounds how long the handle is
    //      held only once the work item STARTS, and under the suite's parallel collections the
    //      pool is saturated, so its start could slip past the whole ~150 ms default budget. Every
    //      attempt then found the file held and the last UnauthorizedAccessException propagated.
    //      A dedicated thread is scheduled by the OS, not the pool, so suite load cannot defer it.
    //   2. The budget was the DEFAULT ~150 ms against a 50 ms hold — a 3x margin, which is not a
    //      margin at all on a loaded machine. Write takes the budget as a parameter precisely so a
    //      caller can choose one; 10 attempts is ~5.1 s, a 100x margin over the hold.
    //
    // What the test still proves is unchanged: the handle is held with FileShare.None BEFORE Write
    // is called, so the first rename attempt necessarily fails and only the retry loop can save it.
    // Delete that loop and this test fails — which is the property that makes it worth having, and
    // is pinned from the other side by Write_DestinationHeldPastRetryBudget below.
    [Fact]
    public void Write_DestinationTransientlyLocked_RetriesAndSucceeds()
    {
        string dir = NewTempDir();
        string path = Path.Combine(dir, "locked.bin");
        File.WriteAllBytes(path, [1, 2, 3]);

        // The `using` is belt-and-braces: the releaser normally disposes the handle mid-test, but a
        // cancelled run can stop it ever getting there, and a still-held handle would then block the
        // temp-dir cleanup. FileStream.Dispose is idempotent, so the double dispose is harmless.
        using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var releaser = new Thread(() => { Thread.Sleep(50); handle.Dispose(); }) { IsBackground = true };
        releaser.Start();

        // Must run while the releaser still holds the file — this is the retry the test exists to
        // pin, so the Join deliberately comes AFTER the write, not before.
        // The generic overload, because only it forwards the retry budget; the Action<Stream> one
        // takes the default. The payload is otherwise identical.
        AtomicFileWriter.Write(path, stream => { stream.Write([7, 8, 9]); return true; },
            maxMoveAttempts: 10);

        releaser.Join();
        Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    // A PERSISTENT hold: platform semantics genuinely differ, and the test pins each.
    // Windows enforces sharing modes at the kernel, so the rename fails every attempt and the
    // last exception must propagate once the budget is spent — the retry must not convert real
    // permission problems into hangs or silent success. POSIX rename() replaces the directory
    // entry regardless of open handles (FileShare is advisory there and rename never consults
    // it), so the same write SUCCEEDS on Unix — the reader keeps the old inode, the path gets
    // the new bytes. This platform split is why the Windows-flake retry exists at all.
    [Fact]
    public void Write_DestinationHeldPastRetryBudget_WindowsThrows_UnixReplaces()
    {
        string dir = NewTempDir();
        string path = Path.Combine(dir, "held.bin");
        File.WriteAllBytes(path, [1, 2, 3]);

        using var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        if (OperatingSystem.IsWindows())
        {
            Assert.ThrowsAny<Exception>(() =>
                AtomicFileWriter.Write(path, stream =>
                {
                    stream.Write([9]);
                    return true;
                }, maxMoveAttempts: 2, baseRetryDelayMs: 1));

            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));   // temp cleaned up on final failure
        }
        else
        {
            AtomicFileWriter.Write(path, stream =>
            {
                stream.Write([9]);
                return true;
            }, maxMoveAttempts: 2, baseRetryDelayMs: 1);

            Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
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
