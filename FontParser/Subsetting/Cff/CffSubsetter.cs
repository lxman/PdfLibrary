using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Tables.Cff.Type1;

namespace FontParser.Subsetting.Cff
{
    /// <summary>
    /// Subsets a CFF font program (from a PDF /FontFile3) to a set of glyph IDs using the
    /// "keep GID numbering, blank unused charstrings" strategy: the CharStrings INDEX keeps its
    /// original glyph count (used glyphs verbatim, GID 0 always kept, unused replaced by a 1-byte
    /// endchar), and charset / encoding / global+local subroutines are kept verbatim. Only the
    /// Header, Top DICT, Private DICT and CharStrings INDEX are re-encoded (with recomputed offsets,
    /// using the fixed 5-byte offset form so layout is a single pass).
    /// </summary>
    public static class CffSubsetter
    {
        private const int OpCharset = 15;
        private const int OpEncoding = 16;
        private const int OpCharStrings = 17;
        private const int OpPrivate = 18;
        private const int OpSubrs = 19;

        /// <summary>Produces a subset CFF retaining <paramref name="usedGids"/> (GID 0 is always kept).</summary>
        public static byte[] Subset(Type1Table source, ISet<int> usedGids)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (usedGids is null) throw new ArgumentNullException(nameof(usedGids));
            if (source.IsCid)
                throw new NotSupportedException("CID-keyed CFF subsetting is handled separately.");
            return SubsetNonCid(source, usedGids);
        }

        private static byte[] SubsetNonCid(Type1Table source, ISet<int> usedGids)
        {
            int numGlyphs = source.RawCharStrings.Count;

            // 1. CharStrings INDEX: keep used + GID 0 verbatim, blank the rest to a 1-byte endchar.
            var endchar = new byte[] { 0x0e };
            var charStrings = new List<byte[]>(numGlyphs);
            for (var gid = 0; gid < numGlyphs; gid++)
                charStrings.Add(gid == 0 || usedGids.Contains(gid)
                    ? source.RawCharStrings[gid].ToArray()
                    : endchar);
            byte[] charStringsBytes = CffWriter.WriteIndex(charStrings);

            // 2. Private DICT: copy every operator except Subrs verbatim; re-add Subrs pointing just past
            //    the Private DICT (where the Local Subr INDEX is placed) when local subrs exist.
            bool hasLocalSubrs = source.LocalSubroutines.Count > 0;
            var priv = new CffDictBuilder();
            foreach (DictEntry e in Tokenize(source.RawPrivateDict))
                if (e.Operator != OpSubrs)
                    priv.AppendRaw(e.Raw);
            if (hasLocalSubrs)
            {
                int subrsPos = priv.AddOffset(OpSubrs);
                priv.PatchOffset(subrsPos, priv.Length); // Subrs offset is relative to the Private DICT start
            }
            byte[] privateBytes = priv.Build();
            byte[] localSubrBytes = hasLocalSubrs
                ? CffWriter.WriteIndex(source.LocalSubroutines)
                : Array.Empty<byte>();

            // 3. Top DICT: copy verbatim except the offset operators, which become fixed-width placeholders.
            bool customCharset = source.RawCharset.Length > 0;
            var top = new CffDictBuilder();
            int charsetPos = -1, charStringsPos = -1, privSizePos = -1, privOffPos = -1;
            foreach (DictEntry e in Tokenize(source.RawTopDict))
            {
                switch (e.Operator)
                {
                    case OpCharStrings:
                        charStringsPos = top.AddOffset(OpCharStrings);
                        break;
                    case OpPrivate:
                        (privSizePos, privOffPos) = top.AddOffsetPair(OpPrivate);
                        break;
                    case OpCharset:
                        if (customCharset) charsetPos = top.AddOffset(OpCharset);
                        else top.AppendRaw(e.Raw); // predefined charset (0/1/2): keep the literal value
                        break;
                    case OpEncoding:
                        // Drop the CFF Encoding operator -> defaults to Standard (0). In PDF the font
                        // dictionary's /Encoding governs code->glyph for simple fonts, so the CFF's own
                        // Encoding is unused and safely omitted (and avoids carrying a custom table).
                        break;
                    default:
                        top.AppendRaw(e.Raw);
                        break;
                }
            }
            byte[] topDictBytes = top.Build();
            byte[] topDictIndexBytes = CffWriter.WriteIndex(new[] { topDictBytes });
            int dataStart = topDictIndexBytes.Length - topDictBytes.Length; // INDEX data sits at the end

            // 4. Verbatim sections + layout. CharStrings is LAST so shrinking it shifts nothing else.
            byte[] header = { 1, 0, 4, 1 };
            byte[] nameIndex = CffWriter.WriteIndex(source.RawNameIndex);     // verbatim (byte-exact)
            byte[] stringIndex = CffWriter.WriteIndex(source.RawStringIndex); // verbatim (preserves SIDs)
            byte[] globalSubr = CffWriter.WriteIndex(source.GlobalSubroutines);
            byte[] charset = customCharset ? source.RawCharset : Array.Empty<byte>();

            int pos = header.Length + nameIndex.Length + topDictIndexBytes.Length
                      + stringIndex.Length + globalSubr.Length;
            int charsetAbs = pos; pos += charset.Length;
            int privateAbs = pos; pos += privateBytes.Length;
            pos += localSubrBytes.Length;
            int charStringsAbs = pos; pos += charStringsBytes.Length;

            // 5. Backfill the Top DICT offsets in place inside the wrapped INDEX.
            if (customCharset) PatchBE32(topDictIndexBytes, dataStart + charsetPos, charsetAbs);
            PatchBE32(topDictIndexBytes, dataStart + charStringsPos, charStringsAbs);
            PatchBE32(topDictIndexBytes, dataStart + privSizePos, privateBytes.Length);
            PatchBE32(topDictIndexBytes, dataStart + privOffPos, privateAbs);

            // 6. Concatenate.
            var outBuf = new byte[pos];
            var w = 0;
            void Append(byte[] b) { Array.Copy(b, 0, outBuf, w, b.Length); w += b.Length; }
            Append(header);
            Append(nameIndex);
            Append(topDictIndexBytes);
            Append(stringIndex);
            Append(globalSubr);
            Append(charset);
            Append(privateBytes);
            Append(localSubrBytes);
            Append(charStringsBytes);
            return outBuf;
        }

