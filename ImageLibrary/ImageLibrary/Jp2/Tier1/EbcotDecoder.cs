using System;
using ImageLibrary.Jp2.Pipeline;

namespace ImageLibrary.Jp2.Tier1;

/// <summary>
/// EBCOT (Embedded Block Coding with Optimized Truncation) decoder.
/// Decodes quantized coefficients from code-block bitstreams.
/// </summary>
internal class EbcotDecoder : ITier1Decoder
{
    private readonly CodeBlockStyle _style;

    // Context indices for different coding operations
    // Per JPEG2000 spec: 0=Uniform, 1=RLC, 2-9=ZC, 10-13=SC, 14-16=MR
    internal const int CtxUniform = 0;
    internal const int CtxRunLength = 1;
    internal const int CtxSign = 10;  // Sign contexts are 10-13

    // Significance context lookup table
    // Index: [sumH][sumV][sumD][subband]
    // sumH = sum of horizontal neighbors (0, 1, 2)
    // sumV = sum of vertical neighbors (0, 1, 2)
    // sumD = sum of diagonal neighbors (0, 1, 2, 3, 4)
    // subband = 0 (HL), 1 (LH), 2 (HH/LL)
    private static readonly int[] SignificanceContexts = InitSignificanceContexts();

    // Sign context lookup
    private static readonly (int ctx, int xorBit)[] SignContextLookup = InitSignContexts();

    public EbcotDecoder(CodeBlockStyle style = CodeBlockStyle.None)
    {
        _style = style;
    }

    public int[,] Process(CodeBlockBitstream input)
    {
        if (input.Data == null || input.Data.Length == 0 || input.CodingPasses == 0)
        {
            return new int[input.Height, input.Width];
        }

        var decoder = new CodeBlockDecoder(
            input.Data,
            input.Width,
            input.Height,
            input.CodingPasses,
            input.ZeroBitPlanes,
            input.SubbandType,
            _style);

        return decoder.Decode();
    }

    private static int[] InitSignificanceContexts()
    {
        // Build significance context lookup table
        // Total size: 3 * 3 * 5 * 3 = 135 entries
        var table = new int[135];

        for (var h = 0; h <= 2; h++)
        {
            for (var v = 0; v <= 2; v++)
            {
                for (var d = 0; d <= 4; d++)
                {
                    for (var sub = 0; sub < 3; sub++)
                    {
                        int idx = ((h * 3 + v) * 5 + d) * 3 + sub;
                        table[idx] = ComputeSignificanceContext(h, v, d, sub);
                    }
                }
            }
        }

        return table;
    }

