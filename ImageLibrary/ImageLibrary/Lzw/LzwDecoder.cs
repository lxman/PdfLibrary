using System;
using System.Collections.Generic;
using System.IO;

namespace ImageLibrary.Lzw;

/// <summary>
/// LZW decoder that decompresses data using the Lempel-Ziv-Welch algorithm.
/// Supports PDF-compatible input with configurable EarlyChange behavior.
/// </summary>
public sealed class LzwDecoder : IDisposable
{
    private readonly BitReader _reader;
    private readonly LzwOptions _options;
    private readonly List<byte[]?> _dictionary;
    private int _nextCode;
    private int _codeSize;
    private bool _disposed;

    /// <summary>
    /// Creates a new LZW decoder that reads from the specified stream.
    /// </summary>
    /// <param name="input">The stream to read compressed data from.</param>
    /// <param name="options">Decoding options. If null, uses PDF defaults.</param>
    /// <param name="leaveOpen">Whether to leave the input stream open when disposed.</param>
    public LzwDecoder(Stream input, LzwOptions? options = null, bool leaveOpen = false)
    {
        _options = options ?? LzwOptions.PdfDefault;
        _reader = new BitReader(input, _options.BitOrder, leaveOpen);
        _dictionary = new List<byte[]?>(LzwConstants.MaxDictionarySize);
        InitializeDictionary();
    }

    private void InitializeDictionary()
    {
        _dictionary.Clear();

        // Initialize with single-byte entries (0-255)
        for (var i = 0; i < LzwConstants.SingleByteEntries; i++)
        {
            _dictionary.Add([(byte)i]);
        }

        // Add placeholders for ClearCode (256) and EndOfDataCode (257)
        _dictionary.Add(null); // ClearCode - not used as output
        _dictionary.Add(null); // EndOfDataCode - not used as output

        _nextCode = LzwConstants.FirstDictionaryCode;
        _codeSize = LzwConstants.InitialCodeSize;
    }

    /// <summary>
    /// Decompresses the input stream and returns the decompressed data.
    /// </summary>
    /// <returns>The decompressed data.</returns>
    public byte[] Decode()
    {
        using (var output = new MemoryStream())
        {
            Decode(output);
            return output.ToArray();
        }
    }

    /// <summary>
    /// Decompresses the input stream and writes to the output stream.
    /// </summary>
    /// <param name="output">The stream to write decompressed data to.</param>
    public void Decode(Stream output)
    {
        if (output == null)
            throw new ArgumentNullException(nameof(output));

        byte[]? previousString = null;

        while (true)
        {
            int code = ReadCode();

            if (code == -1)
            {
                // Unexpected end of stream
                break;
            }

            if (code == LzwConstants.EndOfDataCode)
            {
                // End of data
                break;
            }

            if (code == LzwConstants.ClearCode)
            {
                // Reset dictionary
                InitializeDictionary();
                previousString = null;
                continue;
            }

            byte[] currentString;

            if (code < _dictionary.Count && _dictionary[code] != null)
            {
                // Code exists in dictionary
                currentString = _dictionary[code]!;
            }
            else if (code == _nextCode && previousString != null)
            {
                // Special case: code not yet in table
                // This happens when encoding cScSc pattern
                // The string is previousString + first byte of previousString
                currentString = new byte[previousString.Length + 1];
                Array.Copy(previousString, currentString, previousString.Length);
                currentString[previousString.Length] = previousString[0];
            }
            else
            {
                throw new InvalidDataException($"Invalid LZW code: {code}. Expected code <= {_nextCode}");
            }

            // Output the string
            output.Write(currentString, 0, currentString.Length);

            // Add new entry to dictionary (previousString + first byte of currentString)
            if (previousString != null && _nextCode < LzwConstants.MaxDictionarySize)
            {
                var newEntry = new byte[previousString.Length + 1];
                Array.Copy(previousString, newEntry, previousString.Length);
                newEntry[previousString.Length] = currentString[0];

                if (_nextCode < _dictionary.Count)
                {
                    _dictionary[_nextCode] = newEntry;
                }
                else
                {
                    _dictionary.Add(newEntry);
                }
                _nextCode++;
                UpdateCodeSize();
            }

            previousString = currentString;
        }
    }

    private int ReadCode()
    {
        // For EarlyChange mode, check if we need to increase code size BEFORE reading
        // The decoder is one entry behind the encoder, so we use >= instead of >
        if (_options.EarlyChange)
        {
            int threshold = (1 << _codeSize) - 1;
            if (_nextCode >= threshold && _codeSize < LzwConstants.MaxCodeSize)
            {
                _codeSize++;
            }
        }

        return _reader.ReadCode(_codeSize);
    }

    private void UpdateCodeSize()
    {
        if (_options.EarlyChange)
        {
            // Already handled in ReadCode for early change
            return;
        }

        // Late change: increase code size when _nextCode reaches 2^codeSize
        int threshold = 1 << _codeSize;
        if (_nextCode >= threshold && _codeSize < LzwConstants.MaxCodeSize)
        {
            _codeSize++;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _reader.Dispose();
            _disposed = true;
        }
    }
}