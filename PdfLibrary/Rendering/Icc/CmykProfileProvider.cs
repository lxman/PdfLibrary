using ICCSharp.Profile;
using Logging;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Resolves the active default CMYK ICC profile used for bare DeviceCMYK (and resolved Separation)
/// color that carries no embedded profile. Precedence: an explicit <see cref="OverridePath"/> on
/// disk, else the bundled "U.S. Web Coated (SWOP) v2". The resolved profile is cached; changing the
/// override invalidates it. Thread-safe.
/// </summary>
public sealed class CmykProfileProvider
{
    /// <summary>Process-wide default instance.</summary>
    public static CmykProfileProvider Default { get; } = new();

    private readonly object _lock = new();
    private string? _overridePath;
    private IccProfile? _cached;
    private string? _cachedKey;

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
            string key = _overridePath ?? "<bundled>";
            if (_cached is not null && _cachedKey == key) return _cached;

            IccProfile profile = Load(_overridePath);
            _cached = profile;
            _cachedKey = key;
            return profile;
        }
    }

    private static IccProfile Load(string? overridePath)
    {
        if (!string.IsNullOrEmpty(overridePath))
        {
            try
            {
                return IccProfile.Parse(File.ReadAllBytes(overridePath));
            }
            catch (Exception ex)
            {
                PdfLogger.Log(LogCategory.Graphics,
                    $"CMYK profile override '{overridePath}' failed to load ({ex.GetType().Name}: {ex.Message}); using bundled SWOP v2.");
            }
        }

        return IccProfile.Parse(IccResources.ReadSwop());
    }
}
