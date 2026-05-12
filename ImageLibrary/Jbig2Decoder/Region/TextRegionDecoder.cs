using System;
using Jbig2Decoder.Arith;
using Jbig2Decoder.Huffman;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Text region decoder (T.88 §6.4) — arithmetic and Huffman modes.
    ///
    /// Lays out symbol instances onto a region bitmap. For each instance: a
    /// symbol ID is decoded (via IAID under arithmetic, or a custom symbol-ID
    /// Huffman table under Huffman), the glyph is looked up in the merged
    /// dictionary list, optionally refined per instance via the refinement
    /// region decoder, and composited at a position derived from running
    /// strip-relative coordinates plus REFCORNER / TRANSPOSED rules.
    /// </summary>
    internal sealed class TextRegionDecoder
    {
        public void Decode(TextRegionParams p, byte[] arithData, int offset, int length, Bitmap output)
        {
            if (p.SbHuff)
            {
                DecodeHuffman(p, arithData, offset, length, output);
                return;
            }

            var mq = new MqDecoder(arithData, offset, length);
            DecodeArithmetic(p, mq, contexts: null, output);
        }

        /// <summary>
        /// Arithmetic-mode text-region decode against a caller-supplied MQ stream
        /// and (optionally) caller-supplied integer-context bundle. When <paramref name="contexts"/>
        /// is null this allocates fresh contexts; when non-null the caller's contexts
        /// are reused, which is required by the symbol-dictionary refagg path
        /// (T.88 §6.5.8.2 with REFAGGNINST > 1) where the same context set persists
        /// across many text-region invocations within one SD decode.
        /// </summary>
        public void DecodeArithmetic(TextRegionParams p, MqDecoder mq, TextRegionContexts? contexts, Bitmap output)
        {
            // Decoder contexts shared across all instances (T.88 §6.4.4-6.4.10).
            IntegerDecoder iadt, iafs, iads, iait;
            IntegerDecoder? iari, iardw, iardh, iardx, iardy;
            IaidDecoder iaid;
            byte[]? grStats;
            RefinementRegionDecoder? refDecoder;

            if (contexts != null)
            {
                iadt = contexts.Iadt;
                iafs = contexts.Iafs;
                iads = contexts.Iads;
                iait = contexts.Iait;
                iari = contexts.Iari;
                iardw = contexts.Iardw;
                iardh = contexts.Iardh;
                iardx = contexts.Iardx;
                iardy = contexts.Iardy;
                iaid = contexts.Iaid;
                grStats = contexts.GrStats;
                refDecoder = contexts.RefDecoder;
            }
            else
            {
                iadt = new IntegerDecoder(mq, "IADT");
                iafs = new IntegerDecoder(mq, "IAFS");
                iads = new IntegerDecoder(mq, "IADS");
                iait = new IntegerDecoder(mq, "IAIT");
                iari = p.SbRefine ? new IntegerDecoder(mq, "IARI") : null;
                iardw = p.SbRefine ? new IntegerDecoder(mq, "IARDW") : null;
                iardh = p.SbRefine ? new IntegerDecoder(mq, "IARDH") : null;
                iardx = p.SbRefine ? new IntegerDecoder(mq, "IARDX") : null;
                iardy = p.SbRefine ? new IntegerDecoder(mq, "IARDY") : null;

                var numSyms = 0;
                foreach (SymbolDictionary d in p.Dicts) numSyms += d.Count;

                // T.88 §6.4.5: SBSYMCODELEN = ceil(log2(SBNUMSYMS)). When SBNUMSYMS = 1
                // the result is 0 and IAID decodes zero MQ bits (deterministically returns 0).
                var symCodeLen = 0;
                while (1 << symCodeLen < numSyms) symCodeLen++;
                iaid = new IaidDecoder(mq, symCodeLen);

                // Refinement gear, lazily allocated on first refine.
                grStats = null;
                refDecoder = null;
            }

            // 6.4.5 (1) — clear region to default pixel value.
            byte fill = p.SbDefPixel ? (byte)0xFF : (byte)0x00;
            for (var i = 0; i < output.Data.Length; i++) output.Data[i] = fill;

            // 6.4.6 — initial STRIPT.
            if (!iadt.Decode(out int stript0))
                throw new InvalidOperationException("OOB decoding initial strip T");
            int stript = -p.SbStrips * stript0;
            var firsts = 0;
            uint ninstances = 0;

            // 6.4.5 (3) — strip loop.
            while (ninstances < p.SbNumInstances)
            {
                if (!iadt.Decode(out int dt))
                    throw new InvalidOperationException("OOB decoding delta T");
                stript += dt * p.SbStrips;

                var firstSymbol = true;
                var curs = 0;
                while (true)
                {
                    int curt;
                    if (firstSymbol)
                    {
                        if (!iafs.Decode(out int dfs))
                            throw new InvalidOperationException("OOB decoding first-symbol S delta");
                        firsts += dfs;
                        curs = firsts;
                        firstSymbol = false;
                    }
                    else
                    {
                        // Defensive: if the encoder emitted more instances than
                        // SBNUMINSTANCES advertises, jbig2dec warns and bails
                        // here. Mirroring that keeps malformed streams from
                        // looping forever even when the IADS-OOB never arrives.
                        if (ninstances > p.SbNumInstances)
                            break;
                        if (!iads.Decode(out int ids))
                            break;     // OOB — end of strip
                        curs += ids + p.SbDsOffset;
                    }

                    if (p.SbStrips == 1)
                    {
                        curt = 0;
                    }
                    else
                    {
                        if (!iait.Decode(out curt))
                            throw new InvalidOperationException("OOB decoding instance T");
                    }
                    int t = stript + curt;

                    int id = iaid.Decode();

                    // Look up symbol bitmap across the dictionary list.
                    Bitmap? ib = null;
                    {
                        int searchId = id;
                        foreach (SymbolDictionary dict in p.Dicts)
                        {
                            if (searchId < dict.Count)
                            {
                                ib = dict.Glyphs[searchId];
                                break;
                            }
                            searchId -= dict.Count;
                        }
                    }

                    var ri = 0;
                    if (p.SbRefine)
                    {
                        if (!iari!.Decode(out ri))
                            throw new InvalidOperationException("OOB decoding refinement indicator");
                    }

                    if (ri != 0)
                    {
                        // 6.4.11 — per-instance refinement.
                        if (!iardw!.Decode(out int rdw)) throw new InvalidOperationException("OOB decoding RDW");
                        if (!iardh!.Decode(out int rdh)) throw new InvalidOperationException("OOB decoding RDH");
                        if (!iardx!.Decode(out int rdx)) throw new InvalidOperationException("OOB decoding RDX");
                        if (!iardy!.Decode(out int rdy)) throw new InvalidOperationException("OOB decoding RDY");

                        if (ib == null) throw new InvalidOperationException("Refinement requires a base glyph");
                        if (ib.Width + rdw < 0 || ib.Height + rdh < 0)
                            throw new InvalidOperationException("Refinement produces negative dimensions");

                        var refImage = new Bitmap(ib.Width + rdw, ib.Height + rdh);
                        if (refDecoder == null)
                        {
                            refDecoder = new RefinementRegionDecoder();
                            grStats = new byte[RefinementRegionDecoder.StatsSizeFor(p.SbRTemplate ? 1 : 0)];
                            if (contexts != null)
                            {
                                contexts.RefDecoder = refDecoder;
                                contexts.GrStats = grStats;
                            }
                        }

                        var rp = new RefinementRegionParams
                        {
                            GrTemplate = p.SbRTemplate ? 1 : 0,
                            TpgrOn = false,
                            Reference = ib,
                            ReferenceDx = (rdw >> 1) + rdx,
                            ReferenceDy = (rdh >> 1) + rdy,
                            Grat = p.Sbrat,
                        };
                        refDecoder.Decode(rp, mq, grStats!, refImage);
                        ib = refImage;
                    }

                    // 6.4.5 (3c.vi) — pre-compose CURS bump for transposed/right-anchored cases.
                    if (!p.Transposed && (int)p.RefCorner > 1 && ib != null)
                        curs += ib.Width - 1;
                    else if (p.Transposed && ((int)p.RefCorner & 1) == 0 && ib != null)
                        curs += ib.Height - 1;

                    int s = curs;

                    // 6.4.5 (3c.viii) — placement based on REFCORNER + TRANSPOSED.
                    int x, y;
                    if (!p.Transposed)
                    {
                        switch (p.RefCorner)
                        {
                            case RefCorner.TopLeft:     x = s; y = t; break;
                            case RefCorner.TopRight:    x = ib != null ? s - ib.Width + 1 : s + 1; y = t; break;
                            case RefCorner.BottomLeft:  x = s; y = ib != null ? t - ib.Height + 1 : t + 1; break;
                            default:                    /* BottomRight */
                                x = ib != null ? s - ib.Width + 1 : s + 1;
                                y = ib != null ? t - ib.Height + 1 : t + 1;
                                break;
                        }
                    }
                    else
                    {
                        switch (p.RefCorner)
                        {
                            case RefCorner.TopLeft:     x = t; y = s; break;
                            case RefCorner.TopRight:    x = ib != null ? t - ib.Width + 1 : t + 1; y = s; break;
                            case RefCorner.BottomLeft:  x = t; y = ib != null ? s - ib.Height + 1 : s + 1; break;
                            default:                    /* BottomRight */
                                x = ib != null ? t - ib.Width + 1 : t + 1;
                                y = ib != null ? s - ib.Height + 1 : s + 1;
                                break;
                        }
                    }

                    // 6.4.5 (3c.ix) — composite glyph onto the region bitmap.
                    if (ib != null)
                        Compose(output, ib, x, y, p.SbCombOp);

                    // 6.4.5 (3c.x) — post-compose CURS bump.
                    if (ib != null && !p.Transposed && (int)p.RefCorner < 2)
                        curs += ib.Width - 1;
                    else if (ib != null && p.Transposed && ((int)p.RefCorner & 1) != 0)
                        curs += ib.Height - 1;

                    ninstances++;
                    // Spec §6.4.5: a strip is terminated by an IADS-OOB, not by
                    // hitting SBNUMINSTANCES — so continue the inner loop until
                    // the IADS read at the top of the next iteration returns
                    // OOB. Early-exiting here desyncs the MQ stream because the
                    // encoder always emits the trailing IADS-OOB.
                }
            }
        }

        // Selector → table mapping, T.88 §7.4.3.1.1 / Table 27. Selector value 3
        // (or 1 for Rsize) means the table is supplied via a referred-to segment
        // and is pre-resolved into <paramref name="userTables"/> at the
        // <paramref name="slot"/> position fixed by spec §7.4.3.1.7.
        private static HuffmanParams TableForFs(int sel, HuffmanParams?[]? userTables, int slot) => sel switch
        {
            0 => StandardHuffmanTables.F,
            1 => StandardHuffmanTables.G,
            3 => UserTable(userTables, slot, "SBHUFFFS"),
            _ => throw new NotSupportedException($"reserved SBHUFFFS selector {sel}"),
        };
        private static HuffmanParams TableForDs(int sel, HuffmanParams?[]? userTables, int slot) => sel switch
        {
            0 => StandardHuffmanTables.H,
            1 => StandardHuffmanTables.I,
            2 => StandardHuffmanTables.J,
            3 => UserTable(userTables, slot, "SBHUFFDS"),
            _ => throw new NotSupportedException($"reserved SBHUFFDS selector {sel}"),
        };
        private static HuffmanParams TableForDt(int sel, HuffmanParams?[]? userTables, int slot) => sel switch
        {
            0 => StandardHuffmanTables.K,
            1 => StandardHuffmanTables.L,
            2 => StandardHuffmanTables.M,
            3 => UserTable(userTables, slot, "SBHUFFDT"),
            _ => throw new NotSupportedException($"reserved SBHUFFDT selector {sel}"),
        };
        private static HuffmanParams TableForRdwxhy(int sel, HuffmanParams?[]? userTables, int slot) => sel switch
        {
            0 => StandardHuffmanTables.N,
            1 => StandardHuffmanTables.O,
            3 => UserTable(userTables, slot, "SBHUFFRDx"),
            _ => throw new NotSupportedException($"reserved SBHUFFRDx selector {sel}"),
        };
        private static HuffmanParams TableForRsize(int sel, HuffmanParams?[]? userTables, int slot) => sel switch
        {
            0 => StandardHuffmanTables.A,
            1 => UserTable(userTables, slot, "SBHUFFRSIZE"),
            _ => throw new NotSupportedException($"reserved SBHUFFRSIZE selector {sel}"),
        };

        private static HuffmanParams UserTable(HuffmanParams?[]? userTables, int slot, string what)
        {
            if (userTables == null || slot >= userTables.Length || userTables[slot] == null)
                throw new InvalidOperationException(
                    $"{what} marked user-defined but no user Huffman table supplied at slot {slot}");
            return userTables[slot]!;
        }

        // Build the per-region symbol-ID Huffman table from the segment data
        // (T.88 §7.4.3.1.7). The table itself is encoded inline at the head of
        // the region body using a 35-runcode prelude.
        private static HuffmanTable BuildSymbolIdTable(HuffmanBitReader r, int sbNumSyms)
        {
            // 1) 35 entries × 4-bit raw PREFLENs form the runcode table.
            var runLines = new HuffmanLine[35];
            for (var i = 0; i < 35; i++)
                runLines[i] = new HuffmanLine((int)r.ReadBits(4), 0, i);
            var runTable = new HuffmanTable(new HuffmanParams { HtOob = false, Lines = runLines });

            // 2) Decode SBNUMSYMS symbol-ID PREFLENs using the runcode table.
            var prefLens = new int[sbNumSyms];
            var idx = 0;
            var prevLen = 0;
            while (idx < sbNumSyms)
            {
                if (!runTable.Decode(r, out int code))
                    throw new InvalidOperationException("OOB decoding symbol-ID Huffman runcode");

                int runLen, valLen;
                if (code < 32)
                {
                    valLen = code;
                    runLen = 1;
                }
                else if (code == 32)
                {
                    if (idx == 0) throw new InvalidOperationException("symbol-ID runcode 32 with no antecedent");
                    valLen = prevLen;
                    runLen = (int)r.ReadBits(2) + 3;
                }
                else if (code == 33)
                {
                    valLen = 0;
                    runLen = (int)r.ReadBits(3) + 3;
                }
                else if (code == 34)
                {
                    valLen = 0;
                    runLen = (int)r.ReadBits(7) + 11;
                }
                else
                {
                    throw new InvalidOperationException($"symbol-ID runcode out of range: {code}");
                }

                if (idx + runLen > sbNumSyms) runLen = sbNumSyms - idx;
                for (var k = 0; k < runLen; k++) prefLens[idx + k] = valLen;
                if (code != 33 && code != 34) prevLen = valLen;
                idx += runLen;
            }

            // 3) Skip to byte boundary.
            r.AlignToByte();

            // 4) Build the per-symbol-ID Huffman table.
            var symLines = new HuffmanLine[sbNumSyms];
            for (var i = 0; i < sbNumSyms; i++)
                symLines[i] = new HuffmanLine(prefLens[i], 0, i);
            return new HuffmanTable(new HuffmanParams { HtOob = false, Lines = symLines });
        }

        private void DecodeHuffman(TextRegionParams p, byte[] arithData, int offset, int length, Bitmap output)
        {
            var r = new HuffmanBitReader(arithData, offset, length);
            DecodeHuffmanWithReader(p, arithData, offset + length, r, output);
        }

        // Inner Huffman text-region decode that reuses a caller-supplied bit reader.
        // Used by symbol-dictionary multi-instance refagg (T.88 §6.5.8.2.4): the
        // outer SD reader is shared into the inner text region so the bit stream
        // continues seamlessly. <paramref name="dataEnd"/> bounds any inner
        // arithmetic-coded refinement slices that follow Huffman-coded subfields.
        internal void DecodeHuffmanWithReader(
            TextRegionParams p,
            byte[] arithData, int dataEnd,
            HuffmanBitReader r,
            Bitmap output)
        {
            var sbNumSyms = 0;
            foreach (SymbolDictionary d in p.Dicts) sbNumSyms += d.Count;

            // Pull selector bits from huffman_flags (T.88 §7.4.3.1.1).
            int selFs   = (p.SbHuffFlags >>  0) & 3;
            int selDs   = (p.SbHuffFlags >>  2) & 3;
            int selDt   = (p.SbHuffFlags >>  4) & 3;
            int selRdw  = (p.SbHuffFlags >>  6) & 3;
            int selRdh  = (p.SbHuffFlags >>  8) & 3;
            int selRdx  = (p.SbHuffFlags >> 10) & 3;
            int selRdy  = (p.SbHuffFlags >> 12) & 3;
            int selRsz  = (p.SbHuffFlags >> 14) & 1;

            // User-defined Huffman tables are slotted in selector order: 0=Fs, 1=Ds,
            // 2=Dt, 3=Rdw, 4=Rdh, 5=Rdx, 6=Rdy, 7=Rsize (T.88 §7.4.3.1.7).
            HuffmanParams?[]? ut = p.UserTables;
            var hFs    = new HuffmanTable(TableForFs(selFs, ut, 0));
            var hDs    = new HuffmanTable(TableForDs(selDs, ut, 1));
            var hDt    = new HuffmanTable(TableForDt(selDt, ut, 2));
            HuffmanTable? hRdw   = p.SbRefine ? new HuffmanTable(TableForRdwxhy(selRdw, ut, 3)) : null;
            HuffmanTable? hRdh   = p.SbRefine ? new HuffmanTable(TableForRdwxhy(selRdh, ut, 4)) : null;
            HuffmanTable? hRdx   = p.SbRefine ? new HuffmanTable(TableForRdwxhy(selRdx, ut, 5)) : null;
            HuffmanTable? hRdy   = p.SbRefine ? new HuffmanTable(TableForRdwxhy(selRdy, ut, 6)) : null;
            HuffmanTable? hRsize = p.SbRefine ? new HuffmanTable(TableForRsize(selRsz, ut, 7)) : null;

            // Caller may supply a pre-built symbol-ID table (SD-internal
            // refagg path uses trivial fixed-length codes — see pdfium's
            // SddProc); in that case we skip the standalone-text-region
            // 35-runcode prelude entirely.
            HuffmanTable sbsymcodes = p.PrebuiltSbSymCodes != null
                ? new HuffmanTable(p.PrebuiltSbSymCodes)
                : BuildSymbolIdTable(r, sbNumSyms);

            // Refinement gear, lazy.
            byte[]? grStats = null;
            RefinementRegionDecoder? refDecoder = null;

            // 6.4.5 (1) — clear region to default pixel.
            byte fill = p.SbDefPixel ? (byte)0xFF : (byte)0x00;
            for (var i = 0; i < output.Data.Length; i++) output.Data[i] = fill;

            // 6.4.6 — initial STRIPT.
            if (!hDt.Decode(r, out int stript0))
                throw new InvalidOperationException("OOB decoding initial strip T (Huffman)");
            int stript = -p.SbStrips * stript0;
            var firsts = 0;
            uint ninstances = 0;

            while (ninstances < p.SbNumInstances)
            {
                if (!hDt.Decode(r, out int dt))
                    throw new InvalidOperationException("OOB decoding delta T (Huffman)");
                stript += dt * p.SbStrips;

                var firstSymbol = true;
                var curs = 0;
                while (true)
                {
                    int curt;
                    if (firstSymbol)
                    {
                        if (!hFs.Decode(r, out int dfs))
                            throw new InvalidOperationException("OOB decoding first-symbol S delta (Huffman)");
                        firsts += dfs;
                        curs = firsts;
                        firstSymbol = false;
                    }
                    else
                    {
                        if (!hDs.Decode(r, out int ids))
                            break;     // OOB ends strip
                        curs += ids + p.SbDsOffset;
                    }

                    if (p.SbStrips == 1)
                    {
                        curt = 0;
                    }
                    else
                    {
                        curt = (int)r.ReadBits(p.LogSbStrips);
                    }
                    int t = stript + curt;

                    if (!sbsymcodes.Decode(r, out int id))
                        throw new InvalidOperationException("OOB decoding symbol ID (Huffman)");

                    Bitmap? ib = null;
                    {
                        int searchId = id;
                        foreach (SymbolDictionary dict in p.Dicts)
                        {
                            if (searchId < dict.Count) { ib = dict.Glyphs[searchId]; break; }
                            searchId -= dict.Count;
                        }
                    }

                    var ri = 0;
                    if (p.SbRefine)
                        ri = (int)r.ReadBits(1);

                    if (ri != 0)
                    {
                        if (!hRdw!.Decode(r, out int rdw)) throw new InvalidOperationException("OOB RDW");
                        if (!hRdh!.Decode(r, out int rdh)) throw new InvalidOperationException("OOB RDH");
                        if (!hRdx!.Decode(r, out int rdx)) throw new InvalidOperationException("OOB RDX");
                        if (!hRdy!.Decode(r, out int rdy)) throw new InvalidOperationException("OOB RDY");
                        if (!hRsize!.Decode(r, out int bmsize)) throw new InvalidOperationException("OOB RSIZE");
                        r.AlignToByte();

                        if (ib == null) throw new InvalidOperationException("Refinement requires a base glyph");
                        if (ib.Width + rdw < 0 || ib.Height + rdh < 0)
                            throw new InvalidOperationException("Refinement produces negative dimensions");

                        var refImage = new Bitmap(ib.Width + rdw, ib.Height + rdh);
                        if (refDecoder == null)
                        {
                            refDecoder = new RefinementRegionDecoder();
                            // SD-internal multi-instance refagg passes a shared
                            // grStats array so refinement context probabilities
                            // persist across symbol decodes; standalone text
                            // regions get fresh stats.
                            grStats = p.SharedGrStats
                                ?? new byte[RefinementRegionDecoder.StatsSizeFor(p.SbRTemplate ? 1 : 0)];
                        }

                        // Refinement is arithmetic-coded even inside a Huffman text region.
                        // r.Offset is the absolute byte index in arithData (the Huffman bit
                        // reader was constructed with `offset` as its start), so we feed the
                        // MQ decoder that absolute index — adding `offset` again would double-
                        // count it and trip MqDecoder's range guard with ArgumentOutOfRangeException.
                        var mq = new MqDecoder(arithData, r.Offset, bmsize);
                        var rp = new RefinementRegionParams
                        {
                            GrTemplate = p.SbRTemplate ? 1 : 0,
                            TpgrOn = false,
                            Reference = ib,
                            ReferenceDx = (rdw >> 1) + rdx,
                            ReferenceDy = (rdh >> 1) + rdy,
                            Grat = p.Sbrat,
                        };
                        refDecoder.Decode(rp, mq, grStats!, refImage);
                        ib = refImage;
                        r.Advance(bmsize);
                    }

                    if (!p.Transposed && (int)p.RefCorner > 1 && ib != null)
                        curs += ib.Width - 1;
                    else if (p.Transposed && ((int)p.RefCorner & 1) == 0 && ib != null)
                        curs += ib.Height - 1;

                    int s = curs;
                    int x, y;
                    if (!p.Transposed)
                    {
                        switch (p.RefCorner)
                        {
                            case RefCorner.TopLeft:     x = s; y = t; break;
                            case RefCorner.TopRight:    x = ib != null ? s - ib.Width + 1 : s + 1; y = t; break;
                            case RefCorner.BottomLeft:  x = s; y = ib != null ? t - ib.Height + 1 : t + 1; break;
                            default:
                                x = ib != null ? s - ib.Width + 1 : s + 1;
                                y = ib != null ? t - ib.Height + 1 : t + 1;
                                break;
                        }
                    }
                    else
                    {
                        switch (p.RefCorner)
                        {
                            case RefCorner.TopLeft:     x = t; y = s; break;
                            case RefCorner.TopRight:    x = ib != null ? t - ib.Width + 1 : t + 1; y = s; break;
                            case RefCorner.BottomLeft:  x = t; y = ib != null ? s - ib.Height + 1 : s + 1; break;
                            default:
                                x = ib != null ? t - ib.Width + 1 : t + 1;
                                y = ib != null ? s - ib.Height + 1 : s + 1;
                                break;
                        }
                    }

                    if (ib != null) Compose(output, ib, x, y, p.SbCombOp);

                    if (ib != null && !p.Transposed && (int)p.RefCorner < 2)
                        curs += ib.Width - 1;
                    else if (ib != null && p.Transposed && ((int)p.RefCorner & 1) != 0)
                        curs += ib.Height - 1;

                    ninstances++;
                    // Spec §6.4.5: a strip terminates on DS-OOB, not on hitting
                    // SBNUMINSTANCES. The trailing DS-OOB must be consumed so the
                    // bit stream stays aligned for whatever follows (e.g. SD-
                    // internal multi-instance refagg, where the SD continues
                    // reading from the same bit reader after this text region).
                    if (ninstances > p.SbNumInstances) break;
                }
            }
        }

        private static void Compose(Bitmap dst, Bitmap src, int dx, int dy, int op)
        {
            // Bit-level compositor. Slow but correct; matches jbig2dec's
            // jbig2_image_compose for 1-bit images.
            int sw = src.Width;
            int sh = src.Height;
            for (var sy = 0; sy < sh; sy++)
            {
                int ty = dy + sy;
                if ((uint)ty >= (uint)dst.Height) continue;
                for (var sx = 0; sx < sw; sx++)
                {
                    int tx = dx + sx;
                    if ((uint)tx >= (uint)dst.Width) continue;

                    int s = src.GetPixel(sx, sy);
                    int d = dst.GetPixel(tx, ty);
                    int n = op switch
                    {
                        0 => s | d,
                        1 => s & d,
                        2 => s ^ d,
                        3 => 1 - (s ^ d),
                        4 => s,
                        _ => s | d,
                    };
                    dst.SetPixel(tx, ty, n);
                }
            }
        }
    }
}
