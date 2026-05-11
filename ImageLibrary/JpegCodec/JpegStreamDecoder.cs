using System;
using JpegCodec.Decode;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec;

public sealed class JpegStreamDecoder
{
    public JpegImageInfo Identify(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        var state = new DecoderState();
        WalkAndCollect(data, state, stopAtFirstSos: true);
        if (state.Frame is null)
            throw new InvalidOperationException("No SOF marker found before end of stream.");
        return new JpegImageInfo
        {
            Width = state.Frame.Width,
            Height = state.Frame.Height,
            NumberOfComponents = state.Frame.NumberOfComponents,
            Precision = state.Frame.Precision,
            StartOfFrame = state.Frame.Marker,
            HasAdobeMarker = state.HasAdobe,
            AdobeColorTransform = state.AdobeColorTransform,
            HasJfif = state.HasJfif,
        };
    }

    public JpegDecodeResult Decode(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        var state = new DecoderState();
        WalkAndCollect(data, state, stopAtFirstSos: false);

        if (state.Frame is null)
            throw new InvalidOperationException("No SOF marker found before EOI.");

        byte[][] componentRasters;
        if (state.Frame.Marker == JpegMarker.Sof2)
        {
            if (state.Progressive is null)
                throw new InvalidOperationException("Progressive frame without scans.");
            componentRasters = state.Progressive.FinalizeRasters(state.QuantTables);
        }
        else
        {
            if (state.SequentialRasters is null)
                throw new InvalidOperationException("Sequential frame without scan data.");
            componentRasters = state.SequentialRasters;
        }

        byte[] interleaved = InterleaveAndUpsample(state.Frame, componentRasters, state.MaxH, state.MaxV);

        return new JpegDecodeResult
        {
            ComponentData = interleaved,
            Width = state.Frame.Width,
            Height = state.Frame.Height,
            NumberOfComponents = state.Frame.NumberOfComponents,
            Precision = state.Frame.Precision,
            HasAdobeMarker = state.HasAdobe,
            AdobeColorTransform = state.AdobeColorTransform,
        };
    }

    private sealed class DecoderState
    {
        public FrameHeader? Frame;
        public ushort[]?[] QuantTables = new ushort[]?[4];
        public HuffmanCanonicalTable?[] DcTables = new HuffmanCanonicalTable?[4];
        public HuffmanCanonicalTable?[] AcTables = new HuffmanCanonicalTable?[4];
        public int RestartInterval;
        public bool HasJfif;
        public bool HasAdobe;
        public byte AdobeColorTransform;

        // Sequential output.
        public byte[][]? SequentialRasters;

        // Progressive accumulator.
        public ProgressiveDecoder? Progressive;

        // Max sampling across the frame (cached for upsampling).
        public int MaxH;
        public int MaxV;
    }

