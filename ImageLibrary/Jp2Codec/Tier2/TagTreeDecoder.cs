using System;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// Tag-tree decoder (ISO/IEC 15444-1 B.10.2). A tag tree encodes a 2-D
    /// array of non-negative integers via a quad-tree-like pyramid: each
    /// internal node holds the minimum of the values beneath it, and the tree
    /// is decoded progressively against a sequence of strictly-increasing
    /// thresholds. Used by packet-header parsing to encode codeblock
    /// inclusion and missing-bitplane counts.
    ///
    /// The decoder maintains per-node state across calls within the same
    /// packet stream, because the encoder may emit partial information at
    /// threshold t1 and complete it at a later threshold t2 &gt; t1.
    /// </summary>
    internal sealed class TagTreeDecoder
    {
        private readonly TreeLevel[] _levels;
        private readonly int _leafWidth;

        public int LeafWidth => _leafWidth;
        public int LeafHeight => _levels[0].Height;
        public int LevelCount => _levels.Length;

        public TagTreeDecoder(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            _leafWidth = width;
            // Build levels from leaves up to root. Each level halves dims (ceil).
            var levels = new System.Collections.Generic.List<TreeLevel>();
            int w = width, h = height;
            levels.Add(new TreeLevel(w, h));
            while (w > 1 || h > 1)
            {
                w = (w + 1) / 2;
                h = (h + 1) / 2;
                levels.Add(new TreeLevel(w, h));
            }
            _levels = levels.ToArray();
        }

        /// <summary>
        /// Decode whether the value at leaf (<paramref name="x"/>, <paramref name="y"/>)
        /// is strictly less than <paramref name="threshold"/>. Reads bits from
        /// <paramref name="reader"/> only as needed to settle that question;
        /// state from this call is retained for future queries at higher
        /// thresholds on the same leaf.
        /// </summary>
        public bool DecodeLessThan(int x, int y, int threshold, PacketHeaderBitReader reader)
        {
            if (x < 0 || x >= _leafWidth) throw new ArgumentOutOfRangeException(nameof(x));
            if (y < 0 || y >= LeafHeight) throw new ArgumentOutOfRangeException(nameof(y));
            if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (reader is null) throw new ArgumentNullException(nameof(reader));

            // Walk from root down to leaf, decoding bits along the way.
            // Track the "inherited low" — a child's value cannot be lower than
            // its parent's, since parents hold the minimum over their subtree.
            int parentLow = 0;
            for (int level = _levels.Length - 1; level >= 0; level--)
            {
                int xi = x >> level;
                int yi = y >> level;
                TreeLevel L = _levels[level];
                int idx = yi * L.Width + xi;

                int low = Math.Max(L.Low[idx], parentLow);
                bool known = L.Known[idx];

                while (low < threshold && !known)
                {
                    int b = reader.ReadBit();
                    if (b == 1)
                    {
                        known = true;
                    }
                    else
                    {
                        low++;
                    }
                }
                L.Low[idx] = low;
                L.Known[idx] = known;
                parentLow = low;
            }

            // The leaf is the bottom level (index 0). If its low has reached the
            // threshold and it's not marked known, the actual value is >= threshold.
            TreeLevel leaf = _levels[0];
            int leafIdx = y * leaf.Width + x;
            return leaf.Low[leafIdx] < threshold;
        }

        /// <summary>
        /// Decode the exact value at the leaf using the spec's increment-by-1
        /// protocol, which terminates on the first '1' bit. Useful when the
        /// caller doesn't have a fixed threshold (e.g. number-of-coding-passes
        /// uses a different code-length, but zero-bitplane and inclusion-tag
        /// trees prefer the threshold form).
        /// </summary>
        public int DecodeValue(int x, int y, PacketHeaderBitReader reader)
        {
            int t = 0;
            while (!DecodeLessThan(x, y, t + 1, reader))
            {
                t++;
                // Safety cap: a tag tree value > 65535 is suspect; the largest
                // legitimate use carries 38-bit bit depths, capped well below.
                if (t > 0xFFFF)
                    throw new System.IO.InvalidDataException("Tag tree value exceeded 65 535; likely corrupt stream.");
            }
            return _levels[0].Low[y * _levels[0].Width + x];
        }

        private sealed class TreeLevel
        {
            public int Width { get; }
            public int Height { get; }
            public int[] Low { get; }
            public bool[] Known { get; }

            public TreeLevel(int width, int height)
            {
                Width = width;
                Height = height;
                Low = new int[width * height];
                Known = new bool[width * height];
            }
        }
    }
}
