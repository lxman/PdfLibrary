namespace FontParser.Tables.Cff.Type1
{
    /// <summary>
    /// Maps a character code to a glyph id (GID) through a CFF font's built-in Encoding
    /// (Adobe TN#5176 §12). Glyph ids are assigned sequentially from 1 in the order codes
    /// appear in the Encoding (GID 0 is always .notdef and is never encoded). Used to render
    /// symbolic CFF simple fonts whose PDF font dictionary supplies no usable /Encoding — the
    /// font relies on its own built-in code→glyph mapping instead.
    /// </summary>
    public static class CffEncodingLookup
    {
        /// <summary>
        /// Returns the glyph id for <paramref name="code"/> per the built-in encoding, or 0 if the
        /// code is unmapped or the encoding is null/predefined.
        /// </summary>
        public static ushort GetGlyphId(IEncoding? encoding, byte code)
        {
            switch (encoding)
            {
                case Encoding0 e0:
                    // Format 0: CodeArray[i] is the code assigned to glyph i+1.
                    for (var i = 0; i < e0.CodeArray.Count; i++)
                        if (e0.CodeArray[i] == code)
                            return (ushort)(i + 1);
                    return 0;

                case Encoding1 e1:
                {
                    // Format 1: ranges of sequential codes; GIDs run 1,2,3… across the ranges in order.
                    var gid = 1;
                    foreach (Range1 range in e1.Ranges)
                    {
                        for (var i = 0; i <= range.NumberLeft; i++)
                        {
                            if (range.First + i == code)
                                return (ushort)gid;
                            gid++;
                        }
                    }
                    return 0;
                }

                default:
                    return 0;
            }
        }
    }
}