        private static void PatchBE32(byte[] buf, int pos, int value)
        {
            buf[pos]     = (byte)(value >> 24);
            buf[pos + 1] = (byte)(value >> 16);
            buf[pos + 2] = (byte)(value >> 8);
            buf[pos + 3] = (byte)value;
        }

        private readonly struct DictEntry
        {
            public int Operator { get; }
            public double[] Operands { get; }
            public byte[] Raw { get; }

            public DictEntry(int op, double[] operands, byte[] raw)
            {
                Operator = op;
                Operands = operands;
                Raw = raw;
            }
        }

        /// <summary>Walk a DICT byte stream into (operator, operands, raw-entry-bytes) tuples, reusing the
        /// reader's operand decoders so operand byte-lengths match exactly.</summary>
        private static List<DictEntry> Tokenize(byte[] dict)
        {
            var result = new List<DictEntry>();
            var operands = new List<double>();
            int i = 0, entryStart = 0;
            while (i < dict.Length)
            {
                byte b = dict[i];
                if (b <= 21) // operator (12 = escape -> 2 bytes)
                {
                    int op = b == 12 ? (0x0C00 | dict[i + 1]) : b;
                    i += b == 12 ? 2 : 1;
                    var raw = new byte[i - entryStart];
                    Array.Copy(dict, entryStart, raw, 0, raw.Length);
                    result.Add(new DictEntry(op, operands.ToArray(), raw));
                    operands.Clear();
                    entryStart = i;
                }
                else if (b == 30) operands.Add(Calc.Double(dict, ref i)); // real
                else operands.Add(Calc.Integer(dict, ref i));             // integer (28/29/32-254)
            }
            return result;
        }
    }
}
