using ICCSharp.Profile;
using Logging;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Resolves the active default CMYK ICC profile for bare DeviceCMYK (and resolved Separation) color
/// that carries no embedded profile. Precedence: <see cref="OverrideProfileBytes"/> (in-memory) →
/// <see cref="OverridePath"/> (disk) → the bundled CC0 default. Cached; setting either override
/// invalidates the cache. Thread-safe.
/// </summary>
public sealed class CmykProfileProvider
{
    /// <summary>Process-wide default instance.</summary>
    public static CmykProfileProvider Default { get; } = new();

    private readonly object _lock = new();
    private byte[]? _overrideBytes;
    private string? _overridePath;
    private IccProfile? _cached;
    private string? _cachedKey;

    /// <summary>In-memory CMYK profile bytes that override both the path and the bundled default.</summary>
    public byte[]? OverrideProfileBytes
    {
        get { lock (_lock) { return _overrideBytes; } }
        set
        {
            lock (_lock)
            {
                if (ReferenceEquals(_overrideBytes, value)) return;
                _overrideBytes = value;
                _cached = null;
                _cachedKey = null;
            }
        }
    }

    /// <summary>Optional path to a CMYK <c>.icc</c> profile that overrides the bundled default.</summary>
    public string? OverridePath
    {
        get { lock (_lock) { return _overridePath; } }
        set
        {
            lock (_lock)
            {
                if (_overridePath == value) return;
                _overridePath = value;
                _cached = null;
                _cachedKey = null;
            }
        }
    }

    /// <summary>Returns the active CMYK profile, parsing and caching it on first use.</summary>
    public IccProfile GetProfile()
    {
        lock (_lock)
        {
            string key = _overrideBytes is not null ? "<bytes>" : _overridePath ?? "<bundled>";
            if (_cached is not null && _cachedKey == key) return _cached;

            IccProfile profile = Load(_overrideBytes, _overridePath);
            _cached = profile;
            _cachedKey = key;
            return profile;
        }
    }

    private static IccProfile Load(byte[]? overrideBytes, string? overridePath)
    {
        if (overrideBytes is not null)
        {
            try { return IccProfile.Parse(overrideBytes); }
            catch (Exception ex)
            {
                PdfLogger.Log(LogCategory.Graphics,
                    $"CMYK profile override bytes failed to parse ({ex.GetType().Name}: {ex.Message}); falling back.");
            }
        }

        if (!string.IsNullOrEmpty(overridePath))
        {
            try { return IccProfile.Parse(File.ReadAllBytes(overridePath)); }
            catch (Exception ex)
            {
                PdfLogger.Log(LogCategory.Graphics,
                    $"CMYK profile override '{overridePath}' failed to load ({ex.GetType().Name}: {ex.Message}); using bundled default.");
            }
        }

        return IccProfile.Parse(IccResources.ReadDefaultCmykProfile());
    }
}
