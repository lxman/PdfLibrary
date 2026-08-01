using ICCSharp;
using ICCSharp.Profile;
using Logging;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Resolves a single proofing CMYK destination — the document's <c>/OutputIntents</c> CMYK
/// <c>/DestOutputProfile</c> when present, else <see cref="CmykProfileProvider.Default"/> — and
/// converts arbitrary ICC-managed or Lab source colour to it. Used to colour-manage source content
/// (ICCBased colour, /Lab colour, embedded-profile images) into the document's proofing CMYK target
/// rather than leaving it in its source space.
///
/// Everything here fails soft: a malformed destination or source profile, a channel-count mismatch,
/// or any other exception yields <see langword="null"/> (or, for the ctor, <see cref="HasTarget"/> =
/// false) rather than throwing, so callers can fall back to their existing conversion path.
/// </summary>
internal sealed class ProofCmykResolver
{
    private readonly PdfDocument? _document;
    private readonly IccProfile? _destination;

    // Keyed by (ICC-profile stream reference, mapped ICC intent) — the per-intent widening of
    // IccColorConverter._cache's key discipline; a failed parse is cached as null per intent so
    // repeated calls on a bad stream don't re-parse (or re-log) every time.
    private readonly Dictionary<(PdfStream Stream, RenderingIntent Intent), IccTransform?> _iccCache = new();

    // One PCS-Lab→destination transform per intent; null = attempted and failed for that intent.
    private readonly Dictionary<RenderingIntent, IccPcsLabTransform?> _labTransforms = new();

