using System;
using System.IO;

namespace Compressors.Jpeg2k;

/// <summary>
/// MQ arithmetic coder for JPEG2000 EBCOT tier-1 coding.
/// Implements the context-based adaptive binary arithmetic coder
/// specified in ITU-T T.800 Annex C.
/// </summary>
public class MQEncoder : IDisposable
{
    private readonly Stream _stream;
    private uint _a;          // Interval register (16 bits used)
    private uint _c;          // Code register (28 bits used)
    private int _ct;          // Counter for output
    private int _b;           // Temporary byte buffer
    private bool _disposed;

    // Context states (one per context)
    private readonly byte[] _states;
    private readonly byte[] _mps;  // Most probable symbol for each context

    public MQEncoder(Stream stream, int numContexts = Jp2kConstants.Contexts.TotalContexts)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _states = new byte[numContexts];
        _mps = new byte[numContexts];

        // Initialize all contexts to state 0 with MPS = 0
        Array.Fill(_states, (byte)0);
        Array.Fill(_mps, (byte)0);

        // Initialize encoder state
        _a = 0x8000;
        _c = 0;
        _ct = 12;
        _b = 0;
    }

    /// <summary>
    /// Resets a context to its initial state.
    /// </summary>
    public void ResetContext(int context, int initialState = 0, int initialMps = 0)
    {
        _states[context] = (byte)initialState;
        _mps[context] = (byte)initialMps;
    }

    /// <summary>
    /// Resets all contexts to initial state.
    /// </summary>
    public void ResetAllContexts()
    {
        Array.Fill(_states, (byte)0);
        Array.Fill(_mps, (byte)0);
    }

    /// <summary>
    /// Encodes a single bit with the specified context.
    /// </summary>
    /// <param name="context">Context index</param>
    /// <param name="bit">Bit to encode (0 or 1)</param>
    public void Encode(int context, int bit)
    {
        int state = _states[context];
        int mps = _mps[context];
        var (qe, nmps, nlps, switchFlag) = Jp2kConstants.MQTable[state];

        _a -= qe;

        if (bit == mps)
        {
            // MPS path
            if (_a < 0x8000)
            {
                if (_a < qe)
                {
                    // Conditional exchange
                    _c += _a;
                    _a = qe;
                }
                _states[context] = nmps;
                Renormalize();
            }
        }
        else
        {
            // LPS path
            if (_a >= qe)
            {
                // Conditional exchange
                _c += _a;
                _a = qe;
            }

            if (switchFlag != 0)
            {
                _mps[context] = (byte)(1 - mps);
            }
            _states[context] = nlps;
            Renormalize();
        }
    }

    /// <summary>
    /// Renormalizes the encoder state.
    /// </summary>
    private void Renormalize()
    {
        while (_a < 0x8000)
        {
            _a <<= 1;
            _c <<= 1;
            _ct--;

            if (_ct == 0)
            {
                ByteOut();
            }
        }
    }

    /// <summary>
    /// Outputs a byte to the stream.
    /// </summary>
    private void ByteOut()
    {
        int temp = (int)(_c >> 19);

        if (temp > 0xFF)
        {
            _b++;
            if (_b == 0xFF)
            {
                _stream.WriteByte(0xFF);
                _b = 0;
            }
            OutputByte();
            temp &= 0xFF;
        }
        else if (temp == 0xFF)
        {
            // Bit stuffing needed
            OutputByte();
            _b = temp;
            _ct = 7;
            _c &= 0x7FFFF;
            return;
        }

        OutputByte();
        _b = temp;
        _ct = 8;
        _c &= 0x7FFFF;
    }

    private void OutputByte()
    {
        if (_b >= 0)
        {
            _stream.WriteByte((byte)_b);
        }
    }

    /// <summary>
    /// Flushes the encoder and writes final bytes.
    /// </summary>
    public void Flush()
    {
        // Set remaining bits to 1 for efficient flushing
        uint temp = _c + _a - 1;
        temp &= 0xFFFF0000;

        if (temp < _c)
        {
            temp += 0x8000;
        }

        _c = temp;

        // Output remaining bytes
        _c <<= _ct;
        if ((_c & 0xF8000000) != 0)
        {
            _b++;
            if (_b == 0xFF)
            {
                _stream.WriteByte(0xFF);
                _stream.WriteByte(0x00);
                _b = 0;
            }
        }
        OutputByte();

        _c <<= 8;
        if ((_c & 0xF8000000) != 0)
        {
            _b++;
            if (_b == 0xFF)
            {
                _stream.WriteByte(0xFF);
                _stream.WriteByte(0x00);
                _b = 0;
            }
        }

        if (_b != 0xFF)
        {
            _stream.WriteByte((byte)_b);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Flush();
            _disposed = true;
        }
    }
}

