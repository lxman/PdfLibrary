using ICCSharp.Profile;
using Logging;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Resolves the active default CMYK ICC profile for bare DeviceCMYK (and resolved Separation) color
/// that carries no embedded profile. Precedence: <see cref="OverrideProfileBytes"/> (in-memory) →
/// <see cref="OverridePath"/> (disk) → the bundled CC0 default. Cached; setting either override
/// invalidates the cache. Thread-safe.
/// <para>
/// Mutable override fields live in a per-flow <see cref="State"/> backed by an
/// <see cref="AsyncLocal{T}"/> that falls back to the instance's process default. Production never
/// opens a scope, so every read/write hits the default state directly. Tests that mutate
/// <see cref="OverrideProfileBytes"/> or <see cref="OverridePath"/> on <see cref="Default"/> open an
/// isolated <see cref="NewScope"/> so concurrent tests never race on the shared provider.
/// </para>
/// </summary>
public sealed class CmykProfileProvider
{
    /// <summary>Process-wide default instance.</summary>
    public static CmykProfileProvider Default { get; } = new();

    private readonly State _defaultState = new();
    private readonly AsyncLocal<State?> _scoped = new();
    private State Current => _scoped.Value ?? _defaultState;

    /// <summary>In-memory CMYK profile bytes that override both the path and the bundled default.</summary>
    public byte[]? OverrideProfileBytes
    {
        get
        {
            State s = Current;
            lock (s.Lock) { return s.OverrideBytes; }
        }
        set
        {
            State s = Current;
            lock (s.Lock)
            {
                if (ReferenceEquals(s.OverrideBytes, value)) return;
                s.OverrideBytes = value;
                s.Cached = null;
                s.CachedKey = null;
            }
        }
    }

    /// <summary>Optional path to a CMYK <c>.icc</c> profile that overrides the bundled default.</summary>
    public string? OverridePath
    {
        get
        {
            State s = Current;
            lock (s.Lock) { return s.OverridePath; }
        }
        set
        {
            State s = Current;
            lock (s.Lock)
            {
                if (s.OverridePath == value) return;
                s.OverridePath = value;
                s.Cached = null;
                s.CachedKey = null;
            }
        }
    }

    /// <summary>Returns the active CMYK profile, parsing and caching it on first use.</summary>
    public IccProfile GetProfile()
    {
        State s = Current;
        lock (s.Lock)
        {
            string key = s.OverrideBytes is not null ? "<bytes>" : s.OverridePath ?? "<bundled>";
            if (s.Cached is not null && s.CachedKey == key) return s.Cached;

            IccProfile profile = Load(s.OverrideBytes, s.OverridePath);
            s.Cached = profile;
            s.CachedKey = key;
            return profile;
        }
    }

    /// <summary>
    /// Opens an isolated ambient copy of the CMYK profile override state for the calling async flow,
    /// seeded from the current values. Disposing restores the prior ambient state. Every test that
    /// writes <see cref="OverrideProfileBytes"/> or <see cref="OverridePath"/> on <see cref="Default"/>
    /// must open this first so concurrent tests never race on the process default.
    /// </summary>
    public IDisposable NewScope()
    {
        State? prior = _scoped.Value;
        State source = prior ?? _defaultState;
        State next;
        lock (source.Lock)
        {
            next = new State
            {
                OverrideBytes = source.OverrideBytes,
                OverridePath = source.OverridePath,
                Cached = source.Cached,
                CachedKey = source.CachedKey
            };
        }

        _scoped.Value = next;
        return new Scope(this, prior);
    }

    private sealed class State
    {
        public readonly object Lock = new();
        public byte[]? OverrideBytes;
        public string? OverridePath;
        public IccProfile? Cached;
        public string? CachedKey;
    }

    private sealed class Scope(CmykProfileProvider owner, State? prior) : IDisposable
    {
        public void Dispose()
        {
            owner._scoped.Value = prior;
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
