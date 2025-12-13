using System;
using System.IO;

namespace Compressors.Jpeg;

/// <summary>
/// Encodes images to baseline JPEG format.
/// </summary>
public class JpegEncoder
{
    private readonly int _quality;
    private readonly JpegSubsampling _subsampling;

    private int[]? _luminanceQuantTable;
    private int[]? _chrominanceQuantTable;
    private HuffmanTable? _dcLuminanceTable;
    private HuffmanTable? _dcChrominanceTable;
    private HuffmanTable? _acLuminanceTable;
    private HuffmanTable? _acChrominanceTable;

    /// <summary>
    /// Creates a new JPEG encoder with the specified quality.
    /// </summary>
    /// <param name="quality">Quality factor 1-100 (default 75)</param>
    /// <param name="subsampling">Chroma subsampling mode (default 4:2:0)</param>
    public JpegEncoder(int quality = 75, JpegSubsampling subsampling = JpegSubsampling.Subsampling420)
    {
        _quality = Math.Clamp(quality, 1, 100);
        _subsampling = subsampling;
        InitializeTables();
    }

    /// <summary>
    /// Initializes quantization and Huffman tables.
    /// </summary>
    private void InitializeTables()
    {
        _luminanceQuantTable = Quantization.GenerateLuminanceQuantTable(_quality);
        _chrominanceQuantTable = Quantization.GenerateChrominanceQuantTable(_quality);

        _dcLuminanceTable = HuffmanTable.CreateDcLuminance();
        _dcChrominanceTable = HuffmanTable.CreateDcChrominance();
        _acLuminanceTable = HuffmanTable.CreateAcLuminance();
        _acChrominanceTable = HuffmanTable.CreateAcChrominance();
    }

    /// <summary>
    /// Encodes RGB image data to JPEG.
    /// </summary>
    /// <param name="rgb">RGB pixel data (3 bytes per pixel: R, G, B)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="output">Output stream</param>
    public void Encode(ReadOnlySpan<byte> rgb, int width, int height, Stream output)
    {
        if (rgb.Length != width * height * 3)
            throw new ArgumentException("RGB data length must equal width * height * 3", nameof(rgb));

        using var writer = new BitWriter(output);

        // Write markers and headers
        WriteSOI(writer);
        WriteAPP0(writer);
        WriteDQT(writer);
        WriteSOF0(writer, width, height);
        WriteDHT(writer);
        WriteSOS(writer);

        // Encode image data
        EncodeImageData(rgb, width, height, writer);

        // Write EOI
        writer.FlushWithPadding();
        output.WriteByte(JpegConstants.MarkerPrefix);
        output.WriteByte(JpegConstants.EOI);
    }

    /// <summary>
    /// Encodes grayscale image data to JPEG.
    /// </summary>
    public void EncodeGrayscale(ReadOnlySpan<byte> gray, int width, int height, Stream output)
    {
        if (gray.Length != width * height)
            throw new ArgumentException("Grayscale data length must equal width * height", nameof(gray));

        using var writer = new BitWriter(output);

        WriteSOI(writer);
        WriteAPP0(writer);
        WriteDQTGrayscale(writer);
        WriteSOF0Grayscale(writer, width, height);
        WriteDHTGrayscale(writer);
        WriteSOSGrayscale(writer);

        EncodeGrayscaleData(gray, width, height, writer);

        writer.FlushWithPadding();
        output.WriteByte(JpegConstants.MarkerPrefix);
        output.WriteByte(JpegConstants.EOI);
    }