    private static int ComputeSignificanceContext(int sumH, int sumV, int sumD, int subband)
    {
        // From JPEG2000 spec Table D.1 and Melville/JJ2000 implementation
        // Return values are context indices BEFORE +2 offset (final contexts are 2-10)
        // subband: 0 = HL, 1 = LH, 2 = HH/LL

        if (subband == 0) // HL - horizontal primary (per Melville ZC_LUT_HL)
        {
            // 2 vertical (others irrelevant) → ctx 10
            if (sumV == 2) return 8; // 8+2=10
            // 1 vertical + horizontal (diagonal irrelevant) → ctx 9
            if (sumV == 1 && sumH >= 1) return 7; // 7+2=9
            // 1 vertical, no h, 1+ diagonal → ctx 8
            if (sumV == 1 && sumD >= 1) return 6; // 6+2=8
            // 1 vertical, no h, no d → ctx 7
            if (sumV == 1) return 5; // 5+2=7
            // 2 horizontal (vertical/diagonal irrelevant) → ctx 6
            if (sumH == 2) return 4; // 4+2=6
            // 1 horizontal (no vertical, diagonal irrelevant) → ctx 5
            if (sumH == 1) return 3; // 3+2=5
            // 2+ diagonal only → ctx 4
            if (sumD >= 2) return 2; // 2+2=4
            // 1 diagonal only → ctx 3
            if (sumD == 1) return 1; // 1+2=3
            // no neighbors → ctx 2
            return 0; // 0+2=2
        }

        if (subband == 1) // LH - vertical primary
        {
            // 2 horizontal (vert/diag irrelevant) → ctx 10
            if (sumH == 2) return 8; // 8+2=10
            // 1 horizontal + 1+ vertical (diag irrelevant) → ctx 9
            if (sumH == 1 && sumV >= 1) return 7; // 7+2=9
            // 1 horizontal + diagonal, no vertical → ctx 8
            if (sumH == 1 && sumD >= 1) return 6; // 6+2=8
            // 1 horizontal only → ctx 7
            if (sumH == 1) return 5; // 5+2=7
            // 2 vertical (diagonal irrelevant) → ctx 6
            if (sumV == 2) return 4; // 4+2=6
            // 1 vertical (diagonal irrelevant) → ctx 5
            if (sumV == 1) return 3; // 3+2=5
            // 2+ diagonal only → ctx 4
            if (sumD >= 2) return 2; // 2+2=4
            // 1 diagonal only → ctx 3
            if (sumD == 1) return 1; // 1+2=3
            // no neighbors → ctx 2
            return 0; // 0+2=2
        }

        // HH or LL
        int sumHV = sumH + sumV;
        if (sumD >= 3) return 8;
        if (sumD == 2)
        {
            if (sumHV >= 1) return 7;
            return 6;
        }
        if (sumD == 1)
        {
            if (sumHV >= 2) return 5;
            if (sumHV == 1) return 4;
            return 3;
        }
        // sumD == 0
        if (sumHV >= 2) return 2;
        if (sumHV == 1) return 1;
        return 0;
    }

    private static (int ctx, int xorBit)[] InitSignContexts()
    {
        // Build sign context lookup (Table D.3)
        // Index: contribution from horizontal and vertical neighbors
        // hc, vc range from -1 to +1, encoded as 0, 1, 2
        // SC contexts are 11-15 (spec contexts 9-13 + 2 offset for uniform/RLC)
        // XOR bit flips the decoded sign when hc < 0 or vc < 0 (but not hc > 0 or vc > 0)
        var table = new (int, int)[9];

        // (hc, vc) -> index: (hc+1)*3 + (vc+1)
        table[0 * 3 + 0] = (15, 1); // (-1, -1) -> context 15, xor=1
        table[0 * 3 + 1] = (14, 1); // (-1, 0)  -> context 14, xor=1
        table[0 * 3 + 2] = (13, 1); // (-1, +1) -> context 13, xor=1
        table[1 * 3 + 0] = (12, 1); // (0, -1)  -> context 12, xor=1
        table[1 * 3 + 1] = (11, 0); // (0, 0)   -> context 11, xor=0
        table[1 * 3 + 2] = (12, 0); // (0, +1)  -> context 12, xor=0
        table[2 * 3 + 0] = (13, 0); // (+1, -1) -> context 13, xor=0
        table[2 * 3 + 1] = (14, 0); // (+1, 0)  -> context 14, xor=0
        table[2 * 3 + 2] = (15, 0); // (+1, +1) -> context 15, xor=0

        return table;
    }

    internal static int GetSignificanceContext(int h, int v, int d, int subband)
    {
        h = Math.Min(h, 2);
        v = Math.Min(v, 2);
        d = Math.Min(d, 4);
        int idx = ((h * 3 + v) * 5 + d) * 3 + subband;
        // ZC contexts are 2-9, so add 2 to the 0-8 range from the lookup table
        return SignificanceContexts[idx] + 2;
    }

    internal static (int ctx, int xorBit) GetSignContext(int hc, int vc)
    {
        // hc, vc are -1, 0, or +1
        int idx = (hc + 1) * 3 + (vc + 1);
        return SignContextLookup[idx];
    }
}

/// <summary>
/// Decodes a single code-block.
/// </summary>
internal class CodeBlockDecoder
{
    private readonly byte[] _data;
    private readonly int _width;
    private readonly int _height;
    private readonly int _numPasses;
    private readonly int _zeroBitPlanes;
    private readonly int _zcSubband;  // ZC context subband: 0=HL, 1=LH/LL, 2=HH
    private readonly CodeBlockStyle _style;

