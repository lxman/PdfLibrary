using System;
using JpegCodec.Stream;

namespace JpegCodec.Tests.Encode;

public class EncoderEndToEndTests
{
    [Fact]
    public void Encode_GrayscaleSolid_DecodesBack()
    {
        const int W = 16, H = 16;
        var data = new byte[W * H];
        Array.Fill(data, (byte)128);

        byte[] jpeg = new JpegStreamEncoder().Encode(data,
            new JpegEncodeOptions { Width = W, Height = H, NumberOfComponents = 1, Quality = 90 });

        // Sanity: SOI ... EOI markers.
        Assert.True(jpeg.Length > 4);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Equal(0xFF, jpeg[^2]);
        Assert.Equal(0xD9, jpeg[^1]);

        var info = new JpegStreamDecoder().Identify(jpeg);
        Assert.Equal(W, info.Width);
        Assert.Equal(H, info.Height);
        Assert.Equal(1, info.NumberOfComponents);
        Assert.Equal(JpegMarker.Sof0, info.StartOfFrame);

        var result = new JpegStreamDecoder().Decode(jpeg);
        Assert.Equal(W * H, result.ComponentData.Length);
        for (var i = 0; i < result.ComponentData.Length; i++)
        {
            // 128 round-trip — quantization may shift slightly.
            int delta = Math.Abs(result.ComponentData[i] - 128);
            Assert.True(delta <= 5, $"Pixel {i}: got {result.ComponentData[i]}, expected ≈128");
        }
    }

    [Fact]
    public void Encode_RgbRamp_RoundTripHighQuality_PsnrAcceptable()
    {
        const int W = 32, H = 32;
        var data = new byte[W * H * 3];
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                int i = (y * W + x) * 3;
                data[i + 0] = (byte)(x * 8);          // R-like ramp
                data[i + 1] = (byte)(y * 8);          // G-like ramp
                data[i + 2] = (byte)((x + y) * 4);    // B-like
            }
        }

        byte[] jpeg = new JpegStreamEncoder().Encode(data,
            new JpegEncodeOptions { Width = W, Height = H, NumberOfComponents = 3, Quality = 95 });

        var result = new JpegStreamDecoder().Decode(jpeg);
        Assert.Equal(3, result.NumberOfComponents);
        Assert.Equal(W * H * 3, result.ComponentData.Length);

        double sumSqErr = 0;
        for (var i = 0; i < data.Length; i++)
        {
            int d = result.ComponentData[i] - data[i];
            sumSqErr += d * d;
        }
        double mse = sumSqErr / data.Length;
        double psnr = mse == 0 ? double.PositiveInfinity : 10.0 * Math.Log10(255.0 * 255.0 / mse);

        Assert.True(psnr > 30.0,
            $"Encode/decode round-trip PSNR={psnr:F2} dB is below the 30 dB Q=95 floor.");
    }

    [Fact]
    public void Encode_EmitsSof0_DqtDht_InCorrectOrder()
    {
        var data = new byte[8 * 8];
        Array.Fill(data, (byte)100);

        byte[] jpeg = new JpegStreamEncoder().Encode(data,
            new JpegEncodeOptions { Width = 8, Height = 8, NumberOfComponents = 1, Quality = 80 });

        // Walk markers, accumulate the sequence.
        var seq = new System.Collections.Generic.List<JpegMarker>();
        var reader = new JpegMarkerReader(jpeg);
        while (reader.TryReadMarker(out var m))
        {
            seq.Add(m);
            if (m == JpegMarker.Eoi) break;
            if (m == JpegMarker.Sos)
            {
                // Skip the SOS payload + entropy data — we only care about
                // the marker sequence up to SOS.
                break;
            }
            if (!JpegMarkerReader.IsStandalone(m))
            {
                int len = reader.ReadPayloadLength();
                reader.Skip(len);
            }
        }

        Assert.Contains(JpegMarker.Soi, seq);
        Assert.Contains(JpegMarker.Dqt, seq);
        Assert.Contains(JpegMarker.Sof0, seq);
        Assert.Contains(JpegMarker.Dht, seq);
        Assert.Contains(JpegMarker.Sos, seq);

        Assert.True(seq.IndexOf(JpegMarker.Soi) < seq.IndexOf(JpegMarker.Dqt));
        Assert.True(seq.IndexOf(JpegMarker.Dqt) < seq.IndexOf(JpegMarker.Sof0));
        Assert.True(seq.IndexOf(JpegMarker.Sof0) < seq.IndexOf(JpegMarker.Dht));
        Assert.True(seq.IndexOf(JpegMarker.Dht) < seq.IndexOf(JpegMarker.Sos));
    }

    [Fact]
    public void Encode_CmykWithAdobeMarker_EmitsApp14()
    {
        const int W = 8, H = 8;
        var data = new byte[W * H * 4];
        Array.Fill(data, (byte)64);

        byte[] jpeg = new JpegStreamEncoder().Encode(data,
            new JpegEncodeOptions
            {
                Width = W, Height = H, NumberOfComponents = 4, Quality = 85,
                EmitAdobeMarker = true, AdobeColorTransform = 0, EmitJfif = false,
            });

        var info = new JpegStreamDecoder().Identify(jpeg);
        Assert.Equal(4, info.NumberOfComponents);
        Assert.True(info.HasAdobeMarker);
        Assert.Equal(0, info.AdobeColorTransform);
    }
}