    /// <summary>
    /// Writes SOI marker.
    /// </summary>
    private void WriteSOI(BitWriter writer)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.SOI });
    }

    /// <summary>
    /// Writes APP0 (JFIF) marker.
    /// </summary>
    private void WriteAPP0(BitWriter writer)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.APP0 });

        // Length (16 bytes after length field)
        writer.WriteUInt16BigEndian(16);

        // JFIF signature
        writer.WriteBytesRaw(JpegConstants.JfifSignature);

        // Version
        writer.WriteBytesRaw(new byte[] { JpegConstants.JfifMajorVersion, JpegConstants.JfifMinorVersion });

        // Density units (0 = no units)
        writer.WriteBytesRaw(new byte[] { 0 });

        // X/Y density (1:1)
        writer.WriteUInt16BigEndian(1);
        writer.WriteUInt16BigEndian(1);

        // No thumbnail
        writer.WriteBytesRaw(new byte[] { 0, 0 });
    }

    /// <summary>
    /// Writes DQT marker with both quantization tables.
    /// </summary>
    private void WriteDQT(BitWriter writer)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.DQT });

        // Length: 2 + (1 + 64) * 2 = 132
        writer.WriteUInt16BigEndian(132);

        // Luminance table (ID 0)
        writer.WriteBytesRaw(new byte[] { 0 }); // 8-bit precision, table 0
        writer.WriteBytesRaw(Quantization.TableToZigzag(_luminanceQuantTable!));

        // Chrominance table (ID 1)
        writer.WriteBytesRaw(new byte[] { 1 }); // 8-bit precision, table 1
        writer.WriteBytesRaw(Quantization.TableToZigzag(_chrominanceQuantTable!));
    }

    /// <summary>
    /// Writes DQT marker for grayscale (luminance only).
    /// </summary>
    private void WriteDQTGrayscale(BitWriter writer)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.DQT });
        writer.WriteUInt16BigEndian(67); // 2 + 1 + 64

        writer.WriteBytesRaw(new byte[] { 0 });
        writer.WriteBytesRaw(Quantization.TableToZigzag(_luminanceQuantTable!));
    }

    /// <summary>
    /// Writes SOF0 marker.
    /// </summary>
    private void WriteSOF0(BitWriter writer, int width, int height)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.SOF0 });

        // Length: 2 + 1 + 2 + 2 + 1 + 3*3 = 17
        writer.WriteUInt16BigEndian(17);

        // Precision (8 bits)
        writer.WriteBytesRaw(new byte[] { 8 });

        // Dimensions
        writer.WriteUInt16BigEndian((ushort)height);
        writer.WriteUInt16BigEndian((ushort)width);

        // Number of components
        writer.WriteBytesRaw(new byte[] { 3 });

        // Component specifications
        byte yHV, cbHV, crHV;
        switch (_subsampling)
        {
            case JpegSubsampling.Subsampling444:
                yHV = 0x11;  // 1x1
                cbHV = 0x11; // 1x1
                crHV = 0x11; // 1x1
                break;
            case JpegSubsampling.Subsampling422:
                yHV = 0x21;  // 2x1
                cbHV = 0x11; // 1x1
                crHV = 0x11; // 1x1
                break;
            case JpegSubsampling.Subsampling420:
            default:
                yHV = 0x22;  // 2x2
                cbHV = 0x11; // 1x1
                crHV = 0x11; // 1x1
                break;
        }

        // Y component (ID 1, sampling, quant table 0)
        writer.WriteBytesRaw(new byte[] { 1, yHV, 0 });
        // Cb component (ID 2, sampling, quant table 1)
        writer.WriteBytesRaw(new byte[] { 2, cbHV, 1 });
        // Cr component (ID 3, sampling, quant table 1)
        writer.WriteBytesRaw(new byte[] { 3, crHV, 1 });
    }

    /// <summary>
    /// Writes SOF0 marker for grayscale.
    /// </summary>
    private void WriteSOF0Grayscale(BitWriter writer, int width, int height)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.SOF0 });
        writer.WriteUInt16BigEndian(11); // 2 + 1 + 2 + 2 + 1 + 3

        writer.WriteBytesRaw(new byte[] { 8 });
        writer.WriteUInt16BigEndian((ushort)height);
        writer.WriteUInt16BigEndian((ushort)width);
        writer.WriteBytesRaw(new byte[] { 1 });
        writer.WriteBytesRaw(new byte[] { 1, 0x11, 0 });
    }

    /// <summary>
    /// Writes DHT markers for all Huffman tables.
    /// </summary>
    private void WriteDHT(BitWriter writer)
    {
        // DC Luminance
        WriteHuffmanTable(writer, _dcLuminanceTable!, 0, 0);
        // AC Luminance
        WriteHuffmanTable(writer, _acLuminanceTable!, 1, 0);
        // DC Chrominance
        WriteHuffmanTable(writer, _dcChrominanceTable!, 0, 1);
        // AC Chrominance
        WriteHuffmanTable(writer, _acChrominanceTable!, 1, 1);
    }

    /// <summary>
    /// Writes DHT markers for grayscale.
    /// </summary>
    private void WriteDHTGrayscale(BitWriter writer)
    {
        WriteHuffmanTable(writer, _dcLuminanceTable!, 0, 0);
        WriteHuffmanTable(writer, _acLuminanceTable!, 1, 0);
    }

    /// <summary>
    /// Writes a single Huffman table.
    /// </summary>
    private void WriteHuffmanTable(BitWriter writer, HuffmanTable table, int tableClass, int tableId)
    {
        byte[] data = table.ToSegmentData(tableClass, tableId);

        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.DHT });
        writer.WriteUInt16BigEndian((ushort)(2 + data.Length));
        writer.WriteBytesRaw(data);
    }

    /// <summary>
    /// Writes SOS marker.
    /// </summary>
    private void WriteSOS(BitWriter writer)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.SOS });

        // Length: 2 + 1 + 3*2 + 3 = 12
        writer.WriteUInt16BigEndian(12);

        // Number of components
        writer.WriteBytesRaw(new byte[] { 3 });

        // Component specifications (ID, DC/AC table IDs)
        writer.WriteBytesRaw(new byte[] { 1, 0x00 }); // Y: DC table 0, AC table 0
        writer.WriteBytesRaw(new byte[] { 2, 0x11 }); // Cb: DC table 1, AC table 1
        writer.WriteBytesRaw(new byte[] { 3, 0x11 }); // Cr: DC table 1, AC table 1

        // Spectral selection and approximation (baseline)
        writer.WriteBytesRaw(new byte[] { 0, 63, 0 }); // Ss, Se, Ah/Al
    }

    /// <summary>
    /// Writes SOS marker for grayscale.
    /// </summary>
    private void WriteSOSGrayscale(BitWriter writer)
    {
        writer.WriteBytesRaw(new byte[] { JpegConstants.MarkerPrefix, JpegConstants.SOS });
        writer.WriteUInt16BigEndian(8); // 2 + 1 + 2 + 3

        writer.WriteBytesRaw(new byte[] { 1 });
        writer.WriteBytesRaw(new byte[] { 1, 0x00 });
        writer.WriteBytesRaw(new byte[] { 0, 63, 0 });
    }

    /// <summary>
    /// Encodes the image data.
    /// </summary>
    private void EncodeImageData(ReadOnlySpan<byte> rgb, int width, int height, BitWriter writer)
    {
        // Convert to YCbCr planes
        var yPlane = new byte[width * height];
        var cbPlane = new byte[width * height];
        var crPlane = new byte[width * height];

        ColorConversion.RgbToYCbCrPlanes(rgb, width, height, yPlane, cbPlane, crPlane);

        // Subsample chroma if needed
        byte[] cbSubsampled, crSubsampled;
        int cbWidth, cbHeight, crWidth, crHeight;

        switch (_subsampling)
        {
            case JpegSubsampling.Subsampling420:
                cbWidth = (width + 1) / 2;
                cbHeight = (height + 1) / 2;
                crWidth = cbWidth;
                crHeight = cbHeight;
                cbSubsampled = new byte[cbWidth * cbHeight];
                crSubsampled = new byte[crWidth * crHeight];
                ColorConversion.Downsample420(cbPlane, width, height, cbSubsampled);
                ColorConversion.Downsample420(crPlane, width, height, crSubsampled);
                break;

            case JpegSubsampling.Subsampling422:
                cbWidth = (width + 1) / 2;
                cbHeight = height;
                crWidth = cbWidth;
                crHeight = height;
                cbSubsampled = new byte[cbWidth * cbHeight];
                crSubsampled = new byte[crWidth * crHeight];
                ColorConversion.Downsample422(cbPlane, width, height, cbSubsampled);
                ColorConversion.Downsample422(crPlane, width, height, crSubsampled);
                break;

            case JpegSubsampling.Subsampling444:
            default:
                cbWidth = width;
                cbHeight = height;
                crWidth = width;
                crHeight = height;
                cbSubsampled = cbPlane;
                crSubsampled = crPlane;
                break;
        }

        // Calculate MCU dimensions
        int mcuWidth, mcuHeight;
        int yBlocksH, yBlocksV;

        switch (_subsampling)
        {
            case JpegSubsampling.Subsampling420:
                mcuWidth = 16;
                mcuHeight = 16;
                yBlocksH = 2;
                yBlocksV = 2;
                break;
            case JpegSubsampling.Subsampling422:
                mcuWidth = 16;
                mcuHeight = 8;
                yBlocksH = 2;
                yBlocksV = 1;
                break;
            default:
                mcuWidth = 8;
                mcuHeight = 8;
                yBlocksH = 1;
                yBlocksV = 1;
                break;
        }

        int mcusPerRow = (width + mcuWidth - 1) / mcuWidth;
        int mcuRows = (height + mcuHeight - 1) / mcuHeight;

        // Encoding state
        int yDcPred = 0, cbDcPred = 0, crDcPred = 0;

        Span<float> dctBlock = stackalloc float[64];
        Span<int> quantized = stackalloc int[64];
        Span<int> zigzag = stackalloc int[64];
        Span<byte> blockPixels = stackalloc byte[64];

        // Encode MCUs
        for (var mcuRow = 0; mcuRow < mcuRows; mcuRow++)
        {
            for (var mcuCol = 0; mcuCol < mcusPerRow; mcuCol++)
            {
                // Encode Y blocks
                for (var blockV = 0; blockV < yBlocksV; blockV++)
                {
                    for (var blockH = 0; blockH < yBlocksH; blockH++)
                    {
                        int blockX = mcuCol * mcuWidth + blockH * 8;
                        int blockY = mcuRow * mcuHeight + blockV * 8;

                        ExtractBlock(yPlane, width, height, blockX, blockY, blockPixels);
                        EncodeBlock(blockPixels, dctBlock, quantized, zigzag,
                            _luminanceQuantTable!, _dcLuminanceTable!, _acLuminanceTable!,
                            ref yDcPred, writer);
                    }
                }

                // Encode Cb block
                {
                    int blockX = mcuCol * (mcuWidth / (yBlocksH > 1 ? 2 : 1));
                    int blockY = mcuRow * (mcuHeight / (yBlocksV > 1 ? 2 : 1));

                    ExtractBlock(cbSubsampled, cbWidth, cbHeight, blockX, blockY, blockPixels);
                    EncodeBlock(blockPixels, dctBlock, quantized, zigzag,
                        _chrominanceQuantTable!, _dcChrominanceTable!, _acChrominanceTable!,
                        ref cbDcPred, writer);
                }

                // Encode Cr block
                {
                    int blockX = mcuCol * (mcuWidth / (yBlocksH > 1 ? 2 : 1));
                    int blockY = mcuRow * (mcuHeight / (yBlocksV > 1 ? 2 : 1));

                    ExtractBlock(crSubsampled, crWidth, crHeight, blockX, blockY, blockPixels);
                    EncodeBlock(blockPixels, dctBlock, quantized, zigzag,
                        _chrominanceQuantTable!, _dcChrominanceTable!, _acChrominanceTable!,
                        ref crDcPred, writer);
                }
            }
        }
    }

    /// <summary>
    /// Encodes grayscale image data.
    /// </summary>
    private void EncodeGrayscaleData(ReadOnlySpan<byte> gray, int width, int height, BitWriter writer)
    {
        int blocksPerRow = (width + 7) / 8;
        int blockRows = (height + 7) / 8;

        var dcPred = 0;
        Span<float> dctBlock = stackalloc float[64];
        Span<int> quantized = stackalloc int[64];
        Span<int> zigzag = stackalloc int[64];
        Span<byte> blockPixels = stackalloc byte[64];

        for (var blockRow = 0; blockRow < blockRows; blockRow++)
        {
            for (var blockCol = 0; blockCol < blocksPerRow; blockCol++)
            {
                int blockX = blockCol * 8;
                int blockY = blockRow * 8;

                ExtractBlock(gray, width, height, blockX, blockY, blockPixels);
                EncodeBlock(blockPixels, dctBlock, quantized, zigzag,
                    _luminanceQuantTable!, _dcLuminanceTable!, _acLuminanceTable!,
                    ref dcPred, writer);
            }
        }
    }

    /// <summary>
    /// Extracts an 8x8 block from an image plane.
    /// </summary>
    private static void ExtractBlock(ReadOnlySpan<byte> plane, int planeWidth, int planeHeight,
        int blockX, int blockY, Span<byte> block)
    {
        for (var y = 0; y < 8; y++)
        {
            int srcY = Math.Min(blockY + y, planeHeight - 1);
            for (var x = 0; x < 8; x++)
            {
                int srcX = Math.Min(blockX + x, planeWidth - 1);
                block[y * 8 + x] = plane[srcY * planeWidth + srcX];
            }
        }
    }

    /// <summary>
    /// Encodes a single 8x8 block.
    /// </summary>
    private static void EncodeBlock(ReadOnlySpan<byte> pixels, Span<float> dctBlock,
        Span<int> quantized, Span<int> zigzag,
        int[] quantTable, HuffmanTable dcTable, HuffmanTable acTable,
        ref int dcPred, BitWriter writer)
    {
        // Forward DCT with level shift
        Dct.ForwardDctFromBytes(pixels, dctBlock);

        // Quantize
        Quantization.QuantizeToInt(dctBlock, quantTable, quantized);

        // Convert to zigzag order
        Quantization.ToZigzag(quantized, zigzag);

        // Encode DC coefficient (differential)
        int dcValue = zigzag[0];
        int dcDiff = dcValue - dcPred;
        dcPred = dcValue;

        EncodeDcCoefficient(dcDiff, dcTable, writer);

        // Encode AC coefficients
        EncodeAcCoefficients(zigzag.Slice(1), acTable, writer);
    }

    /// <summary>
    /// Encodes a DC coefficient.
    /// </summary>
    private static void EncodeDcCoefficient(int value, HuffmanTable table, BitWriter writer)
    {
        int size = BitWriter.GetBitSize(value);
        (ushort code, byte length) = table.Encode((byte)size);

        writer.WriteBits(code, length);

        if (size > 0)
        {
            int bits = BitWriter.GetBitPattern(value, size);
            writer.WriteBits(bits, size);
        }
    }

    /// <summary>
    /// Encodes AC coefficients.
    /// </summary>
    private static void EncodeAcCoefficients(ReadOnlySpan<int> coeffs, HuffmanTable table, BitWriter writer)
    {
        var zeroRun = 0;

        for (var i = 0; i < 63; i++)
        {
            int value = coeffs[i];

            if (value == 0)
            {
                zeroRun++;
            }
            else
            {
                // Encode any 16-zero runs
                while (zeroRun >= 16)
                {
                    (ushort zrlCode, byte zrlLen) = table.Encode(JpegConstants.ZRL);
                    writer.WriteBits(zrlCode, zrlLen);
                    zeroRun -= 16;
                }

                // Encode (run, size) symbol
                int size = BitWriter.GetBitSize(value);
                var symbol = (byte)((zeroRun << 4) | size);
                (ushort code, byte length) = table.Encode(symbol);
                writer.WriteBits(code, length);

                // Encode value bits
                int bits = BitWriter.GetBitPattern(value, size);
                writer.WriteBits(bits, size);

                zeroRun = 0;
            }
        }

        // If we ended with zeros, write EOB
        if (zeroRun > 0)
        {
            (ushort eobCode, byte eobLen) = table.Encode(JpegConstants.EOB);
            writer.WriteBits(eobCode, eobLen);
        }
    }
}

/// <summary>
/// Chroma subsampling modes.
/// </summary>
public enum JpegSubsampling
{
    /// <summary>
    /// No subsampling (4:4:4) - best quality, larger files.
    /// </summary>
    Subsampling444,

    /// <summary>
    /// Horizontal subsampling only (4:2:2).
    /// </summary>
    Subsampling422,

    /// <summary>
    /// Horizontal and vertical subsampling (4:2:0) - standard JPEG.
    /// </summary>
    Subsampling420
}
