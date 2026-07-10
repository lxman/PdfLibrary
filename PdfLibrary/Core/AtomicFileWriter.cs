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
    public static T Write<T>(string path, Func<Stream, T> writePayload)
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

            File.Move(tempPath, fullPath, overwrite: true);
            return result;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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