    private MqDecoder _mq;
    private int[,] _coefficients;
    private byte[,] _state; // Bit flags for each sample

    // State flags
    private const byte SigCurrent = 0x01;
    private const byte SigNeighbor = 0x02;
    private const byte FirstRef = 0x04;
    private const byte Visited = 0x08;

    public CodeBlockDecoder(byte[] data, int width, int height, int numPasses, int zeroBitPlanes,
        SubbandType subbandType, CodeBlockStyle style)
    {
        _data = data;
        _width = width;
        _height = height;
        _numPasses = numPasses;
        _zeroBitPlanes = zeroBitPlanes;
        _style = style;

        // Map SubbandType to ZC context subband index
        // HL -> 0, LH -> 1, LL -> 1 (same as LH!), HH -> 2
        _zcSubband = subbandType switch
        {
            SubbandType.HL => 0,
            SubbandType.LH => 1,
            SubbandType.LL => 1,  // LL uses LH table, not HH
            SubbandType.HH => 2,
            _ => 2  // default to HH
        };

        _coefficients = new int[height, width];
        _state = new byte[height + 2, width + 2]; // Padding for neighbor access
        _mq = new MqDecoder(data);
    }

    public int[,] Decode()
    {
        // Use absolute bit positions to match Melville/JJ2000 representation.
        // Coefficients are stored with MSB at position (30 - zeroBitPlanes).
        // This allows comparison at the Tier-1 stage before dequantization.
        //
        // The first coded bit-plane starts at 30 - zeroBitPlanes.
        // Each bit-plane has up to 3 passes: significance propagation, magnitude refinement, cleanup.
        // First bit-plane only has cleanup pass.

        var passIdx = 0;
        int bitPlane = 30 - _zeroBitPlanes;  // Absolute bit position
        var passInPlane = 2; // Start with cleanup pass for first bit-plane

        while (passIdx < _numPasses && bitPlane >= 0)
        {
            switch (passInPlane)
            {
                case 0:
                    SignificancePropagationPass(bitPlane);
                    break;
                case 1:
                    MagnitudeRefinementPass(bitPlane);
                    break;
                case 2:
                    CleanupPass(bitPlane);
                    break;
            }

            passIdx++;
            passInPlane++;
            if (passInPlane > 2)
            {
                passInPlane = 0;
                bitPlane--;
            }

            // Check for termination
            if (_mq.MarkerFound)
                break;
        }

        return _coefficients;
    }

    private void SignificancePropagationPass(int bitPlane)
    {
        // Midpoint reconstruction: use 1.5 * 2^bp = (3 << bp) >> 1
        // Use sign-magnitude format to match Melville: (sign << 31) | magnitude
        var setmask = (int)(((long)3 << bitPlane) >> 1);

        // Scan in vertical stripes (4 rows at a time)
        // Per spec: within stripe, columns left-to-right, within column top-to-bottom
        for (var stripeY = 0; stripeY < _height; stripeY += 4)
        {
            int stripHeight = Math.Min(4, _height - stripeY);

            // Within stripe: column by column (x), then row within column (dy)
            for (var x = 0; x < _width; x++)
            {
                for (var dy = 0; dy < stripHeight; dy++)
                {
                    int y = stripeY + dy;
                    byte state = _state[y + 1, x + 1];

                    // Skip if already significant or not in preferred neighborhood
                    if ((state & SigCurrent) != 0)
                        continue;
                    if ((state & SigNeighbor) == 0)
                        continue;

                    // Decode significance
                    int ctx = GetSigContext(x, y);
                    int sig = _mq.Decode(ctx);


                    if (sig == 1)
                    {
                        // Became significant - decode sign and set with midpoint reconstruction
                        // Use sign-magnitude format: (sign << 31) | magnitude
                        int sign = DecodeSign(x, y);
                        _coefficients[y, x] = (sign << 31) | setmask;
                        SetSignificant(x, y);
                    }

                    _state[y + 1, x + 1] |= Visited;
                }
            }
        }

        // Note: Do NOT clear Visited here - MagnitudeRefinementPass needs to see
        // which samples were visited. Visited is cleared at end of CleanupPass.
    }

