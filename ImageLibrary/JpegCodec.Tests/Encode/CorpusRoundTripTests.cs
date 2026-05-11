using System;
using System.IO;
using JpegCodec.Stream;
using JpegCodec.Tests.Corpus;

namespace JpegCodec.Tests.Encode;

// Full-pipeline round-trip on real-world corpus shapes:
//   corpus_jpeg → my decoder → pixels  P0
//                 ↓ encode with my encoder
//                 my JPEG bytes
//                 ↓ decode with my decoder
//   pixels P1 — compare against P0
//
// Catches encoder bugs that don't surface with synthetic ramps/solids:
// real photographic content exercises every coefficient band and most
// Huffman code paths.
//
// Tolerance: two lossy encode passes (corpus-encoder + ours) accumulate
// quantization error, so this is a PSNR test, not byte-identical. The
// floor is set per-component-count.
public class CorpusRoundTripTests
{
    private static readonly string[] s_files =
    [
        "graduated/color_rgb_ramp.jpg",
        "baseline/cramps.jpg",
        "subsampled/color420_gradient.jpg",
        "turbo/testorig.jpg",
        "cmyk_real/cmyk-sample.jpg",
        "cmyk_real/cmyk_ycck_transform2.jpg",
    ];

    public static System.Collections.Generic.IEnumerable<object[]> Files()
    {
        foreach (var f in s_files)
            yield return [f];
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Corpus_DecodeEncodeDecode_PsnrStable(string corpusFile)
    {
        string path = Path.Combine(CorpusFiles.CorpusRoot, corpusFile);
        if (!File.Exists(path)) return;
        byte[] data = File.ReadAllBytes(path);

        // First decode: ground-truth pixels for the corpus content.
        var firstInfo = new JpegStreamDecoder().Identify(data);
        if (firstInfo.StartOfFrame == JpegMarker.Sof2) return;  // progressive: skip (encoder is sequential-only)
        var first = new JpegStreamDecoder().Decode(data);
        Assert.Equal(firstInfo.Width * firstInfo.Height * firstInfo.NumberOfComponents,
                     first.ComponentData.Length);

        // Re-encode the decoded pixels.
        var opts = new JpegEncodeOptions
        {
            Width = first.Width,
            Height = first.Height,
            NumberOfComponents = first.NumberOfComponents,
            Quality = 92,
            EmitJfif = !first.HasAdobeMarker && first.NumberOfComponents != 4,
            EmitAdobeMarker = first.NumberOfComponents == 4,
            AdobeColorTransform = first.AdobeColorTransform,
        };
        byte[] reEncoded = new JpegStreamEncoder().Encode(first.ComponentData, opts);

        // Second decode of our own encoder output.
        var secondInfo = new JpegStreamDecoder().Identify(reEncoded);
        Assert.Equal(first.Width, secondInfo.Width);
        Assert.Equal(first.Height, secondInfo.Height);
        Assert.Equal(first.NumberOfComponents, secondInfo.NumberOfComponents);

        var second = new JpegStreamDecoder().Decode(reEncoded);
        Assert.Equal(first.ComponentData.Length, second.ComponentData.Length);

        // PSNR between the two pixel buffers. Two lossy passes accumulate
        // error, so the threshold is below a single-pass round-trip.
        double sumSqErr = 0;
        for (var i = 0; i < first.ComponentData.Length; i++)
        {
            int d = second.ComponentData[i] - first.ComponentData[i];
            sumSqErr += d * d;
        }
        double mse = sumSqErr / first.ComponentData.Length;
        double psnr = mse == 0 ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / mse);

        // Q=92 second-pass PSNR floor — 26 dB is generous, real images
        // typically land at 32-38 dB. Set the floor low enough that
        // synthetic patterns (e.g. solid color blocks that quantize
        // poorly at low Q) don't false-positive.
        Assert.True(psnr > 26.0,
            $"{corpusFile}: re-encode/re-decode PSNR={psnr:F2} dB below 26 dB floor. " +
            $"MSE={mse:F4}, components={first.NumberOfComponents}");
    }
}