    public ProofCmykResolver(PdfDocument? document)
    {
        _document = document;
        try
        {
            _destination = ResolveDestination(document);
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"ProofCmykResolver FAIL: {ex.GetType().Name}: {ex.Message}");
            _destination = null;
        }
    }

    /// <summary>True when a usable CMYK destination profile was resolved (document OutputIntent or
    /// the process-wide provider fallback); false only when both failed to parse as CMYK.</summary>
    public bool HasTarget => _destination is not null;

    private static IccProfile? ResolveDestination(PdfDocument? document)
    {
        if (document is not null)
        {
            foreach (OutputIntentDescriptor intent in document.GetOutputIntents())
            {
                if (!intent.HasDestProfile || intent.ColorSpace != OutputIntentColorSpace.Cmyk) continue;

                byte[]? bytes = intent.GetDestProfileBytes();
                if (bytes is null) continue;

                try
                {
                    IccProfile profile = IccProfile.Parse(bytes);
                    if (profile.Header.DataColorSpace == ColorSpaceSignatures.CMYK) return profile;
                }
                catch (Exception ex)
                {
                    PdfLogger.Log(LogCategory.Graphics,
                        $"ProofCmykResolver: OutputIntent dest profile failed to parse ({ex.GetType().Name}: {ex.Message}); falling back.");
                }
                // Not CMYK (or failed to parse) — stop at this first qualifying intent rather than
                // scanning the rest (first-match short-circuit; row 6-3 documents multi-intent
                // selection as a gap) and fall through to the provider default below.
                break;
            }
        }

        return CmykProfileProvider.Default.GetProfile();
    }

    /// <summary>
    /// Converts a single sample carried by an ICC-managed source colour space (<paramref
    /// name="iccStream"/>'s embedded profile) to the proofing CMYK destination. Returns
    /// <see langword="null"/> when there is no destination, the component count is below 3
    /// (excludes N=1 gray sources — not this resolver's job), the source profile's channel count
    /// doesn't match, or the source profile fails to parse.
    /// </summary>
    public double[]? TryIccToProofCmyk(PdfStream iccStream, IReadOnlyList<double> components, string? renderingIntent = null)
    {
        if (!HasTarget) return null;
        if (iccStream is null) throw new ArgumentNullException(nameof(iccStream));
        if (components is null) throw new ArgumentNullException(nameof(components));
        if (components.Count < 3) return null;

        try
        {
            RenderingIntent intent = PdfRenderingIntents.Map(renderingIntent);
            IccTransform? transform = GetOrCreateTransform(iccStream, intent);
            if (transform is null) return null;
            if (components.Count != transform.InputChannels) return null;

            var input = new double[components.Count];
            for (var i = 0; i < components.Count; i++) input[i] = components[i];

            double[] output = transform.Apply(input);
            for (var i = 0; i < output.Length; i++) output[i] = Clamp01(output[i]);
            return output;
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"ProofCmykResolver ICC FAIL: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Converts a PDF <c>/Lab</c> sample (L* 0..100, a*/b* signed) directly to the proofing CMYK
    /// destination via the destination profile's PCS→device leg. Returns <see langword="null"/> when
    /// there is no destination or the destination's from-PCS leg can't be built.
    /// </summary>
    public double[]? TryLabToProofCmyk(double l, double a, double b, string? renderingIntent = null)
    {
        if (!HasTarget) return null;

        try
        {
            RenderingIntent intent = PdfRenderingIntents.Map(renderingIntent);
            IccPcsLabTransform? transform = GetOrCreateLabTransform(intent);
            if (transform is null) return null;

            Span<double> lab = stackalloc double[3]
            {
                Math.Clamp(l, 0.0, 100.0),
                Math.Clamp(a, -128.0, 127.0),
                Math.Clamp(b, -128.0, 127.0),
            };
            Span<double> deviceOut = stackalloc double[transform.OutputChannels];
            transform.Apply(lab, deviceOut);

            var output = new double[deviceOut.Length];
            for (var i = 0; i < deviceOut.Length; i++) output[i] = Clamp01(deviceOut[i]);
            return output;
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"ProofCmykResolver Lab FAIL: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Bulk-converts an interleaved byte buffer (<paramref name="componentsPerPixel"/> bytes per
    /// pixel, <paramref name="pixelCount"/> pixels) through <paramref name="iccStream"/>'s embedded
    /// profile to a freshly-allocated CMYK-byte buffer (<c>pixelCount × 4</c>). Returns
    /// <see langword="null"/> on any failure (no destination, &lt;3 components/pixel, channel-count
    /// mismatch, bad profile, buffer-length mismatch).
    /// </summary>
    public byte[]? TryIccImageToProofCmyk(PdfStream iccStream, byte[] samples, int componentsPerPixel, int pixelCount, string? renderingIntent = null)
    {
        if (!HasTarget) return null;
        if (iccStream is null) throw new ArgumentNullException(nameof(iccStream));
        if (samples is null) throw new ArgumentNullException(nameof(samples));
        if (componentsPerPixel < 3) return null;

        try
        {
            RenderingIntent intent = PdfRenderingIntents.Map(renderingIntent);
            IccTransform? transform = GetOrCreateTransform(iccStream, intent);
            if (transform is null) return null;
            if (transform.InputChannels != componentsPerPixel) return null;
            if (samples.Length != componentsPerPixel * pixelCount) return null;

            var inputs = new double[samples.Length];
            for (var i = 0; i < samples.Length; i++) inputs[i] = samples[i] / 255.0;

            var outputs = new double[pixelCount * transform.OutputChannels];
            transform.ApplyMany(inputs, outputs);

            var result = new byte[outputs.Length];
            for (var i = 0; i < outputs.Length; i++)
                result[i] = ToByte(outputs[i]);
            return result;
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"ProofCmykResolver image FAIL: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private IccTransform? GetOrCreateTransform(PdfStream iccStream, RenderingIntent intent)
    {
        (PdfStream, RenderingIntent) key = (iccStream, intent);
        if (_iccCache.TryGetValue(key, out IccTransform? cached))
            return cached;

        IccTransform? transform = null;
        try
        {
            byte[] profileBytes = iccStream.GetDecodedData(_document?.Decryptor);
            IccProfile profile = IccProfile.Parse(profileBytes);
            transform = IccTransform.Create(profile, _destination!,
                new TransformOptions { Intent = intent });
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"ProofCmykResolver ICC parse FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        _iccCache[key] = transform;
        return transform;
    }

    private IccPcsLabTransform? GetOrCreateLabTransform(RenderingIntent intent)
    {
        if (_labTransforms.TryGetValue(intent, out IccPcsLabTransform? cached))
            return cached;

        IccPcsLabTransform? transform = null;
        try
        {
            transform = IccPcsLabTransform.Create(_destination!,
                new TransformOptions { Intent = intent });
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Graphics, $"ProofCmykResolver Lab transform FAIL: {ex.GetType().Name}: {ex.Message}");
        }

        _labTransforms[intent] = transform;
        return transform;
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    private static byte ToByte(double v)
    {
        if (v <= 0) return 0;
        if (v >= 1) return 255;
        return (byte)Math.Round(v * 255.0);
    }
}
