namespace ImageLibrary.Jp2.Tier1;

/// <summary>
/// MQ arithmetic decoder used in JPEG2000 Tier-1 decoding.
/// Decodes binary symbols using context-based probability estimation.
/// </summary>
internal class MqDecoder
{
    private readonly byte[] _data;
    private int _bytePos;

    // MQ decoder state
    private uint _a;  // Interval width
    private uint _c;  // Code register
    private uint _ct;  // Bit count (unsigned to match reference)
    private uint _b;  // Last byte read
    private bool _markerFound;

    // Context states (0-46)
    private readonly int[] _contextStates;
    private readonly bool[] _mps; // Most Probable Symbol for each context

    // Number of contexts for EBCOT
    public const int NumContexts = 19;

    // MQ lookup table (Qe values and state transitions)
    // From JPEG2000 spec Table D.2
    private static readonly MqState[] States =
    [
        new MqState(0x5601, 1, 1, true),   // 0
        new MqState(0x3401, 2, 6, false),  // 1
        new MqState(0x1801, 3, 9, false),  // 2
        new MqState(0x0AC1, 4, 12, false), // 3
        new MqState(0x0521, 5, 29, false), // 4
        new MqState(0x0221, 38, 33, false), // 5
        new MqState(0x5601, 7, 6, true),   // 6
        new MqState(0x5401, 8, 14, false), // 7
        new MqState(0x4801, 9, 14, false), // 8
        new MqState(0x3801, 10, 14, false), // 9
        new MqState(0x3001, 11, 17, false), // 10
        new MqState(0x2401, 12, 18, false), // 11
        new MqState(0x1C01, 13, 20, false), // 12
        new MqState(0x1601, 29, 21, false), // 13
        new MqState(0x5601, 15, 14, true),  // 14
        new MqState(0x5401, 16, 14, false), // 15
        new MqState(0x5101, 17, 15, false), // 16
        new MqState(0x4801, 18, 16, false), // 17
        new MqState(0x3801, 19, 17, false), // 18
        new MqState(0x3401, 20, 18, false), // 19
        new MqState(0x3001, 21, 19, false), // 20
        new MqState(0x2801, 22, 19, false), // 21
        new MqState(0x2401, 23, 20, false), // 22
        new MqState(0x2201, 24, 21, false), // 23
        new MqState(0x1C01, 25, 22, false), // 24
        new MqState(0x1801, 26, 23, false), // 25
        new MqState(0x1601, 27, 24, false), // 26
        new MqState(0x1401, 28, 25, false), // 27
        new MqState(0x1201, 29, 26, false), // 28
        new MqState(0x1101, 30, 27, false), // 29
        new MqState(0x0AC1, 31, 28, false), // 30
        new MqState(0x09C1, 32, 29, false), // 31
        new MqState(0x08A1, 33, 30, false), // 32
        new MqState(0x0521, 34, 31, false), // 33
        new MqState(0x0441, 35, 32, false), // 34
        new MqState(0x02A1, 36, 33, false), // 35
        new MqState(0x0221, 37, 34, false), // 36
        new MqState(0x0141, 38, 35, false), // 37
        new MqState(0x0111, 39, 36, false), // 38
        new MqState(0x0085, 40, 37, false), // 39
        new MqState(0x0049, 41, 38, false), // 40
        new MqState(0x0025, 42, 39, false), // 41
        new MqState(0x0015, 43, 40, false), // 42
        new MqState(0x0009, 44, 41, false), // 43
        new MqState(0x0005, 45, 42, false), // 44
        new MqState(0x0001, 45, 43, false), // 45
        new MqState(0x5601, 46, 46, false) // 46 (uniform context)
    ];

    public MqDecoder(byte[] data)
    {
        _data = data;
        _bytePos = 0;

        _contextStates = new int[NumContexts];
        _mps = new bool[NumContexts];

        // Initialize contexts
        ResetContexts();

        // Initialize decoder
        Initialize();
    }

    /// <summary>
    /// Resets all contexts to their initial states.
    /// Per JPEG2000 spec: 0=Uniform(46), 1=RLC(3), 2=ZC(4), rest=0
    /// </summary>
    public void ResetContexts()
    {
        for (var i = 0; i < NumContexts; i++)
        {
            _contextStates[i] = 0;
            _mps[i] = false;
        }

        // Context 0 (uniform) should use state 46
        _contextStates[0] = 46;

        // Context 1 (run-length) starts at state 3
        _contextStates[1] = 3;

        // Context 2 (first ZC context) starts at state 4
        _contextStates[2] = 4;
    }

