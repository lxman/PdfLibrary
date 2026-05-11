using System;
using System.Collections.Generic;
using CcittCodec;
using Jbig2Decoder.Image;
using Jbig2Decoder.Mq;
using Jbig2Decoder.Region;

namespace Jbig2Decoder.Stream
{
    /// <summary>
    /// Walks a JBIG2 bitstream — either a standalone file (starts with the 8-byte
    /// "97 4A 42 32 0D 0A 1A 0A" magic, T.88 §7.1) or embedded data (no magic,
    /// e.g. from a PDF /JBIG2Decode filter where the wrapper supplies file header
    /// info externally). Walks segments in sequential order, dispatching each to
    /// its handler, and assembles the rendered page bitmap.
    ///
    /// Currently supported segments: page information (48), immediate generic
    /// region + lossless variant (38, 39), end of page (49), end of file (51),
    /// extension (62, ignored). Unknown / unsupported types are skipped over by
    /// their declared <c>data_length</c>.
    /// </summary>
    internal sealed class Jbig2StreamReader
    {
        private static readonly byte[] Magic =
            { 0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A };

        private byte[] _data;
        private int _pos;

        // Optional /JBIG2Globals stream supplied by a PDF wrapper. Globals are
        // parsed first as embedded sequential segments so their results land in
        // _segmentResults before any main-stream segment can reference them.
        private readonly byte[]? _globals;

        // Sequential file organisation; random-access is rejected for now.
        public bool IsSequential { get; private set; } = true;

        // Page bitmap currently being assembled (only one supported in this pass).
        public Bitmap? Page { get; private set; }

        // Striped-page state (T.88 §7.4.8 / 7.4.9). When the page info segment
        // declares an unknown height (0xFFFFFFFF), the page is allocated with
        // <c>_maxStripeSize</c> rows initially and grown as composites land
        // outside the current bounds; the final page is trimmed at decode end.
        private bool _isStriped;
        private int _maxStripeSize;
        private int _actualHeight;
        private int _pageDefaultPixel;

        // Segment results keyed by segment number, for downstream segments to reference.
        private readonly Dictionary<uint, object> _segmentResults = new Dictionary<uint, object>();

        public Jbig2StreamReader(byte[] data, byte[]? globals = null)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _globals = globals;
        }

        public Bitmap Decode()
        {
            // PDF /JBIG2Globals: a header-less segment list that defines globally
            // shared symbol/pattern dictionaries. Parse before the main stream so
            // its segment-result entries are visible to main-stream lookups.
            if (_globals != null && _globals.Length > 0)
            {
                byte[] mainData = _data;
                int mainPos = _pos;
                bool mainSeq = IsSequential;

                _data = _globals;
                _pos = 0;
                IsSequential = true;
                DecodeSequential();

                _data = mainData;
                _pos = mainPos;
                IsSequential = mainSeq;
            }

            ReadFileHeader();

            if (IsSequential)
                DecodeSequential();
            else
                DecodeRandomAccess();

            if (Page is null)
                throw new InvalidOperationException("Stream contained no page information segment");

            // Trim a striped page down to its actual rendered height.
            if (_isStriped && _actualHeight > 0 && _actualHeight != Page.Height)
                Page = TrimPage(Page, _actualHeight);

            return Page;
        }

        private static Bitmap TrimPage(Bitmap src, int newHeight)
        {
            if (newHeight >= src.Height) return src;
            var trimmed = new Bitmap(src.Width, newHeight);
            int copyBytes = src.Stride * newHeight;
            Buffer.BlockCopy(src.Data, 0, trimmed.Data, 0, copyBytes);
            return trimmed;
        }

        // Grow the page bitmap so it has at least <paramref name="needed"/> rows.
        // Doubles the allocation each time to amortise cost, fills new rows with
        // the page's default pixel.
        private void EnsurePageHeight(int needed)
        {
            if (Page is null || !_isStriped) return;
            if (needed <= Page.Height) return;
            int newH = Math.Max(needed, Page.Height * 2);
            var grown = new Bitmap(Page.Width, newH);
            Buffer.BlockCopy(Page.Data, 0, grown.Data, 0, Page.Data.Length);
            if (_pageDefaultPixel == 1)
            {
                int from = Page.Data.Length;
                int to = grown.Data.Length;
                for (int i = from; i < to; i++) grown.Data[i] = 0xFF;
            }
            Page = grown;
        }

