using System.IO;
using System.Text.Json;

namespace ImageUtility.Codecs;

/// <summary>
/// Stores user preferences for codec selection.
/// </summary>
public class CodecConfiguration
{
    private static readonly string ConfigFilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ImageUtility", "codec-config.json");

    /// <summary>
    /// Maps file extension to preferred decoder codec name.
    /// Example: { ".jpg": "JPEG Decoder (JpegLibrary)" }
    /// </summary>
    public Dictionary<string, string> DecodePreferences { get; set; } = new();

    /// <summary>
    /// Maps file extension to preferred encoder codec name.
    /// Example: { ".jpg": "JPEG Encoder (Custom)" }
    /// </summary>
    public Dictionary<string, string> EncodePreferences { get; set; } = new();

    /// <summary>
    /// Loads configuration from disk, or returns default if not found.
    /// </summary>
    public static CodecConfiguration Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                return JsonSerializer.Deserialize<CodecConfiguration>(json) ?? new CodecConfiguration();
            }
        }
        catch
        {
            // If loading fails, return default configuration
        }

        return new CodecConfiguration();
    }

    /// <summary>
    /// Saves configuration to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(ConfigFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigFilePath, json);
        }
        catch
        {
            // Silently fail if save fails (could log error in production)
        }
    }

    /// <summary>
    /// Gets the preferred decoder for a file extension.
    /// </summary>
    public string? GetPreferredDecoder(string extension)
    {
        extension = NormalizeExtension(extension);
        return DecodePreferences.TryGetValue(extension, out string? codecName) ? codecName : null;
    }

    /// <summary>
    /// Gets the preferred encoder for a file extension.
    /// </summary>
    public string? GetPreferredEncoder(string extension)
    {
        extension = NormalizeExtension(extension);
        return EncodePreferences.TryGetValue(extension, out string? codecName) ? codecName : null;
    }

    /// <summary>
    /// Sets the preferred decoder for a file extension.
    /// </summary>
    public void SetPreferredDecoder(string extension, string? codecName)
    {
        extension = NormalizeExtension(extension);
        if (codecName == null)
        {
            DecodePreferences.Remove(extension);
        }
        else
        {
            DecodePreferences[extension] = codecName;
        }
    }

    /// <summary>
    /// Sets the preferred encoder for a file extension.
    /// </summary>
    public void SetPreferredEncoder(string extension, string? codecName)
    {
        extension = NormalizeExtension(extension);
        if (codecName == null)
        {
            EncodePreferences.Remove(extension);
        }
        else
        {
            EncodePreferences[extension] = codecName;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.ToLowerInvariant();
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }
        return extension;
    }
}
