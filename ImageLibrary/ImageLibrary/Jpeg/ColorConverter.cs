using System;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Converts YCbCr blocks to RGB pixels and assembles the final image.
/// This is Stage 6 of the decoder.
/// </summary>
internal class ColorConverter
{
    private readonly JpegFrame _frame;

    public ColorConverter(JpegFrame frame)
    {
        _frame = frame;
    }

    /// <summary>
    /// Assembles all blocks into the final image.
    /// </summary>
    /// <param name="pixels">Pixel blocks [component][block][pixel]</param>
    /// <returns>Final RGB image as a byte array (R, G, B, R, G, B, ...)</returns>
    public byte[] AssembleImage(byte[][][] pixels)
    {
        if (_frame.ComponentCount == 1)
        {
            return AssembleGrayscale(pixels[0]);
        }

        return AssembleColor(pixels);
    }

    /// <summary>
    /// Assembles grayscale image from Y component blocks.
    /// Handles images with sampling factors > 1.
    /// </summary>
    private byte[] AssembleGrayscale(byte[][] yBlocks)
    {
        int width = _frame.Width;
        int height = _frame.Height;
        var result = new byte[width * height * 3];

        JpegComponent yComp = _frame.Components[0];
        int hSamp = yComp.HorizontalSamplingFactor;
        int vSamp = yComp.VerticalSamplingFactor;

        // For single-component images with sampling > 1, blocks are stored in decode order
        // which corresponds to a simple sequential layout
        bool useDecodeOrder = hSamp > 1 || vSamp > 1;

        // For decode order storage, blocks are simply laid out in a grid
        // where each block covers 8x8 pixels regardless of MCU structure
        int blocksPerRow = (width + 7) / 8;

        for (var imgY = 0; imgY < height; imgY++)
        {
            for (var imgX = 0; imgX < width; imgX++)
            {
                // Calculate which block this pixel belongs to
                int blockX = imgX / 8;
                int blockY = imgY / 8;
                int pixelX = imgX % 8;
                int pixelY = imgY % 8;

                int blockIndex = blockY * blocksPerRow + blockX;
                if (blockIndex >= yBlocks.Length)
                {
                    continue;
                }

                byte y = yBlocks[blockIndex][pixelY * 8 + pixelX];
                int pixelOffset = (imgY * width + imgX) * 3;

                // Grayscale: R = G = B = Y
                result[pixelOffset] = y;
                result[pixelOffset + 1] = y;
                result[pixelOffset + 2] = y;
            }
        }

        return result;
    }

    /// <summary>
    /// Assembles color image from Y, Cb, Cr component blocks.
    /// Handles chroma upsampling for 4:2:0 subsampling.
    /// </summary>
    private byte[] AssembleColor(byte[][][] pixels)
    {
        int width = _frame.Width;
        int height = _frame.Height;
        var result = new byte[width * height * 3];

        // Get sampling factors
        JpegComponent yComp = _frame.Components[0];
        JpegComponent cbComp = _frame.Components[1];
        JpegComponent crComp = _frame.Components[2];

        int yBlocksPerRow = (_frame.Width + _frame.MaxHorizontalSamplingFactor * 8 - 1)
                           / (_frame.MaxHorizontalSamplingFactor * 8) * yComp.HorizontalSamplingFactor;
        int cbBlocksPerRow = (_frame.Width + _frame.MaxHorizontalSamplingFactor * 8 - 1)
                            / (_frame.MaxHorizontalSamplingFactor * 8) * cbComp.HorizontalSamplingFactor;
        int crBlocksPerRow = cbBlocksPerRow;

        // Calculate chroma subsampling ratio
        int chromaHRatio = yComp.HorizontalSamplingFactor / cbComp.HorizontalSamplingFactor;
        int chromaVRatio = yComp.VerticalSamplingFactor / cbComp.VerticalSamplingFactor;

        for (var imgY = 0; imgY < height; imgY++)
        {
            for (var imgX = 0; imgX < width; imgX++)
            {
                // Find Y value
                int yBlockX = imgX / 8;
                int yBlockY = imgY / 8;
                int yPixelX = imgX % 8;
                int yPixelY = imgY % 8;
                int yBlockIndex = yBlockY * yBlocksPerRow + yBlockX;

                byte y = (yBlockIndex < pixels[0].Length)
                    ? pixels[0][yBlockIndex][yPixelY * 8 + yPixelX]
                    : (byte)128;

                // Find Cb and Cr values (with potential upsampling)
                int chromaX = imgX / chromaHRatio;
                int chromaY = imgY / chromaVRatio;
                int cbBlockX = chromaX / 8;
                int cbBlockY = chromaY / 8;
                int cbPixelX = chromaX % 8;
                int cbPixelY = chromaY % 8;
                int cbBlockIndex = cbBlockY * cbBlocksPerRow + cbBlockX;

                byte cb = (cbBlockIndex < pixels[1].Length)
                    ? pixels[1][cbBlockIndex][cbPixelY * 8 + cbPixelX]
                    : (byte)128;

                byte cr = (cbBlockIndex < pixels[2].Length)
                    ? pixels[2][cbBlockIndex][cbPixelY * 8 + cbPixelX]
                    : (byte)128;

                // Convert YCbCr to RGB
                (byte r, byte g, byte b) = YCbCrToRgb(y, cb, cr);

                int pixelOffset = (imgY * width + imgX) * 3;
                result[pixelOffset] = r;
                result[pixelOffset + 1] = g;
                result[pixelOffset + 2] = b;
            }
        }

        return result;
    }

    /// <summary>
    /// Converts YCbCr color values to RGB.
    /// Uses the standard JPEG/JFIF conversion formula.
    /// </summary>
    public static (byte R, byte G, byte B) YCbCrToRgb(byte y, byte cb, byte cr)
    {
        // Standard JPEG YCbCr to RGB conversion
        // R = Y + 1.402 * (Cr - 128)
        // G = Y - 0.344136 * (Cb - 128) - 0.714136 * (Cr - 128)
        // B = Y + 1.772 * (Cb - 128)

        double yVal = y;
        double cbVal = cb - 128.0;
        double crVal = cr - 128.0;

        double r = yVal + 1.402 * crVal;
        double g = yVal - 0.344136 * cbVal - 0.714136 * crVal;
        double b = yVal + 1.772 * cbVal;

        return (
            (byte)Math.Clamp((int)Math.Round(r), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b), 0, 255)
        );
    }

    /// <summary>
    /// Gets the image width.
    /// </summary>
    public int Width => _frame.Width;

    /// <summary>
    /// Gets the image height.
    /// </summary>
    public int Height => _frame.Height;
}
