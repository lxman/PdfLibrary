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
        return Process(input, -1, -1);
    }

    public int[,] Process(CodeBlockBitstream input, int resolution, int subband)
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
            _style,
            resolution,
            subband,
            input.BlockX,
            input.BlockY);

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

    // Debug logging
    private readonly bool _isDebugBlock;
    private readonly int _resolution;
    private readonly int _subband;
    private readonly int _blockX;
    private readonly int _blockY;
    private System.IO.StreamWriter? _debugLog;

    private MqDecoder _mq;
    private int[,] _coefficients;
    private byte[,] _state; // Bit flags for each sample

    // State flags
    private const byte SigCurrent = 0x01;
    private const byte SigNeighbor = 0x02;
    private const byte FirstRef = 0x04;
    private const byte Visited = 0x08;

    public CodeBlockDecoder(byte[] data, int width, int height, int numPasses, int zeroBitPlanes,
        SubbandType subbandType, CodeBlockStyle style, int resolution = -1, int subband = -1,
        int blockX = -1, int blockY = -1)
    {
        _data = data;
        _width = width;
        _height = height;
        _numPasses = numPasses;
        _zeroBitPlanes = zeroBitPlanes;
        _style = style;
        _resolution = resolution;
        _subband = subband;
        _blockX = blockX;
        _blockY = blockY;

        // Check if this is the debug block - using CB(0,0) to test refinement passes
        _isDebugBlock = (resolution == 0 && subband == 0 && blockX == 0 && blockY == 0);

        if (_isDebugBlock)
        {
            _debugLog = new System.IO.StreamWriter(@"C:\temp\our-pass-results.txt", append: false);
            _debugLog.WriteLine($"=== IMAGELIBRARY PASS-BY-PASS COEFFICIENT RESULTS ===");
            _debugLog.WriteLine($"Resolution={resolution}, Subband={subband}, CB({blockX},{blockY})");
            _debugLog.WriteLine();
        }

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
        _mq = new MqDecoder(data, null);  // Don't log MQ operations to pass-results file

        // Log MQ initialization state for all code blocks
        using var mqLog = new System.IO.StreamWriter(@"C:\temp\our-mq-init.txt", append: true);
        mqLog.WriteLine($"MQ_INIT: R={resolution} S={subband} CB({blockX},{blockY}) A=0x{_mq.A:X4} C=0x{_mq.C:X8} CT={_mq.CT} B=0x{_mq.B:X2}");
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

        if (_isDebugBlock)
        {
            _debugLog!.WriteLine($"Starting decode: first bit-plane={bitPlane}, starting with cleanup pass");
            _debugLog.WriteLine();
        }

        while (passIdx < _numPasses && bitPlane >= 0)
        {
            string passName = passInPlane switch
            {
                0 => "Significance Propagation",
                1 => "Magnitude Refinement",
                2 => "Cleanup",
                _ => "Unknown"
            };

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

            if (_isDebugBlock)
            {
                LogPassResult(passIdx, passInPlane, bitPlane, passName);

                // After first Cleanup pass, dump all samples with SigNeighbor flag
                if (passIdx == 0 && passInPlane == 2)
                {
                    LogSigNeighborFlags();
                }
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
            {
                if (_isDebugBlock)
                {
                    _debugLog!.WriteLine($"Marker found after pass {passIdx}, terminating decode");
                }
                break;
            }
        }

        if (_isDebugBlock)
        {
            _debugLog!.WriteLine();
            _debugLog.WriteLine("=== FINAL COEFFICIENTS ===");
            _debugLog.Write("First row: ");
            int maxCols = Math.Min(32, _width);
            for (var x = 0; x < maxCols; x++)
            {
                _debugLog.Write($"0x{_coefficients[0, x]:X8} ");
            }
            _debugLog.WriteLine();
            _debugLog.Close();
        }

        return _coefficients;
    }

    private void SignificancePropagationPass(int bitPlane)
    {
        // Midpoint reconstruction: use 1.5 * 2^bp = (3 << bp) >> 1
        // Use sign-magnitude format to match Melville: (sign << 31) | magnitude
        var setmask = (int)(((long)3 << bitPlane) >> 1);

        // Track newly significant samples - store position AND coefficient value
        // We defer setting SigCurrent until after the pass to prevent context contamination
        var newlySignificant = new System.Collections.Generic.List<(int x, int y, int coef)>();

        // Statistics tracking
        int samplesWithSigNeighbor = 0;
        int samplesAlreadySignificant = 0;
        int samplesTested = 0;
        int samplesBecomingSignificant = 0;

        _debugLog?.WriteLine($"SignificancePropagationPass: bitPlane={bitPlane}, setmask=0x{setmask:X8}");

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
                    {
                        samplesAlreadySignificant++;
                        continue;
                    }
                    if ((state & SigNeighbor) == 0)
                    {
                        continue;
                    }

                    samplesWithSigNeighbor++;

                    // Decode significance (using SNAPSHOT state - only sees samples significant at pass start)
                    int ctx = GetSigContext(x, y);
                    int sig = _mq.Decode(ctx);
                    samplesTested++;

                    // Log first 20 tested samples in detail
                    if (samplesTested <= 20)
                    {
                        _debugLog?.WriteLine($"  Test {samplesTested}: [{y},{x}] state=0x{state:X2}, ctx={ctx}, sig={sig}");
                        _debugLog?.WriteLine($"    MQ state: A=0x{_mq.A:X4}, C=0x{_mq.C:X8}, CT={_mq.CT}, B=0x{_mq.B:X2}");
                    }

                    if (sig == 1)
                    {
                        // Became significant - decode sign and compute coefficient
                        // Use sign-magnitude format: (sign << 31) | magnitude
                        int sign = DecodeSign(x, y);
                        int coef = (sign << 31) | setmask;

                        // Store for later - don't set SigCurrent yet to avoid contaminating context
                        newlySignificant.Add((x, y, coef));
                        samplesBecomingSignificant++;

                        if (samplesTested <= 20)
                            _debugLog?.WriteLine($"    BECAME SIGNIFICANT! sign={sign}, coef=0x{coef:X8}");
                    }

                    _state[y + 1, x + 1] |= Visited;
                }
            }
        }

        _debugLog?.WriteLine($"SigProp stats: {samplesAlreadySignificant} already sig, {samplesWithSigNeighbor} have SigNeighbor, {samplesTested} tested, {samplesBecomingSignificant} became sig");
        _debugLog?.WriteLine($"SignificancePropagationPass: {newlySignificant.Count} newly significant samples");

        // Now set coefficients, SigCurrent flags, and propagate neighbor flags
        // This must be done AFTER the entire pass to prevent cascading and context contamination
        foreach (var (x, y, coef) in newlySignificant)
        {
            _coefficients[y, x] = coef;
            _state[y + 1, x + 1] |= SigCurrent;
            PropagateNeighborFlags(x, y);
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

        int refineCount = 0;
        int ones = 0, zeros = 0;
        int ctx16Count = 0, ctx17Count = 0, ctx18Count = 0;

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

                    // Track statistics
                    refineCount++;
                    if (refineBit == 1) ones++;
                    else zeros++;
                    if (ctx == 16) ctx16Count++;
                    else if (ctx == 17) ctx17Count++;
                    else if (ctx == 18) ctx18Count++;

                    // Debug logging for first 10 refinements
                    if (refineCount <= 10)
                    {
                        int oldCoef = _coefficients[y, x];
                        _debugLog?.WriteLine($"  MagRef[{y},{x}]: oldCoef=0x{oldCoef:X8}, ctx={ctx}, refineBit={refineBit}, bp={bitPlane}");
                        _debugLog?.WriteLine($"    MQ state: A=0x{_mq.A:X4}, C=0x{_mq.C:X8}, CT={_mq.CT}, B=0x{_mq.B:X2}");
                    }

                    // Update coefficient using sign-magnitude format
                    // Clear old approximation, set decoded bit, add new midpoint
                    int coef = _coefficients[y, x];
                    coef &= resetmask;  // Clear old midpoint (preserves sign bit)
                    coef |= (refineBit << bitPlane) | setmask;  // Set refinement bit + new midpoint
                    _coefficients[y, x] = coef;

                    if (refineCount <= 10)
                    {
                        _debugLog?.WriteLine($"    After: coef=0x{coef:X8} (resetmask=0x{resetmask:X8}, setmask=0x{setmask:X8})");
                    }

                    _state[y + 1, x + 1] |= FirstRef; // Mark as refined
                }
            }
        }

        _debugLog?.WriteLine($"MagRefPass bp={bitPlane}: {refineCount} refined, {ones} ones, {zeros} zeros, ctx16={ctx16Count}, ctx17={ctx17Count}, ctx18={ctx18Count}");
    }

    private void CleanupPass(int bitPlane)
    {
        // Midpoint reconstruction: use 1.5 * 2^bp = (3 << bp) >> 1
        // Use sign-magnitude format to match Melville: (sign << 31) | magnitude
        var setmask = (int)(((long)3 << bitPlane) >> 1);

        _debugLog?.WriteLine($"CleanupPass: bitPlane={bitPlane}, setmask=0x{setmask:X8}");

        // CRITICAL: Process stripes from BOTTOM to TOP to match CoreJ2K scanning order
        // This ensures MQ decoder consumes bits in the correct sequence
        int nstripes = (_height + 3) / 4;
        for (int stripe = nstripes - 1; stripe >= 0; stripe--)
        {
            int y = stripe * 4;
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

                // Log RLC check for first few columns in first stripe
                if (_isDebugBlock && _debugLog != null && y == 0 && x < 5)
                {
                    _debugLog.WriteLine($"  RLC check at ({x:D2},{y:D2}): canUseRLC={canUseRunLength}, stripHeight={stripHeight}");
                    if (canUseRunLength)
                    {
                        for (var dy = 0; dy < stripHeight; dy++)
                        {
                            byte state = _state[y + dy + 1, x + 1];
                            _debugLog.WriteLine($"    row {y + dy}: state=0x{state:X2}");
                        }
                    }
                }

                if (canUseRunLength && stripHeight == 4)
                {
                    // Try run-length mode
                    if (_isDebugBlock && _debugLog != null)
                    {
                        _debugLog.WriteLine($"  RLC ({x:D2},{y:D2}-{y+3:D2}): Testing, MQ(A=0x{_mq.A:X4}, C=0x{_mq.C:X8}, CT={_mq.CT}, B=0x{_mq.B:X2})");
                    }

                    int runLength = _mq.Decode(EbcotDecoder.CtxRunLength);
                    if (runLength == 0)
                    {
                        // All four samples are insignificant
                        if (_isDebugBlock && _debugLog != null)
                        {
                            _debugLog.WriteLine($"  RLC ({x:D2},{y:D2}-{y+3:D2}): All insignificant (decoded 0)");
                        }
                        continue;
                    }

                    if (_isDebugBlock && _debugLog != null)
                    {
                        _debugLog.WriteLine($"  RLC ({x:D2},{y:D2}-{y+3:D2}): Has significant (decoded 1), decoding position...");
                    }

                    // Decode which sample becomes significant
                    int uniformBits = _mq.Decode(EbcotDecoder.CtxUniform) << 1;
                    uniformBits |= _mq.Decode(EbcotDecoder.CtxUniform);
                    int firstSig = uniformBits;

                    if (_isDebugBlock && _debugLog != null)
                    {
                        _debugLog.WriteLine($"  RLC ({x:D2},{y:D2}-{y+3:D2}): First significant at offset {firstSig} -> ({x:D2},{y + firstSig:D2})");
                    }

                    // Mark first significant sample with sign-magnitude format
                    int sign = DecodeSign(x, y + firstSig);
                    _coefficients[y + firstSig, x] = (sign << 31) | setmask;

                    if (_isDebugBlock && _debugLog != null)
                    {
                        _debugLog.WriteLine($"  Cleanup ({x:D2},{y + firstSig:D2}): SIGNIFICANT (via RLC), sign={sign}, coeff=0x{_coefficients[y + firstSig, x]:X8}");
                    }

                    SetSignificant(x, y + firstSig);

                    if (x == 8 && y + firstSig == 48)
                    {
                        // Log neighbor states after SetSignificant
                        _debugLog?.WriteLine($"  After SetSignificant for [48,8]:");
                        for (int ny = 47; ny <= 49; ny++)
                        {
                            for (int nx = 7; nx <= 9; nx++)
                            {
                                byte nstate = _state[ny + 1, nx + 1];
                                _debugLog?.WriteLine($"    [{ny},{nx}]: state=0x{nstate:X2} (SigCurrent={((nstate & SigCurrent) != 0)}, SigNeighbor={((nstate & SigNeighbor) != 0)})");
                            }
                        }
                    }

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
            if (_isDebugBlock && _debugLog != null)
            {
                _debugLog.WriteLine($"  Test ({x:D2},{y:D2}): SKIPPED (already sig or visited)");
            }
            return;
        }

        int ctx = GetSigContext(x, y);

        // Log MQ state BEFORE decode
        if (_isDebugBlock && _debugLog != null)
        {
            _debugLog.WriteLine($"  Test ({x:D2},{y:D2}): ctx={ctx}, MQ(A=0x{_mq.A:X4}, C=0x{_mq.C:X8}, CT={_mq.CT}, B=0x{_mq.B:X2})");
        }

        int sig = _mq.Decode(ctx);

        if (_isDebugBlock && _debugLog != null)
        {
            if (sig == 1)
            {
                _debugLog.WriteLine($"  Test ({x:D2},{y:D2}): SIGNIFICANT (decoded 1)");
            }
            else
            {
                _debugLog.WriteLine($"  Test ({x:D2},{y:D2}): insignificant (decoded 0)");
            }
        }

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

        // Log sign decoding with MQ state
        if (_isDebugBlock && _debugLog != null)
        {
            _debugLog.WriteLine($"    DecodeSign ({x:D2},{y:D2}): hc={hc}, vc={vc}, ctx={ctx}, xorBit={xorBit}, MQ(A=0x{_mq.A:X4}, C=0x{_mq.C:X8}, CT={_mq.CT}, B=0x{_mq.B:X2})");
        }

        int signBit = _mq.Decode(ctx);
        int result = signBit ^ xorBit;

        if (_isDebugBlock && _debugLog != null)
        {
            _debugLog.WriteLine($"    DecodeSign ({x:D2},{y:D2}): signBit={signBit}, result={result}");
        }

        return result;
    }

    private int GetSignContribution(int x, int y)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height)
            return 0;

        byte state = _state[y + 1, x + 1];
        if ((state & SigCurrent) == 0)
            return 0;

        // Sign contribution for JPEG2000 sign context (Table D.3)
        // Per CoreJ2K implementation: ls * (1 - 2 * lsgn) where lsgn=1 for negative
        // Bit 31 = 0 means positive (contributes +1), bit 31 = 1 means negative (contributes -1)
        return (_coefficients[y, x] & unchecked((int)0x80000000)) == 0 ? 1 : -1;
    }

    private void SetSignificant(int x, int y)
    {
        _state[y + 1, x + 1] |= SigCurrent;

        // Stripe-causal constraint: when VerticallyCausal is set, do NOT propagate
        // flags UPWARD TO stripe boundary rows (rows where y % 4 == 0)
        bool isVerticallyCausal = (_style & CodeBlockStyle.VerticallyCausal) != 0;
        bool upperIsStripeBoundary = isVerticallyCausal && y > 0 && ((y - 1) % 4) == 0;

        // Debug: log first call to show if VerticallyCausal is set
        if (_isDebugBlock && _debugLog != null && x == 0 && y == 1)
        {
            _debugLog.WriteLine($"SetSignificant({x},{y}): VerticallyCausal={isVerticallyCausal}, style=0x{(int)_style:X2}, upperIsStripeBoundary={upperIsStripeBoundary}");
        }

        if (_isDebugBlock && _debugLog != null)
        {
            var neighbors = new System.Collections.Generic.List<string>();

            // Horizontal neighbors (always set)
            if (x > 0)
                neighbors.Add($"({x-1:D2},{y:D2})");
            if (x < _width - 1)
                neighbors.Add($"({x+1:D2},{y:D2})");

            // Upper neighbors (skip if target is row 0 - codeblock boundary, or stripe boundary in causal mode)
            if (y > 1 && !upperIsStripeBoundary)
            {
                neighbors.Add($"({x:D2},{y-1:D2})");
                if (x > 0)
                    neighbors.Add($"({x-1:D2},{y-1:D2})");
                if (x < _width - 1)
                    neighbors.Add($"({x+1:D2},{y-1:D2})");
            }
            else if (y == 1)
            {
                _debugLog.WriteLine($"    → SKIPPING upper neighbors (row 0 is codeblock boundary)");
            }
            else if (y > 0 && upperIsStripeBoundary)
            {
                _debugLog.WriteLine($"    → SKIPPING upper neighbors (stripe boundary at y={y-1})");
            }

            // Lower neighbors (always set)
            if (y < _height - 1)
            {
                neighbors.Add($"({x:D2},{y+1:D2})");
                if (x > 0)
                    neighbors.Add($"({x-1:D2},{y+1:D2})");
                if (x < _width - 1)
                    neighbors.Add($"({x+1:D2},{y+1:D2})");
            }

            _debugLog.WriteLine($"    → Setting SigNeighbor: {string.Join(", ", neighbors)}");
        }

        // Horizontal neighbors (don't set if they're at row 0 - codeblock boundary)
        if (x > 0 && y > 0)
            _state[y + 1, x] |= SigNeighbor;  // Left
        if (x < _width - 1 && y > 0)
            _state[y + 1, x + 2] |= SigNeighbor;  // Right

        // Upper neighbors (don't set if target would be row 0 - codeblock top boundary)
        // Row 0 has no upper neighbors (like CoreJ2K's padding row)
        if (y > 1 && !upperIsStripeBoundary)
        {
            _state[y, x + 1] |= SigNeighbor;  // Up
            if (x > 0)
                _state[y, x] |= SigNeighbor;  // Upper-left
            if (x < _width - 1)
                _state[y, x + 2] |= SigNeighbor;  // Upper-right
        }

        // Lower neighbors (always set)
        if (y < _height - 1)
        {
            _state[y + 2, x + 1] |= SigNeighbor;  // Down
            if (x > 0)
                _state[y + 2, x] |= SigNeighbor;  // Lower-left
            if (x < _width - 1)
                _state[y + 2, x + 2] |= SigNeighbor;  // Lower-right
        }
    }


    private void PropagateNeighborFlags(int x, int y)
    {
        // Stripe-causal constraint: when VerticallyCausal is set, do NOT propagate
        // flags UPWARD TO stripe boundary rows (rows where y % 4 == 0)
        bool isVerticallyCausal = (_style & CodeBlockStyle.VerticallyCausal) != 0;
        bool upperIsStripeBoundary = isVerticallyCausal && y > 0 && ((y - 1) % 4) == 0;

        // Horizontal neighbors (don't set if they're at row 0 - codeblock boundary)
        if (x > 0 && y > 0)
            _state[y + 1, x] |= SigNeighbor;  // Left
        if (x < _width - 1 && y > 0)
            _state[y + 1, x + 2] |= SigNeighbor;  // Right

        // Upper neighbors (don't set if target would be row 0 - codeblock top boundary)
        // Row 0 has no upper neighbors (like CoreJ2K's padding row)
        if (y > 1 && !upperIsStripeBoundary)
        {
            _state[y, x + 1] |= SigNeighbor;  // Up
            if (x > 0)
                _state[y, x] |= SigNeighbor;  // Upper-left
            if (x < _width - 1)
                _state[y, x + 2] |= SigNeighbor;  // Upper-right
        }

        // Lower neighbors (always set)
        if (y < _height - 1)
        {
            _state[y + 2, x + 1] |= SigNeighbor;  // Down
            if (x > 0)
                _state[y + 2, x] |= SigNeighbor;  // Lower-left
            if (x < _width - 1)
                _state[y + 2, x + 2] |= SigNeighbor;  // Lower-right
        }
    }


    private int GetSigContext(int x, int y)
    {
        // Count significant neighbors
        int h = 0, v = 0, d = 0;

        // Stripe-causal constraint: only apply if VerticallyCausal flag is set
        // At stripe boundary (y % 4 == 0), don't use neighbors from row above
        bool isVerticallyCausal = (_style & CodeBlockStyle.VerticallyCausal) != 0;
        bool atStripeBoundary = isVerticallyCausal && (y % 4) == 0;

        // Horizontal neighbors (always included)
        if (x > 0 && (_state[y + 1, x] & SigCurrent) != 0) h++;
        if (x < _width - 1 && (_state[y + 1, x + 2] & SigCurrent) != 0) h++;

        // Vertical neighbors
        if (y > 0 && !atStripeBoundary && (_state[y, x + 1] & SigCurrent) != 0) v++;  // Up (skip at stripe boundary if causal)
        if (y < _height - 1 && (_state[y + 2, x + 1] & SigCurrent) != 0) v++;  // Down (always included)

        // Diagonal neighbors
        if (x > 0 && y > 0 && !atStripeBoundary && (_state[y, x] & SigCurrent) != 0) d++;  // Upper-left (skip at boundary if causal)
        if (x < _width - 1 && y > 0 && !atStripeBoundary && (_state[y, x + 2] & SigCurrent) != 0) d++;  // Upper-right (skip at boundary if causal)
        if (x > 0 && y < _height - 1 && (_state[y + 2, x] & SigCurrent) != 0) d++;  // Lower-left (always)
        if (x < _width - 1 && y < _height - 1 && (_state[y + 2, x + 2] & SigCurrent) != 0) d++;  // Lower-right (always)

        if (_isDebugBlock && _debugLog != null && x == 0 && y == 56)
        {
            _debugLog.WriteLine($"    [GetSigContext] _style={_style}, isVerticallyCausal={isVerticallyCausal}, atStripeBoundary={atStripeBoundary}");
            _debugLog.WriteLine($"    [GetSigContext] h={h}, v={v}, d={d}, subband={_zcSubband}");
            _debugLog.WriteLine($"    [GetSigContext] Up[{y},{x+1}]={(_state[y, x + 1] & SigCurrent) != 0}, Down[{y+2},{x+1}]={(_state[y + 2, x + 1] & SigCurrent) != 0}");
        }

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

    private void LogPassResult(int passIdx, int passInPlane, int bitPlane, string passName)
    {
        if (!_isDebugBlock || _debugLog == null)
            return;

        // Count significant coefficients
        int sigCount = 0;
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                if ((_state[y + 1, x + 1] & SigCurrent) != 0)
                    sigCount++;
            }
        }

        string passType = passInPlane switch
        {
            0 => "SigProp",
            1 => "MagRef",
            2 => "Cleanup",
            _ => "Unknown"
        };

        _debugLog.WriteLine($"Bit-plane {bitPlane}:");
        _debugLog.WriteLine($"    Pass {passIdx} ({passType}): {sigCount} significant");

        // Log first row of coefficients (up to 32 values)
        _debugLog.Write("        Coefficients: ");
        int maxCols = Math.Min(32, _width);
        for (var x = 0; x < maxCols; x++)
        {
            _debugLog.Write($"0x{_coefficients[0, x]:X8} ");
        }
        _debugLog.WriteLine();
        _debugLog.WriteLine();
    }

    private void LogSigNeighborFlags()
    {
        if (!_isDebugBlock || _debugLog == null)
            return;

        _debugLog.WriteLine("=== SAMPLES WITH SigNeighbor FLAG ===");

        // Count and list all samples with SigNeighbor flag
        var samples = new System.Collections.Generic.List<(int x, int y)>();
        for (var y = 0; y < _height; y++)
        {
            for (var x = 0; x < _width; x++)
            {
                if ((_state[y + 1, x + 1] & SigNeighbor) != 0)
                {
                    samples.Add((x, y));
                }
            }
        }

        _debugLog.WriteLine($"Total samples with SigNeighbor: {samples.Count}");

        // Show first 50 samples with their stripe location
        int maxShow = Math.Min(50, samples.Count);
        for (int i = 0; i < maxShow; i++)
        {
            var (x, y) = samples[i];
            int stripe = y / 4;
            int rowInStripe = y % 4;
            _debugLog.WriteLine($"  ({x:D2},{y:D2}) stripe={stripe} row={rowInStripe}");
        }

        if (samples.Count > 50)
        {
            _debugLog.WriteLine($"  ... and {samples.Count - 50} more");
        }

        _debugLog.WriteLine();
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