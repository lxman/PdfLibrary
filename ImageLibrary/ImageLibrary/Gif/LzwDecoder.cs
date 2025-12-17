using System;

namespace ImageLibrary.Gif;

/// <summary>
/// LZW decompressor for GIF image data.
/// </summary>
internal sealed class LzwDecoder
{
    private readonly byte[] _data;
    private int _dataOffset;
    private readonly int _dataEnd;

    // Sub-block reading state
    private byte[] _blockBuffer = new byte[255];
    private int _blockSize;
    private int _blockOffset;

    // Bit reading state
    private int _bitBuffer;
    private int _bitsInBuffer;

    // LZW state
    private readonly int _minCodeSize;
    private int _codeSize;
    private int _clearCode;
    private int _endCode;
    private int _nextCode;
    private int _codeMask;

    // Code table
    private readonly int[] _prefix;
    private readonly byte[] _suffix;
    private readonly int[] _length;

    // Output buffer for decoding strings
    private readonly byte[] _stringBuffer;
    private int _stringLength;

    private const int MaxCodeSize = 12;
    private const int MaxCodes = 1 << MaxCodeSize;
    private const int MaxIterations = 100_000_000;

    public LzwDecoder(byte[] data, int offset, int length, int minCodeSize)
    {
        if (minCodeSize < 2 || minCodeSize > 11)
            throw new GifException($"Invalid LZW minimum code size: {minCodeSize}");

        _data = data;
        _dataOffset = offset;
        _dataEnd = offset + length;
        _minCodeSize = minCodeSize;

        _prefix = new int[MaxCodes];
        _suffix = new byte[MaxCodes];
        _length = new int[MaxCodes];
        _stringBuffer = new byte[MaxCodes];

        InitializeCodeTable();
    }

    private void InitializeCodeTable()
    {
        _clearCode = 1 << _minCodeSize;
        _endCode = _clearCode + 1;
        _codeSize = _minCodeSize + 1;
        _codeMask = (1 << _codeSize) - 1;
        _nextCode = _endCode + 1;

        // Initialize table with single-byte entries
        for (var i = 0; i < _clearCode; i++)
        {
            _prefix[i] = -1;
            _suffix[i] = (byte)i;
            _length[i] = 1;
        }
    }

    public byte[] Decode(int expectedSize)
    {
        var output = new byte[expectedSize];
        var outputOffset = 0;
        var iterations = 0;

        int prevCode = -1;
        byte firstChar = 0;

        while (outputOffset < expectedSize)
        {
            if (++iterations > MaxIterations)
                throw new GifException("LZW decode exceeded maximum iterations");

            int code = ReadCode();
            if (code < 0)
                throw new GifException("Unexpected end of LZW data");

            if (code == _endCode)
                break;

            if (code == _clearCode)
            {
                InitializeCodeTable();
                prevCode = -1;
                continue;
            }

            if (code < _nextCode)
            {
                // Code is in table
                DecodeString(code);
                firstChar = _stringBuffer[0];
            }
            else if (code == _nextCode && prevCode >= 0)
            {
                // Special case: code not in table yet
                DecodeString(prevCode);
                _stringBuffer[_stringLength++] = firstChar;
            }
            else
            {
                throw new GifException($"Invalid LZW code: {code} (next: {_nextCode})");
            }

            // Output the decoded string
            if (outputOffset + _stringLength > expectedSize)
            {
                // Only copy what fits
                int copyLen = expectedSize - outputOffset;
                Array.Copy(_stringBuffer, 0, output, outputOffset, copyLen);
                outputOffset += copyLen;
                break;
            }

            Array.Copy(_stringBuffer, 0, output, outputOffset, _stringLength);
            outputOffset += _stringLength;

            // Add new entry to table
            if (prevCode >= 0 && _nextCode < MaxCodes)
            {
                _prefix[_nextCode] = prevCode;
                _suffix[_nextCode] = firstChar;
                _length[_nextCode] = _length[prevCode] + 1;
                _nextCode++;

                // Increase code size if needed
                if (_nextCode > _codeMask && _codeSize < MaxCodeSize)
                {
                    _codeSize++;
                    _codeMask = (1 << _codeSize) - 1;
                }
            }

            prevCode = code;
            firstChar = _stringBuffer[0];
        }

        return output;
    }

    private void DecodeString(int code)
    {
        _stringLength = _length[code];
        int pos = _stringLength - 1;

        while (code >= 0 && pos >= 0)
        {
            _stringBuffer[pos--] = _suffix[code];
            code = _prefix[code];
        }
    }

    private int ReadCode()
    {
        // Fill bit buffer as needed
        while (_bitsInBuffer < _codeSize)
        {
            int nextByte = ReadByte();
            if (nextByte < 0)
                return -1;

            _bitBuffer |= nextByte << _bitsInBuffer;
            _bitsInBuffer += 8;
        }

        int code = _bitBuffer & _codeMask;
        _bitBuffer >>= _codeSize;
        _bitsInBuffer -= _codeSize;

        return code;
    }

    private int ReadByte()
    {
        // Read from current sub-block
        if (_blockOffset < _blockSize)
        {
            return _blockBuffer[_blockOffset++];
        }

        // Need to read next sub-block
        if (_dataOffset >= _dataEnd)
            return -1;

        _blockSize = _data[_dataOffset++];
        if (_blockSize == 0)
            return -1; // Block terminator

        if (_dataOffset + _blockSize > _dataEnd)
            throw new GifException("GIF sub-block extends past data end");

        Array.Copy(_data, _dataOffset, _blockBuffer, 0, _blockSize);
        _dataOffset += _blockSize;
        _blockOffset = 0;

        return _blockBuffer[_blockOffset++];
    }
}
