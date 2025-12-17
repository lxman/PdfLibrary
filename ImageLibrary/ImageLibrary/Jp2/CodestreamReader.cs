using System;
using System.Collections.Generic;
using System.Text;

namespace ImageLibrary.Jp2;

/// <summary>
/// Reads and parses JPEG2000 codestream markers.
/// Stage 1 of the decoder pipeline.
/// </summary>
internal class CodestreamReader
{
    private readonly byte[] _data;
    private int _position;

    public CodestreamReader(byte[] data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>
    /// Gets or sets the current position in the codestream.
    /// </summary>
    public int Position
    {
        get => _position;
        set => _position = value;
    }

    /// <summary>
    /// Gets the total length of the codestream.
    /// </summary>
    public int Length => _data.Length;

    /// <summary>
    /// Reads the main header of the codestream.
    /// </summary>
    /// <returns>Parsed codestream information.</returns>
    public Jp2Codestream ReadMainHeader()
    {
        var codestream = new Jp2Codestream();

        // Read SOC marker
        ushort marker = ReadMarker();
        if (marker != Jp2Markers.SOC)
        {
            throw new Jp2Exception($"Expected SOC marker at start, found {Jp2Markers.GetName(marker)}");
        }

        // Read main header markers until SOT or EOC
        while (_position < _data.Length)
        {
            marker = ReadMarker();

            if (marker == Jp2Markers.SOT)
            {
                // Start of tile-part, rewind to let tile reading handle it
                _position -= 2;
                break;
            }

            if (marker == Jp2Markers.EOC)
            {
                break;
            }

            int length = ReadUInt16() - 2; // Length includes the 2-byte length field
            int markerEnd = _position + length;

            switch (marker)
            {
                case Jp2Markers.SIZ:
                    codestream.Frame = ReadSizMarker(length);
                    break;

                case Jp2Markers.COD:
                    codestream.CodingParameters = ReadCodMarker(length);
                    break;

                case Jp2Markers.QCD:
                    codestream.QuantizationParameters = ReadQcdMarker(length);
                    break;

                case Jp2Markers.COM:
                    codestream.Comments.Add(ReadComMarker(length));
                    break;

                default:
                    // Skip unknown markers
                    _position = markerEnd;
                    break;
            }

            // Ensure we're at the correct position
            _position = markerEnd;
        }

        return codestream;
    }

    /// <summary>
    /// Reads the SIZ marker (Image and tile size).
    /// </summary>
    private Jp2Frame ReadSizMarker(int length)
    {
        int rsiz = ReadUInt16();  // Capabilities (Rsiz)
        int xsiz = ReadInt32();   // Reference grid width
        int ysiz = ReadInt32();   // Reference grid height
        int xosiz = ReadInt32();  // Horizontal offset
        int yosiz = ReadInt32();  // Vertical offset
        int xtsiz = ReadInt32();  // Tile width
        int ytsiz = ReadInt32();  // Tile height
        int xtosiz = ReadInt32(); // Tile horizontal offset
        int ytosiz = ReadInt32(); // Tile vertical offset
        int csiz = ReadUInt16();  // Number of components

        var components = new Jp2Component[csiz];
        for (var i = 0; i < csiz; i++)
        {
            byte ssiz = _data[_position++];
            byte xrsiz = _data[_position++];
            byte yrsiz = _data[_position++];

            components[i] = new Jp2Component
            {
                BitDepth = ssiz & 0x7F,
                IsSigned = (ssiz & 0x80) != 0,
                XSubsampling = xrsiz,
                YSubsampling = yrsiz,
            };
        }

        return new Jp2Frame
        {
            Width = xsiz,
            Height = ysiz,
            XOffset = xosiz,
            YOffset = yosiz,
            TileWidth = xtsiz,
            TileHeight = ytsiz,
            TileXOffset = xtosiz,
            TileYOffset = ytosiz,
            ComponentCount = csiz,
            Components = components,
        };
    }

    /// <summary>
    /// Reads the COD marker (Coding style default).
    /// </summary>
    private CodingParameters ReadCodMarker(int length)
    {
        byte scod = _data[_position++];  // Coding style
        byte progression = _data[_position++];
        int layers = ReadUInt16();
        byte mct = _data[_position++];
        byte decompositionLevels = _data[_position++];
        byte xcb = _data[_position++];
        byte ycb = _data[_position++];
        byte codeBlockStyle = _data[_position++];
        byte wavelet = _data[_position++];

        // Read precinct sizes if defined
        var precincts = new List<(int, int)>();
        if ((scod & 0x01) != 0)
        {
            // Precinct sizes for each resolution level (NL + 1 levels)
            for (var r = 0; r <= decompositionLevels; r++)
            {
                if (_position < _data.Length)
                {
                    byte ppx_ppy = _data[_position++];
                    int ppx = ppx_ppy & 0x0F;
                    int ppy = (ppx_ppy >> 4) & 0x0F;
                    precincts.Add((1 << ppx, 1 << ppy));
                }
            }
        }

        return new CodingParameters
        {
            Style = (CodingStyle)scod,
            Progression = (ProgressionOrder)progression,
            LayerCount = layers,
            MultipleComponentTransform = mct,
            DecompositionLevels = decompositionLevels,
            CodeBlockWidthExponent = xcb + 2,
            CodeBlockHeightExponent = ycb + 2,
            CodeBlockFlags = (CodeBlockStyle)codeBlockStyle,
            WaveletType = (WaveletTransform)wavelet,
            PrecinctSizes = precincts.ToArray(),
        };
    }

    /// <summary>
    /// Reads the QCD marker (Quantization default).
    /// </summary>
    private QuantizationParameters ReadQcdMarker(int length)
    {
        int startPos = _position;
        byte sqcd = _data[_position++];

        var style = (QuantizationStyle)(sqcd & 0x1F);
        int guardBits = (sqcd >> 5) & 0x07;

        var stepSizes = new List<QuantizationStepSize>();
        int remaining = length - 1;

        if (style == QuantizationStyle.None)
        {
            // Reversible: one byte per subband (exponent only)
            while (_position < startPos + length)
            {
                byte epsilon = _data[_position++];
                int exponent = (epsilon >> 3) & 0x1F;
                stepSizes.Add(new QuantizationStepSize(exponent, 0));
            }
        }
        else if (style == QuantizationStyle.ScalarDerived)
        {
            // One base step size, derived for others
            int value = ReadUInt16();
            int exponent = (value >> 11) & 0x1F;
            int mantissa = value & 0x07FF;
            stepSizes.Add(new QuantizationStepSize(exponent, mantissa));
        }
        else
        {
            // Scalar expounded: 2 bytes per subband
            while (_position < startPos + length)
            {
                int value = ReadUInt16();
                int exponent = (value >> 11) & 0x1F;
                int mantissa = value & 0x07FF;
                stepSizes.Add(new QuantizationStepSize(exponent, mantissa));
            }
        }

        return new QuantizationParameters
        {
            Style = style,
            GuardBits = guardBits,
            StepSizes = stepSizes.ToArray(),
        };
    }

    /// <summary>
    /// Reads the COM marker (Comment).
    /// </summary>
    private string ReadComMarker(int length)
    {
        int rcom = ReadUInt16();  // Registration (0 = binary, 1 = Latin-1)
        int textLength = length - 2;

        if (rcom == 1 && textLength > 0)
        {
            return Encoding.GetEncoding("iso-8859-1").GetString(_data, _position, textLength);
        }

        return $"<binary data, {textLength} bytes>";
    }

    /// <summary>
    /// Reads a tile-part from the codestream.
    /// </summary>
    public Jp2TilePart? ReadTilePart()
    {
        if (_position >= _data.Length)
        {
            return null;
        }

        // Check for EOC marker
        if (_position + 1 < _data.Length && _data[_position] == 0xFF && _data[_position + 1] == 0xD9)
        {
            _position += 2;
            return null;
        }

        int sotStartPosition = _position;  // Track start of SOT marker

        ushort marker = ReadMarker();
        if (marker == Jp2Markers.EOC)
        {
            return null;
        }

        if (marker != Jp2Markers.SOT)
        {
            throw new Jp2Exception($"Expected SOT marker, found {Jp2Markers.GetName(marker)}");
        }

        int sotLength = ReadUInt16();
        int tileIndex = ReadUInt16();
        int tilePartLength = ReadInt32();
        byte tilePartIndex = _data[_position++];
        byte tilePartCount = _data[_position++];

        // Read tile-part header markers until SOD
        var tilePart = new Jp2TilePart
        {
            TileIndex = tileIndex,
            TilePartIndex = tilePartIndex,
            TilePartCount = tilePartCount,
        };

        while (_position < _data.Length)
        {
            marker = ReadMarker();

            if (marker == Jp2Markers.SOD)
            {
                break;
            }

            int length = ReadUInt16() - 2;
            _position += length; // Skip tile-part header markers for now
        }

        // Calculate bitstream length
        // Psot (tilePartLength) is total bytes from start of SOT marker to end of bitstream
        int bitstreamLength;
        if (tilePartLength > 0)
        {
            // Bitstream ends at sotStartPosition + tilePartLength
            bitstreamLength = (sotStartPosition + tilePartLength) - _position;
        }
        else
        {
            // Psot=0 means tile-part extends to EOC
            int endPos = _data.Length;
            for (int i = _position; i < _data.Length - 1; i++)
            {
                if (_data[i] == 0xFF && _data[i + 1] == 0xD9)
                {
                    endPos = i;
                    break;
                }
            }
            bitstreamLength = endPos - _position;
        }

        if (bitstreamLength < 0) bitstreamLength = 0;
        if (_position + bitstreamLength > _data.Length)
        {
            bitstreamLength = _data.Length - _position;
        }

        tilePart.BitstreamData = new byte[bitstreamLength];
        if (bitstreamLength > 0)
        {
            Array.Copy(_data, _position, tilePart.BitstreamData, 0, bitstreamLength);
        }
        _position += bitstreamLength;

        return tilePart;
    }

    private ushort ReadMarker()
    {
        if (_position + 2 > _data.Length)
        {
            throw new Jp2Exception("Unexpected end of codestream");
        }
        var marker = (ushort)((_data[_position] << 8) | _data[_position + 1]);
        _position += 2;
        return marker;
    }

    private ushort ReadUInt16()
    {
        var value = (ushort)((_data[_position] << 8) | _data[_position + 1]);
        _position += 2;
        return value;
    }

    private int ReadInt32()
    {
        int value = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                    (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        return value;
    }
}

/// <summary>
/// Represents the parsed JPEG2000 codestream.
/// </summary>
public class Jp2Codestream
{
    /// <summary>
    /// Gets or sets the frame information containing image dimensions and component details.
    /// </summary>
    public Jp2Frame Frame { get; set; } = null!;

    /// <summary>
    /// Gets or sets the coding parameters that control how the image is compressed.
    /// </summary>
    public CodingParameters CodingParameters { get; set; } = null!;

    /// <summary>
    /// Gets or sets the quantization parameters used for coefficient quantization.
    /// </summary>
    public QuantizationParameters QuantizationParameters { get; set; } = null!;

    /// <summary>
    /// Gets or sets the list of comments embedded in the codestream.
    /// </summary>
    public List<string> Comments { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of tile-parts that make up the compressed image data.
    /// </summary>
    public List<Jp2TilePart> TileParts { get; set; } = [];
}

/// <summary>
/// Represents a tile-part in the codestream.
/// </summary>
public class Jp2TilePart
{
    /// <summary>
    /// Gets or sets the index of the tile this part belongs to.
    /// </summary>
    public int TileIndex { get; set; }

    /// <summary>
    /// Gets or sets the index of this part within the tile.
    /// </summary>
    public int TilePartIndex { get; set; }

    /// <summary>
    /// Gets or sets the total number of tile-parts for this tile.
    /// </summary>
    public int TilePartCount { get; set; }

    /// <summary>
    /// Gets or sets the compressed bitstream data for this tile-part.
    /// </summary>
    public byte[] BitstreamData { get; set; } = [];
}

/// <summary>
/// Exception thrown when parsing JPEG2000 data.
/// </summary>
public class Jp2Exception : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Jp2Exception"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public Jp2Exception(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Jp2Exception"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public Jp2Exception(string message, Exception inner) : base(message, inner) { }
}
