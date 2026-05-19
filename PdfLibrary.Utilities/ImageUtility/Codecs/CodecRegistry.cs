using System.IO;

namespace ImageUtility.Codecs;

/// <summary>
/// Central registry for managing image codecs.
/// </summary>
public class CodecRegistry
{
    private static readonly Lazy<CodecRegistry> _instance = new(() => new CodecRegistry());
    private readonly List<IImageCodec> _codecs = [];

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
        if (!_codecs.Contains(codec))
        {
            _codecs.Add(codec);
        }
    }

    /// <summary>
    /// Unregisters a codec from the registry.
    /// </summary>
    /// <param name="codec">The codec to unregister.</param>
    public void Unregister(IImageCodec codec)
    {
        _codecs.Remove(codec);
    }

    /// <summary>
    /// Gets all registered codecs.
    /// </summary>
    public IReadOnlyList<IImageCodec> GetAllCodecs() => _codecs.AsReadOnly();

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

        return _codecs.FirstOrDefault(c =>
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
        foreach (IImageCodec codec in _codecs)
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

        if (codec == null)
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

        if (codec == null)
        {
            throw new NotSupportedException($"No encoder found for format: {Path.GetExtension(filePath)}");
        }

        byte[] encodedData = codec.Encode(imageData, options);
        File.WriteAllBytes(filePath, encodedData);
    }

    private void RegisterBuiltInCodecs()
    {
        // In-house codecs from the ImageLibrary projects — primary implementations.
        Register(new CustomJpegCodec());
        Register(new CustomPngCodec());
        Register(new CustomBmpCodec());
        Register(new CustomGifCodec());
        Register(new CustomTgaCodec());
        Register(new CustomTiffCodec());
        Register(new CustomJpeg2000Codec());
        Register(new CustomPbmCodec());
    }
}
