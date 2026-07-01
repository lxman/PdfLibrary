using System;
using System.Buffers.Binary;
using System.IO;

namespace BmpCodec
{
    /// <summary>
    /// Decodes BMP image files.
    /// </summary>
    public static class BmpDecoder
    {
        /// <summary>
        /// Decode a BMP image from a byte array.
        /// </summary>
        public static BmpImage Decode(byte[] data)
        {
            return Decode(data.AsSpan());
        }

        /// <summary>
        /// Decode a BMP image from a span.
        /// </summary>
        public static BmpImage Decode(ReadOnlySpan<byte> data)
        {
            try
            {
                if (data.Length < BitmapFileHeader.Size + BitmapInfoHeader.Size)
                    throw new BmpException("Data too small to contain a valid BMP file");

                // Read the file header
                BitmapFileHeader fileHeader = ReadFileHeader(data);

                if (fileHeader.Type != BitmapFileHeader.BmpSignature)
                    throw new BmpException($"Invalid BMP signature: 0x{fileHeader.Type:X4}");

                // Read info header
                BitmapInfoHeader infoHeader = ReadInfoHeader(data.Slice(BitmapFileHeader.Size));

                if (infoHeader.Planes != 1)
                    throw new BmpException($"Invalid plane count: {infoHeader.Planes}");

                if (infoHeader.Width <= 0)
                    throw new BmpException($"Invalid width: {infoHeader.Width}");

                if (infoHeader.AbsoluteHeight == 0)
                    throw new BmpException("Invalid height: 0");

                // Validate dimensions before allocating the BGRA buffer. A plain `> 32768` check let
                // 32768x32768 through, where width*height*4 == 2^32 overflows int and the allocation
                // wraps to a zero-length buffer. Bound each axis, then cap the byte count in long.
                if (infoHeader.Width > 65535 || infoHeader.AbsoluteHeight > 65535)
                    throw new BmpException($"Image dimensions too large: {infoHeader.Width}x{infoHeader.AbsoluteHeight}");
                if ((long)infoHeader.Width * infoHeader.AbsoluteHeight * 4 > (1L << 30)) // 1 GiB BGRA
                    throw new BmpException($"Image too large to decode: {infoHeader.Width}x{infoHeader.AbsoluteHeight}");

                // Validate pixel data offset
                if (fileHeader.PixelDataOffset > data.Length)
                    throw new BmpException("Pixel data offset beyond end of file");

                // Read color palette if present
                RgbQuad[]? palette = null;
                if (infoHeader.BitsPerPixel <= 8)
                {
                    int paletteSize = infoHeader.ColorsUsed > 0
                        ? (int)infoHeader.ColorsUsed
                        : 1 << infoHeader.BitsPerPixel;

                    // Validate palette size
                    if (paletteSize > 256)
                        throw new BmpException($"Invalid palette size: {paletteSize}");

                    int paletteOffset = BitmapFileHeader.Size + (int)infoHeader.HeaderSize;
                    int paletteBytes = paletteSize * RgbQuad.Size;
                    if (paletteOffset + paletteBytes > data.Length)
                        throw new BmpException("Palette extends beyond end of file");

                    palette = ReadPalette(data.Slice(paletteOffset), paletteSize);
                }

                // For BITFIELDS / ALPHABITFIELDS, read the channel masks. They live at the same file
                // offset (info-header start + 40 = 54) whether they trail a 40-byte BITMAPINFOHEADER
                // or are embedded in a BITMAPV4/V5HEADER.
                uint maskR = 0, maskG = 0, maskB = 0, maskA = 0;
                if (infoHeader.Compression is BmpCompression.BitFields or BmpCompression.AlphaBitFields)
                {
                    int maskOffset = BitmapFileHeader.Size + 40;
                    int maskCount = infoHeader.Compression == BmpCompression.AlphaBitFields ? 4 : 3;
                    if (maskOffset + maskCount * 4 > data.Length)
                        throw new BmpException("BITFIELDS masks extend beyond end of file");
                    maskR = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(maskOffset));
                    maskG = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(maskOffset + 4));
                    maskB = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(maskOffset + 8));
                    if (maskCount == 4)
                        maskA = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(maskOffset + 12));
                }

                // Read pixel data
                ReadOnlySpan<byte> pixelData = data.Slice((int)fileHeader.PixelDataOffset);

                return DecodePixels(infoHeader, palette, pixelData, maskR, maskG, maskB, maskA);
            }
            catch (BmpException)
            {
                throw;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new BmpException($"Invalid data: {ex.Message}", ex);
            }
            catch (OverflowException ex)
            {
                throw new BmpException($"Numeric overflow (image too large?): {ex.Message}", ex);
            }
            catch (IndexOutOfRangeException ex)
            {
                throw new BmpException($"Data truncated or corrupted: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new BmpException($"Failed to decode BMP: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Decode a BMP image from a stream.
        /// </summary>
        public static BmpImage Decode(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Decode(ms.ToArray());
        }

        /// <summary>
        /// Decode a BMP image from a file.
        /// </summary>
        public static BmpImage Decode(string path)
        {
            return Decode(File.ReadAllBytes(path));
        }

        private static BitmapFileHeader ReadFileHeader(ReadOnlySpan<byte> data)
        {
            return new BitmapFileHeader(
                BinaryPrimitives.ReadUInt16LittleEndian(data),
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(10))
            );
        }

        private static BitmapInfoHeader ReadInfoHeader(ReadOnlySpan<byte> data)
        {
            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data);

            // Support BITMAPINFOHEADER (40), BITMAPV4HEADER (108), BITMAPV5HEADER (124)
            if (headerSize < BitmapInfoHeader.Size)
                throw new BmpException($"Unsupported header size: {headerSize}");

            return new BitmapInfoHeader(
                headerSize,
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12)),
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14)),
                (BmpCompression)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(24)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32)),
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(36))
            );
        }

        private static RgbQuad[] ReadPalette(ReadOnlySpan<byte> data, int count)
        {
            var palette = new RgbQuad[count];
            for (var i = 0; i < count; i++)
            {
                int offset = i * RgbQuad.Size;
                palette[i] = new RgbQuad(
                    data[offset],
                    data[offset + 1],
                    data[offset + 2],
                    data[offset + 3]
                );
            }
            return palette;
        }

        private static BmpImage DecodePixels(BitmapInfoHeader header, RgbQuad[]? palette, ReadOnlySpan<byte> pixelData,
            uint maskR, uint maskG, uint maskB, uint maskA)
        {
            int width = header.Width;
            int height = header.AbsoluteHeight;
            bool bottomUp = !header.IsTopDown;

            // Output is always 32-bit BGRA, top-down
            var output = new byte[width * height * 4];

            switch (header.Compression)
            {
                case BmpCompression.Rgb:
                    DecodeRgb(header, palette, pixelData, output, bottomUp);
                    break;

                case BmpCompression.BitFields:
                case BmpCompression.AlphaBitFields:
                    DecodeBitFields(header, pixelData, output, bottomUp, maskR, maskG, maskB, maskA);
                    break;

                case BmpCompression.Rle8:
                    if (header.BitsPerPixel != 8 || palette is null)
                        throw new BmpException("RLE8 compression requires 8-bit palette image");
                    DecodeRle8(header, palette, pixelData, output, bottomUp);
                    break;

                case BmpCompression.Rle4:
                    if (header.BitsPerPixel != 4 || palette is null)
                        throw new BmpException("RLE4 compression requires 4-bit palette image");
                    DecodeRle4(header, palette, pixelData, output, bottomUp);
                    break;

                default:
                    throw new BmpException($"Unsupported compression: {header.Compression}");
            }

            var image = new BmpImage(width, height, header.BitsPerPixel, output)
            {
                XPixelsPerMeter = header.XPixelsPerMeter,
                YPixelsPerMeter = header.YPixelsPerMeter
            };

            return image;
        }

        private static void DecodeRgb(BitmapInfoHeader header, RgbQuad[]? palette, ReadOnlySpan<byte> pixelData, byte[] output, bool bottomUp)
        {
            int width = header.Width;
            int height = header.AbsoluteHeight;
            int stride = header.Stride;

            for (var srcY = 0; srcY < height; srcY++)
            {
                int dstY = bottomUp ? height - 1 - srcY : srcY;
                int srcRowOffset = srcY * stride;
                int dstRowOffset = dstY * width * 4;

                switch (header.BitsPerPixel)
                {
                    case 1:
                        Decode1Bpp(pixelData.Slice(srcRowOffset), palette!, output.AsSpan(dstRowOffset), width);
                        break;
                    case 4:
                        Decode4Bpp(pixelData.Slice(srcRowOffset), palette!, output.AsSpan(dstRowOffset), width);
                        break;
                    case 8:
                        Decode8Bpp(pixelData.Slice(srcRowOffset), palette!, output.AsSpan(dstRowOffset), width);
                        break;
                    case 16:
                        Decode16Bpp(pixelData.Slice(srcRowOffset), output.AsSpan(dstRowOffset), width);
                        break;
                    case 24:
                        Decode24Bpp(pixelData.Slice(srcRowOffset), output.AsSpan(dstRowOffset), width);
                        break;
                    case 32:
                        Decode32Bpp(pixelData.Slice(srcRowOffset), output.AsSpan(dstRowOffset), width);
                        break;
                    default:
                        throw new BmpException($"Unsupported bit depth: {header.BitsPerPixel}");
                }
            }
        }

        private static void Decode1Bpp(ReadOnlySpan<byte> src, RgbQuad[] palette, Span<byte> dst, int width)
        {
            for (var x = 0; x < width; x++)
            {
                int byteIndex = x / 8;
                if (byteIndex >= src.Length) break;
                int bitIndex = 7 - x % 8;
                int colorIndex = (src[byteIndex] >> bitIndex) & 1;

                RgbQuad color = colorIndex < palette.Length ? palette[colorIndex] : default;
                int dstOffset = x * 4;
                dst[dstOffset] = color.Blue;
                dst[dstOffset + 1] = color.Green;
                dst[dstOffset + 2] = color.Red;
                dst[dstOffset + 3] = 255;
            }
        }

        private static void Decode4Bpp(ReadOnlySpan<byte> src, RgbQuad[] palette, Span<byte> dst, int width)
        {
            for (var x = 0; x < width; x++)
            {
                int byteIndex = x / 2;
                if (byteIndex >= src.Length) break;
                int colorIndex = x % 2 == 0
                    ? (src[byteIndex] >> 4) & 0x0F
                    : src[byteIndex] & 0x0F;

                RgbQuad color = colorIndex < palette.Length ? palette[colorIndex] : default;
                int dstOffset = x * 4;
                dst[dstOffset] = color.Blue;
                dst[dstOffset + 1] = color.Green;
                dst[dstOffset + 2] = color.Red;
                dst[dstOffset + 3] = 255;
            }
        }

        private static void Decode8Bpp(ReadOnlySpan<byte> src, RgbQuad[] palette, Span<byte> dst, int width)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= src.Length) break;
                int colorIndex = src[x];
                RgbQuad color = colorIndex < palette.Length ? palette[colorIndex] : default;
                int dstOffset = x * 4;
                dst[dstOffset] = color.Blue;
                dst[dstOffset + 1] = color.Green;
                dst[dstOffset + 2] = color.Red;
                dst[dstOffset + 3] = 255;
            }
        }

        private static void Decode16Bpp(ReadOnlySpan<byte> src, Span<byte> dst, int width)
        {
            // Default 16-bit format: 5-5-5 (X1R5G5B5)
            for (var x = 0; x < width; x++)
            {
                ushort pixel = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(x * 2));

                var b = (byte)((pixel & 0x001F) << 3);
                var g = (byte)(((pixel >> 5) & 0x001F) << 3);
                var r = (byte)(((pixel >> 10) & 0x001F) << 3);

                int dstOffset = x * 4;
                dst[dstOffset] = b;
                dst[dstOffset + 1] = g;
                dst[dstOffset + 2] = r;
                dst[dstOffset + 3] = 255;
            }
        }

        private static void Decode24Bpp(ReadOnlySpan<byte> src, Span<byte> dst, int width)
        {
            for (var x = 0; x < width; x++)
            {
                int srcOffset = x * 3;
                int dstOffset = x * 4;

                dst[dstOffset] = src[srcOffset];         // Blue
                dst[dstOffset + 1] = src[srcOffset + 1]; // Green
                dst[dstOffset + 2] = src[srcOffset + 2]; // Red
                dst[dstOffset + 3] = 255;                // Alpha
            }
        }

        private static void Decode32Bpp(ReadOnlySpan<byte> src, Span<byte> dst, int width)
        {
            // This path is only reached for BI_RGB (uncompressed) 32-bit BMPs, where the 4th byte is
            // undefined ("X8R8G8B8") — most writers leave it 0. Treat it as opaque, matching the spec
            // and other decoders; a 32-bit BMP that genuinely carries alpha uses (ALPHA)BITFIELDS,
            // which goes through DecodeBitFields. Honouring the undefined byte washed real images
            // (e.g. Visual Studio icon BMPs, which store 0 there) to fully transparent.
            for (var x = 0; x < width; x++)
            {
                int srcOffset = x * 4;
                int dstOffset = x * 4;

                dst[dstOffset] = src[srcOffset];         // Blue
                dst[dstOffset + 1] = src[srcOffset + 1]; // Green
                dst[dstOffset + 2] = src[srcOffset + 2]; // Red
                dst[dstOffset + 3] = 255;                // Alpha: BI_RGB 32-bit is opaque
            }
        }

        private static void DecodeBitFields(BitmapInfoHeader header, ReadOnlySpan<byte> pixelData, byte[] output, bool bottomUp,
            uint maskR, uint maskG, uint maskB, uint maskA)
        {
            int width = header.Width;
            int height = header.AbsoluteHeight;
            int stride = header.Stride;
            int bpp = header.BitsPerPixel;
            if (bpp != 16 && bpp != 32)
                throw new BmpException($"BITFIELDS supports 16 or 32 bits per pixel, not {bpp}");

            // Some encoders set BITFIELDS but leave the masks zero; fall back to the conventional
            // layouts (5-6-5 for 16-bit, 8-8-8 for 32-bit).
            if (maskR == 0 && maskG == 0 && maskB == 0)
            {
                if (bpp == 16) { maskR = 0xF800; maskG = 0x07E0; maskB = 0x001F; }
                else { maskR = 0x00FF0000; maskG = 0x0000FF00; maskB = 0x000000FF; }
            }

            int rShift = TrailingZeros(maskR), rBits = PopCount(maskR);
            int gShift = TrailingZeros(maskG), gBits = PopCount(maskG);
            int bShift = TrailingZeros(maskB), bBits = PopCount(maskB);
            int aShift = TrailingZeros(maskA), aBits = PopCount(maskA);
            int bytesPerPixel = bpp / 8;

            for (var srcY = 0; srcY < height; srcY++)
            {
                int dstY = bottomUp ? height - 1 - srcY : srcY;
                int srcRowOffset = srcY * stride;
                int dstRowOffset = dstY * width * 4;
                for (var x = 0; x < width; x++)
                {
                    int srcOffset = srcRowOffset + x * bytesPerPixel;
                    if (srcOffset + bytesPerPixel > pixelData.Length) return; // tolerate truncation

                    uint pixel = bpp == 16
                        ? BinaryPrimitives.ReadUInt16LittleEndian(pixelData.Slice(srcOffset))
                        : BinaryPrimitives.ReadUInt32LittleEndian(pixelData.Slice(srcOffset));

                    int dstOffset = dstRowOffset + x * 4;
                    output[dstOffset] = ScaleChannel(pixel, maskB, bShift, bBits);
                    output[dstOffset + 1] = ScaleChannel(pixel, maskG, gShift, gBits);
                    output[dstOffset + 2] = ScaleChannel(pixel, maskR, rShift, rBits);
                    output[dstOffset + 3] = maskA != 0 ? ScaleChannel(pixel, maskA, aShift, aBits) : (byte)255;
                }
            }
        }

        // Extracts a channel masked out of a packed pixel and scales it to 0-255.
        private static byte ScaleChannel(uint pixel, uint mask, int shift, int bits)
        {
            if (bits == 0) return 0;
            uint raw = (pixel & mask) >> shift;
            int max = (1 << bits) - 1;
            return (byte)(raw * 255 / max);
        }

        // netstandard2.1 has no System.Numerics.BitOperations, so these are spelled out. Masks are
        // at most 32 bits, so the loops are trivially bounded.
        private static int TrailingZeros(uint v)
        {
            if (v == 0) return 0;
            var n = 0;
            while ((v & 1) == 0) { v >>= 1; n++; }
            return n;
        }

        private static int PopCount(uint v)
        {
            var n = 0;
            while (v != 0) { n += (int)(v & 1); v >>= 1; }
            return n;
        }

        private static void DecodeRle8(BitmapInfoHeader header, RgbQuad[] palette, ReadOnlySpan<byte> data, byte[] output, bool bottomUp)
        {
            int width = header.Width;
            int height = header.AbsoluteHeight;
            int x = 0, y = 0;
            var i = 0;

            while (i < data.Length && y < height)
            {
                byte first = data[i++];
                if (i >= data.Length) break;
                byte second = data[i++];

                if (first > 0)
                {
                    // Encoded run: first pixels of color second
                    RgbQuad color = palette[second];
                    for (var j = 0; j < first && x < width; j++, x++)
                    {
                        int dstY = bottomUp ? height - 1 - y : y;
                        int offset = (dstY * width + x) * 4;
                        output[offset] = color.Blue;
                        output[offset + 1] = color.Green;
                        output[offset + 2] = color.Red;
                        output[offset + 3] = 255;
                    }
                }
                else
                {
                    // Escape sequence
                    switch (second)
                    {
                        case 0: // End of line
                            x = 0;
                            y++;
                            break;
                        case 1: // End of bitmap
                            return;
                        case 2: // Delta
                            if (i + 1 >= data.Length) return;
                            x += data[i++];
                            y += data[i++];
                            break;
                        default: // Absolute mode: second literal pixels follow
                            for (var j = 0; j < second && x < width; j++, x++)
                            {
                                if (i >= data.Length) return;
                                RgbQuad color = palette[data[i++]];
                                int dstY = bottomUp ? height - 1 - y : y;
                                int offset = (dstY * width + x) * 4;
                                output[offset] = color.Blue;
                                output[offset + 1] = color.Green;
                                output[offset + 2] = color.Red;
                                output[offset + 3] = 255;
                            }
                            // Pad to word boundary
                            if (second % 2 == 1) i++;
                            break;
                    }
                }
            }
        }

        private static void DecodeRle4(BitmapInfoHeader header, RgbQuad[] palette, ReadOnlySpan<byte> data, byte[] output, bool bottomUp)
        {
            int width = header.Width;
            int height = header.AbsoluteHeight;
            int x = 0, y = 0;
            var i = 0;

            while (i < data.Length && y < height)
            {
                byte first = data[i++];
                if (i >= data.Length) break;
                byte second = data[i++];

                if (first > 0)
                {
                    // Encoded run: first pixels alternating between two colors
                    int color1Index = (second >> 4) & 0x0F;
                    int color2Index = second & 0x0F;

                    for (var j = 0; j < first && x < width; j++, x++)
                    {
                        RgbQuad color = palette[j % 2 == 0 ? color1Index : color2Index];
                        int dstY = bottomUp ? height - 1 - y : y;
                        int offset = (dstY * width + x) * 4;
                        output[offset] = color.Blue;
                        output[offset + 1] = color.Green;
                        output[offset + 2] = color.Red;
                        output[offset + 3] = 255;
                    }
                }
                else
                {
                    // Escape sequence
                    switch (second)
                    {
                        case 0: // End of line
                            x = 0;
                            y++;
                            break;
                        case 1: // End of bitmap
                            return;
                        case 2: // Delta
                            if (i + 1 >= data.Length) return;
                            x += data[i++];
                            y += data[i++];
                            break;
                        default: // Absolute mode
                            int bytesNeeded = (second + 1) / 2;
                            for (var j = 0; j < second && x < width; j++, x++)
                            {
                                int byteIndex = j / 2;
                                if (i + byteIndex >= data.Length) return;
                                byte b = data[i + byteIndex];
                                int colorIndex = j % 2 == 0 ? (b >> 4) & 0x0F : b & 0x0F;

                                RgbQuad color = palette[colorIndex];
                                int dstY = bottomUp ? height - 1 - y : y;
                                int offset = (dstY * width + x) * 4;
                                output[offset] = color.Blue;
                                output[offset + 1] = color.Green;
                                output[offset + 2] = color.Red;
                                output[offset + 3] = 255;
                            }
                            i += bytesNeeded;
                            // Pad to word boundary
                            if (bytesNeeded % 2 == 1) i++;
                            break;
                    }
                }
            }
        }
    }
}