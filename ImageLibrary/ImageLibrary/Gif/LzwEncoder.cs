using System;
using System.IO;

namespace ImageLibrary.Gif;

/// <summary>
/// LZW compressor for GIF image data.
/// </summary>
internal sealed class LzwEncoder
{
    private readonly MemoryStream _output;
    private readonly byte[] _subBlock;
    private int _subBlockSize;

    // Bit writing state
    private int _bitBuffer;
    private int _bitsInBuffer;

    // LZW state
    private readonly int _minCodeSize;
    private int _codeSize;
    private int _clearCode;
    private int _endCode;
    private int _nextCode;

    // Hash table for code lookup
    private readonly int[] _hashTable;
    private readonly int[] _codeTable;

    private const int MaxCodeSize = 12;
    private const int MaxCodes = 1 << MaxCodeSize;
    private const int HashSize = 5003; // Prime number

    public LzwEncoder(int minCodeSize)
    {
        if (minCodeSize < 2 || minCodeSize > 11)
            throw new GifException($"Invalid LZW minimum code size: {minCodeSize}");

        _minCodeSize = minCodeSize;
        _output = new MemoryStream();
        _subBlock = new byte[255];

        _hashTable = new int[HashSize];
        _codeTable = new int[HashSize];

        InitializeCodeTable();
    }

    private void InitializeCodeTable()
    {
        _clearCode = 1 << _minCodeSize;
        _endCode = _clearCode + 1;
        _codeSize = _minCodeSize + 1;
        _nextCode = _endCode + 1;

        // Clear hash table
        Array.Fill(_hashTable, -1);
    }

    public byte[] Encode(byte[] indices)
    {
        // Write clear code first
        WriteCode(_clearCode);

        if (indices.Length == 0)
        {
            WriteCode(_endCode);
            FlushBits();
            FlushSubBlock();
            _output.WriteByte(0); // Block terminator
            return _output.ToArray();
        }

        int prefixCode = indices[0];

        for (var i = 1; i < indices.Length; i++)
        {
            byte suffix = indices[i];

            // Look up prefix + suffix in hash table
            int hashIndex = FindInTable(prefixCode, suffix);

            if (_hashTable[hashIndex] >= 0)
            {
                // Found in table, continue building string
                prefixCode = _codeTable[hashIndex];
            }
            else
            {
                // Not in table, output prefix code
                WriteCode(prefixCode);

                // Add new entry to table
                if (_nextCode < MaxCodes)
                {
                    _hashTable[hashIndex] = (prefixCode << 8) | suffix;
                    _codeTable[hashIndex] = _nextCode++;

                    // Increase code size if needed
                    if (_nextCode > (1 << _codeSize) && _codeSize < MaxCodeSize)
                    {
                        _codeSize++;
                    }
                }
                else
                {
                    // Table full, output clear code and reset
                    WriteCode(_clearCode);
                    InitializeCodeTable();
                }

                prefixCode = suffix;
            }
        }

        // Output final code
        WriteCode(prefixCode);
        WriteCode(_endCode);
        FlushBits();
        FlushSubBlock();
        _output.WriteByte(0); // Block terminator

        return _output.ToArray();
    }

    private int FindInTable(int prefix, byte suffix)
    {
        int key = (prefix << 8) | suffix;
        int hashIndex = ((prefix << 8) ^ suffix) % HashSize;
        if (hashIndex < 0) hashIndex += HashSize;

        while (_hashTable[hashIndex] >= 0)
        {
            if (_hashTable[hashIndex] == key)
                return hashIndex;

            hashIndex++;
            if (hashIndex >= HashSize)
                hashIndex = 0;
        }

        return hashIndex;
    }

    private void WriteCode(int code)
    {
        _bitBuffer |= code << _bitsInBuffer;
        _bitsInBuffer += _codeSize;

        while (_bitsInBuffer >= 8)
        {
            WriteByte((byte)(_bitBuffer & 0xFF));
            _bitBuffer >>= 8;
            _bitsInBuffer -= 8;
        }
    }

    private void FlushBits()
    {
        if (_bitsInBuffer > 0)
        {
            WriteByte((byte)(_bitBuffer & 0xFF));
            _bitBuffer = 0;
            _bitsInBuffer = 0;
        }
    }

    private void WriteByte(byte b)
    {
        _subBlock[_subBlockSize++] = b;
        if (_subBlockSize == 255)
        {
            FlushSubBlock();
        }
    }

    private void FlushSubBlock()
    {
        if (_subBlockSize > 0)
        {
            _output.WriteByte((byte)_subBlockSize);
            _output.Write(_subBlock, 0, _subBlockSize);
            _subBlockSize = 0;
        }
    }
}
