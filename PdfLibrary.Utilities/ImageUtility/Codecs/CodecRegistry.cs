using System.IO;

namespace ImageUtility.Codecs;

/// <summary>
/// Central registry for managing image codecs.
/// </summary>
public class CodecRegistry
{
    private static readonly Lazy<CodecRegistry> _instance = new(() => new CodecRegistry());
    private readonly List<IImageCodec> _codecs = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets the singleton instance of the codec registry.
    /// </summary>
    public static CodecRegistry Instance => _instance.Value;

    private CodecRegistry()
    {
        RegisterBuiltInCodecs();
    }

    /// <summary>
    /// Registers a codec with the registry.
    /// </summary>
    /// <param name="codec">The codec to register.</param>
    public void Register(IImageCodec codec)
    {
        lock (_lock)
        {
            if (!_codecs.Contains(codec))
                _codecs.Add(codec);
        }
    }

    /// <summary>
    /// Unregisters a codec from the registry.
    /// </summary>
    /// <param name="codec">The codec to unregister.</param>
    public void Unregister(IImageCodec codec)
    {
        lock (_lock)
        {
            _codecs.Remove(codec);
        }
    }

    /// <summary>
    /// Gets all registered codecs.
    /// </summary>
    public IReadOnlyList<IImageCodec> GetAllCodecs()
    {
        lock (_lock)
        {
            return _codecs.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Finds a codec that can handle the given file extension.
    /// </summary>
    /// <param name="extension">File extension (e.g., ".jpg").</param>
    /// <param name="requireDecode">If true, only return codecs that can decode.</param>
    /// <param name="requireEncode">If true, only return codecs that can encode.</param>
    /// <returns>Matching codec, or null if none found.</returns>
    public IImageCodec? FindByExtension(string extension, bool requireDecode = false, bool requireEncode = false)
    {
        extension = extension.ToLowerInvariant();
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        IReadOnlyList<IImageCodec> snapshot;
        lock (_lock) { snapshot = _codecs.ToList(); }
        return snapshot.FirstOrDefault(c =>
            c.Extensions.Contains(extension) &&
            (!requireDecode || c.CanDecode) &&
            (!requireEncode || c.CanEncode));
    }

    /// <summary>
    /// Finds a codec that can handle the given file based on its magic bytes.
    /// </summary>
    /// <param name="filePath">Path to the image file.</param>
    /// <param name="requireDecode">If true, only return codecs that can decode.</param>
    /// <param name="requireEncode">If true, only return codecs that can encode.</param>
    /// <returns>Matching codec, or null if none found.</returns>
    public IImageCodec? FindByFileSignature(string filePath, bool requireDecode = false, bool requireEncode = false)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        // Read first 16 bytes for magic number detection
        using FileStream stream = File.OpenRead(filePath);
        var header = new byte[16];
        int bytesRead = stream.Read(header, 0, header.Length);

        if (bytesRead == 0)
        {
            return null;
        }

        ReadOnlySpan<byte> headerSpan = header.AsSpan(0, bytesRead);
        IReadOnlyList<IImageCodec> snapshot;
        lock (_lock) { snapshot = _codecs.ToList(); }
        foreach (IImageCodec codec in snapshot)
        {
            if (codec.CanHandle(headerSpan) &&
                (!requireDecode || codec.CanDecode) &&
                (!requireEncode || codec.CanEncode))
            {
                return codec;
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes an image file using the appropriate codec.
    /// Automatically selects a codec with decode capability.
    /// </summary>
    /// <param name="filePath">Path to the image file.</param>
    /// <returns>Decoded image data.</returns>
    /// <exception cref="NotSupportedException">If no codec can handle the file.</exception>
    public ImageData DecodeFile(string filePath)
    {
        // Try to find codec by file signature first (most reliable)
        // Explicitly require decode capability
        IImageCodec? codec = FindByFileSignature(filePath, requireDecode: true);

        // Fallback to extension-based lookup with decode requirement
        codec ??= FindByExtension(Path.GetExtension(filePath), requireDecode: true);

        if (codec is null)
        {
            throw new NotSupportedException($"No decoder found for file: {filePath}");
        }

        byte[] fileData = File.ReadAllBytes(filePath);
        return codec.Decode(fileData);
    }

    /// <summary>
    /// Encodes image data to a file using the appropriate codec.
    /// Automatically selects a codec with encode capability.
    /// </summary>
    /// <param name="imageData">Image data to encode.</param>
    /// <param name="filePath">Output file path.</param>
    /// <param name="options">Encoding options.</param>
    /// <exception cref="NotSupportedException">If no codec can handle the file format.</exception>
    public void EncodeFile(ImageData imageData, string filePath, CodecOptions? options = null)
    {
        // Find codec with encode capability
        IImageCodec? codec = FindByExtension(Path.GetExtension(filePath), requireEncode: true);

        if (codec is null)
        {
            throw new NotSupportedException($"No encoder found for format: {Path.GetExtension(filePath)}");
        }

        byte[] encodedData = codec.Encode(imageData, options);
        File.WriteAllBytes(filePath, encodedData);
    }

    private void RegisterBuiltInCodecs()
    {
        // In-house codecs from the ImageLibrary projects — primary implementations.
        // Order matters for signature-based dispatch (first CanHandle wins). TGA is registered LAST
        // because it has no magic number: its header-field heuristic can otherwise claim files that
        // belong to a magic-bearing codec registered after it.
        Register(new CustomJpegCodec());
        Register(new CustomPngCodec());
        Register(new CustomBmpCodec());
        Register(new CustomGifCodec());
        Register(new CustomTiffCodec());
        Register(new CustomJpeg2000Codec());
        Register(new CustomPbmCodec());
        Register(new CustomTgaCodec());
    }
}
