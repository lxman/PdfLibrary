using ICCSharp;
using ICCSharp.Profile;
using Logging;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Bridges PDF ICCBased color spaces to a usable device-RGB representation via ICCSharp.
/// Caches a parsed <see cref="IccTransform"/> per ICC profile stream so repeated color lookups
/// on the same profile do not re-parse the bytes.
///
/// Destination is the synthetic sRGB profile from <see cref="BuiltInProfiles.Srgb"/>; output is
/// 3-channel RGB in [0, 1] suitable for downstream rendering as DeviceRGB.
///
/// Failure modes (malformed profile, unsupported PCS path, channel-count mismatch) are reported
/// via logging and surfaced as <see langword="null"/> return values, leaving callers free to fall
/// back to the alternate device color space.
/// </summary>
internal sealed class IccColorConverter
{
    // Keyed by the PdfStream instance — same stream returns the same transform without re-parsing.
    private readonly Dictionary<PdfStream, IccTransform?> _cache = new();

    /// <summary>
    /// Converts <paramref name="sourceColor"/> from the ICC profile carried by
    /// <paramref name="iccStream"/> to device-RGB (sRGB). Returns <see langword="null"/> when the
    /// conversion cannot be performed; the caller should then fall back to the profile's
    /// <c>/Alternate</c> color space.
    /// </summary>
    public double[]? TryConvertToSrgb(PdfStream iccStream, IReadOnlyList<double> sourceColor)
    {
        if (iccStream is null) throw new ArgumentNullException(nameof(iccStream));
        if (sourceColor is null) throw new ArgumentNullException(nameof(sourceColor));

        IccTransform? transform = GetOrCreate(iccStream);
        if (transform is null) return null;

        if (sourceColor.Count != transform.InputChannels)
        {
            PdfLogger.Log(LogCategory.Graphics,
                $"ICC SKIP: source color has {sourceColor.Count} components but profile expects {transform.InputChannels}.");
            return null;
        }

        double[] input = new double[sourceColor.Count];
        for (int i = 0; i < sourceColor.Count; i++) input[i] = sourceColor[i];

        return transform.Apply(input);
    }

    /// <summary>
    /// Bulk-converts a palette of indexed-image entries (each <paramref name="sourceChannels"/>
    /// bytes laid out contiguously in <paramref name="palette"/>) to a freshly-allocated RGB-byte
    /// palette (<c>entryCount × 3</c> bytes). Returns <see langword="null"/> on failure; caller
    /// should fall through with the original palette.
    /// </summary>
    public byte[]? TryConvertPaletteToSrgb(PdfStream iccStream, byte[] palette, int sourceChannels)
    {
        if (iccStream is null) throw new ArgumentNullException(nameof(iccStream));
        if (palette is null) throw new ArgumentNullException(nameof(palette));
        if (sourceChannels < 1) throw new ArgumentOutOfRangeException(nameof(sourceChannels));

        IccTransform? transform = GetOrCreate(iccStream);
        if (transform is null) return null;

        if (transform.InputChannels != sourceChannels)
        {
            PdfLogger.Log(LogCategory.Graphics,
                $"ICC PALETTE SKIP: palette has {sourceChannels}-channel entries but profile expects {transform.InputChannels}.");
            return null;
        }
        if (palette.Length % sourceChannels != 0)
        {
            PdfLogger.Log(LogCategory.Graphics,
                $"ICC PALETTE SKIP: palette length {palette.Length} not a multiple of {sourceChannels} channels.");
            return null;
        }

        int entryCount = palette.Length / sourceChannels;
        byte[] rgb = new byte[entryCount * 3];
        Span<double> input = stackalloc double[sourceChannels];
        Span<double> output = stackalloc double[3];

        for (int i = 0; i < entryCount; i++)
        {
            int srcOff = i * sourceChannels;
            for (int c = 0; c < sourceChannels; c++)
                input[c] = palette[srcOff + c] / 255.0;

            transform.Apply(input, output);

            int dstOff = i * 3;
            rgb[dstOff + 0] = ToByte(output[0]);
            rgb[dstOff + 1] = ToByte(output[1]);
            rgb[dstOff + 2] = ToByte(output[2]);
        }
        return rgb;
    }

    private IccTransform? GetOrCreate(PdfStream iccStream)
    {
        if (_cache.TryGetValue(iccStream, out IccTransform? cached))
            return cached;

        IccTransform? transform = null;
        try
        {
            byte[] profileBytes = iccStream.GetDecodedData();
            IccProfile profile = IccProfile.Parse(profileBytes);
            transform = IccTransform.Create(profile, BuiltInProfiles.Srgb);
            PdfLogger.Log(LogCategory.Graphics,
                $"ICC OK: parsed {profile.Header.Class} profile, {transform.InputChannels} → 3 channels.");
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"ICC FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        _cache[iccStream] = transform;
        return transform;
    }

    private static byte ToByte(double v)
    {
        if (v <= 0) return 0;
        if (v >= 1) return 255;
        return (byte)Math.Round(v * 255.0);
    }
}