    private void MagnitudeRefinementPass(int bitPlane)
    {
        // For midpoint reconstruction using sign-magnitude format:
        // - resetmask clears bits bp and below (preserves sign bit 31)
        // - setmask is the new midpoint at (bp-1)
        int setmask = (1 << bitPlane) >> 1;  // New midpoint = 2^(bp-1)
        int resetmask = (-1) << (bitPlane + 1);  // Clears bits 0 through bitPlane

        // Scan in vertical stripes (4 rows at a time)
        // Per spec: within stripe, columns left-to-right, within column top-to-bottom
        for (var stripeY = 0; stripeY < _height; stripeY += 4)
        {
            int stripHeight = Math.Min(4, _height - stripeY);

            // Within stripe: column by column (x), then row within column (dy)
            for (var x = 0; x < _width; x++)
            {
                for (var dy = 0; dy < stripHeight; dy++)
                {
                    int y = stripeY + dy;
                    byte state = _state[y + 1, x + 1];

                    // Only process already-significant samples
                    if ((state & SigCurrent) == 0)
                        continue;
                    if ((state & Visited) != 0)
                        continue;

                    // Determine refinement context (16, 17, or 18)
                    int ctx = GetMagRefContext(x, y);
                    int refineBit = _mq.Decode(ctx);


                    // Update coefficient using sign-magnitude format
                    // Clear old approximation, set decoded bit, add new midpoint
                    int coef = _coefficients[y, x];
                    coef &= resetmask;  // Clear old midpoint (preserves sign bit)
                    coef |= (refineBit << bitPlane) | setmask;  // Set refinement bit + new midpoint
                    _coefficients[y, x] = coef;

                    _state[y + 1, x + 1] |= FirstRef; // Mark as refined
                }
            }
        }
    }

    private void CleanupPass(int bitPlane)
    {
        // Midpoint reconstruction: use 1.5 * 2^bp = (3 << bp) >> 1
        // Use sign-magnitude format to match Melville: (sign << 31) | magnitude
        var setmask = (int)(((long)3 << bitPlane) >> 1);

        for (var y = 0; y < _height; y += 4)
        {
            for (var x = 0; x < _width; x++)
            {
                int stripHeight = Math.Min(4, _height - y);

                // Check for run-length coding opportunity
                var canUseRunLength = true;
                for (var dy = 0; dy < stripHeight && canUseRunLength; dy++)
                {
                    byte state = _state[y + dy + 1, x + 1];
                    if ((state & (SigCurrent | SigNeighbor | Visited)) != 0)
                        canUseRunLength = false;
                }

                if (canUseRunLength && stripHeight == 4)
                {
                    // Try run-length mode
                    int runLength = _mq.Decode(EbcotDecoder.CtxRunLength);
                    if (runLength == 0)
                    {
                        // All four samples are insignificant
                        continue;
                    }

                    // Decode which sample becomes significant
                    int uniformBits = _mq.Decode(EbcotDecoder.CtxUniform) << 1;
                    uniformBits |= _mq.Decode(EbcotDecoder.CtxUniform);
                    int firstSig = uniformBits;

                    // Mark first significant sample with sign-magnitude format
                    int sign = DecodeSign(x, y + firstSig);
                    _coefficients[y + firstSig, x] = (sign << 31) | setmask;
                    SetSignificant(x, y + firstSig);

                    // Process remaining samples in strip
                    for (int dy = firstSig + 1; dy < stripHeight; dy++)
                    {
                        ProcessCleanupSample(x, y + dy, setmask);
                    }
                }
                else
                {
                    // Process each sample individually
                    for (var dy = 0; dy < stripHeight; dy++)
                    {
                        ProcessCleanupSample(x, y + dy, setmask);
                    }
                }
            }
        }

        ClearVisited();
    }