    private static void WalkAndCollect(byte[] data, DecoderState state, bool stopAtFirstSos)
    {
        var reader = new JpegMarkerReader(data);
        if (!reader.TryReadMarker(out JpegMarker first) || first != JpegMarker.Soi)
            throw new InvalidOperationException("Stream does not start with SOI marker.");

        int currentOffset = reader.Position;

        while (true)
        {
            // Refresh reader at the current offset (we may have just
            // finished a scan and need to resume from the post-scan
            // position).
            reader = new JpegMarkerReader(data, currentOffset);
            if (!reader.TryReadMarker(out JpegMarker marker)) break;
            currentOffset = reader.Position;

            if (marker == JpegMarker.Eoi) break;

            if (JpegMarkerReader.IsStandalone(marker))
                continue;

            int payloadLength = reader.ReadPayloadLength();

            if (marker == JpegMarker.Sos)
            {
                ReadOnlySpan<byte> sosPayload = reader.ReadPayload(payloadLength);
                var scan = ScanHeader.Parse(sosPayload);
                int scanDataStart = reader.Position;

                if (stopAtFirstSos)
                    return;

                if (state.Frame is null)
                    throw new InvalidOperationException("SOS encountered before SOF.");

                int afterScan;
                if (state.Frame.Marker == JpegMarker.Sof2)
                {
                    state.Progressive ??= new ProgressiveDecoder(state.Frame);
                    state.MaxH = state.Progressive.MaxHorizontalSampling;
                    state.MaxV = state.Progressive.MaxVerticalSampling;
                    var byteSrc = new JpegByteSource(data, scanDataStart);
                    var bitRdr = new JpegBitReader(byteSrc);
                    state.Progressive.DecodeScan(byteSrc, bitRdr, scan, state.DcTables, state.AcTables, state.RestartInterval);
                    afterScan = AdvanceAfterScan(byteSrc);
                }
                else
                {
                    var walker = new McuWalker(
                        state.Frame, scan,
                        state.QuantTables, state.DcTables, state.AcTables,
                        state.RestartInterval);
                    state.MaxH = walker.MaxHorizontalSampling;
                    state.MaxV = walker.MaxVerticalSampling;
                    var byteSrc = new JpegByteSource(data, scanDataStart);
                    var bitRdr = new JpegBitReader(byteSrc);
                    byte[][] rasters = AllocateSequentialRasters(state.Frame, walker);
                    walker.Decode(byteSrc, bitRdr, rasters);
                    state.SequentialRasters = rasters;
                    afterScan = AdvanceAfterScan(byteSrc);
                }

                currentOffset = afterScan;
                continue;
            }

            ReadOnlySpan<byte> payload = reader.ReadPayload(payloadLength);
            currentOffset = reader.Position;

            switch (marker)
            {
                case JpegMarker.Sof0:
                case JpegMarker.Sof1:
                case JpegMarker.Sof2:
                    state.Frame = FrameHeader.Parse(marker, payload);
                    break;

                case JpegMarker.Dqt:
                    foreach (var t in QuantizationTable.ParseAll(payload))
                        state.QuantTables[t.TableId] = t.Values;
                    break;

                case JpegMarker.Dht:
                    foreach (var t in HuffmanTable.ParseAll(payload))
                    {
                        var canonical = HuffmanCanonicalTable.Build(t);
                        if (t.Class == 0) state.DcTables[t.TableId] = canonical;
                        else state.AcTables[t.TableId] = canonical;
                    }
                    break;

                case JpegMarker.Dri:
                    state.RestartInterval = RestartInterval.Parse(payload);
                    break;

                case JpegMarker.App0:
                    if (Jfif.IsJfif(payload)) state.HasJfif = true;
                    break;

                case JpegMarker.App14:
                    if (AdobeApp14.TryParse(payload, out var adobe))
                    {
                        state.HasAdobe = true;
                        state.AdobeColorTransform = adobe!.ColorTransform;
                    }
                    break;

                case JpegMarker.Sof3:
                case JpegMarker.Sof5:
                case JpegMarker.Sof6:
                case JpegMarker.Sof7:
                case JpegMarker.Sof9:
                case JpegMarker.Sof10:
                case JpegMarker.Sof11:
                case JpegMarker.Sof13:
                case JpegMarker.Sof14:
                case JpegMarker.Sof15:
                    throw new NotSupportedException(
                        $"SOF marker {marker} (0x{(byte)marker:X2}) not in v1 scope.");

                default:
                    break;
            }
        }
    }

    private static int AdvanceAfterScan(JpegByteSource byteSource)
    {
        // After an MCU loop ends, the byte source's _atMarker may still
        // be false because the bit reader's accumulator was satisfied
        // without pulling further bytes. Force the source to advance
        // until it finds the next marker.
        while (!byteSource.AtMarker)
        {
            if (byteSource.ReadByte() < 0) break;
        }
        if (!byteSource.AtMarker)
            return byteSource.Position;
        // _position is positioned past the 0xFF and the marker code;
        // back up so the marker walker can re-read it.
        return byteSource.Position - 2;
    }

    private static byte[][] AllocateSequentialRasters(FrameHeader frame, McuWalker walker)
    {
        int nf = frame.NumberOfComponents;
        var rasters = new byte[nf][];
        for (var c = 0; c < nf; c++)
        {
            int rasterW = walker.McusPerLine * 8 * frame.Components[c].HorizontalSampling;
            int rasterH = walker.McusPerColumn * 8 * frame.Components[c].VerticalSampling;
            rasters[c] = new byte[rasterW * rasterH];
        }
        return rasters;
    }

    private static byte[] InterleaveAndUpsample(FrameHeader frame, byte[][] componentRasters, int maxH, int maxV)
    {
        int width = frame.Width;
        int height = frame.Height;
        int nf = frame.NumberOfComponents;

        var output = new byte[width * height * nf];
        var rasterWidths = new int[nf];
        var hSamps = new int[nf];
        var vSamps = new int[nf];
        for (var c = 0; c < nf; c++)
        {
            int rasterW = componentRasters[c].Length /
                          ((height * frame.Components[c].VerticalSampling + maxV - 1) / maxV is var h && h > 0 ? h : 1);
            // Safer: derive raster width from componentRasters length and
            // sampling. Use mcusPerLine * 8 * H(c) — we can compute that
            // from frame dimensions.
            // mcusPerLine = ceil(width / (8 * maxH))
            int mcusPerLine = (width + 8 * maxH - 1) / (8 * maxH);
            rasterWidths[c] = mcusPerLine * 8 * frame.Components[c].HorizontalSampling;
            hSamps[c] = frame.Components[c].HorizontalSampling;
            vSamps[c] = frame.Components[c].VerticalSampling;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int outBase = (y * width + x) * nf;
                for (var c = 0; c < nf; c++)
                {
                    int sx = x * hSamps[c] / maxH;
                    int sy = y * vSamps[c] / maxV;
                    output[outBase + c] = componentRasters[c][sy * rasterWidths[c] + sx];
                }
            }
        }

        return output;
    }
}
