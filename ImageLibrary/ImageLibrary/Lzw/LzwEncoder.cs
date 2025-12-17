using System;
using System.Collections.Generic;
using System.IO;

namespace ImageLibrary.Lzw;

/// <summary>
/// LZW encoder that compresses data using the Lempel-Ziv-Welch algorithm.
/// Supports PDF-compatible output with configurable EarlyChange behavior.
/// </summary>
public sealed class LzwEncoder : IDisposable
{
    private readonly BitWriter _writer;
    private readonly LzwOptions _options;
    private readonly Dictionary<string, int> _dictionary;
    private int _nextCode;
    private int _codeSize;
    private bool _disposed;

    /// <summary>
    /// Creates a new LZW encoder that writes to the specified stream.
    /// </summary>
    /// <param name="output">The stream to write compressed data to.</param>
    /// <param name="options">Encoding options. If null, uses PDF defaults.</param>
    /// <param name="leaveOpen">Whether to leave the output stream open when disposed.</param>
    public LzwEncoder(Stream output, LzwOptions? options = null, bool leaveOpen = false)
    {
        _writer = new BitWriter(output, leaveOpen);
        _options = options ?? LzwOptions.PdfDefault;
        _dictionary = new Dictionary<string, int>();
        InitializeDictionary();
    }

    private void InitializeDictionary()
    {
        _dictionary.Clear();

        // Initialize with single-byte entries (0-255)
        for (var i = 0; i < LzwConstants.SingleByteEntries; i++)
        {
            _dictionary[((char)i).ToString()] = i;
        }

        // ClearCode (256) and EndOfDataCode (257) are reserved
        _nextCode = LzwConstants.FirstDictionaryCode;
        _codeSize = LzwConstants.InitialCodeSize;
    }

    /// <summary>
    /// Compresses the input data and writes to the output stream.
    /// </summary>
    /// <param name="input">The data to compress.</param>
    public void Encode(byte[] input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        Encode(input, 0, input.Length);
    }

    /// <summary>
    /// Compresses the input data and writes to the output stream.
    /// </summary>
    /// <param name="input">The data to compress.</param>
    /// <param name="offset">The offset in the input array to start from.</param>
    /// <param name="count">The number of bytes to compress.</param>
    public void Encode(byte[] input, int offset, int count)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (offset < 0 || offset > input.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0 || offset + count > input.Length)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count == 0)
        {
            // Write clear code and EOD for empty input
            if (_options.EmitInitialClearCode)
            {
                _writer.WriteCode(LzwConstants.ClearCode, _codeSize);
            }
            _writer.WriteCode(LzwConstants.EndOfDataCode, _codeSize);
            return;
        }

        // Emit initial clear code if required
        if (_options.EmitInitialClearCode)
        {
            _writer.WriteCode(LzwConstants.ClearCode, _codeSize);
        }

        // Start with first byte
        var current = ((char)input[offset]).ToString();

        for (int i = offset + 1; i < offset + count; i++)
        {
            var next = (char)input[i];
            string combined = current + next;

            if (_dictionary.ContainsKey(combined))
            {
                // Combination exists, continue building
                current = combined;
            }
            else
            {
                // Output code for current string
                OutputCode(_dictionary[current]);

                // Add new combination to dictionary if not full
                if (_nextCode < LzwConstants.MaxDictionarySize)
                {
                    _dictionary[combined] = _nextCode++;
                    UpdateCodeSize();
                }
                else
                {
                    // Dictionary full - emit clear code and reset
                    _writer.WriteCode(LzwConstants.ClearCode, _codeSize);
                    InitializeDictionary();
                }

                // Start new sequence with current character
                current = next.ToString();
            }
        }

        // Output final code
        OutputCode(_dictionary[current]);

        // Output end of data
        _writer.WriteCode(LzwConstants.EndOfDataCode, _codeSize);
    }

    /// <summary>
    /// Compresses the input stream and writes to the output stream.
    /// </summary>
    /// <param name="input">The stream to compress.</param>
    /// <param name="bufferSize">Buffer size for reading input.</param>
    public void Encode(Stream input, int bufferSize = 8192)
    {
        using (var memoryStream = new MemoryStream())
        {
            input.CopyTo(memoryStream, bufferSize);
            Encode(memoryStream.ToArray());
        }
    }

    private void OutputCode(int code)
    {
        _writer.WriteCode(code, _codeSize);
    }

    private void UpdateCodeSize()
    {
        if (_options.EarlyChange)
        {
            // Early change: increase code size when _nextCode reaches 2^codeSize - 1
            // This happens BEFORE the next code is written
            int threshold = (1 << _codeSize) - 1;
            if (_nextCode > threshold && _codeSize < LzwConstants.MaxCodeSize)
            {
                _codeSize++;
            }
        }
        else
        {
            // Late change: increase code size when _nextCode reaches 2^codeSize
            int threshold = 1 << _codeSize;
            if (_nextCode >= threshold && _codeSize < LzwConstants.MaxCodeSize)
            {
                _codeSize++;
            }
        }
    }

    /// <summary>
    /// Flushes any buffered data to the output stream.
    /// </summary>
    public void Flush()
    {
        _writer.Flush();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }
}