    private void Initialize()
    {
        _markerFound = false;

        // Read first byte
        _b = ReadByte();
        // Software conventions decoder: XOR with 0xFF (per JPEG2000 spec)
        _c = (_b ^ 0xFF) << 16;

        // Byte-in
        ByteIn();

        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    private uint ReadByte()
    {
        if (_bytePos >= _data.Length)
            return 0xFF;
        return _data[_bytePos++];
    }

    private void ByteIn()
    {
        if (_markerFound)
        {
            _ct = 8;
            return;
        }

        // Check if PREVIOUS byte was 0xFF (bit-stuffing case)
        if (_b == 0xFF)
        {
            _b = ReadByte();  // Read new byte

            if (_b > 0x8F)
            {
                // Marker found
                _markerFound = true;
                _ct = 8;
            }
            else
            {
                // Bit-stuffed byte: only 7 bits are valid
                _c += 0xFE00 - (_b << 9);
                _ct = 7;
            }
        }
        else
        {
            // Normal case: read 8 bits
            _b = ReadByte();
            _c += 0xFF00 - (_b << 8);
            _ct = 8;
        }
    }

    /// <summary>
    /// Decodes a single bit using the specified context.
    /// </summary>
    /// <param name="ctx">Context index (0-18)</param>
    /// <returns>Decoded bit (0 or 1)</returns>
    public int Decode(int ctx)
    {
        int state = _contextStates[ctx];
        MqState mqState = States[state];
        uint qe = mqState.Qe;
        bool mps = _mps[ctx];

        _a -= qe;

        int d;

        if ((_c >> 16) < _a)
        {
            // MPS path
            if (_a >= 0x8000)
            {
                d = mps ? 1 : 0;
            }
            else
            {
                if (_a < qe)
                {
                    // Conditional exchange: LPS
                    d = mps ? 0 : 1;
                    SwitchLps(ctx);
                }
                else
                {
                    // MPS
                    d = mps ? 1 : 0;
                    SwitchMps(ctx);
                }
                Renormalize();
            }
        }
        else
        {
            // LPS path
            _c -= _a << 16;

            if (_a < qe)
            {
                // Conditional exchange: MPS
                d = mps ? 1 : 0;
                SwitchMps(ctx);
            }
            else
            {
                // LPS
                d = mps ? 0 : 1;
                SwitchLps(ctx);
            }

            _a = qe;
            Renormalize();
        }

        return d;
    }

    private void SwitchMps(int ctx)
    {
        int state = _contextStates[ctx];
        _contextStates[ctx] = States[state].NextMps;
    }

    private void SwitchLps(int ctx)
    {
        int state = _contextStates[ctx];
        MqState mqState = States[state];

        if (mqState.SwitchMps)
        {
            _mps[ctx] = !_mps[ctx];
        }

        _contextStates[ctx] = mqState.NextLps;
    }

    private void Renormalize()
    {
        do
        {
            if (_ct == 0)
            {
                ByteIn();
            }

            _a <<= 1;
            _c <<= 1;
            _ct--;
        } while (_a < 0x8000);
    }

    /// <summary>
    /// Decodes a raw (bypass) bit without arithmetic coding.
    /// </summary>
    public int DecodeRaw()
    {
        if (_ct == 0)
        {
            ByteIn();
        }

        _ct--;
        uint d = (_c >> (int)(16 + _ct)) & 1;
        return (int)d;
    }

    /// <summary>
    /// Gets whether a marker was encountered (end of segment).
    /// </summary>
    public bool MarkerFound => _markerFound;
}

/// <summary>
/// MQ state table entry.
/// </summary>
internal readonly struct MqState
{
    public readonly uint Qe;
    public readonly int NextMps;
    public readonly int NextLps;
    public readonly bool SwitchMps;

    public MqState(uint qe, int nextMps, int nextLps, bool switchMps)
    {
        Qe = qe;
        NextMps = nextMps;
        NextLps = nextLps;
        SwitchMps = switchMps;
    }
}