        private void DecodeSequential()
        {
            while (_pos < _data.Length)
            {
                var hdr = SegmentParser.Parse(_data, _pos);
                _pos += hdr.HeaderLengthBytes;

                if (hdr.DataLengthDeferred)
                    ResolveDeferredDataLength(hdr, _pos);

                int bodyStart = _pos;
                var bodyLen = (int)hdr.DataLength;
                if (bodyStart + bodyLen > _data.Length)
                    throw new InvalidOperationException(
                        $"Segment {hdr.Number} ({hdr.Type}) declares {bodyLen} body bytes but only {_data.Length - bodyStart} remain");

                Dispatch(hdr, bodyStart, bodyLen);
                _pos = bodyStart + bodyLen;

                if (hdr.Type == SegmentType.EndOfFile)
                    break;
            }
        }

        // T.88 §7.2.7 — when a segment header declares data_length = 0xFFFFFFFF,
        // the actual body length isn't known until the body is parsed. The spec
        // restricts deferred lengths to ImmediateGenericRegion (type 38) and
        // determines the boundary by scanning for a 2-byte marker followed by a
        // 4-byte row count — 0xFFAC for arithmetic-coded data, 0x0000 for MMR.
        // The flag bit driving that choice is bit 0 of the segment data flags
        // byte at offset 17 of the body (right after the 17-byte region segment
        // info structure).
        private void ResolveDeferredDataLength(SegmentHeader hdr, int bodyStart)
        {
            if (hdr.Type != SegmentType.ImmediateGenericRegion)
                throw new NotSupportedException(
                    $"Deferred data length is only supported for ImmediateGenericRegion (got {hdr.Type})");

            // Region segment info (17 bytes) + segment data flags (1 byte) = 18.
            const int prelude = 18;
            if (bodyStart + prelude + 2 + 4 > _data.Length)
                throw new InvalidOperationException(
                    "Deferred-length generic region body too short to contain its end-of-data marker");

            bool mmr = (_data[bodyStart + 17] & 0x01) != 0;
            byte m0 = mmr ? (byte)0x00 : (byte)0xFF;
            byte m1 = mmr ? (byte)0x00 : (byte)0xAC;

            int p = bodyStart + prelude;
            int end = _data.Length - 1;
            while (p < end)
            {
                if (_data[p] == m0 && _data[p + 1] == m1) break;
                p++;
            }
            if (p >= end)
                throw new InvalidOperationException(
                    $"Deferred-length generic region: end-of-data marker not found in {_data.Length - bodyStart} bytes");

            // Marker (2 bytes) + row count (4 bytes) = 6 bytes consumed past p.
            if (p + 6 > _data.Length)
                throw new InvalidOperationException(
                    "Deferred-length generic region: end-of-data marker has no following row count");

            hdr.DataLength = (uint)(p + 6 - bodyStart);
        }

        // Random-access organisation (T.88 §7.1):
        //  - All segment headers come first, terminated by an EOF (type 51) header.
        //  - Bodies follow immediately after the EOF header, in the same order as
        //    the headers, sized by each header's declared data_length.
        private void DecodeRandomAccess()
        {
            var headers = new List<SegmentHeader>();
            while (_pos < _data.Length)
            {
                var hdr = SegmentParser.Parse(_data, _pos);
                _pos += hdr.HeaderLengthBytes;
                headers.Add(hdr);
                if (hdr.Type == SegmentType.EndOfFile) break;
            }

            int bodyCursor = _pos;
            foreach (var hdr in headers)
            {
                if (hdr.DataLengthDeferred)
                    ResolveDeferredDataLength(hdr, bodyCursor);

                var bodyLen = (int)hdr.DataLength;
                if (bodyCursor + bodyLen > _data.Length)
                    throw new InvalidOperationException(
                        $"Segment {hdr.Number} ({hdr.Type}) declares {bodyLen} body bytes but only {_data.Length - bodyCursor} remain");

                Dispatch(hdr, bodyCursor, bodyLen);
                bodyCursor += bodyLen;

                if (hdr.Type == SegmentType.EndOfFile) break;
            }
        }

        private void ReadFileHeader()
        {
            if (_data.Length < 9)
                throw new InvalidOperationException("Input too short for a JBIG2 file header");

            // Detect standalone vs embedded by the magic.
            var hasMagic = true;
            for (var i = 0; i < 8; i++)
                if (_data[i] != Magic[i]) { hasMagic = false; break; }

            if (!hasMagic)
            {
                // Embedded mode: no file header. Caller arrangement (e.g. PDF wrapper)
                // is responsible for choosing the segment organization upstream;
                // assume sequential.
                IsSequential = true;
                _pos = 0;
                return;
            }

            byte flags = _data[8];
            IsSequential = (flags & 0x01) != 0;
            bool unknownPageCount = (flags & 0x02) != 0;
            _pos = 9;
            if (!unknownPageCount)
                _pos += 4; // skip the four-byte page count

        }