    private void ProcessCleanupSample(int x, int y, int setmask)
    {
        byte state = _state[y + 1, x + 1];

        if ((state & (SigCurrent | Visited)) != 0)
        {
            return;
        }

        int ctx = GetSigContext(x, y);
        int sig = _mq.Decode(ctx);


        if (sig == 1)
        {
            int sign = DecodeSign(x, y);
            _coefficients[y, x] = (sign << 31) | setmask;
            SetSignificant(x, y);
        }
    }

    private int DecodeSign(int x, int y)
    {
        // Calculate horizontal and vertical contributions
        int leftC = GetSignContribution(x - 1, y);
        int rightC = GetSignContribution(x + 1, y);
        int upC = GetSignContribution(x, y - 1);
        int downC = GetSignContribution(x, y + 1);

        // Sign context uses SUM of contributions (not difference!)
        // Per JPEG2000 spec and Melville/JJ2000 implementation
        int hc = leftC + rightC;
        int vc = upC + downC;

        // Clamp to [-1, 1]
        hc = Math.Max(-1, Math.Min(1, hc));
        vc = Math.Max(-1, Math.Min(1, vc));

        (int ctx, int xorBit) = EbcotDecoder.GetSignContext(hc, vc);

        int signBit = _mq.Decode(ctx);
        int result = signBit ^ xorBit;


        return result;
    }

    private int GetSignContribution(int x, int y)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return 0;

        byte state = _state[y + 1, x + 1];
        if ((state & SigCurrent) == 0)
            return 0;

        // Sign is in bit 31 (sign-magnitude format)
        // Bit 31 = 0 means positive, bit 31 = 1 means negative
        return (_coefficients[y, x] & unchecked((int)0x80000000)) == 0 ? 1 : -1;
    }

    private void SetSignificant(int x, int y)
    {
        _state[y + 1, x + 1] |= SigCurrent;

        // Update neighbor flags
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                _state[y + 1 + dy, x + 1 + dx] |= SigNeighbor;
            }
        }
    }

    private int GetSigContext(int x, int y)
    {
        // Count significant neighbors
        int h = 0, v = 0, d = 0;

        // Horizontal
        if (x > 0 && (_state[y + 1, x] & SigCurrent) != 0) h++;
        if (x < _width - 1 && (_state[y + 1, x + 2] & SigCurrent) != 0) h++;

        // Vertical
        if (y > 0 && (_state[y, x + 1] & SigCurrent) != 0) v++;
        if (y < _height - 1 && (_state[y + 2, x + 1] & SigCurrent) != 0) v++;

        // Diagonal
        if (x > 0 && y > 0 && (_state[y, x] & SigCurrent) != 0) d++;
        if (x < _width - 1 && y > 0 && (_state[y, x + 2] & SigCurrent) != 0) d++;
        if (x > 0 && y < _height - 1 && (_state[y + 2, x] & SigCurrent) != 0) d++;
        if (x < _width - 1 && y < _height - 1 && (_state[y + 2, x + 2] & SigCurrent) != 0) d++;

        // Use subband-specific context: 0=HL, 1=LH/LL, 2=HH
        return EbcotDecoder.GetSignificanceContext(h, v, d, _zcSubband);
    }

    private int GetMagRefContext(int x, int y)
    {
        byte state = _state[y + 1, x + 1];

        // MR contexts are 16-18 (spec contexts 14-16 + 2 offset)
        // Already refined (FirstRef set): context 18
        // First refinement with neighbors: context 17
        // First refinement without neighbors: context 16
        if ((state & FirstRef) != 0)
            return 18;

        // Check for any significant neighbors
        bool hasNeighbor = (state & SigNeighbor) != 0;
        return hasNeighbor ? 17 : 16;
    }

    private void ClearVisited()
    {
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                _state[y + 1, x + 1] &= unchecked((byte)~Visited);
            }
        }
    }
}