using ICCSharp;
using ICCSharp.Profile;
using Logging;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

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
    private readonly PdfDocument? _document;

    public IccColorConverter(PdfDocument? document = null)
    {
        _document = document;
    }

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
    /// Bulk-converts an interleaved byte buffer — <paramref name="sourceChannels"/> bytes per
    /// sample laid out contiguously in <paramref name="data"/> — through the embedded ICC profile
    /// to a freshly-allocated RGB-byte buffer (<c>sampleCount × 3</c>). Serves both indexed-image
    /// palettes and full image pixel buffers. Returns <see langword="null"/> on failure; the caller
    /// should fall back to a device interpretation of the original bytes.
    /// </summary>
    public byte[]? TryConvertInterleavedToSrgb(PdfStream iccStream, byte[] data, int sourceChannels)
    {
        if (iccStream is null) throw new ArgumentNullException(nameof(iccStream));
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (sourceChannels < 1) throw new ArgumentOutOfRangeException(nameof(sourceChannels));

        IccTransform? transform = GetOrCreate(iccStream);
        if (transform is null) return null;

        if (transform.InputChannels != sourceChannels)
        {
            PdfLogger.Log(LogCategory.Graphics,
                $"ICC BUFFER SKIP: {sourceChannels}-channel samples but profile expects {transform.InputChannels}.");
            return null;
        }
        if (data.Length % sourceChannels != 0)
        {
            PdfLogger.Log(LogCategory.Graphics,
                $"ICC BUFFER SKIP: buffer length {data.Length} not a multiple of {sourceChannels} channels.");
            return null;
        }

        int sampleCount = data.Length / sourceChannels;
        byte[] rgb = new byte[sampleCount * 3];
        Span<double> input = stackalloc double[sourceChannels];
        Span<double> output = stackalloc double[3];

        for (int i = 0; i < sampleCount; i++)
        {
            int srcOff = i * sourceChannels;
            for (int c = 0; c < sourceChannels; c++)
                input[c] = data[srcOff + c] / 255.0;

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
            // The PDF may be encrypted; pass the document's decryptor so the stream is
            // properly decrypted before Flate decompression.
            byte[] profileBytes = iccStream.GetDecodedData(_document?.Decryptor);
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
