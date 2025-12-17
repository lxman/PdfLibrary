using System;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Reads and parses JPEG file structure (markers and their data).
/// This is Stage 1 of the decoder - marker parsing only.
/// </summary>
internal class JpegReader
{
    private readonly byte[] _data;
    private int _position;

    public JpegReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _position = 0;
    }

    /// <summary>
    /// Parses the JPEG file and returns a JpegFrame with all header information.
    /// </summary>
    public JpegFrame ReadFrame()
    {
        var frame = new JpegFrame();

        // Read SOI marker
        ReadSOI();

        // Read markers until we hit SOS
        while (_position < _data.Length)
        {
            byte marker = ReadMarker();

            switch (marker)
            {
                case JpegMarker.SOF0:
                case JpegMarker.SOF1:
                case JpegMarker.SOF2:
                    ReadSOF(frame, marker);
                    break;

                case JpegMarker.DHT:
                    ReadDHT(frame);
                    break;

                case JpegMarker.DQT:
                    ReadDQT(frame);
                    break;

                case JpegMarker.DRI:
                    ReadDRI(frame);
                    break;

                case JpegMarker.SOS:
                    ReadSOS(frame);
                    // After SOS, we have entropy-coded data until EOI
                    LocateEntropyData(frame);
                    return frame;

                case JpegMarker.EOI:
                    throw new JpegException("Unexpected EOI marker before SOS");

                case >= JpegMarker.APP0 and <= 0xEF:
                    // Skip application markers
                    SkipMarkerSegment();
                    break;

                case JpegMarker.COM:
                    // Skip comment marker
                    SkipMarkerSegment();
                    break;

                default:
                    // Skip unknown markers with length
                    if (!JpegMarker.IsStandalone(marker))
                    {
                        SkipMarkerSegment();
                    }
                    break;
            }
        }

        throw new JpegException("Unexpected end of data - no SOS marker found");
    }

    private void ReadSOI()
    {
        if (_position + 2 > _data.Length)
        {
            throw new JpegException("File too short - missing SOI marker");
        }

        if (_data[_position] != JpegMarker.Prefix || _data[_position + 1] != JpegMarker.SOI)
        {
            throw new JpegException($"Invalid JPEG - expected SOI marker (0xFFD8), got 0x{_data[_position]:X2}{_data[_position + 1]:X2}");
        }

        _position += 2;
    }

    private byte ReadMarker()
    {
        // Skip any padding 0xFF bytes
        while (_position < _data.Length && _data[_position] == JpegMarker.Prefix)
        {
            _position++;
            if (_position < _data.Length && _data[_position] != JpegMarker.Prefix && _data[_position] != 0x00)
            {
                return _data[_position++];
            }
        }

        throw new JpegException($"Invalid marker at position {_position}");
    }

    private void ReadSOF(JpegFrame frame, byte marker)
    {
        int length = ReadUInt16();
        int endPos = _position + length - 2;

        frame.FrameType = marker;
        frame.Precision = _data[_position++];
        frame.Height = ReadUInt16();
        frame.Width = ReadUInt16();

        int componentCount = _data[_position++];
        frame.Components = new JpegComponent[componentCount];

        int maxH = 0, maxV = 0;

        for (var i = 0; i < componentCount; i++)
        {
            var component = new JpegComponent
            {
                Id = _data[_position++],
                HorizontalSamplingFactor = (byte)(_data[_position] >> 4),
                VerticalSamplingFactor = (byte)(_data[_position++] & 0x0F),
                QuantizationTableId = _data[_position++]
            };

            frame.Components[i] = component;

            if (component.HorizontalSamplingFactor > maxH)
            {
                maxH = component.HorizontalSamplingFactor;
            }

            if (component.VerticalSamplingFactor > maxV)
            {
                maxV = component.VerticalSamplingFactor;
            }
        }

        frame.MaxHorizontalSamplingFactor = maxH;
        frame.MaxVerticalSamplingFactor = maxV;

        _position = endPos;
    }

    private void ReadDHT(JpegFrame frame)
    {
        int length = ReadUInt16();
        int endPos = _position + length - 2;

        while (_position < endPos)
        {
            byte tableInfo = _data[_position++];
            int tableClass = tableInfo >> 4;  // 0 = DC, 1 = AC
            int tableId = tableInfo & 0x0F;

            var spec = new HuffmanTableSpec();

            // Read code counts for each bit length (1-16)
            var totalCodes = 0;
            for (var i = 0; i < 16; i++)
            {
                spec.CodeCounts[i] = _data[_position++];
                totalCodes += spec.CodeCounts[i];
            }

            // Read symbol values
            spec.Symbols = new byte[totalCodes];
            for (var i = 0; i < totalCodes; i++)
            {
                spec.Symbols[i] = _data[_position++];
            }

            // Store in appropriate table array
            if (tableClass == 0)
            {
                frame.DcHuffmanTables[tableId] = spec;
            }
            else
            {
                frame.AcHuffmanTables[tableId] = spec;
            }
        }

        _position = endPos;
    }

    private void ReadDQT(JpegFrame frame)
    {
        int length = ReadUInt16();
        int endPos = _position + length - 2;

        while (_position < endPos)
        {
            byte tableInfo = _data[_position++];
            int precision = tableInfo >> 4;  // 0 = 8-bit, 1 = 16-bit
            int tableId = tableInfo & 0x0F;

            var table = new ushort[64];

            if (precision == 0)
            {
                // 8-bit precision
                for (var i = 0; i < 64; i++)
                {
                    table[i] = _data[_position++];
                }
            }
            else
            {
                // 16-bit precision
                for (var i = 0; i < 64; i++)
                {
                    table[i] = ReadUInt16();
                }
            }

            frame.QuantizationTables[tableId] = table;
        }

        _position = endPos;
    }

    private void ReadDRI(JpegFrame frame)
    {
        int length = ReadUInt16();
        if (length != 4)
        {
            throw new JpegException($"Invalid DRI marker length: {length}");
        }

        frame.RestartInterval = ReadUInt16();
    }

    private void ReadSOS(JpegFrame frame)
    {
        int length = ReadUInt16();
        int componentCount = _data[_position++];

        for (var i = 0; i < componentCount; i++)
        {
            byte componentId = _data[_position++];
            byte tableIds = _data[_position++];

            // Find the component by ID
            JpegComponent? component = Array.Find(frame.Components, c => c.Id == componentId);
            if (component != null)
            {
                component.DcTableId = (byte)(tableIds >> 4);
                component.AcTableId = (byte)(tableIds & 0x0F);
            }
        }

        // Read spectral selection and approximation (used in progressive mode)
        byte spectralStart = _data[_position++];
        byte spectralEnd = _data[_position++];
        byte approximation = _data[_position++];

        // For baseline: spectralStart=0, spectralEnd=63, approximation=0
    }

    private void LocateEntropyData(JpegFrame frame)
    {
        frame.EntropyDataOffset = _position;

        // Scan forward to find EOI marker, accounting for byte stuffing
        while (_position < _data.Length - 1)
        {
            if (_data[_position] == JpegMarker.Prefix)
            {
                byte nextByte = _data[_position + 1];

                // 0xFF00 is stuffed byte - skip it
                if (nextByte == 0x00)
                {
                    _position += 2;
                    continue;
                }

                // RST markers - skip them
                if (nextByte >= JpegMarker.RST0 && nextByte <= JpegMarker.RST7)
                {
                    _position += 2;
                    continue;
                }

                // Any other marker ends the entropy data
                if (nextByte != JpegMarker.Prefix)
                {
                    frame.EntropyDataLength = _position - frame.EntropyDataOffset;
                    return;
                }
            }

            _position++;
        }

        // If we get here, we ran off the end
        frame.EntropyDataLength = _data.Length - frame.EntropyDataOffset;
    }

    private void SkipMarkerSegment()
    {
        int length = ReadUInt16();
        _position += length - 2;
    }

    private ushort ReadUInt16()
    {
        var value = (ushort)((_data[_position] << 8) | _data[_position + 1]);
        _position += 2;
        return value;
    }
}
