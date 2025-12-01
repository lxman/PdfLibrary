namespace Compressors.Jpeg2k;

/// <summary>
/// JPEG2000 constants including markers and codestream syntax.
/// Based on ITU-T T.800 (ISO/IEC 15444-1).
/// </summary>
public static class Jp2kConstants
{
    #region Codestream Markers (Table A.1)

    // Delimiting markers
    public const ushort SOC = 0xFF4F;  // Start of codestream
    public const ushort SOT = 0xFF90;  // Start of tile-part
    public const ushort SOD = 0xFF93;  // Start of data
    public const ushort EOC = 0xFFD9;  // End of codestream

    // Fixed information markers
    public const ushort SIZ = 0xFF51;  // Image and tile size

    // Functional markers
    public const ushort COD = 0xFF52;  // Coding style default
    public const ushort COC = 0xFF53;  // Coding style component
    public const ushort RGN = 0xFF5E;  // Region of interest
    public const ushort QCD = 0xFF5C;  // Quantization default
    public const ushort QCC = 0xFF5D;  // Quantization component
    public const ushort POC = 0xFF5F;  // Progression order change

    // Pointer markers
    public const ushort TLM = 0xFF55;  // Tile-part lengths
    public const ushort PLM = 0xFF57;  // Packet length, main header
    public const ushort PLT = 0xFF58;  // Packet length, tile-part header
    public const ushort PPM = 0xFF60;  // Packed packet headers, main header
    public const ushort PPT = 0xFF61;  // Packed packet headers, tile-part header

    // In bit-stream markers
    public const ushort SOP = 0xFF91;  // Start of packet
    public const ushort EPH = 0xFF92;  // End of packet header

    // Informational markers
    public const ushort CRG = 0xFF63;  // Component registration
    public const ushort COM = 0xFF64;  // Comment

    #endregion

    #region Wavelet Transform Constants

    /// <summary>
    /// CDF 9/7 wavelet lifting coefficients (lossy compression).
    /// </summary>
    public static class Cdf97
    {
        // Lifting step coefficients
        public const float Alpha = -1.586134342f;   // Step 1
        public const float Beta = -0.052980118f;    // Step 2
        public const float Gamma = 0.882911075f;    // Step 3
        public const float Delta = 0.443506852f;    // Step 4

        // Scaling coefficients
        public const float K = 1.230174105f;        // Low-pass scale
        public const float InvK = 0.812893066f;     // 1/K, High-pass scale (actually 1/(2K))
    }

    /// <summary>
    /// CDF 5/3 wavelet lifting coefficients (lossless compression).
    /// </summary>
    public static class Cdf53
    {
        public const float Alpha = -0.5f;           // Predict step
        public const float Beta = 0.25f;            // Update step
    }

    #endregion

    #region EBCOT Constants

    /// <summary>
    /// Code-block dimensions (must be powers of 2, 4 to 1024).
    /// </summary>
    public const int DefaultCodeBlockWidth = 64;
    public const int DefaultCodeBlockHeight = 64;
    public const int MaxCodeBlockSize = 4096;  // 64x64

    /// <summary>
    /// Number of coding passes per bit-plane.
    /// </summary>
    public const int PassesPerBitPlane = 3;  // Significance, Refinement, Cleanup

    /// <summary>
    /// Context labels for EBCOT (Table D.1).
    /// </summary>
    public static class Contexts
    {
        // Significance propagation contexts (0-8)
        public const int SigPropLL_LH = 0;  // LL and LH subbands
        public const int SigPropHL = 1;     // HL subband
        public const int SigPropHH = 2;     // HH subband

        // Sign coding contexts (9-13)
        public const int SignFirst = 9;
        public const int SignCount = 5;

        // Magnitude refinement contexts (14-16)
        public const int MagRefFirst = 14;
        public const int MagRefCount = 3;

        // Cleanup contexts (17-18)
        public const int CleanupFirst = 17;
        public const int RunLength = 17;
        public const int Uniform = 18;

        // Total contexts
        public const int TotalContexts = 19;
    }

    #endregion

    #region MQ Coder Constants

