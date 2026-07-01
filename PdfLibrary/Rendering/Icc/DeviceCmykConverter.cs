using ICCSharp;
using ICCSharp.Profile;
using Logging;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Bidirectional DeviceCMYK ↔ sRGB conversion through a CMYK ICC profile (SWOP v2 by default).
/// Forward (CMYK→sRGB) drives display; inverse (sRGB→CMYK) brings RGB/Gray content into a CMYK
/// buffer. If the ICC transforms cannot be built the converter degrades to the naive formulas so
/// callers never hard-fail.
/// </summary>
public sealed class DeviceCmykConverter
{
    private readonly IccTransform? _toRgb;   // CMYK(4) -> sRGB(3)
    private readonly IccTransform? _toCmyk;  // sRGB(3) -> CMYK(4)

    /// <summary>True when ICC transforms could not be built and naive fallback math is in use.</summary>
    public bool IsDegraded => _toRgb is null || _toCmyk is null;

    /// <summary>Builds a converter bound to <paramref name="cmykProfile"/>.</summary>
    public DeviceCmykConverter(IccProfile cmykProfile)
    {
        if (cmykProfile is null) throw new ArgumentNullException(nameof(cmykProfile));
        try
        {
            // Perceptual intent for display of a press CMYK profile: maps the full dynamic range
            // (incl. a realistic black point) rather than clipping darks to media-white as
            // RelativeColorimetric does. Matches Adobe's SWOP display (ICC.1:2022 §6). Falls back
            // to A2B0/B2A0 inside ICCSharp if the intent table is absent.
            var opts = new ICCSharp.TransformOptions
            {
                Intent = ICCSharp.Profile.RenderingIntent.Perceptual,
            };
            _toRgb = IccTransform.Create(cmykProfile, BuiltInProfiles.Srgb, opts);
            _toCmyk = IccTransform.Create(BuiltInProfiles.Srgb, cmykProfile, opts);
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics,
                $"DeviceCmykConverter: ICC transform build failed ({ex.GetType().Name}: {ex.Message}); using naive fallback.");
            _toRgb = null;
            _toCmyk = null;
        }
    }

    private static readonly object DefaultLock = new();
    private static DeviceCmykConverter? _default;
    private static IccProfile? _defaultProfile;

    /// <summary>
    /// Converter bound to <see cref="CmykProfileProvider.Default"/>'s active profile. Rebuilt
    /// automatically when that profile changes (e.g. the override path is set).
    /// </summary>
    public static DeviceCmykConverter Default
    {
        get
        {
            lock (DefaultLock)
            {
                IccProfile current = CmykProfileProvider.Default.GetProfile();
                if (_default is null || !ReferenceEquals(current, _defaultProfile))
                {
                    _default = new DeviceCmykConverter(current);
                    _defaultProfile = current;
                }
                return _default;
            }
        }
    }

    /// <summary>CMYK (each 0..1) → 8-bit sRGB.</summary>
    public (byte R, byte G, byte B) ToRgb(double c, double m, double y, double k)
    {
        if (_toRgb is null) return NaiveCmykToRgb(c, m, y, k);
        Span<double> input = stackalloc double[4] { Clamp01(c), Clamp01(m), Clamp01(y), Clamp01(k) };
        Span<double> output = stackalloc double[3];
        _toRgb.Apply(input, output);
        return (ToByte(output[0]), ToByte(output[1]), ToByte(output[2]));
    }

    /// <summary>sRGB (each 0..1) → CMYK (each 0..1).</summary>
    public (double C, double M, double Y, double K) ToCmyk(double r, double g, double b)
    {
        if (_toCmyk is null) return NaiveRgbToCmyk(r, g, b);
        Span<double> input = stackalloc double[3] { Clamp01(r), Clamp01(g), Clamp01(b) };
        Span<double> output = stackalloc double[4];
        _toCmyk.Apply(input, output);
        return (Clamp01(output[0]), Clamp01(output[1]), Clamp01(output[2]), Clamp01(output[3]));
    }

    /// <summary>Bulk forward: <paramref name="cmyk"/> is N×4 (0..1); <paramref name="rgb"/> receives N×3 bytes.</summary>
    public void ToRgbMany(ReadOnlySpan<double> cmyk, Span<byte> rgb)
    {
        if (cmyk.Length % 4 != 0)
            throw new ArgumentException("cmyk length must be a multiple of 4.", nameof(cmyk));
        int pixels = cmyk.Length / 4;
        if (rgb.Length < pixels * 3)
            throw new ArgumentException($"rgb buffer too short: need {pixels * 3}, got {rgb.Length}.", nameof(rgb));

        if (_toRgb is null)
        {
            for (var i = 0; i < pixels; i++)
            {
                (byte r, byte g, byte b) = NaiveCmykToRgb(cmyk[i*4], cmyk[i*4+1], cmyk[i*4+2], cmyk[i*4+3]);
                rgb[i*3] = r; rgb[i*3+1] = g; rgb[i*3+2] = b;
            }
            return;
        }

        var inBuf = new double[cmyk.Length];
        for (var i = 0; i < cmyk.Length; i++) inBuf[i] = Clamp01(cmyk[i]);
        var outBuf = new double[pixels * 3];
        _toRgb.ApplyMany(inBuf, outBuf);
        for (var i = 0; i < pixels * 3; i++) rgb[i] = ToByte(outBuf[i]);
    }

    /// <summary>Bulk inverse: <paramref name="rgb"/> is N×3 (0..1); <paramref name="cmyk"/> receives N×4 (0..1).</summary>
    public void ToCmykMany(ReadOnlySpan<double> rgb, Span<double> cmyk)
    {
        if (rgb.Length % 3 != 0)
            throw new ArgumentException("rgb length must be a multiple of 3.", nameof(rgb));
        int pixels = rgb.Length / 3;
        if (cmyk.Length < pixels * 4)
            throw new ArgumentException($"cmyk buffer too short: need {pixels * 4}, got {cmyk.Length}.", nameof(cmyk));

        if (_toCmyk is null)
        {
            for (var i = 0; i < pixels; i++)
            {
                (double c, double m, double y, double k) = NaiveRgbToCmyk(rgb[i*3], rgb[i*3+1], rgb[i*3+2]);
                cmyk[i*4] = c; cmyk[i*4+1] = m; cmyk[i*4+2] = y; cmyk[i*4+3] = k;
            }
            return;
        }

        var inBuf = new double[rgb.Length];
        for (var i = 0; i < rgb.Length; i++) inBuf[i] = Clamp01(rgb[i]);
        var outBuf = new double[pixels * 4];
        _toCmyk.ApplyMany(inBuf, outBuf);
        for (var i = 0; i < pixels * 4; i++) cmyk[i] = Clamp01(outBuf[i]);
    }

    /// <summary>Naive DeviceCMYK→RGB (the legacy formula); also the forward fallback.</summary>
    internal static (byte R, byte G, byte B) NaiveCmykToRgb(double c, double m, double y, double k) =>
        (ToByte(1 - Math.Min(1, c * (1 - k) + k)),
         ToByte(1 - Math.Min(1, m * (1 - k) + k)),
         ToByte(1 - Math.Min(1, y * (1 - k) + k)));

    /// <summary>Naive sRGB→DeviceCMYK; also the inverse fallback.</summary>
    internal static (double C, double M, double Y, double K) NaiveRgbToCmyk(double r, double g, double b)
    {
        double k = 1 - Math.Max(r, Math.Max(g, b));
        if (k >= 1.0 - 1e-9) return (0, 0, 0, 1);
        double c = (1 - r - k) / (1 - k);
        double m = (1 - g - k) / (1 - k);
        double y = (1 - b - k) / (1 - k);
        return (Clamp01(c), Clamp01(m), Clamp01(y), Clamp01(k));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static byte ToByte(double v) => (byte)Math.Round(Clamp01(v) * 255.0);
}