/// <summary>
/// MQ arithmetic decoder for JPEG2000.
/// </summary>
public class MQDecoder
{
    private readonly Stream _stream;
    private uint _a;          // Interval register
    private uint _c;          // Code register
    private int _ct;          // Counter
    private int _b;           // Current byte

    // Context states
    private readonly byte[] _states;
    private readonly byte[] _mps;

    public MQDecoder(Stream stream, int numContexts = Jp2kConstants.Contexts.TotalContexts)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _states = new byte[numContexts];
        _mps = new byte[numContexts];

        Array.Fill(_states, (byte)0);
        Array.Fill(_mps, (byte)0);

        // Initialize decoder
        _b = 0;
        _c = 0;
        _a = 0x8000;
        _ct = 0;

        // Read initial bytes
        ByteIn();
        _c <<= 8;
        ByteIn();
        _c <<= 8;
        _ct = 0;
    }

    /// <summary>
    /// Resets a context to its initial state.
    /// </summary>
    public void ResetContext(int context, int initialState = 0, int initialMps = 0)
    {
        _states[context] = (byte)initialState;
        _mps[context] = (byte)initialMps;
    }

    /// <summary>
    /// Resets all contexts to initial state.
    /// </summary>
    public void ResetAllContexts()
    {
        Array.Fill(_states, (byte)0);
        Array.Fill(_mps, (byte)0);
    }

    /// <summary>
    /// Decodes a single bit with the specified context.
    /// </summary>
    /// <param name="context">Context index</param>
    /// <returns>Decoded bit (0 or 1)</returns>
    public int Decode(int context)
    {
        int state = _states[context];
        int mps = _mps[context];
        var (qe, nmps, nlps, switchFlag) = Jp2kConstants.MQTable[state];

        _a -= qe;

        int bit;
        if ((_c >> 16) < _a)
        {
            // MPS path
            if (_a < 0x8000)
            {
                bit = ExchangeMps(context, qe, nmps, nlps, switchFlag, mps);
                Renormalize();
            }
            else
            {
                bit = mps;
            }
        }
        else
        {
            // LPS path
            bit = ExchangeLps(context, qe, nmps, nlps, switchFlag, mps);
            Renormalize();
        }

        return bit;
    }

    private int ExchangeMps(int context, uint qe, byte nmps, byte nlps, byte switchFlag, int mps)
    {
        if (_a < qe)
        {
            // Conditional exchange
            _states[context] = nlps;
            if (switchFlag != 0)
            {
                _mps[context] = (byte)(1 - mps);
                return 1 - mps;
            }
            return 1 - mps;
        }
        else
        {
            _states[context] = nmps;
            return mps;
        }
    }

    private int ExchangeLps(int context, uint qe, byte nmps, byte nlps, byte switchFlag, int mps)
    {
        _c -= (_a << 16);

        if (_a < qe)
        {
            _a = qe;
            _states[context] = nmps;
            return mps;
        }
        else
        {
            _a = qe;
            _states[context] = nlps;
            if (switchFlag != 0)
            {
                _mps[context] = (byte)(1 - mps);
            }
            return 1 - mps;
        }
    }

    /// <summary>
    /// Renormalizes the decoder state.
    /// </summary>
    private void Renormalize()
    {
        while (_a < 0x8000)
        {
            if (_ct == 0)
            {
                ByteIn();
            }

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
    }

    /// <summary>
    /// Reads a byte from the stream.
    /// </summary>
    private void ByteIn()
    {
        int prevB = _b;
        _b = _stream.ReadByte();

        if (prevB == 0xFF)
        {
            if (_b > 0x8F)
            {
                // Marker found - don't advance
                _c += 0xFF00;
                _ct = 8;
            }
            else
            {
                // Bit stuffing - only 7 bits
                _c += (uint)(_b << 9);
                _ct = 7;
            }
        }
        else
        {
            _c += (uint)(_b << 8);
            _ct = 8;
        }
    }
}
