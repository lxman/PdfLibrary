using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Functions;

/// <summary>
/// Type 0 (Sampled) function - samples stored in a stream, with interpolation.
/// </summary>
public class SampledFunction : PdfFunction
{
    private readonly int[] _size;
    private readonly int _bitsPerSample;
    private readonly double[] _encode;
    private readonly double[] _decode;
    private readonly double[,] _samples; // [sampleIndex, outputIndex]

    private SampledFunction(double[] domain, double[]? range, int[] size, int bitsPerSample,
        double[] encode, double[] decode, double[,] samples)
    {
        Domain = domain;
        Range = range;
        _size = size;
        _bitsPerSample = bitsPerSample;
        _encode = encode;
        _decode = decode;
        _samples = samples;
    }

    public static SampledFunction? Create(PdfStream? stream, double[] domain, double[]? range)
    {
        if (stream is null || range is null)
            return null;

        PdfDictionary dict = stream.Dictionary;

        // Size array (required) - number of samples in each input dimension
        int[]? size = ParseIntArray(dict, "Size");
        if (size is null || size.Length == 0)
            return null;

        // BitsPerSample (required) - 1, 2, 4, 8, 12, 16, 24, or 32
        if (!dict.TryGetValue(new PdfName("BitsPerSample"), out PdfObject bpsObj) || bpsObj is not PdfInteger bpsInt)
            return null;
        int bitsPerSample = bpsInt.Value;

        int inputCount = domain.Length / 2;
        int outputCount = range.Length / 2;

        // Encode array (optional) - maps input to sample index
        // Default: [0 (Size[0]-1) 0 (Size[1]-1) ...]
        double[]? encode = ParseNumberArray(dict, "Encode");
        if (encode is null || encode.Length != inputCount * 2)
        {
            encode = new double[inputCount * 2];
            for (var i = 0; i < inputCount; i++)
            {
                encode[i * 2] = 0;
                encode[i * 2 + 1] = size[i] - 1;
            }
        }

        // Decode array (optional) - maps sample value to output
        // Default: same as Range
        double[]? decode = ParseNumberArray(dict, "Decode");
        if (decode is null || decode.Length != outputCount * 2)
        {
            decode = new double[range.Length];
            Array.Copy(range, decode, range.Length);
        }

        // Calculate the total number of samples
        int totalSamples = size.Aggregate(1, (current, s) => current * s);

        // Decode the stream data
        byte[] data = stream.GetDecodedData();

        // Parse samples from the stream
        var samples = new double[totalSamples, outputCount];
        int maxSampleValue = (1 << bitsPerSample) - 1;

        var bitPosition = 0;
        for (var sampleIdx = 0; sampleIdx < totalSamples; sampleIdx++)
        {
            for (var outputIdx = 0; outputIdx < outputCount; outputIdx++)
            {
                // Read sample value based on bits per sample
                int sampleValue = ReadSample(data, ref bitPosition, bitsPerSample);

                // Decode sample value to output range
                double normalizedSample = (double)sampleValue / maxSampleValue;
                double decodeMin = decode[outputIdx * 2];
                double decodeMax = decode[outputIdx * 2 + 1];
                samples[sampleIdx, outputIdx] = Interpolate(normalizedSample, 0, 1, decodeMin, decodeMax);
            }
        }

        return new SampledFunction(domain, range, size, bitsPerSample, encode, decode, samples);
    }

    private static int ReadSample(byte[] data, ref int bitPosition, int bitsPerSample)
    {
        int byteIndex = bitPosition / 8;
        int bitOffset = bitPosition % 8;
        bitPosition += bitsPerSample;

        if (byteIndex >= data.Length)
            return 0;

        var result = 0;
        int bitsRemaining = bitsPerSample;

        while (bitsRemaining > 0)
        {
            if (byteIndex >= data.Length)
                break;

            int bitsAvailable = 8 - bitOffset;
            int bitsToRead = Math.Min(bitsAvailable, bitsRemaining);

            int mask = (1 << bitsToRead) - 1;
            int shift = bitsAvailable - bitsToRead;
            int bits = (data[byteIndex] >> shift) & mask;

            result = (result << bitsToRead) | bits;
            bitsRemaining -= bitsToRead;

            byteIndex++;
            bitOffset = 0;
        }

        return result;
    }

    public override double[] Evaluate(double[] input)
    {
        if (Range is null)
            return [];

        int inputCount = InputCount;
        int outputCount = OutputCount;

        // For 1D function (most common for tint transforms)
        if (inputCount == 1 && _size.Length == 1)
        {
            // Clamp input to domain
            double x = Clamp(input[0], Domain[0], Domain[1]);

            // Encode to sample index
            double encodedX = Interpolate(x, Domain[0], Domain[1], _encode[0], _encode[1]);

            // Clamp to valid sample range
            encodedX = Clamp(encodedX, 0, _size[0] - 1);

            // Get sample indices for interpolation
            var idx0 = (int)Math.Floor(encodedX);
            int idx1 = Math.Min(idx0 + 1, _size[0] - 1);
            double frac = encodedX - idx0;

            // Interpolate outputs
            var result = new double[outputCount];
            for (var i = 0; i < outputCount; i++)
            {
                double v0 = _samples[idx0, i];
                double v1 = _samples[idx1, i];
                double value = v0 + frac * (v1 - v0);

                // Clamp to range
                result[i] = Clamp(value, Range[i * 2], Range[i * 2 + 1]);
            }
            return result;
        }

        // Multi-dimensional interpolation (not fully implemented, returns nearest sample)
        var indices = new int[inputCount];
        for (var i = 0; i < inputCount; i++)
        {
            double x = Clamp(input[i], Domain[i * 2], Domain[i * 2 + 1]);
            double encoded = Interpolate(x, Domain[i * 2], Domain[i * 2 + 1], _encode[i * 2], _encode[i * 2 + 1]);
            indices[i] = (int)Clamp(Math.Round(encoded), 0, _size[i] - 1);
        }

        // Calculate linear index from multi-dimensional indices
        var linearIndex = 0;
        var multiplier = 1;
        for (int i = inputCount - 1; i >= 0; i--)
        {
            linearIndex += indices[i] * multiplier;
            multiplier *= _size[i];
        }

        // Get output values
        var output = new double[outputCount];
        for (var i = 0; i < outputCount; i++)
        {
            output[i] = linearIndex < _samples.GetLength(0) ? _samples[linearIndex, i] : 0;
            if (Range is not null)
                output[i] = Clamp(output[i], Range[i * 2], Range[i * 2 + 1]);
        }

        return output;
    }
}