        private void Dispatch(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            switch (hdr.Type)
            {
                case SegmentType.PageInformation:
                    HandlePageInformation(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.IntermediateGenericRegion:
                case SegmentType.ImmediateGenericRegion:
                case SegmentType.ImmediateLosslessGenericRegion:
                    HandleImmediateGenericRegion(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.SymbolDictionary:
                    HandleSymbolDictionary(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.IntermediateTextRegion:
                case SegmentType.ImmediateTextRegion:
                case SegmentType.ImmediateLosslessTextRegion:
                    HandleTextRegion(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.IntermediateGenericRefinementRegion:
                case SegmentType.ImmediateGenericRefinementRegion:
                case SegmentType.ImmediateLosslessGenericRefinementRegion:
                    HandleRefinementRegion(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.PatternDictionary:
                    HandlePatternDictionary(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.IntermediateHalftoneRegion:
                case SegmentType.ImmediateHalftoneRegion:
                case SegmentType.ImmediateLosslessHalftoneRegion:
                    HandleHalftoneRegion(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.EndOfStripe:
                    HandleEndOfStripe(hdr, bodyStart, bodyLen);
                    break;

                case SegmentType.Tables:
                    _segmentResults[hdr.Number] =
                        Huffman.HuffmanTableSegment.Parse(_data, bodyStart, bodyLen);
                    break;

                case SegmentType.EndOfPage:
                case SegmentType.EndOfFile:
                case SegmentType.Extension:
                    // No-ops at this level.
                    break;

                default:
                    // Unsupported types are skipped for now — caller's segment scan
                    // continues using the declared body length.
                    break;
            }
        }

        // T.88 §7.4.8 Page information segment — width, height, resolution, flags, striping.
        private void HandlePageInformation(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (bodyLen < 19)
                throw new InvalidOperationException("Page information segment is too short");

            uint width  = BigEndian.U32(_data, bodyStart);
            uint height = BigEndian.U32(_data, bodyStart + 4);
            // x_res(4), y_res(4), flags(1), striping(2) follow.

            if (width > int.MaxValue)
                throw new InvalidOperationException("Page width exceeds addressable range");

            byte pageFlags = _data[bodyStart + 16];
            // Bit 2: default pixel value (0 = white default, 1 = black default).
            _pageDefaultPixel = (pageFlags & 0x04) >> 2;

            int striping = BigEndian.U16(_data, bodyStart + 17);
            bool stripedFlag = (striping & 0x8000) != 0;
            int maxStripeSize = striping & 0x7FFF;

            int initialHeight;
            if (height == 0xFFFFFFFFu)
            {
                _isStriped = true;
                _actualHeight = 0;
                _maxStripeSize = stripedFlag && maxStripeSize > 0 ? maxStripeSize : 0x7FFF;
                initialHeight = _maxStripeSize;
            }
            else
            {
                _isStriped = false;
                if (height > int.MaxValue)
                    throw new InvalidOperationException("Page dimensions exceed addressable range");
                initialHeight = (int)height;
            }

            Page = new Bitmap((int)width, initialHeight);
            if (_pageDefaultPixel == 1)
                Array.Fill(Page.Data, (byte)0xFF);
        }

        // T.88 §7.4.9 End-of-stripe segment: a single 4-byte big-endian Y-coordinate
        // identifying the row at which the current stripe ends. Used to track the
        // actual page extent for striped pages.
        private void HandleEndOfStripe(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (bodyLen < 4) return;
            var yEnd = (int)BigEndian.U32(_data, bodyStart);
            if (_isStriped)
            {
                // The stripe-ending Y is the row index past the last row of the
                // stripe; clamp upwards to track the actual rendered height.
                int reachedHeight = yEnd + 1;
                if (reachedHeight > _actualHeight) _actualHeight = reachedHeight;
                EnsurePageHeight(reachedHeight);
            }
        }

        // T.88 §7.4.6 Immediate generic region: region info + region flags + AT bytes + arithmetic data.
        private void HandleImmediateGenericRegion(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (Page is null)
                throw new InvalidOperationException("Generic region encountered before any page information segment");
            if (bodyLen < 18)
                throw new InvalidOperationException("Generic region body too short");

            RegionInfo rsi = RegionInfo.Parse(_data, bodyStart);
            byte segFlags = _data[bodyStart + 17];

            bool mmr        = (segFlags & 0x01) != 0;
            int gbTemplate  = (segFlags & 0x06) >> 1;
            bool tpgdOn     = (segFlags & 0x08) != 0;
            // bit 4 = TPGDOFF (mutually exclusive with TPGDON), bit 5 = template-extension flag.

            // MMR generic regions have no AT-pixel field (T.88 §7.4.6.3).
            int gbatBytes = mmr ? 0 : (segFlags & 0x06) != 0 ? 2 : 8;
            int dataOffset = bodyStart + 18 + gbatBytes;
            int arithLen = bodyStart + bodyLen - dataOffset;

            var regionBmp = new Bitmap((int)rsi.Width, (int)rsi.Height);

            if (mmr)
            {
                // Group-4 MMR-coded bitmap (T.88 §6.2.6).
                var slice = new byte[arithLen];
                Buffer.BlockCopy(_data, dataOffset, slice, 0, arithLen);
                var dec = new CcittDecoder(new CcittOptions
                {
                    Group = CcittGroup.Group4,
                    K = -1,
                    Width = (int)rsi.Width,
                    Height = (int)rsi.Height,
                    BlackIs1 = true,
                    EndOfBlock = true,
                });
                byte[] decoded = dec.Decode(slice);
                int needed = regionBmp.Data.Length;
                if (decoded.Length < needed)
                    throw new InvalidOperationException(
                        $"MMR generic region produced {decoded.Length} bytes, expected {needed} ({rsi.Width}x{rsi.Height})");
                Buffer.BlockCopy(decoded, 0, regionBmp.Data, 0, needed);
            }
            else
            {
                var gbat = new sbyte[8];
                for (var i = 0; i < gbatBytes; i++)
                    gbat[i] = (sbyte)_data[bodyStart + 18 + i];

                var p = new GenericRegionParams
                {
                    GbTemplate = gbTemplate,
                    TpgdOn = tpgdOn,
                    UseSkip = false,
                    Gbat = gbat,
                };
                var mq = new MqDecoder(_data, dataOffset, arithLen);
                var stats = new byte[GenericRegionDecoder.StatsSizeFor(gbTemplate)];
                new GenericRegionDecoder().Decode(p, mq, stats, regionBmp);
            }

            // Intermediate variant: don't render; stash the bitmap for downstream
            // refinement segments to consume via _segmentResults. Immediate variants
            // composite onto the page using the region info's external comb-op.
            if (hdr.Type == SegmentType.IntermediateGenericRegion)
                _segmentResults[hdr.Number] = regionBmp;
            else
                ComposeOntoPage(regionBmp, (int)rsi.X, (int)rsi.Y, rsi.ExternalCombinationOperator);
        }

        // T.88 §7.4.2 Symbol dictionary segment.
        private void HandleSymbolDictionary(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (bodyLen < 10) throw new InvalidOperationException("Symbol dictionary segment too short");

            int p0 = bodyStart;
            int sdFlags = (_data[p0] << 8) | _data[p0 + 1];
            bool sdHuff = (sdFlags & 0x01) != 0;
            bool sdRefAgg = (sdFlags & 0x02) != 0;
            int sdHuffDh = (sdFlags >> 2) & 0x03;     // ignored when SdHuff = 0
            int sdHuffDw = (sdFlags >> 4) & 0x03;     // ignored when SdHuff = 0
            bool sdHuffBmsize = (sdFlags & 0x40) != 0;
            bool sdHuffAggInst = (sdFlags & 0x80) != 0;
            // T.88 §7.4.2.1.1: bit 8 = "use bitmap coding context" (seed arith
            // stats from a referred-to SD's retained context). Bit 9 =
            // "retain bitmap coding context" (preserve final stats so a later
            // SD can use them).
            bool sdUseRetainedCtx = (sdFlags & 0x100) != 0;
            bool sdRetainCtx      = (sdFlags & 0x200) != 0;
            int sdTemplate = (sdFlags >> 10) & 0x03;
            int sdRTemplate = (sdFlags >> 12) & 0x01;

            int o = bodyStart + 2;
            int sdatBytes = sdHuff ? 0 : sdTemplate == 0 ? 8 : 2;
            var sdat = new sbyte[8];
            for (var i = 0; i < sdatBytes; i++) sdat[i] = (sbyte)_data[o++];
            int sdratBytes = sdRefAgg && sdRTemplate == 0 ? 4 : 0;
            var sdrat = new sbyte[4];
            for (var i = 0; i < sdratBytes; i++) sdrat[i] = (sbyte)_data[o++];

            uint sdNumExSyms  = BigEndian.U32(_data, o); o += 4;
            uint sdNumNewSyms = BigEndian.U32(_data, o); o += 4;

            // Look up referred-to dictionaries and user-defined Huffman tables.
            // Per T.88 §7.4.2.1.6, the referred-to-segments list contains SDs in
            // order then any user-defined Huffman tables in selector order:
            // Dh, Dw, BmSize, AggInst.
            SymbolDictionary? inSyms = null;
            uint sdNumInSyms = 0;
            var userTableSegments = new List<Huffman.HuffmanParams>();
            // For "use bitmap coding context" (bit 8), seed stats are taken
            // from the most recent referred-to SD that has retained stats
            // (pdfium picks the last-referred-to SD that carries them).
            byte[]? seedGbStats = null;
            byte[]? seedGrStats = null;
            if (hdr.ReferredToSegments != null)
            {
                var dictList = new List<SymbolDictionary>();
                foreach (uint refSeg in hdr.ReferredToSegments)
                {
                    if (_segmentResults.TryGetValue(refSeg, out var refRes))
                    {
                        if (refRes is SymbolDictionary sd)
                        {
                            dictList.Add(sd);
                            sdNumInSyms += (uint)sd.Count;
                            if (sdUseRetainedCtx)
                            {
                                if (sd.RetainedGbStats != null) seedGbStats = sd.RetainedGbStats;
                                if (sd.RetainedGrStats != null) seedGrStats = sd.RetainedGrStats;
                            }
                        }
                        else if (refRes is Huffman.HuffmanParams hp)
                        {
                            userTableSegments.Add(hp);
                        }
                    }
                }
                if (dictList.Count > 0)
                {
                    var combined = new List<Bitmap>();
                    foreach (var d in dictList) combined.AddRange(d.Glyphs);
                    inSyms = new SymbolDictionary(combined.ToArray());
                }
            }

            // Slot user Huffman tables into selector positions (only when sdHuff).
            Huffman.HuffmanParams?[]? userTables = null;
            if (sdHuff && userTableSegments.Count > 0)
            {
                userTables = new Huffman.HuffmanParams?[4];
                var next = 0;
                int[] sels = {
                    sdHuffDh,                            // Dh
                    sdHuffDw,                            // Dw
                    sdHuffBmsize ? 1 : 0,                // BmSize (1-bit)
                    sdHuffAggInst ? 1 : 0,               // AggInst (1-bit; only meaningful when SdRefAgg)
                };
                for (var i = 0; i < 4; i++)
                {
                    bool isUser = i < 2 ? sels[i] == 3 : sels[i] == 1;
                    if (i == 3 && !sdRefAgg) continue;   // AggInst is only consumed in refagg path
                    if (!isUser) continue;
                    if (next >= userTableSegments.Count)
                        throw new InvalidOperationException(
                            $"SD selector slot {i} marked user-defined but no Huffman-table segment supplied");
                    userTables[i] = userTableSegments[next++];
                }
            }

            var p = new SymbolDictionaryParams
            {
                SdHuff = sdHuff,
                SdRefAgg = sdRefAgg,
                SdTemplate = sdTemplate,
                SdRTemplate = sdRTemplate,
                Sdat = sdat,
                Sdrat = sdrat,
                SdNumInSyms = sdNumInSyms,
                SdNumNewSyms = sdNumNewSyms,
                SdNumExSyms = sdNumExSyms,
                SdInSyms = inSyms,
                SdHuffFlags = (ushort)sdFlags,
                UserTables = userTables,
                UseRetainedContext = sdUseRetainedCtx,
                SeedGbStats = seedGbStats,
                SeedGrStats = seedGrStats,
                RetainContext = sdRetainCtx,
            };

            int arithStart = o;
            int arithLen = bodyStart + bodyLen - arithStart;
            var dict = new SymbolDictionaryDecoder().Decode(p, _data, arithStart, arithLen);
            _segmentResults[hdr.Number] = dict;
        }

        // T.88 §7.4.7 Generic refinement region segment.
        private void HandleRefinementRegion(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (Page is null)
                throw new InvalidOperationException("Refinement region encountered before any page information segment");
            if (bodyLen < 18)
                throw new InvalidOperationException("Refinement region body too short");

            RegionInfo rsi = RegionInfo.Parse(_data, bodyStart);
            byte segFlags = _data[bodyStart + 17];
            bool grTemplate = (segFlags & 0x01) != 0;
            bool tpgrOn     = (segFlags & 0x02) != 0;

            int o = bodyStart + 18;
            var grat = new sbyte[4];
            if (!grTemplate)
            {
                for (var i = 0; i < 4; i++) grat[i] = (sbyte)_data[o++];
            }

            // T.88 §7.4.7.4: pick the reference bitmap and the offset that maps
            // the refinement region's (0,0) into the reference's coordinate
            // space. Two cases:
            //   a) refinement refers to no segments — reference is the (full)
            //      page bitmap, offsets compensate for the refinement region's
            //      page position so the encoder's "identity refinement" reads
            //      the right page pixels;
            //   b) refinement refers to one segment — that segment's bitmap IS
            //      the reference, offsets are zero.
            // jbig2dec hardcodes (0,0) for both cases (with a TODO to subset),
            // which is why streams like jbig2-tests-pdf bitmap-refine-page-subrect
            // render scribble in the dot positions instead of the source wink.
            Bitmap? reference = null;
            var refDx = 0;
            var refDy = 0;
            if (hdr.ReferredToSegments != null)
            {
                foreach (uint refSeg in hdr.ReferredToSegments)
                {
                    if (_segmentResults.TryGetValue(refSeg, out var refRes) && refRes is Bitmap bmp)
                    {
                        reference = bmp;
                        break;
                    }
                }
            }
            if (reference is null)
            {
                reference = Page;
                refDx = -(int)rsi.X;
                refDy = -(int)rsi.Y;
            }

            int arithStart = o;
            int arithLen = bodyStart + bodyLen - arithStart;

            var regionBmp = new Bitmap((int)rsi.Width, (int)rsi.Height);
            var p = new RefinementRegionParams
            {
                GrTemplate = grTemplate ? 1 : 0,
                TpgrOn = tpgrOn,
                Reference = reference,
                ReferenceDx = refDx,
                ReferenceDy = refDy,
                Grat = grat,
            };
            var mq = new MqDecoder(_data, arithStart, arithLen);
            var stats = new byte[RefinementRegionDecoder.StatsSizeFor(p.GrTemplate)];
            new RefinementRegionDecoder().Decode(p, mq, stats, regionBmp);

            if (hdr.Type == SegmentType.IntermediateGenericRefinementRegion)
                _segmentResults[hdr.Number] = regionBmp;
            else
                ComposeOntoPage(regionBmp, (int)rsi.X, (int)rsi.Y, rsi.ExternalCombinationOperator);
        }

        // T.88 §7.4.4 Pattern dictionary segment.
        private void HandlePatternDictionary(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (bodyLen < 7) throw new InvalidOperationException("Pattern dictionary segment too short");

            byte flags = _data[bodyStart];
            var pdParams = new PatternDictionaryParams
            {
                HdMmr = (flags & 0x01) != 0,
                HdTemplate = (flags & 0x06) >> 1,
                HdPw = _data[bodyStart + 1],
                HdPh = _data[bodyStart + 2],
                GrayMax = BigEndian.U32(_data, bodyStart + 3),
            };

            int dataOffset = bodyStart + 7;
            int dataLen = bodyStart + bodyLen - dataOffset;
            var dict = new PatternDictionaryDecoder().Decode(pdParams, _data, dataOffset, dataLen);
            _segmentResults[hdr.Number] = dict;
        }

        // T.88 §7.4.5 Halftone region segment.
        private void HandleHalftoneRegion(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (Page is null)
                throw new InvalidOperationException("Halftone region encountered before any page information segment");
            if (bodyLen < 17 + 1 + 16 + 4)
                throw new InvalidOperationException("Halftone region body too short");

            RegionInfo rsi = RegionInfo.Parse(_data, bodyStart);
            int o = bodyStart + 17;

            byte hrFlags = _data[o++];
            bool hMmr        = (hrFlags & 0x01) != 0;
            int  hTemplate   = (hrFlags & 0x06) >> 1;
            bool hEnableSkip = (hrFlags & 0x08) != 0;
            int  hCombOp     = (hrFlags & 0x70) >> 4;
            int  hDefPixel   = (hrFlags & 0x80) >> 7;

            uint hgw = BigEndian.U32(_data, o); o += 4;
            uint hgh = BigEndian.U32(_data, o); o += 4;
            var  hgx = (int)BigEndian.U32(_data, o); o += 4;
            var  hgy = (int)BigEndian.U32(_data, o); o += 4;
            int  hrx = (short)BigEndian.U16(_data, o); o += 2;
            int  hry = (short)BigEndian.U16(_data, o); o += 2;

            // Resolve the pattern dictionary from referred-to segments (T.88 §6.6.5).
            PatternDictionary? hpats = null;
            if (hdr.ReferredToSegments != null)
            {
                foreach (uint refSeg in hdr.ReferredToSegments)
                {
                    if (_segmentResults.TryGetValue(refSeg, out var refRes) && refRes is PatternDictionary pd)
                    {
                        hpats = pd;
                        break;
                    }
                }
            }
            if (hpats is null)
                throw new InvalidOperationException("Halftone region has no referenced pattern dictionary");

            var hp = new HalftoneRegionParams
            {
                HMmr = hMmr,
                HTemplate = hTemplate,
                HEnableSkip = hEnableSkip,
                HCombOp = hCombOp,
                HDefPixel = hDefPixel,
                Hgw = hgw,
                Hgh = hgh,
                Hgx = hgx,
                Hgy = hgy,
                Hrx = hrx,
                Hry = hry,
                Patterns = hpats,
            };

            int dataOffset = o;
            int dataLen = bodyStart + bodyLen - dataOffset;

            var regionBmp = new Bitmap((int)rsi.Width, (int)rsi.Height);
            new HalftoneRegionDecoder().Decode(hp, _data, dataOffset, dataLen, regionBmp);

            if (hdr.Type == SegmentType.IntermediateHalftoneRegion)
                _segmentResults[hdr.Number] = regionBmp;
            else
                ComposeOntoPage(regionBmp, (int)rsi.X, (int)rsi.Y, rsi.ExternalCombinationOperator);
        }

        // T.88 §7.4.3 Text region segment.
        private void HandleTextRegion(SegmentHeader hdr, int bodyStart, int bodyLen)
        {
            if (Page is null)
                throw new InvalidOperationException("Text region encountered before any page information segment");
            if (bodyLen < 17 + 4)
                throw new InvalidOperationException("Text region body too short");

            RegionInfo rsi = RegionInfo.Parse(_data, bodyStart);
            int o = bodyStart + 17;
            int trFlags = BigEndian.U16(_data, o); o += 2;

            bool sbHuff      = (trFlags & 0x0001) != 0;
            bool sbRefine    = (trFlags & 0x0002) != 0;
            int  logSbStrips = (trFlags >> 2) & 0x03;
            int  refCorner   = (trFlags >> 4) & 0x03;
            bool transposed  = (trFlags & 0x0040) != 0;
            int  sbCombOp    = (trFlags >> 7) & 0x03;
            bool sbDefPixel  = (trFlags & 0x0200) != 0;
            int  sbDsOffsetRaw = (trFlags >> 10) & 0x1F;     // 5-bit signed
            int  sbDsOffset = sbDsOffsetRaw >= 16 ? sbDsOffsetRaw - 32 : sbDsOffsetRaw;
            bool sbRTemplate = (trFlags & 0x8000) != 0;

            // Huffman selectors word (T.88 §7.4.3.1.2) — only present when SbHuff.
            ushort sbHuffFlags = 0;
            if (sbHuff)
            {
                sbHuffFlags = (ushort)BigEndian.U16(_data, o);
                o += 2;
            }

            // Refinement AT pixels — only present when SbRefine && SbRTemplate==false.
            var sbrat = new sbyte[4];
            if (sbRefine && !sbRTemplate)
            {
                for (var i = 0; i < 4; i++) sbrat[i] = (sbyte)_data[o++];
            }

            uint sbNumInstances = BigEndian.U32(_data, o); o += 4;

            // Resolve referenced symbol dictionaries and any referred-to user
            // Huffman tables. T.88 §7.4.3.1.7: in the referred-to-segments list,
            // SDs come first (in order), then user Huffman tables in selector
            // order — Fs, Ds, Dt, Rdw, Rdh, Rdx, Rdy, Rsize. Tables that aren't
            // user-defined (their selector is 0/1/2 = standard) are simply absent
            // from the list, so we walk the user-table selector flags in parallel.
            var dicts = new List<SymbolDictionary>();
            var userTableSegments = new List<Huffman.HuffmanParams>();
            if (hdr.ReferredToSegments != null)
            {
                foreach (uint refSeg in hdr.ReferredToSegments)
                {
                    if (_segmentResults.TryGetValue(refSeg, out var refRes))
                    {
                        if (refRes is SymbolDictionary sd) dicts.Add(sd);
                        else if (refRes is Huffman.HuffmanParams hp) userTableSegments.Add(hp);
                    }
                }
            }
            if (dicts.Count == 0)
            {
                // T.88 §7.4.3 doesn't require text regions to reference any
                // symbol dictionary — encoders may emit a degenerate text
                // region with SBNUMINSTANCES=0 to test decoder robustness
                // (see jbig2-tests-pdf bitmap-symbol-empty.pdf). jbig2dec
                // logs a warning and skips the segment; we do the same so
                // the rest of the page (typically rendered by a following
                // generic region) is preserved.
                return;
            }

            // Slot user Huffman tables into their selector positions. Walking the
            // selector flags in spec order assigns the next un-resolved user table
            // to each user-defined slot.
            Huffman.HuffmanParams?[]? userTables = null;
            if (sbHuff && userTableSegments.Count > 0)
            {
                userTables = new Huffman.HuffmanParams?[8];
                var next = 0;
                int[] sels = {
                    (sbHuffFlags >>  0) & 3,    // Fs
                    (sbHuffFlags >>  2) & 3,    // Ds
                    (sbHuffFlags >>  4) & 3,    // Dt
                    (sbHuffFlags >>  6) & 3,    // Rdw
                    (sbHuffFlags >>  8) & 3,    // Rdh
                    (sbHuffFlags >> 10) & 3,    // Rdx
                    (sbHuffFlags >> 12) & 3,    // Rdy
                    (sbHuffFlags >> 14) & 1,    // Rsize (1-bit selector, user-defined = 1)
                };
                for (var i = 0; i < 8; i++)
                {
                    bool isUser = i == 7 ? sels[i] == 1 : sels[i] == 3;
                    if (!isUser) continue;
                    if (next >= userTableSegments.Count)
                        throw new InvalidOperationException(
                            $"Text region selector slot {i} marked user-defined but no Huffman-table segment supplied");
                    userTables[i] = userTableSegments[next++];
                }
            }

            int sbStrips = 1 << logSbStrips;
            var p = new TextRegionParams
            {
                SbHuff = sbHuff,
                SbRefine = sbRefine,
                SbDefPixel = sbDefPixel,
                SbCombOp = sbCombOp,
                Transposed = transposed,
                RefCorner = (RefCorner)refCorner,
                SbDsOffset = sbDsOffset,
                SbNumInstances = sbNumInstances,
                LogSbStrips = logSbStrips,
                SbStrips = sbStrips,
                SbRTemplate = sbRTemplate,
                Sbrat = sbrat,
                Dicts = dicts.ToArray(),
                SbHuffFlags = sbHuffFlags,
                UserTables = userTables,
            };

            int arithStart = o;
            int arithLen = bodyStart + bodyLen - arithStart;

            var regionBmp = new Bitmap((int)rsi.Width, (int)rsi.Height);
            new TextRegionDecoder().Decode(p, _data, arithStart, arithLen, regionBmp);

            if (hdr.Type == SegmentType.IntermediateTextRegion)
                _segmentResults[hdr.Number] = regionBmp;
            else
                ComposeOntoPage(regionBmp, (int)rsi.X, (int)rsi.Y, rsi.ExternalCombinationOperator);
        }

        /// <summary>
        /// Composite a region bitmap onto the page bitmap at (x, y) using the
        /// specified combination operator (T.88 §7.4 Table 5: 0=OR, 1=AND, 2=XOR,
        /// 3=XNOR, 4=REPLACE).
        /// </summary>
        private void ComposeOntoPage(Bitmap region, int x, int y, int op)
        {
            if (Page is null) throw new InvalidOperationException();

            int w = region.Width;
            int h = region.Height;

            if (_isStriped)
            {
                EnsurePageHeight(y + h);
                if (y + h > _actualHeight) _actualHeight = y + h;
            }

            for (var ry = 0; ry < h; ry++)
            {
                int dy = y + ry;
                if ((uint)dy >= (uint)Page.Height) continue;

                for (var rx = 0; rx < w; rx++)
                {
                    int dx = x + rx;
                    if ((uint)dx >= (uint)Page.Width) continue;

                    int srcBit = region.GetPixel(rx, ry);
                    int dstBitIdx = dy * Page.Stride + (dx >> 3);
                    int dstShift = 7 - (dx & 7);
                    int dstBit = (Page.Data[dstBitIdx] >> dstShift) & 1;

                    int newBit = op switch
                    {
                        0 => srcBit | dstBit,        // OR
                        1 => srcBit & dstBit,        // AND
                        2 => srcBit ^ dstBit,        // XOR
                        3 => 1 - (srcBit ^ dstBit),  // XNOR
                        4 => srcBit,                  // REPLACE
                        _ => srcBit | dstBit,
                    };

                    if (newBit == 1)
                        Page.Data[dstBitIdx] = (byte)(Page.Data[dstBitIdx] | (1 << dstShift));
                    else
                        Page.Data[dstBitIdx] = (byte)(Page.Data[dstBitIdx] & ~(1 << dstShift));
                }
            }
        }
    }
}
