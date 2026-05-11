using System;
using System.Collections.Generic;
using ImageLibrary.Compression.Ccitt;
using Jbig2Decoder.Arith;
using Jbig2Decoder.Huffman;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Region
{
    /// <summary>
    /// Symbol dictionary decoder (T.88 §6.5).
    ///
    /// Implements the arithmetic-mode non-refagg path (independent generic
    /// regions per symbol, organised into height classes), the arithmetic-mode
    /// refagg path (single-instance refinement and multi-instance via an inner
    /// text region using a SD-shared context bundle), and the Huffman-mode,
    /// non-refagg path (per-class MMR-coded or uncompressed collective bitmaps,
    /// sliced by SDNEWSYMWIDTHS). The Huffman+refagg variant is not yet
    /// implemented.
    /// </summary>
    internal sealed class SymbolDictionaryDecoder
    {
        public SymbolDictionary Decode(SymbolDictionaryParams p, byte[] arithData, int offset, int length)
        {
            if (p.SdHuff && p.SdRefAgg) return DecodeHuffmanRefAgg(p, arithData, offset, length);
            if (p.SdHuff) return DecodeHuffmanNoRefAgg(p, arithData, offset, length);

            var mq = new MqDecoder(arithData, offset, length);
            var iadh = new IntegerDecoder(mq, "IADH");
            var iadw = new IntegerDecoder(mq, "IADW");
            var iaex = new IntegerDecoder(mq, "IAEX");

            // Generic-region context for symbol bitmaps. Per §6.5.5 these stats
            // persist across all symbols in the dictionary (not reset per height
            // class). When the SD requests "use bitmap coding context"
            // (T.88 §7.4.2.1.1 bit 8), seed from a referred-to SD's retained
            // stats instead of starting from zero.
            byte[] gbStats;
            if (p.SdRefAgg)
            {
                gbStats = null!;
            }
            else
            {
                int gbSize = GenericRegionDecoder.StatsSizeFor(p.SdTemplate);
                gbStats = new byte[gbSize];
                if (p.UseRetainedContext && p.SeedGbStats != null && p.SeedGbStats.Length == gbSize)
                    Buffer.BlockCopy(p.SeedGbStats, 0, gbStats, 0, gbSize);
            }
            var genericDecoder = p.SdRefAgg ? null : new GenericRegionDecoder();

            // Refagg gear, allocated lazily so non-refagg decoders pay no cost.
            IntegerDecoder? iaai = null, iardx = null, iardy = null;
            IaidDecoder? iaid = null;
            byte[]? grStats = null;
            RefinementRegionDecoder? refDecoder = null;
            TextRegionContexts? textContexts = null;
            var sbSymCodeLen = 0;
            if (p.SdRefAgg)
            {
                iaai = new IntegerDecoder(mq, "IAAI");
                iardx = new IntegerDecoder(mq, "IARDX");
                iardy = new IntegerDecoder(mq, "IARDY");
                int grSize = RefinementRegionDecoder.StatsSizeFor(p.SdRTemplate);
                grStats = new byte[grSize];
                if (p.UseRetainedContext && p.SeedGrStats != null && p.SeedGrStats.Length == grSize)
                    Buffer.BlockCopy(p.SeedGrStats, 0, grStats, 0, grSize);
                refDecoder = new RefinementRegionDecoder();
                // SBSYMCODELEN may be 0 when only one symbol is referenceable;
                // IaidDecoder handles that case by decoding zero bits (returns 0).
                sbSymCodeLen = CeilLog2((int)(p.SdNumInSyms + p.SdNumNewSyms));
                iaid = new IaidDecoder(mq, sbSymCodeLen);

                // Multi-instance refagg (T.88 §6.5.8.2.4) reuses a SD-wide context
                // bundle across many inner-text-region calls within one SD decode.
                // Allocate it eagerly to mirror jbig2dec's tparams.* setup.
                textContexts = new TextRegionContexts
                {
                    Iadt = new IntegerDecoder(mq, "IADT"),
                    Iafs = new IntegerDecoder(mq, "IAFS"),
                    Iads = new IntegerDecoder(mq, "IADS"),
                    Iait = new IntegerDecoder(mq, "IAIT"),
                    Iari = new IntegerDecoder(mq, "IARI"),
                    Iardw = new IntegerDecoder(mq, "IARDW"),
                    Iardh = new IntegerDecoder(mq, "IARDH"),
                    Iardx = iardx,
                    Iardy = iardy,
                    Iaid = iaid,
                    RefDecoder = refDecoder,
                    GrStats = grStats,
                };
            }

            var newSyms = new List<Bitmap>((int)p.SdNumNewSyms);
            var hcheight = 0;

            while (newSyms.Count < (int)p.SdNumNewSyms)
            {
                if (!iadh.Decode(out int hcdh))
                    throw new InvalidOperationException("OOB decoding height-class delta");
                hcheight += hcdh;
                if (hcheight < 0)
                    throw new InvalidOperationException("Invalid (negative) height-class height");

                var symwidth = 0;
                while (true)
                {
                    if (!iadw.Decode(out int dw))
                        break; // OOB ends this height class
                    symwidth += dw;
                    if (symwidth < 0)
                        throw new InvalidOperationException("Invalid (negative) symbol width");
                    if (newSyms.Count >= (int)p.SdNumNewSyms)
                        break;

                    Bitmap glyph;
                    if (p.SdRefAgg)
                    {
                        glyph = DecodeArithmeticRefAggSymbol(
                            p, mq, iaai!, iaid!, iardx!, iardy!, refDecoder!, grStats!,
                            textContexts!, symwidth, hcheight, newSyms);
                    }
                    else
                    {
                        glyph = new Bitmap(symwidth, hcheight);
                        var rp = new GenericRegionParams
                        {
                            GbTemplate = p.SdTemplate,
                            TpgdOn = false,
                            UseSkip = false,
                            Gbat = p.Sdat,
                        };
                        genericDecoder!.Decode(rp, mq, gbStats, glyph);
                    }
                    newSyms.Add(glyph);
                }
            }

            // 6.5.10 — export filter using IAEX. Alternates skip / include runs.
            var exSyms = new List<Bitmap>((int)p.SdNumExSyms);
            uint limit = p.SdNumInSyms + p.SdNumNewSyms;
            uint i = 0;
            var exflag = false;
            var emptyRuns = 0;
            while (i < limit)
            {
                if (!iaex.Decode(out int runLen))
                    throw new InvalidOperationException("OOB decoding export run length");
                if (runLen <= 0)
                {
                    if (++emptyRuns >= 1000)
                        throw new InvalidOperationException("Empty-run loop in export table");
                }
                else
                {
                    emptyRuns = 0;
                }

                var takeRun = (uint)runLen;
                if (takeRun > limit - i) takeRun = limit - i;

                for (uint k = 0; k < takeRun; k++)
                {
                    if (exflag)
                    {
                        Bitmap glyph = i < p.SdNumInSyms
                            ? p.SdInSyms!.Glyphs[(int)i]
                            : newSyms[(int)(i - p.SdNumInSyms)];
                        exSyms.Add(glyph);
                    }
                    i++;
                }
                exflag = !exflag;
            }

            var result = new SymbolDictionary(exSyms.ToArray());
            if (p.RetainContext)
            {
                // Snapshot the final arith stats so a later SD with bit 8 set
                // (T.88 §7.4.2.1.1) can seed from them. Cloning so subsequent
                // mutations on our local arrays don't leak.
                if (gbStats != null)
                {
                    var copy = new byte[gbStats.Length];
                    Buffer.BlockCopy(gbStats, 0, copy, 0, gbStats.Length);
                    result.RetainedGbStats = copy;
                }
                if (grStats != null)
                {
                    var copy = new byte[grStats.Length];
                    Buffer.BlockCopy(grStats, 0, copy, 0, grStats.Length);
                    result.RetainedGrStats = copy;
                }
            }
            return result;
        }

        private static int CeilLog2(int n)
        {
            if (n <= 1) return 0;
            var r = 0; int v = n - 1;
            while (v > 0) { r++; v >>= 1; }
            return r;
        }

        // T.88 §6.5.8.2 — refinement/aggregate symbol decoding (arithmetic).
        // REFAGGNINST==1 takes the single-glyph refinement fast path
        // (§6.5.8.2.2): decode IAID/IARDX/IARDY then a refinement-region pass.
        // REFAGGNINST>1 (§6.5.8.2.4) recurses into a text-region decode using the
        // SD's pre-allocated context bundle, mirroring jbig2dec's
        // jbig2_decode_text_region call with tparams shared across SD invocations.
        private static Bitmap DecodeArithmeticRefAggSymbol(
            SymbolDictionaryParams p, MqDecoder mq,
            IntegerDecoder iaai, IaidDecoder iaid, IntegerDecoder iardx, IntegerDecoder iardy,
            RefinementRegionDecoder refDecoder, byte[] grStats,
            TextRegionContexts textContexts,
            int symwidth, int hcheight, List<Bitmap> newSyms)
        {
            if (!iaai.Decode(out int refaggninst))
                throw new InvalidOperationException("OOB decoding REFAGGNINST");
            if (refaggninst <= 0)
                throw new InvalidOperationException($"Invalid REFAGGNINST {refaggninst}");

            if (refaggninst == 1)
            {
                int id = iaid.Decode();
                if (!iardx.Decode(out int rdx))
                    throw new InvalidOperationException("OOB decoding IARDX");
                if (!iardy.Decode(out int rdy))
                    throw new InvalidOperationException("OOB decoding IARDY");

                var ninsyms = (int)p.SdNumInSyms;
                int totalIds = ninsyms + newSyms.Count;
                if (id < 0 || id >= totalIds)
                    throw new InvalidOperationException($"Refinement references unknown symbol id={id} (have {totalIds})");

                Bitmap reference = id < ninsyms
                    ? p.SdInSyms!.Glyphs[id]
                    : newSyms[id - ninsyms];

                var glyph = new Bitmap(symwidth, hcheight);
                var rp = new RefinementRegionParams
                {
                    GrTemplate = p.SdRTemplate,
                    TpgrOn = false,
                    Reference = reference,
                    ReferenceDx = rdx,
                    ReferenceDy = rdy,
                    Grat = p.Sdrat,
                };
                refDecoder.Decode(rp, mq, grStats, glyph);
                return glyph;
            }

            // §6.5.8.2.4 — multi-instance refagg via inner text region.
            // The text region references the SD's input symbols (SDINSYMS) plus
            // the symbols we've already decoded in this SD (SDNEWSYMS). We hand
            // the text region a snapshot of the latter via a fresh
            // SymbolDictionary; jbig2dec uses an SDNEWSYMS that grows in place
            // but the count is bounded by NSYMSDECODED at this point in the loop,
            // so a snapshot has the same observable contents.
            var inDict = p.SdInSyms ?? new SymbolDictionary(Array.Empty<Bitmap>());
            var newDict = new SymbolDictionary(newSyms.ToArray());

            // §6.5.8.2.4 fixes most TextRegionParams to spec defaults (Table 17).
            var trp = new TextRegionParams
            {
                SbHuff = false,
                SbRefine = true,
                SbDefPixel = false,
                SbCombOp = 0,                 // OR
                Transposed = false,
                RefCorner = RefCorner.TopLeft,
                SbDsOffset = 0,
                SbNumInstances = (uint)refaggninst,
                LogSbStrips = 0,
                SbStrips = 1,
                SbRTemplate = p.SdRTemplate == 1,
                Sbrat = p.Sdrat,
                Dicts = new[] { inDict, newDict },
            };

            var glyphImage = new Bitmap(symwidth, hcheight);
            new TextRegionDecoder().DecodeArithmetic(trp, mq, textContexts, glyphImage);
            return glyphImage;
        }

        // T.88 §7.4.2.1.1 selectors → standard or user-defined tables. Slot
        // ordering for the userTables array is fixed by spec §7.4.2.1.6:
        // 0=Dh, 1=Dw, 2=BmSize, 3=AggInst.
        private static HuffmanParams TableForDh(int sel, HuffmanParams?[]? ut) => sel switch
        {
            0 => StandardHuffmanTables.D,
            1 => StandardHuffmanTables.E,
            3 => UserTable(ut, 0, "SDHUFFDH"),
            _ => throw new NotSupportedException($"reserved SDHUFFDH selector {sel}"),
        };
        private static HuffmanParams TableForDw(int sel, HuffmanParams?[]? ut) => sel switch
        {
            0 => StandardHuffmanTables.B,
            1 => StandardHuffmanTables.C,
            3 => UserTable(ut, 1, "SDHUFFDW"),
            _ => throw new NotSupportedException($"reserved SDHUFFDW selector {sel}"),
        };
        private static HuffmanParams TableForBmSize(int sel, HuffmanParams?[]? ut) => sel switch
        {
            0 => StandardHuffmanTables.A,
            1 => UserTable(ut, 2, "SDHUFFBMSIZE"),
            _ => throw new NotSupportedException($"reserved SDHUFFBMSIZE selector {sel}"),
        };
        private static HuffmanParams UserTable(HuffmanParams?[]? ut, int slot, string what)
        {
            if (ut == null || slot >= ut.Length || ut[slot] == null)
                throw new InvalidOperationException(
                    $"{what} marked user-defined but no user Huffman table supplied at slot {slot}");
            return ut[slot]!;
        }

        private SymbolDictionary DecodeHuffmanNoRefAgg(SymbolDictionaryParams p, byte[] data, int offset, int length)
        {
            int selDh = (p.SdHuffFlags >> 2) & 3;
            int selDw = (p.SdHuffFlags >> 4) & 3;
            int selBmSize = (p.SdHuffFlags >> 6) & 1;
            // SDHUFFAGGINST (bit 7) is unused when !SdRefAgg.

            var hDh = new HuffmanTable(TableForDh(selDh, p.UserTables));
            var hDw = new HuffmanTable(TableForDw(selDw, p.UserTables));
            var hBmSize = new HuffmanTable(TableForBmSize(selBmSize, p.UserTables));
            // Export run lengths: T.88 §6.5.10 — table B.1.
            var hExSize = new HuffmanTable(StandardHuffmanTables.A);

            var r = new HuffmanBitReader(data, offset, length);

            var newSyms = new List<Bitmap>((int)p.SdNumNewSyms);
            // Per-symbol widths recorded during the height-class width loop.
            // Indexed by NSYMSDECODED at insertion time.
            var newSymWidths = new int[(int)p.SdNumNewSyms];

            var hcheight = 0;
            var nsymsdecoded = 0;

            while (nsymsdecoded < (int)p.SdNumNewSyms)
            {
                if (!hDh.Decode(r, out int hcdh))
                    throw new InvalidOperationException("OOB decoding height-class delta (Huffman)");
                hcheight += hcdh;
                if (hcheight < 0)
                    throw new InvalidOperationException("Invalid (negative) height-class height");

                int hcfirstsym = nsymsdecoded;
                var symwidth = 0;
                var totwidth = 0;

                while (true)
                {
                    if (!hDw.Decode(r, out int dw))
                        break; // OOB ends the height class
                    if (nsymsdecoded >= (int)p.SdNumNewSyms)
                        break; // jbig2dec defends against missing OOB at end
                    symwidth += dw;
                    if (symwidth < 0)
                        throw new InvalidOperationException("Negative SYMWIDTH in symbol dictionary");
                    totwidth += symwidth;
                    newSymWidths[nsymsdecoded] = symwidth;
                    nsymsdecoded++;
                }

                if (!hBmSize.Decode(r, out int bmsize))
                    throw new InvalidOperationException("OOB decoding collective bitmap size");

                r.AlignToByte();

                var collective = new Bitmap(totwidth, hcheight);

                if (bmsize == 0)
                {
                    // Uncompressed: rows of (totwidth+7)/8 bytes laid out back-to-back.
                    int stride = collective.Stride;
                    int needed = stride * hcheight;
                    if (r.Offset + needed > offset + length)
                        throw new InvalidOperationException("Truncated uncompressed collective bitmap");
                    Buffer.BlockCopy(data, r.Offset, collective.Data, 0, needed);
                    r.Advance(needed);
                }
                else
                {
                    // MMR collective bitmap (T.88 §6.2.6) — Group 4, no per-row EOLs,
                    // EOFB at the end. Decode via the shared CCITT decoder from the
                    // PDF project's ImageLibrary.
                    var slice = new byte[bmsize];
                    Buffer.BlockCopy(data, r.Offset, slice, 0, bmsize);
                    var mmr = new CcittDecoder(new CcittOptions
                    {
                        Group = CcittGroup.Group4,
                        K = -1,
                        Width = totwidth,
                        Height = hcheight,
                        BlackIs1 = true,
                        EndOfBlock = true,
                    });
                    var decoded = mmr.Decode(slice);
                    int expected = collective.Data.Length;
                    if (decoded.Length < expected)
                        throw new InvalidOperationException(
                            $"MMR decoder produced {decoded.Length} bytes for collective bitmap (totwidth={totwidth}, hcheight={hcheight}, bmsize={bmsize}), expected {expected}");
                    Buffer.BlockCopy(decoded, 0, collective.Data, 0, expected);
                    r.Advance(bmsize);
                }

                // Slice the collective bitmap into individual glyphs by column.
                var xCursor = 0;
                for (int j = hcfirstsym; j < nsymsdecoded; j++)
                {
                    int gw = newSymWidths[j];
                    var glyph = new Bitmap(gw, hcheight);
                    for (var y = 0; y < hcheight; y++)
                        for (var x = 0; x < gw; x++)
                            glyph.SetPixel(x, y, collective.GetPixel(xCursor + x, y));
                    newSyms.Add(glyph);
                    xCursor += gw;
                }
            }

            // 6.5.10 — export filter, identical structure to the arithmetic
            // path but run lengths come from a Huffman table (B.1).
            var exSyms = new List<Bitmap>((int)p.SdNumExSyms);
            uint limit = p.SdNumInSyms + p.SdNumNewSyms;
            uint i2 = 0;
            var exflag = false;
            var emptyRuns = 0;
            while (i2 < limit)
            {
                if (!hExSize.Decode(r, out int runLen))
                    throw new InvalidOperationException("OOB decoding export run length (Huffman)");
                if (runLen <= 0)
                {
                    if (++emptyRuns >= 1000)
                        throw new InvalidOperationException("Empty-run loop in export table");
                }
                else
                {
                    emptyRuns = 0;
                }

                var takeRun = (uint)runLen;
                if (takeRun > limit - i2) takeRun = limit - i2;

                for (uint k = 0; k < takeRun; k++)
                {
                    if (exflag)
                    {
                        Bitmap glyph = i2 < p.SdNumInSyms
                            ? p.SdInSyms!.Glyphs[(int)i2]
                            : newSyms[(int)(i2 - p.SdNumInSyms)];
                        exSyms.Add(glyph);
                    }
                    i2++;
                }
                exflag = !exflag;
            }

            return new SymbolDictionary(exSyms.ToArray());
        }

        // T.88 §6.5.5 + §6.5.8.2 Huffman-mode SD with SDREFAGG=1.
        //
        // Each new symbol is built by either:
        //   (a) REFAGGNINST = 1: a single-instance refinement of one referenced
        //       symbol (T.88 §6.5.8.2.2). ID/RDX/RDY/BMSIZE are Huffman-coded
        //       in the SD bit stream; the actual refinement bitmap is
        //       arith-coded as a generic refinement region in a BMSIZE-byte
        //       slice that immediately follows.
        //   (b) REFAGGNINST > 1: an inner Huffman text region (T.88
        //       §6.5.8.2.4) that consumes more bits from the same SD bit
        //       reader. The inner text region's selectors are fixed by spec
        //       (F/H/K/O/O/O/O/A) and its referenced dictionary list is
        //       [SDINSYMS, new symbols decoded so far].
        private SymbolDictionary DecodeHuffmanRefAgg(
            SymbolDictionaryParams p, byte[] data, int offset, int length)
        {
            int selDh = (p.SdHuffFlags >> 2) & 3;
            int selDw = (p.SdHuffFlags >> 4) & 3;
            int selBmSize = (p.SdHuffFlags >> 6) & 1;
            int selAggInst = (p.SdHuffFlags >> 7) & 1;

            var hDh = new HuffmanTable(TableForDh(selDh, p.UserTables));
            var hDw = new HuffmanTable(TableForDw(selDw, p.UserTables));
            var hBmSize = new HuffmanTable(TableForBmSize(selBmSize, p.UserTables));
            var hAggInst = new HuffmanTable(selAggInst == 0
                ? StandardHuffmanTables.A
                : UserTable(p.UserTables, 3, "SDHUFFAGGINST"));
            var hExSize = new HuffmanTable(StandardHuffmanTables.A);

            // For refinement subfields (single-instance path) and for the
            // inner-text-region selectors (multi-instance path) we always use
            // the spec-defined standard tables — these aren't user-overridable.
            var hRdx   = new HuffmanTable(StandardHuffmanTables.O);  // B.15
            var hRdy   = new HuffmanTable(StandardHuffmanTables.O);  // B.15
            var hRsize = new HuffmanTable(StandardHuffmanTables.A);  // B.1

            var r = new HuffmanBitReader(data, offset, length);
            int dataEnd = offset + length;

            int sbSymCodeLen = CeilLog2((int)(p.SdNumInSyms + p.SdNumNewSyms));
            var ninsyms = (int)p.SdNumInSyms;

            // Per pdfium's SDDProc: the GR (refinement) arith stats array is
            // allocated ONCE and reused across every per-symbol refinement
            // (single-instance via DecodeSingleInstance and multi-instance via
            // the inner text region's per-instance refinement). Encoders rely
            // on this — the stats accumulate context-probability information
            // that persists from one symbol's decode into the next.
            var grStatsShared = new byte[RefinementRegionDecoder.StatsSizeFor(p.SdRTemplate)];

            var newSyms = new List<Bitmap>((int)p.SdNumNewSyms);
            var hcheight = 0;
            var nsymsdecoded = 0;

            while (nsymsdecoded < (int)p.SdNumNewSyms)
            {
                if (!hDh.Decode(r, out int hcdh))
                    throw new InvalidOperationException("OOB decoding height-class delta (Huffman+refagg)");
                hcheight += hcdh;
                if (hcheight < 0)
                    throw new InvalidOperationException("Invalid (negative) height-class height");

                var symwidth = 0;

                while (true)
                {
                    if (!hDw.Decode(r, out int dw))
                        break; // OOB ends the height class
                    if (nsymsdecoded >= (int)p.SdNumNewSyms)
                        break;
                    symwidth += dw;
                    if (symwidth < 0)
                        throw new InvalidOperationException("Negative SYMWIDTH in Huffman+refagg SD");

                    if (!hAggInst.Decode(r, out int refaggninst))
                        throw new InvalidOperationException("OOB decoding REFAGGNINST (Huffman)");
                    if (refaggninst <= 0)
                        throw new InvalidOperationException(
                            $"Invalid REFAGGNINST {refaggninst} (must be positive)");

                    Bitmap glyph;
                    if (refaggninst > 1)
                    {
                        glyph = DecodeMultiInstanceRefAggHuffman(
                            p, data, dataEnd, r, sbSymCodeLen,
                            symwidth, hcheight, refaggninst, newSyms,
                            grStatsShared);
                    }
                    else
                    {
                        glyph = DecodeSingleInstanceRefAggHuffman(
                            p, data, dataEnd, r, sbSymCodeLen,
                            symwidth, hcheight,
                            hRdx, hRdy, hRsize,
                            ninsyms, newSyms, grStatsShared);
                    }

                    newSyms.Add(glyph);
                    nsymsdecoded++;
                }
            }

            // 6.5.10 — export filter, identical to the non-refagg Huffman path.
            var exSyms = new List<Bitmap>((int)p.SdNumExSyms);
            uint limit = p.SdNumInSyms + p.SdNumNewSyms;
            uint i2 = 0;
            var exflag = false;
            var emptyRuns = 0;
            while (i2 < limit)
            {
                if (!hExSize.Decode(r, out int runLen))
                    throw new InvalidOperationException("OOB decoding export run length (Huffman+refagg)");
                if (runLen <= 0)
                {
                    if (++emptyRuns >= 1000)
                        throw new InvalidOperationException("Empty-run loop in export table");
                }
                else
                {
                    emptyRuns = 0;
                }
                var takeRun = (uint)runLen;
                if (takeRun > limit - i2) takeRun = limit - i2;
                for (uint k = 0; k < takeRun; k++)
                {
                    if (exflag)
                    {
                        Bitmap glyph = i2 < p.SdNumInSyms
                            ? p.SdInSyms!.Glyphs[(int)i2]
                            : newSyms[(int)(i2 - p.SdNumInSyms)];
                        exSyms.Add(glyph);
                    }
                    i2++;
                }
                exflag = !exflag;
            }

            return new SymbolDictionary(exSyms.ToArray());
        }

        private Bitmap DecodeSingleInstanceRefAggHuffman(
            SymbolDictionaryParams p, byte[] data, int dataEnd,
            HuffmanBitReader r, int sbSymCodeLen,
            int symwidth, int hcheight,
            HuffmanTable hRdx, HuffmanTable hRdy, HuffmanTable hRsize,
            int ninsyms, List<Bitmap> newSyms,
            byte[] grStats)
        {
            // T.88 §6.5.8.2.2: ID, RDX, RDY, BMSIZE, then byte-align, then
            // arith-coded refinement bitmap.
            var id = (int)r.ReadBits(sbSymCodeLen);
            if (!hRdx.Decode(r, out int rdx))
                throw new InvalidOperationException("OOB decoding RDX in SD-single-refagg-Huffman");
            if (!hRdy.Decode(r, out int rdy))
                throw new InvalidOperationException("OOB decoding RDY in SD-single-refagg-Huffman");
            if (!hRsize.Decode(r, out int bmsize))
                throw new InvalidOperationException("OOB decoding BMSIZE in SD-single-refagg-Huffman");
            r.AlignToByte();

            if (id < 0 || id >= ninsyms + newSyms.Count)
                throw new InvalidOperationException(
                    $"Refinement references unknown symbol {id} (have {ninsyms + newSyms.Count})");

            Bitmap reference = id < ninsyms
                ? p.SdInSyms!.Glyphs[id]
                : newSyms[id - ninsyms];

            var glyph = new Bitmap(symwidth, hcheight);

            // BMSIZE = 0 means "use stride*height" (jbig2dec convention to round
            // up to the byte boundary based on the resulting glyph size).
            int sliceLen = bmsize > 0 ? bmsize : glyph.Stride * glyph.Height;
            if (r.Offset + sliceLen > dataEnd)
                sliceLen = dataEnd - r.Offset;
            if (sliceLen < 0) sliceLen = 0;

            var rmq = new MqDecoder(data, r.Offset, sliceLen);
            var rp = new RefinementRegionParams
            {
                GrTemplate = p.SdRTemplate,
                TpgrOn = false,
                Reference = reference,
                ReferenceDx = rdx,
                ReferenceDy = rdy,
                Grat = p.Sdrat,
            };
            new RefinementRegionDecoder().Decode(rp, rmq, grStats, glyph);

            r.Advance(sliceLen);
            return glyph;
        }

        private Bitmap DecodeMultiInstanceRefAggHuffman(
            SymbolDictionaryParams p, byte[] data, int dataEnd,
            HuffmanBitReader r, int sbSymCodeLen,
            int symwidth, int hcheight, int refaggninst,
            List<Bitmap> newSyms, byte[] grStats)
        {
            // T.88 §6.5.8.2.4: invoke an inner Huffman text region with a fixed
            // selector preset (F/H/K/O/O/O/O/A). The reference dictionary list
            // is [SDINSYMS, new symbols decoded so far]; the bit reader is the
            // outer SD reader so the bit stream continues seamlessly.
            var refDicts = new List<SymbolDictionary>();
            if (p.SdInSyms != null && p.SdInSyms.Count > 0)
                refDicts.Add(p.SdInSyms);
            if (newSyms.Count > 0)
                refDicts.Add(new SymbolDictionary(newSyms.ToArray()));

            // Pdfium-compatible trivial symbol-ID table: every symbol gets the
            // same code length (sbSymCodeLen), so decoding is equivalent to
            // reading sbSymCodeLen raw bits. This skips the 35-runcode prelude
            // that standalone text regions read at start — encoders for SD
            // multi-instance refagg don't emit that prelude here.
            var sbNumSyms = 0;
            foreach (var d in refDicts) sbNumSyms += d.Count;
            var trivialLines = new HuffmanLine[sbNumSyms];
            for (var i = 0; i < sbNumSyms; i++)
                trivialLines[i] = new HuffmanLine(sbSymCodeLen, 0, i);
            var trivialSbSymCodes = new HuffmanParams { HtOob = false, Lines = trivialLines };

            var trp = new TextRegionParams
            {
                SbHuff = true,
                SbRefine = true,
                SbDefPixel = false,
                SbCombOp = 0,                  // OR
                Transposed = false,
                RefCorner = RefCorner.TopLeft,
                SbDsOffset = 0,
                SbNumInstances = (uint)refaggninst,
                LogSbStrips = 0,
                SbStrips = 1,
                SbRTemplate = p.SdRTemplate == 1,
                Sbrat = p.Sdrat,
                Dicts = refDicts.ToArray(),
                // Selectors per spec preset:
                //   selFs=0(F), selDs=0(H), selDt=0(K),
                //   selRdw=1(O), selRdh=1(O), selRdx=1(O), selRdy=1(O),
                //   selRsz=0(A) — packed into the 16-bit word.
                SbHuffFlags = 0x1540,
                UserTables = null,
                PrebuiltSbSymCodes = trivialSbSymCodes,
                SharedGrStats = grStats,
            };

            var glyph = new Bitmap(symwidth, hcheight);
            new TextRegionDecoder().DecodeHuffmanWithReader(trp, data, dataEnd, r, glyph);
            return glyph;
        }
    }
}
