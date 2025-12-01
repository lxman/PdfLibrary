using System;
using System.IO;

namespace Compressors.Jpeg2k;

/// <summary>
/// EBCOT Tier-1 encoder for JPEG2000.
/// Implements bit-plane coding with context-based arithmetic coding.
/// Based on ITU-T T.800 Annex D.
/// </summary>
public static class EbcotTier1
{
    /// <summary>
    /// Encodes a code-block using EBCOT tier-1 coding.
    /// </summary>
    public static void EncodeBlock(CodeBlock block)
    {
        if (block.Coefficients == null || block.NumBitPlanes == 0)
        {
            block.EncodedData = Array.Empty<byte>();
            block.NumPasses = 0;
            return;
        }

        using var memStream = new MemoryStream();
        using var mqEncoder = new MQEncoder(memStream);

        int width = block.Width;
        int height = block.Height;

        // State arrays
        var significance = new bool[height + 2, width + 2];  // Padded for neighbor access
        var refinement = new bool[height + 2, width + 2];    // Has been refined

        int totalPasses = block.NumBitPlanes * 3;
        var passLengths = new int[totalPasses];
        int passIndex = 0;

        // Encode each bit-plane from MSB to LSB
        for (int bitPlane = block.NumBitPlanes - 1; bitPlane >= 0; bitPlane--)
        {
            int bitMask = 1 << bitPlane;
            long startPos = memStream.Position;

            // Pass 1: Significance Propagation
            EncodeSigPropPass(block, mqEncoder, significance, bitPlane, bitMask);
            passLengths[passIndex++] = (int)(memStream.Position - startPos);
            startPos = memStream.Position;

            // Pass 2: Magnitude Refinement
            EncodeMagRefPass(block, mqEncoder, significance, refinement, bitPlane, bitMask);
            passLengths[passIndex++] = (int)(memStream.Position - startPos);
            startPos = memStream.Position;

            // Pass 3: Cleanup
            EncodeCleanupPass(block, mqEncoder, significance, bitPlane, bitMask);
            passLengths[passIndex++] = (int)(memStream.Position - startPos);

            // Update refinement state
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (significance[y + 1, x + 1])
                    {
                        refinement[y + 1, x + 1] = true;
                    }
                }
            }
        }

        mqEncoder.Flush();

        block.EncodedData = memStream.ToArray();
        block.NumPasses = passIndex;
        block.PassLengths = passLengths;
    }

    /// <summary>
    /// Significance Propagation Pass.
    /// Codes samples that have at least one significant neighbor.
    /// </summary>
    private static void EncodeSigPropPass(
        CodeBlock block,
        MQEncoder mq,
        bool[,] significance,
        int bitPlane,
        int bitMask)
    {
        int width = block.Width;
        int height = block.Height;
        var coeffs = block.Coefficients!;
        var signs = block.Signs!;

        // Process in stripe order (4 rows at a time)
        for (int stripeY = 0; stripeY < height; stripeY += 4)
        {
            int stripeHeight = Math.Min(4, height - stripeY);

            for (int x = 0; x < width; x++)
            {
                for (int dy = 0; dy < stripeHeight; dy++)
                {
                    int y = stripeY + dy;
                    int sy = y + 1;  // Padded index
                    int sx = x + 1;

                    // Skip if already significant
                    if (significance[sy, sx])
                        continue;

                    // Check if any neighbor is significant
                    int sigNeighbors = CountSignificantNeighbors(significance, sy, sx);
                    if (sigNeighbors == 0)
                        continue;

                    // Get context for significance coding
                    int context = GetSigContext(significance, sy, sx, block.Subband.Type);

                    // Code significance
                    int bit = (coeffs[y, x] & bitMask) != 0 ? 1 : 0;
                    mq.Encode(context, bit);

                    if (bit == 1)
                    {
                        significance[sy, sx] = true;

                        // Code sign
                        int signContext = GetSignContext(significance, signs, sy, sx, block.Subband.Type);
                        int signBit = signs[y, x] ? 1 : 0;

                        // XOR with predicted sign
                        int predSign = GetPredictedSign(significance, signs, sy, sx);
                        mq.Encode(Jp2kConstants.Contexts.SignFirst + signContext, signBit ^ predSign);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Magnitude Refinement Pass.
    /// Refines samples that became significant in previous bit-planes.
    /// </summary>
    private static void EncodeMagRefPass(
        CodeBlock block,
        MQEncoder mq,
        bool[,] significance,
        bool[,] refinement,
        int bitPlane,
        int bitMask)
    {
        int width = block.Width;
        int height = block.Height;
        var coeffs = block.Coefficients!;

        for (int stripeY = 0; stripeY < height; stripeY += 4)
        {
            int stripeHeight = Math.Min(4, height - stripeY);

            for (int x = 0; x < width; x++)
            {
                for (int dy = 0; dy < stripeHeight; dy++)
                {
                    int y = stripeY + dy;
                    int sy = y + 1;
                    int sx = x + 1;

                    // Only refine previously significant samples
                    if (!significance[sy, sx] || !refinement[sy, sx])
                        continue;

                    // Get refinement context
                    int context = GetMagRefContext(refinement, sy, sx);

                    // Code the bit
                    int bit = (coeffs[y, x] & bitMask) != 0 ? 1 : 0;
                    mq.Encode(Jp2kConstants.Contexts.MagRefFirst + context, bit);
                }
            }
        }
    }

    /// <summary>
    /// Cleanup Pass.
    /// Codes all remaining samples not coded in previous passes.
    /// </summary>
    private static void EncodeCleanupPass(
        CodeBlock block,
        MQEncoder mq,
        bool[,] significance,
        int bitPlane,
        int bitMask)
    {
        int width = block.Width;
        int height = block.Height;
        var coeffs = block.Coefficients!;
        var signs = block.Signs!;

        for (int stripeY = 0; stripeY < height; stripeY += 4)
        {
            int stripeHeight = Math.Min(4, height - stripeY);

            for (int x = 0; x < width; x++)
            {
                // Check for run-length coding opportunity
                bool canRunLength = true;
                int runCount = 0;

                for (int dy = 0; dy < stripeHeight && canRunLength; dy++)
                {
                    int y = stripeY + dy;
                    int sy = y + 1;
                    int sx = x + 1;

                    if (significance[sy, sx])
                    {
                        canRunLength = false;
                    }
                    else if (CountSignificantNeighbors(significance, sy, sx) > 0)
                    {
                        canRunLength = false;
                    }
                    else
                    {
                        runCount++;
                    }
                }

                // Use run-length coding if possible
                if (canRunLength && runCount == 4)
                {
                    // Check if all 4 are zero at this bit-plane
                    bool allZero = true;
                    for (int dy = 0; dy < 4 && allZero; dy++)
                    {
                        int y = stripeY + dy;
                        if (y < height && (coeffs[y, x] & bitMask) != 0)
                        {
                            allZero = false;
                        }
                    }

                    // Encode run-length decision
                    mq.Encode(Jp2kConstants.Contexts.RunLength, allZero ? 0 : 1);

                    if (allZero)
                        continue;
                }

                // Code samples individually
                for (int dy = 0; dy < stripeHeight; dy++)
                {
                    int y = stripeY + dy;
                    int sy = y + 1;
                    int sx = x + 1;

                    if (significance[sy, sx])
                        continue;

                    if (CountSignificantNeighbors(significance, sy, sx) > 0)
                        continue;  // Was coded in sig prop pass

                    // Get context
                    int context = GetSigContext(significance, sy, sx, block.Subband.Type);

                    // Code significance
                    int bit = (coeffs[y, x] & bitMask) != 0 ? 1 : 0;
                    mq.Encode(context, bit);

                    if (bit == 1)
                    {
                        significance[sy, sx] = true;

                        // Code sign
                        int signContext = GetSignContext(significance, signs, sy, sx, block.Subband.Type);
                        int signBit = signs[y, x] ? 1 : 0;
                        int predSign = GetPredictedSign(significance, signs, sy, sx);
                        mq.Encode(Jp2kConstants.Contexts.SignFirst + signContext, signBit ^ predSign);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Counts significant neighbors (8-connected).
    /// </summary>
    private static int CountSignificantNeighbors(bool[,] significance, int y, int x)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dy == 0 && dx == 0) continue;
                if (significance[y + dy, x + dx]) count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Gets the significance context based on neighbor configuration.
    /// </summary>
    private static int GetSigContext(bool[,] sig, int y, int x, int subbandType)
    {
        // Count horizontal, vertical, and diagonal neighbors
        int h = (sig[y, x - 1] ? 1 : 0) + (sig[y, x + 1] ? 1 : 0);
        int v = (sig[y - 1, x] ? 1 : 0) + (sig[y + 1, x] ? 1 : 0);
        int d = (sig[y - 1, x - 1] ? 1 : 0) + (sig[y - 1, x + 1] ? 1 : 0) +
                (sig[y + 1, x - 1] ? 1 : 0) + (sig[y + 1, x + 1] ? 1 : 0);

        // Context depends on subband type (simplified version)
        int baseContext;
        if (subbandType == Jp2kConstants.SubbandHL)
        {
            // HL: horizontal detail - weight vertical neighbors more
            baseContext = Math.Min(v * 2 + h + (d > 0 ? 1 : 0), 8);
        }
        else if (subbandType == Jp2kConstants.SubbandLH)
        {
            // LH: vertical detail - weight horizontal neighbors more
            baseContext = Math.Min(h * 2 + v + (d > 0 ? 1 : 0), 8);
        }
        else if (subbandType == Jp2kConstants.SubbandHH)
        {
            // HH: diagonal detail
            baseContext = Math.Min(d + h + v, 8);
        }
        else
        {
            // LL: approximation
            baseContext = Math.Min(h + v + (d > 0 ? 1 : 0), 8);
        }

        return baseContext;
    }

    /// <summary>
    /// Gets the sign coding context.
    /// Note: sig is padded (height+2 x width+2), signs is not padded (height x width).
    /// sig[y, x] corresponds to signs[y-1, x-1].
    /// </summary>
    private static int GetSignContext(bool[,] sig, bool[,] signs, int y, int x, int subbandType)
    {
        int height = signs.GetLength(0);
        int width = signs.GetLength(1);

        // Simplified sign context based on neighbors
        int hContrib = 0;
        int vContrib = 0;

        // Left neighbor: sig[y, x-1] -> signs[y-1, x-2]
        if (sig[y, x - 1] && x - 2 >= 0)
            hContrib += signs[y - 1, x - 2] ? -1 : 1;
        // Right neighbor: sig[y, x+1] -> signs[y-1, x]
        if (sig[y, x + 1] && x < width)
            hContrib += signs[y - 1, x] ? -1 : 1;
        // Top neighbor: sig[y-1, x] -> signs[y-2, x-1]
        if (sig[y - 1, x] && y - 2 >= 0)
            vContrib += signs[y - 2, x - 1] ? -1 : 1;
        // Bottom neighbor: sig[y+1, x] -> signs[y, x-1]
        if (sig[y + 1, x] && y < height)
            vContrib += signs[y, x - 1] ? -1 : 1;

        // Map to context 0-4
        int context = 0;
        if (hContrib > 0) context += 1;
        else if (hContrib < 0) context += 2;
        if (vContrib > 0) context += 1;
        else if (vContrib < 0) context += 2;

        return Math.Min(context, 4);
    }

    /// <summary>
    /// Gets the predicted sign based on neighbors.
    /// Note: sig is padded (height+2 x width+2), signs is not padded (height x width).
    /// </summary>
    private static int GetPredictedSign(bool[,] sig, bool[,] signs, int y, int x)
    {
        int height = signs.GetLength(0);
        int width = signs.GetLength(1);

        // Simple prediction: majority vote of significant neighbors
        int positiveCount = 0;
        int negativeCount = 0;

        // Left neighbor
        if (sig[y, x - 1] && x - 2 >= 0)
        {
            if (signs[y - 1, x - 2]) negativeCount++;
            else positiveCount++;
        }
        // Right neighbor
        if (sig[y, x + 1] && x < width)
        {
            if (signs[y - 1, x]) negativeCount++;
            else positiveCount++;
        }
        // Top neighbor
        if (sig[y - 1, x] && y - 2 >= 0)
        {
            if (signs[y - 2, x - 1]) negativeCount++;
            else positiveCount++;
        }
        // Bottom neighbor
        if (sig[y + 1, x] && y < height)
        {
            if (signs[y, x - 1]) negativeCount++;
            else positiveCount++;
        }

        return negativeCount > positiveCount ? 1 : 0;
    }

    /// <summary>
    /// Gets the magnitude refinement context.
    /// </summary>
    private static int GetMagRefContext(bool[,] refinement, int y, int x)
    {
        // Check if this is the first refinement
        bool hasRefinedNeighbor =
            refinement[y - 1, x] || refinement[y + 1, x] ||
            refinement[y, x - 1] || refinement[y, x + 1];

        return hasRefinedNeighbor ? 1 : 0;
    }

    /// <summary>
    /// Decodes a code-block using EBCOT tier-1 decoding.
    /// </summary>
    public static void DecodeBlock(CodeBlock block, byte[] data, int numPasses)
    {
        if (data.Length == 0 || numPasses == 0)
        {
            return;
        }

        block.Initialize();
        var coeffs = block.Coefficients!;
        var signs = block.Signs!;

        using var memStream = new MemoryStream(data);
        var mqDecoder = new MQDecoder(memStream);

        int width = block.Width;
        int height = block.Height;

        var significance = new bool[height + 2, width + 2];
        var refinement = new bool[height + 2, width + 2];

        int passesPerPlane = 3;
        int numBitPlanes = (numPasses + passesPerPlane - 1) / passesPerPlane;
        block.NumBitPlanes = numBitPlanes;

        int passIndex = 0;
        for (int bitPlane = numBitPlanes - 1; bitPlane >= 0 && passIndex < numPasses; bitPlane--)
        {
            int bitMask = 1 << bitPlane;

            // Decode passes for this bit-plane
            if (passIndex < numPasses)
            {
                DecodeSigPropPass(block, mqDecoder, significance, signs, bitPlane, bitMask);
                passIndex++;
            }

            if (passIndex < numPasses)
            {
                DecodeMagRefPass(block, mqDecoder, significance, refinement, bitPlane, bitMask);
                passIndex++;
            }

            if (passIndex < numPasses)
            {
                DecodeCleanupPass(block, mqDecoder, significance, signs, bitPlane, bitMask);
                passIndex++;
            }

            // Update refinement state
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (significance[y + 1, x + 1])
                    {
                        refinement[y + 1, x + 1] = true;
                    }
                }
            }
        }
    }

    private static void DecodeSigPropPass(
        CodeBlock block, MQDecoder mq, bool[,] significance, bool[,] signs,
        int bitPlane, int bitMask)
    {
        int width = block.Width;
        int height = block.Height;
        var coeffs = block.Coefficients!;

        for (int stripeY = 0; stripeY < height; stripeY += 4)
        {
            int stripeHeight = Math.Min(4, height - stripeY);

            for (int x = 0; x < width; x++)
            {
                for (int dy = 0; dy < stripeHeight; dy++)
                {
                    int y = stripeY + dy;
                    int sy = y + 1;
                    int sx = x + 1;

                    if (significance[sy, sx])
                        continue;

                    if (CountSignificantNeighbors(significance, sy, sx) == 0)
                        continue;

                    int context = GetSigContext(significance, sy, sx, block.Subband.Type);
                    int bit = mq.Decode(context);

                    if (bit == 1)
                    {
                        significance[sy, sx] = true;
                        coeffs[y, x] |= bitMask;

                        int signContext = GetSignContext(significance, signs, sy, sx, block.Subband.Type);
                        int predSign = GetPredictedSign(significance, signs, sy, sx);
                        int signBit = mq.Decode(Jp2kConstants.Contexts.SignFirst + signContext);
                        signs[y, x] = (signBit ^ predSign) == 1;
                    }
                }
            }
        }
    }

    private static void DecodeMagRefPass(
        CodeBlock block, MQDecoder mq, bool[,] significance, bool[,] refinement,
        int bitPlane, int bitMask)
    {
        int width = block.Width;
        int height = block.Height;
        var coeffs = block.Coefficients!;

        for (int stripeY = 0; stripeY < height; stripeY += 4)
        {
            int stripeHeight = Math.Min(4, height - stripeY);

            for (int x = 0; x < width; x++)
            {
                for (int dy = 0; dy < stripeHeight; dy++)
                {
                    int y = stripeY + dy;
                    int sy = y + 1;
                    int sx = x + 1;

                    if (!significance[sy, sx] || !refinement[sy, sx])
                        continue;

                    int context = GetMagRefContext(refinement, sy, sx);
                    int bit = mq.Decode(Jp2kConstants.Contexts.MagRefFirst + context);

                    if (bit == 1)
                    {
                        coeffs[y, x] |= bitMask;
                    }
                }
            }
        }
    }

    private static void DecodeCleanupPass(
        CodeBlock block, MQDecoder mq, bool[,] significance, bool[,] signs,
        int bitPlane, int bitMask)
    {
        int width = block.Width;
        int height = block.Height;
        var coeffs = block.Coefficients!;

        for (int stripeY = 0; stripeY < height; stripeY += 4)
        {
            int stripeHeight = Math.Min(4, height - stripeY);

            for (int x = 0; x < width; x++)
            {
                for (int dy = 0; dy < stripeHeight; dy++)
                {
                    int y = stripeY + dy;
                    int sy = y + 1;
                    int sx = x + 1;

                    if (significance[sy, sx])
                        continue;

                    if (CountSignificantNeighbors(significance, sy, sx) > 0)
                        continue;

                    int context = GetSigContext(significance, sy, sx, block.Subband.Type);
                    int bit = mq.Decode(context);

                    if (bit == 1)
                    {
                        significance[sy, sx] = true;
                        coeffs[y, x] |= bitMask;

                        int signContext = GetSignContext(significance, signs, sy, sx, block.Subband.Type);
                        int predSign = GetPredictedSign(significance, signs, sy, sx);
                        int signBit = mq.Decode(Jp2kConstants.Contexts.SignFirst + signContext);
                        signs[y, x] = (signBit ^ predSign) == 1;
                    }
                }
            }
        }
    }
}