    /// <summary>
    /// MQ coder probability estimation table (Table C.2).
    /// Each entry: (Qe value, NMPS index, NLPS index, switch flag)
    /// </summary>
    public static readonly (ushort Qe, byte Nmps, byte Nlps, byte Switch)[] MQTable = new[]
    {
        ((ushort)0x5601, (byte)1,  (byte)1,  (byte)1),   // 0
        ((ushort)0x3401, (byte)2,  (byte)6,  (byte)0),   // 1
        ((ushort)0x1801, (byte)3,  (byte)9,  (byte)0),   // 2
        ((ushort)0x0AC1, (byte)4,  (byte)12, (byte)0),   // 3
        ((ushort)0x0521, (byte)5,  (byte)29, (byte)0),   // 4
        ((ushort)0x0221, (byte)38, (byte)33, (byte)0),   // 5
        ((ushort)0x5601, (byte)7,  (byte)6,  (byte)1),   // 6
        ((ushort)0x5401, (byte)8,  (byte)14, (byte)0),   // 7
        ((ushort)0x4801, (byte)9,  (byte)14, (byte)0),   // 8
        ((ushort)0x3801, (byte)10, (byte)14, (byte)0),   // 9
        ((ushort)0x3001, (byte)11, (byte)17, (byte)0),   // 10
        ((ushort)0x2401, (byte)12, (byte)18, (byte)0),   // 11
        ((ushort)0x1C01, (byte)13, (byte)20, (byte)0),   // 12
        ((ushort)0x1601, (byte)29, (byte)21, (byte)0),   // 13
        ((ushort)0x5601, (byte)15, (byte)14, (byte)1),   // 14
        ((ushort)0x5401, (byte)16, (byte)14, (byte)0),   // 15
        ((ushort)0x5101, (byte)17, (byte)15, (byte)0),   // 16
        ((ushort)0x4801, (byte)18, (byte)16, (byte)0),   // 17
        ((ushort)0x3801, (byte)19, (byte)17, (byte)0),   // 18
        ((ushort)0x3401, (byte)20, (byte)18, (byte)0),   // 19
        ((ushort)0x3001, (byte)21, (byte)19, (byte)0),   // 20
        ((ushort)0x2801, (byte)22, (byte)19, (byte)0),   // 21
        ((ushort)0x2401, (byte)23, (byte)20, (byte)0),   // 22
        ((ushort)0x2201, (byte)24, (byte)21, (byte)0),   // 23
        ((ushort)0x1C01, (byte)25, (byte)22, (byte)0),   // 24
        ((ushort)0x1801, (byte)26, (byte)23, (byte)0),   // 25
        ((ushort)0x1601, (byte)27, (byte)24, (byte)0),   // 26
        ((ushort)0x1401, (byte)28, (byte)25, (byte)0),   // 27
        ((ushort)0x1201, (byte)29, (byte)26, (byte)0),   // 28
        ((ushort)0x1101, (byte)30, (byte)27, (byte)0),   // 29
        ((ushort)0x0AC1, (byte)31, (byte)28, (byte)0),   // 30
        ((ushort)0x09C1, (byte)32, (byte)29, (byte)0),   // 31
        ((ushort)0x08A1, (byte)33, (byte)30, (byte)0),   // 32
        ((ushort)0x0521, (byte)34, (byte)31, (byte)0),   // 33
        ((ushort)0x0441, (byte)35, (byte)32, (byte)0),   // 34
        ((ushort)0x02A1, (byte)36, (byte)33, (byte)0),   // 35
        ((ushort)0x0221, (byte)37, (byte)34, (byte)0),   // 36
        ((ushort)0x0141, (byte)38, (byte)35, (byte)0),   // 37
        ((ushort)0x0111, (byte)39, (byte)36, (byte)0),   // 38
        ((ushort)0x0085, (byte)40, (byte)37, (byte)0),   // 39
        ((ushort)0x0049, (byte)41, (byte)38, (byte)0),   // 40
        ((ushort)0x0025, (byte)42, (byte)39, (byte)0),   // 41
        ((ushort)0x0015, (byte)43, (byte)40, (byte)0),   // 42
        ((ushort)0x0009, (byte)44, (byte)41, (byte)0),   // 43
        ((ushort)0x0005, (byte)45, (byte)42, (byte)0),   // 44
        ((ushort)0x0001, (byte)45, (byte)43, (byte)0),   // 45
        ((ushort)0x5601, (byte)46, (byte)46, (byte)0),   // 46 (uniform context)
    };

    /// <summary>
    /// Initial MQ coder state index for each context.
    /// </summary>
    public const int MQInitialState = 0;

    /// <summary>
    /// Uniform context state (for run-length coding).
    /// </summary>
    public const int MQUniformState = 46;

    #endregion

    #region Quantization Constants

    /// <summary>
    /// Default number of guard bits.
    /// </summary>
    public const int DefaultGuardBits = 2;

    /// <summary>
    /// Quantization step size mantissa bits.
    /// </summary>
    public const int QuantMantissaBits = 11;

    #endregion

    #region Progression Orders

    public const byte ProgressionLRCP = 0;  // Layer-Resolution-Component-Position
    public const byte ProgressionRLCP = 1;  // Resolution-Layer-Component-Position
    public const byte ProgressionRPCL = 2;  // Resolution-Position-Component-Layer
    public const byte ProgressionPCRL = 3;  // Position-Component-Resolution-Layer
    public const byte ProgressionCPRL = 4;  // Component-Position-Resolution-Layer

    #endregion

    #region Subband Types

    public const int SubbandLL = 0;  // Low-Low (approximation)
    public const int SubbandHL = 1;  // High-Low (horizontal detail)
    public const int SubbandLH = 2;  // Low-High (vertical detail)
    public const int SubbandHH = 3;  // High-High (diagonal detail)

    #endregion
}
