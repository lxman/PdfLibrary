namespace PdfLibrary.Core;

/// <summary>
/// Writes a file atomically. The payload is written to a temporary file in the destination's
/// own directory, flushed to disk, and then renamed into place, replacing any existing file.
/// If the payload throws, the destination is left untouched and the temp file is cleaned up.
/// </summary>
/// <remarks>
/// This guarantees a save never truncates a file it then fails to finish writing: an
/// interrupted or failed save cannot destroy the user's previous file. Because the temp file
/// lives in the same directory as the destination, the final <see cref="File.Move(string,string,bool)"/>
/// is a same-volume rename (atomic on POSIX; replace-existing on Windows) rather than a copy.
/// The stream overloads of <c>Save</c>/<c>Write</c> cannot offer this — the library does not
/// own a caller-supplied stream — so only the file-path overloads route through here.
/// </remarks>
internal static class AtomicFileWriter
{
    /// <summary>Atomically writes to <paramref name="path"/> using <paramref name="writePayload"/>.</summary>
    public static void Write(string path, Action<Stream> writePayload)
    {
        ArgumentNullException.ThrowIfNull(writePayload);
        Write(path, stream =>
        {
            writePayload(stream);
            return true;
        });
    }

    /// <summary>
    /// Atomically writes to <paramref name="path"/> using <paramref name="writePayload"/>,
    /// returning the payload's result once the destination has been replaced.
    /// </summary>
    public static T Write<T>(string path, Func<Stream, T> writePayload,
        int maxMoveAttempts = 5, int baseRetryDelayMs = 10)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(writePayload);

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!; // non-empty for an absolute file path
        string tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            T result;
            using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                result = writePayload(temp);
                temp.Flush(flushToDisk: true);
            }

            MoveWithRetry(tempPath, fullPath, maxMoveAttempts, baseRetryDelayMs);
            return result;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// The replace-rename, retried with exponential backoff on the two exceptions Windows
    /// raises when something transiently holds the destination — <see cref="IOException"/>
    /// (sharing violation) and <see cref="UnauthorizedAccessException"/> (access denied, the
    /// shape antivirus/Search-indexer scans produce). Real-time scanners love to sniff freshly
    /// written files, so a rename can land while a scanner holds the target; git retries its
    /// renames on Windows for the same reason. Default budget: 5 attempts over ~150 ms of
    /// backoff (10·2ⁿ ms). A PERSISTENT hold — a genuine lock or permission problem — fails
    /// every attempt and the last exception propagates unchanged, so real errors still throw.
    /// Observed 2026-07-29 as rotating flakes across the editing tests, every stack ending in
    /// this File.Move; each victim passed in isolation.
    /// </summary>
    private static void MoveWithRetry(string tempPath, string fullPath, int maxAttempts, int baseDelayMs)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, fullPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts
                                       && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(baseDelayMs * (1 << (attempt - 1)));
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path); // no-op when the file does not exist
        }
        catch
        {
            // Best effort: a leftover temp file must not mask the original failure.
        }
    }
}
