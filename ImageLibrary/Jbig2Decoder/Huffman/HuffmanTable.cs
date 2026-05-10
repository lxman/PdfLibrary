using System;

namespace Jbig2Decoder.Huffman
{
    /// <summary>One line of a Huffman table per T.88 §B.1.</summary>
    internal readonly struct HuffmanLine
    {
        public readonly int PrefLen;
        public readonly int RangeLen;
        public readonly int RangeLow;
        public HuffmanLine(int prefLen, int rangeLen, int rangeLow)
        { PrefLen = prefLen; RangeLen = rangeLen; RangeLow = rangeLow; }
    }

    /// <summary>
    /// Parameters describing a Huffman table — either a standard one from
    /// Annex B or a runtime-decoded custom one. Order of <see cref="Lines"/>
    /// matters: the OOB entry (when <see cref="HtOob"/> is true) is the last
    /// line, and the second-to-last line with <c>PREFLEN = 0</c> is the
    /// HTLOW boundary catcher.
    /// </summary>
    internal sealed class HuffmanParams
    {
        public bool HtOob;
        public HuffmanLine[] Lines = Array.Empty<HuffmanLine>();
    }

    /// <summary>
    /// Built Huffman table — canonical-Huffman codes assigned to each
    /// non-zero-PREFLEN line (T.88 §B.3). Decoding reads bits from a
    /// <see cref="HuffmanBitReader"/> until a prefix matches, then consumes
    /// RANGELEN extra bits to form HTOFFSET, and combines per §B.4.
    /// </summary>
    internal sealed class HuffmanTable
    {
        private readonly HuffmanLine[] _lines;
        private readonly bool _htoob;
        private readonly int[] _codes;          // canonical code per line (-1 for unassigned PREFLEN=0)
        private readonly int _isLowIndex;       // line index that catches values below the lowest range, -1 if none
        private readonly int _oobIndex;         // line index for OOB marker, -1 if none

        public HuffmanTable(HuffmanParams p)
        {
            _lines = p.Lines;
            _htoob = p.HtOob;
            _codes = new int[_lines.Length];
            for (var i = 0; i < _codes.Length; i++) _codes[i] = -1;

            // T.88 §B.3 — canonical Huffman code assignment.
            var maxLen = 0;
            for (var i = 0; i < _lines.Length; i++)
                if (_lines[i].PrefLen > maxLen) maxLen = _lines[i].PrefLen;

            var lenCount = new int[maxLen + 2];
            for (var i = 0; i < _lines.Length; i++)
                if (_lines[i].PrefLen > 0) lenCount[_lines[i].PrefLen]++;

            var firstCode = 0;
            int curCode;
            for (var curLen = 1; curLen <= maxLen; curLen++)
            {
                firstCode = (firstCode + lenCount[curLen - 1]) << 1;
                curCode = firstCode;
                for (var i = 0; i < _lines.Length; i++)
                {
                    if (_lines[i].PrefLen == curLen)
                    {
                        _codes[i] = curCode;
                        curCode++;
                    }
                }
            }

            // Position-based markers per jbig2dec / T.88 conventions:
            //   - Last line is OOB if HtOob.
            //   - Line at index n - (HtOob ? 3 : 2) is the HTLOW boundary catcher.
            _oobIndex   = _htoob ? _lines.Length - 1 : -1;
            _isLowIndex = _lines.Length - (_htoob ? 3 : 2);
            if (_isLowIndex < 0 || _lines[_isLowIndex].PrefLen != 0) _isLowIndex = -1;
        }

        /// <summary>
        /// Decode one value. Returns the value through <paramref name="value"/>;
        /// returns false if OOB (the value is undefined in that case).
        /// Throws if the bitstream contains no prefix that matches.
        /// </summary>
        public bool Decode(HuffmanBitReader r, out int value)
        {
            // Bit-by-bit prefix match. Slow but spec-direct; correctness now,
            // optimisation later if it becomes a bottleneck.
            var code = 0;
            var len = 0;
            while (len < 32)
            {
                code = (code << 1) | (int)r.ReadBits(1);
                len++;

                int matchIdx = -1;
                for (var i = 0; i < _lines.Length; i++)
                {
                    if (_lines[i].PrefLen == len && _codes[i] == code)
                    {
                        matchIdx = i;
                        break;
                    }
                }
                if (matchIdx < 0) continue;

                if (matchIdx == _oobIndex)
                {
                    value = 0;
                    return false;
                }

                int htOffset = _lines[matchIdx].RangeLen > 0 ? (int)r.ReadBits(_lines[matchIdx].RangeLen) : 0;
                int v = _lines[matchIdx].RangeLow;
                if (matchIdx == _isLowIndex) v -= htOffset;
                else                          v += htOffset;
                value = v;
                return true;
            }
            throw new InvalidOperationException("Huffman prefix not found within 32 bits");
        }

        public int ReadBits(HuffmanBitReader r, int n) => (int)r.ReadBits(n);
    }
}
