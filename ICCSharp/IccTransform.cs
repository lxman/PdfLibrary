using System;
using System.IO;
using ICCSharp.Eval;
using ICCSharp.Profile;
using ICCSharp.Transform;

namespace ICCSharp;

/// <summary>
/// Public color-transform entry point. Construct via <see cref="Create(IccProfile, IccProfile, TransformOptions?)"/>
/// or one of the convenience overloads that read profile bytes from disk or memory.
///
/// <para>Single-pixel: <see cref="Apply(ReadOnlySpan{double}, Span{double})"/>.</para>
/// <para>Batch: <see cref="ApplyMany(ReadOnlySpan{double}, Span{double})"/> — processes contiguous arrays
/// of N×<see cref="InputChannels"/> samples in one call.</para>
/// </summary>
public sealed class IccTransform : IColorTransform
{
    public IccProfile Source => _inner.Source;
    public IccProfile Destination => _inner.Destination;
    public TransformOptions Options { get; }

    public int InputChannels => _inner.InputChannels;
    public int OutputChannels => _inner.OutputChannels;

    private readonly IccTwoProfileTransform _inner;

    private IccTransform(IccTwoProfileTransform inner, TransformOptions options)
    {
        _inner = inner;
        Options = options;
    }

    public static IccTransform Create(IccProfile source, IccProfile destination, TransformOptions? options = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        options ??= new TransformOptions();

        IccTwoProfileTransform inner = new(source, destination, options.Intent, options.BlackPointCompensation);
        return new IccTransform(inner, options);
    }

    /// <summary>Convenience overload: parses profiles from in-memory byte arrays.</summary>
    public static IccTransform Create(byte[] sourceProfile, byte[] destinationProfile, TransformOptions? options = null)
    {
        if (sourceProfile is null) throw new ArgumentNullException(nameof(sourceProfile));
        if (destinationProfile is null) throw new ArgumentNullException(nameof(destinationProfile));
        return Create(IccProfile.Parse(sourceProfile), IccProfile.Parse(destinationProfile), options);
    }

    /// <summary>Convenience overload: reads profiles from disk.</summary>
    public static IccTransform Create(string sourcePath, string destinationPath, TransformOptions? options = null)
    {
        if (sourcePath is null) throw new ArgumentNullException(nameof(sourcePath));
        if (destinationPath is null) throw new ArgumentNullException(nameof(destinationPath));
        return Create(File.ReadAllBytes(sourcePath), File.ReadAllBytes(destinationPath), options);
    }

    /// <summary>Single-pixel forward transform.</summary>
    public void Apply(ReadOnlySpan<double> input, Span<double> output) => _inner.Apply(input, output);

    /// <summary>
    /// Allocating single-pixel convenience overload. Suitable for one-off calls; for image
    /// processing prefer <see cref="ApplyMany(ReadOnlySpan{double}, Span{double})"/> to avoid per-call allocation.
    /// </summary>
    public double[] Apply(params double[] input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        var output = new double[OutputChannels];
        Apply(input, output);
        return output;
    }

    /// <summary>
    /// Batch transform: <paramref name="inputs"/> contains <c>n × InputChannels</c> samples laid
    /// out per-pixel; <paramref name="outputs"/> receives <c>n × OutputChannels</c> samples in
    /// the same per-pixel order. Single allocation amortized across <c>n</c> pixels.
    /// </summary>
    public void ApplyMany(ReadOnlySpan<double> inputs, Span<double> outputs)
    {
        int inStride = InputChannels;
        int outStride = OutputChannels;
        if (inputs.Length % inStride != 0)
            throw new ArgumentException(
                $"Input length {inputs.Length} is not a multiple of {inStride} channels.", nameof(inputs));

        int pixels = inputs.Length / inStride;
        long needed = (long)pixels * outStride;
        if (outputs.Length < needed)
            throw new ArgumentException(
                $"Output buffer too short: need {needed} samples for {pixels} pixels, got {outputs.Length}.",
                nameof(outputs));

        for (var i = 0; i < pixels; i++)
        {
            ReadOnlySpan<double> pixIn = inputs.Slice(i * inStride, inStride);
            Span<double> pixOut = outputs.Slice(i * outStride, outStride);
            _inner.Apply(pixIn, pixOut);
        }
    }
}
